using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.Scripting;
using Meziantou.Framework.WebPlatform.Tests.Scripting;
using Microsoft.Extensions.Time.Testing;

namespace Meziantou.Framework.WebPlatform.Tests.Dom;

/// <summary>Client Hints (https://wicg.github.io/client-hints-infrastructure/, https://wicg.github.io/ua-client-hints/).</summary>
public sealed class ClientHintsTests : IDisposable
{
    private const string PageUrl = "https://example.com/page.html";

    private static readonly ClientHintsOptions Values = new()
    {
        Brands = [new() { Brand = "Test \"Browser\"", Version = "12" }, new() { Brand = "Engine", Version = "3" }],
        FullVersionList = [new() { Brand = "Engine", Version = "3.1.4" }],
        FullVersion = "12.0.1",
        Platform = "Linux",
        PlatformVersion = "6.1.0",
        Architecture = "arm",
        Bitness = "64",
        Model = "Model X",
        FormFactors = ["Tablet", "Desktop"],
        DeviceMemory = 6,
        RoundTripTime = TimeSpan.FromMilliseconds(130),
        Downlink = 1.23,
        EffectiveConnectionType = "3g",
    };

    private readonly BrowsingContextScope _contexts = new();
    private readonly ScriptEngine _engine = TestScriptEngines.Create();
    private readonly Dictionary<string, DocumentResource> _resources = new(StringComparer.Ordinal);
    private readonly List<DocumentRequest> _requests = [];
    private Action? _onRequest;

    public void Dispose()
    {
        _contexts.Dispose();
        TestScriptEngines.Dispose(_engine);
    }

    private void AddPage(string url, string html, params KeyValuePair<string, string>[] headers)
        => _resources[url] = new DocumentResource(html) { Headers = [new("Content-Type", "text/html"), .. headers] };

    private BrowsingContext CreateContext(BrowsingContextOptions? options = null)
    {
        options ??= new BrowsingContextOptions { ClientHints = Values };
        return _contexts.Create(new BrowsingContextOptions
        {
            DocumentLoader = new Loader(this),
            ScriptEngine = options.ScriptEngine,
            ClientHints = options.ClientHints,
            AcceptCHCache = options.AcceptCHCache,
            TimeProvider = options.TimeProvider,
            Features = options.Features,
            Viewport = options.Viewport,
            UserAgent = options.UserAgent,
        });
    }

    private Document Load(BrowsingContext context, string url = PageUrl)
    {
        context.Navigate(url);
        context.EventLoop.RunUntilIdle();
        return context.Document;
    }

    private Document Load(string html, params KeyValuePair<string, string>[] headers)
    {
        AddPage(PageUrl, html, headers);
        return Load(CreateContext());
    }

    /// <summary>The request of a URL: the last one, or the one at an index.</summary>
    private DocumentRequest Request(string url, int? index = null)
    {
        var requests = _requests.Where(request => request.Url == url).ToList();
        Assert.NotEmpty(requests);
        return index is { } i ? requests[i] : requests[^1];
    }

    private static string? Header(DocumentRequest request, string name)
        => request.Headers.FirstOrDefault(header => header.Key.Equals(name, StringComparison.OrdinalIgnoreCase)).Value;

    private static string[] ClientHintNames(DocumentRequest request)
        => [.. request.Headers.Select(header => header.Key).Where(name => name.StartsWith("Sec-CH-", StringComparison.OrdinalIgnoreCase) || name is "Save-Data" or "DPR" or "Width" or "Viewport-Width" or "Device-Memory" or "RTT" or "Downlink" or "ECT")];

    [Fact]
    public void LowEntropyHints_AreSentToTheSecureOrigins()
    {
        AddPage("http://example.com/insecure.html", "<img src='https://example.com/b.png'><img src='http://example.com/c.png'>");
        Load("<img src='a.png'><img src='https://other.example/a.png'>");

        // The navigation and the subresources, also the cross-origin ones: the default allowlist of the features is *
        string[] expected = ["Sec-CH-UA", "Sec-CH-UA-Mobile", "Sec-CH-UA-Platform"];
        Assert.Equal(expected, ClientHintNames(Request(PageUrl)));
        Assert.Equal(expected, ClientHintNames(Request("https://example.com/a.png")));
        Assert.Equal(expected, ClientHintNames(Request("https://other.example/a.png")));
        Assert.Equal("\"Test \\\"Browser\\\"\";v=\"12\", \"Engine\";v=\"3\"", Header(Request(PageUrl), "Sec-CH-UA"));
        Assert.Equal("?0", Header(Request(PageUrl), "Sec-CH-UA-Mobile"));
        Assert.Equal("\"Linux\"", Header(Request(PageUrl), "Sec-CH-UA-Platform"));

        // No hint is sent over an insecure transport
        Load(CreateContext(), "http://example.com/insecure.html");
        Assert.Empty(ClientHintNames(Request("http://example.com/insecure.html")));
        Assert.Empty(ClientHintNames(Request("http://example.com/c.png")));
        Assert.Equal(expected, ClientHintNames(Request("https://example.com/b.png")));
    }

