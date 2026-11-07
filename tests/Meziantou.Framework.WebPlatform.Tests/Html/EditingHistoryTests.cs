using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.Html;

namespace Meziantou.Framework.WebPlatform.Tests.Html;

/// <summary>The undo history of the editable content: the <c>undo</c> and <c>redo</c> commands, and the undo of the user.</summary>
public sealed class EditingHistoryTests : IDisposable
{
    private readonly BrowsingContextScope _contexts = new();

    public void Dispose() => _contexts.Dispose();

    private (Document Document, Element Host) Load(string html)
    {
        var document = _contexts.Create().Navigate("https://example.com/", _ => $"<!DOCTYPE html><div id=host contenteditable>{html}</div><p>after</p>");
        _contexts.EventLoop.RunUntilIdle();
        var host = document.GetElementById("host")!;
        foreach (var text in host.Descendants().OfType<Text>().ToArray())
        {
            if (text.Data.IndexOf('|', StringComparison.Ordinal) is >= 0 and var index)
            {
                text.DeleteData((uint)index, 1);
                document.GetSelection()!.Collapse(text, (uint)index);
            }
        }

        return (document, host);
    }

    private static string DescribeSelection(Document document)
    {
        var selection = document.GetSelection()!;
        return $"{selection.AnchorNode?.NodeName}:{selection.AnchorOffset}-{selection.FocusNode?.NodeName}:{selection.FocusOffset}";
    }

    [Theory]
    [InlineData("<p>abc|def</p>", "insertParagraph", "")]
    [InlineData("<p>|abcdef</p>", "insertParagraph", "")]
    [InlineData("<ul><li>abc|def</li></ul>", "insertParagraph", "")]
    [InlineData("<p>abc|def</p>", "insertLineBreak", "")]
    [InlineData("<p>abc</p><p>|def</p>", "delete", "")]
    [InlineData("<p>abc|</p><p>def</p>", "forwardDelete", "")]
    [InlineData("<p>abc|def</p>", "insertText", "X")]
    [InlineData("<p>abc|def</p>", "insertHTML", "<b>x</b><i>y</i>")]
    [InlineData("<p>abc|def</p>", "insertOrderedList", "")]
    [InlineData("<p>abc|def</p>", "formatBlock", "h1")]
    [InlineData("<p>abc|def</p>", "justifyCenter", "")]
    public void UndoRestoresTheContent_RedoAppliesTheCommandAgain(string html, string command, string value)
    {
        var (document, host) = Load(html);
        var initial = host.InnerHtml;
        var initialSelection = DescribeSelection(document);
        Assert.False(document.QueryCommandEnabled("undo"));
        Assert.True(document.ExecCommand(command, showUI: false, value));
        var edited = host.InnerHtml;
        var editedSelection = DescribeSelection(document);
        Assert.NotEqual(initial, edited);
        Assert.True(document.QueryCommandEnabled("undo"));
        Assert.False(document.QueryCommandEnabled("redo"));

        Assert.True(document.ExecCommand("undo"));
        Assert.Equal(initial, host.InnerHtml);
        Assert.Equal(initialSelection, DescribeSelection(document));
        Assert.False(document.ExecCommand("undo"));

        Assert.True(document.ExecCommand("redo"));
        Assert.Equal(edited, host.InnerHtml);
        Assert.Equal(editedSelection, DescribeSelection(document));
        Assert.False(document.ExecCommand("redo"));
    }

    [Fact]
    public void Undo_RevertsTheCommandsInReverseOrder_AndANewCommandClearsTheRedoHistory()
    {
        var (document, host) = Load("<p>a|</p>");
        document.ExecCommand("insertText", showUI: false, "b");
        document.ExecCommand("bold");
        document.ExecCommand("insertText", showUI: false, "c");
        Assert.Equal("<p>ab<b>c</b></p>", host.InnerHtml);

        Assert.True(document.ExecCommand("undo"));
        Assert.Equal("<p>ab</p>", host.InnerHtml);
        Assert.True(document.ExecCommand("undo"));
        Assert.Equal("<p>a</p>", host.InnerHtml);
        Assert.True(document.ExecCommand("redo"));
        Assert.Equal("<p>ab</p>", host.InnerHtml);

        document.ExecCommand("insertText", showUI: false, "d");
        Assert.Equal("<p>abd</p>", host.InnerHtml);
        Assert.False(document.QueryCommandEnabled("redo"));
    }

