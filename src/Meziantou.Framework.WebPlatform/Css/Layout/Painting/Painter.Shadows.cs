using Meziantou.Framework.WebPlatform.Rendering;

namespace Meziantou.Framework.WebPlatform.Css.Layout;

// The shadows of the boxes (https://drafts.csswg.org/css-backgrounds/#box-shadow).
internal sealed partial class Painter
{
    /// <summary>
    /// Paints the shadows of a fragment of a box. The shadows of a box split by the block fragmentation are the ones of its
    /// fragments stitched together, sliced at the breaks (https://drafts.csswg.org/css-break-3/#valdef-box-decoration-break-slice:
    /// "the box is rendered as if it were not fragmented, and then sliced by the breaks": no shadow is painted at the breaks).
    /// The shadows of an inline box split over several lines are sliced in the same way at the edges where the box is broken:
    /// each fragment paints the shadows of a box that continues past these edges, clipped to the fragment in the inline axis.
    /// </summary>
    private void PaintSlicedBoxShadows(BoxFragment fragment, PhysicalOffset offset, bool inset)
    {
        if (fragment.Box.IsInlineBox)
        {
            var style = GetDecorationStyle(fragment);
            var borderBox = GetSnappedBorderBox(fragment, offset);
            if ((fragment.IsFirstFragment && fragment.IsLastFragment) || style.Layout.Fragmentation.BoxDecorationBreak == BoxDecorationBreak.Clone || GetShadows(style, "box-shadow") is not { } shadows)
            {
                PaintBoxShadows(fragment, borderBox, inset);
                return;
            }

            // The box continues past its broken edges farther than its shadows reach
            var reach = 0d;
            foreach (var shadow in shadows)
            {
                if (shadow.Inset == inset && !shadow.Color.IsTransparent)
                {
                    reach = Math.Max(reach, (shadow.Blur * 1.5) + Math.Abs(shadow.Spread) + Math.Abs(shadow.X) + Math.Abs(shadow.Y) + 1);
                }
            }

            if (reach == 0)
                return;

            var writingDirection = fragment.WritingDirection;
            var extended = borderBox;
            var clip = Inflate(borderBox, UnclippedExtent);
            if (!fragment.IsFirstFragment)
            {
                Slice(writingDirection.InlineStart);
            }

            if (!fragment.IsLastFragment)
            {
                Slice(writingDirection.InlineEnd);
            }

            _builder.Save();
            _builder.Add(new ClipRectItem(clip));
            PaintBoxShadows(fragment, extended, inset);
            _builder.Restore();
            return;

            void Slice(PhysicalSide side)
            {
                switch (side)
                {
                    case PhysicalSide.Left:
                        extended = new PhysicalRect(extended.X - reach, extended.Y, extended.Width + reach, extended.Height);
                        clip = new PhysicalRect(borderBox.X, clip.Y, clip.Right - borderBox.X, clip.Height);
                        break;

                    case PhysicalSide.Right:
                        extended = new PhysicalRect(extended.X, extended.Y, extended.Width + reach, extended.Height);
                        clip = new PhysicalRect(clip.X, clip.Y, borderBox.Right - clip.X, clip.Height);
                        break;

                    case PhysicalSide.Top:
                        extended = new PhysicalRect(extended.X, extended.Y - reach, extended.Width, extended.Height + reach);
                        clip = new PhysicalRect(clip.X, borderBox.Y, clip.Width, clip.Bottom - borderBox.Y);
                        break;

                    default:
                        extended = new PhysicalRect(extended.X, extended.Y, extended.Width, extended.Height + reach);
                        clip = new PhysicalRect(clip.X, clip.Y, clip.Width, borderBox.Bottom - clip.Y);
                        break;
                }
            }
        }

        if (fragment.StitchedBox is not { } stitched)
        {
            PaintBoxShadows(fragment, GetSnappedBorderBox(fragment, offset), inset, fragment.BorderBoxRect.Translate(offset).Offset);
            return;
        }

        _builder.Save();
        _builder.Add(new ClipRectItem(GetSliceClip(fragment, stitched, offset)));
        PaintBoxShadows(stitched, GetSnappedBorderBox(stitched, offset + GetStitchedOrigin(fragment, stitched)), inset);
        _builder.Restore();
    }

