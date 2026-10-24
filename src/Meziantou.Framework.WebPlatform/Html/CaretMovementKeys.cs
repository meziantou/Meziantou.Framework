using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.Unicode;

namespace Meziantou.Framework.WebPlatform.Html;

/// <summary>
/// The default actions of the caret movement keys (the arrow keys, Home and End) in the editable content and in the text
/// controls, with the key bindings of the browsers on Windows and Linux (<c>navigator.platform</c> is empty): Control moves
/// by words (ArrowLeft, ArrowRight) or paragraphs (ArrowUp, ArrowDown), or to the start or end of the content (Home, End),
/// and Shift extends the selection. In editable content, the selection moves as with <see cref="Selection.Modify"/>; in a
/// text control, its selection moves in its value (the lines of a textarea are its line feeds, without wrapping).
/// </summary>
internal static class CaretMovementKeys
{
    /// <summary>Runs the default action of a trusted keydown event. Returns whether the key moved the caret.</summary>
    public static bool Process(Node target, KeyboardEvent keyboardEvent)
    {
        if (keyboardEvent.AltKey || keyboardEvent.MetaKey || keyboardEvent.Key is not ("ArrowLeft" or "ArrowRight" or "ArrowUp" or "ArrowDown" or "Home" or "End"))
            return false;

        var extend = keyboardEvent.ShiftKey;
        var control = keyboardEvent.CtrlKey;
        if (target is HtmlTextAreaElement or HtmlInputElement)
            return MoveInTextControl((HtmlElement)target, keyboardEvent.Key, extend, control);

        var document = target.NodeDocument;
        if (Editing.GetEditingHost(target) is null || document.GetSelection() is not { RangeCount: > 0 } selection)
            return false;

        var (direction, granularity) = keyboardEvent.Key switch
        {
            "ArrowLeft" => ("left", control ? "word" : "character"),
            "ArrowRight" => ("right", control ? "word" : "character"),
            "ArrowUp" => ("backward", control ? "paragraph" : "line"),
            "ArrowDown" => ("forward", control ? "paragraph" : "line"),
            "Home" => ("backward", control ? "documentboundary" : "lineboundary"),
            _ => ("forward", control ? "documentboundary" : "lineboundary"),
        };

        var caret = extend && selection is { IsCollapsed: true, AnchorNode: { } anchorNode } ? (Node: anchorNode, Offset: selection.AnchorOffset) : ((Node Node, uint Offset)?)null;
        selection.Modify(extend ? "extend" : "move", direction, granularity);
        if (caret is { } start && selection is { IsCollapsed: false, AnchorNode: { } newAnchor, FocusNode: { } newFocus })
        {
            // https://w3c.github.io/selection-api/#selectstart-event: the key extends the selection from a caret: selectstart is
            // fired before the selection changes, and the selection does not change when the event is canceled
            var (anchorOffset, focusOffset) = (selection.AnchorOffset, selection.FocusOffset);
            selection.Collapse(start.Node, start.Offset);
            if (Selection.FireSelectStart(start.Node))
            {
                selection.SetBaseAndExtent(newAnchor, anchorOffset, newFocus, focusOffset);
            }
        }

        return true;
    }

    /// <summary>Moves the selection of the value of a text control (an input whose type allows the entry of text, or a textarea).</summary>
    private static bool MoveInTextControl(HtmlElement element, string key, bool extend, bool control)
    {
        string value;
        int start;
        int end;
        string? direction;
        switch (element)
        {
            case HtmlTextAreaElement textArea:
                value = textArea.Value;
                start = (int)textArea.SelectionStart;
                end = (int)textArea.SelectionEnd;
                direction = textArea.SelectionDirection;
                break;

            case HtmlInputElement { SelectionStart: { } inputStart, SelectionEnd: { } inputEnd } input:
                value = input.Value;
                start = (int)inputStart;
                end = (int)inputEnd;
                direction = input.SelectionDirection;
                break;

            case IEditableTextControl { IsTextEditor: true } editable:
                // The caret of the inputs whose selection is not exposed (number and email) moves in the text they show
                value = editable.EditValue;
                start = editable.EditSelectionStart;
                end = editable.EditSelectionEnd;
                direction = editable.EditSelectionDirection;
                break;

            default:
                return false;
        }

        start = Math.Min(start, value.Length);
        end = Math.Min(end, value.Length);
        var isTextArea = element is HtmlTextAreaElement;
        var backwardSelection = direction == "backward";
        var focus = backwardSelection ? start : end;
        var anchor = backwardSelection ? end : start;
        int newFocus;
        switch (key)
        {
            case "ArrowLeft" or "ArrowRight":
            {
                var forward = key == "ArrowRight";
                if (control)
                {
                    newFocus = WordBoundaries.Move(value, focus, forward);
                }
                else if (!extend && start != end)
                {
                    // Moving a selection by a character collapses it at its start (or its end)
                    newFocus = forward ? end : start;
                }
                else
                {
                    newFocus = forward ? GraphemeClusters.GetNextBoundary(value, focus) : GraphemeClusters.GetPreviousBoundary(value, focus);
                }

                break;
            }

            case "Home" or "End":
            {
                var forward = key == "End";
                if (control || !isTextArea)
                {
                    newFocus = forward ? value.Length : 0;
                }
                else
                {
                    newFocus = forward ? GetLineEnd(value, focus) : GetLineStart(value, focus);
                }

                break;
            }

            default:
            {
                // The lines of a textarea (a single-line text control does not move the caret)
                if (!isTextArea)
                    return false;

                var forward = key == "ArrowDown";
                var lineStart = GetLineStart(value, focus);
                var column = focus - lineStart;
                if (forward)
                {
                    var lineEnd = GetLineEnd(value, focus);
                    newFocus = lineEnd >= value.Length ? value.Length : Math.Min(lineEnd + 1 + column, GetLineEnd(value, lineEnd + 1));
                }
                else
                {
                    newFocus = lineStart == 0 ? 0 : Math.Min(GetLineStart(value, lineStart - 1) + column, lineStart - 1);
                }

                break;
            }
        }

        if (!extend)
        {
            anchor = newFocus;
        }

        var (newStart, newEnd) = anchor <= newFocus ? (anchor, newFocus) : (newFocus, anchor);
        var newDirection = newFocus < anchor ? "backward" : "forward";
        if (element is HtmlTextAreaElement area)
        {
            area.SetSelectionRange((uint)newStart, (uint)newEnd, newDirection);
        }
        else if (element is HtmlInputElement { SelectionStart: not null } input)
        {
            input.SetSelectionRange((uint)newStart, (uint)newEnd, newDirection);
        }
        else
        {
            ((IEditableTextControl)element).SetUserSelection(newStart, newEnd, newDirection);
        }

        // As in the browsers, the value is scrolled to reveal the caret
        Css.Layout.TextControlLayout.RevealCaret(element, newFocus);
        return true;

        static int GetLineStart(string value, int index) => index <= 0 ? 0 : value.LastIndexOf('\n', index - 1, StringComparison.Ordinal) + 1;

        static int GetLineEnd(string value, int index)
        {
            var lineFeed = value.IndexOf('\n', Math.Min(index, value.Length), StringComparison.Ordinal);
            return lineFeed < 0 ? value.Length : lineFeed;
        }
    }
}
