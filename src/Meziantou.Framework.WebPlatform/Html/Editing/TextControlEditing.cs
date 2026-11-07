using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.Unicode;

namespace Meziantou.Framework.WebPlatform.Html;

/// <summary>The value and the selection of a text control (a textarea, or an input whose type allows the entry of text) for the edits of the user and of the editing commands.</summary>
internal interface IEditableTextControl
{
    /// <summary>Whether the control is a text editor: a textarea, or an input in the Text, Search, Telephone, URL, Email, Password or Number state.</summary>
    bool IsTextEditor { get; }

    /// <summary>Whether the value can be edited: the control is not disabled and not read-only.</summary>
    bool IsEditMutable { get; }

    /// <summary>Whether the value has several lines (a textarea).</summary>
    bool IsMultiline { get; }

    /// <summary>Whether the value is obscured (the Password state): its text cannot be cut nor copied.</summary>
    bool IsPassword { get; }

    /// <summary>The text shown in the control: its API value, or the input of the user that the value sanitization changed (number and email).</summary>
    string EditValue { get; }

    /// <summary>The start of the selection in the API value (the caret of the types whose selection is not exposed, e.g. email).</summary>
    int EditSelectionStart { get; }

    int EditSelectionEnd { get; }

    string EditSelectionDirection { get; }

    /// <summary>The maximum length of the value in UTF-16 code units, or null (https://html.spec.whatwg.org/multipage/form-control-infrastructure.html#attr-fe-maxlength).</summary>
    int? EditMaxLength { get; }

    /// <summary>
    /// Sets the value and the selection as an edit of the user: the dirty value flag is set, and the selection is not reset.
    /// Returns the text shown in the control after the edit.
    /// </summary>
    string ApplyUserEdit(string value, int selectionStart, int selectionEnd, string direction);

    /// <summary>Sets the selection as the user does: selectionchange is fired, but no select event.</summary>
    void SetUserSelection(int selectionStart, int selectionEnd, string direction);
}

/// <summary>The undo history of the value of a text control: the values and the selections before and after each edit.</summary>
internal sealed class TextControlHistory
{
    public List<TextControlEdit> Undo { get; } = [];

    public List<TextControlEdit> Redo { get; } = [];
}

internal sealed record TextControlEdit(string ValueBefore, int StartBefore, int EndBefore, string DirectionBefore, string ValueAfter, int StartAfter, int EndAfter);

/// <summary>
/// The editing of the value of the text controls (<c>input</c>, <c>textarea</c>): the edits of the user at the selection
/// of the control (typing, deleting, pasting, cutting, undoing), with the <c>beforeinput</c> and <c>input</c> events of
/// Input Events (https://w3c.github.io/input-events/), and the editing commands of <c>document.execCommand()</c> while a
/// text control is focused, which edit its value and fire <c>input</c> only, as in the browsers (the editing
/// specification only defines the commands for the editable content). Each control has its own undo history, which the
/// changes of its value by the scripts clear.
/// </summary>
internal static class TextControlEditing
{
    /// <summary>The maximum number of edits that are kept in the history of a control.</summary>
    private const int MaximumHistoryDepth = 1000;

    /// <summary>The focused area of the document when it is a text control that is a text editor, else null: the commands of the document then apply to its value.</summary>
    public static HtmlElement? GetFocusedTextControl(Document document)
        => document.FocusedArea is HtmlElement element and IEditableTextControl { IsTextEditor: true } ? element : null;

    /// <summary>The commands that edit the value of a text control or its selection; the other commands are not enabled while a text control is focused.</summary>
    private static bool IsTextCommand(string command) => command is "copy" or "cut" or "paste" or "delete" or "forwarddelete" or "inserthtml" or "inserttext"
        or "insertlinebreak" or "insertparagraph" or "selectall" or "undo" or "redo";

    /// <summary>Whether a command (in lowercase) is enabled for the focused text control.</summary>
    public static bool IsEnabled(HtmlElement element, string command)
    {
        var control = (IEditableTextControl)element;
        var hasSelection = control.EditSelectionStart < control.EditSelectionEnd;

        // A control that is not rendered has no editor: its value cannot be edited
        var canEdit = CanBeEditedByUser(element);
        return command switch
        {
            "selectall" => true,
            "copy" => hasSelection && !control.IsPassword && IsClipboardAllowed(element, ClipboardAccess.Write),
            "cut" => hasSelection && !control.IsPassword && canEdit && IsClipboardAllowed(element, ClipboardAccess.Write),
            "paste" => canEdit && IsClipboardAllowed(element, ClipboardAccess.Read),
            "undo" => canEdit && GetHistory(element) is { Undo.Count: > 0 },
            "redo" => canEdit && GetHistory(element) is { Redo.Count: > 0 },
            "insertlinebreak" or "insertparagraph" => control.IsMultiline && canEdit,
            _ => IsTextCommand(command) && canEdit,
        };
    }

