using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.Scripting;
using Meziantou.Framework.WebPlatform.Tests.Scripting;

namespace Meziantou.Framework.WebPlatform.Tests.Html;

/// <summary>
/// The preloads (https://html.spec.whatwg.org/multipage/links.html#link-type-preload): the map of preloaded resources and
/// its consumption by the fetches, the <c>Link</c> headers, the module preloads and the prefetches.
/// </summary>
public sealed class LinkPreloadTests : IDisposable
{
    // A 2x3 PNG (only the header is read)
    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 13, 0x49, 0x48, 0x44, 0x52, 0, 0, 0, 2, 0, 0, 0, 3, 8, 6, 0, 0, 0];

    private readonly BrowsingContextScope _contexts = new();
    private readonly ScriptEngine _engine = TestScriptEngines.Create();
    private readonly List<ScriptError> _errors = [];
    private readonly List<DocumentRequest> _requests = [];
    private readonly Dictionary<string, TaskCompletionSource<DocumentResource?>> _pending = new(StringComparer.Ordinal);

    public void Dispose()
    {
        _contexts.Dispose();
        TestScriptEngines.Dispose(_engine);
    }

    private Document Load(string html, IReadOnlyList<KeyValuePair<string, string>>? headers = null)
    {
        var context = _contexts.Create(new BrowsingContextOptions
        {
            ScriptEngine = _engine,
            DocumentLoader = new Loader(this),
            UnhandledScriptError = (_, error) => _errors.Add(error),
        });
        var document = context.Navigate("https://example.com/dir/page.html", _ => new DocumentResource(html) { Headers = [new("Content-Type", "text/html"), .. headers ?? []] });
        _contexts.EventLoop.RunUntilIdle();
        Assert.Empty(_errors);
        return document;
    }

    /// <summary>The requests sent to the loader that are not speculative: their destination, their mode and their URL.</summary>
    private List<string> Requests => [.. _requests.Where(r => !r.IsSpeculative).Select(r => r.Destination + " " + r.Mode + " " + r.Url["https://example.com/dir/".Length..])];

    private static string EntryNames(Document document) => string.Join(',', document.DefaultView!.Performance.GetEntriesByType("resource").Select(entry => entry.Name["https://example.com/dir/".Length..]));

    private sealed class Loader(LinkPreloadTests test) : DocumentLoader
    {
        public override ValueTask<DocumentResource?> LoadAsync(DocumentRequest request, CancellationToken cancellationToken)
        {
            test._requests.Add(request);
            var name = request.Url[(request.Url.LastIndexOf('/', StringComparison.Ordinal) + 1)..];
            if (name.StartsWith("slow", StringComparison.Ordinal) && !request.IsSpeculative)
            {
                var completion = new TaskCompletionSource<DocumentResource?>(TaskCreationOptions.RunContinuationsAsynchronously);
                test._pending[name] = completion;
                return new ValueTask<DocumentResource?>(completion.Task);
            }

            return ValueTask.FromResult(Respond(name));
        }
    }

    private static DocumentResource? Respond(string name) => name switch
    {
        _ when name.StartsWith("missing", StringComparison.Ordinal) => null,
        _ when name.StartsWith("notfound", StringComparison.Ordinal) => new DocumentResource("") { StatusCode = 404, Headers = [new("Content-Type", "text/plain")] },
        _ when name.Contains(".css", StringComparison.Ordinal) => DocumentResource.Create("p { color: rgb(0, 128, 0) }", "text/css"),
        _ when name.Contains(".png", StringComparison.Ordinal) => DocumentResource.Create(Png, "image/png"),
        _ when name.Contains(".mjs", StringComparison.Ordinal) => DocumentResource.Create("window.log = (window.log || '') + 'module,';", "text/javascript"),
        _ when name.Contains(".js", StringComparison.Ordinal) => DocumentResource.Create("window.log = (window.log || '') + 'script,';", "text/javascript"),
        _ => DocumentResource.Create("text", "text/plain"),
    };

    [Fact]
    public void Preload_IsConsumedByTheFetchOfTheSameRequest()
    {
        var document = Load("""
            <!doctype html>
            <link rel=preload as=script href=a.js onload="window.log = (window.log || '') + 'preloaded,'">
            <link rel=preload as=image href=b.png>
            <link rel=preload as=fetch href=c.txt crossorigin>
            <script>
              addEventListener('load', async () => {
                const script = document.createElement('script');
                script.src = 'a.js';
                await new Promise(resolve => { script.onload = resolve; document.body.append(script); });
                const image = new Image();
                await new Promise(resolve => { image.onload = resolve; image.src = 'b.png'; });
                const text = await (await fetch('c.txt')).text();
                document.title = window.log + image.width + ',' + text;
              });
            </script>
            """);

        // Each resource is requested once, and has one resource timing entry (the one of its preload)
        Assert.Equal("preloaded,script,2,text", document.Title);
        Assert.Equal(["Script NoCors a.js", "Image NoCors b.png", "Empty Cors c.txt"], Requests);
        Assert.Equal("a.js,b.png,c.txt", EntryNames(document));
    }

    [Fact]
    public void Preload_IsNotConsumedByARequestWithAnotherModeOrDestination()
    {
        var document = Load("""
            <!doctype html>
            <link rel=preload as=script href=a.js>
            <link rel=preload as=image href=b.png crossorigin>
            <script src=a.js crossorigin></script>
            <img src=b.png>
            """);

        Assert.Equal(["Script NoCors a.js", "Image Cors b.png", "Script Cors a.js", "Image NoCors b.png"], Requests);
        Assert.Equal(4, document.DefaultView!.Performance.GetEntriesByType("resource").Count);
    }

    [Fact]
    public async Task Preload_BeingFetchedIsConsumed_AndDoesNotDelayTheLoadEvent()
    {
        var document = Load("""
            <!doctype html>
            <link rel=preload as=script href=slow.js onload="window.log = (window.log || '') + 'preloaded,'">
            <body onload="window.log = (window.log || '') + 'load,'">
            """);

        // The load event of the window does not wait for the preload
        Assert.Equal("load,", TestScriptEngines.Evaluate(_engine, document.DefaultView!, "window.log"));

        // A script that requests the resource waits for the response of the preload
        TestScriptEngines.Evaluate(_engine, document.DefaultView!, "const script = document.createElement('script'); script.src = 'slow.js'; script.onload = () => document.title = window.log; document.body.append(script);");
        _contexts.EventLoop.RunUntilIdle();
        Assert.Equal(["Script NoCors slow.js"], Requests);

        _pending["slow.js"].SetResult(Respond("slow.js"));
        await _contexts.EventLoop.RunUntilIdleAsync(TestContext.Current.CancellationToken);
        // The response is given to the fetch that consumed the preload, then the load event of the link is fired
        Assert.Equal("load,script,", document.Title);
        Assert.Equal("load,script,preloaded,", TestScriptEngines.Evaluate(_engine, document.DefaultView!, "window.log"));
        Assert.Equal(["Script NoCors slow.js"], Requests);
    }

    [Fact]
    public void Preload_FiresAnErrorEventOnlyForANetworkError()
    {
        var document = Load("""
            <!doctype html>
            <link rel=preload as=image href=notfound.png onload="document.title += 'load 404,'" onerror="document.title += 'error 404,'">
            <link rel=preload as=image href=missing.png onload="document.title += 'load,'" onerror="document.title += 'error,'">
            <script>
              addEventListener('load', () => {
                const image = new Image();
                image.onerror = () => document.title += 'image error';
                image.src = 'missing.png';
              });
            </script>
            """);

        // The network error of the preload is consumed too: the image is not requested again
        Assert.Equal("load 404,error,image error", document.Title);
        Assert.Equal(["Image NoCors notfound.png", "Image NoCors missing.png"], Requests);
    }

    [Theory]
    [InlineData("as=image type=image/png href=a.png", "Image NoCors a.png")]
    [InlineData("as=image type=image/unknown href=a.png", null)]
    [InlineData("as=IMAGE href=a.png", "Image NoCors a.png")]
    [InlineData("as=font type=font/woff2 href=a.woff2 crossorigin=use-credentials", "Font Cors a.woff2")]
    [InlineData("as=font type=text/plain href=a.woff2", null)]
    [InlineData("as=style type=text/css href=a.css", "Style NoCors a.css")]
    [InlineData("as=track type=text/vtt href=a.vtt", "Track NoCors a.vtt")]
    [InlineData("as=fetch type=anything href=a.txt", "Empty NoCors a.txt")]
    [InlineData("as=script type=text/plain href=a.js", null)]
    [InlineData("as=video href=a.mp4", null)]
    [InlineData("as=json href=a.json", null)]
    [InlineData("href=a.txt", null)]
    [InlineData("as=image href=''", null)]
    [InlineData("as=image media=print href=a.png", null)]
    [InlineData("as=image href=a.png imagesrcset='b.png 100w, c.png 400w, d.png 800w' imagesizes=300px", "Image NoCors c.png")]
    [InlineData("as=image imagesrcset='b.png 1x, c.png 2x'", "Image NoCors b.png")]
    public void Preload_RequestOfTheAttributes(string attributes, string? expectedRequest)
    {
        Load($"<!doctype html><script>const link = document.createElement('link'); link.rel = 'preload'; document.head.append(link);</script><link rel=preload {attributes}>");
        Assert.Equal(expectedRequest is null ? [] : [expectedRequest], Requests);
    }

    [Fact]
    public void Preload_IntegrityMetadata()
    {
        // sha256 of "text"
        const string Integrity = "sha256-mC2ePrmW9VnmM/TRlN7zdh2Qn1o7ZH0ahR/q1nwyydE=";
        var document = Load($$"""
            <!doctype html>
            <link rel=preload as=fetch crossorigin href=a.txt integrity="{{Integrity}}" onload="document.title += 'load,'">
            <link rel=preload as=fetch crossorigin href=b.txt integrity="sha256-AAAA" onerror="document.title += 'error,'">
            <link rel=preload as=fetch crossorigin href=c.txt integrity="{{Integrity}}">
            <script>
              addEventListener('load', async () => {
                // Consumed: no integrity metadata, or the metadata of the preload
                await fetch('a.txt');
                await fetch('b.txt').catch(() => document.title += 'failed,');
                // Not consumed: other metadata
                await fetch('c.txt', { integrity: 'sha384-AAAA' }).catch(() => document.title += 'mismatch');
              });
            </script>
            """);

        Assert.Equal("load,error,failed,mismatch", document.Title);
        Assert.Equal(["Empty Cors a.txt", "Empty Cors b.txt", "Empty Cors c.txt", "Empty Cors c.txt"], Requests);
    }

    [Fact]
    public void ScriptAndStyleSheet_CheckTheirIntegrityMetadata()
    {
        var document = Load("""
            <!doctype html>
            <script src=a.js integrity="sha256-AAAA" onerror="document.title += 'script error,'"></script>
            <link rel=stylesheet href=a.css integrity="sha256-AAAA" onerror="document.title += 'style error'">
            <p>text</p>
            """);

        Assert.Equal("script error,style error", document.Title);
        Assert.Equal("rgb(0, 0, 0)", document.DefaultView!.GetComputedStyle(document.QuerySelector("p")!).Color);
    }

    [Fact]
    public void LinkHeaders_PreloadTheResourcesOfTheDocument()
    {
        var document = Load(
            """
            <!doctype html>
            <script src=a.js></script>
            <script>
              addEventListener('load', () => document.title = window.log);
            </script>
            """,
            [
                new("Link", "<a.js>; rel=preload; as=script, </dir/b.png>;rel=\"preload\";as=image;crossorigin=\"anonymous\""),
                new("Link", "<c.css>; rel=preload; as=style; media=print"),
                new("Link", "<d.png>; rel=preload; as=image; imagesrcset=\"e.png 2x, f.png 3x\"; referrerpolicy=no-referrer"),
                new("Link", "<g.txt>; rel=stylesheet, <h.txt>; rel=preload"),
            ]);

        Assert.Equal("script,", document.Title);
        Assert.Equal(["Script NoCors a.js", "Image Cors b.png", "Image NoCors d.png"], Requests);
        Assert.Null(_requests.Single(r => r.Url.EndsWith("d.png", StringComparison.Ordinal)).Referrer);
        Assert.Equal("a.js,b.png,d.png", EntryNames(document));
    }

    [Theory]
    [InlineData("<a>; rel=preload", "a|rel=preload")]
    [InlineData("  <a b> ;REL = \"pre\\\"load\" ; as=script;as=style , <c>;x", "a b|rel=pre\"load;as=script,c|x=")]
    [InlineData("a; rel=preload, <b>; rel=\"x,y\"; title=\"a;b\"", "b|rel=x,y;title=a;b")]
    [InlineData("<a", "")]
    public void LinkHeaderParser_ParsesTheLinks(string value, string expected)
    {
        var links = LinkHeaderParser.Parse(value);
        Assert.Equal(expected, string.Join(',', links.Select(link => link.Target + "|" + string.Join(';', link.Parameters.Select(parameter => parameter.Key + "=" + parameter.Value)))));
    }

    [Fact]
    public async Task ModulePreload_AddsTheModuleToTheModuleMap()
    {
        var document = Load("""
            <!doctype html>
            <link rel=modulepreload href=a.mjs onload="document.title += 'load,'">
            <link rel=modulepreload href=b.mjs as=image onerror="document.title += 'error as,'">
            <link rel=modulepreload href=missing.mjs onerror="document.title += 'error,'">
            <link rel=modulepreload href=c.json as=json onerror="document.title += 'error json,'">
            <link rel=modulepreload href=d.mjs media=print onload="document.title += 'media,'">
            """);

        // The module is fetched, not evaluated; a destination that is not a module preload destination is an error
        await _contexts.EventLoop.RunUntilIdleAsync(TestContext.Current.CancellationToken);
        Assert.Equal(["error as,", "error json,", "error,", "load,"], document.Title.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(part => part + ",").Order(StringComparer.Ordinal));
        Assert.Equal(["Script Cors a.mjs", "Script Cors missing.mjs", "Json Cors c.json"], Requests);
        Assert.Equal("undefined", TestScriptEngines.Evaluate(_engine, document.DefaultView!, "String(window.log)"));

        // The module script uses the module of the module map
        TestScriptEngines.Evaluate(_engine, document.DefaultView!, "const script = document.createElement('script'); script.type = 'module'; script.src = 'a.mjs'; document.body.append(script);");
        await _contexts.EventLoop.RunUntilIdleAsync(TestContext.Current.CancellationToken);
        Assert.Equal("module,", TestScriptEngines.Evaluate(_engine, document.DefaultView!, "window.log"));
        Assert.Equal(3, Requests.Count);
    }

    [Fact]
    public void Prefetch_SendsARequestWithItsPurpose()
    {
        var document = Load("""
            <!doctype html>
            <link rel=prefetch href=a.txt as=image onload="document.title += 'load,'">
            <link rel=prefetch href=missing.txt onerror="document.title += 'error'">
            """);

        Assert.Equal("load,error", document.Title);
        Assert.Equal(["Empty NoCors a.txt", "Empty NoCors missing.txt"], Requests);
        Assert.All(_requests, request => Assert.Equal("prefetch", HeaderList.Get(request.Headers, "Sec-Purpose")));
    }
}
