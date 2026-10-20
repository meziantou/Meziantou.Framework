namespace Meziantou.Framework;

/// <summary>Describes the layout of the tables of <c>UnicodeCharacterInfos.g.cs</c>, generated from <c>UnicodeData.txt</c>.</summary>
/// <remarks>
/// Every table is a <see cref="ReadOnlySpan{T}"/> of bytes over static data, so reading the database copies nothing,
/// allocates nothing and parses nothing when the program starts. Every table is also laid out for random access:
/// a lookup reads the few bytes it needs and the fields of a character are only decoded when they are asked for.
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
/// RangeNames                 names shared by a whole range, such as "&lt;CJK Ideograph&gt;", sorted
///     start                    3 bytes
///     end                      3 bytes        inclusive
///     name                     2 bytes        string index
///
/// NameBuckets                one record per NameBucketSize names of their own, sorted by code point
///     firstCodePoint           3 bytes
///     entryOffset              3 bytes        in NameEntries
///     textOffset               3 bytes        in NameText
/// NameEntries                one entry per name, front-coded, restarting at every bucket
///     codePointDelta           varint         from the previous entry of the bucket, 0 for the first one
///     sharedPrefixLength       1 byte         bytes kept from the previous name
///     suffixLength             1 byte         bytes appended from NameText
/// NameText                   the suffixes, in order
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

    public const int RangeNameRecordSize = 8;

    /// <summary>
    /// The number of names between two front-coding restarts. A name is decoded by replaying its bucket from the
    /// start, so this bounds the work of a single lookup; smaller buckets cost size, larger ones cost time.
    /// </summary>
    public const int NameBucketSize = 64;

    public const int NameBucketRecordSize = 9;

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
