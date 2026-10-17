using Meziantou.Framework.WebPlatform.Css.Cascade;
using Meziantou.Framework.WebPlatform.Css.Om;
using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.Unicode;
using DomRange = Meziantou.Framework.WebPlatform.Dom.Range;

namespace Meziantou.Framework.WebPlatform.Dom
{
    public partial class Document
    {
        /// <summary>The selection that the pressed primary button of the mouse is making in a document of this top-level document (see <see cref="Html.PointerSelection"/>), or null.</summary>
        internal Html.PointerSelection.State? PointerSelectionState { get; set; }
    }
}

namespace Meziantou.Framework.WebPlatform.Html
{
    /// <summary>
    /// The selection with the pointing device, the default action of the trusted mouse events (unspecified, as in the
    /// browsers): a press of the primary button puts the caret at the position of the pointer (the hit testing of
    /// <c>document.caretPositionFromPoint()</c>), or extends the selection to it with the Shift key; a double click selects
    /// the word at the position and a triple click the block; moving the pointer while the button is pressed extends the
    /// selection from the position of the press. In a text control, the selection of its value is set the same way. A press
    /// in the selection keeps it (it may be dragged), and collapses it when the button is released without a move.
    /// Nothing is selected from an element whose <c>user-select</c> is <c>none</c>, and <c>selectstart</c> (cancelable) is
    /// fired before a press changes the selection of the document.
    /// </summary>
    internal static class PointerSelection
    {
        /// <summary>The state of a selection that is being made: the anchor of the press, in the document or in the value of a text control.</summary>
        internal sealed class State(Document document)
        {
            public Document Document { get; } = document;

            public Node? AnchorNode { get; init; }

            public uint AnchorOffset { get; init; }

            public HtmlElement? TextControl { get; init; }

            /// <summary>Whether the press was in the selection: the selection is collapsed at the position when the button is released without a move.</summary>
            public bool InSelection { get; init; }

            /// <summary>The pressed element when the position of the press is not in a text (e.g. a replaced element): the selection is not extended while the pointer stays over it.</summary>
            public Element? PressedElement { get; init; }

            /// <summary>The focused area of the document once the press has moved the focus.</summary>
            public Element? FocusedArea { get; set; }
        }

