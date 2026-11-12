using System.Buffers;
using System.Diagnostics;
using System.IO.Compression;

namespace Meziantou.Framework.WebPlatform.Compression;

/// <summary>
/// The decoders of the content codings of the compression dictionaries (https://www.rfc-editor.org/rfc/rfc9842#section-4):
/// <c>dcb</c> (Brotli, RFC 7932, with the dictionary as a raw prefix) and <c>dcz</c> (Zstandard, RFC 8878, with the
/// dictionary as a raw content dictionary). The encoded data start with a header that has the SHA-256 hash of the dictionary.
/// </summary>
internal static class DictionaryDecompressor
{
    /// <summary>The length of the SHA-256 hash of the dictionary in the header of the encoded data.</summary>
    public const int HashLength = 32;

    // https://www.rfc-editor.org/rfc/rfc9842#section-4.1 and #section-4.2: the magic numbers of the headers
    private static ReadOnlySpan<byte> BrotliMagic => [0xFF, 0x44, 0x43, 0x42];

    private static ReadOnlySpan<byte> ZstandardMagic => [0x5E, 0x2A, 0x4D, 0x18, 0x20, 0x00, 0x00, 0x00];

    // RFC 7932, section 9.1: the largest window of a Brotli stream that is not a large-window stream
    private const int MaxWindowBits = 24;

    // RFC 7932, section 7.1: the lookup tables of the context IDs of the UTF8 and signed context modes, for the last byte
    // (p1) and the byte before it (p2)
    private static ReadOnlySpan<byte> Utf8ContextP1 =>
    [
        0, 0, 0, 0, 0, 0, 0, 0, 0, 4, 4, 0, 0, 4, 0, 0,
        0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
        8, 12, 16, 12, 12, 20, 12, 16, 24, 28, 12, 12, 32, 12, 36, 12,
        44, 44, 44, 44, 44, 44, 44, 44, 44, 44, 32, 32, 24, 40, 28, 12,
        12, 48, 52, 52, 52, 48, 52, 52, 52, 48, 52, 52, 52, 52, 52, 48,
        52, 52, 52, 52, 52, 48, 52, 52, 52, 52, 52, 24, 12, 28, 12, 12,
        12, 56, 60, 60, 60, 56, 60, 60, 60, 56, 60, 60, 60, 60, 60, 56,
        60, 60, 60, 60, 60, 56, 60, 60, 60, 60, 60, 24, 12, 28, 12, 0,
        0, 1, 0, 1, 0, 1, 0, 1, 0, 1, 0, 1, 0, 1, 0, 1,
        0, 1, 0, 1, 0, 1, 0, 1, 0, 1, 0, 1, 0, 1, 0, 1,
        0, 1, 0, 1, 0, 1, 0, 1, 0, 1, 0, 1, 0, 1, 0, 1,
        0, 1, 0, 1, 0, 1, 0, 1, 0, 1, 0, 1, 0, 1, 0, 1,
        2, 3, 2, 3, 2, 3, 2, 3, 2, 3, 2, 3, 2, 3, 2, 3,
        2, 3, 2, 3, 2, 3, 2, 3, 2, 3, 2, 3, 2, 3, 2, 3,
        2, 3, 2, 3, 2, 3, 2, 3, 2, 3, 2, 3, 2, 3, 2, 3,
        2, 3, 2, 3, 2, 3, 2, 3, 2, 3, 2, 3, 2, 3, 2, 3
    ];

    private static ReadOnlySpan<byte> Utf8ContextP2 =>
    [
        0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
        0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
        0, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1,
        2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 1, 1, 1, 1, 1, 1,
        1, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2,
        2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 1, 1, 1, 1, 1,
        1, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3,
        3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 1, 1, 1, 1, 0,
        0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
        0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
        0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
        0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
        0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
        0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
        2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2,
        2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2
    ];

