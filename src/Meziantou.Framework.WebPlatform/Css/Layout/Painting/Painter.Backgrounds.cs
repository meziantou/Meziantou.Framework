using Meziantou.Framework.WebPlatform.Css.Cascade;
using Meziantou.Framework.WebPlatform.Css.Syntax;
using Meziantou.Framework.WebPlatform.Css.Values;
using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.Infra;
using Meziantou.Framework.WebPlatform.Rendering;

namespace Meziantou.Framework.WebPlatform.Css.Layout;

// The backgrounds (https://drafts.csswg.org/css-backgrounds/#backgrounds): the background color and the layers of images
// (raster images, gradients), sized, positioned, repeated and clipped, with the special backgrounds of the canvas.
internal sealed partial class Painter
{
    /// <summary>The extent of the painting area of the canvas background (it covers the whole canvas).</summary>
    private const double CanvasExtent = 1e6;

    /// <summary>The parsed background layers of the styles painted by this painter.</summary>
    private Dictionary<ComputedStyle, BackgroundLayer[]>? _backgroundLayers;

    /// <summary>The boxes of the background (https://drafts.csswg.org/css-backgrounds/#typedef-visual-box).</summary>
    private enum BackgroundBox : byte
    {
        BorderBox,
        PaddingBox,
        ContentBox,
        Text,
        BorderArea,

        /// <summary>The union of the area of the borders and of the text (<c>background-clip: border-area text</c>).</summary>
        BorderAreaText,
    }

    /// <summary>The repetition of a background layer in an axis (https://drafts.csswg.org/css-backgrounds/#background-repeat).</summary>
    private enum BackgroundRepeat : byte
    {
        Repeat,
        Space,
        Round,
        NoRepeat,
    }

    /// <summary>
    /// A layer of the background of a style: its image and the values of its properties (the lists of the background properties
    /// are repeated to the number of images, https://drafts.csswg.org/css-backgrounds/#layering).
    /// </summary>
    private sealed record BackgroundLayer(
        CssComponentValue? Image,
        LengthPercentage PositionX,
        LengthPercentage PositionY,
        string Size,
        BackgroundRepeat RepeatX,
        BackgroundRepeat RepeatY,
        string Attachment,
        BackgroundBox Origin,
        BackgroundBox Clip,
        BlendMode BlendMode)
    {
        /// <summary>The effective zoom of the style (the computed lengths are multiplied by it).</summary>
        public double Zoom { get; init; } = 1;
    }

    /// <summary>
    /// Paints the background of the canvas (https://drafts.csswg.org/css-backgrounds/#special-backgrounds): the background of the
    /// root element, or, in an HTML document whose root element has no background, the one of the body element, covers the
    /// whole canvas; its images are positioned relative to the root element (the background positioning area of the root
    /// element). The background of the box it comes from is not painted again (see <see cref="IsBackgroundPropagated"/>).
    /// </summary>
    private void PaintCanvasBackground(BoxFragment root)
    {
        if (GetCanvasBackgroundStyle(root, out var rootFragment) is not { } style)
            return;

        var canvas = new PhysicalRect(-CanvasExtent, -CanvasExtent, 2 * CanvasExtent, 2 * CanvasExtent);

        // (in forced colors mode, the color is forced when the colors of the root element are)
        var color = DisplayColor.Parse(style.GetCanvasBackgroundColor(rootFragment.Box.Style)) ?? DisplayColor.Transparent;
        var layers = GetBackgroundLayers(style);
        if (ForcesWhiteBackground(style) && (!color.IsTransparent || layers.Length > 0))
        {
            _builder.Add(new DrawPaintItem(new DisplayPaint(DisplayColor.White)) { Source = style.Element });
            return;
        }

        var blends = HasBlendModes(layers);
        if (blends)
        {
            _builder.SaveLayer(new SaveLayerItem(1, BlendMode.Normal, Filters: null) { Source = style.Element });
        }

        if (!color.IsTransparent)
        {
            _builder.Add(new DrawPaintItem(new DisplayPaint(color)) { Source = style.Element });
        }

        // The positioning area is the one of the root element (its border box when it is the root element of the canvas)
        var offset = rootFragment.GetOffsetInRoot() - _group.Origin;
        var rootBorderBox = GetSnappedBorderBox(rootFragment, offset);
        for (var i = layers.Length - 1; i >= 0; i--)
        {
            PaintBackgroundLayer(rootFragment, style, layers[i], rootBorderBox, rootFragment.Border, rootFragment.Padding, paintingArea: new RoundedRect(canvas, default), blends);
        }

        if (blends)
        {
            _builder.Restore();
        }
    }

    /// <summary>
    /// Returns whether the background of a style is painted solid white: when the document is printed without its backgrounds
    /// (<see cref="Dom.PrintOptions.PrintBackground"/>) and its <c>print-color-adjust</c> is <c>economy</c>, as in the browsers,
    /// the backgrounds (their colors and images) are replaced by a white background (https://drafts.csswg.org/css-color-adjust-1/#print-color-adjust);
    /// a box without background stays transparent.
    /// </summary>
    private bool ForcesWhiteBackground(ComputedStyle style) => _engine.PrintsWithoutBackgrounds && style.GetComputedValue("print-color-adjust") != "exact";

    /// <summary>
    /// Returns the color of the text printed without the backgrounds (see <see cref="ForcesWhiteBackground"/>): as in Chromium, a
    /// color nearly white (whose squared distance to white is at most 255²) is darkened, so that the text stays readable on the
    /// white backgrounds.
    /// </summary>
    private DisplayColor GetPrintedTextColor(ComputedStyle style, DisplayColor color)
    {
        if (!ForcesWhiteBackground(style) || color.IsTransparent)
            return color;

        var r = (int)Math.Round(Math.Clamp(color.R, 0, 1) * 255, MidpointRounding.AwayFromZero);
        var g = (int)Math.Round(Math.Clamp(color.G, 0, 1) * 255, MidpointRounding.AwayFromZero);
        var b = (int)Math.Round(Math.Clamp(color.B, 0, 1) * 255, MidpointRounding.AwayFromZero);
        var distance = ((255 - r) * (255 - r)) + ((255 - g) * (255 - g)) + ((255 - b) * (255 - b));
        return distance > 255 * 255 ? color : BorderPainter.Shade(color, darker: true);
    }

    /// <summary>
    /// Returns the color of the canvas below its background: the canvas system color of the used color scheme of the root element
    /// (https://drafts.csswg.org/css-color-adjust/#color-scheme-effect: white, or a dark color when the page uses a dark color scheme).
    /// </summary>
    internal static DisplayColor GetCanvasBaseColor(Document document)
    {
        if (document.DocumentElement is not { } root || document.GetStyleEngine().GetStyle(root) is not { } style)
            return DisplayColor.White;

        return DisplayColor.Parse(CssValueComputer.GetSystemColor(style, "canvas")) ?? DisplayColor.White;
    }

