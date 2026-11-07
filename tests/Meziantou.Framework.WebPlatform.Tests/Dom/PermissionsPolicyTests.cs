using System.Text.Json;
using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.Scripting;
using Meziantou.Framework.WebPlatform.Tests.Scripting;
using Meziantou.Framework.WebPlatform.Urls;

namespace Meziantou.Framework.WebPlatform.Tests.Dom;

/// <summary>Permissions Policy (https://w3c.github.io/webappsec-permissions-policy/).</summary>
public sealed class PermissionsPolicyTests : IDisposable
{
    private const string PageUrl = "https://example.com/page.html";

    private readonly BrowsingContextScope _contexts = new();
    private readonly ScriptEngine _engine = TestScriptEngines.Create();
    private readonly Dictionary<string, DocumentResource> _resources = new(StringComparer.Ordinal);
    private readonly List<DocumentRequest> _requests = [];

    public void Dispose()
    {
        _contexts.Dispose();
        TestScriptEngines.Dispose(_engine);
    }

    private static OriginRecord Origin(string url) => OriginRecord.FromUrl(UrlParser.Parse(url, baseUrl: null)!);

    private static PolicyControlledFeature Feature(string name) => PolicyControlledFeature.Get(name)!;

    private void AddPage(string url, string html, params KeyValuePair<string, string>[] headers)
        => _resources[url] = new DocumentResource(html) { Headers = [new("Content-Type", "text/html"), .. headers] };

    private Document Load(string html, params KeyValuePair<string, string>[] headers)
    {
        AddPage(PageUrl, html, headers);
        var context = _contexts.Create(new BrowsingContextOptions { ScriptEngine = _engine, DocumentLoader = new Loader(this) });
        context.Navigate(PageUrl);
        _contexts.EventLoop.RunUntilIdle();
        return context.Document;
    }

    private static Document GetFrameDocument(Document document, string selector = "iframe")
        => ((HtmlIFrameElement)document.QuerySelector(selector)!).ContentNavigable!.Document;

    [Fact]
    public void Header_IsAStructuredFieldDictionary()
    {
        var self = Origin("https://example.com/");
        var policy = PermissionsPolicyAlgorithms.ProcessResponsePolicy(
            [new("Permissions-Policy", """fullscreen=(self "https://a.example" "https://*.b.example:8443" "https:" "not a source"), camera=*, geolocation=(), unknown=*, microphone=?1, usb=self;report-to=usb-endpoint, midi=("https://a.example" *)""")],
            self,
            reportOnly: false);

        // The unknown features and the members whose value is not a token, a string or an inner list are ignored
        Assert.Equal(["fullscreen", "camera", "geolocation", "usb", "midi"], policy.Declarations.Keys.Select(feature => feature.Name));

        var fullscreen = policy.Declarations[Feature("fullscreen")];
        Assert.False(fullscreen.IsAll);
        Assert.Same(self, fullscreen.SelfOrigin);
        Assert.Equal(["https://a.example", "https://*.b.example:8443", "https:"], fullscreen.Expressions);
        Assert.True(fullscreen.Matches(Origin("https://example.com/")));
        Assert.True(fullscreen.Matches(Origin("https://a.example/")));
        Assert.True(fullscreen.Matches(Origin("https://www.b.example:8443/")));
        Assert.True(fullscreen.Matches(Origin("https://other.example/")));
        Assert.False(fullscreen.Matches(Origin("http://other.example/")));
        Assert.False(fullscreen.Matches(OriginRecord.CreateOpaque()));

        Assert.True(policy.Declarations[Feature("camera")].IsAll);
        Assert.True(policy.Declarations[Feature("midi")].IsAll);
        Assert.False(policy.Declarations[Feature("geolocation")].Matches(self));
        Assert.True(policy.Declarations[Feature("usb")].Matches(self));
        Assert.Equal("usb-endpoint", policy.ReportingConfiguration[Feature("usb")]);
    }

