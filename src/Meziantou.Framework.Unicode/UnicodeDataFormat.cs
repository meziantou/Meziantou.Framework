using System.Runtime.CompilerServices;

namespace Meziantou.Framework;

/// <summary>Describes the layout of the tables of <c>UnicodeCharacterInfos.g.cs</c>, generated from <c>UnicodeData.txt</c>.</summary>
/// <remarks>
/// The tables are embedded resources (<c>Data/UnicodeCharacterInfos.*</c>, see <c>UnicodeResources</c>). The
/// tables that every lookup reads are read in place, so finding the properties of a character copies nothing, allocates
/// nothing and parses nothing when the program starts. The names and the text properties, which most programs never
/// read, are compressed with Brotli in two other resources, decompressed the first time one of their tables is read.
/// Every table is laid out for random access: a lookup reads the few bytes it needs and the fields of a character are
/// only decoded when they are asked for.
/// <para>
/// The Unicode Character Database is range-structured: most code points belong to a block whose characters share
/// every property but the name. Those properties are stored once per distinct tuple, and a three-stage table maps a
/// code point to its tuple. Names, decomposition mappings and the sparse text properties live in side tables
/// sorted by code point.
/// </para>
/// <para>
/// This file is compiled into the generator as well, so the writer and the reader cannot disagree about the layout.
/// Integers are little-endian. A varint is a LEB128 variable-length unsigned integer. A string index points into
/// <c>Strings</c>. The tables are:
/// </para>
/// <code>
/// UnicodeCharacterInfos.bin, read in place:
///
/// PropertyIndexStage1..3     a three-stage table of 2-byte entries, see PropertyIndexShift1 and PropertyIndexShift2,
///                            giving the index of the property record of a code point, or 0 when the code point is
///                            not a character
///
/// Properties                 PropertyRecordSize-byte records; record 0 is all zeros, the values of a default
///                            UnicodeCharacterInfo
///     category                 1 byte
///     bidiCategory             1 byte
///     canonicalCombiningClass  1 byte
///     decimalDigitValue        1 byte (sbyte, -1 when none)
///     digitValue               1 byte (sbyte, -1 when none)
///     emojiProperties          1 byte
///     flags                    1 byte         see the *Flag constants
///     simpleUppercaseDelta     3 bytes        signed, added to the code point when HasUppercaseFlag
///     simpleLowercaseDelta     3 bytes        signed, added to the code point when HasLowercaseFlag
///     simpleTitlecaseDelta     3 bytes        signed, added to the code point when HasTitlecaseFlag
///
/// UnicodeCharacterInfos.Names.br, compressed:
///
/// NameRanges                 the names that follow from the code point, sorted
///     start                    3 bytes
///     end                      3 bytes        inclusive
///     text                     2 bytes        offset in NameRangeText, plus DerivedNameFlag when the name is the text, a
///                                             hyphen and the code point in hexadecimal (at least 4 digits), such as
///                                             "CJK COMPATIBILITY IDEOGRAPH-F900"; otherwise the name is the text, such
///                                             as "&lt;CJK Ideograph&gt;"
/// NameRangeText              for each text: its length (1 byte), then its ASCII characters
///
/// NameBuckets                one record per NameBucketSize names of NameEntries, sorted by code point
///     firstCodePoint           3 bytes
///     entryOffset              3 bytes        in NameEntries
/// NameEntries                one entry per name, front-coded by word, restarting at every bucket
///     codePointDelta           varint         from the previous entry of the bucket, 0 for the first one
///     wordCounts               1 byte         the number of words kept from the previous name (bits 4-7), then the
///                                             number of words that follow (bits 0-3)
///     words                                   each word that follows, the words of a name being separated by a space:
///                                               b &lt; LiteralNameWord     1 byte, the word b of NameWords
///                                               b = LiteralNameWord     then the length (1 byte) and the ASCII
///                                                                       characters of a word that is not in NameWords
///                                               b &gt; LiteralNameWord     2 bytes, the word ((b &amp; 0x7F) &lt;&lt; 8) | next byte
/// NameWordOffsets            2 bytes per word of NameWords, plus a last one for the end of the last word
/// NameWordText               the ASCII characters of the words of NameWords, the most frequent first
///
/// UnicodeCharacterInfos.Values.br, compressed:
///
/// Decompositions             sorted by code point
///     codePoint                3 bytes
///     offset                   3 bytes        in DecompositionMappings
/// DecompositionMappings
///     tag                      1 byte         0 when untagged, otherwise 1 + the string index of the tag
///     mappingLength            1 byte
///     mapping[mappingLength]   varint         the mapped code points
///
/// NumericValues, Unicode1Names, IsoComments
///     codePoint                3 bytes        sorted
///     value                    2 bytes        string index
///
/// StringOffsets              3 bytes per string, plus a last one for the end of the last string
/// Strings                    the UTF-8 text of the strings
/// </code>
/// </remarks>
internal static class UnicodeDataFormat
{
    /// <summary>The character is mirrored in bidirectional text.</summary>
    public const byte MirroredFlag = 1 << 0;

