using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.Html;

namespace Meziantou.Framework.WebPlatform.Tests.Html;

/// <summary>The editing of the value of the text controls: the commands of <c>document.execCommand()</c> while a text control is focused, the edits of the user and the undo history of the control.</summary>
public sealed class TextControlEditingTests : IDisposable
{
    private readonly BrowsingContextScope _contexts = new();

    public void Dispose() => _contexts.Dispose();

    private Document Load(string html)
    {
        var document = _contexts.Create().Navigate("https://example.com/", _ => "<!DOCTYPE html>" + html);
        _contexts.EventLoop.RunUntilIdle();
        return document;
    }

    /// <summary>Loads a focused text control whose value and selection are given with the markers <c>[</c> and <c>]</c>.</summary>
    private (Document Document, HtmlElement Control, List<string> Events) LoadControl(string markup, string value)
    {
        var document = Load(markup);
        var control = (HtmlElement)document.QuerySelector("input, textarea")!;
        var start = value.IndexOf('[', StringComparison.Ordinal);
        var end = value.IndexOf(']', StringComparison.Ordinal) - 1;
        SetValue(control, value.Replace("[", "", StringComparison.Ordinal).Replace("]", "", StringComparison.Ordinal));
        control.Focus();
        if (control is HtmlTextAreaElement textArea)
        {
            textArea.SetSelectionRange((uint)start, (uint)end);
        }
        else
        {
            ((HtmlInputElement)control).SetSelectionRange((uint)start, (uint)end);
        }

        var events = new List<string>();
        foreach (var type in new[] { "beforeinput", "input", "change" })
        {
            document.AddEventListener(type, e => events.Add(e is InputEvent input ? $"{type} {input.InputType} {input.Data ?? "null"}" : type));
        }

        return (document, control, events);
    }

    private static void SetValue(HtmlElement control, string value)
    {
        if (control is HtmlTextAreaElement textArea)
        {
            textArea.Value = value;
        }
        else
        {
            ((HtmlInputElement)control).Value = value;
        }
    }

    /// <summary>The value of the control, with the markers of its selection.</summary>
    private static string Describe(HtmlElement control)
    {
        var (value, start, end) = control is HtmlTextAreaElement textArea
            ? (textArea.Value, (int)textArea.SelectionStart, (int)textArea.SelectionEnd)
            : (((HtmlInputElement)control).Value, (int)((HtmlInputElement)control).SelectionStart!.Value, (int)((HtmlInputElement)control).SelectionEnd!.Value);
        return value[..start] + "[" + value[start..end] + "]" + value[end..];
    }

    [Theory]
    [InlineData("<input>", "a[b]c", "insertText", "XY", "aXY[]c", "input insertText XY")]
    [InlineData("<input>", "a[b]c", "insertText", "", "a[]c", "input insertText ")]
    [InlineData("<input>", "ab[]c", "delete", "", "a[]c", "input deleteContentBackward null")]
    [InlineData("<input>", "a[b]c", "delete", "", "a[]c", "input deleteContentBackward null")]
    [InlineData("<input>", "a[]bc", "forwardDelete", "", "a[]c", "input deleteContentForward null")]
    [InlineData("<input>", "a[b]c", "insertHTML", "<b>in</b>serted", "ainserted[]c", "input insertText inserted")]
    [InlineData("<textarea></textarea>", "a[b]c", "insertParagraph", "", "a\n[]c", "input insertParagraph null")]
    [InlineData("<textarea></textarea>", "a[b]c", "insertLineBreak", "", "a\n[]c", "input insertLineBreak null")]
    [InlineData("<input type=password>", "a[b]c", "insertText", "X", "aX[]c", "input insertText X")]
    public void ExecCommand_EditsTheValueOfTheFocusedControl(string markup, string value, string command, string commandValue, string expected, string expectedEvent)
    {
        var (document, control, events) = LoadControl(markup, value);
        Assert.True(document.QueryCommandEnabled(command));
        Assert.True(document.ExecCommand(command, showUI: false, commandValue));
        Assert.Equal(expected, Describe(control));

        // As the commands in editable content, the commands fire no beforeinput event
        Assert.Equal([expectedEvent], events);
    }