    [Theory]
    [InlineData("fullscreen=self, camera")]
    [InlineData("fullscreen=self;")]
    [InlineData("Fullscreen=self")]
    public void Header_ThatIsNotADictionary_DeclaresNothing(string value)
    {
        var policy = PermissionsPolicyAlgorithms.ProcessResponsePolicy([new("Permissions-Policy", value)], Origin("https://example.com/"), reportOnly: false);

        // "camera" without value is the boolean true: the member is ignored; the other values are not valid dictionaries
        Assert.DoesNotContain(Feature("camera"), policy.Declarations.Keys);
        Assert.True(policy.Declarations.Count <= 1);
    }

    [Fact]
    public void AllowAttribute_IsAPolicyDirective()
    {
        var container = Origin("https://example.com/");
        var target = Origin("https://frame.example/");
        var directive = PermissionsPolicyAlgorithms.ParsePolicyDirective(
            "fullscreen; camera 'self' https://a.example/path https://*.b.example; geolocation 'none'; unknown *;; microphone 'SRC' *; usb 'src'",
            container,
            target);

        Assert.Equal(["fullscreen", "camera", "geolocation", "microphone", "usb"], directive.Keys.Select(feature => feature.Name));

        // A feature without allowlist has the allowlist 'src': the declared origin of the iframe
        Assert.Same(target, directive[Feature("fullscreen")].SrcOrigin);
        Assert.Same(target, directive[Feature("usb")].SrcOrigin);

        // The origins are compared exactly: a wildcard is not a source expression in the attribute
        var camera = directive[Feature("camera")];
        Assert.Same(container, camera.SelfOrigin);
        Assert.Equal(["https://a.example"], camera.Expressions);
        Assert.False(camera.Matches(Origin("https://www.b.example/")));

        Assert.False(directive[Feature("geolocation")].Matches(container));
        Assert.False(directive[Feature("geolocation")].Matches(target));
        Assert.True(directive[Feature("microphone")].IsAll);
    }

    [Fact]
    public void Document_WithoutHeader_HasTheDefaultAllowlists()
    {
        var document = Load("<p>");
        var policy = document.PermissionsPolicy;

        Assert.Contains("sync-xhr", policy.Features());
        Assert.Equal(policy.Features(), policy.AllowedFeatures());
        Assert.True(policy.AllowsFeature("fullscreen"));
        Assert.False(policy.AllowsFeature("fullscreen", "https://other.example"));
        Assert.True(policy.AllowsFeature("sync-xhr", "https://other.example"));
        Assert.False(policy.AllowsFeature("unknown-feature"));
        Assert.False(policy.AllowsFeature("fullscreen", "not a url"));
        Assert.Equal(["https://example.com"], policy.GetAllowlistForFeature("fullscreen"));
        Assert.Equal(["*"], policy.GetAllowlistForFeature("sync-xhr"));
        Assert.Empty(policy.GetAllowlistForFeature("unknown-feature"));
    }

    [Fact]
    public void Document_Header_DeclaresTheAllowlists()
    {
        var document = Load("<p>", new KeyValuePair<string, string>("Permissions-Policy", """fullscreen=(self "https://a.example"), sync-xhr=(), camera=*, geolocation=("https://a.example")"""));
        var policy = document.PermissionsPolicy;

        Assert.True(policy.AllowsFeature("fullscreen"));
        Assert.True(policy.AllowsFeature("fullscreen", "https://a.example"));
        Assert.False(policy.AllowsFeature("fullscreen", "https://b.example"));
        Assert.False(policy.AllowsFeature("sync-xhr"));
        Assert.True(policy.AllowsFeature("camera", "https://b.example"));
        Assert.DoesNotContain("sync-xhr", policy.AllowedFeatures());
        Assert.DoesNotContain("geolocation", policy.AllowedFeatures());
        Assert.Equal(["https://example.com", "https://a.example"], policy.GetAllowlistForFeature("fullscreen"));
        Assert.Equal(["*"], policy.GetAllowlistForFeature("camera"));

        // The allowlist of a feature that is not allowed in the document is empty
        Assert.Empty(policy.GetAllowlistForFeature("sync-xhr"));
        Assert.Empty(policy.GetAllowlistForFeature("geolocation"));
    }