    /// <summary>The character has a simple uppercase mapping.</summary>
    public const byte HasUppercaseFlag = 1 << 1;

    /// <summary>The character has a simple lowercase mapping.</summary>
    public const byte HasLowercaseFlag = 1 << 2;

    /// <summary>The character has a simple titlecase mapping.</summary>
    public const byte HasTitlecaseFlag = 1 << 3;

    public const int PropertyRecordSize = 16;
    public const int CategoryField = 0;
    public const int BidiCategoryField = 1;
    public const int CanonicalCombiningClassField = 2;
    public const int DecimalDigitValueField = 3;
    public const int DigitValueField = 4;
    public const int EmojiPropertiesField = 5;
    public const int FlagsField = 6;
    public const int UppercaseDeltaField = 7;
    public const int LowercaseDeltaField = 10;
    public const int TitlecaseDeltaField = 13;

    public const int NameRangeRecordSize = 8;

    /// <summary>The flag of a record of <c>NameRanges</c> whose names end with the code point.</summary>
    public const int DerivedNameFlag = 0x8000;

    /// <summary>
    /// The number of names between two front-coding restarts. A name is decoded by replaying its bucket from the
    /// start, so this bounds the work of a single lookup; smaller buckets cost size, larger ones cost time.
    /// </summary>
    public const int NameBucketSize = 64;

    public const int NameBucketRecordSize = 6;

    /// <summary>The greatest number of words of a name of <c>NameEntries</c>, which a 4-bit count can hold.</summary>
    public const int MaxNameWordCount = 15;

    /// <summary>The first byte of a word of <c>NameEntries</c> that is not in <c>NameWords</c>.</summary>
    public const byte LiteralNameWord = 0x7F;

    public const int NameWordOffsetSize = 2;

    public const int DecompositionRecordSize = 6;

    public const int StringValueRecordSize = 5;

    public const int StringOffsetSize = 3;

    /// <summary>Reads a LEB128 variable-length unsigned integer and advances <paramref name="position"/>.</summary>
    /// <param name="data">The buffer to read from.</param>
    /// <param name="position">The offset to read at, advanced past the value.</param>
    /// <returns>The decoded value.</returns>
    /// <exception cref="InvalidDataException">The encoded value does not fit in an <see cref="int"/>.</exception>
    public static int ReadVarInt(ReadOnlySpan<byte> data, ref int position)
    {
        var result = 0;
        var shift = 0;
        while (true)
        {
            var b = data[position];
            position++;
            result |= (b & 0x7F) << shift;
            if ((b & 0x80) == 0)
                return result;

            shift += 7;
            if (shift >= 32)
                throw new InvalidDataException("Invalid variable-length integer in the Unicode data tables.");
        }
    }

