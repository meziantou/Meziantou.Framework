using Meziantou.Framework.Imaging;
using Meziantou.Framework.Pdf.Objects;
using Meziantou.Framework.Pdf.Rendering.Images;

namespace Meziantou.Framework.Pdf.Redaction;

/// <summary>
/// Erases the pixels of an image that the areas of a redaction cover: the image is decoded to sRGB with its alpha, the
/// pixels whose cell touches an area become transparent, and a new image replaces it. Its masks are part of the alpha, so
/// nothing of the original samples or masks is left in the new image.
/// </summary>
internal static class ImageRedactor
{
    private static readonly PdfName[] KeptKeys = [PdfName.Create("Interpolate"), PdfName.Create("Intent"), PdfName.Create("OC"), PdfName.Create("StructParent")];

    /// <summary>Erases the pixels of an image under the areas.</summary>
    /// <param name="document">The document the new image is added to.</param>
    /// <param name="decoded">The decoded image, or <see langword="null"/> when the image has no pixels.</param>
    /// <param name="dictionary">The image dictionary, with the keys of inline images expanded.</param>
    /// <param name="matrix">The matrix from the unit square of the image to the default user space of the page.</param>
    /// <param name="areas">The areas, in default user space.</param>
    /// <returns>The outcome: the image is kept as it is, removed (no pixel is left), or replaced by a new image.</returns>
    public static (ImageRedactionKind Kind, PdfReference? Image) Redact(PdfDocument document, DecodedImage? decoded, PdfDictionary dictionary, PdfMatrix matrix, IReadOnlyList<PdfRectangle> areas)
    {
        // An image without pixels, or painted in a degenerate parallelogram, paints nothing
        if (decoded is null || !matrix.TryInvert(out var inverse))
            return (ImageRedactionKind.Removed, null);

        using var image = decoded.Image.CloneAs<Rgba32>();
        var width = image.Width;
        var height = image.Height;
        var erased = false;
        foreach (var area in areas)
        {
            if (!TryGetPixelBounds(area, inverse, width, height, out var left, out var top, out var right, out var bottom))
                continue;

            // An image painted without rotation or skew: the box of the area is the pixels it touches. Otherwise, the
            // area is a parallelogram in the image, and each pixel of its box is tested
            var axisAligned = (matrix.B == 0 && matrix.C == 0) || (matrix.A == 0 && matrix.D == 0);
            image.Frames[0].ProcessPixelRows(rows =>
            {
                for (var y = top; y < bottom; y++)
                {
                    var row = rows.GetRowSpan(y);
                    for (var x = left; x < right; x++)
                    {
                        if (axisAligned || CellTouches(matrix, area, x, y, width, height))
                        {
                            row[x] = default;
                            erased = true;
                        }
                    }
                }
            });
        }

        if (!erased)
            return (ImageRedactionKind.Unchanged, null);

        var painted = false;
        image.Frames[0].ProcessPixelRows(rows =>
        {
            for (var y = 0; y < rows.Height && !painted; y++)
            {
                foreach (var pixel in rows.GetRowSpan(y))
                {
                    if (pixel.A != 0)
                    {
                        painted = true;
                        break;
                    }
                }
            }
        });

        if (!painted)
            return (ImageRedactionKind.Removed, null);

        PdfReference reference;
        if (decoded.IsStencil)
        {
            reference = document.Objects.Add(CreateStencilMask(image));
        }
        else
        {
            // A JPEG image stays a JPEG image: the losses of a second compression are small next to the size of its samples
            var pdfImage = IsJpeg(dictionary) ? PdfImage.FromImageAsJpeg(image) : PdfImage.FromImage(image);
            reference = pdfImage.GetReference(document);
        }

        var stream = (PdfStream)reference.Resolve()!;
        foreach (var key in KeptKeys)
        {
            if (dictionary[key] is { } value)
            {
                stream.Dictionary[key] = value;
            }
        }

        return (ImageRedactionKind.Replaced, reference);
    }

