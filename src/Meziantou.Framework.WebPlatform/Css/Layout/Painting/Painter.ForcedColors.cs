using Meziantou.Framework.WebPlatform.Css.Cascade;
using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.Rendering;

namespace Meziantou.Framework.WebPlatform.Css.Layout;

// Forced colors mode (https://drafts.csswg.org/css-color-adjust-1/#forced): the colors are forced by the used values of the
// styles (ComputedStyle.GetUsedValue); the painting adds the backplates of the text.
internal sealed partial class Painter
{
    /// <summary>
    /// The number of consecutive forced line breaks that end a paragraph of the lines of a block container (as in Chromium): the
    /// text of each paragraph has its own backplate.
    /// </summary>
    private const int BackplateParagraphLineBreaks = 2;

    /// <summary>
    /// Paints the backplates of the text of the lines of a block container in forced colors mode: the user agent "may also
    /// enforce a backplate underneath text (similar to the way backgrounds are painted on the ::selection pseudo-element) to
    /// ensure adequate contrast for readability" (e.g. over a background image, which is not forced). As in Chromium, a
    /// backplate is the bounding box of the text of a paragraph of the block container (the text of its lines, also in its
    /// inline boxes whatever their <c>forced-color-adjust</c>, but not the atomic inlines; two consecutive forced line breaks
    /// end a paragraph, and so does the end of a column), painted when <c>forced-color-adjust</c> is <c>auto</c> on the block container and it is visible. Its
    /// color is the one that the background of the block container is forced to (<c>Canvas</c>, or e.g. <c>ButtonFace</c> in a
    /// button, <c>Field</c> for the value and the placeholder of a text control), opaque.
    /// </summary>
    private void PaintBackplates(BoxFragment fragment)
    {
        var style = fragment.Box.Style;
        if (!style.IsForcedColorsAdjusted || style.Layout.Visibility != Visibility.Visible)
            return;

        List<PhysicalRect>? backplates = null;
        PhysicalRect? current = null;
        var lineBreaks = 0;
        var isHorizontal = style.Layout.IsHorizontalWritingMode;
        LineBoxFragment? previous = null;
        foreach (var child in fragment.ChildrenSpan)
        {
            if (child is not LineBoxFragment { IsHiddenForPaint: false } line)
                continue;

            // The lines of each column of a multi-column container have their own backplates (the column rules are between them)
            if (previous is not null && current is { } column && (isHorizontal ? line.Offset.Y < previous.Offset.Y : line.Offset.Y != previous.Offset.Y))
            {
                (backplates ??= []).Add(column);
                current = null;
            }

            previous = line;
            Collect(line, line.GetOffsetInRoot() - _group.Origin);
        }

        if (current is { } last)
        {
            (backplates ??= []).Add(last);
        }

        if (backplates is null)
            return;

        // The placeholder of a text control is painted over the background of the control: its backplate has the color that
        // this background is forced to (Field), not the one paired with its own color (GrayText, on the canvas)
        var backplateStyle = fragment.Box.PseudoElement == TextControlLayout.PlaceholderPseudoElement && fragment.Parent is BoxFragment { Box.Kind: ContainerBoxKind.Replaced } control ? control.Box.Style : style;
        if (DisplayColor.Parse(backplateStyle.GetBackplateColor()) is not { IsTransparent: false } color)
            return;

        SyncClips(GetClipChain(fragment, includeSelf: true));
        foreach (var backplate in backplates)
        {
            _builder.Add(new DrawRectItem(SnapToDevicePixels(backplate), new DisplayPaint(color)) { Source = fragment.Box.Element });
        }

        void Collect(Fragment parent, PhysicalOffset offset)
        {
            foreach (var child in parent.ChildrenSpan)
            {
                if (child.IsHiddenForPaint)
                    continue;

                var childOffset = offset + child.Offset;
                switch (child)
                {
                    case TextFragment { Size: { Width: > 0, Height: > 0 } } text:
                        if (lineBreaks >= BackplateParagraphLineBreaks && current is { } paragraph)
                        {
                            (backplates ??= []).Add(paragraph);
                            current = null;
                        }

                        lineBreaks = 0;
                        var rect = new PhysicalRect(childOffset, text.Size);
                        current = current is { } union ? union.Union(rect) : rect;
                        break;

                    case BoxFragment { Box.Element: HtmlBRElement, Box.PseudoElement: null }:
                        lineBreaks++;
                        break;

                    case BoxFragment { Box.IsInlineBox: true }:
                        Collect(child, childOffset);
                        break;
                }
            }
        }
    }
}