    private static ReadOnlySpan<byte> SignedContextP1 =>
    [
        0, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8, 8,
        16, 16, 16, 16, 16, 16, 16, 16, 16, 16, 16, 16, 16, 16, 16, 16,
        16, 16, 16, 16, 16, 16, 16, 16, 16, 16, 16, 16, 16, 16, 16, 16,
        16, 16, 16, 16, 16, 16, 16, 16, 16, 16, 16, 16, 16, 16, 16, 16,
        24, 24, 24, 24, 24, 24, 24, 24, 24, 24, 24, 24, 24, 24, 24, 24,
        24, 24, 24, 24, 24, 24, 24, 24, 24, 24, 24, 24, 24, 24, 24, 24,
        24, 24, 24, 24, 24, 24, 24, 24, 24, 24, 24, 24, 24, 24, 24, 24,
        24, 24, 24, 24, 24, 24, 24, 24, 24, 24, 24, 24, 24, 24, 24, 24,
        32, 32, 32, 32, 32, 32, 32, 32, 32, 32, 32, 32, 32, 32, 32, 32,
        32, 32, 32, 32, 32, 32, 32, 32, 32, 32, 32, 32, 32, 32, 32, 32,
        32, 32, 32, 32, 32, 32, 32, 32, 32, 32, 32, 32, 32, 32, 32, 32,
        32, 32, 32, 32, 32, 32, 32, 32, 32, 32, 32, 32, 32, 32, 32, 32,
        40, 40, 40, 40, 40, 40, 40, 40, 40, 40, 40, 40, 40, 40, 40, 40,
        40, 40, 40, 40, 40, 40, 40, 40, 40, 40, 40, 40, 40, 40, 40, 40,
        40, 40, 40, 40, 40, 40, 40, 40, 40, 40, 40, 40, 40, 40, 40, 40,
        48, 48, 48, 48, 48, 48, 48, 48, 48, 48, 48, 48, 48, 48, 48, 56
    ];

    private static ReadOnlySpan<byte> SignedContextP2 =>
    [
        0, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1,
        2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2,
        2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2,
        2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2,
        3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3,
        3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3,
        3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3,
        3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3,
        4, 4, 4, 4, 4, 4, 4, 4, 4, 4, 4, 4, 4, 4, 4, 4,
        4, 4, 4, 4, 4, 4, 4, 4, 4, 4, 4, 4, 4, 4, 4, 4,
        4, 4, 4, 4, 4, 4, 4, 4, 4, 4, 4, 4, 4, 4, 4, 4,
        4, 4, 4, 4, 4, 4, 4, 4, 4, 4, 4, 4, 4, 4, 4, 4,
        5, 5, 5, 5, 5, 5, 5, 5, 5, 5, 5, 5, 5, 5, 5, 5,
        5, 5, 5, 5, 5, 5, 5, 5, 5, 5, 5, 5, 5, 5, 5, 5,
        5, 5, 5, 5, 5, 5, 5, 5, 5, 5, 5, 5, 5, 5, 5, 5,
        6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 7
    ];

    /// <summary>Whether a content coding is one of the dictionary content codings (<c>dcb</c> or <c>dcz</c>).</summary>
    public static bool IsDictionaryCoding(string coding) => coding is "dcb" or "dcz";

    /// <summary>
    /// Decodes data encoded with a dictionary content coding. The hash of the header must be the SHA-256 hash of the dictionary.
    /// </summary>
    /// <param name="coding">The content coding: <c>dcb</c> or <c>dcz</c>.</param>
    /// <param name="data">The encoded data, with their header.</param>
    /// <param name="dictionary">The dictionary.</param>
    /// <param name="dictionaryHash">The SHA-256 hash of the dictionary.</param>
    /// <param name="maxLength">The maximum length of the decoded data.</param>
    /// <returns>The decoded data.</returns>
    /// <exception cref="InvalidDataException">The data are not valid, or the hash of their header is not the one of the dictionary.</exception>
    public static byte[] Decompress(string coding, ReadOnlySpan<byte> data, ReadOnlySpan<byte> dictionary, ReadOnlySpan<byte> dictionaryHash, int maxLength)
    {
        var magic = coding switch
        {
            "dcb" => BrotliMagic,
            "dcz" => ZstandardMagic,
            _ => throw new ArgumentOutOfRangeException(nameof(coding)),
        };

        if (data.Length < magic.Length + HashLength || !data.StartsWith(magic))
            throw new InvalidDataException("The header of the dictionary-compressed data is invalid");

        if (!data.Slice(magic.Length, HashLength).SequenceEqual(dictionaryHash))
            throw new InvalidDataException("The dictionary-compressed data were compressed with another dictionary");

        var payload = data[(magic.Length + HashLength)..];
        return coding == "dcb"
            ? DecompressBrotli(payload, dictionary, maxLength)
            : new ZstdDecoder().DecompressWithRawDictionary(payload, dictionary, maxLength);
    }

