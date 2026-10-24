using Meziantou.Framework.Imaging;

namespace Meziantou.Framework.Pdf.Filters;

/// <summary>
/// The filters whose data is an image (<c>DCTDecode</c>, ISO 32000-2, 7.4.8; <c>JPXDecode</c>, 7.4.9), decoded by the
/// codecs of <c>Meziantou.Framework.Imaging</c> into the samples of the image, one byte per component.
/// </summary>
internal static class ImageFilters
{
    public static byte[] DecodeDct(ReadOnlySpan<byte> input, long limit) => Decode(input, "DCTDecode", limit);

    public static byte[] DecodeJpx(ReadOnlySpan<byte> input, long limit) => Decode(input, "JPXDecode", limit);

    private static byte[] Decode(ReadOnlySpan<byte> input, string filter, long limit)
    {
        try
        {
            var info = Image.Identify(input);
            if (info.ColorModel is ImageColorModel.Cmyk or ImageColorModel.Ycck)
                throw new UnsupportedPdfFeatureException($"The {filter} data has four components (CMYK): it is not supported yet");

            var gray = info.ColorModel is ImageColorModel.Grayscale or ImageColorModel.GrayscaleAlpha;
            var components = gray ? 1 : 3;
            if ((long)info.Width * info.Height * components > limit)
                throw new PdfResourceLimitException($"The decoded data of a stream exceeds {limit} bytes ({nameof(PdfReadOptions)}.{nameof(PdfReadOptions.MaxDecodedStreamLength)})");

            if (gray)
            {
                using var image = Image.Load<Gray8>(input);
                var result = new byte[image.Width * image.Height];
                image.Frames[0].CopyPixelBytesTo(result);
                return result;
            }
            else
            {
                using var image = Image.Load<Rgb24>(input);
                var result = new byte[image.Width * image.Height * 3];
                image.Frames[0].CopyPixelBytesTo(result);
                return result;
            }
        }
        catch (ImageException ex) when (ex is not ImageResourceLimitException)
        {
            throw new InvalidPdfContentException($"The {filter} data is not a valid image: {ex.Message}", ex);
        }
        catch (ImageResourceLimitException ex)
        {
            throw new PdfResourceLimitException($"The {filter} image exceeds a limit: {ex.Message}", ex);
        }
    }
}