    [Fact]
    public void Document_WithoutBrowsingContext_HasTheEmptyPolicy()
    {
        var document = Document.CreateHtmlDocument();

        Assert.True(document.PermissionsPolicy.AllowsFeature("fullscreen"));
        Assert.False(PermissionsPolicyAlgorithms.IsAllowedToUse(document, PolicyControlledFeature.SyncXhr));
    }

    [Theory]
    // The default allowlist 'self': a cross-origin iframe is not allowed without allow attribute
    [InlineData("", "", "https://example.com/frame.html", "fullscreen", true)]
    [InlineData("", "", "https://other.example/frame.html", "fullscreen", false)]
    [InlineData("", "allow='fullscreen'", "https://other.example/frame.html", "fullscreen", true)]
    [InlineData("", "allow='fullscreen *'", "https://other.example/frame.html", "fullscreen", true)]
    [InlineData("", "allow=\"fullscreen 'self'\"", "https://other.example/frame.html", "fullscreen", false)]
    [InlineData("", "allow=\"fullscreen 'none'\"", "https://example.com/frame.html", "fullscreen", false)]
    [InlineData("", "allow='fullscreen https://other.example'", "https://other.example/frame.html", "fullscreen", true)]
    [InlineData("", "allow='fullscreen https://third.example'", "https://other.example/frame.html", "fullscreen", false)]
    [InlineData("", "allowfullscreen", "https://other.example/frame.html", "fullscreen", true)]
    [InlineData("", "allowfullscreen allow=\"fullscreen 'none'\"", "https://other.example/frame.html", "fullscreen", false)]
    // The default allowlist *: a cross-origin iframe is allowed unless the allow attribute says otherwise
    [InlineData("", "", "https://other.example/frame.html", "sync-xhr", true)]
    [InlineData("", "allow=\"sync-xhr 'none'\"", "https://other.example/frame.html", "sync-xhr", false)]
    // The allow attribute cannot delegate a feature that the policy of the document disables for the origin of the iframe
    [InlineData("fullscreen=()", "allow='fullscreen *'", "https://example.com/frame.html", "fullscreen", false)]
    [InlineData("fullscreen=self", "allow='fullscreen *'", "https://other.example/frame.html", "fullscreen", false)]
    [InlineData("fullscreen=(self \"https://other.example\")", "allow='fullscreen'", "https://other.example/frame.html", "fullscreen", true)]
    [InlineData("fullscreen=(self \"https://other.example\")", "", "https://other.example/frame.html", "fullscreen", false)]
    [InlineData("fullscreen=*", "", "https://other.example/frame.html", "fullscreen", false)]
    [InlineData("sync-xhr=()", "", "https://other.example/frame.html", "sync-xhr", false)]
    [InlineData("sync-xhr=self", "", "https://other.example/frame.html", "sync-xhr", false)]
    public void Iframe_InheritedPolicy(string header, string attributes, string frameUrl, string feature, bool expected)
    {
        AddPage(frameUrl, "<p>");
        var document = Load($"<iframe {attributes} src='{frameUrl}'></iframe>", header.Length > 0 ? [new("Permissions-Policy", header)] : []);
        var iframe = (HtmlIFrameElement)document.QuerySelector("iframe")!;
        var frameDocument = iframe.ContentNavigable!.Document;

        Assert.Equal(expected, frameDocument.PermissionsPolicy.AllowsFeature(feature));
        Assert.Equal(expected, PermissionsPolicyAlgorithms.IsAllowedToUse(frameDocument, Feature(feature), report: false));

        // The policy object of the iframe gives what a document of the declared origin of the iframe would inherit
        Assert.Equal(expected, iframe.PermissionsPolicy.AllowsFeature(feature));
        Assert.Equal(expected, iframe.PermissionsPolicy.AllowedFeatures().Contains(feature));
    }

