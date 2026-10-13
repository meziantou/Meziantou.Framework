using System.Globalization;

namespace Meziantou.Framework.Imaging.TestHarness.ExternalTools;

/// <summary>
/// The planes <c>heif-dec</c> writes to a YUV4MPEG2 file (<c>.y4m</c>), read back without the library under test: one
/// frame of 8-bit 4:2:0 samples, which is the only layout heif-dec writes.
/// </summary>
/// <remarks>
/// For an 8-bit 4:2:0 image, these are the planes libde265 decoded, after libheif assembled the tiles of a grid and applied
/// the transformations of the file (<c>clap</c>, <c>irot</c>, <c>imir</c>): no conversion to RGB is involved. For any other
/// image, libheif converts the image to 8-bit 4:2:0 first, and the planes are not the decoded ones.
/// </remarks>
public sealed class HeifDecPlanes
{
    private HeifDecPlanes(int width, int height, byte[] y, byte[] u, byte[] v)
    {
        Width = width;
        Height = height;
        Y = y;
        U = u;
        V = v;
    }

    /// <summary>Gets the width of the luma plane.</summary>
    public int Width { get; }

    /// <summary>Gets the height of the luma plane.</summary>
    public int Height { get; }

    /// <summary>Gets the width of the chroma planes: half the width of the luma plane, rounded up.</summary>
    public int ChromaWidth => (Width + 1) / 2;

    /// <summary>Gets the height of the chroma planes: half the height of the luma plane, rounded up.</summary>
    public int ChromaHeight => (Height + 1) / 2;

    /// <summary>Gets the luma plane, rows in order.</summary>
    public ReadOnlyMemory<byte> Y { get; }

    /// <summary>Gets the first chroma plane (Cb), rows in order.</summary>
    public ReadOnlyMemory<byte> U { get; }

    /// <summary>Gets the second chroma plane (Cr), rows in order.</summary>
    public ReadOnlyMemory<byte> V { get; }

    /// <summary>Reads a YUV4MPEG2 file of one frame written by heif-dec.</summary>
    /// <param name="data">The file.</param>
    /// <returns>The planes.</returns>
    /// <exception cref="InvalidDataException">The file is not a YUV4MPEG2 file of one 8-bit 4:2:0 frame.</exception>
    public static HeifDecPlanes ReadY4m(ReadOnlySpan<byte> data)
    {
        // "YUV4MPEG2 W80 H64 F30:1", a line feed, "FRAME", a line feed, then the planes
        var headerEnd = data.IndexOf((byte)'\n');
        if (headerEnd < 0 || !data.StartsWith("YUV4MPEG2 "u8))
            throw new InvalidDataException("heif-dec did not write a YUV4MPEG2 file.");

        var (width, height) = (0, 0);
        foreach (var parameter in Encoding.ASCII.GetString(data[..headerEnd]).Split(' ', StringSplitOptions.RemoveEmptyEntries).Skip(1))
        {
            switch (parameter[0])
            {
                case 'W':
                    width = int.Parse(parameter.AsSpan(1), NumberStyles.None, CultureInfo.InvariantCulture);
                    break;

                case 'H':
                    height = int.Parse(parameter.AsSpan(1), NumberStyles.None, CultureInfo.InvariantCulture);
                    break;

                // The default color space of the format is 4:2:0 with 8-bit samples
                case 'C' when !parameter.StartsWith("C420", StringComparison.Ordinal) || parameter.Contains('p', StringComparison.Ordinal):
                    throw new InvalidDataException($"heif-dec wrote a YUV4MPEG2 file with the unexpected color space '{parameter}'.");
            }
        }

        if (width <= 0 || height <= 0)
            throw new InvalidDataException("The YUV4MPEG2 file written by heif-dec has no size.");

        var frame = data[(headerEnd + 1)..];
        if (!frame.StartsWith("FRAME"u8))
            throw new InvalidDataException("The YUV4MPEG2 file written by heif-dec has no frame.");

        var frameHeaderEnd = frame.IndexOf((byte)'\n');
        var planes = frame[(frameHeaderEnd + 1)..];
        var (chromaWidth, chromaHeight) = ((width + 1) / 2, (height + 1) / 2);
        var lumaLength = checked(width * height);
        var chromaLength = chromaWidth * chromaHeight;
        if (frameHeaderEnd < 0 || planes.Length != lumaLength + (2 * chromaLength))
            throw new InvalidDataException(string.Create(CultureInfo.InvariantCulture, $"The YUV4MPEG2 file written by heif-dec holds {planes.Length} bytes of planes; {lumaLength + (2 * chromaLength)} are expected for one 8-bit 4:2:0 frame of {width}x{height}."));

        return new HeifDecPlanes(width, height, planes[..lumaLength].ToArray(), planes.Slice(lumaLength, chromaLength).ToArray(), planes[(lumaLength + chromaLength)..].ToArray());
    }
}