    [Theory]
    [InlineData("<input>", "bold")]
    [InlineData("<input>", "insertParagraph")]
    [InlineData("<input>", "insertLineBreak")]
    [InlineData("<input>", "formatBlock")]
    [InlineData("<textarea></textarea>", "italic")]
    [InlineData("<textarea></textarea>", "insertOrderedList")]
    [InlineData("<input readonly>", "insertText")]
    [InlineData("<input readonly>", "delete")]
    [InlineData("<input style='display: none'>", "insertText")]
    public void ExecCommand_NotEnabledInATextControl(string markup, string command)
    {
        var (document, control, events) = LoadControl("<div contenteditable>" + markup + "</div>", "a[b]c");
        Assert.True(document.QueryCommandSupported(command));
        Assert.False(document.QueryCommandEnabled(command));
        Assert.False(document.ExecCommand(command, showUI: false, "x"));
        Assert.Equal("a[b]c", Describe(control));
        Assert.Empty(events);
    }

    [Fact]
    public void ExecCommand_SelectAll_SelectsTheValue()
    {
        var (document, control, events) = LoadControl("<p>text</p><input>", "a[]bc");
        Assert.True(document.ExecCommand("selectAll"));
        Assert.Equal("[abc]", Describe(control));
        Assert.Empty(events);
        Assert.True(document.GetSelection()!.IsCollapsed);
    }

    [Fact]
    public void ExecCommand_UndoAndRedo_UseTheHistoryOfTheControl()
    {
        var (document, control, events) = LoadControl("<input>", "a[b]c");
        Assert.False(document.QueryCommandEnabled("undo"));
        Assert.False(document.ExecCommand("undo"));

        Assert.True(document.ExecCommand("delete"));
        Assert.True(document.ExecCommand("insertText", showUI: false, "X"));
        Assert.Equal("aX[]c", Describe(control));
        Assert.False(document.QueryCommandEnabled("redo"));

        Assert.True(document.ExecCommand("undo"));
        Assert.Equal("a[]c", Describe(control));
        Assert.True(document.ExecCommand("undo"));
        Assert.Equal("a[b]c", Describe(control));
        Assert.False(document.ExecCommand("undo"));

        Assert.True(document.ExecCommand("redo"));
        Assert.Equal("a[]c", Describe(control));
        Assert.Equal(["input deleteContentBackward null", "input insertText X", "input historyUndo null", "input historyUndo null", "input historyRedo null"], events);

        // A new edit clears the edits that can be redone
        Assert.True(document.ExecCommand("insertText", showUI: false, "Y"));
        Assert.False(document.QueryCommandEnabled("redo"));
    }

    [Fact]
    public void ValueSetByScript_ClearsTheHistory()
    {
        var (document, control, _) = LoadControl("<input>", "a[b]c");
        Assert.True(document.ExecCommand("delete"));
        Assert.True(document.QueryCommandEnabled("undo"));
        ((HtmlInputElement)control).Value = "other";
        Assert.False(document.QueryCommandEnabled("undo"));

        Assert.True(document.ExecCommand("insertText", showUI: false, "X"));
        Assert.True(document.QueryCommandEnabled("undo"));
        ((HtmlInputElement)control).SetRangeText("Z");
        Assert.False(document.QueryCommandEnabled("undo"));
    }