    /// <summary>Gets the pixels whose cell touches an area: the bounding box of the area in the pixel grid of the image.</summary>
    private static bool TryGetPixelBounds(PdfRectangle area, PdfMatrix inverse, int width, int height, out int left, out int top, out int right, out int bottom)
    {
        // The unit square of the image: (0, 0) is the lower-left corner of the last row (ISO 32000-2, 8.9.4)
        Span<(double X, double Y)> corners = [inverse.Transform(area.Left, area.Bottom), inverse.Transform(area.Right, area.Bottom), inverse.Transform(area.Left, area.Top), inverse.Transform(area.Right, area.Top)];
        var minX = double.PositiveInfinity;
        var maxX = double.NegativeInfinity;
        var minY = double.PositiveInfinity;
        var maxY = double.NegativeInfinity;
        foreach (var (u, v) in corners)
        {
            var x = u * width;
            var y = (1 - v) * height;
            minX = Math.Min(minX, x);
            maxX = Math.Max(maxX, x);
            minY = Math.Min(minY, y);
            maxY = Math.Max(maxY, y);
        }

        left = Clamp(Math.Floor(minX), width);
        right = Clamp(Math.Ceiling(maxX), width);
        top = Clamp(Math.Floor(minY), height);
        bottom = Clamp(Math.Ceiling(maxY), height);
        return left < right && top < bottom;

        static int Clamp(double value, int max) => double.IsNaN(value) ? 0 : (int)Math.Clamp(value, 0, max);
    }

    /// <summary>Indicates whether the cell of a pixel, painted by the matrix, overlaps an area: no axis of the area or of the cell separates them.</summary>
    private static bool CellTouches(PdfMatrix matrix, PdfRectangle area, int x, int y, int width, int height)
    {
        // The corners of the cell in the unit square of the image, (0, 0) at the lower-left corner of the last row
        var u0 = (double)x / width;
        var u1 = (double)(x + 1) / width;
        var v0 = 1 - ((double)(y + 1) / height);
        var v1 = 1 - ((double)y / height);
        Span<(double X, double Y)> cell = [matrix.Transform(u0, v0), matrix.Transform(u1, v0), matrix.Transform(u1, v1), matrix.Transform(u0, v1)];
        Span<(double X, double Y)> rectangle = [(area.Left, area.Bottom), (area.Right, area.Bottom), (area.Right, area.Top), (area.Left, area.Top)];
        Span<(double X, double Y)> axes = [(1, 0), (0, 1), (-(cell[1].Y - cell[0].Y), cell[1].X - cell[0].X), (-(cell[3].Y - cell[0].Y), cell[3].X - cell[0].X)];
        foreach (var (axisX, axisY) in axes)
        {
            var (cellMin, cellMax) = Project(cell, axisX, axisY);
            var (areaMin, areaMax) = Project(rectangle, axisX, axisY);
            if (cellMax <= areaMin || areaMax <= cellMin)
                return false;
        }

        return true;

        static (double Min, double Max) Project(ReadOnlySpan<(double X, double Y)> points, double axisX, double axisY)
        {
            var min = double.PositiveInfinity;
            var max = double.NegativeInfinity;
            foreach (var (px, py) in points)
            {
                var value = (px * axisX) + (py * axisY);
                min = Math.Min(min, value);
                max = Math.Max(max, value);
            }

            return (min, max);
        }
    }

    /// <summary>Creates a stencil mask (8.9.6.2) from the alpha of the image: a sample 0 paints the fill color.</summary>
    private static PdfStream CreateStencilMask(Image<Rgba32> image)
    {
        var width = image.Width;
        var height = image.Height;
        var stride = (width + 7) / 8;
        var data = new byte[(long)stride * height];
        image.Frames[0].ProcessPixelRows(rows =>
        {
            for (var y = 0; y < height; y++)
            {
                var row = rows.GetRowSpan(y);
                var output = data.AsSpan(y * stride, stride);
                for (var x = 0; x < width; x++)
                {
                    if (row[x].A < 128)
                    {
                        output[x >> 3] |= (byte)(0x80 >> (x & 7));
                    }
                }
            }
        });

        var dictionary = new PdfDictionary
        {
            [PdfNames.Type] = PdfNames.XObject,
            [PdfNames.Subtype] = PdfNames.Image,
            [PdfNames.Width] = PdfInteger.Get(width),
            [PdfNames.Height] = PdfInteger.Get(height),
            [PdfNames.ImageMask] = PdfBoolean.True,
            [PdfNames.BitsPerComponent] = PdfInteger.Get(1),
        };
        var stream = new PdfStream(dictionary);
        stream.SetData(data, compressible: true);
        return stream;
    }

    private static bool IsJpeg(PdfDictionary dictionary) => dictionary.GetResolved(PdfNames.Filter) switch
    {
        PdfName name => name.Value is "DCTDecode" or "DCT",
        PdfArray { Count: > 0 } array => array.GetResolved(array.Count - 1) is PdfName { Value: "DCTDecode" or "DCT" },
        _ => false,
    };
}
