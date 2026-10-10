using System.Buffers.Binary;
using System.Globalization;
using System.Text;

namespace Meziantou.Framework.Imaging.TestHarness.Pnm;

/// <summary>
/// An independent, deliberately simple reading of the Netpbm formats (PBM, PGM, PPM and PAM) the library supports, used to
/// verify the library's encoder output without the library's decoder. It keeps the whole input in memory, normalizes every
/// sample to the full 8- or 16-bit range and is strict: an illegal magic number, dimensions or <c>MAXVAL</c>, a header
/// token that is not followed by white space, an undefined or duplicated PAM keyword, a tuple type it does not support, a
/// sample greater than <c>MAXVAL</c> and truncation throw <see cref="InvalidDataException"/>. Only the first image of a
/// concatenation is read; the remaining bytes are reported.
/// </summary>
public sealed class ReferencePnm
{
    private ReferencePnm(int magic, int width, int height, int maxValue, string tupleType, int trailingBytes, ushort[] rgba)
    {
        Magic = magic;
        Width = width;
        Height = height;
        MaxValue = maxValue;
        TupleType = tupleType;
        TrailingBytes = trailingBytes;
        Rgba = rgba;
    }

    /// <summary>Gets the magic number digit (1 to 7).</summary>
    public int Magic { get; }

    /// <summary>Gets the width, in pixels.</summary>
    public int Width { get; }

    /// <summary>Gets the height, in pixels.</summary>
    public int Height { get; }

    /// <summary>Gets <c>MAXVAL</c>.</summary>
    public int MaxValue { get; }

    /// <summary>Gets the tuple type (<c>BLACKANDWHITE</c>, <c>GRAYSCALE</c>, <c>RGB</c> and their alpha forms).</summary>
    public string TupleType { get; }

    /// <summary>Gets the number of bytes after the first image.</summary>
    public int TrailingBytes { get; }

    /// <summary>Gets a value indicating whether the decoded samples are 16-bit.</summary>
    public bool Is16Bit => MaxValue > 255;

    /// <summary>Gets the decoded pixels as straight RGBA, row-major, top-down, at <see cref="Is16Bit"/> precision.</summary>
    public ReadOnlyMemory<ushort> Rgba { get; }

    /// <summary>Gets the decoded pixels as straight RGBA8 (16-bit samples are reduced with nearest rounding).</summary>
    public byte[] GetRgba8()
    {
        var samples = Rgba.Span;
        var result = new byte[samples.Length];
        for (var i = 0; i < samples.Length; i++)
        {
            result[i] = Is16Bit ? (byte)(((samples[i] * 255) + 32767) / 65535) : (byte)samples[i];
        }

        return result;
    }

