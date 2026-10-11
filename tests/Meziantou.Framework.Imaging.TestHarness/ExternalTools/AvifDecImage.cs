using System.Buffers.Binary;
using System.IO.Compression;
using Meziantou.Framework.Imaging.TestHarness.Pixels;

namespace Meziantou.Framework.Imaging.TestHarness.ExternalTools;

/// <summary>
/// An image decoded by <c>avifdec</c> to a 16-bit PNG file (<c>--depth 16</c>), read back without the library under test.
/// </summary>
/// <remarks>
/// avifdec writes the layout that holds the decoded image: gray for a monochrome image, RGB for a color image, and an alpha
/// channel only when the image has an alpha plane that is not fully opaque. <see cref="Pixels"/> is normalized to straight
/// RGBA, 16 bits per sample: a gray sample is replicated and a missing alpha channel is opaque (65535). The reader only
/// accepts what avifdec writes: non-interlaced, 16 bits per sample, no palette.
/// </remarks>
public sealed class AvifDecImage
{
    private static readonly byte[] Signature = [0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A];

    private AvifDecImage(RawPixelBuffer pixels, bool isGray, bool hasAlpha)
    {
        Pixels = pixels;
        IsGray = isGray;
        HasAlpha = hasAlpha;
    }

    /// <summary>Gets the width avifdec wrote, after it applied the transformations of the file (<c>clap</c>, <c>irot</c>, <c>imir</c>).</summary>
    public int Width => Pixels.Width;

    /// <summary>Gets the height avifdec wrote, after it applied the transformations of the file (<c>clap</c>, <c>irot</c>, <c>imir</c>).</summary>
    public int Height => Pixels.Height;

    /// <summary>Gets the pixels as straight RGBA, 16-bit little-endian samples (<see cref="RawPixelLayout.Rgba16Le"/>).</summary>
    public RawPixelBuffer Pixels { get; }

    /// <summary>Gets a value indicating whether avifdec wrote a gray PNG (a monochrome image).</summary>
    public bool IsGray { get; }

    /// <summary>Gets a value indicating whether avifdec wrote an alpha channel (an alpha plane that is not fully opaque).</summary>
    public bool HasAlpha { get; }