    /// <summary>
    /// Decodes a Brotli stream compressed with a raw prefix dictionary (the "compound dictionary" of the reference decoder).
    /// The decoder of the base class library has no dictionary support: the stream is decoded as the stream whose first
    /// meta-blocks produce the dictionary, followed by the meta-blocks of the stream. A copy whose distance is beyond the
    /// bytes decoded before it reads the dictionary as it reads the end of the dictionary of the reference decoder, as long
    /// as the dictionary and the decoded data fit in the window of the new stream and the decoded data fit in the window of
    /// the stream. The context of the first two literals is computed from the bytes before them, which are the end of the
    /// dictionary instead of zeros: a stream whose context map selects another literal tree for them is not supported (see
    /// <see cref="CheckFirstMetaBlock"/>).
    /// </summary>
    internal static byte[] DecompressBrotli(ReadOnlySpan<byte> stream, ReadOnlySpan<byte> dictionary, int maxLength)
    {
        var reader = new BitReader(stream);
        var windowBits = ReadWindowBits(ref reader);
        var headerBits = (int)reader.Position;
        var maxDistance = (1L << MaxWindowBits) - 16;
        var streamMaxDistance = (1L << windowBits) - 16;
        if (dictionary.Length > maxDistance)
            throw new InvalidDataException("The dictionary of the Brotli stream is too long");

        ReadOnlySpan<byte> input;
        if (dictionary.IsEmpty)
        {
            input = stream;
        }
        else
        {
            CheckFirstMetaBlock(stream, headerBits, dictionary);

            // The meta-blocks after the dictionary start at the same position in a byte as after the header of the stream, so
            // that the uncompressed and metadata meta-blocks of the stream stay aligned: the last byte of the dictionary is a
            // compressed meta-block whose length is chosen accordingly
            var writer = new BitWriter(stream.Length + dictionary.Length + (((dictionary.Length / 65536) + 1) * 4) + 16);
            writer.Write(1, 1);
            writer.Write(7, 3);
            var uncompressed = dictionary[..^1];
            for (var offset = 0; offset < uncompressed.Length; offset += 65536)
            {
                var length = Math.Min(65536, uncompressed.Length - offset);

                // ISLAST = 0, MNIBBLES = 4, MLEN - 1, ISUNCOMPRESSED = 1, then the bytes from the next byte boundary
                writer.Write(0, 1);
                writer.Write(0, 2);
                writer.Write(length - 1, 16);
                writer.Write(1, 1);
                writer.AlignToByte();
                writer.WriteBytes(uncompressed.Slice(offset, length));
            }

            writer.AlignToByte();
            WriteSingleByteMetaBlock(writer, dictionary[^1], headerBits);
            writer.WriteTail(stream, headerBits);
            input = writer.WrittenSpan;
        }

        var limit = (long)dictionary.Length + Math.Min(maxLength, streamMaxDistance);
        var output = new ArrayBufferWriter<byte>(Math.Max(dictionary.Length + (stream.Length * 4), 4096));
        using var decoder = new BrotliDecoder();
        while (true)
        {
            var status = decoder.Decompress(input, output.GetSpan(65536), out var consumed, out var written);
            output.Advance(written);
            input = input[consumed..];
            if (output.WrittenCount > limit || (!dictionary.IsEmpty && output.WrittenCount > maxDistance))
                throw new InvalidDataException("The Brotli stream is too long for its dictionary");

            switch (status)
            {
                case OperationStatus.Done:
                    if (!input.IsEmpty)
                        throw new InvalidDataException("The Brotli stream has data after its end");

                    return output.WrittenSpan[dictionary.Length..].ToArray();

                case OperationStatus.DestinationTooSmall:
                    continue;

                default:
                    throw new InvalidDataException("The Brotli stream is invalid");
            }
        }
    }

