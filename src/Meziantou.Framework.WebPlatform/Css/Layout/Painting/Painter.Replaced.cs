using System.Globalization;
using Meziantou.Framework.WebPlatform.Css.Cascade;
using Meziantou.Framework.WebPlatform.Css.Syntax;
using Meziantou.Framework.WebPlatform.Css.Values;
using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.Rendering;
using Meziantou.Framework.WebPlatform.Svg;

namespace Meziantou.Framework.WebPlatform.Css.Layout;

// The contents of the replaced boxes (https://drafts.csswg.org/css2/#replaced-element): the images, the poster frames, the
// images of the generated content, the SVG document fragments of the outermost svg elements, the documents of the iframe,
// frame, object and embed elements and the form controls (Painter.FormControls.cs). The SVG content is painted by Painter.Svg.cs,
// the canvases by #379.
internal sealed partial class Painter
{
    /// <summary>Paints the contents of a replaced box, in the clip of its overflow clip edge.</summary>
    private void PaintReplacedContent(BoxFragment fragment, PhysicalOffset offset)
    {
        // https://drafts.csswg.org/css-contain-2/#content-visibility: the skipped contents of a replaced element are not painted
        if (fragment.Box.SkipsContents)
            return;

        // https://drafts.csswg.org/css-backgrounds/#corner-clipping: "the content of replaced elements is always trimmed to the
        // content edge curve": to the inner edge of a border shaped by border-shape (the overflow clip of the box when it clips)
        if (!(fragment.ClipsOverflowX && fragment.ClipsOverflowY) && BorderShapeGeometry.Get(fragment) is { } shape)
        {
            _builder.Save();
            ClipToPath(PathOperations.Translate(shape.InnerPath, fragment.BorderBoxRect.Translate(offset).Offset));
            PaintReplacedContentCore(fragment, offset);
            _builder.Restore();
            return;
        }

        PaintReplacedContentCore(fragment, offset);
    }

    private void PaintReplacedContentCore(BoxFragment fragment, PhysicalOffset offset)
    {

        // The images of a view transition (https://drafts.csswg.org/css-view-transitions-1/#view-transition-pseudo-elements)
        if (fragment.Box.PseudoElement is { } pseudoElement && ViewTransitionPseudoTree.IsViewTransitionPseudoElement(pseudoElement))
        {
            PaintViewTransitionImage(fragment, offset, pseudoElement);
            return;
        }

        // The images of the generated content and of the list markers (https://drafts.csswg.org/css-content-3/#content-property)
        if (fragment.Box.GeneratedImage is { } generated)
        {
            var values = ValueMatcher.RemoveWhitespace(CssParser.ParseListOfComponentValues(generated));
            if (values.Count > 0 && PaintedImage.Resolve(_document, fragment.Box.Style, values[0], this) is { } image)
            {
                image.Draw(_builder, SnapToDevicePixels((fragment.ReplacedContentRect ?? fragment.ContentBoxRect).Translate(offset)), fragment.Box.Element);
            }

            return;
        }

        switch (fragment.Box.Element)
        {
            case HtmlImageElement image when image.GetPaintImage() is { } paintImage:
                PaintImage(fragment, offset, paintImage);
                break;

            case HtmlVideoElement video:
                // The poster frame, or the frame of the video the media backend gives
                if (video.GetPaintImage() is { } frame)
                {
                    PaintImage(fragment, offset, frame);
                }

                PaintCues(fragment);
                break;

            case HtmlCanvasElement canvas:
                PaintCanvas(fragment, offset, canvas);
                break;

            case SvgSvgElement svg when BoxGeneration.IsSvgRoot(svg):
                PaintSvgContent(fragment, svg, offset);
                break;

            case HtmlInputElement input when input.GetImageButtonPaintImage() is { } buttonImage:
                PaintImage(fragment, offset, buttonImage);
                break;

            case HtmlInputElement or HtmlTextAreaElement or HtmlSelectElement or HtmlProgressElement or HtmlMeterElement:
                PaintFormControl(fragment, offset);
                break;

            case HtmlObjectElement { Image: { } objectImage }:
                PaintImage(fragment, offset, objectImage);
                break;

            case HtmlEmbedElement { Image: { } embedImage }:
                PaintImage(fragment, offset, embedImage);
                break;

            case HtmlIFrameElement or HtmlFrameElement or HtmlEmbedElement or HtmlObjectElement:
                PaintNestedDocument(fragment, offset);
                break;
        }
    }

