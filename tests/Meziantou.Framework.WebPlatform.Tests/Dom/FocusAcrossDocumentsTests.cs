using Meziantou.Framework.WebPlatform.Dom;

namespace Meziantou.Framework.WebPlatform.Tests.Dom;

/// <summary>The focus of the nested browsing contexts and of the inert nodes (https://html.spec.whatwg.org/multipage/interaction.html#focus).</summary>
public sealed class FocusAcrossDocumentsTests : IDisposable
{
    private readonly BrowsingContextScope _contexts = new();

    public void Dispose() => _contexts.Dispose();

    private Document Load(string html)
    {
        var document = _contexts.Create().Navigate("https://example.com/", _ => html);
        _contexts.EventLoop.RunUntilIdle();
        return document;
    }

    private static List<string> RecordWindowFocusEvents(params (string Name, Window Window)[] windows)
    {
        var events = new List<string>();
        foreach (var (name, window) in windows)
        {
            window.AddEventListener("focus", _ => events.Add(name + " focus"));
            window.AddEventListener("blur", _ => events.Add(name + " blur"));
        }

        return events;
    }

    [Fact]
    public void WindowFocusEvents_FollowTheFocusedDocument()
    {
        var document = Load("<input id=outer><iframe id=frame srcdoc='<input id=inner><iframe id=nested srcdoc=\"<input id=deep>\"></iframe>'></iframe>");
        var frame = (HtmlIFrameElement)document.GetElementById("frame")!;
        var middle = frame.ContentDocument!;
        var nested = (HtmlIFrameElement)middle.GetElementById("nested")!;
        var deep = nested.ContentDocument!;
        var events = RecordWindowFocusEvents(("top", document.DefaultView!), ("middle", middle.DefaultView!), ("deep", deep.DefaultView!));

        // The focus moves into the innermost document: the window of the top-level document loses the focus, and the window
        // of the document in between gets no event
        ((HtmlElement)deep.GetElementById("deep")!).Focus();
        Assert.Equal(["top blur", "deep focus"], events);
        Assert.Same(frame, document.ActiveElement);
        Assert.Same(nested, middle.ActiveElement);

        // The focus moves to an element of the top-level document: the active element of the documents it leaves is their body
        events.Clear();
        ((HtmlElement)document.GetElementById("outer")!).Focus();
        Assert.Equal(["deep blur", "top focus"], events);
        Assert.Same(middle.Body, middle.ActiveElement);
        Assert.Same(deep.Body, deep.ActiveElement);
    }

    [Fact]
    public void WindowFocus_KeepsTheFocusedElementOfTheDocument()
    {
        var document = Load("<input id=outer><iframe id=frame srcdoc='<input id=inner>'></iframe>");
        var frame = (HtmlIFrameElement)document.GetElementById("frame")!;
        var inner = (HtmlElement)frame.ContentDocument!.GetElementById("inner")!;
        inner.Focus();

        frame.ContentWindow!.Focus();
        Assert.Same(inner, frame.ContentDocument.ActiveElement);

        // The focused element of the top-level document is the iframe: window.focus() focuses its viewport
        document.DefaultView!.Focus();
        Assert.Same(document.Body, document.ActiveElement);
        Assert.False(frame.ContentDocument.HasFocus());
    }

    [Fact]
    public void Inert_TheDocumentOfAnInertIFrameCanBeFocusedByItsScripts()
    {
        var document = Load("<div inert><iframe id=frame srcdoc='<input id=inner>'></iframe></div><input id=outer>");
        var frame = (HtmlIFrameElement)document.GetElementById("frame")!;
        var inner = (HtmlElement)frame.ContentDocument!.GetElementById("inner")!;

        // The inert iframe cannot be focused, but the elements of its document can
        frame.Focus();
        Assert.Same(document.Body, document.ActiveElement);
        inner.Focus();
        Assert.Same(inner, frame.ContentDocument.ActiveElement);
        Assert.True(frame.ContentDocument.HasFocus());
    }

    [Fact]
    public void Inert_TheTextOfTheInertNodesIsNotInTheTextOfTheSelection()
    {
        var document = Load("<div id=a>one</div><div inert>two</div><div id=c>three</div>");
        var selection = document.GetSelection()!;
        selection.SelectAllChildren(document.Body!);
        Assert.Equal("one\nthree", selection.ToString());
    }

    [Fact]
    public void Inert_EditingCommandsAreDisabledInInertEditingHosts()
    {
        var document = Load("<div id=host contenteditable inert>text</div><div id=other contenteditable>text</div>");
        var selection = document.GetSelection()!;
        selection.Collapse(document.GetElementById("host")!.FirstChild, 2);
        Assert.False(document.ExecCommand("delete"));
        Assert.Equal("text", document.GetElementById("host")!.TextContent);

        selection.Collapse(document.GetElementById("other")!.FirstChild, 2);
        Assert.True(document.ExecCommand("delete"));
        Assert.Equal("txt", document.GetElementById("other")!.TextContent);
    }

    [Fact]
    public void LabelFocus_FocusesTheLabeledControl()
    {
        var document = Load("<label id=label for=control inert>label</label><input id=control><div inert><input id=inert></div><label id=other for=inert>other</label>");
        ((HtmlElement)document.GetElementById("label")!).Focus();
        Assert.Same(document.GetElementById("control"), document.ActiveElement);

        // The inert control of the other label cannot be focused
        ((HtmlElement)document.GetElementById("other")!).Focus();
        Assert.Same(document.GetElementById("control"), document.ActiveElement);
    }
}
