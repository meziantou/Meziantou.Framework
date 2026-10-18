using Meziantou.Framework.WebPlatform.Dom;

namespace Meziantou.Framework.WebPlatform.Tests.Html;

public sealed class EditingTests : IDisposable
{
    private readonly BrowsingContextScope _contexts = new();

    public void Dispose() => _contexts.Dispose();

    private Document Load(string html)
    {
        var document = _contexts.Create().Navigate("https://example.com/", _ => html);
        _contexts.EventLoop.RunUntilIdle();
        return document;
    }

    [Fact]
    public void IsContentEditable_DisconnectedElement()
    {
        var document = HtmlDocumentParser.ParseDocument("");
        var parent = (HtmlElement)document.CreateElement("div");
        var child = (HtmlElement)parent.AppendChild(document.CreateElement("span"));
        Assert.False(child.IsContentEditable);

        parent.ContentEditable = "plaintext-only";
        Assert.True(parent.IsContentEditable);
        Assert.True(child.IsContentEditable);

        child.ContentEditable = "false";
        Assert.False(child.IsContentEditable);
    }

    [Fact]
    public void DesignMode_ContentEditableFalseIsNotEditable()
    {
        var document = Load("<div id=a contenteditable=false><div id=b contenteditable=true>text</div></div><p id=c>text</p>");
        document.DesignMode = "on";
        Assert.False(((HtmlElement)document.GetElementById("a")!).IsContentEditable);
        Assert.True(((HtmlElement)document.GetElementById("b")!).IsContentEditable);
        Assert.True(((HtmlElement)document.GetElementById("c")!).IsContentEditable);
        Assert.True(document.QueryCommandEnabled("delete"));

        document.DesignMode = "off";
        Assert.False(((HtmlElement)document.GetElementById("c")!).IsContentEditable);
        Assert.False(document.QueryCommandEnabled("delete"));
    }

    [Fact]
    public void ExecCommand_InsertTextAndDelete()
    {
        var document = Load("<div id=host contenteditable>FAILPASS</div>");
        var host = document.GetElementById("host")!;
        var text = (Text)host.FirstChild!;
        var inputTypes = new List<string>();
        host.AddEventListener("input", e => inputTypes.Add(((InputEvent)e).InputType));

        Assert.True(document.QueryCommandSupported("insertText"));
        Assert.True(document.QueryCommandSupported("bold"));
        Assert.False(document.QueryCommandSupported("quasit"));

        var selection = document.GetSelection()!;
        selection.Collapse(text, 0);
        selection.Extend(text, 4);
        Assert.True(document.ExecCommand("delete"));
        Assert.Equal("PASS", text.Data);

        Assert.True(document.ExecCommand("insertText", showUI: false, "OK "));
        Assert.Equal("OK PASS", text.Data);
        Assert.Equal(3u, selection.FocusOffset);

        Assert.True(document.ExecCommand("forwardDelete"));
        Assert.Equal("OK ASS", text.Data);
        Assert.Equal(["deleteContentBackward", "insertText", "deleteContentForward"], inputTypes);

        Assert.False(document.ExecCommand("quasit"));
    }

    [Fact]
    public void ExecCommand_DisabledOutsideEditingHost()
    {
        var document = Load("<p id=p>text</p>");
        document.GetSelection()!.Collapse(document.GetElementById("p")!.FirstChild, 1);
        Assert.False(document.QueryCommandEnabled("insertText"));
        Assert.False(document.ExecCommand("insertText", showUI: false, "x"));
        Assert.True(document.QueryCommandEnabled("selectAll"));
    }

    [Fact]
    public void QueryCommandSupported_ThrowsInXmlDocument()
    {
        var document = new DomParser().ParseFromString("<root/>", DomParserSupportedType.ApplicationXml);
        var exception = Assert.Throws<DomException>(() => document.QueryCommandSupported("delete"));
        Assert.Equal("InvalidStateError", exception.Name);
    }

    [Fact]
    public void FragmentNavigation_RevealsHiddenUntilFoundAncestors()
    {
        var document = Load("<div id=outer hidden=until-found><details id=details><summary>s</summary><div id=target>t</div></details></div>");
        var events = new List<string>();
        document.AddEventListener("beforematch", e => events.Add(((Element)e.Target!).Id));

        document.Location!.Hash = "#target";

        Assert.Equal(["outer"], events);
        Assert.False(document.GetElementById("outer")!.HasAttribute("hidden"));
        Assert.True(document.GetElementById("details")!.HasAttribute("open"));
    }
}