    /// <summary>
    /// Writes a compressed meta-block (RFC 7932, section 9.2) that produces a byte, whose length in bits is congruent to
    /// <paramref name="residue"/> modulo 8, from a byte boundary. Its prefix codes are simple prefix codes (section 3.4): the
    /// number of their symbols and the size of the distance alphabet (NPOSTFIX and NDIRECT) give the length.
    /// </summary>
    private static void WriteSingleByteMetaBlock(BitWriter writer, byte value, int residue)
    {
        // The length is 63 bits, plus the bits of a distance symbol (6 to 9 bits), plus 9 bits with a second literal symbol,
        // plus 11 bits with a second command symbol
        for (var variant = 0; variant < 16; variant++)
        {
            var distanceBits = 6 + (variant & 3);
            var twoLiterals = (variant & 4) != 0;
            var twoCommands = (variant & 8) != 0;
            var length = 63 + distanceBits + (twoLiterals ? 9 : 0) + (twoCommands ? 11 : 0);
            if (length % 8 != residue)
                continue;

            // ISLAST = 0, MNIBBLES = 4, MLEN - 1 = 0, ISUNCOMPRESSED = 0, NBLTYPESL = NBLTYPESI = NBLTYPESD = 1
            writer.Write(0, 1);
            writer.Write(0, 2);
            writer.Write(0, 16);
            writer.Write(0, 1);
            writer.Write(0, 3);

            // NPOSTFIX and NDIRECT: the size of the distance alphabet is 16 + NDIRECT + (48 << NPOSTFIX)
            var (postfix, direct) = distanceBits switch
            {
                6 => (0, 0),
                7 => (0, 1),
                8 => (2, 0),
                _ => (3, 0),
            };
            writer.Write(postfix, 2);
            writer.Write(direct, 4);

            // The context mode of the literals (LSB6), NTREESL = NTREESD = 1
            writer.Write(0, 2);
            writer.Write(0, 2);

            // The literal code: the byte (and another symbol); the smaller symbol has the code 0
            var otherLiteral = value ^ 1;
            WriteSimplePrefixCode(writer, 8, twoLiterals ? [value, otherLiteral] : [value]);

            // The insert-and-copy code: the command 8 (an insert length of 1 and a copy length of 2, ignored because the
            // meta-block ends after the insert), and the command 9
            WriteSimplePrefixCode(writer, 10, twoCommands ? [8, 9] : [8]);

            // The distance code (no distance is read)
            WriteSimplePrefixCode(writer, distanceBits, [0]);

            // The command, then the literal
            if (twoCommands)
            {
                writer.Write(0, 1);
            }

            if (twoLiterals)
            {
                writer.Write(value < otherLiteral ? 0 : 1, 1);
            }

            return;
        }

        throw new UnreachableException();
    }

    /// <summary>RFC 7932, section 3.4: a simple prefix code (HSKIP = 1, NSYM - 1, the symbols).</summary>
    private static void WriteSimplePrefixCode(BitWriter writer, int alphabetBits, ReadOnlySpan<int> symbols)
    {
        writer.Write(1, 2);
        writer.Write(symbols.Length - 1, 2);
        foreach (var symbol in symbols)
        {
            writer.Write(symbol, alphabetBits);
        }
    }

    /// <summary>RFC 7932, section 9.1: the window size of a stream (WBITS).</summary>
    private static int ReadWindowBits(ref BitReader reader)
    {
        if (reader.ReadBits(1) == 0)
            return 16;

        var n = reader.ReadBits(3);
        if (n != 0)
            return 17 + n;

        var m = reader.ReadBits(3);
        return m switch
        {
            0 => 17,
            1 => throw new InvalidDataException("The large-window Brotli streams are not supported"),
            _ => 8 + m,
        };
    }

