namespace Meziantou.Framework.MediaTags.Internals;

/// <summary>Detects the MIME type of an image from its first bytes, for the formats that store a picture without one.</summary>
internal static class ImageMimeType
{
    /// <returns>The MIME type. JPEG is assumed for an image that is not one of the common formats, as the other formats do for a picture without a known type.</returns>
    public static string Detect(ReadOnlySpan<byte> data)
    {
        if (data.StartsWith((ReadOnlySpan<byte>)[0x89, (byte)'P', (byte)'N', (byte)'G']))
            return "image/png";

        if (data.StartsWith("GIF8"u8))
            return "image/gif";

        if (data.StartsWith("BM"u8))
            return "image/bmp";

        if (data.Length >= 12 && data.StartsWith("RIFF"u8) && data[8..12].SequenceEqual("WEBP"u8))
            return "image/webp";

        return "image/jpeg";
    }
}