    [Fact]
    public void Iframe_HeaderOfTheFrame_CannotEnableADisabledFeature()
    {
        AddPage("https://other.example/frame.html", "<p>", new KeyValuePair<string, string>("Permissions-Policy", "fullscreen=*, sync-xhr=()"));
        var document = Load("<iframe src='https://other.example/frame.html'></iframe>");
        var policy = GetFrameDocument(document).PermissionsPolicy;

        Assert.False(policy.AllowsFeature("fullscreen"));
        Assert.Empty(policy.GetAllowlistForFeature("fullscreen"));
        Assert.False(policy.AllowsFeature("sync-xhr"));
    }

    [Fact]
    public void Iframe_LocalSchemes_InheritFromTheContainer()
    {
        var document = Load("""
            <iframe id=blank></iframe>
            <iframe id=srcdoc srcdoc="<p>"></iframe>
            <iframe id=data src="data:text/html,<p>"></iframe>
            <iframe id=allowed allow="fullscreen *" src="data:text/html,<p>"></iframe>
            <iframe id=src allow="fullscreen" src="data:text/html,<p>"></iframe>
            """, new KeyValuePair<string, string>("Permissions-Policy", "sync-xhr=(), fullscreen=*"));

        foreach (var id in (ReadOnlySpan<string>)["blank", "srcdoc", "data", "allowed", "src"])
        {
            Assert.False(GetFrameDocument(document, "#" + id).PermissionsPolicy.AllowsFeature("sync-xhr"), id);
        }

        // The about:blank and srcdoc documents have the origin of their container; a data: document has an opaque origin
        Assert.True(GetFrameDocument(document, "#blank").PermissionsPolicy.AllowsFeature("fullscreen"));
        Assert.True(GetFrameDocument(document, "#srcdoc").PermissionsPolicy.AllowsFeature("fullscreen"));
        Assert.False(GetFrameDocument(document, "#data").PermissionsPolicy.AllowsFeature("fullscreen"));
        Assert.True(GetFrameDocument(document, "#allowed").PermissionsPolicy.AllowsFeature("fullscreen"));
        Assert.True(GetFrameDocument(document, "#src").PermissionsPolicy.AllowsFeature("fullscreen"));
    }

    [Theory]
    // 'src' is the declared origin of the iframe: the opaque origin of the documents of a sandboxed iframe
    [InlineData("", "sandbox allow='fullscreen'", true)]
    [InlineData("", "sandbox allow=\"fullscreen 'src'\"", true)]
    [InlineData("", "sandbox allow='fullscreen *'", true)]
    [InlineData("", "sandbox allow=\"fullscreen 'self'\"", false)]
    [InlineData("", "sandbox allow='fullscreen https://example.com'", false)]
    [InlineData("", "sandbox", false)]
    [InlineData("", "sandbox='allow-same-origin'", true)]
    // The policy of the document must allow the opaque origin too
    [InlineData("fullscreen=self", "sandbox allow='fullscreen'", false)]
    [InlineData("fullscreen=*", "sandbox allow='fullscreen'", true)]
    public void Iframe_Sandboxed_HasAnOpaqueDeclaredOrigin(string header, string attributes, bool expected)
    {
        AddPage("https://example.com/frame.html", "<p>");
        var document = Load($"<iframe {attributes} src='/frame.html'></iframe>", header.Length > 0 ? [new("Permissions-Policy", header)] : []);
        var iframe = (HtmlIFrameElement)document.QuerySelector("iframe")!;

        Assert.Equal(expected, iframe.PermissionsPolicy.AllowsFeature("fullscreen"));
        Assert.Equal(expected, iframe.ContentNavigable!.Document.PermissionsPolicy.AllowsFeature("fullscreen"));
    }