    /// <summary>Reads an entry of <c>NameEntries</c> and replaces the words of the previous name with the words of this one.</summary>
    /// <param name="entries">The table <c>NameEntries</c>.</param>
    /// <param name="position">The offset of the entry, advanced past it.</param>
    /// <param name="words">
    /// The words of the previous name of the bucket, replaced by the words of the entry: the index of a word of
    /// <c>NameWords</c>, or the bitwise complement of the offset in <paramref name="entries"/> of the length of a literal word.
    /// Its length is <see cref="MaxNameWordCount"/>.
    /// </param>
    /// <param name="wordCount">The number of words of the previous name of the bucket (0 for the first one), then of this one.</param>
    /// <returns>The difference between the code point of the entry and the code point of the previous one.</returns>
    /// <exception cref="InvalidDataException">The entry is invalid.</exception>
    /// <remarks>
    /// The enumeration of the characters calls it for every name, usually once, faster than the tiered JIT compiler would
    /// promote it: it is compiled optimized right away.
    /// </remarks>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public static int ReadNameEntry(ReadOnlySpan<byte> entries, ref int position, Span<int> words, ref int wordCount)
    {
        var delta = ReadVarInt(entries, ref position);
        var counts = entries[position];
        position++;
        var keptCount = counts >> 4;
        var addedCount = counts & 0xF;
        if (keptCount > wordCount || keptCount + addedCount > words.Length)
            throw new InvalidDataException("Invalid name in the Unicode data tables.");

        wordCount = keptCount;
        for (var i = 0; i < addedCount; i++)
        {
            var value = entries[position];
            position++;
            if (value < LiteralNameWord)
            {
                words[wordCount] = value;
            }
            else if (value == LiteralNameWord)
            {
                words[wordCount] = ~position;
                position += 1 + entries[position];
            }
            else
            {
                words[wordCount] = ((value & 0x7F) << 8) | entries[position];
                position++;
            }

            wordCount++;
        }

        return delta;
    }

    /// <summary>Writes the ASCII characters of a name read by <see cref="ReadNameEntry"/>.</summary>
    /// <param name="words">The words of the name.</param>
    /// <param name="entries">The table <c>NameEntries</c>, which holds the literal words.</param>
    /// <param name="wordOffsets">The table <c>NameWordOffsets</c>.</param>
    /// <param name="wordText">The table <c>NameWordText</c>.</param>
    /// <param name="destination">The buffer that receives the name.</param>
    /// <returns>The length of the name.</returns>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public static int WriteName(ReadOnlySpan<int> words, ReadOnlySpan<byte> entries, ReadOnlySpan<byte> wordOffsets, ReadOnlySpan<byte> wordText, Span<byte> destination)
    {
        var length = 0;
        for (var i = 0; i < words.Length; i++)
        {
            if (i > 0)
            {
                destination[length] = (byte)' ';
                length++;
            }

            var word = words[i];
            ReadOnlySpan<byte> text;
            if (word >= 0)
            {
                var start = ReadUInt16(wordOffsets, word * NameWordOffsetSize);
                text = wordText[start..ReadUInt16(wordOffsets, (word + 1) * NameWordOffsetSize)];
            }
            else
            {
                text = entries.Slice(~word + 1, entries[~word]);
            }

            text.CopyTo(destination[length..]);
            length += text.Length;
        }

        return length;
    }

    /// <summary>Reads a little-endian unsigned 24-bit integer.</summary>
    /// <param name="data">The buffer to read from.</param>
    /// <param name="offset">The offset of the value.</param>
    /// <returns>The decoded value.</returns>
    public static int ReadUInt24(ReadOnlySpan<byte> data, int offset) => data[offset] | (data[offset + 1] << 8) | (data[offset + 2] << 16);

    /// <summary>Reads a little-endian signed 24-bit integer.</summary>
    /// <param name="data">The buffer to read from.</param>
    /// <param name="offset">The offset of the value.</param>
    /// <returns>The decoded value.</returns>
    public static int ReadInt24(ReadOnlySpan<byte> data, int offset) => (ReadUInt24(data, offset) << 8) >> 8;

    /// <summary>Reads a little-endian unsigned 16-bit integer.</summary>
    /// <param name="data">The buffer to read from.</param>
    /// <param name="offset">The offset of the value.</param>
    /// <returns>The decoded value.</returns>
    public static int ReadUInt16(ReadOnlySpan<byte> data, int offset) => data[offset] | (data[offset + 1] << 8);
}