        /// <summary>
        /// The default action of a mousedown event that was not canceled. The middle and the secondary buttons only move the
        /// caret (or extend the selection with the Shift key): they select no word, and their moves do not extend the selection.
        /// </summary>
        public static void OnMouseDown(Document topDocument, MouseEvent mouseEvent, Node target)
        {
            var primary = mouseEvent.Button == 0;
            if (primary)
            {
                topDocument.PointerSelectionState = null;
            }

            var document = target.NodeDocument;
            if (mouseEvent.IsPositionless || !target.IsConnected || document.BrowsingContext is null || (target as Element ?? target.ParentElement) is not { } element)
                return;

            if (IsUserSelectNone(element) || IsDraggableOrButton(element) || CaretPositions.FromPoint(document, mouseEvent.ClientX, mouseEvent.ClientY) is not var (node, offset, _))
                return;

            // The pressed element when the position is not in a text (e.g. a replaced element)
            var pressedElement = node is Text ? null : element;
            (node, offset) = Canonicalize(node, offset);

            var clickCount = primary ? mouseEvent.Detail : 1;

            // As in the browsers, the Shift key does not extend the selection to a link (the press collapses the selection in it)
            var extend = mouseEvent.ShiftKey && !IsInLink(element);
            if (node is HtmlElement control and IEditableTextControl { IsTextEditor: true } textControl)
            {
                var value = textControl.EditValue;
                var position = (int)Math.Min(offset, (uint)value.Length);
                if (clickCount >= 3)
                {
                    SelectInTextControl(textControl, 0, value.Length, "none");
                }
                else if (clickCount == 2)
                {
                    var (start, end) = GetWord(value, position);
                    SelectInTextControl(textControl, start, end, "none");
                }
                else if (extend)
                {
                    ExtendInTextControl(textControl, GetAnchor(textControl), position);
                }
                else
                {
                    textControl.SetUserSelection(position, position, "none");
                }

                if (primary)
                {
                    topDocument.PointerSelectionState = new State(document) { TextControl = control, AnchorOffset = (uint)(extend ? GetAnchor(textControl) : position) };
                }

                return;
            }

            if (node is IEditableTextControl || document.GetSelection() is not { } selection)
                return;

            // A press in an area without content (the position is the start of the root element): nothing is selected anymore
            if (ReferenceEquals(node, document.DocumentElement) && offset == 0 && !Editing.IsEditingHost(node))
            {
                if (primary && !mouseEvent.ShiftKey && selection.RangeOrNull is not null && Selection.FireSelectStart(node))
                {
                    selection.RemoveAllRanges();
                }

                return;
            }

            if (clickCount >= 3)
            {
                if (GetBlock(node) is { } block && Selection.FireSelectStart(node))
                {
                    selection.SelectDirectionless(block, 0, block, DomRange.GetNodeLength(block));
                }

                return;
            }

            if (clickCount == 2)
            {
                if (node is Text text && Selection.FireSelectStart(node))
                {
                    var (start, end) = GetWord(text.Data, (int)offset);
                    selection.SelectDirectionless(text, (uint)start, text, (uint)end);
                }

                return;
            }

            if (extend && selection is { RangeOrNull: not null, AnchorNode: { } anchorNode })
            {
                if (ReferenceEquals(anchorNode.GetRoot(), node.GetRoot()) && (!selection.IsCollapsed || Selection.FireSelectStart(anchorNode)))
                {
                    selection.SetBaseAndExtent(anchorNode, selection.AnchorOffset, node, offset);
                    if (primary)
                    {
                        topDocument.PointerSelectionState = new State(document) { AnchorNode = anchorNode, AnchorOffset = selection.AnchorOffset };
                    }
                }

                return;
            }

            // A press in the selection does not change it: the selection may be dragged (or is the subject of the context menu)
            if (selection.RangeOrNull is { Collapsed: false } range && ReferenceEquals(range.StartContainer.GetRoot(), node.GetRoot()) && range.IsPointInRange(node, offset)
                && !IsBoundary(range, node, offset))
            {
                if (primary)
                {
                    topDocument.PointerSelectionState = new State(document) { AnchorNode = node, AnchorOffset = offset, InSelection = true };
                }

                return;
            }

            if (!Selection.FireSelectStart(node) || !node.IsConnected)
                return;

            selection.Collapse(node, Math.Min(offset, DomRange.GetNodeLength(node)));
            if (primary)
            {
                topDocument.PointerSelectionState = new State(document) { AnchorNode = node, AnchorOffset = offset, PressedElement = pressedElement };
            }
        }