    private static bool IsClipboardAllowed(HtmlElement element, ClipboardAccess access)
        => element.NodeDocument is { RelevantWindow: { } window } document && ReferenceEquals(window.Document, document) && ClipboardPermissions.IsAllowed(window, access);

    /// <summary>
    /// Runs a command (in lowercase) on the focused text control: the value is edited at the selection and input is fired at
    /// the control (no beforeinput, as for the commands in editable content). Returns false when the command is not enabled.
    /// </summary>
    public static bool Execute(HtmlElement element, string command, string value)
    {
        if (!IsEnabled(element, command))
            return false;

        var document = element.NodeDocument;
        var control = (IEditableTextControl)element;
        switch (command)
        {
            case "copy":
                return ClipboardActions.Copy(document, scriptTriggered: true);

            case "cut":
                return ClipboardActions.Cut(document, scriptTriggered: true);

            case "paste":
                return ClipboardActions.Paste(document, scriptTriggered: true);

            case "selectall":
                SelectAll(element);
                return true;

            case "undo":
                return UndoOrRedo(element, undo: true, fireBeforeInput: false);

            case "redo":
                return UndoOrRedo(element, undo: false, fireBeforeInput: false);

            case "delete":
                Delete(element, forward: false, word: false);
                FireInputEvent(element, "deleteContentBackward", data: null);
                return true;

            case "forwarddelete":
                Delete(element, forward: true, word: false);
                FireInputEvent(element, "deleteContentForward", data: null);
                return true;

            case "insertlinebreak" or "insertparagraph":
                ReplaceSelection(element, "\n");
                FireInputEvent(element, command == "insertlinebreak" ? "insertLineBreak" : "insertParagraph", data: null);
                return true;

            case "inserthtml":
                // As in Chromium and WebKit, the text of the fragment is inserted
                value = element.NodeDocument.CreateRange().CreateContextualFragment(value).TextContent ?? "";
                goto default;

            default:
                ReplaceSelection(element, control.IsMultiline ? value : RemoveLineBreaks(value));
                FireInputEvent(element, "insertText", value);
                return true;
        }
    }

    /// <summary>Selects the whole value of the control (the selectAll command, and the Select All shortcut).</summary>
    public static void SelectAll(HtmlElement element)
    {
        switch (element)
        {
            case HtmlTextAreaElement textArea:
                textArea.SetSelectionRange(0, (uint)textArea.Value.Length, "none");
                break;

            case HtmlInputElement input:
                var control = (IEditableTextControl)input;
                var value = control.EditValue;
                if (input.SelectionStart is not null)
                {
                    input.SetSelectionRange(0, (uint)value.Length, "none");
                }
                else
                {
                    control.SetUserSelection(0, value.Length, "none");
                }

                break;
        }
    }

    /// <summary>Collapses the selection of a text control at the end of its value, without a select event (the caret of a control the user starts typing in, e.g. the element send keys of WebDriver).</summary>
    public static void MoveCaretToEnd(HtmlElement element)
    {
        if (element is IEditableTextControl { IsTextEditor: true } control)
        {
            var value = control.EditValue;
            if (control.EditSelectionStart != value.Length || control.EditSelectionEnd != value.Length)
            {
                control.SetUserSelection(value.Length, value.Length, "none");
            }
        }
    }

