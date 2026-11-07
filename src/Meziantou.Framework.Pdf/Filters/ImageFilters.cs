using System.Globalization;
using Meziantou.Framework.Imaging;
using Meziantou.Framework.Pdf.Objects;

namespace Meziantou.Framework.Pdf.Filters;

/// <summary>
/// The filters whose data is an image (<c>DCTDecode</c>, ISO 32000-2, 7.4.8; <c>JPXDecode</c>, 7.4.9), decoded by the
/// codecs of <c>Meziantou.Framework.Imaging</c> into the samples of the image, one byte per component.
/// </summary>
/// <remarks>
/// The data must be of the format of the filter: a JPEG datastream for <c>DCTDecode</c>, a JPEG 2000 file or codestream
/// for <c>JPXDecode</c>. Another image format is not accepted under a filter that does not declare it.
/// </remarks>
internal static class ImageFilters
{
    private const byte MarkerPrefix = 0xFF;
    private const byte StartOfImage = 0xD8;
    private const byte StartOfScan = 0xDA;
    private const byte App14 = 0xEE;

    private static readonly PdfName ColorTransform = PdfName.Create("ColorTransform");

    public static byte[] DecodeDct(ReadOnlySpan<byte> input, PdfDictionary? parameters, long limit)
    {
        var transformed = ApplyColorTransform(input, parameters);
        return Decode(transformed is null ? input : transformed, ImageFormat.Jpeg, "DCTDecode", limit);
    }

    public static byte[] DecodeJpx(ReadOnlySpan<byte> input, long limit) => Decode(input, ImageFormat.Jpeg2000, "JPXDecode", limit);

    /// <summary>
    /// Gives the JPEG decoder the color transform the parameters of <c>DCTDecode</c> declare (7.4.8, table 13): an Adobe
    /// APP14 segment (Adobe Technical Note #5116, section 18) inserted after the start of the image. The segment of the data
    /// wins over the parameter, so the data is kept as it is when it has one, or when the parameter is not set.
    /// </summary>
    /// <returns>The data with the segment, or <see langword="null"/> when the data is kept as it is.</returns>
    public static byte[]? ApplyColorTransform(ReadOnlySpan<byte> data, PdfDictionary? parameters)
    {
        if (parameters?.GetResolved(ColorTransform) is not PdfNumber value)
            return null;

        if (data.Length < 4 || data[0] != MarkerPrefix || data[1] != StartOfImage)
            return null;

        // The segments before the first scan: an Adobe segment, and the number of components of the frame header
        var components = 0;
        var position = 2;
        while (position + 4 <= data.Length && data[position] == MarkerPrefix)
        {
            var marker = data[position + 1];
            if (marker == MarkerPrefix)
            {
                // A fill byte
                position++;
                continue;
            }

            if (marker == StartOfScan)
                break;

            var length = (data[position + 2] << 8) | data[position + 3];
            if (marker == App14 && length >= 7 && data[(position + 4)..].StartsWith("Adobe"u8))
                return null;

            // The frame headers (SOF0 to SOF15, without DHT, JPG and DAC): the number of components after the precision and the size
            if (marker is >= 0xC0 and <= 0xCF and not 0xC4 and not 0xC8 and not 0xCC && position + 9 < data.Length)
            {
                components = data[position + 9];
            }

            position += 2 + length;
        }

        if (components is not (3 or 4))
            return null;

        // 1 transforms three components from YCbCr and four from YCCK; 0 keeps them as they are
        var transform = value.DoubleValue == 0 ? (byte)0 : components == 3 ? (byte)1 : (byte)2;

        // "Adobe", the version (100), two flags of 0 and the transform
        ReadOnlySpan<byte> segment = [MarkerPrefix, App14, 0, 14, (byte)'A', (byte)'d', (byte)'o', (byte)'b', (byte)'e', 0, 100, 0, 0, 0, 0, transform];
        var result = new byte[data.Length + segment.Length];
        data[..2].CopyTo(result);
        segment.CopyTo(result.AsSpan(2));
        data[2..].CopyTo(result.AsSpan(2 + segment.Length));
        return result;
    }

    private static byte[] Decode(ReadOnlySpan<byte> input, ImageFormat format, string filter, long limit)
    {
        try
        {
            var info = Image.Identify(input);
            if (info.Format != format)
                throw new InvalidPdfContentException($"The {filter} data is a {info.Format} image, not a {format} image");

            if (info.ColorModel is ImageColorModel.Cmyk or ImageColorModel.Ycck)
                throw new UnsupportedPdfFeatureException($"The {filter} data has four components (CMYK): it is not supported yet");

            var gray = info.ColorModel is ImageColorModel.Grayscale or ImageColorModel.GrayscaleAlpha;
            var components = gray ? 1 : 3;
            var length = (long)info.Width * info.Height * components;
            if (length > limit)
                throw new PdfResourceLimitException(string.Create(CultureInfo.InvariantCulture, $"The decoded data of a stream exceeds {limit} bytes ({nameof(PdfReadOptions)}.{nameof(PdfReadOptions.MaxDecodedStreamLength)})"));

            if (length > Array.MaxLength)
                throw new PdfResourceLimitException(string.Create(CultureInfo.InvariantCulture, $"The decoded data of the {filter} image ({length} bytes) exceeds the maximum length of an array"));

            return gray
                ? Load<Gray8>(input, info, (int)length, filter)
                : Load<Rgb24>(input, info, (int)length, filter);
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

    /// <summary>
    /// Decodes the pixels into an array of the size checked against the limit. The decoder keeps its pixels in pooled
    /// memory, so this array is the only large allocation: it is not zeroed, because the copy writes every byte of it.
    /// </summary>
    private static byte[] Load<TPixel>(ReadOnlySpan<byte> input, ImageInfo info, int length, string filter)
        where TPixel : unmanaged
    {
        using var image = Image.Load<TPixel>(input);
        if (image.Width != info.Width || image.Height != info.Height)
            throw new InvalidPdfContentException($"The {filter} data decodes to {image.Width}x{image.Height} pixels, not the {info.Width}x{info.Height} pixels of its header");

        var result = unsafe(GC.AllocateUninitializedArray<byte>(length));
        image.Frames[0].CopyPixelBytesTo(result);
        return result;
    }
}
