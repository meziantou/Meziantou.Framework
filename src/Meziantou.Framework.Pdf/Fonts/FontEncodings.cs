namespace Meziantou.Framework.Pdf.Fonts;

/// <summary>
/// The glyph names of the codes of the standard encodings of simple fonts (ISO 32000-2, Annex D):
/// <c>StandardEncoding</c> (the codes of the AFM files of the Latin standard fonts), <c>WinAnsiEncoding</c> and
/// <c>MacRomanEncoding</c>, and the built-in encodings of Symbol and ZapfDingbats (the codes of their AFM files).
/// </summary>
internal static class FontEncodings
{
    private static readonly Lazy<string?[]> StandardEncodingNames = new(() => FromAfm("Times-Roman"));
    private static readonly Lazy<string?[]> SymbolEncodingNames = new(() => FromAfm("Symbol"));
    private static readonly Lazy<string?[]> ZapfDingbatsEncodingNames = new(() => FromAfm("ZapfDingbats"));
    private static readonly Lazy<string?[]> WinAnsiEncodingNames = new(CreateWinAnsi);
    private static readonly Lazy<string?[]> MacRomanEncodingNames = new(CreateMacRoman);

    public static string?[] Standard => StandardEncodingNames.Value;

    public static string?[] WinAnsi => WinAnsiEncodingNames.Value;

    public static string?[] MacRoman => MacRomanEncodingNames.Value;

    public static string?[] Symbol => SymbolEncodingNames.Value;

    public static string?[] ZapfDingbats => ZapfDingbatsEncodingNames.Value;

    /// <summary>The names of the codes 32 to 126 shared by WinAnsiEncoding and MacRomanEncoding (Annex D, table D.2).</summary>
    private static readonly string[] Ascii =
    [
        "space", "exclam", "quotedbl", "numbersign", "dollar", "percent", "ampersand", "quotesingle", "parenleft", "parenright", "asterisk", "plus", "comma", "hyphen", "period", "slash",
        "zero", "one", "two", "three", "four", "five", "six", "seven", "eight", "nine", "colon", "semicolon", "less", "equal", "greater", "question",
        "at", "A", "B", "C", "D", "E", "F", "G", "H", "I", "J", "K", "L", "M", "N", "O",
        "P", "Q", "R", "S", "T", "U", "V", "W", "X", "Y", "Z", "bracketleft", "backslash", "bracketright", "asciicircum", "underscore",
        "grave", "a", "b", "c", "d", "e", "f", "g", "h", "i", "j", "k", "l", "m", "n", "o",
        "p", "q", "r", "s", "t", "u", "v", "w", "x", "y", "z", "braceleft", "bar", "braceright", "asciitilde",
    ];

    /// <summary>The names of the codes 128 to 255 of WinAnsiEncoding; <see langword="null"/> for the codes without glyph.</summary>
    private static readonly string?[] WinAnsiHigh =
    [
        "Euro", null, "quotesinglbase", "florin", "quotedblbase", "ellipsis", "dagger", "daggerdbl", "circumflex", "perthousand", "Scaron", "guilsinglleft", "OE", null, "Zcaron", null,
        null, "quoteleft", "quoteright", "quotedblleft", "quotedblright", "bullet", "endash", "emdash", "tilde", "trademark", "scaron", "guilsinglright", "oe", null, "zcaron", "Ydieresis",
        "space", "exclamdown", "cent", "sterling", "currency", "yen", "brokenbar", "section", "dieresis", "copyright", "ordfeminine", "guillemotleft", "logicalnot", "hyphen", "registered", "macron",
        "degree", "plusminus", "twosuperior", "threesuperior", "acute", "mu", "paragraph", "periodcentered", "cedilla", "onesuperior", "ordmasculine", "guillemotright", "onequarter", "onehalf", "threequarters", "questiondown",
        "Agrave", "Aacute", "Acircumflex", "Atilde", "Adieresis", "Aring", "AE", "Ccedilla", "Egrave", "Eacute", "Ecircumflex", "Edieresis", "Igrave", "Iacute", "Icircumflex", "Idieresis",
        "Eth", "Ntilde", "Ograve", "Oacute", "Ocircumflex", "Otilde", "Odieresis", "multiply", "Oslash", "Ugrave", "Uacute", "Ucircumflex", "Udieresis", "Yacute", "Thorn", "germandbls",
        "agrave", "aacute", "acircumflex", "atilde", "adieresis", "aring", "ae", "ccedilla", "egrave", "eacute", "ecircumflex", "edieresis", "igrave", "iacute", "icircumflex", "idieresis",
        "eth", "ntilde", "ograve", "oacute", "ocircumflex", "otilde", "odieresis", "divide", "oslash", "ugrave", "uacute", "ucircumflex", "udieresis", "yacute", "thorn", "ydieresis",
    ];

