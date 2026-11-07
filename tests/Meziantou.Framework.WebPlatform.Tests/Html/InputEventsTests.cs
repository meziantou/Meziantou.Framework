using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.Html;

namespace Meziantou.Framework.WebPlatform.Tests.Html;

/// <summary>The events of the user edits (https://w3c.github.io/input-events/).</summary>
public sealed class InputEventsTests : IDisposable
{
    private readonly BrowsingContextScope _contexts = new();

    public void Dispose() => _contexts.Dispose();

    private Document Load(string html)
    {
        var document = _contexts.Create().Navigate("https://example.com/", _ => html);
        _contexts.EventLoop.RunUntilIdle();
        return document;
    }

    private static string Describe(StaticRange range) => $"{range.StartContainer.NodeName}:{range.StartOffset}-{range.EndContainer.NodeName}:{range.EndOffset}";

    private static List<string> RecordInputEvents(Element target)
    {
        var events = new List<string>();
        foreach (var type in new[] { "beforeinput", "input", "change" })
        {
            target.AddEventListener(type, e => events.Add(e is InputEvent input
                ? $"{type} {input.InputType} {input.Data ?? "null"} [{string.Join(",", input.GetTargetRanges().Select(Describe))}]"
                : type));
        }

        return events;
    }

    private static void PressKey(Element target, string key)
    {
        var @event = new KeyboardEvent("keydown", new KeyboardEventInit { Key = key, Bubbles = true, Cancelable = true }) { IsTrusted = true };
        EventDispatcher.Dispatch(@event, target);
    }

    [Fact]
    public void UserEdit_DeletionAtACaret_TargetRangeIsThePreviousCharacter()
    {
        var document = Load("<div id=host contenteditable>hello world</div>");
        var host = (HtmlElement)document.GetElementById("host")!;
        var events = RecordInputEvents(host);
        host.Focus();
        document.GetSelection()!.Collapse(host.FirstChild, 5);

        Editing.UserEdit(host, "deleteContentBackward", data: null);
        Assert.Equal("hell world", host.TextContent);
        Assert.Equal(["beforeinput deleteContentBackward null [#text:4-#text:5]", "input deleteContentBackward null []"], events);
    }

    [Fact]
    public void UserEdit_WordDeletion()
    {
        var document = Load("<div id=host contenteditable>hello world</div>");
        var host = (HtmlElement)document.GetElementById("host")!;
        var events = RecordInputEvents(host);
        host.Focus();
        var selection = document.GetSelection()!;
        selection.Collapse(host.FirstChild, 11);

        Editing.UserEdit(host, "deleteWordBackward", data: null);
        Assert.Equal("hello ", host.TextContent!.Replace((char)0xA0, ' '));
        Assert.Equal(["beforeinput deleteWordBackward null [#text:6-#text:11]", "input deleteWordBackward null []"], events);

        // The deletion of a selection is a deletion of content
        events.Clear();
        selection.SetBaseAndExtent(host.FirstChild!, 0, host.FirstChild!, 2);
        Editing.UserEdit(host, "deleteWordBackward", data: null);
        Assert.Equal(["beforeinput deleteContentBackward null [#text:0-#text:2]", "input deleteContentBackward null []"], events);
    }

    [Fact]
    public void UserEdit_TargetRangesAreClearedAfterTheDispatch()
    {
        var document = Load("<div id=host contenteditable>hello</div>");
        var host = (HtmlElement)document.GetElementById("host")!;
        InputEvent? beforeInput = null;
        host.AddEventListener("beforeinput", e => beforeInput = (InputEvent)e);
        host.Focus();
        document.GetSelection()!.Collapse(host.FirstChild, 5);

        Editing.UserEdit(host, "insertText", "!");
        Assert.Equal("hello!", host.TextContent);
        Assert.Empty(beforeInput!.GetTargetRanges());
    }

    [Fact]
    public void UserEdit_Paste_TheDataIsInTheDataTransfer()
    {
        var document = Load("<div id=host contenteditable>hello</div>");
        var host = (HtmlElement)document.GetElementById("host")!;
        var events = new List<string>();
        foreach (var type in new[] { "beforeinput", "input" })
        {
            host.AddEventListener(type, e => events.Add($"{type} {((InputEvent)e).Data ?? "null"} {((InputEvent)e).DataTransfer?.GetData("text/plain")}"));
        }

        host.Focus();
        document.GetSelection()!.Collapse(host.FirstChild, 5);
        Editing.UserEdit(host, "insertFromPaste", " world");
        Assert.Equal("hello world", host.TextContent!.Replace((char)0xA0, ' '));
        Assert.Equal(["beforeinput null  world", "input null  world"], events);
    }

    [Fact]
    public void NumberInput_ArrowKeysStepTheValue()
    {
        var document = Load("<input id=number type=number value=5 max=6>");
        var input = (HtmlInputElement)document.GetElementById("number")!;
        var events = RecordInputEvents(input);
        input.Focus();

        PressKey(input, "ArrowUp");
        Assert.Equal("6", input.Value);
        Assert.Equal(["beforeinput insertReplacementText 6 []", "input insertReplacementText 6 []", "change"], events);

        // The value is at its maximum
        events.Clear();
        PressKey(input, "ArrowUp");
        Assert.Empty(events);

        // beforeinput cancels the change
        input.AddEventListener("beforeinput", e => e.PreventDefault());
        PressKey(input, "ArrowDown");
        Assert.Equal("6", input.Value);
        Assert.Equal(["beforeinput insertReplacementText 5 []"], events);
    }

    [Fact]
    public void SelectionToString_ReturnsTheSelectedTextOfTheFocusedTextControl()
    {
        var document = Load("<p>text</p><textarea id=area>hello world</textarea>");
        var area = (HtmlTextAreaElement)document.GetElementById("area")!;
        area.Focus();
        area.SetSelectionRange(0, 5);
        Assert.Equal("hello", document.GetSelection()!.ToString());

        area.Blur();
        Assert.Equal("", document.GetSelection()!.ToString());
    }
}
