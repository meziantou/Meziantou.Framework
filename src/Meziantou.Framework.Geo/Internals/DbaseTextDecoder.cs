using System.Globalization;
using System.Text;
using System.Text.Unicode;

namespace Meziantou.Framework.Geo.Internals;

/// <summary>Decodes the text of a dBASE file, in the encoding given by the options, the <c>.cpg</c> file or the language driver ID.</summary>
internal sealed class DbaseTextDecoder
{
    // The characters of Windows-1252 from 0x80 to 0x9F; the other bytes are the same as ISO-8859-1. The five undefined
    // bytes map to the C1 control of the same value, as Encoding.GetEncoding(1252) does.
    private const string Windows1252HighCharacters = "\u20AC\u0081\u201A\u0192\u201E\u2026\u2020\u2021\u02C6\u2030\u0160\u2039\u0152\u008D\u017D\u008F\u0090\u2018\u2019\u201C\u201D\u2022\u2013\u2014\u02DC\u2122\u0161\u203A\u0153\u009D\u017E\u0178";

    private readonly Encoding? _encoding;
    private readonly bool _windows1252;

    private DbaseTextDecoder(Encoding? encoding, bool windows1252)
    {
        _encoding = encoding;
        _windows1252 = windows1252;
    }

    /// <summary>Gets the decoder used when nothing gives the encoding: UTF-8 when the bytes are valid UTF-8, else ISO-8859-1.</summary>
    public static DbaseTextDecoder Default { get; } = new(encoding: null, windows1252: false);

    private static DbaseTextDecoder Windows1252 { get; } = new(encoding: null, windows1252: true);

    /// <summary>Gets the decoder for a file: the encoding of the options, else the one of the <c>.cpg</c> file, else the one of the language driver ID, else <see cref="Default"/>.</summary>
    public static DbaseTextDecoder Create(Encoding? encoding, string? codePage, byte languageDriverId)
    {
        if (encoding is not null)
            return new DbaseTextDecoder(encoding, windows1252: false);

        if (!string.IsNullOrWhiteSpace(codePage))
            return FromCodePageName(codePage) ?? Default;

        return GetCodePage(languageDriverId) is { } number ? FromCodePage(number) ?? Default : Default;
    }

    public string Decode(ReadOnlySpan<byte> bytes)
    {
        if (_encoding is not null)
            return _encoding.GetString(bytes);

        if (_windows1252)
            return DecodeWindows1252(bytes);

        return Utf8.IsValid(bytes) ? Encoding.UTF8.GetString(bytes) : Encoding.Latin1.GetString(bytes);
    }

    // The content of a .cpg file, as written by ArcGIS, QGIS and GDAL: "UTF-8", "UTF8", "65001", "ISO-8859-1", "88591", "1252", "CP1252", "ANSI 1252", "OEM 850", ...
    private static DbaseTextDecoder? FromCodePageName(string codePage)
    {
        var name = codePage.Trim().ToUpperInvariant();
        switch (name)
        {
            case "UTF-8" or "UTF8" or "65001":
                return new DbaseTextDecoder(Encoding.UTF8, windows1252: false);
            case "ISO-8859-1" or "ISO8859-1" or "ISO_8859-1" or "ISO88591" or "88591" or "8859-1" or "28591" or "LATIN1":
                return new DbaseTextDecoder(Encoding.Latin1, windows1252: false);
        }

        foreach (var prefix in (ReadOnlySpan<string>)["ANSI ", "OEM ", "WINDOWS-", "CP", "IBM"])
        {
            if (name.StartsWith(prefix, StringComparison.Ordinal))
            {
                name = name[prefix.Length..].Trim();
                break;
            }
        }

        if (int.TryParse(name, NumberStyles.None, CultureInfo.InvariantCulture, out var number))
            return FromCodePage(number);

        try
        {
            return new DbaseTextDecoder(Encoding.GetEncoding(codePage.Trim()), windows1252: false);
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    // Windows-1252, UTF-8 and ISO-8859-1 are always available; the other code pages only when the application registered
    // a provider, such as CodePagesEncodingProvider
    private static DbaseTextDecoder? FromCodePage(int codePage)
    {
        switch (codePage)
        {
            case 1252:
                return Windows1252;
            case 65001:
                return new DbaseTextDecoder(Encoding.UTF8, windows1252: false);
            case 28591:
                return new DbaseTextDecoder(Encoding.Latin1, windows1252: false);
        }

        try
        {
            return new DbaseTextDecoder(Encoding.GetEncoding(codePage), windows1252: false);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException)
        {
            return null;
        }
    }

    // The code pages of the language driver IDs, as in shapelib and GDAL. GDAL reads 0x57 as ISO-8859-1; Windows-1252 is a
    // superset of it for the printable characters, and is what the writers of 0x57 ("ANSI") use.
    private static int? GetCodePage(byte languageDriverId) => languageDriverId switch
    {
        0x01 or 0x09 or 0x0B or 0x0D or 0x0F or 0x11 or 0x15 or 0x18 or 0x19 or 0x1B => 437,
        0x02 or 0x0A or 0x0E or 0x10 or 0x12 or 0x14 or 0x16 or 0x1D or 0x25 or 0x37 => 850,
        0x03 or 0x57 or 0x58 or 0x59 => 1252,
        0x04 => 10000,
        0x08 or 0x17 or 0x66 => 865,
        0x13 or 0x7B => 932,
        0x1C or 0x6C => 863,
        0x1F or 0x22 or 0x23 or 0x40 or 0x64 or 0x87 => 852,
        0x24 => 860,
        0x26 or 0x65 => 866,
        0x4D or 0x7A => 936,
        0x4E or 0x79 => 949,
        0x4F or 0x78 => 950,
        0x50 or 0x7C => 874,
        0x67 => 861,
        0x6A or 0x86 => 737,
        0x6B or 0x88 => 857,
        0x7D => 1255,
        0x7E => 1256,
        0x96 => 10007,
        0x97 => 10029,
        0x98 => 10006,
        0xC8 => 1250,
        0xC9 => 1251,
        0xCA => 1254,
        0xCB => 1253,
        0xCC => 1257,
        _ => null,
    };

    private static string DecodeWindows1252(ReadOnlySpan<byte> bytes) => string.Create(bytes.Length, bytes, static (chars, source) =>
    {
        for (var i = 0; i < source.Length; i++)
        {
            var b = source[i];
            chars[i] = b is >= 0x80 and <= 0x9F ? Windows1252HighCharacters[b - 0x80] : (char)b;
        }
    });
}