    /// <summary>Returns the style whose background is the background of the canvas, or null when the root element has no box.</summary>
    private static ComputedStyle? GetCanvasBackgroundStyle(BoxFragment root, out BoxFragment rootElement)
    {
        rootElement = null!;
        foreach (var child in root.ChildrenSpan)
        {
            if (child is BoxFragment { Box: { IsAnonymous: false, Style.IsRoot: true } } fragment)
            {
                rootElement = fragment;
                break;
            }
        }

        if (rootElement is null)
            return null;

        var style = rootElement.Box.Style;
        // (also in an XHTML document: the html and body elements are HTML elements)
        if (HasBackground(style) || style.Element is not HtmlHtmlElement || style.Element.OwnerDocument is not { } document)
            return style;

        // https://drafts.csswg.org/css-contain-2/#contain-property: the containment of the html or body element disables the
        // propagation of the background of the body element to the canvas
        if (ScrollContainers.HasContainment(style.Layout))
            return style;

        // A root element replaced by an image (content: url()) has no contents: the body element is not rendered
        if (rootElement.Box.Kind == ContainerBoxKind.Replaced)
            return style;

        // "it is recommended that authors of HTML documents specify the canvas background for the BODY element rather than the
        // HTML element": the background of the first body child of the html element is used. "However, if the element whose
        // background would be used for the canvas is display: none, then the canvas background is transparent"; the background
        // of a body element that generates no box because of display: contents is not used either (as in the browsers)
        return document.Body is HtmlBodyElement body && body.ParentNode == style.Element && document.GetStyleEngine().GetStyle(body) is { } bodyStyle
            && !ScrollContainers.HasContainment(bodyStyle.Layout) && bodyStyle.Layout.Display.GeneratesBox ? bodyStyle : style;
    }

    /// <summary>Whether the background of a style is not transparent (a color other than transparent, or an image).</summary>
    private static bool HasBackground(ComputedStyle style)
        => style.GetComputedValue("background-image") is not ("none" or "") || !GetColor(style, "background-color").IsTransparent;

    /// <summary>Whether the background of a box is the background of the canvas (the root element, or the body element whose background propagates).</summary>
    private static bool IsBackgroundPropagated(BoxFragment fragment)
    {
        var style = fragment.Box.Style;
        if (fragment.Box.PseudoElement is not null)
            return false;

        if (style.IsRoot)
            return true;

        if (style.Element is not HtmlBodyElement body || body.ParentNode is not HtmlHtmlElement html || !ReferenceEquals(body.OwnerDocument!.Body, body))
            return false;

        return html.NodeDocument.GetStyleEngine().GetStyle(html) is { } htmlStyle && !HasBackground(htmlStyle)
            && !ScrollContainers.HasContainment(htmlStyle.Layout) && !ScrollContainers.HasContainment(style.Layout);
    }

    /// <summary>
    /// Paints the background of a box (https://drafts.csswg.org/css-backgrounds/#backgrounds): the background color in the
    /// background painting area of the last layer, then the layers of images from the bottom one (the last of the lists), each
    /// in its painting area (<c>background-clip</c>), rounded by the border radii.
    /// </summary>
    private void PaintBackground(BoxFragment fragment, PhysicalOffset offset)
    {
        if (IsBackgroundPropagated(fragment) || IsHiddenEmptyCell(fragment))
            return;

        // https://w3c.github.io/webvtt/#the-cue-pseudo-element: the background of ::cue is applied to the WebVTT cue background
        // box rather than to the list of WebVTT node objects (the root of the cue, whose style computes it)
        if (fragment.Box is { PseudoElement: null, Element: WebVtt.WebVttElement { Kind: WebVtt.WebVttElementKind.CueRoot } })
            return;

        var style = GetDecorationStyle(fragment);

        // https://drafts.csswg.org/css-page-3/#painting: as in the browsers, the background of a page box covers the whole page
        // sheet (its margins included, whatever its background-clip); its images are positioned in the page box
        PhysicalRect? paintingBox = fragment.Box.PseudoElement == Cascade.StyleEngine.PageContextPseudoElement && fragment.Parent is BoxFragment sheet
            ? new PhysicalRect(offset - fragment.Offset, sheet.Size)
            : null;
        PaintBackground(fragment, style, GetSnappedBorderBox(fragment, offset), fragment.Border, fragment.Padding, GetDecorationBox(fragment).Translate(offset), GetBackgroundBleedInset(fragment), paintingBox);
    }

    /// <summary>
    /// Returns how much the background of a box painted in its border box is moved inside of its borders, to avoid the bleeding
    /// of the background around the rounded corners: the outer edge of an opaque border and the edge of the background
    /// below it are antialiased on the same curve, so a fringe of the background would stay visible around the border. As in
    /// the browsers (the "background bleed avoidance" of Blink), when the four borders hide the background below them (they
    /// have a width, a style that paints their outer edge and an opaque color), the background stops in the middle of the borders
    /// (at one sixth of the borders when one of them is double).
    /// </summary>
    private static Sides<double> GetBackgroundBleedInset(BoxFragment fragment)
    {
        var border = fragment.Border;
        var box = fragment.Box;
        if (!(border.Top > 0 && border.Right > 0 && border.Bottom > 0 && border.Left > 0) || BorderRadii.Get(fragment) is null || IsInCollapsedTable(box))
            return default;

        var style = box.Style;
        if (style.GetComputedValue("border-image-source") is not ("none" or ""))
            return default;

        var styles = style.Layout.Box.BorderStyle;
        if (!HidesBackground(styles.Top, "border-top-color") || !HidesBackground(styles.Right, "border-right-color")
            || !HidesBackground(styles.Bottom, "border-bottom-color") || !HidesBackground(styles.Left, "border-left-color"))
        {
            return default;
        }

        var fraction = styles.Top == BorderStyle.Double || styles.Right == BorderStyle.Double || styles.Bottom == BorderStyle.Double || styles.Left == BorderStyle.Double ? 1 / 6d : 1 / 2d;
        return new Sides<double>(border.Top * fraction, border.Right * fraction, border.Bottom * fraction, border.Left * fraction);

        bool HidesBackground(BorderStyle borderStyle, string colorProperty)
            => borderStyle is not (BorderStyle.None or BorderStyle.Hidden or BorderStyle.Dotted or BorderStyle.Dashed) && GetColor(style, colorProperty).IsOpaque;
    }

