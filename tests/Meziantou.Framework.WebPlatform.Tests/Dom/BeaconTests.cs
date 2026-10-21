using System.Text;
using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.Dom.FileApi;
using Meziantou.Framework.WebPlatform.Scripting;
using Meziantou.Framework.WebPlatform.Tests.Scripting;

namespace Meziantou.Framework.WebPlatform.Tests.Dom;

/// <summary>navigator.sendBeacon() (https://w3c.github.io/beacon/), with a stub loader.</summary>
public sealed class BeaconTests : IDisposable
{
    private readonly BrowsingContextScope _contexts = new();
    private readonly List<ScriptError> _errors = [];
    private readonly ScriptEngine _engine = TestScriptEngines.Create();
    private readonly StubLoader _loader = new();

    public void Dispose()
    {
        _contexts.Dispose();
        TestScriptEngines.Dispose(_engine);
    }

    private Document Load(string html, string url = "https://example.com/")
    {
        var context = _contexts.Create(new BrowsingContextOptions
        {
            DocumentLoader = _loader,
            ScriptEngine = _engine,
            UnhandledScriptError = (_, error) => _errors.Add(error),
        });

        var document = context.Navigate(url, _ => html);
        _contexts.EventLoop.RunUntilIdle();
        Assert.Empty(_errors);
        return document;
    }

    [Fact]
    public void SendBeacon_PostsTheData()
    {
        var document = Load("<p>");
        Assert.True(document.DefaultView!.Navigator.SendBeacon("log?a=1", "hello"));

        var request = Assert.Single(_loader.Requests);
        Assert.Equal("https://example.com/log?a=1", request.Url);
        Assert.Equal("POST", request.Method);
        Assert.Equal(RequestMode.NoCors, request.Mode);
        Assert.True(request.IncludeCredentials);
        Assert.Equal("hello", Encoding.UTF8.GetString(request.Body!.Value.Span));
        Assert.Equal("text/plain;charset=UTF-8", request.BodyContentType);
    }

