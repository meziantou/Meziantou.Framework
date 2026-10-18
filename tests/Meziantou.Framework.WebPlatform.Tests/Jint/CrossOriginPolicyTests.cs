using System.Net;
using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.Jint;
using Meziantou.Framework.WebPlatform.Scripting;
using Meziantou.Framework.WebPlatform.Tests.Scripting;

namespace Meziantou.Framework.WebPlatform.Tests.Jint;

/// <summary>The cross-origin opener and embedder policies, the cookies of the documents and the navigator object.</summary>
public sealed class CrossOriginPolicyTests : IDisposable
{
    private readonly BrowsingContextScope _contexts = new();
    private readonly List<ScriptError> _errors = [];
    private readonly RecordingLoader _loader = new();
    private readonly JintScriptEngine _engine = new();
    private readonly CookieJar _cookies = new();

    public void Dispose()
    {
        _contexts.Dispose();
        _engine.Dispose();
    }

    private Document Load(string html, params KeyValuePair<string, string>[] headers)
    {
        var context = _contexts.Create(new BrowsingContextOptions
        {
            ScriptEngine = _engine,
            DocumentLoader = _loader,
            CookieJar = _cookies,
            UnhandledScriptError = (_, error) => _errors.Add(error),
        });

        var document = context.Navigate("https://example.com/", _ => new DocumentResource(html) { Headers = headers });
        for (var i = 0; i < 5; i++)
        {
            _contexts.EventLoop.RunUntilIdle();
            Thread.Sleep(10);
        }

        Assert.Empty(_errors);
        return document;
    }

    [Fact]
    public void PopupWithAnotherOpenerPolicy_SwitchesBrowsingContextGroup()
    {
        _loader.Resources["https://example.com/popup.html"] = new DocumentResource("<script>window.result = String(window.opener) + ',' + window.name;</script>")
        {
            Headers = [new("Cross-Origin-Opener-Policy", "same-origin")],
        };
        var document = Load("""
            <script>
              const popup = window.open('popup.html', 'name');
              window.onload = () => setTimeout(() => document.title = popup.closed, 0);
            </script>
            """);

        Assert.Equal("true", document.Title);
        var popup = Assert.Single(document.BrowsingContext!.GetGroupTopLevelBrowsingContexts(), context => context.IsAuxiliary);
        Assert.Null(popup.Opener);
        Assert.Equal("", popup.Name);
        Assert.Equal("https://example.com/popup.html", popup.Document.Url);
    }

    [Fact]
    public void PopupWithSameOpenerPolicy_KeepsItsOpener()
    {
        KeyValuePair<string, string>[] coop = [new("Cross-Origin-Opener-Policy", "same-origin")];
        _loader.Resources["https://example.com/popup.html"] = new DocumentResource("") { Headers = coop };
        var document = Load("""
            <script>
              const popup = window.open('popup.html');
              window.onload = () => setTimeout(() => document.title = popup.closed + ',' + (popup.opener === window), 0);
            </script>
            """, coop);

        Assert.Equal("false,true", document.Title);
    }

    [Fact]
    public void OpenerPolicyAndEmbedderPolicy_CrossOriginIsolated()
    {
        var document = Load(
            "<script>document.title = crossOriginIsolated;</script>",
            new("Cross-Origin-Opener-Policy", "same-origin"),
            new("Cross-Origin-Embedder-Policy", "require-corp"));

        Assert.Equal("true", document.Title);
    }

    [Fact]
    public void EmbedderPolicy_BlocksCrossOriginNoCorsResponseWithoutResourcePolicy()
    {
        _loader.Resources["https://other.example/data"] = new DocumentResource("");
        _loader.Resources["https://other.example/corp"] = new DocumentResource("") { Headers = [new("Cross-Origin-Resource-Policy", "cross-origin")] };
        var document = Load("""
            <script>
              Promise.all([
                fetch('https://other.example/data', { mode: 'no-cors' }).then(() => 'loaded', () => 'blocked'),
                fetch('https://other.example/corp', { mode: 'no-cors' }).then(() => 'loaded', () => 'blocked'),
              ]).then(results => document.title = results.join());
            </script>
            """, new KeyValuePair<string, string>("Cross-Origin-Embedder-Policy", "require-corp"));

        Assert.Equal("blocked,loaded", document.Title);
    }

    [Fact]
    public void DocumentCookie_UsesTheCookieJar()
    {
        _cookies.SetCookie("https://example.com/", "server=1; Path=/");
        _cookies.SetCookie("https://example.com/", "secret=2; Path=/; HttpOnly");
        var document = Load("""
            <script>
              document.cookie = 'script=3';
              document.cookie = 'ignored=4; HttpOnly';
              document.title = document.cookie;
            </script>
            """);

        Assert.Equal("server=1; script=3", document.Title);
        Assert.Equal("3", _cookies.GetCookies("https://example.com/").Single(cookie => cookie.Name == "script").Value);
    }

    [Fact]
    public void Navigator_DescribesTheUserAgent()
    {
        var document = Load("<script>document.title = [navigator.userAgent, navigator.language, navigator.plugins.length, navigator.userActivation.isActive, navigator === window.clientInformation].join();</script>");

        Assert.Equal(Navigator.DefaultUserAgent + ",en-US,0,false,true", document.Title);
    }
}
