using System.Buffers.Binary;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// Reads the ICC profile that follows the image header (ISO/IEC 18181-1, annex E): an entropy-coded stream of bytes
/// (section E.4), which is a list of commands and their data that rebuild the profile from predictions (section E.5).
/// </summary>
internal static class JpegXlIccDecoder
{
    /// <summary>The number of contexts of the entropy-coded stream.</summary>
    public const int ContextCount = 41;

    /// <summary>The largest encoded size the reference decoder accepts.</summary>
    public const ulong MaxEncodedSize = 1 << 28;

    /// <summary>The length of the header of a profile, whose bytes are predicted one by one.</summary>
    public const int HeaderSize = 128;

    // Commands of the tag list: the low 6 bits select the tag, the high bits tell that an offset or a size follows
    private const int TagUnknown = 1;
    private const int TagTrc = 2;
    private const int TagXyz = 3;
    private const int TagStringFirst = 4;
    private const int FlagOffset = 64;
    private const int FlagSize = 128;

    // Commands of the main content
    private const int CommandInsert = 1;
    private const int CommandShuffle2 = 2;
    private const int CommandShuffle4 = 3;
    private const int CommandPredict = 4;
    private const int CommandXyz = 10;
    private const int CommandTypeStartFirst = 16;

    private static readonly string[] TagStrings =
    [
        "cprt", "wtpt", "bkpt", "rXYZ", "gXYZ", "bXYZ", "kXYZ", "rTRC", "gTRC", "bTRC", "kTRC", "chad", "desc", "chrm", "dmnd", "dmdd", "lumi",
    ];

    private static readonly string[] TypeStrings = ["XYZ ", "desc", "text", "mluc", "para", "curv", "sf32", "gbd "];

    // The prediction of the 128 bytes of the header; the first four bytes are the size of the profile
    public static ReadOnlySpan<byte> InitialHeaderPrediction =>
    [
        0, 0, 0, 0, 0, 0, 0, 0, 4, 0, 0, 0, (byte)'m', (byte)'n', (byte)'t', (byte)'r',
        (byte)'R', (byte)'G', (byte)'B', (byte)' ', (byte)'X', (byte)'Y', (byte)'Z', (byte)' ', 0, 0, 0, 0, 0, 0, 0, 0,
        0, 0, 0, 0, (byte)'a', (byte)'c', (byte)'s', (byte)'p', 0, 0, 0, 0, 0, 0, 0, 0,
        0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
        0, 0, 0, 0, 0, 0, 246, 214, 0, 1, 0, 0, 0, 0, 211, 45,
        0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
        0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
        0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
    ];