    [Fact]
    public void DefaultValues_DescribeTheLibrary()
    {
        AddPage(PageUrl, "", new KeyValuePair<string, string>("Accept-CH", "Sec-CH-UA-Arch, Sec-CH-UA-Platform-Version, Sec-CH-Device-Memory, RTT, Downlink, ECT, Save-Data, Sec-CH-UA-Form-Factors"));
        var context = CreateContext(new BrowsingContextOptions());
        Load(context);
        Load(context);

        var request = Request(PageUrl);
        Assert.StartsWith("\"Meziantou.Framework.WebPlatform\";v=\"", Header(request, "Sec-CH-UA"), StringComparison.Ordinal);
        Assert.Equal("\"Unknown\"", Header(request, "Sec-CH-UA-Platform"));
        Assert.Equal("\"\"", Header(request, "Sec-CH-UA-Arch"));
        Assert.Equal("\"\"", Header(request, "Sec-CH-UA-Platform-Version"));
        Assert.Equal("\"Desktop\"", Header(request, "Sec-CH-UA-Form-Factors"));

        // The hints without value are omitted
        Assert.Equal(["Sec-CH-UA", "Sec-CH-UA-Mobile", "Sec-CH-UA-Platform", "Sec-CH-UA-Arch", "Sec-CH-UA-Form-Factors", "Sec-CH-UA-Platform-Version"], ClientHintNames(request));
    }

    [Fact]
    public void AcceptCH_OptsIntoTheHintsOfTheNextRequestsOfTheOrigin()
    {
        var cache = new AcceptCHCache();
        AddPage(PageUrl, "<img src='a.png'><img src='https://other.example/a.png'><iframe src='frame.html'></iframe><iframe src='https://other.example/frame.html'></iframe>",
            new KeyValuePair<string, string>("Accept-CH", "Sec-CH-UA-Arch, sec-ch-ua-bitness, Sec-CH-UA-Model, Sec-CH-UA-Full-Version-List, Sec-CH-UA-Full-Version, Sec-CH-UA-WoW64, Sec-CH-UA-Form-Factors"),
            new KeyValuePair<string, string>("Accept-CH", "Sec-CH-Device-Memory, Device-Memory, RTT, Downlink, ECT, Unknown-Hint, \"sec-ch-dpr\""));
        AddPage("https://example.com/frame.html", "");
        AddPage("https://other.example/frame.html", "");
        var context = CreateContext(new BrowsingContextOptions { ClientHints = Values, AcceptCHCache = cache });
        Load(context);

        // The request of the first navigation only has the low-entropy hints; the response opts the origin in
        Assert.Equal(3, ClientHintNames(Request(PageUrl)).Length);
        Assert.Equal(
            ["sec-ch-ua-arch", "sec-ch-ua-bitness", "sec-ch-ua-form-factors", "sec-ch-ua-full-version", "sec-ch-ua-full-version-list", "sec-ch-ua-model", "sec-ch-ua-wow64", "sec-ch-device-memory", "device-memory", "rtt", "downlink", "ect"],
            cache.GetClientHints("https://example.com"));
        Assert.Equal(["https://example.com"], cache.GetOrigins());

        // The subresources and the iframes of the origin get the hints; the other origins only get the low-entropy hints
        foreach (var url in new[] { "https://example.com/a.png", "https://example.com/frame.html" })
        {
            var request = Request(url);
            Assert.Equal("\"arm\"", Header(request, "Sec-CH-UA-Arch"));
            Assert.Equal("\"64\"", Header(request, "Sec-CH-UA-Bitness"));
            Assert.Equal("\"Model X\"", Header(request, "Sec-CH-UA-Model"));
            Assert.Equal("\"Engine\";v=\"3.1.4\"", Header(request, "Sec-CH-UA-Full-Version-List"));
            Assert.Equal("\"12.0.1\"", Header(request, "Sec-CH-UA-Full-Version"));
            Assert.Equal("?0", Header(request, "Sec-CH-UA-WoW64"));
            Assert.Equal("\"Desktop\", \"Tablet\"", Header(request, "Sec-CH-UA-Form-Factors"));

            // The device memory is a power of two, the round-trip time a multiple of 50 ms, the downlink a multiple of 50 kbps
            Assert.Equal("4", Header(request, "Sec-CH-Device-Memory"));
            Assert.Equal("4", Header(request, "Device-Memory"));
            Assert.Equal("150", Header(request, "RTT"));
            Assert.Equal("1.25", Header(request, "Downlink"));
            Assert.Equal("3g", Header(request, "ECT"));
            Assert.Null(Header(request, "Sec-CH-DPR"));
        }

        Assert.Equal(3, ClientHintNames(Request("https://other.example/a.png")).Length);
        Assert.Equal(3, ClientHintNames(Request("https://other.example/frame.html")).Length);

        // The next navigation to the origin has the hints, also in another browsing context that shares the cache
        Load(CreateContext(new BrowsingContextOptions { ClientHints = Values, AcceptCHCache = cache }));
        Assert.Equal("\"arm\"", Header(Request(PageUrl), "Sec-CH-UA-Arch"));

        // A browsing context has its own cache by default
        Load(CreateContext());
        Assert.Null(Header(Request(PageUrl), "Sec-CH-UA-Arch"));
    }

