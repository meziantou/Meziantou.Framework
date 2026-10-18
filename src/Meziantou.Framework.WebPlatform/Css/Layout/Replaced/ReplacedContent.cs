using Meziantou.Framework.WebPlatform.Css.Cascade;
using Meziantou.Framework.WebPlatform.Css.Om;
using Meziantou.Framework.WebPlatform.Css.Syntax;
using Meziantou.Framework.WebPlatform.Css.Values;
using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.Html;

namespace Meziantou.Framework.WebPlatform.Css.Layout;

/// <summary>The kind of content of a replaced box.</summary>
internal enum ReplacedContentKind : byte
{
    /// <summary>An image, a video, a canvas, an SVG document or a nested document: its painted rectangle follows <c>object-fit</c> and <c>object-position</c>.</summary>
    Object,

    /// <summary>A form control (https://html.spec.whatwg.org/multipage/rendering.html#widgets).</summary>
    FormControl,
}

/// <summary>
/// The contents of a replaced box (https://drafts.csswg.org/css-display/#replaced-element): their natural dimensions
/// (https://drafts.csswg.org/css-images-3/#natural-dimensions), in the coordinates of the fragment tree (CSS px multiplied by
/// the effective zoom), and the baseline of the form controls. Computed by <see cref="Get"/> for each layout of the box.
/// </summary>
internal sealed class ReplacedContent
{
    /// <summary>The default object size (https://drafts.csswg.org/css-images-3/#default-object-size) of the replaced elements: 300x150.</summary>
    public const double DefaultWidth = 300;

    public const double DefaultHeight = 150;

    /// <summary>The natural dimensions, physical (each of them may be absent).</summary>
    public ImageNaturalSize NaturalSize { get; private init; }

    /// <summary>
    /// The natural dimensions of the painted contents, physical: the ones of <see cref="NaturalSize"/> without the size
    /// containment (the box is sized without its contents, which are still fitted in it by <c>object-fit</c>), and the size
    /// of the <c>object-view-box</c> when there is one (see <see cref="ViewBox"/>).
    /// </summary>
    public ImageNaturalSize ObjectNaturalSize { get; private init; }

    /// <summary>
    /// The view box of <c>object-view-box</c> (https://drafts.csswg.org/css-images-5/#the-object-view-box) in the rectangle
    /// of the natural dimensions of the contents (<see cref="ViewBoxContentSize"/>), or null: the part of the contents that
    /// is fitted in the content box, as if it was the whole contents.
    /// </summary>
    public PhysicalRect? ViewBox { get; private init; }

    /// <summary>The natural size of the whole contents when there is a <see cref="ViewBox"/>.</summary>
    public PhysicalSize ViewBoxContentSize { get; private init; }

    public ReplacedContentKind Kind { get; private init; }

    /// <summary>The baseline of a form control (see <see cref="FormControlBaseline"/>).</summary>
    public FormControlBaseline Baseline { get; private init; }

    /// <summary>The number of lines of text centered in the content box of a form control (see <see cref="FormControlSize.Lines"/>).</summary>
    public int FormControlLines { get; private init; } = 1;

    /// <summary>The default object size of the box, physical: 300x150, or 1em square for the image of a list marker (https://drafts.csswg.org/css-lists-3/#image-markers).</summary>
    public PhysicalSize DefaultObjectSize { get; private init; }

