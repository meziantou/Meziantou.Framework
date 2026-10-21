using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.Scripting;
using Meziantou.Framework.WebPlatform.Tests.Scripting;

namespace Meziantou.Framework.WebPlatform.Tests.Jint;

/// <summary>document.open(), write(), writeln() and close() (https://html.spec.whatwg.org/multipage/dynamic-markup-insertion.html).</summary>
public sealed class DynamicMarkupInsertionTests : IDisposable
{
    private readonly BrowsingContextScope _contexts = new();
    private readonly List<ScriptError> _errors = [];
    private readonly ScriptEngine _engine = TestScriptEngines.Create();

    public void Dispose()
    {
        _contexts.Dispose();
        TestScriptEngines.Dispose(_engine);
    }

    private Document Load(string html, DocumentLoader? loader = null)
    {
        var context = _contexts.Create(new BrowsingContextOptions
        {
            ScriptEngine = _engine,
            DocumentLoader = loader,
            UnhandledScriptError = (_, error) => _errors.Add(error),
        });

        var document = context.Navigate("https://example.com/", _ => html);
        _contexts.EventLoop.RunUntilIdle();
        return document;
    }

    private static string BodyHtml(Document document) => document.Body!.InnerHtml;

    [Fact]
    public void Write_InsertsAfterTheScript()
    {
        var document = Load("<body><script>document.write('<p id=a>a</p>')</script><p id=b>b</p>");
        Assert.Equal("<script>document.write('<p id=a>a</p>')</script><p id=\"a\">a</p><p id=\"b\">b</p>", BodyHtml(document));
        Assert.Empty(_errors);
    }

    [Fact]
    public void Write_TokenizesAcrossCalls()
    {
        var document = Load("<body><script>document.write('<di'); document.write('v id=d cla'); document.write('ss=c>x</div>');</script>");
        Assert.Equal("c", document.GetElementById("d")!.ClassName);
        Assert.Equal("x", document.GetElementById("d")!.TextContent);
    }

    [Fact]
    public void Write_LineFeedAfterACarriageReturnOfAnotherCall_IsIgnored()
    {
        var document = Load("<body><script>document.write('<pre id=p>\\r'); document.write('\\nx\\r'); document.write('\\nA</pre>');</script>");
        Assert.Equal("x\nA", document.GetElementById("p")!.TextContent);
    }

    [Fact]
    public void Write_TheCharactersAreInsertedImmediately()
    {
        var document = Load("<body><script>document.write('abc<b>d'); document.title = document.body.textContent;</script>");
        Assert.Equal("document.write('abc<b>d'); document.title = document.body.textContent;abcd", document.Title);
    }

    [Fact]
    public void Write_TheMarkupCanContinueAfterTheScript()
    {
        var document = Load("<body><script>document.write('<p id=p title=\"')</script>t\">x</p>");
        Assert.Equal("t", document.GetElementById("p")!.GetAttribute("title"));
    }

    [Fact]
    public void Write_WrittenScriptsRunInOrder()
    {
        var document = Load("""
            <body><script>
              var log = [];
              document.write('<script>log.push(2); document.write("<i>3</i>"); log.push(4)</' + 'script>');
              log.push(5);
            </script><script>document.title = log.join() + ' ' + document.querySelector('i').textContent</script>
            """);
        Assert.Equal("2,4,5 3", document.Title);
        Assert.Empty(_errors);
    }

    [Fact]
    public void Writeln_AppendsALineFeed()
    {
        var document = Load("<body><pre id=p><script>document.writeln('a'); document.writeln('b')</script></pre>");
        Assert.Equal("a\nb\n", document.GetElementById("p")!.LastChild!.TextContent);
    }

    [Fact]
    public void Write_AfterTheDocumentIsLoaded_ReplacesTheDocument()
    {
        var document = Load("<body><p id=old>old</p><script>setTimeout(() => { document.write('<p id=new>new</p>'); document.close(); }, 0)</script>");
        Assert.Null(document.GetElementById("old"));
        Assert.Equal("new", document.GetElementById("new")!.TextContent);
        Assert.Equal(DocumentReadyState.Complete, document.ReadyState);
    }

    [Fact]
    public void Open_IsIgnoredInAScriptRunByTheParser()
    {
        var document = Load("<body><p id=p>p</p><script>document.title = String(document.open() === document)</script>");
        Assert.Equal("true", document.Title);
        Assert.NotNull(document.GetElementById("p"));
    }

    [Fact]
    public void Open_RemovesTheNodesAndTheEventListeners()
    {
        var document = Load("""
            <body><p id=p>p</p><script>
              var calls = 0;
              document.addEventListener('custom', () => calls++);
              setTimeout(() => {
                document.open();
                document.dispatchEvent(new Event('custom'));
                document.write('<title>' + calls + ' ' + document.readyState + '</title>');
                document.close();
              }, 0);
            </script>
            """);
        Assert.Null(document.GetElementById("p"));
        Assert.Equal("0 loading", document.Title);
    }