    [Fact]
    public void AcceptCH_OfAnIframeOrASubresource_IsIgnored()
    {
        var cache = new AcceptCHCache();
        AddPage(PageUrl, "<iframe src='https://frame.example/frame.html'></iframe><img src='https://image.example/a.png'>");
        AddPage("https://frame.example/frame.html", "", new KeyValuePair<string, string>("Accept-CH", "Sec-CH-UA-Arch"));
        _resources["https://image.example/a.png"] = new DocumentResource("") { Headers = [new("Accept-CH", "Sec-CH-UA-Arch")] };
        Load(CreateContext(new BrowsingContextOptions { ClientHints = Values, AcceptCHCache = cache }));

        Assert.Empty(cache.GetOrigins());
    }

    [Fact]
    public void AcceptCH_EmptyOrMalformed_OptsOut()
    {
        var cache = new AcceptCHCache();
        cache.SetClientHints("https://example.com", ["Sec-CH-UA-Arch", "not-a-hint"]);
        cache.SetClientHints("https://other.example", ["sec-ch-ua-model"]);
        Assert.Equal(["sec-ch-ua-arch"], cache.GetClientHints("https://example.com"));

        // A response without header keeps the entry
        var context = CreateContext(new BrowsingContextOptions { ClientHints = Values, AcceptCHCache = cache });
        AddPage(PageUrl, "");
        Load(context);
        Assert.Equal("\"arm\"", Header(Request(PageUrl), "Sec-CH-UA-Arch"));
        Assert.Equal(["sec-ch-ua-arch"], cache.GetClientHints("https://example.com"));

        // A malformed header (not a structured field list) and an empty header empty the entry
        AddPage(PageUrl, "", new KeyValuePair<string, string>("Accept-CH", "sec-ch-ua-arch sec-ch-ua-model"));
        Load(context);
        Assert.Empty(cache.GetClientHints("https://example.com"));
        Assert.Equal(["https://example.com", "https://other.example"], cache.GetOrigins().Order(StringComparer.Ordinal));

        cache.SetClientHints("https://example.com", ["sec-ch-ua-arch"]);
        AddPage(PageUrl, "", new KeyValuePair<string, string>("Accept-CH", ""));
        Load(context);
        Assert.Empty(cache.GetClientHints("https://example.com"));

        Assert.True(cache.Remove("https://other.example"));
        Assert.False(cache.Remove("https://other.example"));
        cache.Clear();
        Assert.Empty(cache.GetOrigins());
    }

    [Theory]
    [InlineData("", "")]
    [InlineData("Sec-CH-UA-Arch", "sec-ch-ua-arch")]
    [InlineData("sec-ch-ua-arch,sec-ch-ua-model", "sec-ch-ua-arch,sec-ch-ua-model")]
    [InlineData("  sec-ch-ua-arch ,\tSec-CH-UA-Model;q=1 ", "sec-ch-ua-arch,sec-ch-ua-model")]
    [InlineData("sec-ch-ua-arch, \"sec-ch-ua-model, x\", (sec-ch-ua-wow64), 12, unknown", "sec-ch-ua-arch")]
    [InlineData("sec-ch-ua-arch sec-ch-ua-model", null)]
    [InlineData("sec-ch-ua-arch,", null)]
    [InlineData(", sec-ch-ua-arch", null)]
    [InlineData("sec-ch-ua-arch, \"unterminated", null)]
    public void TokenList_IsAStructuredFieldList(string value, string? expected)
    {
        Assert.Equal(expected, ClientHints.ParseTokenList(value) is { } hints ? string.Join(',', hints.ToNames()) : null);
    }