    /// <summary>
    /// Paints a background of a style in a border box with the given border widths and paddings.
    /// <paramref name="layoutBorderBox"/> is the border box before its snapping to the device pixels, when it is known;
    /// <paramref name="bleedInset"/> moves the edges of the border box painting area inside of the borders (see <see cref="GetBackgroundBleedInset"/>).
    /// <paramref name="paintingBox"/> is the background painting area when it is not a box of the fragment (the cell that the
    /// background of a row or of a column is painted in): the border box is then only the background positioning area.
    /// </summary>
    private void PaintBackground(BoxFragment fragment, ComputedStyle style, PhysicalRect borderBox, Sides<double> border, Sides<double> padding, PhysicalRect? layoutBorderBox = null, Sides<double> bleedInset = default, PhysicalRect? paintingBox = null)
    {
        var color = GetColor(style, "background-color");

        // A widget with its native appearance keeps its native background: its background color comes from the user agent
        // style sheet, an animation or a transition (an author-level one devolves the widget, see FormControlContents.IsDevolvedWidget).
        // A drop-down box with appearance: menulist-button keeps the background of CSS, as its borders (see PaintFormControl)
        if (fragment.Box.Element is { } control && ReferenceEquals(style, fragment.Box.Style) && FormControlContents.HasNativeFrame(control)
            && (control is not HtmlSelectElement || style.GetComputedValue("appearance") != "menulist-button") && FormControlContents.HasNativeAppearance(control, style))
        {
            color = FormControlContents.GetNativeBackgroundColor(control, style);
        }

        // As in Chromium, the inline boxes have no background image in forced colors mode: it would be painted above the
        // backplate of the text (https://drafts.csswg.org/css-color-adjust-1/#forced, see PaintBackplates)
        var hasImages = style.GetComputedValue("background-image") is not ("none" or "") && !(fragment.Box.IsInlineBox && style.IsForcedColorsAdjusted);
        if (color.IsTransparent && !hasImages)
            return;

        // Printed without the backgrounds: a background is solid white (see ForcesWhiteBackground)
        if (ForcesWhiteBackground(style))
        {
            color = DisplayColor.White;
            hasImages = false;
        }

        var borderShape = RoundedRect.FromBorderBox(fragment, borderBox);

        // https://drafts.csswg.org/css-borders-4/#border-shape: the backgrounds of a box whose border is shaped are clipped to
        // the second shape, or to the stroked shape (as in Blink: the specification does not define their painting area), in the
        // box of background-clip; border-area is the area of the shaped border
        var shaped = ReferenceEquals(style, fragment.Box.Style) ? BorderShapeGeometry.Get(fragment) : null;
        var shapeOrigin = (layoutBorderBox ?? borderBox).Offset;
        var shapedBackground = shaped is null ? null : PathOperations.Translate(shaped.BackgroundPath, shapeOrigin);
        var layers = hasImages ? GetBackgroundLayers(style) : [];
        var blends = HasBlendModes(layers);
        var saveCount = _builder.SaveCount;
        if (blends)
        {
            // The layers blend with the layers below them only (the background is an isolated group)
            _builder.SaveLayer(new SaveLayerItem(1, BlendMode.Normal, Filters: null) { Source = style.Element });
        }

        // The color is painted in the painting area of the bottom layer
        var bottomClip = layers.Length > 0 ? layers[^1].Clip : ParseBackgroundBox(GetLastListItem(style.GetComputedValue("background-clip")), BackgroundBox.BorderBox);
        var first = layers.Length - 1;
        if (IsClippedToText(bottomClip))
        {
            // The color and the layers above it that are clipped to the text are painted (and blended) as one group, which is
            // then clipped: clipped one by one, each of them would leave a part of what is below it on the antialiased edges
            // of the glyphs
            while (first >= 0 && layers[first].Clip == bottomClip)
            {
                first--;
            }

            PaintBackgroundClippedToText(fragment, style, BlendMode.Normal, bottomClip == BackgroundBox.BorderAreaText ? GetBorderAreaPath(borderShape, border) : null, () =>
            {
                FillRoundedRect(borderShape, new DisplayPaint(color), style.Element);
                for (var i = layers.Length - 1; i > first; i--)
                {
                    PaintBackgroundLayer(fragment, style, layers[i], borderBox, border, padding, borderShape, blends);
                }
            });
        }
        else if (!color.IsTransparent)
        {
            var attachment = layers.Length > 0 ? layers[^1].Attachment : GetLastListItem(style.GetComputedValue("background-attachment"));
            if (shaped is not null && bottomClip == BackgroundBox.BorderArea)
            {
                FillBorderArea(shaped, shapeOrigin, color, style.Element);
            }
            else if (bottomClip == BackgroundBox.BorderArea)
            {
                // https://drafts.csswg.org/css-backgrounds-4/#valdef-background-clip-border-area: the area of the borders only
                _builder.Add(new DrawPathItem(GetBorderAreaPath(borderShape, border), new DisplayPaint(color)) { Source = style.Element });
            }
            else
            {
                PhysicalRect? scrolledClip = null;
                RoundedRect area;
                if (paintingBox is { } colorBox)
                {
                    area = new RoundedRect(colorBox, default);
                }
                else
                {
                    area = GetBackgroundPaintingArea(fragment, borderShape, border, padding, bottomClip, attachment, out scrolledClip);
                    if (bottomClip == BackgroundBox.BorderBox && bleedInset != default && scrolledClip is null && area.Rect == borderBox)
                    {
                        area = area.Inset(bleedInset);
                    }
                }

                ClipToScrolledArea(scrolledClip);
                if (shapedBackground is null)
                {
                    FillRoundedRect(area, new DisplayPaint(color), style.Element);
                }
                else if (shapedBackground.RoundedRect is { Contour: null } rounded && Contains(area.Rect, rounded.Rect))
                {
                    // A rounded rectangle (or an ellipse) inside of the painting area is filled as the background of a box with border radii
                    FillRoundedRect(rounded, new DisplayPaint(color), style.Element);
                }
                else if (!shapedBackground.IsEmpty)
                {
                    _builder.Save();
                    ClipToPath(shapedBackground);
                    FillRoundedRect(area, new DisplayPaint(color), style.Element);
                    _builder.Restore();
                }

                if (scrolledClip is not null)
                {
                    _builder.Restore();
                }
            }
        }

        for (var i = first; i >= 0; i--)
        {
            var layer = layers[i];
            if (layer.Image is null)
                continue;

            if (IsClippedToText(layer.Clip))
            {
                // The layer is blended once it is clipped (it is a group: painted with its blend mode in the group, it would
                // blend with nothing)
                PaintBackgroundClippedToText(fragment, style, blends ? layer.BlendMode : BlendMode.Normal, layer.Clip == BackgroundBox.BorderAreaText ? GetBorderAreaPath(borderShape, border) : null,
                    () => PaintBackgroundLayer(fragment, style, layer, borderBox, border, padding, borderShape, blends: false));
                continue;
            }

            if (shaped is not null && layer.Clip == BackgroundBox.BorderArea)
            {
                // The layer is masked by the area of the shaped border, which can be outside of the border box
                var bounds = shaped.GetBounds();
                if (bounds.IsEmpty)
                    continue;

                var shapedArea = new RoundedRect(GetBackgroundArea(fragment, borderShape, border, padding, layer.Clip).Rect.Union(Inflate(bounds.Translate(shapeOrigin), 1)), default);
                _builder.SaveLayer(new SaveLayerItem(1, blends ? layer.BlendMode : BlendMode.Normal, Filters: null) { Source = style.Element });
                PaintBackgroundLayer(fragment, style, layer with { Clip = BackgroundBox.BorderBox }, borderBox, border, padding, shapedArea, blends: false, layoutBorderBox);
                _builder.SaveLayer(new SaveLayerItem(1, BlendMode.DestinationIn, Filters: null) { Source = style.Element });
                FillBorderArea(shaped, shapeOrigin, DisplayColor.Black, style.Element);
                _builder.Restore();
                _builder.Restore();
                continue;
            }

            if (shapedBackground is { IsEmpty: true })
                continue;

            PhysicalRect? scrolledClip = null;
            RoundedRect paintingArea;
            if (paintingBox is { } layerBox)
            {
                paintingArea = new RoundedRect(layerBox, default);
            }
            else
            {
                paintingArea = GetBackgroundPaintingArea(fragment, borderShape, border, padding, layer.Clip, layer.Attachment, out scrolledClip);
                if (layer.Clip == BackgroundBox.BorderBox && bleedInset != default && paintingArea.Rect == borderBox)
                {
                    paintingArea = paintingArea.Inset(bleedInset);
                }
            }

            ClipToScrolledArea(scrolledClip);
            PaintBackgroundLayer(fragment, style, layer, borderBox, border, padding, paintingArea, blends, layoutBorderBox, shapedBackground);
            if (scrolledClip is not null)
            {
                _builder.Restore();
            }
        }

        _builder.RestoreTo(saveCount);

        void ClipToScrolledArea(PhysicalRect? scrolledClip)
        {
            if (scrolledClip is { } rect)
            {
                _builder.Save();
                _builder.Add(new ClipRectItem(rect));
            }
        }
    }