    [Fact]
    public void UserEdit_TypesAtTheCaret_WithBeforeInputAndInput()
    {
        var (_, control, events) = LoadControl("<textarea></textarea>", "ab[]cd");
        Assert.True(Editing.UserEditTextControl(control, "insertText", "X"));
        Assert.True(Editing.UserEditTextControl(control, "insertLineBreak", "\n"));
        Assert.Equal("abX\n[]cd", Describe(control));
        Assert.True(Editing.UserEditTextControl(control, "deleteContentBackward", data: null));
        Assert.True(Editing.UserEditTextControl(control, "deleteContentForward", data: null));
        Assert.Equal("abX[]d", Describe(control));
        Assert.Equal(
            [
                "beforeinput insertText X", "input insertText X",
                "beforeinput insertLineBreak null", "input insertLineBreak null",
                "beforeinput deleteContentBackward null", "input deleteContentBackward null",
                "beforeinput deleteContentForward null", "input deleteContentForward null",
            ],
            events);

        // The change is committed when the control loses the focus
        control.Blur();
        Assert.Equal("change", events[^1]);
    }

    [Fact]
    public void UserEdit_DeletesWordsAndSelections()
    {
        var (_, control, events) = LoadControl("<input>", "one two[] three");
        Assert.True(Editing.UserEditTextControl(control, "deleteWordBackward", data: null));
        Assert.Equal("one [] three", Describe(control));

        // Nothing is deleted before the start of the value, but beforeinput is fired
        ((HtmlInputElement)control).SetSelectionRange(0, 0);
        events.Clear();
        Assert.False(Editing.UserEditTextControl(control, "deleteContentBackward", data: null));
        Assert.Equal(["beforeinput deleteContentBackward null"], events);

        // The deletion of a selection is a deletion of content, whatever the granularity
        ((HtmlInputElement)control).SetSelectionRange(0, 3);
        events.Clear();
        Assert.True(Editing.UserEditTextControl(control, "deleteWordForward", data: null));
        Assert.Equal("[]  three", Describe(control));
        Assert.Equal(["beforeinput deleteContentForward null", "input deleteContentForward null"], events);
    }

    [Fact]
    public void UserEdit_CanceledBeforeInput_DoesNotEdit()
    {
        var (document, control, events) = LoadControl("<input>", "ab[]c");
        document.AddEventListener("beforeinput", e => e.PreventDefault());
        Assert.False(Editing.UserEditTextControl(control, "insertText", "X"));
        Assert.Equal("ab[]c", Describe(control));
        Assert.Equal(["beforeinput insertText X"], events);
    }

    [Fact]
    public void UserEdit_HiddenByBeforeInput_DoesNotEdit()
    {
        var (document, control, _) = LoadControl("<input>", "ab[]c");
        document.AddEventListener("beforeinput", _ => control.SetAttribute("style", "display: none"));
        Assert.False(Editing.UserEditTextControl(control, "insertText", "X"));
        Assert.Equal("ab[]c", Describe(control));
    }

    [Fact]
    public void UserEdit_ReadOnlyControl_FiresNoEvent()
    {
        var (_, control, events) = LoadControl("<input readonly>", "ab[]c");
        Assert.False(Editing.UserEditTextControl(control, "insertText", "X"));
        Assert.Equal("ab[]c", Describe(control));
        Assert.Empty(events);
    }

    [Theory]
    [InlineData("<input maxlength=5>", "ab[]c", "XYZ", "abXY[]c")]
    [InlineData("<input maxlength=5>", "a[bc]d", "WXYZ", "aWXY[]d")]
    [InlineData("<input maxlength=3>", "ab[]c", "X", "ab[]c")]
    [InlineData("<textarea maxlength=4></textarea>", "ab[]", "X\nY", "abX\n[]")]
    [InlineData("<input>", "ab[]c", "X\nY", "abXY[]c")]
    public void UserEdit_InsertedTextIsLimitedByMaxLengthAndHasNoLineBreakInAnInput(string markup, string value, string text, string expected)
    {
        var (_, control, _) = LoadControl(markup, value);
        Editing.UserEditTextControl(control, "insertText", text);
        Assert.Equal(expected, Describe(control));
    }

