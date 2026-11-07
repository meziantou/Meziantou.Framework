using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.Tests.Scripting;

namespace Meziantou.Framework.WebPlatform.Tests.Dom;

/// <summary>https://html.spec.whatwg.org/multipage/scripting.html#script-processing-model</summary>
public sealed class ScriptElementTests : IDisposable
{
    private readonly BrowsingContextScope _contexts = new();
    private readonly RecordingScriptEngine _engine = new();
    private readonly RecordingLoader _loader = new();

    public void Dispose() => _contexts.Dispose();

    private Document Load(string html)
    {
        var context = _contexts.Create(new BrowsingContextOptions { ScriptEngine = _engine, DocumentLoader = _loader });
        return context.Navigate("https://example.com/dir/page.html", _ => html);
    }

    private List<string> Executed => _engine.Scripts.Select(s => s.SourceText).ToList();

    private static HtmlScriptElement CreateScript(Document document, string text)
    {
        var script = (HtmlScriptElement)document.CreateElement("script");
        script.Text = text;
        return script;
    }

    [Fact]
    public void ParserInsertedScripts_RunAtTheirEndTag()
    {
        _loader.Resources["https://example.com/dir/b.js"] = new DocumentResource("b()") { Headers = [new("Content-Type", "text/javascript")] };
        var document = Load("<script>a()</script><script src='b.js'></script><script type=text/plain>c()</script><script></script>");
        Assert.Equal(["a()", "b()"], Executed);
        Assert.Equal("https://example.com/dir/page.html", _engine.Scripts[0].Url);
        Assert.Equal("https://example.com/dir/b.js", _engine.Scripts[1].Url);
        Assert.Equal("https://example.com/dir/b.js", _engine.Scripts[1].BaseUrl);
        Assert.Same(document.RelevantWindow, _engine.Scripts[0].Global);

        var request = Assert.Single(_loader.Requests, r => r.Destination == RequestDestination.Script);
        Assert.Equal("https://example.com/dir/b.js", request.Url);
        Assert.Same(document.GetElementsByTagName("script")[1], request.Initiator);
        Assert.Equal("https://example.com", request.Origin);

        var scripts = document.GetElementsByTagName("script").Cast<HtmlScriptElement>().ToArray();
        Assert.True(scripts[0].AlreadyStarted);
        Assert.True(scripts[1].AlreadyStarted);
        Assert.True(scripts[0].IsParserInserted);
        Assert.False(scripts[0].Async);

        // A script whose type is not supported is not started: it can run if its type is changed and it is inserted again
        Assert.False(scripts[2].AlreadyStarted);

        // The empty script can run later, when a script adds text to it
        Assert.False(scripts[3].AlreadyStarted);
        Assert.False(scripts[3].IsParserInserted);
        Assert.True(scripts[3].Async);
        scripts[3].AppendChild(document.CreateTextNode("d()"));
        Assert.Equal(["a()", "b()", "d()"], Executed);
    }

    [Fact]
    public void ExecutedScript_DoesNotRunAgainWhenMoved()
    {
        var document = Load("<script>a()</script><div></div>");
        document.QuerySelector("div")!.AppendChild(document.QuerySelector("script")!);
        Assert.Equal(["a()"], Executed);
    }

    [Fact]
    public void InsertedScript_IsPreparedAfterTheInsertionCompletes()
    {
        var document = Load("<body>");
        var fragment = document.CreateDocumentFragment();
        var script1 = CreateScript(document, "a()");
        var script2 = CreateScript(document, "b()");
        fragment.AppendChild(script1);
        fragment.AppendChild(script2);
        var countWhenRunning = new List<int>();
        _engine.Behaviors["a()"] = _ => { countWhenRunning.Add(document.Body!.ChildNodes.Count); return null; };
        Assert.Empty(Executed);

        document.Body!.AppendChild(fragment);
        Assert.Equal(["a()", "b()"], Executed);
        Assert.Equal([2], countWhenRunning);
        Assert.True(script1.AlreadyStarted);

        // Removing and inserting again does not prepare the script again
        script1.Remove();
        document.Body.AppendChild(script1);
        Assert.Equal(2, Executed.Count);
    }

