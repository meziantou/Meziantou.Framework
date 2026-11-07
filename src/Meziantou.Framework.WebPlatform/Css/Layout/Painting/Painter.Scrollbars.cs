using Meziantou.Framework.WebPlatform.Css.Cascade;
using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.Rendering;

namespace Meziantou.Framework.WebPlatform.Css.Layout;

// The painting of the classic scrollbars (https://drafts.csswg.org/css-overflow-3/#classic-scrollbars, see ScrollbarGeometry):
// the scrollbars of a scroll container are painted with its box, over its background and its borders, in the gutters that
// the layout reserves between its border and its padding (its contents are clipped to its padding box, which excludes them);
// the scrollbars of the viewport are painted over the document. The look is the same on every platform: a track that fills
// the scrollbar, a rectangular thumb inset by 2px from the long edges of the track, no button, and a scroll corner of the
// color of the track.
internal sealed partial class Painter
{
    /// <summary>The distance between the long edges of a track and its thumb, in CSS pixels.</summary>
    private const double ScrollbarThumbInset = 2;

    // The colors of scrollbar-color: auto, for the light and the dark color schemes
    private static readonly DisplayColor LightScrollbarThumb = DisplayColor.FromRgb(0xC1, 0xC1, 0xC1);
    private static readonly DisplayColor LightScrollbarTrack = DisplayColor.FromRgb(0xF1, 0xF1, 0xF1);
    private static readonly DisplayColor DarkScrollbarThumb = DisplayColor.FromRgb(0x68, 0x68, 0x68);
    private static readonly DisplayColor DarkScrollbarTrack = DisplayColor.FromRgb(0x2C, 0x2C, 0x2C);

    /// <summary>Whether the classic scrollbars are painted (<see cref="ViewportOptions.ScrollbarsVisible"/>).</summary>
    private readonly bool _paintsScrollbars;

    /// <summary>Paints the scrollbars of the fragment of a scroll container, at the offset of its border box.</summary>
    private void PaintScrollbars(BoxFragment fragment, PhysicalOffset offset)
    {
        if (ScrollbarGeometry.ForFragment(fragment) is { } geometry)
        {
            PaintScrollbars(geometry, offset, fragment.Box.Style, fragment.Box.ScrollingElement);
        }
    }

    /// <summary>
    /// Paints the scrollbars of the viewport, in the coordinates of the viewport: their style is the one of the root element
    /// (https://drafts.csswg.org/css-scrollbars-1/#scrollbar-color: "applied to the root element will be propagated to the viewport").
    /// </summary>
    private void PaintViewportScrollbars(BoxFragment root, PhysicalSize viewportSize)
    {
        if (ScrollbarGeometry.ForViewport(root, viewportSize, _engine.ViewportScrollState) is not { } geometry)
            return;

        var rootElement = _document.DocumentElement;
        var style = rootElement is not null && ComputedStyleDeclarations.IsBeingRendered(rootElement) ? _document.GetStyleEngine().GetStyle(rootElement) : null;
        PaintScrollbars(geometry, PhysicalOffset.Zero, style, rootElement);
    }

    private void PaintScrollbars(ScrollbarGeometry geometry, PhysicalOffset offset, ComputedStyle? style, Node? source)
    {
        var (thumbColor, trackColor) = GetScrollbarColors(style);
        PaintScrollbar(geometry.Vertical);
        PaintScrollbar(geometry.Horizontal);
        if (geometry.Corner is { } corner && !trackColor.IsTransparent)
        {
            _builder.Add(new DrawRectItem(SnapToDevicePixels(corner.Translate(offset)), new DisplayPaint(trackColor)) { Source = source });
        }

        void PaintScrollbar(Scrollbar? scrollbar)
        {
            if (scrollbar is not { } bar)
                return;

            if (!trackColor.IsTransparent)
            {
                _builder.Add(new DrawRectItem(SnapToDevicePixels(bar.Rect.Translate(offset)), new DisplayPaint(trackColor)) { Source = source });
            }

            if (!bar.HasThumb || thumbColor.IsTransparent)
                return;

            // The thumb is inset from the long edges of the track (not when the scrollbar is too thin)
            var thumb = bar.ThumbRect.Translate(offset);
            var inset = bar.Thickness > 3 * ScrollbarThumbInset ? ScrollbarThumbInset : 0;
            thumb = bar.IsVertical ? new PhysicalRect(thumb.X + inset, thumb.Y, thumb.Width - (2 * inset), thumb.Height) : new PhysicalRect(thumb.X, thumb.Y + inset, thumb.Width, thumb.Height - (2 * inset));
            _builder.Add(new DrawRectItem(SnapToDevicePixels(thumb), new DisplayPaint(thumbColor)) { Source = source });
        }
    }

    /// <summary>
    /// Returns the colors of the thumb and of the track of the scrollbars of a box (https://drafts.csswg.org/css-scrollbars-1/#scrollbar-color):
    /// the two colors of <c>scrollbar-color</c>, or for <c>auto</c> the colors of the scrollbars of the library for the used
    /// color scheme of the box.
    /// </summary>
    private static (DisplayColor Thumb, DisplayColor Track) GetScrollbarColors(ComputedStyle? style)
    {
        if (style is not null && style.GetComputedValue("scrollbar-color") is not ("auto" or "") && CssValueComputer.SplitComponents(style.GetUsedValue("scrollbar-color")) is [var thumb, var track]
            && DisplayColor.Parse(thumb) is { } thumbColor && DisplayColor.Parse(track) is { } trackColor)
        {
            return (thumbColor, trackColor);
        }

        return style is not null && CssValueComputer.GetUsedColorScheme(style) == "dark" ? (DarkScrollbarThumb, DarkScrollbarTrack) : (LightScrollbarThumb, LightScrollbarTrack);
    }
}
