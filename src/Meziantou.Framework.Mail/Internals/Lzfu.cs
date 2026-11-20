using System.Buffers.Binary;
using System.Text;
using Meziantou.Framework.Mail.Internals.Ndb;

namespace Meziantou.Framework.Mail.Internals;

/// <summary>The compressed RTF format of [MS-OXRTFCP], used by the PidTagRtfCompressed property.</summary>
internal static class Lzfu
{
    public const uint CompressedType = 0x75465A4C; // "LZFu"
    public const uint UncompressedType = 0x414C454D; // "MELA"
    private const int HeaderSize = 16;
    private const int DictionarySize = 4096;
    private const int MaxMatchLength = 17;
    private const int MinMatchLength = 3;

    // The initial content of the dictionary ([MS-OXRTFCP] 2.1.2.1)
    private static readonly byte[] InitialDictionary = Encoding.ASCII.GetBytes(
        "{\\rtf1\\ansi\\mac\\deff0\\deftab720{\\fonttbl;}{\\f0\\fnil \\froman \\fswiss \\fmodern \\fscript \\fdecor MS Sans SerifSymbolArialTimes New RomanCourier{\\colortbl\\red0\\green0\\blue0\r\n\\par \\pard\\plain\\f0\\fs20\\b\\i\\u\\tab\\tx");

    /// <summary>Decompresses a compressed RTF stream.</summary>
    /// <exception cref="InvalidPstException">The data is not a valid compressed RTF stream.</exception>
    public static byte[] Decompress(ReadOnlySpan<byte> data, long maxSize, bool verifyCrc = true)
    {
        if (data.Length < HeaderSize)
        {
            throw new InvalidPstException("The compressed RTF is shorter than its header.");
        }

        var compressedSize = BinaryPrimitives.ReadUInt32LittleEndian(data);
        var rawSize = BinaryPrimitives.ReadUInt32LittleEndian(data[4..]);
        var type = BinaryPrimitives.ReadUInt32LittleEndian(data[8..]);
        var crc = BinaryPrimitives.ReadUInt32LittleEndian(data[12..]);
        if (rawSize > maxSize)
        {
            throw new PstResourceLimitException($"The compressed RTF has {rawSize} bytes, more than the limit of {maxSize} bytes.");
        }

        // COMPSIZE counts the bytes after itself
        var payloadLength = (int)Math.Min((long)compressedSize - 12, data.Length - HeaderSize);
        if (compressedSize < 12 || payloadLength < 0)
        {
            throw new InvalidPstException("The compressed RTF has an invalid size.");
        }

        var payload = data.Slice(HeaderSize, payloadLength);
        if (type == UncompressedType)
        {
            // [MS-OXRTFCP] 2.2.3.1: the reader may read all the bytes, whatever RAWSIZE says, and does not check the CRC
            return payload[..(int)Math.Min(rawSize, (uint)payload.Length)].ToArray();
        }

        if (type != CompressedType)
        {
            throw new InvalidPstException($"The compressed RTF has an unknown type 0x{type:X8}.");
        }

        if (verifyCrc && PstCrc.Compute(payload) != crc)
        {
            throw new InvalidPstException("The CRC of the compressed RTF does not match its content.");
        }

        var dictionary = new byte[DictionarySize];
        InitialDictionary.CopyTo(dictionary, 0);
        var writePosition = InitialDictionary.Length;
        // A reference (2 bytes) produces at most 17 bytes: the output cannot be larger than 9 times the input, whatever RAWSIZE says
        var output = new byte[Math.Min(rawSize, ((long)payload.Length * 9) + 16)];
        var outputLength = 0;
        var position = 0;
        while (position < payload.Length)
        {
            var control = payload[position++];
            for (var bit = 0; bit < 8; bit++)
            {
                if ((control & (1 << bit)) == 0)
                {
                    if (position >= payload.Length)
                    {
                        return Finish(output, outputLength);
                    }

                    var value = payload[position++];
                    dictionary[writePosition] = value;
                    writePosition = (writePosition + 1) % DictionarySize;
                    if (!Append(output, ref outputLength, value))
                    {
                        return output;
                    }
                }
                else
                {
                    if (position + 2 > payload.Length)
                    {
                        throw new InvalidPstException("The compressed RTF is truncated.");
                    }

                    var reference = BinaryPrimitives.ReadUInt16BigEndian(payload[position..]);
                    position += 2;
                    var offset = reference >> 4;
                    var length = (reference & 0xF) + 2;
                    if (offset == writePosition)
                    {
                        return Finish(output, outputLength);
                    }

                    for (var i = 0; i < length; i++)
                    {
                        var value = dictionary[(offset + i) % DictionarySize];
                        dictionary[writePosition] = value;
                        writePosition = (writePosition + 1) % DictionarySize;
                        if (!Append(output, ref outputLength, value))
                        {
                            return output;
                        }
                    }
                }
            }
        }

        return Finish(output, outputLength);
    }