    [Fact]
    public void EmptyInsertedScript_IsPreparedWhenItGetsTextOrSrc()
    {
        _loader.Resources["https://example.com/dir/a.js"] = new DocumentResource("a()") { Headers = [new("Content-Type", "text/javascript")] };
        var document = Load("<body>");
        var script = (HtmlScriptElement)document.CreateElement("script");
        document.Body!.AppendChild(script);
        Assert.DoesNotContain(_loader.Requests, r => r.Destination == RequestDestination.Script);

        script.Src = "a.js";
        Assert.Equal("https://example.com/dir/a.js", Assert.Single(_loader.Requests, r => r.Destination == RequestDestination.Script).Url);

        // A script inserted by a script runs from a task
        Assert.Empty(Executed);
        _contexts.EventLoop.RunUntilIdle();
        Assert.Equal(["a()"], Executed);

        script.Src = "b.js";
        Assert.Single(_loader.Requests, r => r.Destination == RequestDestination.Script);
    }

    [Fact]
    public void RemovingAChild_DoesNotPrepareTheScript()
    {
        var document = Load("<body>");
        var script = CreateScript(document, "a()");
        script.Type = "text/plain";
        var div = document.CreateElement("div");
        script.AppendChild(div);
        document.Body!.AppendChild(script);
        Assert.Empty(Executed);

        script.Type = "";
        div.Remove();
        Assert.Empty(Executed);
    }

    [Fact]
    public void InnerHtmlScripts_NeverRun()
    {
        var document = Load("<body>");
        document.Body!.InnerHtml = "<script>a()</script><svg><script>b()</script></svg>";
        var script = (HtmlScriptElement)document.QuerySelector("script")!;
        Assert.True(script.AlreadyStarted);
        Assert.Empty(Executed);

        document.Body.AppendChild(script.CloneNode(subtree: true));
        Assert.Empty(Executed);
    }

    [Fact]
    public void CreateContextualFragmentScripts_RunWhenInserted()
    {
        var document = Load("<body>");
        var range = document.CreateRange();
        range.SelectNodeContents(document.Body!);
        var fragment = range.CreateContextualFragment("<script>a()</script>");
        Assert.Empty(Executed);

        document.Body!.AppendChild(fragment);
        Assert.Equal(["a()"], Executed);
    }

    [Fact]
    public void InsertHtmlCommandScripts_NeverRun()
    {
        // The insertHTML command creates its fragment as createContextualFragment() does, but its scripts are already started
        var document = Load("<body><div contenteditable>foo</div>");
        var host = document.QuerySelector("div")!;
        document.GetSelection()!.SetBaseAndExtent(host.FirstChild!, 3, host.FirstChild!, 3);
        Assert.True(document.ExecCommand("insertHTML", showUI: false, "<script>a()</script><svg><script>b()</script></svg>bar"));
        Assert.Equal("foo<script>a()</script><svg><script>b()</script></svg>bar", host.InnerHtml);
        Assert.True(((HtmlScriptElement)host.QuerySelector("script")!).AlreadyStarted);
        Assert.Empty(Executed);

        document.Body!.AppendChild(host.QuerySelector("script")!.CloneNode(subtree: true));
        document.Body.AppendChild(host.QuerySelector("svg")!.CloneNode(subtree: true));
        Assert.Empty(Executed);
    }

    [Fact]
    public void TemplateContentScripts_RunWhenClonedIntoTheDocument()
    {
        var document = Load("<template><script>a()</script></template>");
        Assert.Empty(Executed);

        var template = (HtmlTemplateElement)document.QuerySelector("template")!;
        document.Body!.AppendChild(template.Content.CloneNode(subtree: true));
        Assert.Equal(["a()"], Executed);
    }