    /// <summary>
    /// An edit of the value of a text control by the user (e.g. a key press, a paste): beforeinput (cancelable) is fired at
    /// the control, the value is edited at its selection, and input is fired. The change is committed when the control
    /// loses the focus (its user validity becomes true, and change is fired, see <see cref="HtmlElement.CommitUserEdits"/>).
    /// Returns false when the element is not a text control that can be edited, or when nothing is edited.
    /// </summary>
    /// <param name="inputType">insertText, insertLineBreak, insertFromPaste, deleteByCut, deleteContentBackward, deleteContentForward, deleteWordBackward, deleteWordForward, historyUndo or historyRedo.</param>
    /// <param name="fireBeforeInput">False for the cut and paste actions that a script triggers (<c>document.execCommand()</c>): as the commands, they fire no beforeinput event.</param>
    public static bool UserEdit(Element target, string inputType, string? data, bool fireBeforeInput)
    {
        if (target is not HtmlElement element || !CanBeEditedByUser(element))
            return false;

        var control = (IEditableTextControl)element;
        if (inputType == "insertLineBreak" && !control.IsMultiline)
            return false;

        if (inputType is "historyUndo" or "historyRedo")
            return UndoOrRedo(element, undo: inputType == "historyUndo", fireBeforeInput);

        // https://w3c.github.io/input-events/#overview: the deletion of a selection that is not collapsed is a deletion of
        // content, whatever the granularity of the key
        var hasSelection = control.EditSelectionStart < control.EditSelectionEnd;
        if (hasSelection && inputType is "deleteWordBackward" or "deleteWordForward")
        {
            inputType = inputType == "deleteWordBackward" ? "deleteContentBackward" : "deleteContentForward";
        }

        var eventData = inputType is "insertText" or "insertFromPaste" ? data : null;
        // The listeners may make the control read-only, remove it or hide it
        if (fireBeforeInput && (!FireBeforeInputEvent(element, inputType, eventData) || !CanBeEditedByUser(element)))
            return false;

        switch (inputType)
        {
            case "deleteContentBackward" or "deleteContentForward" or "deleteWordBackward" or "deleteWordForward":
                if (!Delete(element, forward: inputType is "deleteContentForward" or "deleteWordForward", word: inputType is "deleteWordBackward" or "deleteWordForward"))
                    return false;

                break;

            case "deleteByCut":
                if (!ReplaceSelection(element, ""))
                    return false;

                break;

            case "insertLineBreak":
                ReplaceSelection(element, "\n");
                break;

            default:
                var text = data ?? "";
                if (!ReplaceSelection(element, control.IsMultiline ? text : RemoveLineBreaks(text)) && text.Length == 0)
                    return false;

                break;
        }

        RevealTextControl(element);
        FireInputEvent(element, inputType, eventData);
        return true;
    }

    /// <summary>Whether the user can edit the value of the element: a text editor that is mutable and, in a browsing context, being rendered.</summary>
    private static bool CanBeEditedByUser(HtmlElement element)
    {
        if (element is not IEditableTextControl { IsTextEditor: true, IsEditMutable: true })
            return false;

        return !element.IsConnected || element.NodeDocument.BrowsingContext is null || Css.Cascade.ComputedStyleDeclarations.IsBeingRendered(element);
    }

    /// <summary>
    /// Replaces the selected text of the control with a text (truncated so that the value is not longer than the maximum
    /// length of the control), collapses the selection after it and records the edit in the undo history. Returns false
    /// when the value does not change.
    /// </summary>
    private static bool ReplaceSelection(HtmlElement element, string text)
    {
        var control = (IEditableTextControl)element;
        var value = control.EditValue;
        var start = Math.Min(control.EditSelectionStart, value.Length);
        var end = Math.Clamp(control.EditSelectionEnd, start, value.Length);
        if (control.EditMaxLength is { } maxLength)
        {
            // https://html.spec.whatwg.org/multipage/form-control-infrastructure.html#attr-fe-maxlength: the user cannot make
            // the value longer than the maximum length (a surrogate pair is not split)
            var available = Math.Max(0, maxLength - (value.Length - (end - start)));
            if (text.Length > available)
            {
                if (available > 0 && char.IsHighSurrogate(text[available - 1]))
                {
                    available--;
                }

                text = text[..available];
            }
        }

        if (text.Length == 0 && start == end)
            return false;

        Edit(element, string.Concat(value.AsSpan(0, start), text, value.AsSpan(end)), start + text.Length);
        return true;
    }

    /// <summary>Deletes the selected text, or the character (a grapheme cluster) or the word before or after the caret. Returns false when nothing is deleted.</summary>
    private static bool Delete(HtmlElement element, bool forward, bool word)
    {
        var control = (IEditableTextControl)element;
        var value = control.EditValue;
        var start = Math.Min(control.EditSelectionStart, value.Length);
        var end = Math.Clamp(control.EditSelectionEnd, start, value.Length);
        if (start == end)
        {
            if (forward)
            {
                end = word ? WordBoundaries.Move(value, start, forward: true) : GraphemeClusters.GetNextBoundary(value, start);
            }
            else
            {
                // Backspace removes a code point: the last character of a cluster (e.g. a combining mark) is removed alone
                start = word ? WordBoundaries.Move(value, end, forward: false) : end - (end >= 2 && char.IsSurrogatePair(value[end - 2], value[end - 1]) ? 2 : Math.Min(end, 1));
            }

            start = Math.Clamp(start, 0, end);
            end = Math.Clamp(end, start, value.Length);
            if (start == end)
                return false;
        }

        Edit(element, string.Concat(value.AsSpan(0, start), value.AsSpan(end)), start);
        return true;
    }