    /// <summary>
    /// Reads the profile. The reader may overrun (<see cref="JpegXlBitReader.IsOverrun"/>): the result is then meaningless,
    /// and an exception that this method throws may be a consequence of the missing bits.
    /// </summary>
    /// <param name="reader">The reader, after the image header.</param>
    /// <param name="scope">The scope of the working memory.</param>
    /// <param name="maxLength">The largest length of the profile: what remains of the metadata limit.</param>
    /// <param name="limits">The limits, for the exception thrown when the profile is longer than <paramref name="maxLength"/>.</param>
    /// <returns>The bytes of the profile.</returns>
    public static byte[] Read(JpegXlBitReader reader, AllocationScope scope, long maxLength, ImageResourceLimits limits)
    {
        var encodedSize = reader.ReadU64();
        if (encodedSize > MaxEncodedSize)
            throw JpegXlFormat.Invalid("the encoded ICC profile is larger than 256 MiB.");

        using var code = JpegXlEntropyCode.Read(reader, ContextCount, scope);
        using var decoder = new JpegXlEntropyDecoder(code, scope);
        decoder.Begin(reader);

        var length = (int)encodedSize;
        var capacity = Math.Min(length, 1024);
        var encoded = scope.Rent(capacity, AllocationKind.Temporary, clear: false);
        try
        {
            var startBits = reader.BitsConsumed;
            byte previous = 0;
            byte beforePrevious = 0;
            for (var i = 0; i < length; i++)
            {
                if (i == capacity)
                {
                    // The stream is checked before the buffer grows: a hostile size does not allocate more than what was decoded
                    if (reader.IsOverrun)
                        throw JpegXlFormat.Invalid("the encoded ICC profile is truncated.");

                    if ((long)i * 8 > (reader.BitsConsumed - startBits + 64) * 256)
                        throw JpegXlFormat.Invalid("the encoded ICC profile expands more than an entropy-coded stream can.");

                    capacity = (int)Math.Min(length, (long)capacity * 2);
                    var larger = scope.Rent(capacity, AllocationKind.Temporary, clear: false);
                    encoded.RawBuffer.AsSpan(0, i).CopyTo(larger.RawBuffer);
                    encoded.Dispose();
                    encoded = larger;
                }

                var value = (byte)decoder.Read(reader, GetContext(i, previous, beforePrevious));
                encoded.RawBuffer[i] = value;
                beforePrevious = previous;
                previous = value;
            }

            decoder.EnsureFinalState();
            return Unpredict(encoded.RawBuffer.AsSpan(0, length), reader, maxLength, limits);
        }
        finally
        {
            encoded.Dispose();
        }
    }

    /// <summary>Gets the context of a byte of the encoded profile from its position and the two bytes before it.</summary>
    public static int GetContext(int index, byte previous, byte beforePrevious)
    {
        if (index <= HeaderSize)
            return 0;

        return 1 + GetKind(previous) + (GetCoarseKind(beforePrevious) * 8);

        static int GetKind(byte value) => value switch
        {
            (>= (byte)'a' and <= (byte)'z') or (>= (byte)'A' and <= (byte)'Z') => 0,
            (>= (byte)'0' and <= (byte)'9') or (byte)'.' or (byte)',' => 1,
            0 => 2,
            1 => 3,
            < 16 => 4,
            255 => 6,
            > 240 => 5,
            _ => 7,
        };

        static int GetCoarseKind(byte value) => value switch
        {
            (>= (byte)'a' and <= (byte)'z') or (>= (byte)'A' and <= (byte)'Z') => 0,
            (>= (byte)'0' and <= (byte)'9') or (byte)'.' or (byte)',' => 1,
            < 16 => 2,
            > 240 => 3,
            _ => 4,
        };
    }

