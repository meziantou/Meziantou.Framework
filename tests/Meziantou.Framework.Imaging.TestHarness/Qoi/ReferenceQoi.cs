using System.Buffers.Binary;

namespace Meziantou.Framework.Imaging.TestHarness.Qoi;

/// <summary>
/// An independent, deliberately simple reading of the QOI specification 1.0 (https://qoiformat.org/), used to verify the
/// library's encoder output without the library's decoder. It keeps the whole input in memory, reconstructs every pixel as a
/// tuple, counts the chunks of each kind, and is strict: invalid header fields, truncation, runs past the last pixel and a
/// missing or different end marker throw <see cref="InvalidDataException"/>. Bytes after the end marker are reported.
/// </summary>
public sealed class ReferenceQoi
{
    private ReferenceQoi(uint width, uint height, byte channels, byte colorSpace, byte[] rgba, IReadOnlyDictionary<string, int> chunkCounts, int trailingBytes, int longestRun)
    {
        Width = width;
        Height = height;
        Channels = channels;
        ColorSpace = colorSpace;
        Rgba = rgba;
        ChunkCounts = chunkCounts;
        TrailingBytes = trailingBytes;
        LongestRun = longestRun;
    }

    public uint Width { get; }

    public uint Height { get; }

    /// <summary>Gets the header channel count (3 or 4).</summary>
    public byte Channels { get; }

    /// <summary>Gets the header colorspace (0 sRGB with linear alpha, 1 linear).</summary>
    public byte ColorSpace { get; }

    /// <summary>Gets the decoded pixels as straight RGBA8, row-major; alpha is the decoder state's alpha even for 3-channel streams.</summary>
    public ReadOnlyMemory<byte> Rgba { get; }

    /// <summary>Gets the number of chunks of each kind: <c>rgb</c>, <c>rgba</c>, <c>index</c>, <c>diff</c>, <c>luma</c>, <c>run</c>.</summary>
    public IReadOnlyDictionary<string, int> ChunkCounts { get; }

    /// <summary>Gets the number of bytes after the end marker.</summary>
    public int TrailingBytes { get; }

    /// <summary>Gets the longest run of a <c>QOI_OP_RUN</c> chunk, or 0.</summary>
    public int LongestRun { get; }

    /// <summary>Gets the pixels as the image declares them: RGBA8 for 4 channels, RGB8 for 3 channels.</summary>
    public byte[] GetDeclaredPixels()
    {
        var rgba = Rgba.Span;
        if (Channels == 4)
            return rgba.ToArray();

        var rgb = new byte[rgba.Length / 4 * 3];
        for (int i = 0, j = 0; i < rgba.Length; i += 4, j += 3)
        {
            rgb[j] = rgba[i];
            rgb[j + 1] = rgba[i + 1];
            rgb[j + 2] = rgba[i + 2];
        }

        return rgb;
    }

    public static ReferenceQoi Parse(ReadOnlySpan<byte> data)
    {
        if (data.Length < 14 || data[0] != (byte)'q' || data[1] != (byte)'o' || data[2] != (byte)'i' || data[3] != (byte)'f')
            throw new InvalidDataException("Missing QOI header.");

        var width = BinaryPrimitives.ReadUInt32BigEndian(data[4..]);
        var height = BinaryPrimitives.ReadUInt32BigEndian(data[8..]);
        var channels = data[12];
        var colorSpace = data[13];
        if (width == 0 || height == 0 || channels is not (3 or 4) || colorSpace > 1)
            throw new InvalidDataException("Invalid QOI header field.");

        var total = checked((int)(width * height));
        var rgba = new byte[checked(total * 4)];
        var index = new (byte R, byte G, byte B, byte A)[64];
        (byte R, byte G, byte B, byte A) pixel = (0, 0, 0, 255);
        var counts = new Dictionary<string, int>(StringComparer.Ordinal) { ["rgb"] = 0, ["rgba"] = 0, ["index"] = 0, ["diff"] = 0, ["luma"] = 0, ["run"] = 0 };
        var longestRun = 0;
        var position = 14;
        var produced = 0;
        while (produced < total)
        {
            var tag = Read(data, position);
            var repeat = 1;
            if (tag == 0xFE)
            {
                pixel = (Read(data, position + 1), Read(data, position + 2), Read(data, position + 3), pixel.A);
                position += 4;
                counts["rgb"]++;
            }
            else if (tag == 0xFF)
            {
                pixel = (Read(data, position + 1), Read(data, position + 2), Read(data, position + 3), Read(data, position + 4));
                position += 5;
                counts["rgba"]++;
            }
            else
            {
                switch (tag >> 6)
                {
                    case 0:
                        pixel = index[tag];
                        position++;
                        counts["index"]++;
                        break;
                    case 1:
                        pixel = ((byte)(pixel.R + ((tag >> 4) & 3) - 2), (byte)(pixel.G + ((tag >> 2) & 3) - 2), (byte)(pixel.B + (tag & 3) - 2), pixel.A);
                        position++;
                        counts["diff"]++;
                        break;
                    case 2:
                    {
                        var second = Read(data, position + 1);
                        var greenDifference = (tag & 63) - 32;
                        pixel = ((byte)(pixel.R + greenDifference + (second >> 4) - 8), (byte)(pixel.G + greenDifference), (byte)(pixel.B + greenDifference + (second & 15) - 8), pixel.A);
                        position += 2;
                        counts["luma"]++;
                        break;
                    }

                    default:
                        repeat = (tag & 63) + 1;
                        if (repeat > total - produced)
                            throw new InvalidDataException("A QOI run extends past the last pixel.");

                        position++;
                        counts["run"]++;
                        longestRun = Math.Max(longestRun, repeat);
                        break;
                }
            }

            index[((pixel.R * 3) + (pixel.G * 5) + (pixel.B * 7) + (pixel.A * 11)) % 64] = pixel;
            for (var i = 0; i < repeat; i++)
            {
                var offset = (produced + i) * 4;
                rgba[offset] = pixel.R;
                rgba[offset + 1] = pixel.G;
                rgba[offset + 2] = pixel.B;
                rgba[offset + 3] = pixel.A;
            }

            produced += repeat;
        }

        ReadOnlySpan<byte> marker = [0, 0, 0, 0, 0, 0, 0, 1];
        if (data.Length - position < 8 || !data.Slice(position, 8).SequenceEqual(marker))
            throw new InvalidDataException("Missing QOI end marker.");

        return new ReferenceQoi(width, height, channels, colorSpace, rgba, counts, data.Length - position - 8, longestRun);
    }

    private static byte Read(ReadOnlySpan<byte> data, int position)
        => position < data.Length ? data[position] : throw new InvalidDataException("Truncated QOI chunk stream.");
}