    /// <summary>Sets the value of the control, with a caret, and records the edit in its undo history.</summary>
    private static void Edit(HtmlElement element, string newValue, int caret)
    {
        var control = (IEditableTextControl)element;
        var before = (Value: control.EditValue, Start: control.EditSelectionStart, End: control.EditSelectionEnd, Direction: control.EditSelectionDirection);
        element.ElementRareData.ValueBeforeUserEdit ??= before.Value;
        var sanitized = control.ApplyUserEdit(newValue, caret, caret, "none");
        caret = Math.Min(caret, sanitized.Length);

        var history = element.ElementRareData.TextEditHistory ??= new TextControlHistory();
        history.Redo.Clear();
        history.Undo.Add(new TextControlEdit(before.Value, before.Start, before.End, before.Direction, sanitized, caret, caret));
        if (history.Undo.Count > MaximumHistoryDepth)
        {
            history.Undo.RemoveAt(0);
        }
    }

    private static TextControlHistory? GetHistory(HtmlElement element) => (element.RareDataOrNull as ElementRareData)?.TextEditHistory;

    /// <summary>Reverts the last edit of the history of the control, or applies again the last edit that was reverted, and fires the events of the historyUndo or historyRedo input type.</summary>
    private static bool UndoOrRedo(HtmlElement element, bool undo, bool fireBeforeInput)
    {
        var inputType = undo ? "historyUndo" : "historyRedo";
        if (GetHistory(element) is not { } history || (undo ? history.Undo : history.Redo).Count == 0)
            return false;

        if (fireBeforeInput && (!FireBeforeInputEvent(element, inputType, data: null) || !CanBeEditedByUser(element)))
            return false;

        // The listeners of beforeinput may have changed the value, which clears the history
        var (source, destination) = undo ? (history.Undo, history.Redo) : (history.Redo, history.Undo);
        if (!ReferenceEquals(GetHistory(element), history) || source.Count == 0)
            return false;

        var edit = source[^1];
        source.RemoveAt(source.Count - 1);
        destination.Add(edit);
        var control = (IEditableTextControl)element;
        var value = control.EditValue;
        if (undo)
        {
            control.ApplyUserEdit(edit.ValueBefore, edit.StartBefore, edit.EndBefore, edit.DirectionBefore);
        }
        else
        {
            control.ApplyUserEdit(edit.ValueAfter, edit.StartAfter, edit.EndAfter, "none");
        }

        element.ElementRareData.ValueBeforeUserEdit ??= value;
        FireInputEvent(element, inputType, data: null);
        return true;
    }

    /// <summary>Clears the undo history of a text control: its value was changed by a script, or its type changed.</summary>
    public static void ClearHistory(HtmlElement element)
    {
        if (element.RareDataOrNull is ElementRareData { TextEditHistory: not null } rareData)
        {
            rareData.TextEditHistory = null;
        }
    }

    /// <summary>The value of a single-line text control has no line break: the line breaks of the inserted text are removed (the value sanitization algorithm strips them).</summary>
    private static string RemoveLineBreaks(string text)
        => text.AsSpan().ContainsAny('\n', '\r') ? text.Replace("\r", "", StringComparison.Ordinal).Replace("\n", "", StringComparison.Ordinal) : text;

    /// <summary>
    /// Reveals a text control edited by the user: as in the browsers, its scrolling boxes are scrolled when it is not in view
    /// (to its unshifted position when it is sticky positioned, WPT css/css-position/sticky/sticky-after-input.html). The
    /// control is revealed as a whole: the position of the caret in it is not used.
    /// </summary>
    private static void RevealTextControl(Element control)
    {
        if (control.NodeDocument is { BrowsingContext: not null } document && Css.Cascade.ComputedStyleDeclarations.IsBeingRendered(control))
        {
            document.GetLayoutEngine().ScrollIntoViewWhenLaidOut(control, ScrollLogicalPosition.Nearest, ScrollLogicalPosition.Nearest);
        }
    }

    private static bool FireBeforeInputEvent(HtmlElement element, string inputType, string? data)
        => element.FireEvent(new InputEvent("beforeinput", new InputEventInit { Bubbles = true, Cancelable = true, Composed = true, Data = data, InputType = inputType, View = element.NodeDocument.DefaultView }));

    private static void FireInputEvent(HtmlElement element, string inputType, string? data)
        => element.FireEvent(new InputEvent("input", new InputEventInit { Bubbles = true, Composed = true, Data = data, InputType = inputType, View = element.NodeDocument.DefaultView }));
}
