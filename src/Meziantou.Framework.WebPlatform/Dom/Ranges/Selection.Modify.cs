using Meziantou.Framework.WebPlatform.Css.Layout;
using Meziantou.Framework.WebPlatform.Html;
using Meziantou.Framework.WebPlatform.Infra;

namespace Meziantou.Framework.WebPlatform.Dom;

public sealed partial class Selection
{
    /// <summary>
    /// The state of the caret kept between the movements of the selection, valid while the focus does not change: the caret
    /// affinity (the visual location of the caret at a bidi boundary or at the end of a wrapped line), and the inline
    /// coordinate kept by the consecutive movements by lines.
    /// </summary>
    private CaretNavigationState? _caretState;

    private sealed record CaretNavigationState(Node FocusNode, uint FocusOffset, bool AttachedUpstream, double? LineCoordinate);

    /// <summary>
    /// Returns the position of the caret: the focus of the collapsed selection, and whether the caret is attached to the content
    /// before it (its affinity after a movement, e.g. the end of a wrapped line instead of the start of the next one). Returns
    /// null when the selection is empty or is a range. Used by the painting of the caret.
    /// </summary>
    internal (Node Node, int Offset, bool AttachedUpstream)? GetCaretPosition()
    {
        if (_range is not { Collapsed: true } || !HasRangeInDocumentTree)
            return null;

        var (node, offset) = Focus;
        return (node, (int)offset, _caretState is { AttachedUpstream: true } state && ReferenceEquals(state.FocusNode, node) && state.FocusOffset == offset);
    }

    /// <summary>
    /// Moves the focus of the selection (<paramref name="alter"/> <c>extend</c>), or the selection collapsed at its focus
    /// (<c>move</c>), by a character, a word, a sentence, a line or a paragraph, or to a line, sentence, paragraph or document
    /// boundary, in the direction <paramref name="direction"/> (<c>forward</c>, <c>backward</c>, <c>left</c> or <c>right</c>),
    /// as the user would with the keyboard. The movements follow the layout: see <see cref="CaretNavigator"/>.
    /// </summary>
    /// <seealso href="https://w3c.github.io/selection-api/#dom-selection-modify"/>
    [IdlMember("modify", "https://w3c.github.io/selection-api/#dom-selection-modify")]
    public void Modify(string? alter = null, string? direction = null, string? granularity = null)
    {
        // "If alter is not ASCII case-insensitive match with "extend" or "move", abort these steps", and the same for direction
        // and granularity
        bool extend;
        if (InfraAscii.EqualsIgnoreAsciiCase(alter, "extend"))
        {
            extend = true;
        }
        else if (InfraAscii.EqualsIgnoreAsciiCase(alter, "move"))
        {
            extend = false;
        }
        else
        {
            return;
        }

        if (!TryParseDirection(direction, out var isForward, out var isVisual) || !TryParseGranularity(granularity, out var caretGranularity))
            return;

        // "If this selection is empty, abort these steps"
        if (_range is null || !HasRangeInDocumentTree)
            return;

        var (focusNode, focusOffset) = Focus;
        var (anchorNode, anchorOffset) = Anchor;

        // As in the browsers, the selection does not move while the focused editing host does not contain it
        if (FocusModel.GetFocusedElement(_document) is { } focusedElement && Editing.IsEditingHost(focusedElement) && !focusedElement.IsShadowIncludingInclusiveAncestorOf(focusNode))
            return;

        var engine = _document.GetLayoutEngine();
        if (engine.EnsureLayout() is null)
            return;

        // The caret stays in the editing host of the focus (or in the atomic inline that contains it, e.g. an inline-block)
        var editingHost = GetOutermostEditingHost(focusNode);
        var root = editingHost ?? GetAtomicInlineAncestor(engine, focusNode) ?? (Node)_document;
        if (CaretNavigator.Create(engine, root, editingHost) is not { } navigator)
            return;

        var state = _caretState is { } previous && ReferenceEquals(previous.FocusNode, focusNode) && previous.FocusOffset == focusOffset ? previous : null;
        var stop = navigator.Resolve(focusNode, (int)focusOffset, state?.AttachedUpstream ?? false);

        // The logical direction of the movements that are not visual: "left" and "right" depend on the resolved text direction
        // at the focus (https://w3c.github.io/selection-api/#dfn-resolved-text-direction), or on the direction of the line for the
        // line movements
        var line = navigator.Lines[stop.Line];
        if (isVisual && caretGranularity != CaretGranularity.Character)
        {
            var isRightToLeft = caretGranularity is CaretGranularity.Word or CaretGranularity.Sentence or CaretGranularity.SentenceBoundary
                ? (stop.Index > 0 ? line.Units[stop.Index - 1] : line.Units[Math.Min(stop.Index, line.Units.Count - 1)]).IsRightToLeft
                : line.IsRightToLeft;
            isForward = isForward != isRightToLeft;
        }

        var collapsedRange = _range.Collapsed;
        if (!extend && !collapsedRange)
        {
            if (caretGranularity == CaretGranularity.Character)
            {
                // Moving a range by a character collapses it at its end (or its start), without moving further
                var forwardEnd = isVisual ? isForward != line.IsRightToLeft : isForward;
                var point = forwardEnd ? (_range.EndContainer, _range.EndOffset) : (_range.StartContainer, _range.StartOffset);
                Collapse(point.Item1, point.Item2);
                _caretState = null;
                return;
            }

            if (caretGranularity is not (CaretGranularity.Line or CaretGranularity.Paragraph))
            {
                // The other movements start from the end of the range (or its start)
                var (node, offset) = isForward ? (_range.EndContainer, _range.EndOffset) : (_range.StartContainer, _range.StartOffset);
                stop = navigator.Resolve(node, (int)offset, attachedUpstream: isForward);
            }
        }

        var attachedUpstream = state?.AttachedUpstream ?? false;
        CaretMoveResult? result = caretGranularity switch
        {
            CaretGranularity.Character when isVisual => navigator.MoveVisually(stop, state?.AttachedUpstream, toRight: isForward),
            CaretGranularity.Character => navigator.MoveByCharacter(stop, isForward),
            CaretGranularity.Word => navigator.MoveByWord(stop, isForward),
            CaretGranularity.Sentence => navigator.MoveBySentence(stop, isForward, boundary: false),
            CaretGranularity.SentenceBoundary => navigator.MoveBySentence(stop, isForward, boundary: true),
            CaretGranularity.Line or CaretGranularity.Paragraph => navigator.MoveByLine(stop, state?.LineCoordinate ?? navigator.GetCoordinate(stop, attachedUpstream), isForward, paragraph: caretGranularity == CaretGranularity.Paragraph),
            _ => navigator.MoveToBoundary(stop, caretGranularity, isForward),
        };

        CaretPoint newFocus;
        if (result is { } moved)
        {
            newFocus = moved.Point;
            attachedUpstream = moved.AttachedUpstream;
        }
        else if (!extend && !collapsedRange)
        {
            newFocus = navigator.GetPoint(stop, isForward);
        }
        else
        {
            newFocus = new CaretPoint(focusNode, (int)focusOffset);
        }

        if (extend)
        {
            SetBaseAndExtent(anchorNode, anchorOffset, newFocus.Node, (uint)newFocus.Offset);
        }
        else
        {
            Collapse(newFocus.Node, (uint)newFocus.Offset);
        }

        _caretState = new CaretNavigationState(newFocus.Node, (uint)newFocus.Offset, attachedUpstream, result?.LineCoordinate);
    }