    [Fact]
    public void PermissionsPolicy_DelegatesTheHintsToTheOtherOrigins()
    {
        AddPage(PageUrl, """
            <img src='a.png'><img src='https://cdn.example/a.png'><img src='https://other.example/a.png'>
            <iframe src='https://cdn.example/frame.html'></iframe>
            <iframe src='https://other.example/frame.html' allow='ch-ua-model'></iframe>
            <iframe src='https://other.example/sandboxed.html' sandbox></iframe>
            <iframe src='sandboxed.html' sandbox></iframe>
            """,
            new KeyValuePair<string, string>("Accept-CH", "Sec-CH-UA-Arch, Sec-CH-UA-Model, Sec-CH-UA-Bitness"),
            new KeyValuePair<string, string>("Permissions-Policy", "ch-ua-arch=(self \"https://cdn.example\"), ch-ua-bitness=(), ch-ua-platform=()"));
        AddPage("https://cdn.example/frame.html", "<img src='https://example.com/from-frame.png'><img src='b.png'>");
        AddPage("https://other.example/frame.html", "<img src='c.png'>");
        var context = CreateContext();
        Load(context);
        _requests.Clear();
        Load(context);

        // The document: its own origin, but not the features that its policy disables
        Assert.Equal(["Sec-CH-UA", "Sec-CH-UA-Mobile", "Sec-CH-UA-Arch", "Sec-CH-UA-Model"], ClientHintNames(Request("https://example.com/a.png")));

        // The origin that the policy delegates a hint to, for the subresources and the navigation of an iframe
        Assert.Equal(["Sec-CH-UA", "Sec-CH-UA-Mobile", "Sec-CH-UA-Arch"], ClientHintNames(Request("https://cdn.example/a.png")));
        Assert.Equal(["Sec-CH-UA", "Sec-CH-UA-Mobile", "Sec-CH-UA-Arch"], ClientHintNames(Request("https://cdn.example/frame.html")));
        Assert.Equal(["Sec-CH-UA", "Sec-CH-UA-Mobile"], ClientHintNames(Request("https://other.example/a.png")));

        // The allow attribute delegates a hint to the document of the iframe
        Assert.Equal(["Sec-CH-UA", "Sec-CH-UA-Mobile", "Sec-CH-UA-Model"], ClientHintNames(Request("https://other.example/frame.html")));
        Assert.Equal(["Sec-CH-UA", "Sec-CH-UA-Mobile", "Sec-CH-UA-Model"], ClientHintNames(Request("https://other.example/c.png")));

        // The document of a sandboxed iframe has an opaque origin: only the hints whose allowlist is * are delegated to it
        Assert.Equal(["Sec-CH-UA", "Sec-CH-UA-Mobile"], ClientHintNames(Request("https://other.example/sandboxed.html")));
        Assert.Equal(["Sec-CH-UA", "Sec-CH-UA-Mobile"], ClientHintNames(Request("https://example.com/sandboxed.html")));

        // The requests of a nested document: the hints of its top-level document that its own policy allows. The header
        // of the parent is not enough to enable a feature in a cross-origin iframe: the allow attribute is needed.
        Assert.Equal(["Sec-CH-UA", "Sec-CH-UA-Mobile"], ClientHintNames(Request("https://cdn.example/b.png")));
        Assert.Equal(["Sec-CH-UA", "Sec-CH-UA-Mobile"], ClientHintNames(Request("https://example.com/from-frame.png")));
    }