    /// <summary>Reads a 16-bit PNG file written by avifdec.</summary>
    /// <param name="data">The file.</param>
    /// <returns>The image.</returns>
    /// <exception cref="InvalidDataException">The file is not a non-interlaced 16-bit gray, gray+alpha, RGB or RGBA PNG, or its data is invalid.</exception>
    public static AvifDecImage ReadPng(ReadOnlySpan<byte> data)
    {
        if (data.Length < 8 || !data[..8].SequenceEqual(Signature))
            throw new InvalidDataException("avifdec did not write a PNG file.");

        var header = default(ReadOnlySpan<byte>);
        using var compressed = new MemoryStream();
        var offset = 8;
        var ended = false;
        while (!ended)
        {
            if (data.Length - offset < 12)
                throw new InvalidDataException("The PNG file written by avifdec is truncated.");

            var length = BinaryPrimitives.ReadUInt32BigEndian(data[offset..]);
            if (length > data.Length - offset - 12)
                throw new InvalidDataException("The PNG file written by avifdec has an invalid chunk length.");

            var type = data.Slice(offset + 4, 4);
            var body = data.Slice(offset + 8, (int)length);
            if (type.SequenceEqual("IHDR"u8))
            {
                header = body;
            }
            else if (type.SequenceEqual("IDAT"u8))
            {
                compressed.Write(body);
            }
            else if (type.SequenceEqual("IEND"u8))
            {
                ended = true;
            }

            offset += 12 + (int)length;
        }

        if (header.Length != 13)
            throw new InvalidDataException("The PNG file written by avifdec has no valid IHDR chunk.");

        var width = checked((int)BinaryPrimitives.ReadUInt32BigEndian(header));
        var height = checked((int)BinaryPrimitives.ReadUInt32BigEndian(header[4..]));
        var (bitDepth, colorType, interlace) = (header[8], header[9], header[12]);
        var channels = colorType switch
        {
            0 => 1,
            2 => 3,
            4 => 2,
            6 => 4,
            _ => throw new InvalidDataException($"avifdec wrote a PNG file with the unexpected color type {colorType}."),
        };

        if (bitDepth != 16 || interlace != 0 || width <= 0 || height <= 0)
            throw new InvalidDataException($"avifdec wrote a PNG file that is not a non-interlaced 16-bit image (bit depth {bitDepth}, interlace method {interlace}).");

        var bytesPerPixel = channels * 2;
        var rowLength = width * bytesPerPixel;
        var inflated = new byte[checked(height * (rowLength + 1))];
        compressed.Position = 0;
        using (var zlib = new ZLibStream(compressed, CompressionMode.Decompress))
        {
            // ZLibStream reports a truncated stream as a clean end: the expected number of bytes must be produced, and no more
            if (zlib.ReadAtLeast(inflated, inflated.Length, throwOnEndOfStream: false) != inflated.Length || zlib.ReadByte() >= 0)
                throw new InvalidDataException("The image data of the PNG file written by avifdec does not have the expected length.");
        }

        var isGray = channels <= 2;
        var hasAlpha = channels is 2 or 4;
        var rgba = new byte[checked(width * height * 8)];
        var previous = new byte[rowLength];
        var current = new byte[rowLength];
        for (var y = 0; y < height; y++)
        {
            var filter = inflated[y * (rowLength + 1)];
            inflated.AsSpan((y * (rowLength + 1)) + 1, rowLength).CopyTo(current);
            Unfilter(filter, current, previous, bytesPerPixel);
            for (var x = 0; x < width; x++)
            {
                var source = current.AsSpan(x * bytesPerPixel, bytesPerPixel);
                var target = rgba.AsSpan(((y * width) + x) * 8, 8);
                for (var channel = 0; channel < 3; channel++)
                {
                    // Big-endian PNG samples to little-endian samples
                    var sample = source.Slice(isGray ? 0 : channel * 2, 2);
                    target[channel * 2] = sample[1];
                    target[(channel * 2) + 1] = sample[0];
                }

                target[6] = hasAlpha ? source[bytesPerPixel - 1] : (byte)0xFF;
                target[7] = hasAlpha ? source[bytesPerPixel - 2] : (byte)0xFF;
            }

            (previous, current) = (current, previous);
        }

        return new AvifDecImage(RawPixelBuffer.Create(width, height, RawPixelLayout.Rgba16Le, rgba), isGray, hasAlpha);
    }

    /// <summary>Reverses a PNG scanline filter in place (PNG specification, section 9.2).</summary>
    private static void Unfilter(byte filter, Span<byte> row, ReadOnlySpan<byte> previous, int bytesPerPixel)
    {
        for (var i = 0; i < row.Length; i++)
        {
            int left = i >= bytesPerPixel ? row[i - bytesPerPixel] : 0;
            int up = previous[i];
            int upLeft = i >= bytesPerPixel ? previous[i - bytesPerPixel] : 0;
            var predictor = filter switch
            {
                0 => 0,
                1 => left,
                2 => up,
                3 => (left + up) / 2,
                4 => Paeth(left, up, upLeft),
                _ => throw new InvalidDataException($"The PNG file written by avifdec has the invalid filter type {filter}."),
            };

            row[i] = (byte)(row[i] + predictor);
        }
    }

    private static int Paeth(int left, int up, int upLeft)
    {
        var estimate = left + up - upLeft;
        var distanceLeft = Math.Abs(estimate - left);
        var distanceUp = Math.Abs(estimate - up);
        var distanceUpLeft = Math.Abs(estimate - upLeft);
        if (distanceLeft <= distanceUp && distanceLeft <= distanceUpLeft)
            return left;

        return distanceUp <= distanceUpLeft ? up : upLeft;
    }
}