    /// <summary>Whether a painting area is the text of the box (<c>text</c>, or the union of the text and of the borders: <c>border-area text</c>).</summary>
    private static bool IsClippedToText(BackgroundBox clip) => clip is BackgroundBox.Text or BackgroundBox.BorderAreaText;

    /// <summary>
    /// Returns the area of the borders of a box (https://drafts.csswg.org/css-backgrounds-4/#valdef-background-clip-border-area):
    /// its border box without its padding box. The style of the borders is not taken into account.
    /// </summary>
    private static DisplayPath GetBorderAreaPath(RoundedRect borderShape, Sides<double> border)
    {
        var ring = new DisplayPath { FillRule = FillRule.EvenOdd };
        ring.AddRoundedRect(borderShape);
        ring.AddRoundedRect(borderShape.Inset(border));
        return ring;
    }

    private static bool HasBlendModes(BackgroundLayer[] layers)
    {
        foreach (var layer in layers)
        {
            if (layer.BlendMode != BlendMode.Normal && layer.Image is not null)
                return true;
        }

        return false;
    }

    /// <summary>Fills a rounded rectangle (a rectangle when its corners are square).</summary>
    private void FillRoundedRect(RoundedRect area, DisplayPaint paint, Node? source)
    {
        if (area.Rect.IsEmpty)
            return;

        _builder.Add(area.IsRect ? new DrawRectItem(area.Rect, paint) { Source = source } : new DrawRoundedRectItem(area, paint) { Source = source });
    }

    /// <summary>
    /// Returns the area of a box of the background (https://drafts.csswg.org/css-backgrounds/#background-clip): the border box,
    /// the padding box or the content box, rounded by the radii of the border box reduced by the widths of the borders and the
    /// paddings (https://drafts.csswg.org/css-backgrounds/#corner-clipping).
    /// </summary>
    private static RoundedRect GetBackgroundArea(BoxFragment? fragment, RoundedRect borderShape, Sides<double> border, Sides<double> padding, BackgroundBox box)
    {
        var area = borderShape;
        if (box is BackgroundBox.PaddingBox or BackgroundBox.ContentBox)
        {
            area = area.Inset(fragment is null ? border : border.Add(fragment.Scrollbars));
            if (box == BackgroundBox.ContentBox)
            {
                area = area.Inset(padding);
            }
        }

        return area;
    }

    /// <summary>
    /// Returns the background painting area of a layer (<c>background-clip</c>). The one of a layer attached to the contents of
    /// a scroll container (https://drafts.csswg.org/css-backgrounds/#valdef-background-attachment-local: "the background
    /// painting area and background positioning area are relative to the scrollable overflow area of the box rather than to
    /// the border framing them. Because the scrollable overflow area does not include the border area, for scroll containers the
    /// border-box value of background-clip may be treated the same as padding-box") is what the padding box of the box shows
    /// of the scrolled area: the padding box for <c>border-box</c> and <c>padding-box</c>, and for <c>content-box</c> the padding
    /// box clipped to the content box of the scrollable overflow area moved by the scroll position (<paramref name="scrolledClip"/>).
    /// </summary>
    private RoundedRect GetBackgroundPaintingArea(BoxFragment fragment, RoundedRect borderShape, Sides<double> border, Sides<double> padding, BackgroundBox clip, string attachment, out PhysicalRect? scrolledClip)
    {
        scrolledClip = null;
        if (attachment != "local" || !fragment.IsScrollContainer || clip is not (BackgroundBox.BorderBox or BackgroundBox.PaddingBox or BackgroundBox.ContentBox))
            return GetBackgroundArea(fragment, borderShape, border, padding, clip);

        if (clip == BackgroundBox.ContentBox)
        {
            scrolledClip = SnapToDevicePixels(GetScrolledScrollingArea(fragment, borderShape.Rect).Contract(padding));
        }

        return GetBackgroundArea(fragment, borderShape, border, padding, BackgroundBox.PaddingBox);
    }

    /// <summary>
    /// Returns the scrollable overflow area of a scroll container (https://drafts.csswg.org/css-overflow-3/#scrollable) moved
    /// by its scroll position, in the coordinates of <paramref name="borderBox"/> (the border box of the fragment).
    /// </summary>
    private static PhysicalRect GetScrolledScrollingArea(BoxFragment fragment, PhysicalRect borderBox)
    {
        var scroll = fragment.ScrollState?.Offset ?? default;
        return fragment.GetScrollingArea().Translate(borderBox.Offset - fragment.BorderBoxRect.Offset).Translate(new PhysicalOffset(-scroll.X, -scroll.Y));
    }