    [Fact]
    public void Redirect_HasTheHintsOfTheOriginOfItsUrl()
    {
        AddPage(PageUrl, "<img src='redirect-image'>", new KeyValuePair<string, string>("Accept-CH", "Sec-CH-UA-Arch"));
        _resources["https://example.com/redirect-image"] = new DocumentResource("") { StatusCode = 302, Headers = [new("Location", "https://other.example/a.png")] };
        _resources["https://example.com/redirect"] = new DocumentResource("") { StatusCode = 302, Headers = [new("Location", "https://other.example/page.html")] };
        _resources["https://other.example/redirect"] = new DocumentResource("") { StatusCode = 302, Headers = [new("Location", PageUrl), new("Accept-CH", "Sec-CH-UA-Model")] };
        AddPage("https://other.example/page.html", "");
        var context = CreateContext();
        Load(context);
        _requests.Clear();
        Load(context);

        // A subresource redirected to another origin loses the hints that are not delegated to it
        Assert.NotNull(Header(Request("https://example.com/redirect-image"), "Sec-CH-UA-Arch"));
        Assert.Null(Header(Request("https://other.example/a.png"), "Sec-CH-UA-Arch"));

        // A navigation redirected to another origin has the hints of that origin
        Load(context, "https://example.com/redirect");
        Assert.NotNull(Header(Request("https://example.com/redirect"), "Sec-CH-UA-Arch"));
        Assert.Null(Header(Request("https://other.example/page.html"), "Sec-CH-UA-Arch"));

        // The Accept-CH header of a redirect of a top-level navigation opts its origin in
        _requests.Clear();
        Load(context, "https://other.example/redirect");
        Assert.Null(Header(Request("https://other.example/redirect"), "Sec-CH-UA-Model"));
        Assert.NotNull(Header(Request(PageUrl), "Sec-CH-UA-Arch"));
        Load(context, "https://other.example/page.html");
        Assert.Equal("\"Model X\"", Header(Request("https://other.example/page.html"), "Sec-CH-UA-Model"));
    }

    [Fact]
    public void CriticalCH_RestartsTheNavigation()
    {
        AddPage(PageUrl, "",
            new KeyValuePair<string, string>("Accept-CH", "Sec-CH-UA-Arch, Sec-CH-UA-Model"),
            new KeyValuePair<string, string>("Critical-CH", "Sec-CH-UA-Model"));
        // The loader takes 10 ms to answer
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        _onRequest = () => clock.Advance(TimeSpan.FromMilliseconds(10));
        var context = CreateContext(new BrowsingContextOptions { ClientHints = Values, ScriptEngine = _engine, TimeProvider = clock });
        var document = Load(context);

        // The first request did not have the critical hint: the navigation is restarted, and the second request has it
        Assert.Equal(2, _requests.Count);
        Assert.Null(Header(Request(PageUrl, 0), "Sec-CH-UA-Model"));
        Assert.Equal("\"Model X\"", Header(Request(PageUrl, 1), "Sec-CH-UA-Model"));

        var timing = Assert.IsType<PerformanceNavigationTiming>(document.DefaultView!.Performance.GetEntriesByType("navigation").Single());
        Assert.Equal(10, timing.CriticalCHRestart);
        Assert.Equal(10, timing.FetchStart);
        Assert.Equal(20, timing.ResponseStart);
        Assert.Equal(0, timing.RedirectCount);

        // The hints are in the cache: the next navigation is not restarted
        _requests.Clear();
        document = Load(context);
        Assert.Single(_requests);
        timing = Assert.IsType<PerformanceNavigationTiming>(document.DefaultView!.Performance.GetEntriesByType("navigation").Single());
        Assert.Equal(0, timing.CriticalCHRestart);
    }

    [Fact]
    public void CriticalCH_ThatIsNotAccepted_DoesNotRestartTheNavigation()
    {
        // A critical hint that the response does not opt into would not be sent by a restart
        AddPage(PageUrl, "",
            new KeyValuePair<string, string>("Accept-CH", "Sec-CH-UA-Arch"),
            new KeyValuePair<string, string>("Critical-CH", "Sec-CH-UA-Model"));
        AddPage("https://example.com/frame.html", "",
            new KeyValuePair<string, string>("Accept-CH", "Sec-CH-UA-Model"),
            new KeyValuePair<string, string>("Critical-CH", "Sec-CH-UA-Model"));
        AddPage("https://example.com/parent.html", "<iframe src='frame.html'></iframe>");
        var context = CreateContext();
        Load(context);
        Assert.Single(_requests);

        // The navigation of an iframe is not restarted
        Load(context, "https://example.com/parent.html");
        Assert.Single(_requests, request => request.Url == "https://example.com/frame.html");
    }