    [Fact]
    public void Iframe_NestedSandboxedIframes_DoNotShareTheirOpaqueOrigin()
    {
        AddPage("https://example.com/frame.html", "<iframe sandbox src='/nested.html'></iframe>");
        AddPage("https://example.com/nested.html", "<p>");
        var document = Load("<iframe sandbox allow='fullscreen' src='/frame.html'></iframe>");
        var frameDocument = GetFrameDocument(document);

        Assert.True(frameDocument.PermissionsPolicy.AllowsFeature("fullscreen"));
        Assert.False(GetFrameDocument(frameDocument).PermissionsPolicy.AllowsFeature("fullscreen"));
    }

    [Fact]
    public void Iframe_Navigation_UsesTheAllowAttributeOfTheNavigation()
    {
        AddPage("https://other.example/frame.html", "<p>");
        AddPage("https://other.example/frame2.html", "<p>");
        var document = Load("<iframe src='https://other.example/frame.html'></iframe>");
        var iframe = (HtmlIFrameElement)document.QuerySelector("iframe")!;
        Assert.False(iframe.ContentNavigable!.Document.PermissionsPolicy.AllowsFeature("fullscreen"));

        // The attribute does not change the policy of the loaded document, but the one of the next document
        iframe.SetAttribute("allow", "fullscreen");
        Assert.False(iframe.ContentNavigable!.Document.PermissionsPolicy.AllowsFeature("fullscreen"));
        Assert.True(iframe.PermissionsPolicy.AllowsFeature("fullscreen"));

        iframe.Src = "https://other.example/frame2.html";
        _contexts.EventLoop.RunUntilIdle();
        Assert.True(iframe.ContentNavigable!.Document.PermissionsPolicy.AllowsFeature("fullscreen"));
    }

    [Fact]
    public void SyncXhr_Disabled_ThrowsAndReports()
    {
        _resources["https://example.com/data.txt"] = DocumentResource.Create("data", "text/plain");
        var document = Load("""
            <script>
            new ReportingObserver(reports => {
              const report = reports[0];
              document.title += "|" + [reports.length, report.type, report.url, JSON.stringify(report.body)].join("|");
            }).observe();
            const xhr = new XMLHttpRequest();
            xhr.open("GET", "/data.txt", false);
            try { xhr.send(); document.title = "sent"; } catch (e) { document.title = e.name; }
            const asyncXhr = new XMLHttpRequest();
            asyncXhr.open("GET", "/data.txt");
            asyncXhr.send();
            </script>
            """, new("Permissions-Policy", "sync-xhr=();report-to=endpoint"), new("Reporting-Endpoints", "endpoint=\"/reports\""));

        Assert.Equal("""NetworkError|1|permissions-policy-violation|https://example.com/page.html|{"allowAttribute":null,"columnNumber":11,"disposition":"enforce","featureId":"sync-xhr","lineNumber":8,"sourceFile":"https://example.com/page.html","srcAttribute":null}""", document.Title);

        // The asynchronous request is sent, and the report is delivered to the endpoint of the report-to parameter
        Assert.Single(_requests, request => request.Url == "https://example.com/data.txt");
        var reportRequest = Assert.Single(_requests, request => request.Destination == RequestDestination.Report);
        Assert.Equal("https://example.com/reports", reportRequest.Url);
        using var json = JsonDocument.Parse(reportRequest.Body!.Value);
        var report = Assert.Single(json.RootElement.EnumerateArray());
        Assert.Equal("permissions-policy-violation", report.GetProperty("type").GetString());
        Assert.Equal("sync-xhr", report.GetProperty("body").GetProperty("featureId").GetString());
        Assert.Equal("enforce", report.GetProperty("body").GetProperty("disposition").GetString());
        Assert.False(report.GetProperty("body").TryGetProperty("allowAttribute", out _));
    }