    /// <summary>
    /// Paints the image of a <c>::view-transition-old()</c> or <c>::view-transition-new()</c> pseudo-element
    /// (https://drafts.csswg.org/css-view-transitions-1/#view-transition-pseudo-elements) in the rectangle of <c>object-fit</c>: the
    /// snapshot of the old element (the bitmap rasterized by the rendering backend when it was captured), or the live image of
    /// the new element (its painting, https://drafts.csswg.org/css-view-transitions-1/#capture-the-image). The natural size of
    /// an image is the size of the border box of its element: the ink overflow is painted outside of the rectangle.
    /// </summary>
    private void PaintViewTransitionImage(BoxFragment fragment, PhysicalOffset offset, string pseudoElement)
    {
        // The view transition layer of the document is not in the images of the captured elements; the pseudo-elements of a scoped
        // view transition of a descendant are (https://drafts.csswg.org/css-view-transitions-2/#scoped-vt-layout: they are in the
        // box tree of their root element)
        if (fragment.Box.Element is not { } originatingElement || _document.GetViewTransitionPseudoTree(originatingElement) is not { } tree
            || (_isViewTransitionCapture && _document.GetScopedViewTransition(originatingElement) is null)
            || !ViewTransitionPseudoTree.TryParse(pseudoElement, out var type, out var name) || tree.FindGroup(name) is not { } group)
        {
            return;
        }

        var captured = group.Captured;
        var naturalSize = type == "view-transition-old" ? captured.OldImage?.NaturalSize ?? default : captured.NewSize;

        // An image displayed at its natural size is not snapped to the device pixels: its painting is (see
        // ViewTransitionSnapshot.Origin); the rectangle of a scaled image is snapped as the one of the other images
        var destination = (fragment.ReplacedContentRect ?? fragment.ContentBoxRect).Translate(offset);
        var pixel = 1 / _devicePixelRatio;
        if (Math.Abs(destination.Width - naturalSize.Width) >= pixel || Math.Abs(destination.Height - naturalSize.Height) >= pixel)
        {
            destination = SnapToDevicePixels(destination);
        }

        if (destination.IsEmpty)
            return;

        if (type == "view-transition-old")
        {
            if (captured.OldImage is not { NaturalSize: { Width: > 0, Height: > 0 } natural } snapshot)
                return;

            var scaleX = destination.Width / natural.Width;
            var scaleY = destination.Height / natural.Height;
            if (snapshot.Image is { Width: > 0, Height: > 0 } image)
            {
                var sampling = fragment.Box.Style.GetComputedValue("image-rendering") switch
                {
                    "pixelated" or "crisp-edges" or "optimizespeed" => ImageSampling.Pixelated,
                    "high-quality" => ImageSampling.HighQuality,
                    _ => ImageSampling.Smooth,
                };
                var bounds = snapshot.Bounds.Translate(new PhysicalOffset(-snapshot.Origin.X, -snapshot.Origin.Y));
                var rect = new PhysicalRect(destination.X + (bounds.X * scaleX), destination.Y + (bounds.Y * scaleY), bounds.Width * scaleX, bounds.Height * scaleY);
                _builder.Add(new DrawImageItem(image, new PhysicalRect(0, 0, image.Width, image.Height), rect, sampling) { Source = fragment.Box.Element });
                return;
            }

            // Without rendering backend, the snapshot is its display list
            DrawViewTransitionContent(snapshot.Content, snapshot.Origin, destination, scaleX, scaleY, snapshot.Bounds);
            return;
        }

        if (type != "view-transition-new" || captured.NewElement is not { } element || _rootFragment is not { } root || captured.NewSize is not { Width: > 0, Height: > 0 } size)
            return;

        BoxFragment? elementFragment = null;
        if (captured.NewPseudoElement is not null || !ReferenceEquals(element, _document.DocumentElement))
        {
            var box = captured.NewPseudoElement is null ? _engine.BoxTree?.GetPrincipalBox(element) : _engine.BoxTree?.GetPseudoElementBox(element, captured.NewPseudoElement);
            if (box is null || _engine.GetLaidOutFirstFragment(box) is not BoxFragment first)
                return;

            elementFragment = first;
        }

        // The image of the document element is "the region of document [...] that intersects the snapshot containing block"
        var origin = elementFragment is null ? PhysicalOffset.Zero : captured.NewOrigin;
        var content = ViewTransitionCapture.Paint(_document, _engine, root, elementFragment, origin, _paintOrder);
        DrawViewTransitionContent(content, origin, destination, destination.Width / size.Width, destination.Height / size.Height, elementFragment is null ? new PhysicalRect(0, 0, size.Width, size.Height) : null);
    }