    /// <summary>
    /// Checks that the context of the first two literals of the stream selects the same literal tree with the end of the
    /// dictionary (in the emulated stream) as with zeros (the reference decoder), see <see cref="DecompressBrotli"/>. The
    /// header of the first meta-block that produces bytes is read: the block switch codes, the context modes of the literal
    /// block types, the literal context map and the prefix codes (RFC 7932, section 9.2), then the first command and its
    /// first literal: the first two bytes of the output are literals only when the first command inserts them, and the
    /// context of the second literal is computed from the first one.
    /// </summary>
    /// <exception cref="InvalidDataException">The first literals could use another literal tree.</exception>
    private static void CheckFirstMetaBlock(ReadOnlySpan<byte> stream, int headerBits, ReadOnlySpan<byte> dictionary)
    {
        var reader = new BitReader(stream) { Position = headerBits };
        while (true)
        {
            // RFC 7932, section 9.2: ISLAST, ISLASTEMPTY, MNIBBLES, MLEN, ISUNCOMPRESSED
            var isLast = reader.ReadBits(1) == 1;
            if (isLast && reader.ReadBits(1) == 1)
                return;

            var nibbles = reader.ReadBits(2);
            if (nibbles == 3)
            {
                // A metadata meta-block (reserved bit, MSKIPBYTES, MSKIPLEN - 1): its bytes are skipped
                if (reader.ReadBits(1) != 0)
                    throw new InvalidDataException("The Brotli stream is invalid");

                var skipBytes = reader.ReadBits(2);
                var skipLength = 0;
                for (var i = 0; i < skipBytes; i++)
                {
                    skipLength |= reader.ReadBits(8) << (8 * i);
                }

                if (skipBytes > 0)
                {
                    skipLength++;
                }

                reader.AlignToByte();
                reader.Position += 8L * skipLength;
                if (isLast)
                    return;

                continue;
            }

            var length = 0;
            for (var i = 0; i < nibbles + 4; i++)
            {
                length |= reader.ReadBits(4) << (4 * i);
            }

            length++;

            // The second byte of a meta-block of one byte is produced by the next meta-block: not supported
            var isUncompressed = !isLast && reader.ReadBits(1) == 1;
            if (length < 2)
                throw new InvalidDataException("The Brotli streams compressed with a dictionary whose first meta-block has a single byte are not supported");

            if (isUncompressed)
                return;

            // NBLTYPESL and the block switch codes of the literals, of the commands and of the distances
            var literalBlockTypes = SkipBlockSwitchCodes(ref reader);
            var commandBlockTypes = SkipBlockSwitchCodes(ref reader);
            var distanceBlockTypes = SkipBlockSwitchCodes(ref reader);

            // NPOSTFIX, NDIRECT, the context modes of the literal block types
            var postfixBits = reader.ReadBits(2);
            var directDistanceCodes = reader.ReadBits(4) << postfixBits;
            var modes = new int[literalBlockTypes];
            for (var i = 0; i < modes.Length; i++)
            {
                modes[i] = reader.ReadBits(2);
            }

            // NTREESL: with a single literal tree, the context does not matter
            var trees = ReadVarLengthCount(ref reader);
            if (trees < 2)
                return;

            var map = new LiteralContextMap(ReadContextMap(ref reader, trees, 64 * literalBlockTypes), modes);

            // NTREESD and the distance context map, the prefix codes of the literals, of the commands and of the distances, then
            // the first command: the first literals are the first bytes of the output only when the first command inserts them
            var distanceTrees = ReadVarLengthCount(ref reader);
            if (distanceTrees > 1)
            {
                ReadContextMap(ref reader, distanceTrees, 4 * distanceBlockTypes);
            }

            var literalCodes = new PrefixCode[trees];
            for (var i = 0; i < trees; i++)
            {
                literalCodes[i] = PrefixCode.Read(ref reader, 256);
            }

            var commandCode = PrefixCode.Read(ref reader, 704);
            for (var i = 1; i < commandBlockTypes; i++)
            {
                PrefixCode.Read(ref reader, 704);
            }

            for (var i = 0; i < distanceTrees; i++)
            {
                PrefixCode.Read(ref reader, 16 + directDistanceCodes + (48 << postfixBits));
            }

            var command = commandCode.ReadSymbol(ref reader);
            var insertLengthCode = GetInsertLengthCode(command);
            if (insertLengthCode == 0)
                return;

            var p1 = dictionary[^1];
            var p2 = dictionary.Length > 1 ? dictionary[^2] : (byte)0;
            if (!map.SelectsSameTree(0, 0, p1, p2))
                throw new InvalidDataException("The Brotli streams compressed with a dictionary whose first literal depends on the bytes before the stream are not supported");

            // With a single inserted literal, the second byte is copied
            if (insertLengthCode == 1)
                return;

            // The first literal (after the extra bits of the insert length and of the copy length) is the byte before the second one
            reader.ReadBits(InsertLengthExtraBits[insertLengthCode]);
            reader.ReadBits(CopyLengthExtraBits[GetCopyLengthCode(command)]);
            var first = (byte)literalCodes[map.GetTree(0, 0, 0)].ReadSymbol(ref reader);
            if (!map.SelectsSameTree(first, 0, first, p1))
                throw new InvalidDataException("The Brotli streams compressed with a dictionary whose second literal depends on the bytes before the stream are not supported");

            return;
        }
    }

    /// <summary>Reads NBLTYPESx and, with several block types, skips the prefix codes of the block types and counts and the first block count.</summary>
    private static int SkipBlockSwitchCodes(ref BitReader reader)
    {
        var blockTypes = ReadVarLengthCount(ref reader);
        if (blockTypes < 2)
            return blockTypes;

        PrefixCode.Read(ref reader, blockTypes + 2);
        var countCode = PrefixCode.Read(ref reader, 26);
        reader.ReadBits(BlockLengthExtraBits[countCode.ReadSymbol(ref reader)]);
        return blockTypes;
    }