    [Fact]
    public void SendBeacon_WithoutData_PostsAnEmptyRequest()
    {
        var document = Load("<p>");
        Assert.True(document.DefaultView!.Navigator.SendBeacon("https://example.com/log"));

        var request = Assert.Single(_loader.Requests);
        Assert.Equal("POST", request.Method);
        Assert.Equal(0, request.Body?.Length ?? 0);
        Assert.DoesNotContain(request.Headers!, header => header.Key.Equals("Content-Type", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void SendBeacon_CrossOriginWithNonSafelistedContentType_IsPreflighted()
    {
        _loader.Responses["https://other.example/log"] = new DocumentResource("")
        {
            Headers = [new("Access-Control-Allow-Origin", "https://example.com"), new("Access-Control-Allow-Credentials", "true"), new("Access-Control-Allow-Headers", "content-type")],
        };
        var document = Load("<p>");
        var blob = new Blob([new byte[] { 1, 2, 3 }], new BlobPropertyBag { Type = "application/octet-stream" });
        Assert.True(document.DefaultView!.Navigator.SendBeacon("https://other.example/log", blob));
        _contexts.EventLoop.RunUntilIdle();

        Assert.Equal(["OPTIONS", "POST"], _loader.Requests.Select(request => request.Method));
        Assert.Equal(RequestMode.Cors, _loader.Requests[1].Mode);
        Assert.Equal("application/octet-stream", _loader.Requests[1].BodyContentType);
    }

    [Theory]
    [InlineData("ftp://example.com/")]
    [InlineData("http://[")]
    public void SendBeacon_InvalidUrl_ThrowsTypeError(string url)
    {
        var document = Load("<p>");
        Assert.Throws<TypeErrorException>(() => document.DefaultView!.Navigator.SendBeacon(url));
        Assert.Empty(_loader.Requests);
    }

    [Fact]
    public void SendBeacon_MixedContent_ReturnsFalse()
    {
        var document = Load("<p>");
        Assert.False(document.DefaultView!.Navigator.SendBeacon("http://example.com/log", "data"));
        Assert.Empty(_loader.Requests);
    }

    [Fact]
    public void SendBeacon_InflightKeepaliveQuota_IsShared()
    {
        var loader = new PendingLoader();
        var context = _contexts.Create(new BrowsingContextOptions { DocumentLoader = loader, ScriptEngine = _engine });
        var document = context.Navigate("https://example.com/", _ => "<p>");
        _contexts.EventLoop.RunUntilIdle();
        var navigator = document.DefaultView!.Navigator;

        Assert.False(navigator.SendBeacon("log", new string('*', 64 * 1024 + 1)));
        Assert.True(navigator.SendBeacon("log", new string('*', 64 * 1024)));
        Assert.True(navigator.SendBeacon("log", ""));
        Assert.False(navigator.SendBeacon("log", "x"));

        // The quota is released when the request is done
        loader.Complete();
        _contexts.EventLoop.RunUntilIdle();
        Assert.True(navigator.SendBeacon("log", "x"));
    }

    [Fact]
    public void SendBeacon_IsSentWhenTheDocumentIsDestroyed()
    {
        var loader = new PendingLoader();
        var context = _contexts.Create(new BrowsingContextOptions { DocumentLoader = loader, ScriptEngine = _engine });
        var document = context.Navigate("https://example.com/", _ => "<iframe src=about:blank></iframe>");
        _contexts.EventLoop.RunUntilIdle();
        var iframe = (HtmlIFrameElement)document.QuerySelector("iframe")!;

        Assert.True(iframe.ContentWindow!.Navigator.SendBeacon("https://example.com/log", "data"));
        iframe.Remove();
        loader.Complete();
        _contexts.EventLoop.RunUntilIdle();

        var request = Assert.Single(loader.Requests);
        Assert.Equal("https://example.com/log", request.Url);
        Assert.False(loader.Canceled);
    }

    [Fact]
    public void SendBeacon_FromScript()
    {
        var document = Load("""
            <script>
              const results = [navigator.sendBeacon('a', new Uint8Array([1, 2])), navigator.sendBeacon('b', new URLSearchParams('x=1'))];
              try { navigator.sendBeacon('c', new ReadableStream()); } catch (e) { results.push(e.name); }
              document.title = results.join('|');
            </script>
            """);
        Assert.Equal("true|true|TypeError", document.Title);
        Assert.Equal(["https://example.com/a", "https://example.com/b"], _loader.Requests.Select(request => request.Url));
        Assert.Null(_loader.Requests[0].BodyContentType);
        Assert.Equal("application/x-www-form-urlencoded;charset=UTF-8", _loader.Requests[1].BodyContentType);
    }

    private sealed class StubLoader : DocumentLoader
    {
        public Dictionary<string, DocumentResource> Responses { get; } = new(StringComparer.Ordinal);

        public List<DocumentRequest> Requests { get; } = [];

        public override ValueTask<DocumentResource?> LoadAsync(DocumentRequest request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return ValueTask.FromResult<DocumentResource?>(Responses.GetValueOrDefault(request.Url) ?? new DocumentResource(""));
        }
    }

    /// <summary>Loads the documents synchronously, and completes the other requests when <see cref="Complete"/> is called.</summary>
    private sealed class PendingLoader : DocumentLoader
    {
        private readonly List<TaskCompletionSource<DocumentResource?>> _pending = [];

        public List<DocumentRequest> Requests { get; } = [];

        public bool Canceled { get; private set; }

        public override ValueTask<DocumentResource?> LoadAsync(DocumentRequest request, CancellationToken cancellationToken)
        {
            if (request.Destination != RequestDestination.Empty)
                return ValueTask.FromResult<DocumentResource?>(new DocumentResource("") { Headers = [new("Content-Type", "text/html")] });

            Requests.Add(request);
            var completion = new TaskCompletionSource<DocumentResource?>();
            cancellationToken.Register(() => Canceled = true);
            _pending.Add(completion);
            return new ValueTask<DocumentResource?>(completion.Task);
        }

        public void Complete()
        {
            foreach (var completion in _pending)
            {
                completion.SetResult(new DocumentResource(""));
            }

            _pending.Clear();
        }
    }
}