    /// <summary>Draws the painting of a captured element, whose border box is at <paramref name="origin"/>, in a rectangle.</summary>
    private void DrawViewTransitionContent(DisplayList content, PhysicalOffset origin, PhysicalRect destination, double scaleX, double scaleY, PhysicalRect? clip = null)
    {
        _builder.Save();
        _builder.Transform(DisplayMatrix.Translation(destination.X, destination.Y));
        if (scaleX != 1 || scaleY != 1)
        {
            _builder.Transform(DisplayMatrix.Scale(scaleX, scaleY));
        }

        if (origin.X != 0 || origin.Y != 0)
        {
            _builder.Transform(DisplayMatrix.Translation(-origin.X, -origin.Y));
        }

        if (clip is { } clipRect)
        {
            _builder.Add(new ClipRectItem(clipRect));
        }

        _builder.AddList(content);
        _builder.Restore();
    }

    /// <summary>
    /// Paints the cues of the text tracks rendered over a video element (https://w3c.github.io/webvtt/#rendering): the
    /// fragments of the boxes of the cues and of the regions are the children of the fragment of the video element (see
    /// <see cref="WebVttCueLayout"/>); each one is painted as a stacking context, over the frame of the video, in the order
    /// of the output of the rules for updating the display of WebVTT text tracks.
    /// </summary>
    private void PaintCues(BoxFragment fragment)
    {
        foreach (var child in fragment.ChildrenSpan)
        {
            if (child is not BoxFragment cue)
                continue;

            var items = new List<PaintItem>();
            PaintOrder.CollectStackingContext(cue, items, markers: true);
            var saveCount = _builder.SaveCount;
            _builder.Save();
            PaintItems(items);
            _builder.RestoreTo(saveCount);
        }
    }

    /// <summary>
    /// Paints the document of the navigable of an iframe, frame, embed or object element (https://html.spec.whatwg.org/multipage/rendering.html#embedded-content-rendering-rules):
    /// its viewport is the content box of the element, the display list of the document is drawn in it (also when the document
    /// is not same origin with the container document), clipped to the content box. The viewport of a document that has natural
    /// dimensions (an SVG document) is the rectangle given by <c>object-fit</c> and <c>object-position</c>.
    /// </summary>
    private void PaintNestedDocument(BoxFragment fragment, PhysicalOffset offset)
    {
        // (the content navigable of a frame element that does not have a frameset parent is not rendered, see FramesetFormattingContext)
        var element = fragment.Box.Element!;
        if (NavigableContainers.GetContentNavigable(element)?.Document is not { } document || ReferenceEquals(document, _document)
            || (element is HtmlFrameElement && !FramesetFormattingContext.IsFramesetChild(element)) || document.GetDisplayList() is not { } displayList)
        {
            return;
        }

        var content = SnapToDevicePixels(fragment.ContentBoxRect.Translate(offset));
        if (content.IsEmpty)
            return;

        var viewport = fragment.ReplacedContentRect is { } objectRect ? SnapToDevicePixels(objectRect.Translate(offset)) : content;
        _builder.Save();
        _builder.Add(new ClipRectItem(content));
        if (BorderRadii.Get(fragment) is not null)
        {
            // https://drafts.csswg.org/css-backgrounds/#corner-clipping: "the content of replaced elements is always trimmed to
            // the content edge curve"
            var border = GetSnappedBorderBox(fragment, offset);
            var rounded = RoundedRect.FromBorderBox(fragment, border).Inset(new Sides<double>(content.Top - border.Top, border.Right - content.Right, border.Bottom - content.Bottom, content.Left - border.Left));
            if (!rounded.IsRect)
            {
                _builder.Add(new ClipRoundedRectItem(rounded));
            }
        }

        _builder.Transform(DisplayMatrix.Translation(viewport.X, viewport.Y));

        // The effective zoom of the container scales the nested document: its viewport is the content box in the zoomed CSS
        // pixels of the container (see LayoutEngine.GetNestedViewportSize), and its device pixel ratio is multiplied by the zoom
        var zoom = fragment.Box.Style.Layout.EffectiveZoom;
        if (zoom != 1 && zoom > 0)
        {
            _builder.Transform(DisplayMatrix.Scale(zoom, zoom));
        }

        _builder.AddList(displayList);
        _builder.Restore();
    }