    /// <summary>Gets the decoded pixels as little-endian RGBA16.</summary>
    public byte[] GetRgba16Le()
    {
        var samples = Rgba.Span;
        var result = new byte[samples.Length * 2];
        for (var i = 0; i < samples.Length; i++)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(i * 2), Is16Bit ? samples[i] : (ushort)(samples[i] * 257));
        }

        return result;
    }

    /// <summary>Reads the first image of a Netpbm file.</summary>
    /// <param name="data">The whole file.</param>
    /// <returns>The decoded image.</returns>
    /// <exception cref="InvalidDataException">The input is malformed or uses an unsupported variant.</exception>
    public static ReferencePnm Parse(ReadOnlySpan<byte> data)
    {
        if (data.Length < 3 || data[0] != (byte)'P' || data[1] is < (byte)'1' or > (byte)'7' || !IsWhiteSpace(data[2]))
            throw new InvalidDataException("Missing Netpbm magic number.");

        var magic = data[1] - '0';
        var offset = 2;
        int width, height, maxValue, depth;
        string tupleType;
        if (magic == 7)
        {
            (width, height, depth, maxValue, tupleType) = ReadPamHeader(data, ref offset);
        }
        else
        {
            width = ReadToken(data, ref offset);
            height = ReadToken(data, ref offset);
            maxValue = magic is 1 or 4 ? 1 : ReadToken(data, ref offset);
            if (magic is 4 or 5 or 6)
            {
                if (offset >= data.Length || !IsWhiteSpace(data[offset]))
                    throw new InvalidDataException("The last header token is not followed by white space.");

                offset++;
            }

            tupleType = magic switch { 1 or 4 => "BLACKANDWHITE", 2 or 5 => "GRAYSCALE", _ => "RGB" };
            depth = Channels(tupleType);
        }

        if (width <= 0 || height <= 0)
            throw new InvalidDataException($"Illegal Netpbm dimensions {width}x{height}.");

        if (maxValue is < 1 or > 65535)
            throw new InvalidDataException($"Illegal Netpbm MAXVAL {maxValue}.");

        var bitmap = magic is 1 or 4;
        var plain = magic is 1 or 2 or 3;
        var target = maxValue > 255 ? 65535 : 255;
        var rgba = new ushort[width * height * 4];
        Span<ushort> tuple = stackalloc ushort[4];
        for (var y = 0; y < height; y++)
        {
            if (bitmap && !plain)
            {
                var rowBytes = (width + 7) / 8;
                Require(data, offset + rowBytes);
                for (var x = 0; x < width; x++)
                {
                    var bit = (data[offset + (x >> 3)] >> (7 - (x & 7))) & 1;
                    WriteGray(rgba, ((y * width) + x) * 4, bit != 0 ? (ushort)0 : (ushort)255, 255);
                }

                offset += rowBytes;
                continue;
            }

            for (var x = 0; x < width; x++)
            {
                for (var channel = 0; channel < depth; channel++)
                {
                    int value;
                    if (plain && bitmap)
                    {
                        value = ReadPlainBit(data, ref offset);
                    }
                    else if (plain)
                    {
                        value = ReadToken(data, ref offset, requireTerminator: false);
                    }
                    else if (maxValue <= 255)
                    {
                        Require(data, offset + 1);
                        value = data[offset++];
                    }
                    else
                    {
                        Require(data, offset + 2);
                        value = BinaryPrimitives.ReadUInt16BigEndian(data[offset..]);
                        offset += 2;
                    }

                    tuple[channel] = Normalize(value, maxValue, target, bitmap && plain);
                }

                var index = ((y * width) + x) * 4;
                switch (depth)
                {
                    case 1:
                        WriteGray(rgba, index, tuple[0], (ushort)target);
                        break;

                    case 2:
                        WriteGray(rgba, index, tuple[0], tuple[1]);
                        break;

                    case 3:
                        rgba[index] = tuple[0];
                        rgba[index + 1] = tuple[1];
                        rgba[index + 2] = tuple[2];
                        rgba[index + 3] = (ushort)target;
                        break;

                    default:
                        tuple.CopyTo(rgba.AsSpan(index, 4));
                        break;
                }
            }
        }

        return new ReferencePnm(magic, width, height, maxValue, tupleType, data.Length - offset, rgba);
    }

    private static void WriteGray(ushort[] rgba, int index, ushort gray, ushort alpha)
    {
        rgba[index] = gray;
        rgba[index + 1] = gray;
        rgba[index + 2] = gray;
        rgba[index + 3] = alpha;
    }

    private static ushort Normalize(int value, int maxValue, int target, bool invertedBit)
    {
        if (invertedBit)
            return value != 0 ? (ushort)0 : (ushort)255;

        if (value > maxValue)
            throw new InvalidDataException($"The Netpbm sample {value} is greater than MAXVAL ({maxValue}).");

        return maxValue == target ? (ushort)value : (ushort)((((long)value * target) + (maxValue / 2)) / maxValue);
    }

    private static int Channels(string tupleType) => tupleType switch
    {
        "BLACKANDWHITE" or "GRAYSCALE" => 1,
        "BLACKANDWHITE_ALPHA" or "GRAYSCALE_ALPHA" => 2,
        "RGB" => 3,
        "RGB_ALPHA" => 4,
        _ => throw new InvalidDataException($"Unsupported PAM tuple type '{tupleType}'."),
    };

    private static (int Width, int Height, int Depth, int MaxValue, string TupleType) ReadPamHeader(ReadOnlySpan<byte> data, ref int offset)
    {
        var fields = new Dictionary<string, int>(StringComparer.Ordinal);
        string? tupleType = null;
        while (true)
        {
            var end = data[offset..].IndexOf((byte)'\n');
            if (end < 0)
                throw new InvalidDataException("The PAM header has no ENDHDR line.");

            var line = data.Slice(offset, end).TrimEnd((byte)'\r').Trim((byte)' ');
            offset += end + 1;
            if (line.IsEmpty || line[0] == (byte)'#')
                continue;

            var text = Encoding.ASCII.GetString(line);
            if (string.Equals(text, "ENDHDR", StringComparison.Ordinal))
                break;

            var parts = text.Split(' ', 2, StringSplitOptions.TrimEntries);
            if (string.Equals(parts[0], "TUPLTYPE", StringComparison.Ordinal))
            {
                if (tupleType is not null || parts.Length < 2)
                    throw new InvalidDataException("TUPLTYPE is missing or declared twice.");

                tupleType = parts[1];
                _ = Channels(tupleType);
                continue;
            }

            if (parts[0] is not ("WIDTH" or "HEIGHT" or "DEPTH" or "MAXVAL"))
                throw new InvalidDataException($"Undefined PAM keyword '{parts[0]}'.");

            if (parts.Length < 2 || !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var value))
                throw new InvalidDataException($"The PAM keyword '{parts[0]}' has no decimal value.");

            if (!fields.TryAdd(parts[0], value))
                throw new InvalidDataException($"The PAM keyword '{parts[0]}' is declared twice.");
        }

        if (fields.Count != 4)
            throw new InvalidDataException("The PAM header must declare WIDTH, HEIGHT, DEPTH and MAXVAL.");

        tupleType ??= fields["DEPTH"] switch
        {
            1 => "GRAYSCALE",
            2 => "GRAYSCALE_ALPHA",
            3 => "RGB",
            4 => "RGB_ALPHA",
            _ => throw new InvalidDataException($"DEPTH {fields["DEPTH"]} without a TUPLTYPE."),
        };

        if (Channels(tupleType) != fields["DEPTH"])
            throw new InvalidDataException($"DEPTH {fields["DEPTH"]} does not match the tuple type {tupleType}.");

        if (tupleType.StartsWith("BLACKANDWHITE", StringComparison.Ordinal) && fields["MAXVAL"] != 1)
            throw new InvalidDataException($"{tupleType} requires MAXVAL 1.");

        return (fields["WIDTH"], fields["HEIGHT"], fields["DEPTH"], fields["MAXVAL"], tupleType);
    }

    private static int ReadToken(ReadOnlySpan<byte> data, ref int offset, bool requireTerminator = true)
    {
        SkipWhiteSpaceAndComments(data, ref offset);
        var start = offset;
        while (offset < data.Length && data[offset] is >= (byte)'0' and <= (byte)'9')
        {
            offset++;
        }

        if (offset == start)
            throw new InvalidDataException("A Netpbm token is not a decimal number.");

        if (requireTerminator && offset < data.Length && !IsWhiteSpace(data[offset]) && data[offset] != (byte)'#')
            throw new InvalidDataException("A Netpbm token is not followed by white space.");

        return int.Parse(Encoding.ASCII.GetString(data[start..offset]), NumberStyles.None, CultureInfo.InvariantCulture);
    }

    private static int ReadPlainBit(ReadOnlySpan<byte> data, ref int offset)
    {
        SkipWhiteSpaceAndComments(data, ref offset);
        if (offset >= data.Length)
            throw new InvalidDataException("The plain PBM raster is truncated.");

        var value = data[offset++];
        if (value is not ((byte)'0' or (byte)'1'))
            throw new InvalidDataException("A plain PBM sample is neither 0 nor 1.");

        return value - '0';
    }

    private static void SkipWhiteSpaceAndComments(ReadOnlySpan<byte> data, ref int offset)
    {
        while (offset < data.Length)
        {
            if (IsWhiteSpace(data[offset]))
            {
                offset++;
            }
            else if (data[offset] == (byte)'#')
            {
                while (offset < data.Length && data[offset] is not ((byte)'\n' or (byte)'\r'))
                {
                    offset++;
                }
            }
            else
            {
                return;
            }
        }
    }

    private static bool IsWhiteSpace(byte value) => value is (byte)' ' or (byte)'\t' or (byte)'\n' or (byte)'\r' or 0x0B or 0x0C;

    private static void Require(ReadOnlySpan<byte> data, int length)
    {
        if (data.Length < length)
            throw new InvalidDataException($"The Netpbm raster is truncated ({data.Length} bytes, {length} needed).");
    }
}