    /// <summary>
    /// RFC 7932, section 5: the insert length code of an insert-and-copy symbol. The symbols are cells of 64 symbols, whose
    /// insert length codes start at 0 (the first 4 cells), 8 (256 to 383 and 512 to 575) or 16 (448 to 511 and 576 to 703).
    /// </summary>
    private static int GetInsertLengthCode(int symbol)
    {
        var cellInsertBase = (symbol >> 6) switch
        {
            0 or 1 or 2 or 3 or 6 => 0,
            4 or 5 or 8 => 8,
            _ => 16,
        };

        return cellInsertBase + ((symbol >> 3) & 7);
    }

    /// <summary>RFC 7932, section 5: the copy length code of an insert-and-copy symbol (see <see cref="GetInsertLengthCode"/>).</summary>
    private static int GetCopyLengthCode(int symbol)
    {
        var cellCopyBase = (symbol >> 6) switch
        {
            0 or 2 or 4 or 7 => 0,
            1 or 3 or 5 or 9 => 8,
            _ => 16,
        };

        return cellCopyBase + (symbol & 7);
    }

    // RFC 7932, section 5: the number of extra bits of the insert length codes and of the copy length codes
    private static ReadOnlySpan<byte> InsertLengthExtraBits => [0, 0, 0, 0, 0, 0, 1, 1, 2, 2, 3, 3, 4, 4, 5, 5, 6, 7, 8, 9, 10, 12, 14, 24];

    private static ReadOnlySpan<byte> CopyLengthExtraBits => [0, 0, 0, 0, 0, 0, 0, 0, 1, 1, 2, 2, 3, 3, 4, 4, 5, 5, 6, 7, 8, 9, 10, 24];

    // RFC 7932, section 6: the number of extra bits of the block count codes
    private static ReadOnlySpan<byte> BlockLengthExtraBits => [2, 2, 2, 2, 3, 3, 3, 3, 4, 4, 4, 4, 5, 5, 5, 5, 6, 6, 7, 8, 9, 10, 11, 12, 13, 24];

    /// <summary>RFC 7932, section 7.3: a context map (RLEMAX, the prefix code, the values with their runs of zeros, IMTF).</summary>
    private static byte[] ReadContextMap(ref BitReader reader, int trees, int size)
    {
        var maxRunLengthPrefix = reader.ReadBits(1) == 1 ? reader.ReadBits(4) + 1 : 0;
        var code = PrefixCode.Read(ref reader, trees + maxRunLengthPrefix);
        var map = new byte[size];
        for (var i = 0; i < size;)
        {
            var symbol = code.ReadSymbol(ref reader);
            if (symbol == 0)
            {
                map[i++] = 0;
            }
            else if (symbol <= maxRunLengthPrefix)
            {
                var run = (1 << symbol) + reader.ReadBits(symbol);
                if (run > size - i)
                    throw new InvalidDataException("The Brotli stream is invalid");

                i += run;
            }
            else
            {
                map[i++] = (byte)(symbol - maxRunLengthPrefix);
            }
        }

        // The inverse move-to-front transform
        if (reader.ReadBits(1) == 1)
        {
            Span<byte> mtf = stackalloc byte[256];
            for (var i = 0; i < mtf.Length; i++)
            {
                mtf[i] = (byte)i;
            }

            for (var i = 0; i < map.Length; i++)
            {
                var index = map[i];
                var value = mtf[index];
                map[i] = value;
                mtf[..index].CopyTo(mtf[1..]);
                mtf[0] = value;
            }
        }

        return map;
    }

    /// <summary>RFC 7932, section 9.2: the encoding of NBLTYPESx and NTREESx (1 to 256).</summary>
    private static int ReadVarLengthCount(ref BitReader reader)
    {
        if (reader.ReadBits(1) == 0)
            return 1;

        var bits = reader.ReadBits(3);
        return bits == 0 ? 2 : (1 << bits) + reader.ReadBits(bits) + 1;
    }

    /// <summary>The literal context map of the first meta-block and the context modes of its literal block types.</summary>
    private sealed class LiteralContextMap(byte[] map, int[] modes)
    {
        /// <summary>Whether every literal block type selects the same tree for the two pairs of previous bytes.</summary>
        public bool SelectsSameTree(byte p1, byte p2, byte otherP1, byte otherP2)
        {
            for (var blockType = 0; blockType < modes.Length; blockType++)
            {
                if (map[(64 * blockType) + GetContext(modes[blockType], p1, p2)] != map[(64 * blockType) + GetContext(modes[blockType], otherP1, otherP2)])
                    return false;
            }

            return true;
        }