    /// <summary>Returns the contents of a replaced box.</summary>
    public static ReplacedContent Get(LayoutContext context, ContainerBox box)
    {
        var layout = box.Style.Layout;
        var zoom = layout.EffectiveZoom;
        var defaultSize = new PhysicalSize(DefaultWidth * zoom, DefaultHeight * zoom);
        var kind = ReplacedContentKind.Object;
        var baseline = FormControlBaseline.None;
        var lines = 1;
        ImageNaturalSize natural;
        if (box.PseudoElement is { } viewTransitionPseudoElement && ViewTransitionPseudoTree.IsViewTransitionPseudoElement(viewTransitionPseudoElement))
        {
            // https://drafts.csswg.org/css-view-transitions-1/#capture-the-image: "the natural dimensions of this image must be those
            // of its principal border box" (the snapshot containing block for the image of the document element)
            natural = default;
            if (box.Element is { } viewTransitionRoot && context.Document.GetViewTransitionPseudoTree(viewTransitionRoot) is { } tree && ViewTransitionPseudoTree.TryParse(viewTransitionPseudoElement, out var type, out var name) && tree.FindGroup(name) is { } group)
            {
                var size = type == "view-transition-old" ? group.Captured.OldImage?.NaturalSize ?? default : group.Captured.NewSize;
                natural = new ImageNaturalSize(size.Width, size.Height, size.Width > 0 && size.Height > 0 ? size.Width / size.Height : null);
            }
        }
        else if (box.GeneratedImage is { } image)
        {
            // https://drafts.csswg.org/css-images-3/#image-orientation: image-orientation applies to the images of the generated content
            if (image.StartsWith("cross-fade(", StringComparison.Ordinal))
            {
                // https://drafts.csswg.org/css-images-4/#cross-fade-function: the weighted average of the natural dimensions of its images
                var values = ValueMatcher.RemoveWhitespace(CssParser.ParseListOfComponentValues(image));
                var crossFade = values.Count > 0 ? PaintedImage.Resolve(context.Document, box.Style, values[0]) : null;
                natural = crossFade is null ? new ImageNaturalSize(0, 0, null) : new ImageNaturalSize(crossFade.NaturalWidth, crossFade.NaturalHeight, crossFade.NaturalRatio);
            }
            else
            {
                natural = CssImageFetching.GetNaturalSize(context.Document, image);
                natural = (layout.Replaced.ImageOrientation.FromImage ? natural : natural.WithoutOrientation()).Scale(zoom);
            }

            if (box.PseudoElement == "marker")
            {
                defaultSize = new PhysicalSize(layout.FontSize, layout.FontSize);
            }
        }
        else if (box.Element is not { } element)
        {
            natural = default;
        }
        else if (FormControlSizing.Get(element, box.Style, ScrollContainers.GetScrollbarThickness(context, layout) / zoom) is { } control)
        {
            kind = ReplacedContentKind.FormControl;
            baseline = control.Baseline;
            lines = control.Lines;
            var horizontal = layout.IsHorizontalWritingMode;

            // The rows of a list box are boxes: the width of its labels is their max-content inline size
            if (FormControlContents.GetListBoxLabelsInlineSize(context, box) is { } labelsInlineSize)
            {
                control = control with { InlineSize = (labelsInlineSize + ScrollContainers.GetScrollbarThickness(context, layout)) / zoom };
            }

            var (width, height) = horizontal ? (control.InlineSize, control.BlockSize) : (control.BlockSize, control.InlineSize);

            // A form control has no natural aspect ratio: its sizes are independent
            natural = new ImageNaturalSize(width * zoom, height * zoom, null);
        }
        else
        {
            natural = GetElementNaturalSize(element, layout).Scale(zoom);
        }

        // https://drafts.csswg.org/css-images-5/#the-object-view-box: the view box is the contents of the element for its
        // sizing and for object-fit and object-position; the contents outside of it are painted (they are clipped by the
        // content box). As in Chrome, it does not apply to the contents without natural size, nor to the nested documents,
        // and a view box that is empty is ignored.
        PhysicalRect? viewBox = null;
        var viewBoxContentSize = default(PhysicalSize);
        if (kind == ReplacedContentKind.Object && natural is { Width: > 0 and var naturalWidth, Height: > 0 and var naturalHeight }
            && box.Element is not (HtmlIFrameElement or HtmlFrameElement or HtmlObjectElement or HtmlEmbedElement)
            && GetViewBox(box.Style, new PhysicalSize(naturalWidth, naturalHeight), zoom) is { } rect)
        {
            viewBox = rect;
            viewBoxContentSize = new PhysicalSize(naturalWidth, naturalHeight);
            natural = new ImageNaturalSize(rect.Width, rect.Height, rect.Width / rect.Height);
        }

        var objectNatural = natural;

        // https://drafts.csswg.org/css-contain-2/#containment-size (also when it skips its contents, content-visibility): "the
        // element is treated as having no natural aspect ratio" and its natural size is the one of contain-intrinsic-size
        // (https://drafts.csswg.org/css-sizing-4/#intrinsic-size-override)
        if (IntrinsicSizing.HasSizeContainment(box, inlineAxis: true))
        {
            var horizontal = layout.IsHorizontalWritingMode;
            var both = IntrinsicSizing.HasSizeContainment(box, inlineAxis: false);
            var containWidth = horizontal || both;
            var containHeight = !horizontal || both;
            natural = new ImageNaturalSize(
                containWidth ? LastRememberedSizes.GetContainIntrinsicSize(box, horizontal: true) : natural.Width,
                containHeight ? LastRememberedSizes.GetContainIntrinsicSize(box, horizontal: false) : natural.Height,
                null);

            // A drop-down box is a widget, not a replaced element: as in the browsers (WPT css/css-contain/contain-size-select-*,
            // css/css-sizing/contain-intrinsic-size/contain-intrinsic-size-032.html), its contents are its label, so with size
            // containment and contain-intrinsic-size: none, it is sized as an empty drop-down box (its arrow and one line)
            if (kind == ReplacedContentKind.FormControl && box.Element is HtmlSelectElement select && StyleEngine.IsDropDownBox(select))
            {
                var (emptyInline, emptyBlock) = FormControlSizing.GetEmptyDropDownBoxSize(box.Style);
                var (emptyWidth, emptyHeight) = horizontal ? (emptyInline * zoom, emptyBlock * zoom) : (emptyBlock * zoom, emptyInline * zoom);
                natural = new ImageNaturalSize(
                    containWidth && !LastRememberedSizes.HasExplicitContainIntrinsicSize(box, horizontal: true) ? emptyWidth : natural.Width,
                    containHeight && !LastRememberedSizes.HasExplicitContainIntrinsicSize(box, horizontal: false) ? emptyHeight : natural.Height,
                    null);
            }
        }

        return new ReplacedContent
        {
            NaturalSize = natural,
            ObjectNaturalSize = objectNatural,
            ViewBox = viewBox,
            ViewBoxContentSize = viewBoxContentSize,
            Kind = kind,
            Baseline = baseline,
            FormControlLines = lines,
            DefaultObjectSize = defaultSize,
        };
    }

