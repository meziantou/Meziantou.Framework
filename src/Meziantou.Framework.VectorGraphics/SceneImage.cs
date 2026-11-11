namespace Meziantou.Framework.VectorGraphics;

/// <summary>A raster image (PNG, JPEG, GIF or WebP), stretched to fill a rectangle.</summary>
/// <remarks>
/// The SVG embeds the bytes of the image as they are. The image and PDF renderers decode them: an image they cannot
/// decode is not drawn.
/// </remarks>
public sealed class SceneImage : SceneElement
{
    /// <summary>Initializes a new instance of the <see cref="SceneImage"/> class.</summary>
    /// <param name="x">The left edge.</param>
    /// <param name="y">The top edge.</param>
    /// <param name="width">The width.</param>
    /// <param name="height">The height.</param>
    /// <param name="data">The bytes of a PNG, JPEG, GIF or WebP file.</param>
    /// <exception cref="ArgumentException"><paramref name="data"/> does not start with the signature of a PNG, JPEG, GIF or WebP file.</exception>
    public SceneImage(double x, double y, double width, double height, ReadOnlyMemory<byte> data)
    {
        MediaType = GetMediaType(data.Span) ?? throw new ArgumentException("The data is not a PNG, JPEG, GIF or WebP file.", nameof(data));
        X = x;
        Y = y;
        Width = width;
        Height = height;
        Data = data;
    }

    /// <summary>Gets the left edge.</summary>
    public double X { get; }

    /// <summary>Gets the top edge.</summary>
    public double Y { get; }

    /// <summary>Gets the width.</summary>
    public double Width { get; }

    /// <summary>Gets the height.</summary>
    public double Height { get; }

    /// <summary>Gets the bytes of the image file.</summary>
    public ReadOnlyMemory<byte> Data { get; }

    /// <summary>Gets the media type of the image file: <c>image/png</c>, <c>image/jpeg</c>, <c>image/gif</c> or <c>image/webp</c>.</summary>
    public string MediaType { get; }

    /// <summary>Gets the media type of an image file from its signature, or <see langword="null"/> when it is not a PNG, JPEG, GIF or WebP file.</summary>
    /// <param name="data">The first bytes of the file, at least 12.</param>
    /// <returns>The media type, or <see langword="null"/>.</returns>
    public static string? GetMediaType(ReadOnlySpan<byte> data)
    {
        if (data.StartsWith((ReadOnlySpan<byte>)[0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A]))
            return "image/png";

        if (data.StartsWith((ReadOnlySpan<byte>)[0xFF, 0xD8, 0xFF]))
            return "image/jpeg";

        if (data.StartsWith("GIF87a"u8) || data.StartsWith("GIF89a"u8))
            return "image/gif";

        if (data.Length >= 12 && data.StartsWith("RIFF"u8) && data[8..12].SequenceEqual("WEBP"u8))
            return "image/webp";

        return null;
    }
}