    [Fact]
    public void Scripts_DoNotRunInDocumentsWithoutBrowsingContext()
    {
        var document = Load("<body>");
        var other = document.Implementation.CreateHtmlDocument("");
        var script = CreateScript(other, "a()");
        other.Body!.AppendChild(script);
        Assert.True(script.AlreadyStarted);
        Assert.Empty(Executed);

        // The script is already started: it does not run when moved to a document that has a browsing context
        document.Body!.AppendChild(script);
        Assert.Empty(Executed);
    }

    [Fact]
    public void DisconnectedScript_IsNotPrepared()
    {
        var document = Load("<body>");
        var div = document.CreateElement("div");
        div.AppendChild(CreateScript(document, "a()"));
        Assert.Empty(Executed);

        document.Body!.AppendChild(div);
        Assert.Equal(["a()"], Executed);
    }

    [Fact]
    public void ExternalScript_SetsCurrentScriptAndFiresLoad()
    {
        _loader.Resources["https://example.com/dir/a.js"] = new DocumentResource("a()") { Headers = [new("Content-Type", "text/javascript")] };
        var document = Load("<body>");
        var script = (HtmlScriptElement)document.CreateElement("script");
        script.Src = "a.js";
        var events = new List<string>();
        script.AddEventListener("load", e => events.Add("load"));
        script.AddEventListener("error", e => events.Add("error"));
        Element? currentScript = null;
        _engine.Behaviors["a()"] = _ => currentScript = document.CurrentScript;
        document.Body!.AppendChild(script);
        _contexts.EventLoop.RunUntilIdle();

        Assert.Same(script, currentScript);
        Assert.Null(document.CurrentScript);
        Assert.Equal(["load"], events);
    }

    [Fact]
    public void ExternalScript_FiresErrorWhenTheScriptCannotBeFetched()
    {
        var document = Load("<body>");
        var script = (HtmlScriptElement)document.CreateElement("script");
        script.Src = "a.js";
        var events = new List<string>();
        script.AddEventListener("error", e => events.Add("error"));
        document.Body!.AppendChild(script);
        _contexts.EventLoop.RunUntilIdle();

        Assert.Equal(["error"], events);
        Assert.Empty(Executed);
    }

    [Fact]
    public void ExternalScript_FiresErrorWhenTheStatusIsNotOk()
    {
        // https://html.spec.whatwg.org/multipage/webappapis.html#fetch-a-classic-script: the body of an error page is not executed
        _loader.Resources["https://example.com/dir/a.js"] = new DocumentResource("a()") { StatusCode = 404, Headers = [new("Content-Type", "text/javascript")] };
        var document = Load("<body>");
        var script = (HtmlScriptElement)document.CreateElement("script");
        script.Src = "a.js";
        var events = new List<string>();
        script.AddEventListener("load", e => events.Add("load"));
        script.AddEventListener("error", e => events.Add("error"));
        document.Body!.AppendChild(script);
        _contexts.EventLoop.RunUntilIdle();

        Assert.Equal(["error"], events);
        Assert.Empty(Executed);
    }

    [Theory]
    [InlineData("text/javascript", null, true)]
    [InlineData("text/plain", null, true)]
    [InlineData("application/octet-stream", null, true)]
    [InlineData(null, null, true)]
    [InlineData("image/png", null, false)]
    [InlineData("video/mp4", null, false)]
    [InlineData("audio/ogg", null, false)]
    [InlineData("text/csv", null, false)]
    [InlineData("text/javascript", "nosniff", true)]
    [InlineData("application/x-javascript; charset=utf-8", "NOSNIFF", true)]
    [InlineData("text/plain", "nosniff", false)]
    [InlineData(null, "nosniff", false)]
    [InlineData("text/plain", "nosniff, invalid", false)]
    [InlineData("text/plain", "invalid, nosniff", true)]
    [InlineData("text/plain", "no-sniff", true)]
    public void ExternalScript_MimeType(string? contentType, string? contentTypeOptions, bool expectedExecuted)
    {
        // https://fetch.spec.whatwg.org/#should-response-to-request-be-blocked-due-to-mime-type? and https://fetch.spec.whatwg.org/#should-response-to-request-be-blocked-due-to-nosniff?
        var headers = new List<KeyValuePair<string, string>>();
        if (contentType is not null)
        {
            headers.Add(new("Content-Type", contentType));
        }

        if (contentTypeOptions is not null)
        {
            headers.Add(new("X-Content-Type-Options", contentTypeOptions));
        }

        _loader.Resources["https://example.com/dir/a.js"] = new DocumentResource("a()") { Headers = headers };
        var document = Load("<body>");
        var script = (HtmlScriptElement)document.CreateElement("script");
        script.Src = "a.js";
        var events = new List<string>();
        script.AddEventListener("load", e => events.Add("load"));
        script.AddEventListener("error", e => events.Add("error"));
        document.Body!.AppendChild(script);
        _contexts.EventLoop.RunUntilIdle();

        Assert.Equal(expectedExecuted ? ["a()"] : [], Executed);
        Assert.Equal([expectedExecuted ? "load" : "error"], events);
    }