    /// <summary>
    /// Paints the bitmap of a canvas (https://html.spec.whatwg.org/multipage/canvas.html#the-canvas-element): the bitmap of its 2D
    /// context, the output bitmap of its <see cref="ImageBitmapRenderingContext"/> (over black when the context has no alpha
    /// channel) or the last frame committed by its <see cref="OffscreenCanvas"/>, scaled to the rectangle of <c>object-fit</c>.
    /// A canvas without context is transparent.
    /// </summary>
    private void PaintCanvas(BoxFragment fragment, PhysicalOffset offset, HtmlCanvasElement canvas)
    {
        if (_document.RenderingBackend is not { } backend)
            return;

        PaintImage? image;
        bool opaque;
        switch (canvas.RenderingContext)
        {
            case CanvasRenderingContext2D context:
                image = context.Bitmap.GetSnapshot();
                opaque = false;
                break;

            case ImageBitmapRenderingContext context:
                image = context.OutputBitmap?.GetPaintImage(backend);
                opaque = !context.Alpha;
                break;

            default:
                image = canvas.PlaceholderFrame?.Image;
                opaque = false;
                break;
        }

        var destination = SnapToDevicePixels((fragment.ReplacedContentRect ?? fragment.ContentBoxRect).Translate(offset));
        if (destination.IsEmpty)
            return;

        if (opaque)
        {
            _builder.Add(new DrawRectItem(destination, new DisplayPaint(DisplayColor.Black)) { Source = canvas });
        }

        if (image is not null)
        {
            PaintImage(fragment, offset, image);
        }
    }

    /// <summary>
    /// Whether the painting of the contents of a replaced box clips them to its content box. The overflow of the img, video and
    /// canvas elements is clipped by the <c>overflow: clip</c> of the user agent style sheet (https://drafts.csswg.org/css-overflow-4/#overflow-control),
    /// which the author can replace by <c>overflow: visible</c>; the contents of the other replaced boxes are always clipped.
    /// </summary>
    private static bool ClipsReplacedContent(BoxFragment fragment)
        => !fragment.ClipsOverflow && fragment.Box.Element is not (HtmlImageElement or HtmlVideoElement or HtmlCanvasElement);

    /// <summary>
    /// Paints an image in the rectangle given by <c>object-fit</c> and <c>object-position</c> (<see cref="BoxFragment.ReplacedContentRect"/>),
    /// sampled per <c>image-rendering</c> (https://drafts.csswg.org/css-images-3/#the-image-rendering).
    /// </summary>
    private void PaintImage(BoxFragment fragment, PhysicalOffset offset, PaintImage image)
    {
        var unsnappedDestination = (fragment.ReplacedContentRect ?? fragment.ContentBoxRect).Translate(offset);
        var destination = SnapToDevicePixels(unsnappedDestination);
        if (destination.IsEmpty)
            return;

        // The SVG images are painted by their document, laid out in the rectangle of the concrete object size
        if (image.IsSvg)
        {
            if (SvgImage.Get(image, _document) is not { } svgImage)
                return;

            _builder.Save();
            if (ClipsReplacedContent(fragment))
            {
                _builder.Add(new ClipRectItem(SnapToDevicePixels(fragment.ContentBoxRect.Translate(offset))));
            }

            new PaintedImage.SvgPaintedImage(svgImage, fragment.Box.Style.Layout.EffectiveZoom, CssValueComputer.GetUsedColorScheme(fragment.Box.Style)).Draw(_builder, destination, fragment.Box.Element, unsnappedDestination.Size);
            _builder.Restore();
            return;
        }

        if (image.Width <= 0 || image.Height <= 0)
            return;

        // https://drafts.csswg.org/css-images-3/#the-image-orientation: the EXIF orientation of the image (unless it is none), and
        // the frame of an animated image displayed now
        image = GetDisplayedFrame(image.WithOrientation(fragment.Box.Style.Layout.Replaced.ImageOrientation.FromImage));

        var sampling = fragment.Box.Style.GetComputedValue("image-rendering") switch
        {
            "pixelated" or "crisp-edges" or "optimizespeed" => ImageSampling.Pixelated,
            "high-quality" => ImageSampling.HighQuality,
            _ => ImageSampling.Smooth,
        };
        // https://drafts.csswg.org/css-images-3/#the-object-fit: the parts of the image outside of the content box are clipped
        var content = SnapToDevicePixels(fragment.ContentBoxRect.Translate(offset));
        var clips = ClipsReplacedContent(fragment) && (destination.Left < content.Left || destination.Top < content.Top || destination.Right > content.Right || destination.Bottom > content.Bottom);
        if (clips)
        {
            _builder.Save();
            _builder.Add(new ClipRectItem(content));
        }

        _builder.Add(new DrawImageItem(image, new PhysicalRect(0, 0, image.Width, image.Height), destination, sampling) { Source = fragment.Box.Element });
        if (clips)
        {
            _builder.Restore();
        }
    }
}