        /// <summary>The default action of a mousemove event while the primary button is pressed: the selection is extended to the position of the pointer.</summary>
        public static void OnMouseMove(Document topDocument, MouseEvent mouseEvent, Node target)
        {
            if (topDocument.PointerSelectionState is not { } state)
                return;

            if ((mouseEvent.Buttons & 1) == 0)
            {
                topDocument.PointerSelectionState = null;
                return;
            }

            if (state.InSelection)
            {
                // The move drags the selection (see the drag-and-drop operation): the selection is kept
                topDocument.PointerSelectionState = null;
                return;
            }

            if (ReferenceEquals(target, state.PressedElement) || !ReferenceEquals(target.NodeDocument, state.Document) || mouseEvent.IsPositionless || state.Document.BrowsingContext is null
                || CaretPositions.FromPoint(state.Document, mouseEvent.ClientX, mouseEvent.ClientY) is not var (node, offset, _))
            {
                return;
            }

            (node, offset) = Canonicalize(node, offset);

            if (state.TextControl is { } control)
            {
                if (ReferenceEquals(node, control) && control is IEditableTextControl { IsTextEditor: true } textControl)
                {
                    ExtendInTextControl(textControl, (int)state.AnchorOffset, (int)Math.Min(offset, (uint)textControl.EditValue.Length));
                }

                return;
            }

            if (state.AnchorNode is not { IsConnected: true } anchorNode || state.Document.GetSelection() is not { } selection)
                return;

            // The selection ends when a text control gets the focus while it is made (e.g. a listener of selectionchange focuses it)
            if (state.Document.FocusedArea is IEditableTextControl { IsTextEditor: true } focused && !ReferenceEquals(focused, state.FocusedArea))
            {
                topDocument.PointerSelectionState = null;
                return;
            }

            // The selection that starts in an editing host does not leave it: it extends to the start or to the end of the host
            if (Editing.GetEditingHost(anchorNode) is { } editingHost && !editingHost.IsInclusiveAncestorOf(node))
            {
                if (!ReferenceEquals(editingHost.GetRoot(), node.GetRoot()))
                    return;

                var after = AbstractRange.ComparePoints(node, offset, editingHost, 0) == AbstractRange.BoundaryPointPosition.After;
                (node, offset) = (editingHost, after ? DomRange.GetNodeLength(editingHost) : 0);
                (node, offset) = Canonicalize(node, offset);
            }
            else if (node is IEditableTextControl || !ReferenceEquals(anchorNode.GetRoot(), node.GetRoot()) || (node as Element ?? node.ParentElement) is not { } element || IsUserSelectNone(element))
            {
                // The selection does not extend into the content that cannot be selected, nor into another tree
                return;
            }

            selection.SetBaseAndExtent(anchorNode, Math.Min(state.AnchorOffset, DomRange.GetNodeLength(anchorNode)), node, offset);
        }

        /// <summary>The default action of a mouseup event of the primary button: the selection that is being made ends.</summary>
        public static void OnMouseUp(Document topDocument)
        {
            if (topDocument.PointerSelectionState is not { } state)
                return;

            topDocument.PointerSelectionState = null;
            if (state is { InSelection: true, AnchorNode: { IsConnected: true } node } && state.Document.GetSelection() is { } selection && Selection.FireSelectStart(node))
            {
                selection.Collapse(node, Math.Min(state.AnchorOffset, DomRange.GetNodeLength(node)));
            }
        }

        private static bool IsInLink(Element element)
        {
            for (var current = element; current is not null; current = current.ParentElement)
            {
                if (current.IsHtml(TagId.A) && current.HasAttribute("href"))
                    return true;
            }

            return false;
        }

        private static bool IsBoundary(DomRange range, Node node, uint offset)
            => (ReferenceEquals(range.StartContainer, node) && range.StartOffset == offset) || (ReferenceEquals(range.EndContainer, node) && range.EndOffset == offset);

        /// <summary>The end of the selection of a text control that does not move when the selection is extended.</summary>
        private static int GetAnchor(IEditableTextControl control) => control.EditSelectionDirection == "backward" ? control.EditSelectionEnd : control.EditSelectionStart;

        private static void ExtendInTextControl(IEditableTextControl control, int anchor, int focus)
            => SelectInTextControl(control, Math.Min(anchor, focus), Math.Max(anchor, focus), focus < anchor ? "backward" : focus > anchor ? "forward" : "none");

        /// <summary>Sets the selection of a text control; the select event is fired when the user selects text (https://html.spec.whatwg.org/multipage/indices.html#event-select).</summary>
        private static void SelectInTextControl(IEditableTextControl control, int start, int end, string direction)
        {
            if (start == control.EditSelectionStart && end == control.EditSelectionEnd)
                return;

            control.SetUserSelection(start, end, direction);
            if (start != end && control is HtmlElement element)
            {
                element.NodeDocument.QueueElementTask(() => element.FireEvent(new Event("select", new EventInit { Bubbles = true })));
            }
        }

