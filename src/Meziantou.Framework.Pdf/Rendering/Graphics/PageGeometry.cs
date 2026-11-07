namespace Meziantou.Framework.Pdf.Rendering.Graphics;

/// <summary>The size of the image of a page and the matrix from its default user space to the pixels (ISO 32000-2, 14.11.2 and 7.7.3.3).</summary>
internal static class PageGeometry
{
    // The largest side of the image of a page
    private const double MaxSide = 1 << 15;

    public static (int Width, int Height, PdfMatrix Matrix) Compute(PdfPage page, PdfRenderOptions options)
    {
        var box = Intersect(page.CropBox, page.MediaBox);
        if (options.Region is { } region)
        {
            box = Intersect(region, page.MediaBox, fallback: null) ?? throw new ArgumentException("The region does not intersect the media box of the page", nameof(options));
        }

        var userUnit = page.UserUnit is > 0 and var unit && double.IsFinite(unit) ? unit : 1;
        var width = box.Right - box.Left;
        var height = box.Top - box.Bottom;
        var rotation = page.Rotation;
        var (shownWidth, shownHeight) = rotation is 90 or 270 ? (height, width) : (width, height);

        // The pixels per unit of the default user space: from the resolution, or from the size of the image
        var scale = (options.Width, options.Height) switch
        {
            ({ } w, { } h) => Math.Min(w / shownWidth, h / shownHeight),
            ({ } w, null) => w / shownWidth,
            (null, { } h) => h / shownHeight,
            _ => options.Dpi / 72 * userUnit,
        };
        var (deviceWidth, deviceHeight) = (shownWidth * scale, shownHeight * scale);
        if (!(deviceWidth >= 0.5 && deviceHeight >= 0.5) || deviceWidth > MaxSide || deviceHeight > MaxSide)
            throw new PdfResourceLimitException($"The page is {deviceWidth} x {deviceHeight} pixels at {options.Dpi} dpi");

        // The box moved to the origin, then rotated clockwise and flipped: the y axis of the image goes down
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

    private static PdfRectangle Intersect(PdfRectangle crop, PdfRectangle media) => Intersect(crop, media, media)!.Value;

    private static PdfRectangle? Intersect(PdfRectangle box, PdfRectangle media, PdfRectangle? fallback)
    {
        var left = Math.Max(Math.Min(box.Left, box.Right), media.Left);
        var bottom = Math.Max(Math.Min(box.Bottom, box.Top), media.Bottom);
        var right = Math.Min(Math.Max(box.Left, box.Right), media.Right);
        var top = Math.Min(Math.Max(box.Bottom, box.Top), media.Top);
        return right > left && top > bottom ? new PdfRectangle(left, bottom, right, top) : fallback;
    }
}
