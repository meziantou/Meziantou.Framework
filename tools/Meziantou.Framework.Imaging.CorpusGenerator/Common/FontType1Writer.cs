using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text;

namespace Meziantou.Framework.Imaging.CorpusGenerator.Common;

/// <summary>
/// Writes a PostScript Type 1 font program (Adobe Type 1 Font Format) as a PFA or a PFB file: the clear text, the
/// private dictionary encrypted with <c>eexec</c>, and charstrings assembled from numbers and operator names and
/// encrypted with the charstring key.
/// </summary>
/// <remarks>
/// Self-contained: the fuzz project links this file for its seeds. <see cref="Sample"/> is the font of the tests, whose
/// outlines were checked against FreeType (<c>hb-vector --font-funcs=ft</c>).
/// </remarks>
[SuppressMessage("Design", "MA0182:Unused internal type", Justification = "The corpus generator does not use it: the font tests and the fuzz project use it.")]
internal sealed class FontType1Writer
{
    private static readonly Dictionary<string, int[]> Operators = new(StringComparer.Ordinal)
    {
        ["hstem"] = [1],
        ["vstem"] = [3],
        ["vmoveto"] = [4],
        ["rlineto"] = [5],
        ["hlineto"] = [6],
        ["vlineto"] = [7],
        ["rrcurveto"] = [8],
        ["closepath"] = [9],
        ["callsubr"] = [10],
        ["return"] = [11],
        ["hsbw"] = [13],
        ["endchar"] = [14],
        ["rmoveto"] = [21],
        ["hmoveto"] = [22],
        ["vhcurveto"] = [30],
        ["hvcurveto"] = [31],
        ["dotsection"] = [12, 0],
        ["vstem3"] = [12, 1],
        ["hstem3"] = [12, 2],
        ["seac"] = [12, 6],
        ["sbw"] = [12, 7],
        ["div"] = [12, 12],
        ["callothersubr"] = [12, 16],
        ["pop"] = [12, 17],
        ["setcurrentpoint"] = [12, 33],
    };

    public string FontName { get; set; } = "MeziantouType1-Regular";

    public string FamilyName { get; set; } = "Meziantou Type1";

    public string FullName { get; set; } = "Meziantou Type1 Regular";

    public string Weight { get; set; } = "Regular";

    public string Version { get; set; } = "001.002";

    public string Notice { get; set; } = "A font of the tests of Meziantou.Framework.Fonts";

    public double ItalicAngle { get; set; }

    public string FontMatrix { get; set; } = "[0.001 0 0 0.001 0 0]";

    public string FontBBox { get; set; } = "{-50 -250 1000 900}";

    /// <summary>Gets or sets PostScript code added at the end of the font dictionary, in the clear text.</summary>
    public string ExtraClearText { get; set; } = "";

    /// <summary>Gets or sets the glyph name of each code; <see langword="null"/> for <c>StandardEncoding</c>.</summary>
    public SortedDictionary<int, string>? Encoding { get; set; }

    /// <summary>Gets or sets the number of random bytes before each charstring; -1 for charstrings that are not encrypted.</summary>
    public int LenIV { get; set; } = 4;

    /// <summary>Gets the subroutines, as their plain charstrings.</summary>
    public List<byte[]> Subroutines { get; } = [];

    /// <summary>Gets the glyphs and their plain charstrings.</summary>
    public List<(string Name, byte[] Charstring)> CharStrings { get; } = [];

    /// <summary>Assembles a charstring: integers are numbers, strings are operators.</summary>
    public static byte[] Charstring(params object[] tokens)
    {
        var result = new List<byte>();
        foreach (var token in tokens)
        {
            switch (token)
            {
                case int value:
                    switch (value)
                    {
                        case >= -107 and <= 107:
                            result.Add((byte)(value + 139));
                            break;
                        case >= 108 and <= 1131:
                            result.Add((byte)(((value - 108) >> 8) + 247));
                            result.Add((byte)(value - 108));
                            break;
                        case >= -1131 and <= -108:
                            result.Add((byte)(((-value - 108) >> 8) + 251));
                            result.Add((byte)(-value - 108));
                            break;
                        default:
                            result.AddRange([255, (byte)(value >> 24), (byte)(value >> 16), (byte)(value >> 8), (byte)value]);
                            break;
                    }

                    break;

                case string name:
                    result.AddRange(Operators[name].Select(b => (byte)b));
                    break;

                default:
                    throw new ArgumentException("A token is an integer or an operator name", nameof(tokens));
            }
        }

        return [.. result];
    }