    [Theory]
    [InlineData("<script src='a.js'></script>", "https://example.com/dir/page.html", "strict-origin-when-cross-origin")]
    [InlineData("<script src='https://cdn.example.net/a.js'></script>", "https://example.com/", "strict-origin-when-cross-origin")]
    [InlineData("<script src='a.js' referrerpolicy=no-referrer></script>", null, "no-referrer")]
    [InlineData("<meta name=referrer content=origin><script src='a.js'></script>", "https://example.com/", "origin")]
    public void ExternalScript_Referrer(string html, string? expectedReferrer, string expectedPolicy)
    {
        Load(html);
        var request = Assert.Single(_loader.Requests, r => r.Destination == RequestDestination.Script);
        Assert.Equal(expectedReferrer, request.Referrer);
        Assert.Equal(expectedPolicy, request.ReferrerPolicy);
    }

    [Fact]
    public void ExternalScript_DataUrlWithAnImageMimeType_IsBlocked()
    {
        Load("<script src='data:image/png,a()'></script><script src='data:text/plain,b()'></script>");
        Assert.Equal(["b()"], Executed);
    }

    [Fact]
    public void ExternalScript_IsDecodedWithTheCharsetOfTheResponse()
    {
        _loader.Resources["https://example.com/dir/a.js"] = new DocumentResource(new byte[] { 0x61, 0xE9 }) { Headers = [new("Content-Type", "text/javascript; charset=windows-1252")] };
        _loader.Resources["https://example.com/dir/b.js"] = new DocumentResource(new byte[] { 0xEF, 0xBB, 0xBF, 0x62, 0xC3, 0xA9 }) { Headers = [new("Content-Type", "text/javascript; charset=windows-1252")] };
        Load("<script src=a.js></script><script src=b.js></script>");
        Assert.Equal(["a\U000000E9", "b\U000000E9"], Executed);
    }

    [Fact]
    public void ExternalScript_WithoutCharsetInTheResponse_IsDecodedWithTheCharsetAttribute()
    {
        _loader.Resources["https://example.com/dir/a.js"] = new DocumentResource(new byte[] { 0x61, 0xE9 }) { Headers = [new("Content-Type", "text/javascript")] };
        _loader.Resources["https://example.com/dir/b.js"] = new DocumentResource(new byte[] { 0x62, 0xE9 }) { Headers = [new("Content-Type", "text/javascript; charset=utf-8")] };
        _loader.Resources["https://example.com/dir/c.js"] = new DocumentResource(new byte[] { 0x63, 0xC3, 0xA9 }) { Headers = [new("Content-Type", "text/javascript")] };
        Load("<meta charset=utf-8><script src=a.js charset=windows-1252></script><script src=b.js charset=windows-1252></script><script src=c.js charset=bogus></script>");
        Assert.Equal(["a\U000000E9", "b\U0000FFFD", "c\U000000E9"], Executed);
    }