    /// <summary>Rebuilds the profile from the decoded bytes: the size of the profile, the size of the commands, the commands, then the data.</summary>
    internal static byte[] Unpredict(ReadOnlySpan<byte> encoded, JpegXlBitReader reader, long maxLength, ImageResourceLimits limits)
    {
        var position = 0;
        var outputSize = ReadVarInt(encoded, encoded.Length, ref position);
        var commandsSize = ReadVarInt(encoded, encoded.Length, ref position);
        if (outputSize > uint.MaxValue || commandsSize > (ulong)(encoded.Length - position))
            throw Invalid();

        // A profile is at most 65,536 bytes shorter than its encoding
        if (outputSize + 65536 < (ulong)encoded.Length)
            throw Invalid();

        if (outputSize > (ulong)maxLength)
        {
            // A size read from bits that do not exist is not a size
            if (reader.IsOverrun)
                throw Invalid();

            throw new ImageResourceLimitException(ImageResourceLimitKind.MetadataBytes, limits.MaxMetadataBytes, limits.MaxMetadataBytes - maxLength + (long)outputSize);
        }

        var commandPosition = position;
        var commandsEnd = position + (int)commandsSize;
        position = commandsEnd;
        var result = new byte[(int)outputSize];
        var length = 0;

        // The header: each byte is the sum of a prediction and of a byte of the data
        Span<byte> header = stackalloc byte[HeaderSize];
        InitialHeaderPrediction.CopyTo(header);
        BinaryPrimitives.WriteUInt32BigEndian(header, (uint)outputSize);
        for (var i = 0; i <= HeaderSize; i++)
        {
            if (length == result.Length)
            {
                if (commandPosition != commandsEnd || position != encoded.Length)
                    throw Invalid();

                return result;
            }

            if (i == HeaderSize)
                break;

            PredictHeader(result.AsSpan(0, length), header, i);
            if (position >= encoded.Length)
                throw Invalid();

            result[length++] = (byte)(encoded[position++] + header[i]);
        }

        // The tag list
        if (commandPosition >= commandsEnd)
            throw Invalid();

        var tagCount = ReadVarInt(encoded, commandsEnd, ref commandPosition);
        if (tagCount != 0)
        {
            tagCount--;
            if (tagCount > uint.MaxValue)
                throw Invalid();

            AppendUInt32(result, ref length, (uint)tagCount);
            var previousTagStart = HeaderSize + (tagCount * 12);
            ulong previousTagSize = 0;
            Span<byte> tag = stackalloc byte[4];
            while (commandPosition < commandsEnd)
            {
                var command = encoded[commandPosition++];
                var tagCode = command & 63;
                if (tagCode == 0)
                    break;

                switch (tagCode)
                {
                    case TagUnknown:
                        if (encoded.Length - position < 4)
                            throw Invalid();

                        encoded.Slice(position, 4).CopyTo(tag);
                        position += 4;
                        break;
                    case TagTrc:
                        "rTRC"u8.CopyTo(tag);
                        break;
                    case TagXyz:
                        "rXYZ"u8.CopyTo(tag);
                        break;
                    default:
                        if (tagCode - TagStringFirst >= TagStrings.Length)
                            throw Invalid();

                        Encoding.ASCII.GetBytes(TagStrings[tagCode - TagStringFirst], tag);
                        break;
                }

                Append(result, ref length, tag);
                var tagSize = previousTagSize;
                if (tag.SequenceEqual("rXYZ"u8) || tag.SequenceEqual("gXYZ"u8) || tag.SequenceEqual("bXYZ"u8) || tag.SequenceEqual("kXYZ"u8) || tag.SequenceEqual("wtpt"u8) || tag.SequenceEqual("bkpt"u8) || tag.SequenceEqual("lumi"u8))
                {
                    tagSize = 20;
                }

                var tagStart = (command & FlagOffset) != 0 ? ReadVarInt(encoded, commandsEnd, ref commandPosition) : previousTagStart + previousTagSize;
                if (tagStart > uint.MaxValue)
                    throw Invalid();

                AppendUInt32(result, ref length, (uint)tagStart);
                if ((command & FlagSize) != 0)
                {
                    tagSize = ReadVarInt(encoded, commandsEnd, ref commandPosition);
                }

                if (tagSize > uint.MaxValue)
                    throw Invalid();

                AppendUInt32(result, ref length, (uint)tagSize);
                previousTagStart = tagStart;
                previousTagSize = tagSize;
                if (tagCode == TagTrc)
                {
                    // The green and blue curves share the data of the red one
                    Append(result, ref length, "gTRC"u8);
                    AppendUInt32(result, ref length, (uint)tagStart);
                    AppendUInt32(result, ref length, (uint)tagSize);
                    Append(result, ref length, "bTRC"u8);
                    AppendUInt32(result, ref length, (uint)tagStart);
                    AppendUInt32(result, ref length, (uint)tagSize);
                }
                else if (tagCode == TagXyz)
                {
                    // The green and blue colorants follow the red one
                    if (tagStart + (tagSize * 2) > uint.MaxValue)
                        throw Invalid();

                    Append(result, ref length, "gXYZ"u8);
                    AppendUInt32(result, ref length, (uint)(tagStart + tagSize));
                    AppendUInt32(result, ref length, (uint)tagSize);
                    Append(result, ref length, "bXYZ"u8);
                    AppendUInt32(result, ref length, (uint)(tagStart + (tagSize * 2)));
                    AppendUInt32(result, ref length, (uint)tagSize);
                }
            }
        }

        // The main content. The buffer is allocated once: a stackalloc in the loop would grow the stack with every
        // command, and the file chooses the number of commands
        Span<byte> type = stackalloc byte[4];
        while (commandPosition < commandsEnd)
        {
            var command = encoded[commandPosition++];
            switch (command)
            {
                case CommandInsert:
                {
                    var count = ReadCount(encoded, commandsEnd, ref commandPosition, position);
                    Append(result, ref length, encoded.Slice(position, count));
                    position += count;
                    break;
                }

                case CommandShuffle2 or CommandShuffle4:
                {
                    var count = ReadCount(encoded, commandsEnd, ref commandPosition, position);
                    if (count > result.Length - length)
                        throw Invalid();

                    Unshuffle(encoded.Slice(position, count), result.AsSpan(length, count), command == CommandShuffle2 ? 2 : 4);
                    length += count;
                    position += count;
                    break;
                }

                case CommandPredict:
                {
                    if (commandsEnd - commandPosition < 2)
                        throw Invalid();

                    var flags = encoded[commandPosition++];
                    var width = (flags & 3) + 1;
                    var order = (flags & 12) >> 2;
                    if (width == 3 || order == 3)
                        throw Invalid();

                    ulong stride = (ulong)width;
                    if ((flags & 16) != 0)
                    {
                        stride = ReadVarInt(encoded, commandsEnd, ref commandPosition);
                        if (stride < (ulong)width)
                            throw Invalid();
                    }

                    // The three previous values, each one stride before the next, must exist
                    if (length == 0 || (ulong)((length - 1) >> 2) < stride)
                        throw Invalid();

                    var count = ReadCount(encoded, commandsEnd, ref commandPosition, position);
                    if (count > result.Length - length)
                        throw Invalid();

                    var start = length;
                    var data = result.AsSpan(start, count);
                    if (width > 1)
                    {
                        Unshuffle(encoded.Slice(position, count), data, width);
                    }
                    else
                    {
                        encoded.Slice(position, count).CopyTo(data);
                    }

                    for (var i = 0; i < count; i++)
                    {
                        result[start + i] = (byte)(result[start + i] + PredictValue(result, start, i, (int)stride, width, order));
                    }

                    length += count;
                    position += count;
                    break;
                }

                case CommandXyz:
                    Append(result, ref length, "XYZ "u8);
                    Append(result, ref length, [0, 0, 0, 0]);
                    if (encoded.Length - position < 12)
                        throw Invalid();

                    Append(result, ref length, encoded.Slice(position, 12));
                    position += 12;
                    break;

                case >= CommandTypeStartFirst when command - CommandTypeStartFirst < TypeStrings.Length:
                {
                    Encoding.ASCII.GetBytes(TypeStrings[command - CommandTypeStartFirst], type);
                    Append(result, ref length, type);
                    Append(result, ref length, [0, 0, 0, 0]);
                    break;
                }

                default:
                    throw Invalid();
            }
        }

        if (position != encoded.Length || length != result.Length)
            throw Invalid();

        return result;
    }

