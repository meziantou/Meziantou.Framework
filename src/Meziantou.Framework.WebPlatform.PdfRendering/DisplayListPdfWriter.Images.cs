using Meziantou.Framework.WebPlatform.Css.Layout;
using Meziantou.Framework.WebPlatform.Imaging;
using Meziantou.Framework.WebPlatform.Rendering;
using ImagingImage = Meziantou.Framework.Imaging.Image;
using PdfImage = Meziantou.Framework.Pdf.PdfImage;
using Rgba32 = Meziantou.Framework.Imaging.Rgba32;

namespace Meziantou.Framework.WebPlatform.PdfRendering;

internal sealed partial class DisplayListPdfWriter
{
    /// <summary>Draws the area of an image scaled into a rectangle: the image is clipped to the rectangle when only a part of it is drawn.</summary>
    private void DrawImage(DrawImageItem draw)
    {
        var source = draw.SourceRect;
        var destination = draw.DestinationRect;
        if (!(source.Width > 0) || !(source.Height > 0) || !(destination.Width > 0) || !(destination.Height > 0))
            return;

        if (_owner.GetImage(draw.Image) is not { } image)
            return;

        // The source rectangle is in the natural pixels of the image
        var naturalWidth = draw.Image.Width > 0 ? draw.Image.Width : image.Width;
        var naturalHeight = draw.Image.Height > 0 ? draw.Image.Height : image.Height;
        var scaleX = destination.Width / source.Width;
        var scaleY = destination.Height / source.Height;
        var x = destination.X - (source.X * scaleX);
        var y = destination.Y - (source.Y * scaleY);
        var width = naturalWidth * scaleX;
        var height = naturalHeight * scaleY;

        Mark(draw);
        _builder.SaveGraphicsState();
        if (draw.Paint is { } paint)
        {
            var alpha = float.IsNaN(paint.Color.A) ? 0 : Math.Clamp(paint.Color.A, 0, 1);
            if (alpha <= 0)
            {
                _builder.RestoreGraphicsState();
                return;
            }

            var blend = ToPdf(paint.BlendMode);
            if (alpha < 1 || blend != Pdf.PdfBlendMode.Normal)
            {
                _builder.SetGraphicsState(_owner.GetGraphicsState(alpha, blend));
            }
        }

        if (source.X > 0 || source.Y > 0 || source.Right < naturalWidth || source.Bottom < naturalHeight)
        {
            _builder.Rectangle(destination.X, destination.Y, destination.Width, destination.Height).Clip().EndPath();
        }

        // The first row of an image is at the top of its unit square: the y axis of the items points down
        _builder.DrawImage(image, x, y + height, width, -height);
        _builder.RestoreGraphicsState();
    }

    /// <summary>
    /// Rasterizes items (a balanced sequence of the display list) in the CSS px of the page, at the rasterization scale of
    /// the document, and draws the image at their place: in the area of their bounds (in the coordinates of the items), or of
    /// the clip when the bounds are not known.
    /// </summary>
    private void Rasterize(IReadOnlyList<DisplayItem> items, PhysicalRect? localBounds, DisplayItem? firstItem)
    {
        var area = localBounds is { } bounds ? PdfGeometry.Intersect(_state.Clip, PdfGeometry.Transform(_state.ToPage, bounds)) : _state.Clip;
        if (area.IsEmpty || PdfGeometry.Invert(_state.ToPage) is not { } toItems)
            return;

        var scale = _owner.Options.RasterizationScale;
        var list = new DisplayItem[items.Count + 3];
        list[0] = SaveItem.Instance;
        list[1] = new TransformItem(_state.ToPage);
        for (var i = 0; i < items.Count; i++)
        {
            list[i + 2] = items[i];
        }

        list[^1] = RestoreItem.Instance;
        var (pixels, pixelWidth, pixelHeight) = ImagingScreenshotExtensions.Render(_owner.Document, new DisplayList(list, default), area, new ScreenshotOptions { DevicePixelRatio = scale });
        if (IsTransparent(pixels))
            return;

        using var bitmap = ImagingImage.ImportPixelBytes<Rgba32>(pixels, pixelWidth, pixelHeight, pixelWidth * 4);
        var image = PdfImage.FromImage(bitmap);

        // The pixels cover the area rounded outwards to the pixels of the scale (as the screenshots)
        const double Epsilon = 1e-6;
        var left = Math.Floor((area.Left * scale) + Epsilon) / scale;
        var top = Math.Floor((area.Top * scale) + Epsilon) / scale;
        var width = pixelWidth / scale;
        var height = pixelHeight / scale;
        if (firstItem is not null)
        {
            Mark(firstItem);
        }

        _builder.SaveGraphicsState();
        _builder.Transform(PdfGeometry.ToPdf(toItems));
        _builder.DrawImage(image, left, top + height, width, -height);
        _builder.RestoreGraphicsState();

        static bool IsTransparent(byte[] pixels)
        {
            for (var i = 3; i < pixels.Length; i += 4)
            {
                if (pixels[i] != 0)
                    return false;
            }

            return true;
        }
    }
}
