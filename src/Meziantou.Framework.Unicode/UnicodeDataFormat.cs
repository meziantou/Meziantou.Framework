namespace Meziantou.Framework;

/// <summary>Describes the layout of the <c>UnicodeData.bin</c> embedded resource.</summary>
/// <remarks>
/// The resource is deliberately <em>not</em> compressed. The payload is small enough that the
/// outer layers that already compress it — the NuGet package, and the Brotli pass a Blazor
/// application applies when publishing — do a better job than an inner GZip stream, which they
/// cannot recompress. Leaving it raw also removes decompression from application startup.
/// <para>
/// The Unicode Character Database is range-structured: most code points belong to a block whose
/// characters share every property but the name. The layout stores those ranges instead of
/// expanding them, so the reader can binary search the table in place rather than materializing
/// one entry per code point.
/// </para>
/// <para>
/// This file is compiled into the generator as well, so the writer and the reader cannot disagree
/// about the layout. Changing anything here requires bumping <see cref="LayoutVersion"/> and
/// regenerating the resource.
/// </para>
/// <para>
/// Every integer is a LEB128 variable-length unsigned integer unless stated otherwise; values that
/// can be negative are zig-zag encoded first. The layout is:
/// </para>
/// <code>
/// magic                "MUCD" (4 bytes)
/// version              1 byte
///
/// propertyCount
/// property[propertyCount]                     the distinct property tuples, in first-use order
///     category                 1 byte
///     bidiCategory             1 byte
///     canonicalCombiningClass  1 byte
///     decimalDigitValue        1 byte (sbyte)
///     digitValue               1 byte (sbyte)
///     emojiProperties          1 byte
///     flags                    1 byte         see the *Flag constants
///     simpleUppercaseDelta     zig-zag        added to the code point when HasUppercaseFlag
///     simpleLowercaseDelta     zig-zag        added to the code point when HasLowercaseFlag
///     simpleTitlecaseDelta     zig-zag        added to the code point when HasTitlecaseFlag
///
/// runCount
/// run[runCount]                               sorted, non-overlapping, non-adjacent
///     startDelta                              from the end (exclusive) of the previous run
///     length
///     propertyIndex
///
/// namedCount                                  code points with a name of their own
/// namedCodePointDelta[namedCount]             from the previous named code point
/// nameBucketOffsetDelta[ceil(namedCount / NameBucketSize)]
/// nameTextLength
/// nameText[nameTextLength]                    front-coded, restarting at every bucket
///     sharedPrefixLength       1 byte         bytes kept from the previous name
///     suffixLength             1 byte
///     suffix                   suffixLength bytes
///
/// rangeNameCount                              names shared by a whole range, "&lt;CJK Ideograph&gt;"
/// rangeName[rangeNameCount]
///     length                   1 byte
///     text                     length bytes
/// rangeNameRunCount
/// rangeNameRun[rangeNameRunCount]
///     startDelta                              from the end (exclusive) of the previous run
///     length
///     rangeNameIndex
///
/// decompositionTagCount                       "&lt;compat&gt;", "&lt;font&gt;", ...
/// decompositionTag[decompositionTagCount]
///     length                   1 byte
///     text                     length bytes
/// decompositionCount
/// decomposition[decompositionCount]           ordered by code point
///     codePointDelta                          from the previous decomposition's code point
///     tagIndex                 1 byte         0 when untagged, otherwise 1-based
///     mappingLength            1 byte
///     mapping[mappingLength]                  the mapped code points
///
/// stringColumn * 3                            numericValue, unicode1Name, isoComment
///     stringCount
///     string[stringCount]                     front-coded, no buckets
///         sharedPrefixLength   1 byte
///         suffixLength         1 byte
///         suffix               suffixLength bytes
///     entryCount
///     entry[entryCount]                       ordered by code point
///         codePointDelta                      from the previous entry's code point
///         stringIndex
/// </code>
/// </remarks>
internal static class UnicodeDataFormat
{
    /// <summary>The bytes every valid resource starts with.</summary>
    public static ReadOnlySpan<byte> Magic => "MUCD"u8;

    /// <summary>The layout version. Bump it whenever the layout changes.</summary>
    public const byte LayoutVersion = 2;

    /// <summary>The number of bytes <see cref="Magic"/> and <see cref="LayoutVersion"/> occupy.</summary>
    public const int HeaderLength = 5;

    /// <summary>
    /// The number of names between two front-coding restarts. A name is decoded by replaying its
    /// bucket from the start, so this bounds the work of a single lookup; smaller buckets cost
    /// size, larger ones cost time.
    /// </summary>
    public const int NameBucketSize = 64;

    /// <summary>The character is mirrored in bidirectional text.</summary>
    public const byte MirroredFlag = 1 << 0;

    /// <summary>The character has a simple uppercase mapping.</summary>
    public const byte HasUppercaseFlag = 1 << 1;

    /// <summary>The character has a simple lowercase mapping.</summary>
    public const byte HasLowercaseFlag = 1 << 2;

    /// <summary>The character has a simple titlecase mapping.</summary>
    public const byte HasTitlecaseFlag = 1 << 3;

    /// <summary>The number of bytes of a property tuple that are stored as raw bytes.</summary>
    public const int PropertyFixedLength = 7;

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
                throw new InvalidDataException("Invalid variable-length integer in the Unicode data resource.");
        }
    }

    /// <summary>Reads a zig-zag encoded signed integer and advances <paramref name="position"/>.</summary>
    /// <param name="data">The buffer to read from.</param>
    /// <param name="position">The offset to read at, advanced past the value.</param>
    /// <returns>The decoded value.</returns>
    public static int ReadZigZagVarInt(ReadOnlySpan<byte> data, ref int position)
    {
        var value = (uint)ReadVarInt(data, ref position);
        return (int)(value >> 1) ^ -(int)(value & 1);
    }
}