    /// <summary>
    /// Paints the outer or the inset shadows of a box (https://drafts.csswg.org/css-backgrounds/#box-shadow), the first shadow on
    /// top: an outer shadow is the border box offset, expanded by the spread distance and blurred (a Gaussian blur whose standard
    /// deviation is half the blur radius), painted only outside of the border box; an inset shadow is painted inside of the
    /// padding box, outside of the padding box offset and shrunk by the spread distance. The shadows of a box whose border is
    /// shaped by <c>border-shape</c> follow the edges of its border (see <see cref="PaintShapedBoxShadows"/>), positioned at
    /// <paramref name="layoutOrigin"/> (the origin of the border box before its snapping to the device pixels).
    /// </summary>
    private void PaintBoxShadows(BoxFragment fragment, PhysicalRect borderBox, bool inset, PhysicalOffset? layoutOrigin = null)
    {
        var style = GetDecorationStyle(fragment);
        if (GetShadows(style, "box-shadow") is not { } shadows)
            return;

        if (BorderShapeGeometry.Get(fragment) is { } borderShape)
        {
            PaintShapedBoxShadows(fragment, borderShape, shadows, layoutOrigin ?? borderBox.Offset, inset);
            return;
        }

        var border = RoundedRect.FromBorderBox(fragment, borderBox);
        var padding = border.Inset(fragment.Border.Add(fragment.Scrollbars));
        var knockout = !inset && HasOpaqueBorderBoxBackground(fragment) ? border.Inset(Uniform(1)) : border;
        var source = fragment.Box.Element;
        for (var i = shadows.Count - 1; i >= 0; i--)
        {
            var shadow = shadows[i];
            if (shadow.Inset != inset || shadow.Color.IsTransparent)
                continue;

            var paint = new DisplayPaint(shadow.Color) { BlurSigma = shadow.Blur / 2 };
            var extent = (shadow.Blur * 1.5) + Math.Abs(shadow.Spread) + Math.Abs(shadow.X) + Math.Abs(shadow.Y) + 1;
            _builder.Save();
            if (!inset)
            {
                // https://drafts.csswg.org/css-backgrounds/#shadow-shape: the radii grow with the spread distance
                var shape = Spread(border, shadow.Spread, borderBox.Size).Translate(new PhysicalOffset(shadow.X, shadow.Y));
                if (shape.Rect.Width <= 0 || shape.Rect.Height <= 0)
                {
                    _builder.Restore();
                    continue;
                }

                // "The shadow is drawn outside the border edge only: it is clipped inside the border-box of the element." The
                // edge of the clip and the edge of the background are antialiased on the same curve (or on the same fractional
                // edge): what is below the box would show through between the shadow and the background. When the background
                // covers the border box with an opaque color, the clip is moved one pixel inside of the border box (as Blink
                // does), where the background hides the shadow
                if (knockout.Rect.Width > 0 && knockout.Rect.Height > 0)
                {
                    _builder.Add(new ClipPathItem(DisplayPath.CreateComplement(Inflate(borderBox.Union(shape.Rect), extent), knockout)));
                }

                if (shape.IsRect)
                {
                    _builder.Add(new DrawRectItem(shape.Rect, paint) { Source = source });
                }
                else
                {
                    _builder.Add(new DrawRoundedRectItem(shape, paint) { Source = source });
                }
            }
            else
            {
                if (padding.Rect.IsEmpty)
                {
                    _builder.Restore();
                    continue;
                }

                if (padding.IsRect)
                {
                    _builder.Add(new ClipRectItem(padding.Rect));
                }
                else
                {
                    _builder.Add(new ClipRoundedRectItem(padding));
                }

                // The area of the padding box outside of the hole: the padding box shrunk by the spread distance and offset
                var hole = Spread(padding, -shadow.Spread, borderBox.Size).Translate(new PhysicalOffset(shadow.X, shadow.Y));
                _builder.Add(new DrawPathItem(DisplayPath.CreateComplement(Inflate(padding.Rect, extent), hole), paint) { Source = source });
            }

            _builder.Restore();
        }
    }

    private static PhysicalRect Inflate(PhysicalRect rect, double amount) => new(rect.X - amount, rect.Y - amount, rect.Width + (2 * amount), rect.Height + (2 * amount));

    /// <summary>
    /// Whether the background of a box covers its whole border box with an opaque color: an opaque <c>background-color</c>
    /// painted in the border box (the <c>background-clip</c> of the bottom layer), by the box itself (the background of the root
    /// element, or of the body element, may be the background of the canvas; the background of a row or of a row group is
    /// only painted in its cells).
    /// </summary>
    private static bool HasOpaqueBorderBoxBackground(BoxFragment fragment)
    {
        var style = fragment.Box.Style;
        if (!GetColor(style, "background-color").IsOpaque || IsBackgroundPropagated(fragment) || IsHiddenEmptyCell(fragment) || IsTablePart(fragment.Box))
            return false;

        return ParseBackgroundBox(GetLastListItem(style.GetComputedValue("background-clip")), BackgroundBox.BorderBox) == BackgroundBox.BorderBox;
    }

    /// <summary>
    /// Expands (or shrinks, for a negative spread) a rounded rectangle by a spread distance
    /// (https://drafts.csswg.org/css-borders-4/#shadow-shape): "the corner radii of the shadow are also increased (decreased, for
    /// inner shadows) from the border-box (padding-box) radii by adding (subtracting) the spread distance (and flooring at zero).
    /// For outer shadows, the border radius is then adjusted, independently in each dimension, to preserve the sharpness of
    /// rounded corners": the outset-adjusted border radius (https://drafts.csswg.org/css-borders-4/#outset-adjusted-border-radius,
    /// for the border box of the size <paramref name="edge"/>) of a corner grows by less than the spread when it is smaller
    /// than the spread, unless the corner covers the box (the coverage is 1 when the radii of the corner are half of the box: a
    /// circle stays a circle). The shaped corners follow the contour of the box.
    /// </summary>
    private static RoundedRect Spread(RoundedRect shape, double spread, PhysicalSize edge) => shape.Outset(Uniform(spread), edge);
}