    /// <summary>
    /// Paints a layer of a background (https://drafts.csswg.org/css-backgrounds/#background-image): the tiles of its image, of
    /// the size of <c>background-size</c>, at the position of <c>background-position</c> in the background positioning area
    /// (<c>background-origin</c>, the viewport for <c>background-attachment: fixed</c>, the scrolled contents of a scroll
    /// container for <c>local</c>), repeated per <c>background-repeat</c> and clipped to the painting area.
    /// </summary>
    /// <param name="fragment">The fragment of the box, or null for an SVG element (a mask layer).</param>
    private void PaintBackgroundLayer(BoxFragment? fragment, ComputedStyle style, BackgroundLayer layer, PhysicalRect borderBox, Sides<double> border, Sides<double> padding, RoundedRect paintingArea, bool blends, PhysicalRect? layoutBorderBox = null, DisplayPath? shapeClip = null)
    {
        if (layer.Image is not { } value || paintingArea.Rect.IsEmpty || PaintedImage.Resolve(_document, style, value, this) is not { } image)
            return;

        // The background positioning area, and the one of the layout (before the snapping of the border box to the device pixels)
        var origin = layer.Origin is BackgroundBox.Text or BackgroundBox.BorderArea or BackgroundBox.BorderAreaText ? BackgroundBox.BorderBox : layer.Origin;
        var positioningArea = GetBackgroundArea(fragment, new RoundedRect(borderBox, default), border, padding, origin).Rect;
        var layoutPositioningArea = layoutBorderBox is { } layoutBox ? GetBackgroundArea(fragment, new RoundedRect(layoutBox, default), border, padding, origin).Rect : (PhysicalRect?)null;
        if (layer.Attachment == "fixed" && IsUntransformedGroup())
        {
            // https://drafts.csswg.org/css-backgrounds/#valdef-background-attachment-fixed: fixed to the viewport (a transformed
            // ancestor makes it behave as scroll, as in the browsers)
            positioningArea = _viewportRect.Translate(new PhysicalOffset(-_group.Origin.X, -_group.Origin.Y));
            layoutPositioningArea = null;
        }
        else if (layer.Attachment == "local" && fragment is { IsScrollContainer: true })
        {
            // https://drafts.csswg.org/css-backgrounds/#valdef-background-attachment-local: fixed to the contents of the scroll
            // container: the scrolling area, moved by the scroll position
            positioningArea = GetScrolledScrollingArea(fragment, borderBox);
            if (layer.Origin == BackgroundBox.ContentBox)
            {
                positioningArea = positioningArea.Contract(padding);
            }

            layoutPositioningArea = null;
        }
        else if (fragment is not null && fragment.Box.IsInlineBox && style.Layout.Fragmentation.BoxDecorationBreak != BoxDecorationBreak.Clone && (!fragment.IsFirstFragment || !fragment.IsLastFragment))
        {
            // https://drafts.csswg.org/css-break-3/#valdef-box-decoration-break-slice: the background of an inline box split over
            // several lines is positioned as if the fragments were one box
            positioningArea = GetSlicedInlineArea(fragment, positioningArea);
            layoutPositioningArea = null;
        }

        else if (fragment is { StitchedBox: { } stitched })
        {
            // https://drafts.csswg.org/css-break-3/#valdef-box-decoration-break-slice: the background of a box split by the block
            // fragmentation is positioned in its fragments stitched together (and painted in the fragment)
            var isOwnBox = border == fragment.Border && padding == fragment.Padding;
            var stitchedBorderBox = SnapToDevicePixels(new PhysicalRect(borderBox.Offset + GetStitchedOrigin(fragment, stitched), stitched.Size));
            positioningArea = GetBackgroundArea(stitched, new RoundedRect(stitchedBorderBox, default), isOwnBox ? stitched.Border : border, isOwnBox ? stitched.Padding : padding, origin).Rect;
            layoutPositioningArea = null;
        }

        if (positioningArea.Width <= 0 && positioningArea.Height <= 0)
            return;

        // https://drafts.csswg.org/css-backgrounds/#background-size
        var (tileWidth, tileHeight) = GetTileSize(image, layer, positioningArea.Size);
        if (!(tileWidth > 0) || !(tileHeight > 0))
            return;

        // https://drafts.csswg.org/css-backgrounds/#valdef-background-repeat-round: the size is adjusted to fit a whole number of tiles
        var sizeIsAuto = GetSizeComponents(layer.Size);
        if (layer.RepeatX == BackgroundRepeat.Round && positioningArea.Width > 0)
        {
            var scaled = positioningArea.Width / Math.Max(1, Math.Round(positioningArea.Width / tileWidth, MidpointRounding.AwayFromZero));
            if (layer.RepeatY != BackgroundRepeat.Round && sizeIsAuto.Height)
            {
                tileHeight *= scaled / tileWidth;
            }

            tileWidth = scaled;
        }

        if (layer.RepeatY == BackgroundRepeat.Round && positioningArea.Height > 0)
        {
            var scaled = positioningArea.Height / Math.Max(1, Math.Round(positioningArea.Height / tileHeight, MidpointRounding.AwayFromZero));
            if (layer.RepeatX != BackgroundRepeat.Round && sizeIsAuto.Width)
            {
                tileWidth *= scaled / tileHeight;
            }

            tileHeight = scaled;
        }

        // https://drafts.csswg.org/css-backgrounds/#background-position, and the spacing of space
        var x = positioningArea.X + layer.PositionX.Resolve(positioningArea.Width - tileWidth);
        var y = positioningArea.Y + layer.PositionY.Resolve(positioningArea.Height - tileHeight);
        var (repeatX, spacingX) = GetSpacing(layer.RepeatX, positioningArea.Width, tileWidth, positioningArea.X, ref x);
        var (repeatY, spacingY) = GetSpacing(layer.RepeatY, positioningArea.Height, tileHeight, positioningArea.Y, ref y);

        var saveCount = _builder.SaveCount;
        _builder.Save();
        if (paintingArea.IsRect)
        {
            _builder.Add(new ClipRectItem(paintingArea.Rect));
        }
        else
        {
            _builder.Add(new ClipRoundedRectItem(paintingArea));
        }

        if (shapeClip is not null)
        {
            // The path of the border shaped by border-shape the layer is clipped to
            ClipToPath(shapeClip);
        }
        else if (layer.Clip == BackgroundBox.BorderArea)
        {
            // https://drafts.csswg.org/css-backgrounds-4/#valdef-background-clip-border-area: the area of the borders only
            if (paintingArea.Contour is not null)
            {
                // The corners shaped by corner-shape: the outside of the inner contour, in the clip of the outer one
                var bounds = paintingArea.Rect;
                _builder.Add(new ClipPathItem(DisplayPath.CreateComplement(new PhysicalRect(bounds.X - 1, bounds.Y - 1, bounds.Width + 2, bounds.Height + 2), paintingArea.Inset(border))));
            }
            else
            {
                _builder.Add(new ClipPathItem(GetBorderAreaPath(paintingArea, border)));
            }
        }

        var tile = new PhysicalRect(x, y, tileWidth, tileHeight);
        var blendMode = blends ? layer.BlendMode : BlendMode.Normal;

        // A single tile that covers the painting area (e.g. a gradient of the size of the box) is drawn without tiling
        var painted = paintingArea.Rect;
        if (spacingX == 0 && spacingY == 0 && tile.Left <= painted.Left && tile.Top <= painted.Top && tile.Right >= painted.Right && tile.Bottom >= painted.Bottom)
        {
            repeatX = false;
            repeatY = false;
        }

        if (!repeatX && !repeatY)
        {
            // An image that is not repeated is drawn as the image of a replaced box: its edges are on device pixels (as in the
            // browsers, which snap the destination rectangle of the image). The image is positioned in the area of the layout,
            // so the images of two boxes that are at the same place are painted on the same pixels, whatever the snapping
            // of their boxes. An image that covers no pixel is not painted.
            if (layoutPositioningArea is { } layoutArea)
            {
                tile = new PhysicalRect(
                    layoutArea.X + layer.PositionX.Resolve(layoutArea.Width - tileWidth),
                    layoutArea.Y + layer.PositionY.Resolve(layoutArea.Height - tileHeight),
                    tileWidth,
                    tileHeight);
            }

            var (left, top, right, bottom) = (SnapToDevicePixels(tile.Left), SnapToDevicePixels(tile.Top), SnapToDevicePixels(tile.Right), SnapToDevicePixels(tile.Bottom));
            tile = new PhysicalRect(left, top, Math.Max(0, right - left), Math.Max(0, bottom - top));
            if (tile.IsEmpty)
            {
                _builder.RestoreTo(saveCount);
                return;
            }

            if (blendMode == BlendMode.Normal)
            {
                if (image is PaintedImage.SvgPaintedImage && (tile.Width != tileWidth || tile.Height != tileHeight))
                {
                    // An SVG image is laid out in its concrete object size, then scaled to the snapped rectangle (laid out in
                    // the snapped rectangle, whose aspect ratio is not exactly the one of the image, it would be letterboxed)
                    _builder.Transform(DisplayMatrix.Translation(tile.X, tile.Y).PreConcat(DisplayMatrix.Scale(tile.Width / tileWidth, tile.Height / tileHeight)));
                    image.Draw(_builder, new PhysicalRect(0, 0, tileWidth, tileHeight), style.Element);
                }
                else
                {
                    image.Draw(_builder, tile, style.Element);
                }
            }
            else if (image.CreateShader(tile.Size) is { } single)
            {
                _builder.Add(new DrawRectItem(tile, new DisplayPaint(DisplayColor.Black) { Shader = single with { LocalTransform = PaintedImage.Translate(single.LocalTransform, tile.X, tile.Y) }, BlendMode = blendMode }) { Source = style.Element });
            }
        }
        else
        {
            // In the axis where the image is not repeated, the edges of the row (or of the column) of tiles are on device
            // pixels, as the edges of an image that is not repeated; a row that covers no pixel is not painted
            if (!repeatX)
            {
                var (left, right) = (SnapToDevicePixels(tile.Left), SnapToDevicePixels(tile.Right));
                (x, tileWidth) = (left, right - left);
            }

            if (!repeatY)
            {
                var (top, bottom) = (SnapToDevicePixels(tile.Top), SnapToDevicePixels(tile.Bottom));
                (y, tileHeight) = (top, bottom - top);
            }

            if (!(tileWidth > 0) || !(tileHeight > 0))
            {
                _builder.RestoreTo(saveCount);
                return;
            }

            tile = new PhysicalRect(x, y, tileWidth, tileHeight);
            var spreadX = repeatX ? SpreadMode.Repeat : SpreadMode.Decal;
            var spreadY = repeatY ? SpreadMode.Repeat : SpreadMode.Decal;
            DisplayShader? shader;
            if (image is PaintedImage.RasterImage raster && spacingX == 0 && spacingY == 0)
            {
                shader = new ImageShader(raster.Image, spreadX, spreadY, PaintedImage.GetSampling(style.GetComputedValue("image-rendering")))
                {
                    LocalTransform = DisplayMatrix.Translation(x, y).PreConcat(DisplayMatrix.Scale(tileWidth / raster.Image.Width, tileHeight / raster.Image.Height)),
                };
            }
            else
            {
                shader = image.CreateShader(tile.Size) is { } content
                    ? new TiledShader(content, tile.Size, new PhysicalSize(spacingX, spacingY), spreadX, spreadY) { LocalTransform = DisplayMatrix.Translation(x, y) }
                    : null;
            }

            if (shader is not null)
            {
                // The tiles cover the painting area (an axis that does not repeat only covers its tile)
                var bounds = paintingArea.Rect;
                if (!repeatX)
                {
                    bounds = Intersect(bounds, new PhysicalRect(tile.X, bounds.Y, tile.Width, bounds.Height));
                }

                if (!repeatY)
                {
                    bounds = Intersect(bounds, new PhysicalRect(bounds.X, tile.Y, bounds.Width, tile.Height));
                }

                if (!bounds.IsEmpty)
                {
                    _builder.Add(new DrawRectItem(bounds, new DisplayPaint(DisplayColor.Black) { Shader = shader, BlendMode = blendMode }) { Source = style.Element });
                }
            }
        }

        _builder.RestoreTo(saveCount);
    }