    /// <summary>
    /// The font of the tests: lines, curves, subroutines nested two deep, flex, hint replacement, <c>div</c>,
    /// <c>sbw</c>, an accented character made with <c>seac</c>, and glyph names with and without characters.
    /// </summary>
    public static FontType1Writer Sample(bool standardEncoding = false)
    {
        var font = new FontType1Writer();
        if (!standardEncoding)
        {
            font.Encoding = new()
            {
                [32] = "space", [46] = "period", [65] = "A", [69] = "E", [72] = "H", [79] = "O", [120] = "x", [193] = "Aacute", [194] = "acute",
            };
        }

        // The four subroutines of flex and hint replacement (8.4), hints, and a path in two levels of subroutines
        font.Subroutines.Add(Charstring(3, 0, "callothersubr", "pop", "pop", "setcurrentpoint", "return"));
        font.Subroutines.Add(Charstring(0, 1, "callothersubr", "return"));
        font.Subroutines.Add(Charstring(0, 2, "callothersubr", "return"));
        font.Subroutines.Add(Charstring("return"));
        font.Subroutines.Add(Charstring(100, 50, "hstem", "return"));
        font.Subroutines.Add(Charstring(100, 0, "rlineto", "return"));
        font.Subroutines.Add(Charstring(5, "callsubr", 0, 500, "rlineto", "return"));

        font.CharStrings.Add((".notdef", Charstring(0, 250, "hsbw", "endchar")));
        font.CharStrings.Add(("space", Charstring(0, 250, "hsbw", "endchar")));
        font.CharStrings.Add(("A", Charstring(20, 600, "hsbw", 0, 50, "hstem", 0, 0, "rmoveto", 280, 700, "rlineto", 280, -700, "rlineto", "closepath", "endchar")));
        font.CharStrings.Add(("acute", Charstring(50, 333, "hsbw", 0, 550, "rmoveto", 100, 150, "rlineto", 50, -30, "rlineto", "closepath", "endchar")));
        font.CharStrings.Add(("Aacute", Charstring(20, 600, "hsbw", 50, 150, 120, 65, 194, "seac")));
        font.CharStrings.Add(("Aring", Charstring(5, 610, "hsbw", 50, 150, 120, 65, 194, "seac")));
        font.CharStrings.Add(("O", Charstring(
            40, 700, "hsbw", 310, 0, "rmoveto",
            170, 140, 160, 190, "hvcurveto", 190, -140, 160, -170, "vhcurveto",
            -170, -140, -160, -190, "hvcurveto", -190, 140, -160, 170, "vhcurveto", "closepath", "endchar")));
        font.CharStrings.Add(("H", Charstring(
            30, 700, "hsbw", 0, 100, 270, 100, 540, 100, "vstem3", 0, 0, "rmoveto", 700, "vlineto", 100, "hlineto", -300, "vlineto", 440, "hlineto", 300, "vlineto",
            100, "hlineto", -700, "vlineto", -100, "hlineto", 300, "vlineto", -440, "hlineto", -300, "vlineto", -100, "hlineto", "closepath", "endchar")));
        font.CharStrings.Add(("x", Charstring(10, 500, "hsbw", 0, 30, "hstem", 0, 0, "rmoveto", 6, "callsubr", 4, 1, 3, "callothersubr", "pop", "callsubr", -100, 0, "rlineto", "closepath", "endchar")));
        font.CharStrings.Add(("E", Charstring(
            20, 600, "hsbw", "dotsection", 0, 0, "rmoveto", 1, "callsubr",
            300, 10, "rmoveto", 2, "callsubr", -200, -10, "rmoveto", 2, "callsubr", 100, 10, "rmoveto", 2, "callsubr", 100, 0, "rmoveto", 2, "callsubr",
            100, 0, "rmoveto", 2, "callsubr", 100, -10, "rmoveto", 2, "callsubr", 100, 0, "rmoveto", 2, "callsubr",
            50, 620, 0, 0, "callsubr", 0, 300, "rlineto", -600, 0, "rlineto", "closepath", "dotsection", "endchar")));
        font.CharStrings.Add(("period", Charstring(100, 10, 250, 0, "sbw", 0, 0, "rmoveto", 50, 0, "rlineto", 0, 50, "rlineto", -50, 0, "rlineto", "closepath", "endchar")));
        font.CharStrings.Add(("fi", Charstring(20, 540, "hsbw", 0, 0, "rmoveto", 1001, 2, "div", 0, "rlineto", 0, 7, 3, "div", "rlineto", "closepath", "endchar")));
        font.CharStrings.Add(("a.sc", Charstring(0, 400, "hsbw", "endchar")));
        font.CharStrings.Add(("uni2206", Charstring(0, 600, "hsbw", 0, 0, "hmoveto", 300, 600, "rlineto", 300, -600, "rlineto", "closepath", "endchar")));
        return font;
    }