        /// <summary>The word (or the run of the characters between two words) of a text at an index: the segment between the word boundaries of UAX #29 around it.</summary>
        private static (int Start, int End) GetWord(string text, int index)
        {
            if (text.Length == 0)
                return (0, 0);

            var boundaries = new bool[text.Length];
            WordBoundaries.GetBoundaries(text, boundaries);

            // The position at the end of the text is in its last word
            var start = Math.Min(index, text.Length - 1);
            while (start > 0 && !boundaries[start])
            {
                start--;
            }

            var end = start + 1;
            while (end < text.Length && !boundaries[end])
            {
                end++;
            }

            return (start, end);
        }

        /// <summary>
        /// Whether a press on the element does not select: it begins a drag-and-drop operation of a draggable inclusive
        /// ancestor (as <see cref="DragAndDropOperation.TryStart"/>: not in a text control or editable content), or it presses
        /// a form control that is not a text control (as in the browsers, the text of a button is not selected from inside,
        /// and dragging the thumb of a slider selects nothing).
        /// </summary>
        private static bool IsDraggableOrButton(Element element)
        {
            for (var current = element; current is not null; current = current.ParentElement)
            {
                if (current is IEditableTextControl { IsTextEditor: true } or HtmlElement { IsContentEditable: true })
                    return false;

                if (current is HtmlElement { Draggable: true } or HtmlButtonElement or HtmlInputElement or HtmlSelectElement)
                    return true;
            }

            return false;
        }

        /// <summary>
        /// The position in the text that is at a position between the children of an element (e.g. the position of a point of
        /// the generated content of the element, or of its padding): the start of the text that starts the child after the
        /// position, else the end of the text that ends the child before it, as the browsers canonicalize the caret.
        /// </summary>
        private static (Node Node, uint Offset) Canonicalize(Node node, uint offset)
        {
            if (node is not Element element || node is IEditableTextControl)
                return (node, offset);

            for (var child = offset < element.ChildNodes.Length ? element.ChildNodes[(int)offset] : null; child is not null; child = child.FirstChild)
            {
                if (child is Text text)
                    return (text, 0);

                if (child is not HtmlElement { IsContentEditable: var editable } || editable != (element as HtmlElement)?.IsContentEditable || child is IEditableTextControl)
                    break;
            }

            for (var child = offset > 0 && offset <= element.ChildNodes.Length ? element.ChildNodes[(int)offset - 1] : null; child is not null; child = child.LastChild)
            {
                if (child is Text text)
                    return (text, text.Length);

                if (child is not HtmlElement { IsContentEditable: var editable } || editable != (element as HtmlElement)?.IsContentEditable || child is IEditableTextControl)
                    break;
            }

            return (node, offset);
        }

        /// <summary>The closest inclusive ancestor element of a node that is not inline-level (the block that a triple click selects).</summary>
        private static Element? GetBlock(Node node)
        {
            var engine = node.NodeDocument.GetStyleEngine();
            for (var element = node as Element ?? node.ParentElement; element is not null; element = element.ParentElement)
            {
                var display = engine.GetStyle(element).Layout.Display;
                if (!display.IsNone && !display.IsInlineLevel)
                    return element;
            }

            return null;
        }

        /// <summary>
        /// Whether the used value of <c>user-select</c> of an element is <c>none</c> (https://drafts.csswg.org/css-ui-4/#content-selection):
        /// its computed value, or for <c>auto</c> the used value of its parent when it is <c>none</c> (an editable element is <c>contain</c>).
        /// </summary>
        private static bool IsUserSelectNone(Element element)
        {
            var engine = element.NodeDocument.GetStyleEngine();
            for (var current = element; current is not null; current = StyleEngine.GetInheritanceParent(current))
            {
                // The used value of an editable element is contain, whatever its computed value
                if (Editing.IsEditingHost(current) || current is IEditableTextControl { IsTextEditor: true })
                    return false;

                var value = engine.GetStyle(current).GetComputedValue("user-select");
                if (value is not ("auto" or ""))
                    return value == "none";
            }

            return false;
        }
    }
}
