using Meziantou.Framework.Imaging;
using Meziantou.Framework.Imaging.Formats;

namespace Meziantou.Framework.WebPlatform.Imaging;

/// <summary>
/// Encodes the bitmaps as JPEG and WebP image files with the codecs of Meziantou.Framework.Imaging
/// (https://html.spec.whatwg.org/multipage/canvas.html#a-serialisation-of-the-bitmap-as-a-file). The PNG files are encoded by
/// the core library.
/// </summary>
internal static class ImagingImageEncoder
{
    /// <summary>The default quality of the JPEG images of the browsers.</summary>
    private const double DefaultJpegQuality = 0.92;

    /// <summary>The default quality of the WebP images of the browsers.</summary>
    private const double DefaultWebPQuality = 0.8;

    /// <summary>Encodes pixels (RGBA, not premultiplied, sRGB) as an image file.</summary>
    /// <param name="width">The width in pixels (at least 1).</param>
    /// <param name="height">The height in pixels (at least 1).</param>
    /// <param name="pixels">The pixels: 4 bytes per pixel, row by row.</param>
    /// <param name="mimeType">The MIME type of the image file (lowercase).</param>
    /// <param name="quality">The quality from 0 to 1 of a lossy format, or null (or a value out of the range) for the default quality.</param>
    /// <returns>
    /// The bytes of the file, or null when the type is not <c>image/jpeg</c> or <c>image/webp</c>, or when the format cannot
    /// store the image (it is larger than the largest image of the format): the image is then encoded as PNG.
    /// </returns>
    public static byte[]? Encode(int width, int height, ReadOnlySpan<byte> pixels, string mimeType, double? quality)
    {
        ImageEncoder encoder;
        switch (mimeType)
        {
            case "image/jpeg":
                // The JPEG images have no alpha channel: the pixels are composited over opaque black. The chroma is subsampled
                // (4:2:0), as by the browsers.
                encoder = new JpegEncoder
                {
                    // The lowest quality of the encoder is 1
                    Quality = Math.Max(1, ToQuality(quality, DefaultJpegQuality)),
                    ChromaSubsampling = JpegChromaSubsampling.Ratio420,
                    BackgroundColor = new Rgba64(0, 0, 0, ushort.MaxValue),
                };
                break;

            case "image/webp":
                // A WebP image of quality 1 is lossless, as in Chrome
                var webPQuality = ToQuality(quality, DefaultWebPQuality);
                encoder = webPQuality >= 100
                    ? new WebPEncoder { Compression = WebPCompression.Lossless }
                    : new WebPEncoder { Compression = WebPCompression.Lossy, Quality = webPQuality };
                break;

            default:
                return null;
        }

        try
        {
            using var image = Image.ImportPixelBytes<Rgba32>(pixels, width, height, width * 4, ImagingImageDecoder.Configuration);
            using var stream = new MemoryStream();
            image.Save(stream, encoder);
            return stream.ToArray();
        }
        catch (ImageException)
        {
            // The format cannot store an image of this size (65,535 pixels per side for JPEG, 16,383 for WebP)
            return null;
        }

        static int ToQuality(double? quality, double defaultQuality) => (int)Math.Round((quality is >= 0 and <= 1 ? quality.Value : defaultQuality) * 100, MidpointRounding.AwayFromZero);
    }
}