    /// <summary>Writes a PFA file: the clear text, the encrypted part in hexadecimal and the trailer.</summary>
    public byte[] WritePfa()
    {
        var encrypted = Encrypt(GetPrivateText(), 55665);
        var builder = new StringBuilder(GetClearText());
        for (var i = 0; i < encrypted.Length; i += 32)
        {
            builder.Append(Convert.ToHexString(encrypted, i, Math.Min(32, encrypted.Length - i)).ToLowerInvariant()).Append('\n');
        }

        builder.Append(GetTrailer());
        return System.Text.Encoding.Latin1.GetBytes(builder.ToString());
    }

    /// <summary>Writes a PFB file: a text segment, a binary segment, the trailer in a text segment, and the end of the file.</summary>
    public byte[] WritePfb()
    {
        var result = new List<byte>();
        void AddSegment(int type, byte[] data)
        {
            result.AddRange([0x80, (byte)type, (byte)data.Length, (byte)(data.Length >> 8), (byte)(data.Length >> 16), (byte)(data.Length >> 24)]);
            result.AddRange(data);
        }

        AddSegment(1, System.Text.Encoding.Latin1.GetBytes(GetClearText()));
        AddSegment(2, Encrypt(GetPrivateText(), 55665));
        AddSegment(1, System.Text.Encoding.Latin1.GetBytes(GetTrailer()));
        result.AddRange([0x80, 3]);
        return [.. result];
    }

    private string GetClearText()
    {
        var builder = new StringBuilder();
        builder.Append(CultureInfo.InvariantCulture, $"%!PS-AdobeFont-1.0: {FontName} {Version}\n");
        builder.Append("%%CreationDate: Fri Nov 13 2026\n");
        builder.Append("% A comment with a /Name and (a string\n");
        builder.Append("12 dict begin\n");
        builder.Append("/FontInfo 9 dict dup begin\n");
        builder.Append(CultureInfo.InvariantCulture, $"/version ({Version}) readonly def\n");
        builder.Append(CultureInfo.InvariantCulture, $"/Notice ({Notice.Replace("(", "\\(", StringComparison.Ordinal).Replace(")", "\\)", StringComparison.Ordinal)}) readonly def\n");
        builder.Append(CultureInfo.InvariantCulture, $"/FullName ({FullName}) readonly def\n");
        builder.Append(CultureInfo.InvariantCulture, $"/FamilyName ({FamilyName}) readonly def\n");
        builder.Append(CultureInfo.InvariantCulture, $"/Weight ({Weight}) readonly def\n");
        builder.Append(CultureInfo.InvariantCulture, $"/ItalicAngle {ItalicAngle.ToString(CultureInfo.InvariantCulture)} def\n");
        builder.Append("/isFixedPitch false def\n");
        builder.Append("/UnderlinePosition -100 def\n");
        builder.Append("/UnderlineThickness 50 def\n");
        builder.Append("end readonly def\n");
        builder.Append(CultureInfo.InvariantCulture, $"/FontName /{FontName} def\n");
        builder.Append("/PaintType 0 def\n");
        builder.Append("/FontType 1 def\n");
        builder.Append(CultureInfo.InvariantCulture, $"/FontMatrix {FontMatrix} readonly def\n");
        if (Encoding is null)
        {
            builder.Append("/Encoding StandardEncoding def\n");
        }
        else
        {
            builder.Append("/Encoding 256 array\n0 1 255 {1 index exch /.notdef put} for\n");
            foreach (var (code, name) in Encoding)
            {
                builder.Append(CultureInfo.InvariantCulture, $"dup {code} /{name} put\n");
            }

            builder.Append("readonly def\n");
        }

        builder.Append(CultureInfo.InvariantCulture, $"/FontBBox {FontBBox} readonly def\n");
        builder.Append("/UniqueID 4000000 def\n");
        builder.Append(ExtraClearText);
        builder.Append("currentdict end\n");
        builder.Append("currentfile eexec\n");
        return builder.ToString();
    }

