namespace Meziantou.Framework.Fonts;

/// <summary>Reads the strings of the <c>name</c> table.</summary>
/// <remarks>
/// OpenType specification, "name - Naming Table". A name is taken from the Windows platform in US English when the
/// font has it, then from the Windows platform in any language, then from the Unicode platform, then from the
/// Macintosh platform in MacRoman. A record whose string is outside the table is ignored, as if the font did not have
/// it: a malformed name does not make the font unusable, and another record can still give the name.
/// </remarks>
internal static class NameTable
{
    public const int FamilyName = 1;
    public const int SubfamilyName = 2;
    public const int FullName = 4;
    public const int PostScriptName = 6;
    public const int TypographicFamilyName = 16;
    public const int TypographicSubfamilyName = 17;

    private const int RecordLength = 12;

    /// <summary>Finds a name.</summary>
    /// <returns>The name, or <see langword="null"/> when the table does not have it or it is empty.</returns>
    public static string? Find(ReadOnlySpan<byte> table, int nameId)
    {
        if (table.IsEmpty)
            return null;

        var count = FontBinary.UInt16(table, 2);
        var storageOffset = FontBinary.UInt16(table, 4);
        var records = FontBinary.Slice(table, 6, (long)count * RecordLength);
        var bestPriority = 0;
        ReadOnlySpan<byte> best = default;
        var bestPlatform = 0;
        for (var i = 0; i < count; i++)
        {
            var record = records.Slice(i * RecordLength, RecordLength);
            if (FontBinary.UInt16(record, 6) != nameId)
                continue;

            var platform = FontBinary.UInt16(record, 0);
            var encoding = FontBinary.UInt16(record, 2);
            var language = FontBinary.UInt16(record, 4);
            var priority = platform switch
            {
                3 when encoding is 0 or 1 or 10 => language == 0x0409 ? 4 : 3,
                0 => 2,
                1 when encoding == 0 => 1,
                _ => 0,
            };
            if (priority <= bestPriority || !TryGetString(table, storageOffset, record, out var data))
                continue;

            best = data;
            bestPriority = priority;
            bestPlatform = platform;
        }

        if (bestPriority == 0 || best.IsEmpty)
            return null;

        var value = bestPlatform == 1 ? DecodeMacintoshRoman(best) : Encoding.BigEndianUnicode.GetString(best);
        return value.Length == 0 ? null : value;
    }

    /// <summary>
    /// Adds the values a name has in every language: those of the Unicode platform, of the Windows platform (Unicode and
    /// symbol encodings) and of the Macintosh platform in the Roman encoding, each value once.
    /// </summary>
    public static void FindAll(ReadOnlySpan<byte> table, int nameId, List<string> names)
    {
        if (table.IsEmpty)
            return;

        var count = FontBinary.UInt16(table, 2);
        var storageOffset = FontBinary.UInt16(table, 4);
        var records = FontBinary.Slice(table, 6, (long)count * RecordLength);
        for (var i = 0; i < count; i++)
        {
            var record = records.Slice(i * RecordLength, RecordLength);
            if (FontBinary.UInt16(record, 6) != nameId)
                continue;

            var platform = FontBinary.UInt16(record, 0);
            var encoding = FontBinary.UInt16(record, 2);
            if (!(platform == 0 || (platform == 3 && encoding is 0 or 1 or 10) || (platform == 1 && encoding == 0)))
                continue;

            if (!TryGetString(table, storageOffset, record, out var data))
                continue;

            var value = platform == 1 ? DecodeMacintoshRoman(data) : Encoding.BigEndianUnicode.GetString(data);
            if (value.Length != 0 && !names.Contains(value, StringComparer.Ordinal))
            {
                names.Add(value);
            }
        }
    }

    /// <summary>Gets the bytes of the string of a record; <see langword="false"/> when they are not all inside the table.</summary>
    private static bool TryGetString(ReadOnlySpan<byte> table, int storageOffset, ReadOnlySpan<byte> record, out ReadOnlySpan<byte> data)
    {
        var length = FontBinary.UInt16(record, 8);
        var offset = (long)storageOffset + FontBinary.UInt16(record, 10);
        if (offset + length > table.Length)
        {
            data = default;
            return false;
        }

        data = table.Slice((int)offset, length);
        return true;
    }

    // The upper half of the Mac OS Roman character set (the lower half is ASCII), in the order of its bytes from 0x80:
    // the mapping the Unicode Consortium publishes as MAC/ROMAN.TXT. The library carries it so that reading a font
    // does not depend on the code pages of the platform.
    private const string MacintoshRomanUpperHalf =
        "\u00C4\u00C5\u00C7\u00C9\u00D1\u00D6\u00DC\u00E1\u00E0\u00E2\u00E4\u00E3\u00E5\u00E7\u00E9\u00E8" +
        "\u00EA\u00EB\u00ED\u00EC\u00EE\u00EF\u00F1\u00F3\u00F2\u00F4\u00F6\u00F5\u00FA\u00F9\u00FB\u00FC" +
        "\u2020\u00B0\u00A2\u00A3\u00A7\u2022\u00B6\u00DF\u00AE\u00A9\u2122\u00B4\u00A8\u2260\u00C6\u00D8" +
        "\u221E\u00B1\u2264\u2265\u00A5\u00B5\u2202\u2211\u220F\u03C0\u222B\u00AA\u00BA\u03A9\u00E6\u00F8" +
        "\u00BF\u00A1\u00AC\u221A\u0192\u2248\u2206\u00AB\u00BB\u2026\u00A0\u00C0\u00C3\u00D5\u0152\u0153" +
        "\u2013\u2014\u201C\u201D\u2018\u2019\u00F7\u25CA\u00FF\u0178\u2044\u20AC\u2039\u203A\uFB01\uFB02" +
        "\u2021\u00B7\u201A\u201E\u2030\u00C2\u00CA\u00C1\u00CB\u00C8\u00CD\u00CE\u00CF\u00CC\u00D3\u00D4" +
        "\uF8FF\u00D2\u00DA\u00DB\u00D9\u0131\u02C6\u02DC\u00AF\u02D8\u02D9\u02DA\u00B8\u02DD\u02DB\u02C7";

    /// <summary>Decodes a name of the Macintosh platform in the Roman script.</summary>
    internal static string DecodeMacintoshRoman(ReadOnlySpan<byte> data)
    {
        return string.Create(data.Length, data, static (characters, bytes) =>
        {
            for (var i = 0; i < bytes.Length; i++)
            {
                characters[i] = bytes[i] < 0x80 ? (char)bytes[i] : MacintoshRomanUpperHalf[bytes[i] - 0x80];
            }
        });
    }
}