    private static bool TryParseDirection(string? value, out bool isForward, out bool isVisual)
    {
        isForward = InfraAscii.EqualsIgnoreAsciiCase(value, "forward") || InfraAscii.EqualsIgnoreAsciiCase(value, "right");
        isVisual = InfraAscii.EqualsIgnoreAsciiCase(value, "left") || InfraAscii.EqualsIgnoreAsciiCase(value, "right");
        return isForward || isVisual || InfraAscii.EqualsIgnoreAsciiCase(value, "backward");
    }

    private static bool TryParseGranularity(string? value, out CaretGranularity granularity)
    {
        granularity = default;
        if (value is null)
            return false;

        foreach (var (name, candidate) in Granularities)
        {
            if (InfraAscii.EqualsIgnoreAsciiCase(value, name))
            {
                granularity = candidate;
                return true;
            }
        }

        return false;
    }

    private static readonly (string Name, CaretGranularity Granularity)[] Granularities =
    [
        ("character", CaretGranularity.Character),
        ("word", CaretGranularity.Word),
        ("sentence", CaretGranularity.Sentence),
        ("line", CaretGranularity.Line),
        ("paragraph", CaretGranularity.Paragraph),
        ("lineboundary", CaretGranularity.LineBoundary),
        ("sentenceboundary", CaretGranularity.SentenceBoundary),
        ("paragraphboundary", CaretGranularity.ParagraphBoundary),
        ("documentboundary", CaretGranularity.DocumentBoundary),
    ];

    /// <summary>The outermost editing host that contains the node (the editing host of the editable content of the node), or null.</summary>
    internal static Element? GetOutermostEditingHost(Node node)
    {
        var host = Editing.GetEditingHost(node);
        while (host?.ParentNode is { } parent && Editing.GetEditingHost(parent) is { } outer)
        {
            host = outer;
        }

        return host;
    }

    /// <summary>The closest ancestor element of the node that is an atomic inline (e.g. an inline-block), whose content has its own lines, or null.</summary>
    private static Element? GetAtomicInlineAncestor(LayoutEngine engine, Node node)
    {
        if (engine.BoxTree is not { } boxTree)
            return null;

        for (var element = node as Element ?? node.ParentElement; element is not null; element = element.ParentElement)
        {
            if (boxTree.GetPrincipalBox(element) is ContainerBox { IsAtomicInline: true, Kind: not ContainerBoxKind.Replaced })
                return element;
        }

        return null;
    }
}