    private static InvalidImageContentException Invalid() => JpegXlFormat.Invalid("the encoded ICC profile is not valid.");

    /// <summary>Updates the prediction of the header from the bytes of the profile that precede <paramref name="position"/>.</summary>
    public static void PredictHeader(ReadOnlySpan<byte> profile, Span<byte> header, int position)
    {
        if (position == 8 && profile.Length >= 8)
        {
            // The creator of the profile is predicted to be its preferred color management module
            profile.Slice(4, 4).CopyTo(header[80..]);
        }

        if (position == 41 && profile.Length >= 41)
        {
            if (profile[40] == 'A')
            {
                "PPL"u8.CopyTo(header[41..]);
            }
            else if (profile[40] == 'M')
            {
                "SFT"u8.CopyTo(header[41..]);
            }
        }

        if (position == 42 && profile.Length >= 42 && profile[40] == 'S')
        {
            if (profile[41] == 'G')
            {
                "I "u8.CopyTo(header[42..]);
            }
            else if (profile[41] == 'U')
            {
                "NW"u8.CopyTo(header[42..]);
            }
        }
    }

    /// <summary>Predicts a byte from the three values that precede its value by one, two and three strides (order 0: the first; order 1: linear; order 2: quadratic).</summary>
    private static byte PredictValue(byte[] data, int start, int index, int stride, int width, int order)
    {
        var position = start + index;
        if (width == 1)
            return (byte)Predict(data[position - stride], data[position - (stride * 2)], data[position - (stride * 3)], order);

        if (width == 2)
        {
            var origin = start + (index & ~1);
            var value = Predict(ReadUInt16(data, origin - stride), ReadUInt16(data, origin - (stride * 2)), ReadUInt16(data, origin - (stride * 3)), order);
            return (index & 1) != 0 ? (byte)value : (byte)(value >> 8);
        }
        else
        {
            var origin = start + (index & ~3);
            var value = Predict(ReadUInt32(data, position, origin - stride), ReadUInt32(data, position, origin - (stride * 2)), ReadUInt32(data, position, origin - (stride * 3)), order);
            return (byte)(value >> ((3 - (index & 3)) * 8));
        }

        static uint Predict(uint p1, uint p2, uint p3, int order) => order switch
        {
            0 => p1,
            1 => (2 * p1) - p2,
            _ => (3 * p1) - (3 * p2) + p3,
        };

        static uint ReadUInt16(byte[] data, int position) => (uint)((data[position] << 8) + data[position + 1]);

        // A value that is not complete yet reads as zero
        static uint ReadUInt32(byte[] data, int size, int position) => position + 4 > size ? 0 : BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(position));
    }

    /// <summary>Reverses the transposition of values of <paramref name="width"/> bytes: the first bytes of all the values come first, then their second bytes.</summary>
    private static void Unshuffle(ReadOnlySpan<byte> source, Span<byte> destination, int width)
    {
        var size = source.Length;
        var height = (size + width - 1) / width;
        var start = 0;
        var j = 0;
        for (var i = 0; i < size; i++)
        {
            destination[i] = source[j];
            j += height;
            if (j >= size)
            {
                j = ++start;
            }
        }
    }

    private static int ReadCount(ReadOnlySpan<byte> encoded, int commandsEnd, ref int commandPosition, int position)
    {
        var count = ReadVarInt(encoded, commandsEnd, ref commandPosition);
        if (count > (ulong)(encoded.Length - position))
            throw Invalid();

        return (int)count;
    }

    private static ulong ReadVarInt(ReadOnlySpan<byte> data, int end, ref int position)
    {
        ulong value = 0;
        for (var i = 0; i < 10; i++)
        {
            if (position >= end)
                throw Invalid();

            var current = data[position++];
            if (i == 9)
            {
                if ((current & 0xFE) != 0)
                    throw Invalid();

                return value | ((ulong)current << 63);
            }

            value |= (ulong)(current & 0x7F) << (7 * i);
            if ((current & 0x80) == 0)
                return value;
        }

        return value;
    }

    private static void Append(byte[] result, ref int length, ReadOnlySpan<byte> data)
    {
        if (data.Length > result.Length - length)
            throw Invalid();

        data.CopyTo(result.AsSpan(length));
        length += data.Length;
    }

    private static void AppendUInt32(byte[] result, ref int length, uint value)
    {
        if (result.Length - length < 4)
            throw Invalid();

        BinaryPrimitives.WriteUInt32BigEndian(result.AsSpan(length), value);
        length += 4;
    }
}
