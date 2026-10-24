namespace Meziantou.Framework.Pdf.Rendering.Graphics;

/// <summary>The size of the image of a page and the matrix from its default user space to the pixels (ISO 32000-2, 14.11.2 and 7.7.3.3).</summary>
internal static class PageGeometry
{
    // The largest side of the image of a page
    private const double MaxSide = 1 << 15;

    public static (int Width, int Height, PdfMatrix Matrix) Compute(PdfPage page, double dpi)
    {
        var box = Intersect(page.CropBox, page.MediaBox);
        var scale = dpi / 72 * page.UserUnit;
        var width = box.Right - box.Left;
        var height = box.Top - box.Bottom;
        var rotation = page.Rotation;
        var (deviceWidth, deviceHeight) = rotation is 90 or 270 ? (height * scale, width * scale) : (width * scale, height * scale);
        if (!(deviceWidth >= 0.5 && deviceHeight >= 0.5) || deviceWidth > MaxSide || deviceHeight > MaxSide)
            throw new PdfResourceLimitException($"The page is {deviceWidth} x {deviceHeight} pixels at {dpi} dpi");

        // The crop box moved to the origin, then rotated clockwise and flipped: the y axis of the image goes down
        var origin = PdfMatrix.CreateTranslation(-box.Left, -box.Bottom);
        var orientation = rotation switch
        {
            90 => new PdfMatrix(0, scale, scale, 0, 0, 0),
            180 => new PdfMatrix(-scale, 0, 0, scale, width * scale, 0),
            270 => new PdfMatrix(0, -scale, -scale, 0, height * scale, width * scale),
            _ => new PdfMatrix(scale, 0, 0, -scale, 0, height * scale),
        };

        // A fraction of a pixel is left out, as viewers do: an A4 page at 36 dpi is 297 x 420 pixels
        return (Math.Max(1, (int)Math.Floor(deviceWidth + 0.001)), Math.Max(1, (int)Math.Floor(deviceHeight + 0.001)), origin * orientation);
    }

    private static PdfRectangle Intersect(PdfRectangle crop, PdfRectangle media)
    {
        var left = Math.Max(crop.Left, media.Left);
        var bottom = Math.Max(crop.Bottom, media.Bottom);
        var right = Math.Min(crop.Right, media.Right);
        var top = Math.Min(crop.Top, media.Top);
        return right > left && top > bottom ? new PdfRectangle(left, bottom, right, top) : media;
    }
}