    [Fact]
    public void ReportOnly_ObserverReceivesTheReport()
    {
        _resources["https://example.com/data.txt"] = DocumentResource.Create("data", "text/plain");
        var document = Load("""
            <script>
            new ReportingObserver(reports => document.title += "|" + reports.map(report => report.type + " " + report.body.featureId + " " + report.body.disposition).join()).observe();
            const xhr = new XMLHttpRequest();
            xhr.open("GET", "/data.txt", false);
            xhr.send();
            document.title = xhr.responseText;
            </script>
            """, new KeyValuePair<string, string>("Permissions-Policy-Report-Only", "sync-xhr=()"));

        Assert.Equal("data|permissions-policy-violation sync-xhr report", document.Title);
        Assert.True(document.PermissionsPolicy.AllowsFeature("sync-xhr"));
    }

    [Fact]
    public void SyncXhr_DisabledByTheAllowAttribute()
    {
        _resources["https://example.com/data.txt"] = DocumentResource.Create("data", "text/plain");
        AddPage("https://example.com/frame.html", """
            <script>
            const xhr = new XMLHttpRequest();
            xhr.open("GET", "/data.txt", false);
            try { xhr.send(); document.title = "sent"; } catch (e) { document.title = e.name; }
            </script>
            """);
        var document = Load("""<iframe id=a src="/frame.html"></iframe><iframe id=b allow="sync-xhr 'none'" src="/frame.html"></iframe>""");

        Assert.Equal("sent", GetFrameDocument(document, "#a").Title);
        Assert.Equal("NetworkError", GetFrameDocument(document, "#b").Title);
    }

    [Fact]
    public void AllowAttribute_DelegatingADisabledFeature_IsAPotentialViolation()
    {
        AddPage("https://example.com/frame.html", "<p>");
        var document = Load("""
            <script>
            new ReportingObserver(reports => document.title += reports.map(report => report.type + " " + JSON.stringify(report.body)).join()).observe();
            </script>
            <iframe allow="camera; fullscreen" src="/frame.html"></iframe>
            """, new KeyValuePair<string, string>("Permissions-Policy", "camera=()"));

        Assert.Equal("""potential-permissions-policy-violation {"allowAttribute":"camera; fullscreen","columnNumber":null,"disposition":"enforce","featureId":"camera","lineNumber":null,"sourceFile":null,"srcAttribute":"/frame.html"}""", document.Title);
    }

    [Fact]
    public void Unload_Disabled_DoesNotFireTheEvent()
    {
        AddPage("https://example.com/next.html", "<p>");
        AddPage("https://example.com/frame.html", "<script>onunload = () => parent.document.title += 'frame;';</script>");
        AddPage("https://example.com/disabled.html", "<script>onunload = () => parent.document.title += 'disabled;';</script>", new KeyValuePair<string, string>("Permissions-Policy", "unload=()"));
        var document = Load("""<iframe id=a src="/frame.html"></iframe><iframe id=b src="/disabled.html"></iframe>""");

        ((HtmlIFrameElement)document.QuerySelector("#a")!).Src = "/next.html";
        ((HtmlIFrameElement)document.QuerySelector("#b")!).Src = "/next.html";
        _contexts.EventLoop.RunUntilIdle();

        Assert.Equal("frame;", document.Title);
    }

    [Fact]
    public void Autoplay_Disabled_PlayIsNotAllowed()
    {
        AddPage("https://other.example/frame.html", """
            <video></video>
            <script>document.querySelector("video").play().catch(e => document.title = e.name);</script>
            """);
        var document = Load("""<iframe id=a src="https://other.example/frame.html"></iframe><iframe id=b allow="autoplay" src="https://other.example/frame.html"></iframe>""");

        Assert.Equal("NotAllowedError", GetFrameDocument(document, "#a").Title);
        Assert.Equal("NotSupportedError", GetFrameDocument(document, "#b").Title);
    }

    private sealed class Loader(PermissionsPolicyTests test) : DocumentLoader
    {
        public override ValueTask<DocumentResource?> LoadAsync(DocumentRequest request, CancellationToken cancellationToken)
        {
            test._requests.Add(request);
            if (request.Destination == RequestDestination.Report)
                return ValueTask.FromResult<DocumentResource?>(DocumentResource.Create("", "text/plain"));

            return ValueTask.FromResult(test._resources.GetValueOrDefault(request.Url));
        }
    }
}