        /// <summary>The literal tree of a literal block type for the two previous bytes.</summary>
        public int GetTree(int blockType, byte p1, byte p2) => map[(64 * blockType) + GetContext(modes[blockType], p1, p2)];

        /// <summary>RFC 7932, section 7.1: the context ID of a literal.</summary>
        private static int GetContext(int mode, byte p1, byte p2) => mode switch
        {
            0 => p1 & 0x3F,
            1 => p1 >> 2,
            2 => Utf8ContextP1[p1] | Utf8ContextP2[p2],
            _ => SignedContextP1[p1] | SignedContextP2[p2],
        };
    }

    /// <summary>A prefix code (RFC 7932, sections 3.4 and 3.5), whose symbols are read bit by bit.</summary>
    private sealed class PrefixCode
    {
        private readonly Dictionary<(int Length, int Code), int> _symbols = [];
        private int _singleSymbol = -1;
        private int _maxLength;

        // Section 3.5: the order of the code length code lengths
        private static ReadOnlySpan<byte> CodeLengthOrder => [1, 2, 3, 4, 0, 5, 17, 6, 16, 7, 8, 9, 10, 11, 12, 13, 14, 15];

        public static PrefixCode Read(ref BitReader reader, int alphabetSize)
        {
            var lengths = new byte[alphabetSize];
            var skip = reader.ReadBits(2);
            if (skip == 1)
            {
                // A simple prefix code: NSYM - 1, the symbols, and the tree-select bit of 4 symbols
                var alphabetBits = 0;
                while ((1 << alphabetBits) < alphabetSize)
                {
                    alphabetBits++;
                }

                var count = reader.ReadBits(2) + 1;
                Span<int> symbols = stackalloc int[4];
                for (var i = 0; i < count; i++)
                {
                    symbols[i] = reader.ReadBits(alphabetBits);
                    if (symbols[i] >= alphabetSize)
                        throw new InvalidDataException("The Brotli stream is invalid");
                }

                ReadOnlySpan<byte> codeLengths = count switch
                {
                    1 => [0],
                    2 => [1, 1],
                    3 => [1, 2, 2],
                    _ => reader.ReadBits(1) == 0 ? [2, 2, 2, 2] : [1, 2, 3, 3],
                };

                if (count == 1)
                    return new PrefixCode { _singleSymbol = symbols[0] };

                for (var i = 0; i < count; i++)
                {
                    if (lengths[symbols[i]] != 0)
                        throw new InvalidDataException("The Brotli stream is invalid");

                    lengths[symbols[i]] = codeLengths[i];
                }

                return Create(lengths);
            }

            // A complex prefix code: the code length code lengths, read with a fixed code, then the code lengths
            Span<byte> codeLengthCodeLengths = stackalloc byte[18];
            var space = 32;
            for (var i = skip; i < 18 && space > 0; i++)
            {
                var length = ReadCodeLengthCodeLength(ref reader);
                codeLengthCodeLengths[CodeLengthOrder[i]] = (byte)length;
                if (length != 0)
                {
                    space -= 32 >> length;
                }
            }

            var codeLengthCode = Create(codeLengthCodeLengths.ToArray());
            var symbol = 0;
            var previousLength = 8;
            var repeat = 0;
            var repeatLength = 0;
            var symbolSpace = 32768;
            while (symbol < alphabetSize && symbolSpace > 0)
            {
                var code = codeLengthCode.ReadSymbol(ref reader);
                if (code < 16)
                {
                    repeat = 0;
                    lengths[symbol++] = (byte)code;
                    if (code != 0)
                    {
                        previousLength = code;
                        symbolSpace -= 32768 >> code;
                    }

                    continue;
                }

                var extraBits = code == 16 ? 2 : 3;
                var delta = reader.ReadBits(extraBits);
                var newLength = code == 16 ? previousLength : 0;
                if (repeatLength != newLength)
                {
                    repeat = 0;
                    repeatLength = newLength;
                }

                var oldRepeat = repeat;
                if (repeat > 0)
                {
                    repeat = (repeat - 2) << extraBits;
                }

                repeat += delta + 3;
                var count = repeat - oldRepeat;
                if (count > alphabetSize - symbol)
                    throw new InvalidDataException("The Brotli stream is invalid");

                lengths.AsSpan(symbol, count).Fill((byte)newLength);
                symbol += count;
                if (newLength != 0)
                {
                    symbolSpace -= count << (15 - newLength);
                }
            }

            return Create(lengths);
        }