    /// <summary>Compresses RTF to the compressed RTF format.</summary>
    public static byte[] Compress(ReadOnlySpan<byte> rtf)
    {
        var dictionary = new byte[DictionarySize];
        InitialDictionary.CopyTo(dictionary, 0);
        var writePosition = InitialDictionary.Length;

        // Hash chains over the positions of the dictionary, keyed by the 3 bytes that start at each position
        var head = new int[4096];
        var previous = new int[DictionarySize];
        head.AsSpan().Fill(-1);
        previous.AsSpan().Fill(-1);
        for (var i = 0; i + 2 < InitialDictionary.Length; i++)
        {
            Insert(dictionary, head, previous, i);
        }

        using var output = new MemoryStream();
        Span<byte> header = stackalloc byte[HeaderSize];
        output.Write(header);

        var position = 0;
        var tokens = new byte[1 + (8 * 2)];
        var done = false;
        while (!done)
        {
            byte control = 0;
            var tokenLength = 1;
            for (var bit = 0; bit < 8; bit++)
            {
                if (position >= rtf.Length)
                {
                    // The end of the stream is a reference to the current write position
                    control |= (byte)(1 << bit);
                    BinaryPrimitives.WriteUInt16BigEndian(tokens.AsSpan(tokenLength), (ushort)(writePosition << 4));
                    tokenLength += 2;
                    done = true;
                    break;
                }

                var (matchOffset, matchLength) = FindMatch(dictionary, head, previous, writePosition, rtf[position..]);
                if (matchLength >= MinMatchLength)
                {
                    control |= (byte)(1 << bit);
                    BinaryPrimitives.WriteUInt16BigEndian(tokens.AsSpan(tokenLength), (ushort)((matchOffset << 4) | (matchLength - 2)));
                    tokenLength += 2;
                }
                else
                {
                    matchLength = 1;
                    tokens[tokenLength++] = rtf[position];
                }

                for (var i = 0; i < matchLength; i++)
                {
                    dictionary[writePosition] = rtf[position + i];
                    Insert(dictionary, head, previous, (writePosition - 2 + DictionarySize) % DictionarySize);
                    writePosition = (writePosition + 1) % DictionarySize;
                }

                position += matchLength;
            }

            tokens[0] = control;
            output.Write(tokens, 0, tokenLength);
        }

        var result = output.ToArray();
        BinaryPrimitives.WriteUInt32LittleEndian(result, (uint)(result.Length - 4));
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(4), (uint)rtf.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(8), CompressedType);
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(12), PstCrc.Compute(result.AsSpan(HeaderSize)));
        return result;
    }

    /// <summary>Stores RTF in the uncompressed variant of the format ("MELA").</summary>
    public static byte[] Store(ReadOnlySpan<byte> rtf)
    {
        var result = new byte[HeaderSize + rtf.Length];
        BinaryPrimitives.WriteUInt32LittleEndian(result, (uint)(result.Length - 4));
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(4), (uint)rtf.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(8), UncompressedType);
        rtf.CopyTo(result.AsSpan(HeaderSize));
        return result;
    }

    private static int Hash(byte a, byte b, byte c) => ((a << 4) ^ (b << 2) ^ c) & 0xFFF;

    private static void Insert(byte[] dictionary, int[] head, int[] previous, int position)
    {
        var hash = Hash(dictionary[position], dictionary[(position + 1) % DictionarySize], dictionary[(position + 2) % DictionarySize]);
        previous[position] = head[hash];
        head[hash] = position;
    }

    private static (int Offset, int Length) FindMatch(byte[] dictionary, int[] head, int[] previous, int writePosition, ReadOnlySpan<byte> input)
    {
        if (input.Length < MinMatchLength)
        {
            return (0, 0);
        }

        var maxLength = Math.Min(MaxMatchLength, input.Length);
        var bestOffset = 0;
        var bestLength = 0;
        var candidate = head[Hash(input[0], input[1], input[2])];
        for (var chain = 0; candidate >= 0 && chain < 64; chain++)
        {
            // A reference to the write position marks the end of the stream
            if (candidate != writePosition)
            {
                var length = MatchLength(dictionary, writePosition, candidate, input, maxLength);
                if (length > bestLength)
                {
                    bestLength = length;
                    bestOffset = candidate;
                    if (length == maxLength)
                    {
                        break;
                    }
                }
            }

            var next = previous[candidate];
            if (next == candidate)
            {
                break;
            }

            candidate = next;
        }

        return (bestOffset, bestLength);
    }

    /// <summary>Returns the length the decoder would copy from <paramref name="offset"/>, including the bytes the copy itself writes.</summary>
    private static int MatchLength(byte[] dictionary, int writePosition, int offset, ReadOnlySpan<byte> input, int maxLength)
    {
        for (var i = 0; i < maxLength; i++)
        {
            var source = (offset + i) % DictionarySize;
            var distance = (source - writePosition + DictionarySize) % DictionarySize;
            var value = distance < i ? input[distance] : dictionary[source];
            if (value != input[i])
            {
                return i;
            }
        }

        return maxLength;
    }

    private static bool Append(byte[] output, ref int length, byte value)
    {
        if (length >= output.Length)
        {
            return false;
        }

        output[length++] = value;
        return true;
    }

    private static byte[] Finish(byte[] output, int length) => length == output.Length ? output : output.AsSpan(0, length).ToArray();
}