    private static bool Contains(PhysicalRect outer, PhysicalRect inner) => inner.Left >= outer.Left && inner.Top >= outer.Top && inner.Right <= outer.Right && inner.Bottom <= outer.Bottom;

    private static PhysicalRect Intersect(PhysicalRect a, PhysicalRect b)
    {
        var left = Math.Max(a.Left, b.Left);
        var top = Math.Max(a.Top, b.Top);
        var right = Math.Min(a.Right, b.Right);
        var bottom = Math.Min(a.Bottom, b.Bottom);
        return new PhysicalRect(left, top, Math.Max(0, right - left), Math.Max(0, bottom - top));
    }

    /// <summary>
    /// Returns whether a background repeats in an axis, and the spacing between its tiles for <c>space</c>
    /// (https://drafts.csswg.org/css-backgrounds/#valdef-background-repeat-space): the tiles that fit are spread from the start
    /// of the positioning area, or a single tile is placed by <c>background-position</c> when fewer than two fit.
    /// </summary>
    private static (bool Repeats, double Spacing) GetSpacing(BackgroundRepeat repeat, double areaSize, double tileSize, double areaStart, ref double position)
    {
        switch (repeat)
        {
            case BackgroundRepeat.NoRepeat:
                return (false, 0);

            case BackgroundRepeat.Space:
            {
                var count = Math.Floor((areaSize / tileSize) + 1e-9);
                if (count < 2)
                    return (false, 0);

                position = areaStart;
                return (true, (areaSize - (count * tileSize)) / (count - 1));
            }

            default:
                return (true, 0);
        }
    }