        public int ReadSymbol(ref BitReader reader)
        {
            if (_singleSymbol >= 0)
                return _singleSymbol;

            var code = 0;
            for (var length = 1; length <= _maxLength; length++)
            {
                code = (code << 1) | reader.ReadBits(1);
                if (_symbols.TryGetValue((length, code), out var symbol))
                    return symbol;
            }

            throw new InvalidDataException("The Brotli stream is invalid");
        }

        /// <summary>Section 3.5: the code length code lengths, read with a fixed code (the lengths 0 to 5).</summary>
        private static int ReadCodeLengthCodeLength(ref BitReader reader)
        {
            // The codes, as read: 00 → 0, 1110 → 1, 110 → 2, 01 → 3, 10 → 4, 1111 → 5
            if (reader.ReadBits(1) == 0)
                return reader.ReadBits(1) == 0 ? 0 : 3;

            if (reader.ReadBits(1) == 0)
                return 4;

            if (reader.ReadBits(1) == 0)
                return 2;

            return reader.ReadBits(1) == 0 ? 1 : 5;
        }

        /// <summary>The canonical prefix code of code lengths (section 3.2); a code with a single symbol reads no bit.</summary>
        private static PrefixCode Create(byte[] lengths)
        {
            var result = new PrefixCode();
            var used = 0;
            var last = -1;
            for (var i = 0; i < lengths.Length; i++)
            {
                if (lengths[i] != 0)
                {
                    used++;
                    last = i;
                }
            }

            if (used == 0)
                throw new InvalidDataException("The Brotli stream is invalid");

            if (used == 1)
            {
                result._singleSymbol = last;
                return result;
            }

            var code = 0;
            for (var length = 1; length <= 15; length++)
            {
                for (var symbol = 0; symbol < lengths.Length; symbol++)
                {
                    if (lengths[symbol] == length)
                    {
                        result._symbols[(length, code)] = symbol;
                        result._maxLength = length;
                        code++;
                    }
                }

                code <<= 1;
            }

            return result;
        }
    }

    private ref struct BitReader(ReadOnlySpan<byte> data)
    {
        private readonly ReadOnlySpan<byte> _data = data;

        public long Position { get; set; }

        public int ReadBits(int count)
        {
            var value = 0;
            for (var i = 0; i < count; i++)
            {
                var byteIndex = Position >> 3;
                if (byteIndex >= _data.Length)
                    throw new InvalidDataException("The Brotli stream is truncated");

                value |= ((_data[(int)byteIndex] >> (int)(Position & 7)) & 1) << i;
                Position++;
            }

            return value;
        }

        public void AlignToByte() => Position = (Position + 7) & ~7L;
    }

    private sealed class BitWriter(int capacity)
    {
        private readonly ArrayBufferWriter<byte> _buffer = new(capacity);
        private ulong _accumulator;
        private int _bitCount;

        public ReadOnlySpan<byte> WrittenSpan => _buffer.WrittenSpan;

        public void Write(int value, int count)
        {
            _accumulator |= (ulong)(uint)value << _bitCount;
            _bitCount += count;
            while (_bitCount >= 8)
            {
                WriteByte((byte)_accumulator);
                _accumulator >>= 8;
                _bitCount -= 8;
            }
        }

        public void AlignToByte()
        {
            if (_bitCount > 0)
            {
                WriteByte((byte)_accumulator);
                _accumulator = 0;
                _bitCount = 0;
            }
        }

        public void WriteBytes(ReadOnlySpan<byte> bytes) => _buffer.Write(bytes);

        /// <summary>
        /// Writes the bits of <paramref name="data"/> from the bit <paramref name="bitOffset"/>, when the position in the
        /// current byte is <paramref name="bitOffset"/>.
        /// </summary>
        public void WriteTail(ReadOnlySpan<byte> data, int bitOffset)
        {
            Debug.Assert(_bitCount == bitOffset);
            WriteByte((byte)(_accumulator | (uint)(data[0] & (0xFF << bitOffset))));
            _accumulator = 0;
            _bitCount = 0;
            _buffer.Write(data[1..]);
        }

        private void WriteByte(byte value)
        {
            _buffer.GetSpan(1)[0] = value;
            _buffer.Advance(1);
        }
    }
}