    /// <summary>The names of the codes 128 to 255 of MacRomanEncoding; <see langword="null"/> for the codes without glyph.</summary>
    private static readonly string?[] MacRomanHigh =
    [
        "Adieresis", "Aring", "Ccedilla", "Eacute", "Ntilde", "Odieresis", "Udieresis", "aacute", "agrave", "acircumflex", "adieresis", "atilde", "aring", "ccedilla", "eacute", "egrave",
        "ecircumflex", "edieresis", "iacute", "igrave", "icircumflex", "idieresis", "ntilde", "oacute", "ograve", "ocircumflex", "odieresis", "otilde", "uacute", "ugrave", "ucircumflex", "udieresis",
        "dagger", "degree", "cent", "sterling", "section", "bullet", "paragraph", "germandbls", "registered", "copyright", "trademark", "acute", "dieresis", null, "AE", "Oslash",
        null, "plusminus", null, null, "yen", "mu", null, null, null, null, null, "ordfeminine", "ordmasculine", null, "ae", "oslash",
        "questiondown", "exclamdown", "logicalnot", null, "florin", null, null, "guillemotleft", "guillemotright", "ellipsis", "space", "Agrave", "Atilde", "Otilde", "OE", "oe",
        "endash", "emdash", "quotedblleft", "quotedblright", "quoteleft", "quoteright", "divide", null, "ydieresis", "Ydieresis", "fraction", "currency", "guilsinglleft", "guilsinglright", "fi", "fl",
        "daggerdbl", "periodcentered", "quotesinglbase", "quotedblbase", "perthousand", "Acircumflex", "Ecircumflex", "Aacute", "Edieresis", "Egrave", "Iacute", "Icircumflex", "Idieresis", "Igrave", "Oacute", "Ocircumflex",
        null, "Ograve", "Uacute", "Ucircumflex", "Ugrave", "dotlessi", "circumflex", "tilde", "macron", "breve", "dotaccent", "ring", "cedilla", "hungarumlaut", "ogonek", "caron",
    ];

    public static string?[]? GetByName(string name)
    {
        return name switch
        {
            "StandardEncoding" => Standard,
            "WinAnsiEncoding" => WinAnsi,
            "MacRomanEncoding" => MacRoman,
            _ => null,
        };
    }

    private static string?[] CreateWinAnsi()
    {
        var names = new string?[256];
        Ascii.CopyTo(names, 32);
        WinAnsiHigh.CopyTo(names, 128);
        return names;
    }

    private static string?[] CreateMacRoman()
    {
        var names = new string?[256];
        Ascii.CopyTo(names, 32);
        MacRomanHigh.CopyTo(names, 128);
        return names;
    }

    private static string?[] FromAfm(string fontName)
    {
        var names = new string?[256];
        foreach (var character in StandardFontMetrics.Get(fontName).CharacterMetrics)
        {
            if (character is { Code: >= 0 and < 256, Name: { } name })
            {
                names[character.Code] = name;
            }
        }

        return names;
    }
}