    [Fact]
    public void ClearSiteData_ClearsTheHintsOfTheOrigin()
    {
        var cache = new AcceptCHCache();
        var context = CreateContext(new BrowsingContextOptions { ClientHints = Values, AcceptCHCache = cache });
        foreach (var (type, cleared) in new[] { ("\"clientHints\"", true), ("\"cookies\"", true), ("\"cache\"", true), ("\"*\"", true), ("\"storage\", \"clientHints\"", true), ("\"storage\"", false), ("clientHints", false) })
        {
            cache.SetClientHints("https://example.com", ["sec-ch-ua-arch"]);
            AddPage(PageUrl, "", new KeyValuePair<string, string>("Clear-Site-Data", type));
            Load(context);
            Assert.Equal(cleared, cache.GetClientHints("https://example.com").Count == 0);
        }

        // The header clears the hints that the response opts into, and the response of a subresource clears them too
        AddPage(PageUrl, "", new KeyValuePair<string, string>("Clear-Site-Data", "\"clientHints\""), new KeyValuePair<string, string>("Accept-CH", "sec-ch-ua-model"));
        Load(context);
        Assert.Empty(cache.GetOrigins());

        AddPage(PageUrl, "<img src='clear.png'>", new KeyValuePair<string, string>("Accept-CH", "sec-ch-ua-model"));
        _resources["https://example.com/clear.png"] = new DocumentResource("") { Headers = [new("Clear-Site-Data", "\"clientHints\"")] };
        Load(context);
        Assert.Empty(cache.GetOrigins());

        // The cache is partitioned: a response in a document of another origin does not clear the hints of its origin
        AddPage("https://other.example/page.html", "<img src='https://example.com/clear.png'><iframe src='https://example.com/clear.html'></iframe>");
        AddPage("https://example.com/clear.html", "", new KeyValuePair<string, string>("Clear-Site-Data", "\"*\""));
        cache.SetClientHints("https://example.com", ["sec-ch-ua-arch"]);
        Load(context, "https://other.example/page.html");
        Assert.Equal(["sec-ch-ua-arch"], cache.GetClientHints("https://example.com"));
    }

    [Fact]
    public void DelegateCH_AddsHintsAndDelegatesThem()
    {
        var cache = new AcceptCHCache();
        AddPage(PageUrl, """
            <!DOCTYPE html>
            <meta http-equiv="Delegate-CH" content="Sec-CH-UA-Arch https://cdn.example https://static.example/path; sec-ch-ua-model; not-a-hint https://cdn.example">
            <meta http-equiv="delegate-ch" content="sec-ch-ua-bitness https://cdn.example">
            <meta http-equiv="Accept-CH" content="Sec-CH-UA-WoW64, sec-ch-ua-full-version">
            <img src='a.png'><img src='https://cdn.example/a.png'><img src='https://static.example/a.png'><img src='https://other.example/a.png'>
            <script></script>
            <meta http-equiv="Delegate-CH" content="sec-ch-ua-platform-version">
            <img src='b.png'>
            """);
        var document = Load(CreateContext(new BrowsingContextOptions { ClientHints = Values, AcceptCHCache = cache }));

        // The hints are not in the Accept-CH cache
        Assert.Empty(cache.GetOrigins());
        Assert.Equal(["sec-ch-ua-arch", "sec-ch-ua-bitness", "sec-ch-ua-full-version", "sec-ch-ua-model", "sec-ch-ua-wow64"], document.ClientHintsSet.ToNames());

        string[] lowEntropy = ["Sec-CH-UA", "Sec-CH-UA-Mobile", "Sec-CH-UA-Platform"];
        Assert.Equal([.. lowEntropy, "Sec-CH-UA-Arch", "Sec-CH-UA-Bitness", "Sec-CH-UA-Full-Version", "Sec-CH-UA-Model", "Sec-CH-UA-WoW64"], ClientHintNames(Request("https://example.com/a.png")));
        Assert.Equal([.. lowEntropy, "Sec-CH-UA-Arch", "Sec-CH-UA-Bitness"], ClientHintNames(Request("https://cdn.example/a.png")));
        Assert.Equal([.. lowEntropy, "Sec-CH-UA-Arch"], ClientHintNames(Request("https://static.example/a.png")));
        Assert.Equal(lowEntropy, ClientHintNames(Request("https://other.example/a.png")));

        // The element that follows a script element is ignored
        Assert.Null(Header(Request("https://example.com/b.png"), "Sec-CH-UA-Platform-Version"));
    }

    [Fact]
    public void DelegateCH_OfANestedDocument_IsIgnored()
    {
        AddPage(PageUrl, "<iframe src='frame.html'></iframe>");
        AddPage("https://example.com/frame.html", "<meta http-equiv='Delegate-CH' content='sec-ch-ua-arch'><img src='a.png'>");
        var document = Load(CreateContext());

        Assert.Empty(document.QuerySelector("iframe") is HtmlIFrameElement iframe ? iframe.ContentNavigable!.Document.ClientHintsSet.ToNames() : ["no iframe"]);
        Assert.Null(Header(Request("https://example.com/a.png"), "Sec-CH-UA-Arch"));
    }