    [Fact]
    public void Write_ExternalScriptsCannotReplaceTheDocument()
    {
        var loader = new RecordingLoader();
        loader.Resources["https://example.com/a.js"] = new DocumentResource("setTimeout(() => {}, 0); document.write('<p id=written>w</p>');") { Headers = [new("Content-Type", "text/javascript")] };
        var document = Load("<body><p id=p>p</p><script async src=a.js></script>", loader);
        Assert.NotNull(document.GetElementById("p"));
        Assert.Null(document.GetElementById("written"));
    }

    [Fact]
    public void Write_ThrowsInAnXmlDocument()
    {
        var document = Load("<body><script>try { new DOMParser().parseFromString('<a/>', 'application/xml').write('x') } catch (e) { document.title = e.name }</script>");
        Assert.Equal("InvalidStateError", document.Title);
    }

    [Fact]
    public void Write_ThrowsInACustomElementConstructorRunByTheParser()
    {
        var document = Load("""
            <body><script>
              customElements.define('x-e', class extends HTMLElement {
                constructor() {
                  super();
                  try { document.write('x') } catch (e) { document.title = e.name }
                }
              });
            </script><x-e></x-e>
            """);
        Assert.Equal("InvalidStateError", document.Title);
    }

    [Fact]
    public void Write_TrustedTypesRequireATrustedHtml()
    {
        var document = Load("""
            <head><meta http-equiv="Content-Security-Policy" content="require-trusted-types-for 'script'"></head>
            <body><script>
              try { document.write('<p id=a>a</p>') } catch (e) { document.title = e.name }
              const policy = trustedTypes.createPolicy('p', { createHTML: s => s });
              document.write(policy.createHTML('<p id=b>b</p>'));
            </script>
            """);
        Assert.Equal("TypeError", document.Title);
        Assert.Null(document.GetElementById("a"));
        Assert.NotNull(document.GetElementById("b"));
    }

    [Fact]
    public void NetApi_OpenWriteClose()
    {
        var document = Document.CreateHtmlDocument();
        document.Open();
        document.Write("<!DOCTYPE html><title>t</title><p>", "a");
        document.Writeln("b");
        document.Close();
        Assert.Equal("t", document.Title);
        Assert.Equal("<p>ab\n</p>", document.Body!.InnerHtml);
        Assert.Equal("CSS1Compat", document.CompatMode);
    }

    [Fact]
    public void NetApi_TokensCanBeSplitAcrossWrites()
    {
        var document = Document.CreateHtmlDocument();
        document.Open();
        foreach (var part in new[] { "<!DOC", "TYPE html><p ti", "tle=\"a&am", "p;b\">x&l", "t;y<!-", "- c -", "-><scr", "ipt>1 < 2</sc", "ript>" })
        {
            document.Write(part);
        }

        document.Close();
        Assert.Equal("CSS1Compat", document.CompatMode);
        var p = document.QuerySelector("p")!;
        Assert.Equal("a&b", p.GetAttribute("title"));
        Assert.Equal("<p title=\"a&amp;b\">x&lt;y<!-- c --><script>1 < 2</script></p>", document.Body!.InnerHtml);
    }

    [Fact]
    public void Write_FromAnSvgScript_RunsTheWrittenInlineScriptDuringTheWrite_AndTheExternalScriptAfterTheScript()
    {
        // As in the browsers (the specification sets the parser pause flag while an SVG script runs). The content of an SVG
        // script is markup: the tags written by the script are split.
        var loader = new RecordingLoader();
        loader.Resources["https://example.com/a.js"] = new DocumentResource("log.push('external')") { Headers = [new("Content-Type", "text/javascript")] };
        var document = Load("""
            <script>window.log = [];</script>
            <svg><script>
              log.push('start');
              document.write("<" + "script>log.push('inline')<" + "/script><" + "script href='a.js'><" + "/script>");
              log.push('end');
            </script><script>log.push('next')</script></svg>
            <script>document.title = log.join()</script>
            """, loader);

        Assert.Equal("start,inline,end,external,next", document.Title);
    }

    [Fact]
    public void Write_ExternalScriptOfAScriptCreatedParser_RunsAfterTheWriteReturns()
    {
        // The loader completes synchronously, but the fetch completes from a task: the parser of document.open() pauses
        var loader = new RecordingLoader();
        loader.Resources["https://example.com/a.js"] = new DocumentResource("top.log.push('external')") { Headers = [new("Content-Type", "text/javascript")] };
        var document = Load("""
            <iframe></iframe>
            <script>
              window.log = [];
              var doc = frames[0].document;
              doc.open();
              doc.write("<script>top.log.push('inline')<" + "/script><script src='https://example.com/a.js'><" + "/script><script>top.log.push('after')<" + "/script>");
              log.push('close');
              doc.close();
              window.onload = () => document.title = log.join();
            </script>
            """, loader);

        Assert.Equal("inline,close,external,after", document.Title);
    }
}
