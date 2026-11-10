using System.Buffers.Binary;

namespace Meziantou.Framework.MediaTags.Formats.Mp4;

/// <summary>
/// Reads the text records QuickTime stores in the <c>udta</c> atom of a movie, such as <c>©nam</c> for the title.
/// </summary>
/// <remarks>
/// A record holds one or more texts, each a 16-bit length, a 16-bit language code and the text. The text is UTF-8 for an
/// ISO language code (0x400 and above), and Mac OS Roman for a Macintosh language code. See the QuickTime File Format
/// Specification, "User Data Text Strings and Language Codes".
/// </remarks>
internal static class QuickTimeUserData
{
    private const string Title = "\u00A9nam";
    private const string Artist = "\u00A9ART";
    private const string Album = "\u00A9alb";
    private const string Year = "\u00A9day";
    private const string Comment = "\u00A9cmt";
    private const string Genre = "\u00A9gen";
    private const string Composer = "\u00A9wrt";
    private const string Copyright = "\u00A9cpy";
    private const string Description = "\u00A9des";

    /// <summary>The characters of the bytes 0x80 to 0xFF in Mac OS Roman.</summary>
    private static readonly ushort[] MacRomanHighCharacters =
    [
        0x00C4, 0x00C5, 0x00C7, 0x00C9, 0x00D1, 0x00D6, 0x00DC, 0x00E1, 0x00E0, 0x00E2, 0x00E4, 0x00E3, 0x00E5, 0x00E7, 0x00E9, 0x00E8,
        0x00EA, 0x00EB, 0x00ED, 0x00EC, 0x00EE, 0x00EF, 0x00F1, 0x00F3, 0x00F2, 0x00F4, 0x00F6, 0x00F5, 0x00FA, 0x00F9, 0x00FB, 0x00FC,
        0x2020, 0x00B0, 0x00A2, 0x00A3, 0x00A7, 0x2022, 0x00B6, 0x00DF, 0x00AE, 0x00A9, 0x2122, 0x00B4, 0x00A8, 0x2260, 0x00C6, 0x00D8,
        0x221E, 0x00B1, 0x2264, 0x2265, 0x00A5, 0x00B5, 0x2202, 0x2211, 0x220F, 0x03C0, 0x222B, 0x00AA, 0x00BA, 0x03A9, 0x00E6, 0x00F8,
        0x00BF, 0x00A1, 0x00AC, 0x221A, 0x0192, 0x2248, 0x2206, 0x00AB, 0x00BB, 0x2026, 0x00A0, 0x00C0, 0x00C3, 0x00D5, 0x0152, 0x0153,
        0x2013, 0x2014, 0x201C, 0x201D, 0x2018, 0x2019, 0x00F7, 0x25CA, 0x00FF, 0x0178, 0x2044, 0x20AC, 0x2039, 0x203A, 0xFB01, 0xFB02,
        0x2021, 0x00B7, 0x201A, 0x201E, 0x2030, 0x00C2, 0x00CA, 0x00C1, 0x00CB, 0x00C8, 0x00CD, 0x00CE, 0x00CF, 0x00CC, 0x00D3, 0x00D4,
        0xF8FF, 0x00D2, 0x00DA, 0x00DB, 0x00D9, 0x0131, 0x02C6, 0x02DC, 0x00AF, 0x02D8, 0x02D9, 0x02DA, 0x00B8, 0x02DD, 0x02DB, 0x02C7,
    ];

    /// <summary>Gets whether an atom of <c>udta</c> is a text record the reader reads.</summary>
    public static bool IsTextRecord(string type) => type is Title or Artist or Album or Year or Comment or Genre or Composer or Copyright or Description;

    /// <summary>Gets whether the reader reads a record of <c>udta</c> into a property. The writer moves those to the metadata items.</summary>
    public static bool IsReadIntoProperty(Mp4Atom record)
    {
        if (!IsTextRecord(record.Type) || record.Data is null || ReadText(record.Data) is not { Length: > 0 } text)
            return false;

        return record.Type != Year || (text.Length >= 4 && int.TryParse(text.AsSpan(0, 4), NumberStyles.None, CultureInfo.InvariantCulture, out _));
    }

    /// <summary>Applies the text records of a <c>udta</c> atom to the fields that are not set yet.</summary>
    public static void Apply(Mp4Atom userData, MediaTagInfo tags)
    {
        foreach (var record in userData.Children)
        {
            if (!IsTextRecord(record.Type) || record.Data is null || ReadText(record.Data) is not { Length: > 0 } text)
                continue;

            switch (record.Type)
            {
                case Title: tags.Title ??= text; break;
                case Artist: tags.Artist ??= text; break;
                case Album: tags.Album ??= text; break;
                case Comment: tags.Comment ??= text; break;
                case Genre: tags.Genre ??= text; break;
                case Composer: tags.Composer ??= text; break;
                case Copyright: tags.Copyright ??= text; break;
                case Description: tags.Description ??= text; break;
                case Year:
                    if (text.Length >= 4 && int.TryParse(text.AsSpan(0, 4), NumberStyles.None, CultureInfo.InvariantCulture, out var year))
                        tags.Year ??= year;
                    break;
            }
        }
    }

    /// <summary>Reads the first text of a record.</summary>
    private static string? ReadText(ReadOnlySpan<byte> data)
    {
        if (data.Length < 4)
            return null;

        var length = BinaryPrimitives.ReadUInt16BigEndian(data);
        var language = BinaryPrimitives.ReadUInt16BigEndian(data[2..]);
        if (length > data.Length - 4)
            return null;

        var text = data.Slice(4, length);
        return (language >= 0x400 ? Encoding.UTF8.GetString(text) : DecodeMacRoman(text)).TrimEnd('\0');
    }

    private static string DecodeMacRoman(ReadOnlySpan<byte> data)
    {
        return string.Create(data.Length, data.ToArray(), (chars, bytes) =>
        {
            for (var i = 0; i < bytes.Length; i++)
            {
                chars[i] = bytes[i] < 0x80 ? (char)bytes[i] : (char)MacRomanHighCharacters[bytes[i] - 0x80];
            }
        });
    }
}