    [Fact]
    public void ViewportHints_AreTheValuesOfTheViewport()
    {
        AddPage(PageUrl, "<iframe src='frame.html' width='300' height='200' style='border: 0'></iframe><img src='a.png' sizes='25vw'><img src='b.png'>",
            new KeyValuePair<string, string>("Accept-CH", "Sec-CH-DPR, DPR, Sec-CH-Viewport-Width, Viewport-Width, Sec-CH-Viewport-Height, Sec-CH-Width, Width, Sec-CH-Prefers-Color-Scheme, Sec-CH-Prefers-Reduced-Motion, Sec-CH-Prefers-Reduced-Transparency"));
        AddPage("https://example.com/frame.html", "<img src='c.png'>");
        var context = CreateContext(new BrowsingContextOptions
        {
            ClientHints = Values,
            Viewport = new ViewportOptions { Width = 1001, Height = 700, DevicePixelRatio = 1.5, PreferredColorScheme = PreferredColorScheme.Dark, PrefersReducedMotion = true },
        });
        Load(context);
        _requests.Clear();
        Load(context);

        var request = Request(PageUrl);
        Assert.Equal("1.5", Header(request, "Sec-CH-DPR"));
        Assert.Equal("1.5", Header(request, "DPR"));
        Assert.Equal("1001", Header(request, "Sec-CH-Viewport-Width"));
        Assert.Equal("1001", Header(request, "Viewport-Width"));
        Assert.Equal("700", Header(request, "Sec-CH-Viewport-Height"));
        Assert.Equal("\"dark\"", Header(request, "Sec-CH-Prefers-Color-Scheme"));
        Assert.Equal("\"reduce\"", Header(request, "Sec-CH-Prefers-Reduced-Motion"));
        Assert.Equal("\"no-preference\"", Header(request, "Sec-CH-Prefers-Reduced-Transparency"));
        Assert.Null(Header(request, "Sec-CH-Width"));

        // The width of an image that has a sizes attribute, in device pixels: 25vw of 1001px at 1.5x
        Assert.Equal("376", Header(Request("https://example.com/a.png"), "Sec-CH-Width"));
        Assert.Equal("376", Header(Request("https://example.com/a.png"), "Width"));
        Assert.Null(Header(Request("https://example.com/b.png"), "Sec-CH-Width"));

        // The navigation of an iframe has the viewport of the top-level document; its requests have its own viewport
        Assert.Equal("1001", Header(Request("https://example.com/frame.html"), "Sec-CH-Viewport-Width"));
        Assert.Equal("300", Header(Request("https://example.com/c.png"), "Sec-CH-Viewport-Width"));
        Assert.Equal("200", Header(Request("https://example.com/c.png"), "Sec-CH-Viewport-Height"));
    }

    [Fact]
    public void SaveData_IsSentWithoutOptIn()
    {
        AddPage(PageUrl, "<img src='https://other.example/a.png'>");
        Load(CreateContext(new BrowsingContextOptions { ClientHints = Values with { SaveData = true, Mobile = true } }));

        Assert.Equal("on", Header(Request(PageUrl), "Save-Data"));
        Assert.Equal("on", Header(Request("https://other.example/a.png"), "Save-Data"));
        Assert.Equal("?1", Header(Request(PageUrl), "Sec-CH-UA-Mobile"));
    }

    [Fact]
    public void DisabledFeature_SendsNoHint()
    {
        var cache = new AcceptCHCache();
        cache.SetClientHints("https://example.com", ["sec-ch-ua-arch"]);
        AddPage(PageUrl, "<meta http-equiv='Delegate-CH' content='sec-ch-ua-model'><img src='a.png'>", new KeyValuePair<string, string>("Accept-CH", "Sec-CH-UA-Bitness"));
        var document = Load(CreateContext(new BrowsingContextOptions { ClientHints = Values, AcceptCHCache = cache, Features = new WebPlatformFeatures { ClientHintsEnabled = false } }));

        Assert.All(_requests, request => Assert.Empty(ClientHintNames(request)));
        Assert.Equal(["sec-ch-ua-arch"], cache.GetClientHints("https://example.com"));
        Assert.True(document.ClientHintsSet.IsEmpty);
    }