    private byte[] GetPrivateText()
    {
        var result = new List<byte>();
        void Text(string value) => result.AddRange(System.Text.Encoding.Latin1.GetBytes(value));

        Text("dup /Private 10 dict dup begin\n");
        Text("/RD{string currentfile exch readstring pop}executeonly def\n");
        Text("/ND{noaccess def}executeonly def\n");
        Text("/NP{noaccess put}executeonly def\n");
        Text("/BlueValues [-15 0 500 515 700 715] def\n");
        Text("/StdHW [30] def\n");
        Text("/StdVW [100] def\n");
        Text("/StemSnapV [100 110] def\n");
        Text("/BlueScale 0.039625 def\n");
        Text("/ForceBold false def\n");
        Text("/MinFeature{16 16}def\n");
        Text("/password 5839 def\n");
        if (LenIV != 4)
        {
            Text(string.Create(CultureInfo.InvariantCulture, $"/lenIV {LenIV} def\n"));
        }

        Text("/OtherSubrs [ {} {} {} { systemdict /internaldict known not {pop 3} { 1183615869 systemdict /internaldict get exec dup /startlock known {/startlock get exec} {dup /strtlck known {/strtlck get exec} {pop 3} ifelse} ifelse} ifelse } executeonly ] noaccess def\n");
        Text(string.Create(CultureInfo.InvariantCulture, $"/Subrs {Subroutines.Count} array\n"));
        for (var i = 0; i < Subroutines.Count; i++)
        {
            var data = EncryptCharstring(Subroutines[i]);
            Text(string.Create(CultureInfo.InvariantCulture, $"dup {i} {data.Length} RD "));
            result.AddRange(data);
            Text(" NP\n");
        }

        Text("ND\n");
        Text(string.Create(CultureInfo.InvariantCulture, $"2 index /CharStrings {CharStrings.Count} dict dup begin\n"));
        foreach (var (name, charstring) in CharStrings)
        {
            var data = EncryptCharstring(charstring);
            Text(string.Create(CultureInfo.InvariantCulture, $"/{name} {data.Length} RD "));
            result.AddRange(data);
            Text(" ND\n");
        }

        Text("end\nend\nreadonly put\nnoaccess put\ndup /FontName get exch definefont pop\nmark currentfile closefile\n");
        return [.. result];
    }

    private static string GetTrailer()
    {
        var builder = new StringBuilder();
        for (var i = 0; i < 8; i++)
        {
            builder.Append('0', 64).Append('\n');
        }

        return builder.Append("cleartomark\n").ToString();
    }

    private byte[] EncryptCharstring(byte[] charstring) => LenIV < 0 ? charstring : Encrypt([.. new byte[LenIV], .. charstring], 4330, addRandomBytes: false);

    /// <summary>Encrypts with the cipher of chapter 7; four bytes come first when the plain text has no random bytes of its own.</summary>
    private static byte[] Encrypt(byte[] plain, int key, bool addRandomBytes = true)
    {
        byte[] input = addRandomBytes ? [0x6D, 0x65, 0x7A, 0x69, .. plain] : plain;
        var result = new byte[input.Length];
        var r = key;
        for (var i = 0; i < input.Length; i++)
        {
            var cipher = (byte)(input[i] ^ (r >> 8));
            result[i] = cipher;
            r = (((cipher + r) * 52845) + 22719) & 0xFFFF;
        }

        return result;
    }
}