    [Fact]
    public void Undo_FiresInputAtTheEditingHost_AndBeforeInputForTheUser()
    {
        var (document, host) = Load("<p>a|</p>");
        var events = new List<string>();
        foreach (var type in new[] { "beforeinput", "input" })
        {
            host.AddEventListener(type, e => events.Add($"{type} {((InputEvent)e).InputType}"));
        }

        Editing.UserEdit(host, "insertText", "b");
        events.Clear();

        // The command fires no beforeinput event
        Assert.True(document.ExecCommand("undo"));
        Assert.Equal(["input historyUndo"], events);
        events.Clear();

        // The undo and the redo of the user fire beforeinput, which can be canceled
        Assert.True(Editing.UndoOrRedo(document, undo: false, userTriggered: true));
        Assert.Equal("<p>ab</p>", host.InnerHtml);
        Assert.Equal(["beforeinput historyRedo", "input historyRedo"], events);

        host.AddEventListener("beforeinput", e => e.PreventDefault());
        Assert.False(Editing.UndoOrRedo(document, undo: true, userTriggered: true));
        Assert.Equal("<p>ab</p>", host.InnerHtml);
    }

    [Fact]
    public void Undo_AfterAMutationByAScript_RevertsTheRecordedNodes()
    {
        var (document, host) = Load("<div>abc|def</div><p>ghi</p>");
        document.ExecCommand("insertParagraph");
        Assert.Equal("<div>abc</div><div>def</div><p>ghi</p>", host.InnerHtml);

        // The new paragraph is moved by a script: undoing removes it from its new parent and restores the text
        host.AppendChild(host.ChildNodes[1]);
        Assert.Equal("<div>abc</div><p>ghi</p><div>def</div>", host.InnerHtml);
        Assert.True(document.ExecCommand("undo"));
        Assert.Equal("<div>abcdef</div><p>ghi</p>", host.InnerHtml);
    }

    [Fact]
    public void Redo_AfterAMutationByAScript_InsertsBeforeTheRecordedSiblingOrAtTheEnd()
    {
        var (document, host) = Load("<b>abc</b><i>def</i>");
        document.GetSelection()!.Collapse(host, 1);
        document.ExecCommand("insertLineBreak");
        Assert.Equal("<b>abc</b><br><i>def</i>", host.InnerHtml);
        document.ExecCommand("undo");

        // The next sibling of the inserted node was removed: the node is appended
        host.RemoveChild(host.LastChild!);
        Assert.True(document.ExecCommand("redo"));
        Assert.Equal("<b>abc</b><br>", host.InnerHtml);
    }

    [Fact]
    public void UndoShortcut_InEditableContent()
    {
        var (document, host) = Load("<p>a|</p>");
        ((HtmlElement)host).Focus();
        document.GetSelection()!.Collapse(host.FirstChild!.FirstChild, 1);
        Editing.UserEdit(host, "insertText", "b");
        Assert.Equal("<p>ab</p>", host.InnerHtml);

        static void Press(Element target, string key, bool shift = false)
            => EventDispatcher.Dispatch(new KeyboardEvent("keydown", new KeyboardEventInit { Key = key, CtrlKey = true, ShiftKey = shift, Bubbles = true, Cancelable = true }) { IsTrusted = true }, target);

        Press(host, "z");
        Assert.Equal("<p>a</p>", host.InnerHtml);
        Press(host, "y");
        Assert.Equal("<p>ab</p>", host.InnerHtml);

        // The formatting shortcuts run their commands
        document.GetSelection()!.SelectAllChildren(host.FirstChild!);
        Press(host, "b");
        Assert.Equal("<p><b>ab</b></p>", host.InnerHtml);
        Press(host, "a");
        Assert.Equal("ab", document.GetSelection()!.ToString());
    }

    [Fact]
    public void Undo_NotEnabledWithoutAnEditableSelection()
    {
        var (document, host) = Load("<p>a|</p>");
        document.ExecCommand("insertText", showUI: false, "b");
        Assert.True(document.QueryCommandEnabled("undo"));

        document.GetSelection()!.Collapse(document.QuerySelector("#host + p")!.FirstChild, 0);
        Assert.False(document.QueryCommandEnabled("undo"));
        Assert.False(document.ExecCommand("undo"));
        Assert.Equal("<p>ab</p>", host.InnerHtml);
    }
}