    [Fact]
    public void UserAgentData_ExposesTheValuesToTheScripts()
    {
        AddPage(PageUrl, """
            <iframe src='https://other.example/frame.html' allow="ch-ua-high-entropy-values 'none'"></iframe>
            <script>
            const data = navigator.userAgentData;
            window.result = '';
            data.getHighEntropyValues(['architecture', 'bitness', 'formFactors', 'fullVersionList', 'model', 'platformVersion', 'uaFullVersion', 'wow64', 'unknown'])
                .then(values => window.result = JSON.stringify([data.brands, data.mobile, data.platform, data.toJSON(), values, Object.isFrozen(data.brands)]));
            </script>
            """);
        AddPage("https://other.example/frame.html", "<script>navigator.userAgentData.getHighEntropyValues(['architecture']).then(values => window.result = JSON.stringify(values));</script>");
        AddPage("http://example.com/insecure.html", "<script>window.result = typeof navigator.userAgentData + typeof NavigatorUAData;</script>");
        var context = CreateContext(new BrowsingContextOptions { ClientHints = Values, ScriptEngine = _engine });
        var document = Load(context);

        var brands = """[{"brand":"Test \"Browser\"","version":"12"},{"brand":"Engine","version":"3"}]""";
        Assert.Equal(
            $$"""[{{brands}},false,"Linux",{"brands":{{brands}},"mobile":false,"platform":"Linux"},{"architecture":"arm","bitness":"64","brands":{{brands}},"formFactors":["Tablet","Desktop"],"fullVersionList":[{"brand":"Engine","version":"3.1.4"}],"mobile":false,"model":"Model X","platform":"Linux","platformVersion":"6.1.0","uaFullVersion":"12.0.1","wow64":false},true]""",
            TestScriptEngines.Evaluate(_engine, document.DefaultView!, "window.result"));

        // A document that is not allowed to use ch-ua-high-entropy-values only gets the low-entropy values
        var frame = ((HtmlIFrameElement)document.QuerySelector("iframe")!).ContentNavigable!.Document;
        Assert.Equal($$"""{"brands":{{brands}},"mobile":false,"platform":"Linux"}""", TestScriptEngines.Evaluate(_engine, frame.DefaultView!, "window.result"));

        // The API is only exposed in the secure contexts
        document = Load(context, "http://example.com/insecure.html");
        Assert.Equal("undefinedfunction", TestScriptEngines.Evaluate(_engine, document.DefaultView!, "window.result"));

        // The .NET API
        var data = Load(context).DefaultView!.Navigator.UserAgentData;
        Assert.Equal("Linux", data.Platform);
        Assert.False(data.Mobile);
        Assert.Equal(["Test \"Browser\"", "Engine"], data.Brands.Select(brand => brand.Brand));
        Assert.Equal("Linux", data.ToJson().Platform);
    }

    [Fact]
    public void UserAgent_IsTheOneOfTheOptions()
    {
        AddPage(PageUrl, "<img src='https://example.com/a.png'>");
        var document = Load(CreateContext(new BrowsingContextOptions { UserAgent = "Mozilla/5.0 (Test)" }));

        Assert.Equal("Mozilla/5.0 (Test)", document.DefaultView!.Navigator.UserAgent);
        Assert.Equal("5.0 (Test)", document.DefaultView!.Navigator.AppVersion);
        Assert.Equal("Mozilla/5.0 (Test)", Header(Request(PageUrl), "User-Agent"));
        Assert.Equal("Mozilla/5.0 (Test)", Header(Request("https://example.com/a.png"), "User-Agent"));

        // Without the option, the user agent is the one of the library, and the requests have no User-Agent header
        _requests.Clear();
        document = Load(CreateContext());
        Assert.Equal(Navigator.DefaultUserAgent, document.DefaultView!.Navigator.UserAgent);
        Assert.Null(Header(Request(PageUrl), "User-Agent"));
    }

    [Fact]
    public void Mobile_DefaultsToTheViewport()
    {
        AddPage(PageUrl, "<p>a</p>");
        var document = Load(CreateContext(new BrowsingContextOptions { Viewport = new ViewportOptions { IsMobile = true } }));

        Assert.Equal("?1", Header(Request(PageUrl), "Sec-CH-UA-Mobile"));
        Assert.True(document.DefaultView!.Navigator.UserAgentData.Mobile);
    }

    private sealed class Loader(ClientHintsTests test) : DocumentLoader
    {
        public override ValueTask<DocumentResource?> LoadAsync(DocumentRequest request, CancellationToken cancellationToken)
        {
            test._requests.Add(request);
            test._onRequest?.Invoke();
            return ValueTask.FromResult(test._resources.GetValueOrDefault(request.Url));
        }
    }
}