    [Fact]
    public void WithoutScriptEngine_NothingIsFetchedOrRun()
    {
        var context = _contexts.Create(new BrowsingContextOptions { DocumentLoader = _loader, ScriptingEnabled = true });
        var document = context.Navigate("https://example.com/", _ => "<script src=a.js></script><script>a()</script><noscript><b></b></noscript>");
        Assert.Empty(_loader.Requests);
        Assert.True(((HtmlScriptElement)document.QuerySelector("script")!).AlreadyStarted);

        // Scripting is enabled: the content of noscript is text
        Assert.Null(document.QuerySelector("b"));
    }

    [Fact]
    public void SvgScript_RunsAtItsEndTag()
    {
        _loader.Resources["https://example.com/dir/b.js"] = new DocumentResource("b()") { Headers = [new("Content-Type", "text/javascript")] };
        var document = Load("<svg><script>a()</script><script href=b.js /><script type=text/plain>c()</script></svg>");
        Assert.Equal(["a()", "b()"], Executed);
        Assert.IsType<SvgScriptElement>(_engine.Scripts[0].Element);
        Assert.Same(document.QuerySelectorAll("script")[0], _engine.Scripts[0].Element);
    }

    [Fact]
    public void InsertedSvgScript_Runs()
    {
        _loader.Resources["https://example.com/dir/d.js"] = new DocumentResource("d()") { Headers = [new("Content-Type", "text/javascript")] };
        var document = Load("<svg></svg>");
        var svg = document.QuerySelector("svg")!;
        SvgScriptElement CreateSvgScript(string text, string? type = null)
        {
            var script = (SvgScriptElement)document.CreateElementNS(Namespaces.Svg, "script");
            if (type is not null)
            {
                script.SetAttribute("type", type);
            }

            script.TextContent = text;
            return script;
        }

        // The language attribute is ignored, the type must be a JavaScript MIME type
        var languageScript = CreateSvgScript("a()");
        languageScript.SetAttribute("language", "invalid");
        svg.AppendChild(languageScript);
        svg.AppendChild(CreateSvgScript("b()", " TEXT/JAVASCRIPT "));
        var invalidType = (SvgScriptElement)svg.AppendChild(CreateSvgScript("c()", "text/plain"));
        Assert.Equal(["a()", "b()"], Executed);

        // An empty script runs when a child is inserted, and a script runs when href is set (an external script runs from a task)
        var empty = (SvgScriptElement)svg.AppendChild(CreateSvgScript(""));
        empty.AppendChild(document.CreateTextNode("e()"));
        var external = (SvgScriptElement)svg.AppendChild(CreateSvgScript(""));
        external.SetAttribute("href", "d.js");
        Assert.Equal(["a()", "b()", "e()"], Executed);
        _contexts.EventLoop.RunUntilIdle();
        Assert.Equal(["a()", "b()", "e()", "d()"], Executed);

        // A script runs once
        invalidType.RemoveAttribute("type");
        svg.AppendChild(invalidType);
        svg.AppendChild(languageScript);
        Assert.Equal(["a()", "b()", "e()", "d()", "c()"], Executed);
    }

    [Fact]
    public void SvgScriptWithEmptyHref_FiresAnErrorEventWithoutRunning()
    {
        var document = Load("<svg><script href=''>a()</script></svg>");
        var errors = 0;
        document.QuerySelector("script")!.AddEventListener("error", _ => errors++);
        _contexts.EventLoop.RunUntilIdle();
        Assert.Empty(Executed);
        Assert.Equal(1, errors);
        Assert.DoesNotContain(_loader.Requests, request => request.Url == "https://example.com/dir/page.html");
    }

    [Fact]
    public void SvgScriptsOfFragments_RunWhenInsertedFromCreateContextualFragmentOnly()
    {
        var document = Load("<body>");
        document.Body!.InnerHtml = "<svg><script>a()</script></svg>";
        document.Body.AppendChild(document.QuerySelector("svg")!.CloneNode(subtree: true));
        Assert.Empty(Executed);

        var range = document.CreateRange();
        range.SelectNodeContents(document.Body);
        var fragment = range.CreateContextualFragment("<svg><script>b()</script></svg>");
        Assert.Empty(Executed);
        document.Body.AppendChild(fragment);
        Assert.Equal(["b()"], Executed);
    }
}