    /// <summary>Returns whether the width and the height of a value of <c>background-size</c> are auto.</summary>
    private static (bool Width, bool Height) GetSizeComponents(string size)
    {
        if (size is "auto" or "auto auto" or "")
            return (true, true);

        if (size is "cover" or "contain")
            return (false, false);

        var parts = Cascade.CssValueComputer.SplitSpaceSeparated(size);
        return parts.Length == 1 ? (parts[0] == "auto", true) : (parts[0] == "auto", parts[1] == "auto");
    }

    /// <summary>
    /// Returns the size of the tiles of a layer (https://drafts.csswg.org/css-backgrounds/#background-size): <c>cover</c> and
    /// <c>contain</c> scale the image by its natural aspect ratio, the explicit sizes with an auto side use the natural aspect
    /// ratio or the natural size, and the image without natural dimensions (a gradient) fills the positioning area
    /// (https://drafts.csswg.org/css-images-3/#default-sizing).
    /// </summary>
    private static (double Width, double Height) GetTileSize(PaintedImage image, BackgroundLayer layer, PhysicalSize area)
    {
        var naturalWidth = image.NaturalWidth;
        var naturalHeight = image.NaturalHeight;
        var ratio = image.NaturalRatio;
        var size = layer.Size;
        if (size is "cover" or "contain")
        {
            if (ratio is not { } r)
                return (area.Width, area.Height);

            var cover = size == "cover";
            var widthFromHeight = area.Height * r;
            if (cover ? widthFromHeight >= area.Width : widthFromHeight <= area.Width)
                return (widthFromHeight, area.Height);

            return (area.Width, area.Width / r);
        }

        var parts = Cascade.CssValueComputer.SplitSpaceSeparated(size);
        double? width = null;
        double? height = null;
        if (parts.Length >= 1 && parts[0] != "auto" && LengthPercentage.TryParse(parts[0], layer.Zoom, nonNegative: true, out var w))
        {
            width = Math.Max(0, w.Resolve(area.Width));
        }

        if (parts.Length >= 2 && parts[1] != "auto" && LengthPercentage.TryParse(parts[1], layer.Zoom, nonNegative: true, out var h))
        {
            height = Math.Max(0, h.Resolve(area.Height));
        }

        if (width is { } fixedWidth && height is { } fixedHeight)
            return (fixedWidth, fixedHeight);

        if (width is { } onlyWidth)
            return (onlyWidth, ratio is { } r1 ? onlyWidth / r1 : naturalHeight ?? area.Height);

        if (height is { } onlyHeight)
            return (ratio is { } r2 ? onlyHeight * r2 : naturalWidth ?? area.Width, onlyHeight);

        // auto auto: the natural size, else the size given by the ratio, else the positioning area
        if (naturalWidth is { } nw && naturalHeight is { } nh)
            return (nw, nh);

        if (ratio is { } r3)
        {
            // One natural dimension and a natural aspect ratio (an SVG image with a width and a viewBox): the other dimension is
            // given by the ratio
            if (naturalWidth is { } onlyNaturalWidth)
                return (onlyNaturalWidth, onlyNaturalWidth / r3);

            if (naturalHeight is { } onlyNaturalHeight)
                return (onlyNaturalHeight * r3, onlyNaturalHeight);

            var containWidth = Math.Min(area.Width, area.Height * r3);
            return (containWidth, containWidth / r3);
        }

        return (naturalWidth ?? area.Width, naturalHeight ?? area.Height);
    }

    /// <summary>Whether no stacking context of the painted ones has a transform (the coordinates of the group are the ones of the viewport).</summary>
    private bool IsUntransformedGroup()
    {
        for (var group = _group; group is not null; group = group.Parent)
        {
            if (group.IsTransformed)
                return false;
        }

        return true;
    }

    /// <summary>
    /// Returns the background positioning area of a fragment of an inline box split over several lines
    /// (https://drafts.csswg.org/css-break-3/#valdef-box-decoration-break-slice): the area of the box as if its fragments were laid
    /// out side by side in the inline axis, from the first one.
    /// </summary>
    private static PhysicalRect GetSlicedInlineArea(BoxFragment fragment, PhysicalRect area)
    {
        var box = fragment.Box;
        var horizontal = fragment.WritingDirection.IsHorizontal;
        var before = 0d;
        var total = 0d;
        var found = false;
        for (var current = box.FirstFragment as BoxFragment; current is not null; current = current.NextFragmentOfBox as BoxFragment)
        {
            var size = horizontal ? current.Size.Width : current.Size.Height;
            if (ReferenceEquals(current, fragment))
            {
                found = true;
            }
            else if (!found)
            {
                before += size;
            }

            total += size;
        }

        if (!found)
            return area;

        var own = horizontal ? fragment.Size.Width : fragment.Size.Height;
        var extra = total - own;
        var rtl = fragment.WritingDirection.InlineStart is PhysicalSide.Right or PhysicalSide.Bottom;
        var shift = rtl ? extra - before : before;
        return horizontal
            ? new PhysicalRect(area.X - shift, area.Y, area.Width + extra, area.Height)
            : new PhysicalRect(area.X, area.Y - shift, area.Width, area.Height + extra);
    }

    /// <summary>Returns the layers of the background of a style (parsed once per style and painting).</summary>
    private BackgroundLayer[] GetBackgroundLayers(ComputedStyle style)
    {
        _backgroundLayers ??= [];
        if (_backgroundLayers.TryGetValue(style, out var cached))
            return cached;

        var images = ValueMatcher.SplitAtCommas(ValueMatcher.RemoveWhitespace(CssParser.ParseListOfComponentValues(style.GetComputedValue("background-image"))));
        var positionsX = CssBackgrounds.SplitList(style.GetComputedValue("background-position-x"));
        var positionsY = CssBackgrounds.SplitList(style.GetComputedValue("background-position-y"));
        var sizes = CssBackgrounds.SplitList(style.GetComputedValue("background-size"));
        var repeats = CssBackgrounds.SplitList(style.GetComputedValue("background-repeat"));
        var attachments = CssBackgrounds.SplitList(style.GetComputedValue("background-attachment"));
        var origins = CssBackgrounds.SplitList(style.GetComputedValue("background-origin"));
        var clips = CssBackgrounds.SplitList(style.GetComputedValue("background-clip"));
        var blendModes = CssBackgrounds.SplitList(style.GetComputedValue("background-blend-mode"));
        var zoom = style.Layout.EffectiveZoom;
        var layers = new BackgroundLayer[images.Count];
        for (var i = 0; i < images.Count; i++)
        {
            var image = images[i] is [var single] && !(single is CssPreservedToken { Type: CssTokenType.Ident } ident && InfraAscii.EqualsIgnoreAsciiCase(ident.Token.Value, "none")) ? single : null;
            var (repeatX, repeatY) = ParseRepeat(Get(repeats, i, "repeat"));
            layers[i] = new BackgroundLayer(
                image,
                ParsePosition(Get(positionsX, i, "0%"), zoom, "left", "right"),
                ParsePosition(Get(positionsY, i, "0%"), zoom, "top", "bottom"),
                Get(sizes, i, "auto"),
                repeatX,
                repeatY,
                Get(attachments, i, "scroll"),
                ParseBackgroundBox(Get(origins, i, "padding-box"), BackgroundBox.PaddingBox),
                ParseBackgroundBox(Get(clips, i, "border-box"), BackgroundBox.BorderBox),
                ParseBlendMode(Get(blendModes, i, "normal")))
            {
                Zoom = zoom,
            };
        }

        _backgroundLayers[style] = layers;
        return layers;

        static string Get(List<string> values, int index, string defaultValue) => values.Count == 0 ? defaultValue : values[index % values.Count].Trim();
    }

