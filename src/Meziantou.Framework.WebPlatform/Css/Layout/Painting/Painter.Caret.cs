using Meziantou.Framework.WebPlatform.Rendering;

namespace Meziantou.Framework.WebPlatform.Css.Layout;

// The painting of the text insertion caret (https://drafts.csswg.org/css-ui-4/#insertion-caret, see PaintedCaret): as in the
// browsers, the caret is painted with the inline content of its block container (in its layer, with its clips and its
// transforms), over the text of its line. A block caret that covers a character is an overlay of its text fragment, painted
// as the background of a highlight: under the glyphs, which are painted once, in the second color of caret-color when it has one.
internal sealed partial class Painter
{
    /// <summary>The caret that is painted, or null.</summary>
    private PaintedCaret? _caret;

    /// <summary>The fragment of the item of the painting order that the caret is painted after, or null (a block caret over a character is painted with its text).</summary>
    private Fragment? _caretAnchor;

    /// <summary>
    /// Whether the document displays a caret that blinks (<c>caret-animation: auto</c>), painted or in the hidden phase of the
    /// blinking: the layout engine runs the timer of the blinking while such a caret is painted.
    /// </summary>
    private bool _hasAnimatedCaret;

    /// <summary>
    /// Computes the caret of the document and the item of the painting order that it is painted after: the last text of its
    /// line; the box of its block container when the line has no text (the caret of an empty editing host, or beside an atomic
    /// inline).
    /// </summary>
    /// <param name="items">The painting order of the document.</param>
    /// <param name="hidesAnimatedCaret">Whether the blinking caret is in its hidden phase.</param>
    private void PrepareCaret(List<PaintItem> items, bool hidesAnimatedCaret)
    {
        if (PaintedCaret.Compute(_engine, _document) is not { } caret)
            return;

        _hasAnimatedCaret = caret.IsAnimated;
        if (caret.IsAnimated && hidesAnimatedCaret)
            return;

        if (caret.Rect is null)
        {
            _caret = caret;
            return;
        }

        if (caret.Color.IsTransparent)
            return;

        Fragment? anchor = null;
        if (caret.Line is { } line)
        {
            foreach (var item in items)
            {
                if (item is { Marker: PaintItemMarker.None, Fragment: TextFragment text } && IsOnLine(text, line))
                {
                    anchor = text;
                }
            }
        }

        if (anchor is null)
        {
            foreach (var item in items)
            {
                if (item is { Marker: PaintItemMarker.None, IsSplitInlineBand: false, Part: PaintItemPart.All or PaintItemPart.Decorations } && ReferenceEquals(item.Fragment, caret.Container))
                {
                    anchor = item.Fragment;
                }
            }
        }

        if (anchor is not null)
        {
            _caret = caret;
            _caretAnchor = anchor;
        }

        static bool IsOnLine(TextFragment text, LineBoxFragment line)
        {
            var parent = text.Parent;
            while (parent is BoxFragment { Box.IsInlineBox: true })
            {
                parent = parent.Parent;
            }

            return ReferenceEquals(parent, line);
        }
    }

    /// <summary>Paints the caret after the item of the painting order it is anchored to (see <see cref="PrepareCaret"/>).</summary>
    private void PaintCaretAfter(in PaintItem item)
    {
        if (_caret is not { Rect: { } rect } caret || !ReferenceEquals(item.Fragment, _caretAnchor) || item.Part is not (PaintItemPart.All or PaintItemPart.Decorations))
            return;

        _caretAnchor = null;
        SyncClips(GetClipChain(caret.Container, includeSelf: true));
        _builder.Add(new DrawRectItem(SnapToDevicePixels(rect.Translate(-_group.Origin)), new DisplayPaint(caret.Color)) { Source = caret.Source });
    }

    /// <summary>
    /// Adds the block caret that covers a character of a text fragment to the highlights of the fragment: its color is the
    /// background of the character, whose glyphs are painted in the second color of <c>caret-color</c> when it has one.
    /// </summary>
    private List<TextHighlight>? AddBlockCaret(List<TextHighlight>? highlights, TextFragment fragment, PhysicalOffset offset)
    {
        if (_caret is not { CoveredText: { } covered } caret || !ReferenceEquals(covered, fragment))
            return highlights;

        var bounds = fragment.GetRangeRect(caret.CoveredStart, caret.CoveredEnd).Translate(offset);
        (highlights ??= []).Add(new TextHighlight(SnapToDevicePixels(bounds), caret.Color, caret.TextColor, Decorations: null, bounds) { Recolors = caret.TextColor is not null });
        return highlights;
    }
}