    /// <summary>
    /// Returns the rectangle of <c>object-view-box</c> (an <c>inset()</c>, <c>rect()</c> or <c>xywh()</c> shape whose reference box
    /// is the rectangle of the natural dimensions of the contents), or null for <c>none</c>, an empty rectangle or the whole contents.
    /// </summary>
    private static PhysicalRect? GetViewBox(ComputedStyle style, PhysicalSize contentSize, double zoom)
    {
        var value = style.GetComputedValue("object-view-box");
        if (value is "none" or "")
            return null;

        var components = ValueMatcher.RemoveWhitespace(CssParser.ParseListOfComponentValues(value));
        if (components is not [CssFunction shape])
            return null;

        var content = new PhysicalRect(0, 0, contentSize.Width, contentSize.Height);
        if (BasicShapePaths.Resolve(shape, content, zoom) is not { Rectangle: { } rounded })
            return null;

        var rect = rounded.Rect;
        return rect.Width > 0 && rect.Height > 0 && rect != content ? rect : null;
    }

    /// <summary>Returns the natural dimensions of a replaced element (not a form control), in the coordinates of the element (not zoomed).</summary>
    private static ImageNaturalSize GetElementNaturalSize(Element element, LayoutStyle layout)
    {
        switch (element)
        {
            case HtmlImageElement image:
                return image.GetRenderedNaturalSize(layout.Replaced.ImageOrientation.FromImage);

            case HtmlInputElement input:
                // An image button that represents its image (see FormControlSizing.Get)
                return input.GetImageButtonNaturalSize(layout.Replaced.ImageOrientation.FromImage);

            case HtmlCanvasElement canvas:
                // https://html.spec.whatwg.org/multipage/canvas.html#the-canvas-element: the natural dimensions of the bitmap
                return ImageNaturalSize.FromPixels(canvas.Width, canvas.Height);

            case HtmlVideoElement video:
                // https://html.spec.whatwg.org/multipage/media.html#the-video-element: the natural size of the poster frame when
                // the element represents it, else the one of the video, else no natural dimensions (the default object size)
                return video.GetNaturalSize();

            case HtmlAudioElement:
                // https://html.spec.whatwg.org/multipage/rendering.html#embedded-content-rendering-rules: "about one line high, as
                // wide as is necessary to expose the user agent's user interface features" (the size of the controls of Chrome)
                return new ImageNaturalSize(DefaultWidth, 54, null);

            case HtmlObjectElement { Representation: ObjectElementRepresentation.Image } obj:
                return obj.ImageNaturalSize;

            case HtmlEmbedElement { Image: not null } embed:
                return embed.ImageNaturalSize;

            case HtmlFrameElement frame when !FramesetFormattingContext.IsFramesetChild(frame):
                // https://html.spec.whatwg.org/multipage/rendering.html#frames-and-framesets: "The contents of a frame element that
                // does not have a frameset parent are expected to be rendered as transparent black; [...] its content navigable is
                // expected to have a viewport with zero width and zero height"
                return new ImageNaturalSize(0, 0, null);

            case HtmlObjectElement or HtmlEmbedElement:
                // The natural size of the SVG document of the content navigable (https://svgwg.org/svg2-draft/coords.html#SizingSVGInCSS)
                return GetContentDocumentNaturalSize(element);

            default:
                // https://svgwg.org/svg2-draft/coords.html#SizingSVGInCSS: the outermost svg element of an HTML document has the
                // natural size of its width and height attributes (they are also presentation attributes of the width and height
                // properties, which may override them, e.g. width: max-content), as an SVG document
                if (BoxGeneration.IsSvgRoot(element))
                    return ImageHeaders.GetSvgNaturalSize(element.GetAnimatedAttributeValue("width"), element.GetAnimatedAttributeValue("height"), element.GetAnimatedAttributeValue("viewBox"));

                // An iframe, a frame, an embed or an object without SVG document: no natural dimensions
                return default;
        }
    }

    /// <summary>Returns the natural dimensions of the document displayed by an embed or an object element when its root is an svg element.</summary>
    private static ImageNaturalSize GetContentDocumentNaturalSize(Element element)
    {
        if (NavigableContainers.GetContentNavigable(element)?.Document.DocumentElement is { } root && BoxGeneration.IsSvgRoot(root))
            return ImageHeaders.GetSvgNaturalSize(root.GetAnimatedAttributeValue("width"), root.GetAnimatedAttributeValue("height"), Svg.SvgUserSpace.GetViewBoxValue(root));

        return default;
    }
}