    /// <summary>Parses a computed position of an axis (an offset, or a keyword followed by an offset from that side).</summary>
    private static LengthPercentage ParsePosition(string value, double zoom, string startKeyword, string endKeyword)
    {
        if (LengthPercentage.TryParse(value, zoom, nonNegative: false, out var offset))
            return offset;

        var parts = Cascade.CssValueComputer.SplitSpaceSeparated(value);
        if (parts.Length == 0)
            return LengthPercentage.Zero;

        var keyword = parts[0];
        var length = parts.Length > 1 && LengthPercentage.TryParse(parts[1], zoom, nonNegative: false, out var parsed) ? parsed : LengthPercentage.Zero;
        if (keyword == "center")
            return LengthPercentage.FromPercentage(50);

        if (keyword == endKeyword || keyword is "x-end" or "y-end")
        {
            // right 10px is calc(100% - 10px)
            return length.HasPercentage ? LengthPercentage.FromPercentage(100) : LengthPercentage.FromSum(-length.Length, 100);
        }

        return keyword == startKeyword || keyword is "x-start" or "y-start" ? length : LengthPercentage.Zero;
    }

    private static (BackgroundRepeat X, BackgroundRepeat Y) ParseRepeat(string value)
    {
        switch (value)
        {
            case "repeat-x":
                return (BackgroundRepeat.Repeat, BackgroundRepeat.NoRepeat);
            case "repeat-y":
                return (BackgroundRepeat.NoRepeat, BackgroundRepeat.Repeat);
        }

        var parts = value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var x = parts.Length > 0 ? Parse(parts[0]) : BackgroundRepeat.Repeat;
        var y = parts.Length > 1 ? Parse(parts[1]) : x;
        return (x, y);

        static BackgroundRepeat Parse(string keyword) => keyword switch
        {
            "space" => BackgroundRepeat.Space,
            "round" => BackgroundRepeat.Round,
            "no-repeat" => BackgroundRepeat.NoRepeat,
            _ => BackgroundRepeat.Repeat,
        };
    }

    /// <summary>
    /// Paints a background clipped to the text of the box (https://drafts.csswg.org/css-backgrounds-4/#valdef-background-clip-text:
    /// "the intersection of the border box and the geometry of the text including any decorations and emphasis marks in the
    /// element and its in-flow and floated descendants"): the background is painted in a group, which keeps only the pixels
    /// covered by the glyphs, the decoration lines and the emphasis marks of the text fragments of the box and of its
    /// descendants that are not absolutely positioned, and by <paramref name="borderAreaPath"/> (the area of the borders, for
    /// <c>border-area text</c>: "the background is painted within (clipped to) the union of these two areas"). The group is
    /// composited with <paramref name="blendMode"/>.
    /// </summary>
    private void PaintBackgroundClippedToText(BoxFragment fragment, ComputedStyle style, BlendMode blendMode, DisplayPath? borderAreaPath, Action paint)
    {
        var saveCount = _builder.SaveCount;
        _builder.SaveLayer(new SaveLayerItem(1, blendMode, Filters: null) { Source = style.Element });
        paint();
        _builder.SaveLayer(new SaveLayerItem(1, BlendMode.DestinationIn, Filters: null) { Source = style.Element });
        var previous = _textPaintOverride;
        _textPaintOverride = new DisplayPaint(DisplayColor.Black);
        if (borderAreaPath is not null)
        {
            _builder.Add(new DrawPathItem(borderAreaPath, new DisplayPaint(DisplayColor.Black)) { Source = style.Element });
        }

        PaintTextMask(fragment, truncation: null);
        _textPaintOverride = previous;
        _builder.RestoreTo(saveCount);

        // The glyphs of the lines as they are painted: truncated by text-overflow, with their ellipses
        void PaintTextMask(Fragment parent, LineTruncation? truncation)
        {
            foreach (var child in parent.ChildrenSpan)
            {
                // The content hidden after the clamp point of a line-clamp container (see LineClamp)
                if (child.IsHiddenForPaint)
                    continue;

                switch (child)
                {
                    case LineBoxFragment line:
                        var lineTruncation = LineTruncator.GetTruncation(line);
                        PaintTextMask(line, lineTruncation);
                        if (!line.IsTruncatedByBlockEllipsis)
                        {
                            PaintEllipsis(line.BlockEllipsis);
                        }

                        PaintEllipsis(lineTruncation?.LeftEllipsis);
                        PaintEllipsis(lineTruncation?.RightEllipsis);
                        break;

                    case TextFragment text:
                        if (truncation?.IsHidden(text) == true)
                            break;

                        var clipped = ApplyTruncationClip(truncation);
                        PaintText(text, text.GetOffsetInRoot() - _group.Origin);
                        if (clipped)
                        {
                            _builder.Restore();
                        }

                        break;

                    // The absolutely positioned descendants are not in-flow nor floated
                    case BoxFragment { Box: var box } when PositionedLayout.IsAbsolutelyPositioned(box):
                        break;

                    default:
                        PaintTextMask(child, truncation);
                        break;
                }
            }
        }

        void PaintEllipsis(LineEllipsis? ellipsis)
        {
            if (ellipsis is null)
                return;

            foreach (var text in ellipsis.Fragments)
            {
                PaintText(text, text.GetOffsetInRoot() - _group.Origin);
            }
        }
    }

    private static BackgroundBox ParseBackgroundBox(string value, BackgroundBox defaultValue) => value switch
    {
        "border-box" => BackgroundBox.BorderBox,
        "padding-box" => BackgroundBox.PaddingBox,
        "content-box" => BackgroundBox.ContentBox,
        "text" => BackgroundBox.Text,
        "border-area" => BackgroundBox.BorderArea,
        "border-area text" or "text border-area" => BackgroundBox.BorderAreaText,
        _ => defaultValue,
    };
}