    [Fact]
    public void UserEdit_UndoAndRedo_FireBeforeInput()
    {
        var (_, control, events) = LoadControl("<input>", "ab[]c");
        Assert.True(Editing.UserEditTextControl(control, "insertText", "X"));
        events.Clear();
        Assert.True(Editing.UserEditTextControl(control, "historyUndo", data: null));
        Assert.Equal("ab[]c", Describe(control));
        Assert.True(Editing.UserEditTextControl(control, "historyRedo", data: null));
        Assert.Equal("abX[]c", Describe(control));
        Assert.False(Editing.UserEditTextControl(control, "historyRedo", data: null));
        Assert.Equal(["beforeinput historyUndo null", "input historyUndo null", "beforeinput historyRedo null", "input historyRedo null"], events);
    }

    [Fact]
    public void Shortcuts_SelectAllAndUndo()
    {
        var (_, control, events) = LoadControl("<input>", "ab[]c");
        Assert.True(Editing.UserEditTextControl(control, "insertText", "X"));
        events.Clear();

        static void Press(Element target, string key, bool shift = false)
            => EventDispatcher.Dispatch(new KeyboardEvent("keydown", new KeyboardEventInit { Key = key, CtrlKey = true, ShiftKey = shift, Bubbles = true, Cancelable = true }) { IsTrusted = true }, target);

        Press(control, "z");
        Assert.Equal("ab[]c", Describe(control));
        Press(control, "Z", shift: true);
        Assert.Equal("abX[]c", Describe(control));
        Press(control, "a");
        Assert.Equal("[abXc]", Describe(control));
        Assert.Equal(["beforeinput historyUndo null", "input historyUndo null", "beforeinput historyRedo null", "input historyRedo null"], events);
    }

    [Fact]
    public void UserEdit_FiresSelectionChangeAtTheControl_ButNoSelectEvent()
    {
        var (document, control, _) = LoadControl("<input>", "hello[]");
        _contexts.EventLoop.RunUntilIdle();
        var events = new List<string>();
        control.AddEventListener("selectionchange", _ => events.Add("selectionchange@input"));
        control.AddEventListener("select", _ => events.Add("select@input"));
        document.AddEventListener("selectionchange", e => events.Add("selectionchange@document:" + (e.Target as Node)?.NodeName));

        Assert.True(Editing.UserEditTextControl(control, "deleteContentBackward", data: null));
        _contexts.EventLoop.RunUntilIdle();
        Assert.Equal("hell[]", Describe(control));
        Assert.Equal(["selectionchange@input", "selectionchange@document:INPUT"], events);
    }

    [Fact]
    public void EmailInput_IsEditedAtItsCaret()
    {
        var document = Load("<input type=email>");
        var email = (HtmlInputElement)document.QuerySelector("input")!;
        email.Value = "ab";
        email.Focus();
        Assert.True(Editing.UserEditTextControl(email, "insertText", "c"));
        Assert.True(Editing.UserEditTextControl(email, "deleteContentBackward", data: null));
        Assert.True(Editing.UserEditTextControl(email, "insertText", "d"));
        Assert.Equal("abd", email.Value);
        Assert.Null(email.SelectionStart);
    }

    [Fact]
    public void TextControlInEditingHost_TakesTheCommands()
    {
        var (document, control, _) = LoadControl("<div contenteditable>text <input contenteditable></div>", "a[]c");
        Assert.True(document.ExecCommand("insertText", showUI: false, "b"));
        Assert.Equal("ab[]c", Describe(control));
        Assert.Equal("text ", document.QuerySelector("div")!.TextContent);

        // The commands that set a state of the document do not depend on the focused control
        Assert.True(document.ExecCommand("styleWithCSS", showUI: false, "true"));
        Assert.True(document.QueryCommandState("styleWithCSS"));
    }
}
