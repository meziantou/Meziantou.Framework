using System.Buffers.Binary;

namespace Meziantou.Framework.Fonts;

/// <summary>
/// A PostScript Type 1 font program, read from a PFB or PFA file: Adobe Type 1 Font Format (version 1.1), and Adobe
/// Technical Note #5040 for the segments of PFB files.
/// </summary>
/// <remarks>
/// <para>
/// The clear text gives the font dictionary (names, matrix, bounding box, encoding); the part encrypted with
/// <c>eexec</c> gives the private dictionary, the subroutines and the charstrings, which are decrypted when the font is
/// read (chapter 7, "Encryption"). The font program is not run as PostScript: the dictionaries are read from the
/// tokens of the usual layout of a font (chapter 2, "Font Program Organization"), and a key whose value is not in the
/// form the specification gives is ignored.
/// </para>
/// <para>
/// Multiple master fonts, CID-keyed fonts and fonts that are not of type 1 are
/// <see cref="UnsupportedFontFeatureException"/>.
/// </para>
/// </remarks>
internal sealed class Type1Font
{
    // The keys of eexec and charstring encryption, and the constants of the cipher (7.1)
    private const int EexecKey = 55665;
    private const int CharstringKey = 4330;
    private const int CipherMultiplier = 52845;
    private const int CipherIncrement = 22719;

    // The glyphs an OpenType font can have
    private const int MaxGlyphs = 65535;

    private Type1Font()
    {
    }

    public string FontName { get; private set; } = "";

    public string? FamilyName { get; private set; }

    public string? FullName { get; private set; }

    public string? Weight { get; private set; }

    public string? Version { get; private set; }

    public string? Notice { get; private set; }

    public string? Copyright { get; private set; }

    public double ItalicAngle { get; private set; }

    public bool IsFixedPitch { get; private set; }

    /// <summary>Gets the position of the center of the underline (FontInfo dictionary, 5.4).</summary>
    public double UnderlinePosition { get; private set; } = -100;

    public double UnderlineThickness { get; private set; } = 50;

    public double[] FontMatrix { get; private set; } = [0.001, 0, 0, 0.001, 0, 0];

    public double[] FontBBox { get; private set; } = [0, 0, 0, 0];

    /// <summary>Gets the name of the glyph of each code; <see langword="null"/> for a code without glyph.</summary>
    public string?[] Encoding { get; private set; } = new string?[256];

    /// <summary>Gets a value indicating whether the encoding is the standard encoding.</summary>
    public bool HasStandardEncoding { get; private set; }

    /// <summary>Gets the decrypted subroutines by their index; the font defines the indexes it uses, which can leave gaps.</summary>
    public Dictionary<int, byte[]> Subroutines { get; } = [];

    /// <summary>Gets the names of the glyphs and their decrypted charstrings, in the order of the font program; a name is given once.</summary>
    public List<(string Name, byte[] Charstring)> CharStrings { get; } = [];

    /// <summary>Gets the numbers of the private dictionary that describe the alignment zones and the stems (5.6 to 5.8).</summary>
    public Dictionary<string, double[]> PrivateValues { get; } = new(StringComparer.Ordinal);

    /// <summary>Determines whether data starts with the signature of a PFB file or of a PFA file.</summary>
    public static bool HasSignature(ReadOnlySpan<byte> data)
        => (data.Length >= 2 && data[0] == 0x80 && data[1] == 1)
        || data.StartsWith("%!PS-AdobeFont"u8)
        || data.StartsWith("%!FontType1"u8);

    /// <summary>Reads a PFB or a PFA file.</summary>
    public static Type1Font Read(ReadOnlySpan<byte> data)
    {
        byte[] clearText;
        byte[] encrypted;
        if (data.Length >= 2 && data[0] == 0x80)
        {
            (clearText, encrypted) = ReadSegments(data);
        }
        else
        {
            (clearText, encrypted) = SplitAtEexec(data);
        }

        var font = new Type1Font();
        font.ReadClearText(clearText);

        // 7.2: the first four bytes are random; they are hexadecimal digits when the encrypted part is in hexadecimal
        if (encrypted.Length >= 4 && encrypted.AsSpan(0, 4).IndexOfAnyExcept("0123456789abcdefABCDEF"u8) < 0)
        {
            encrypted = PostScriptTokenizer.DecodeHex(encrypted);
        }

        font.ReadPrivate(Decrypt(encrypted, EexecKey, skip: 4));
        if (font.CharStrings.Count == 0)
            throw FontErrors.InvalidType1("the font has no CharStrings dictionary.");

        return font;
    }

    /// <summary>Decrypts the eexec part or a charstring (7.1): the first bytes are random and are dropped.</summary>
    public static byte[] Decrypt(ReadOnlySpan<byte> data, int key, int skip)
    {
        var r = key;
        var result = new byte[Math.Max(0, data.Length - skip)];
        for (var i = 0; i < data.Length; i++)
        {
            var cipher = data[i];
            if (i >= skip)
            {
                result[i - skip] = (byte)(cipher ^ (r >> 8));
            }

            r = (((cipher + r) * CipherMultiplier) + CipherIncrement) & 0xFFFF;
        }

        return result;
    }

    /// <summary>
    /// Reads the segments of a PFB file (Technical Note #5040, "Supporting Downloadable PostScript Language Fonts", 3.3):
    /// a marker 128, a type (1: text, 2: binary, 3: end of file) and a length of 32 bits in little-endian order. The
    /// text before the first binary segment is the clear text; the binary segments that follow are the encrypted part.
    /// </summary>
    private static (byte[] ClearText, byte[] Encrypted) ReadSegments(ReadOnlySpan<byte> data)
    {
        var clearText = new List<byte>();
        var encrypted = new List<byte>();
        var position = 0;
        while (true)
        {
            if (position + 2 > data.Length || data[position] != 0x80)
                throw FontErrors.InvalidType1("a segment of the PFB file does not start with its marker.");

            var type = data[position + 1];
            if (type == 3)
                break;

            if (type is not (1 or 2) || position + 6 > data.Length)
                throw FontErrors.InvalidType1($"the PFB file has a segment of the unknown type {type}.");

            var length = BinaryPrimitives.ReadUInt32LittleEndian(data[(position + 2)..]);
            position += 6;
            if (length > (uint)(data.Length - position))
                throw FontErrors.InvalidType1("a segment of the PFB file extends beyond the end of the file.");

            var segment = data.Slice(position, (int)length);
            position += (int)length;
            if (type == 1)
            {
                // The text after the encrypted part is the trailer: zeros and cleartomark
                if (encrypted.Count > 0)
                    break;

                clearText.AddRange(segment);
            }
            else
            {
                encrypted.AddRange(segment);
            }

            if (position == data.Length)
                break;
        }

        if (encrypted.Count == 0)
            throw FontErrors.InvalidType1("the PFB file has no binary segment.");

        return ([.. clearText], [.. encrypted]);
    }

    /// <summary>Splits a PFA file at the <c>eexec</c> operator: the encrypted part starts after the white space that follows it (7.2).</summary>
    private static (byte[] ClearText, byte[] Encrypted) SplitAtEexec(ReadOnlySpan<byte> data)
    {
        var tokenizer = new PostScriptTokenizer(data);
        while (true)
        {
            var token = tokenizer.Read();
            if (token.Kind == PostScriptTokenKind.EndOfData)
                throw FontErrors.InvalidType1("the font has no part encrypted with eexec.");

            tokenizer.SkipValue(token);
            if (!token.IsExecutable(data, "eexec"u8))
                continue;

            var position = tokenizer.Position;
            while (position < data.Length && PostScriptTokenizer.IsWhiteSpace(data[position]))
            {
                position++;
            }

            return (data[..token.Start].ToArray(), data[position..].ToArray());
        }
    }

    /// <summary>Reads the entries of the font dictionary and of the FontInfo dictionary (chapter 5).</summary>
    private void ReadClearText(byte[] text)
    {
        var tokenizer = new PostScriptTokenizer(text);
        var hasFontType = false;
        while (true)
        {
            var token = tokenizer.Read();
            if (token.Kind == PostScriptTokenKind.EndOfData)
                break;

            // The procedures (the definitions of RD, ND, NP, BuildChar of other formats) hold nothing of the font
            if (token.Kind != PostScriptTokenKind.LiteralName)
            {
                tokenizer.SkipValue(token);
                continue;
            }

            var key = System.Text.Encoding.Latin1.GetString(text, token.Start, token.Length);
            var value = tokenizer.Read();
            switch (key)
            {
                case "FontType" when value.Kind == PostScriptTokenKind.Number:
                    if (value.Number != 1)
                        throw FontErrors.Unsupported($"The PostScript font is of type {value.Number}; only Type 1 fonts are supported.", "PostScript font type " + value.Number.ToString(System.Globalization.CultureInfo.InvariantCulture));

                    hasFontType = true;
                    break;

                case "CIDFontType" or "CIDSystemInfo" or "FDArray":
                    throw FontErrors.Unsupported("The font is a CID-keyed PostScript font, which is not supported.", "CID-keyed Type 1 fonts");

                case "BlendDesignPositions" or "BlendDesignMap" or "BlendAxisTypes" or "WeightVector":
                    throw FontErrors.Unsupported("The font is a multiple master Type 1 font, which is not supported.", "Multiple master fonts");

                case "PaintType" when value.Kind == PostScriptTokenKind.Number && value.Number != 0:
                    throw FontErrors.Unsupported("The Type 1 font is stroked (PaintType 2), which is not supported.", "Stroked Type 1 fonts");

                case "FontName" when value.Kind is PostScriptTokenKind.LiteralName or PostScriptTokenKind.String:
                    FontName = tokenizer.GetText(value);
                    break;

                case "FamilyName" when value.Kind == PostScriptTokenKind.String:
                    FamilyName = tokenizer.GetText(value);
                    break;

                case "FullName" when value.Kind == PostScriptTokenKind.String:
                    FullName = tokenizer.GetText(value);
                    break;

                case "Weight" when value.Kind == PostScriptTokenKind.String:
                    Weight = tokenizer.GetText(value);
                    break;

                case "version" when value.Kind == PostScriptTokenKind.String:
                    Version = tokenizer.GetText(value);
                    break;

                case "Notice" when value.Kind == PostScriptTokenKind.String:
                    Notice = tokenizer.GetText(value);
                    break;

                case "Copyright" when value.Kind == PostScriptTokenKind.String:
                    Copyright = tokenizer.GetText(value);
                    break;

                case "ItalicAngle" when value.Kind == PostScriptTokenKind.Number:
                    ItalicAngle = value.Number;
                    break;

                case "isFixedPitch" when value.Kind == PostScriptTokenKind.ExecutableName:
                    IsFixedPitch = value.IsExecutable(text, "true"u8);
                    break;

                case "UnderlinePosition" when value.Kind == PostScriptTokenKind.Number:
                    UnderlinePosition = value.Number;
                    break;

                case "UnderlineThickness" when value.Kind == PostScriptTokenKind.Number:
                    UnderlineThickness = value.Number;
                    break;

                case "FontMatrix":
                    if (tokenizer.ReadNumbers(value, 6) is [var a, var b, var c, var d, var e, var f])
                    {
                        FontMatrix = [a, b, c, d, e, f];
                    }

                    break;

                case "FontBBox":
                    if (tokenizer.ReadNumbers(value, 4) is [var xMin, var yMin, var xMax, var yMax])
                    {
                        FontBBox = [xMin, yMin, xMax, yMax];
                    }

                    break;

                case "Encoding":
                    ReadEncoding(ref tokenizer, text, value);
                    break;

                default:
                    // FontInfo is a dictionary whose entries are read as entries of the font dictionary
                    if (value.Kind != PostScriptTokenKind.DictionaryStart)
                    {
                        tokenizer.SkipValue(value);
                    }

                    break;
            }
        }

        if (!hasFontType)
            throw FontErrors.InvalidType1("the font dictionary has no FontType entry.");
    }

    /// <summary>
    /// Reads the encoding (5.3): <c>StandardEncoding</c>, or an array of 256 names filled by <c>dup code /name put</c>
    /// up to the <c>def</c> that stores it.
    /// </summary>
    private void ReadEncoding(ref PostScriptTokenizer tokenizer, byte[] text, PostScriptToken value)
    {
        if (value.IsExecutable(text, "StandardEncoding"u8))
        {
            HasStandardEncoding = true;
            Encoding = (string?[])CffFont.StandardEncoding.Value.Clone();
            return;
        }

        if (value.Kind != PostScriptTokenKind.Number)
        {
            tokenizer.SkipValue(value);
            return;
        }

        var encoding = new string?[256];
        while (true)
        {
            var token = tokenizer.Read();
            if (token.Kind == PostScriptTokenKind.EndOfData || token.IsExecutable(text, "def"u8) || token.IsExecutable(text, "readonly"u8))
                break;

            if (!token.IsExecutable(text, "dup"u8))
            {
                // The loop that fills the array with .notdef is a procedure
                tokenizer.SkipValue(token);
                continue;
            }

            var code = tokenizer.Peek();
            if (!code.IsInteger)
                continue;

            tokenizer.Read();
            var name = tokenizer.Peek();
            if (name.Kind != PostScriptTokenKind.LiteralName)
                continue;

            tokenizer.Read();
            if (tokenizer.Peek().IsExecutable(text, "put"u8) && code.Number is >= 0 and < 256)
            {
                tokenizer.Read();
                var glyphName = tokenizer.GetText(name);
                encoding[(int)code.Number] = glyphName == ".notdef" ? null : glyphName;
            }
        }

        Encoding = encoding;
    }

    /// <summary>
    /// Reads the private dictionary (chapter 5.5 to 5.8): <c>lenIV</c>, the hinting values, the subroutines
    /// (<c>dup index length RD binary NP</c>) and the charstrings (<c>/name length RD binary ND</c>).
    /// </summary>
    private void ReadPrivate(byte[] text)
    {
        var tokenizer = new PostScriptTokenizer(text);
        var lenIV = 4;
        var subroutines = new List<(int Index, int Start, int Length)>();
        var subroutineCount = 0;
        var charstrings = new List<(string Name, int Start, int Length)>();
        var hasPrivate = false;
        while (true)
        {
            var token = tokenizer.Read();
            if (token.Kind == PostScriptTokenKind.EndOfData)
                break;

            // The font program ends with "mark currentfile closefile": what follows is the trailer
            if (token.IsExecutable(text, "closefile"u8))
                break;

            if (token.Kind != PostScriptTokenKind.LiteralName)
            {
                tokenizer.SkipValue(token);
                continue;
            }

            var key = System.Text.Encoding.Latin1.GetString(text, token.Start, token.Length);
            switch (key)
            {
                case "Private":
                    hasPrivate = true;
                    break;

                case "lenIV" when tokenizer.Peek().IsInteger:
                    lenIV = (int)tokenizer.Read().Number;
                    break;

                case "BlueValues" or "OtherBlues" or "FamilyBlues" or "FamilyOtherBlues" or "StemSnapH" or "StemSnapV" or "StdHW" or "StdVW":
                {
                    var value = tokenizer.Read();
                    if (tokenizer.ReadNumbers(value, 14) is { } numbers)
                    {
                        PrivateValues[key] = numbers;
                    }

                    break;
                }

                case "BlueScale" or "BlueShift" or "BlueFuzz" or "LanguageGroup" or "ExpansionFactor" when tokenizer.Peek().Kind == PostScriptTokenKind.Number:
                    PrivateValues[key] = [tokenizer.Read().Number];
                    break;

                case "ForceBold" when tokenizer.Peek().Kind == PostScriptTokenKind.ExecutableName:
                    PrivateValues[key] = [tokenizer.Read().IsExecutable(text, "true"u8) ? 1 : 0];
                    break;

                case "Subrs" when tokenizer.Peek().IsInteger:
                    subroutineCount = (int)tokenizer.Read().Number;
                    if (subroutineCount < 0)
                        throw FontErrors.InvalidType1($"the font declares {subroutineCount} subroutines.");

                    ReadSubroutines(ref tokenizer, text, subroutines);
                    break;

                case "CharStrings" when tokenizer.Peek().IsInteger:
                    tokenizer.Read();
                    ReadCharStrings(ref tokenizer, text, charstrings);
                    break;
            }
        }

        if (!hasPrivate)
            throw FontErrors.InvalidType1("the encrypted part has no Private dictionary: the key of the encryption is not the one of eexec.");

        foreach (var (index, start, length) in subroutines)
        {
            if (index >= subroutineCount)
                throw FontErrors.InvalidType1($"the subroutine {index} is outside the {subroutineCount} subroutines of the font.");

            Subroutines[index] = DecryptCharstring(text.AsSpan(start, length), lenIV);
        }

        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (name, start, length) in charstrings)
        {
            if (!names.Add(name))
                continue;

            if (CharStrings.Count == MaxGlyphs)
                throw FontErrors.Unsupported($"The Type 1 font has more glyphs than the {MaxGlyphs} glyphs an OpenType font can have.", "Type 1 fonts of more than 65,535 glyphs");

            CharStrings.Add((name, DecryptCharstring(text.AsSpan(start, length), lenIV)));
        }
    }

    /// <summary>Reads the entries <c>dup index length RD binary NP</c> of the subroutines, up to the first token that is not <c>dup</c>.</summary>
    private static void ReadSubroutines(ref PostScriptTokenizer tokenizer, byte[] text, List<(int Index, int Start, int Length)> subroutines)
    {
        // "array"
        tokenizer.Read();
        while (true)
        {
            var token = tokenizer.Peek();
            if (!token.IsExecutable(text, "dup"u8))
                return;

            tokenizer.Read();
            var index = tokenizer.Read();
            var length = tokenizer.Read();
            if (!index.IsInteger || !length.IsInteger || index.Number < 0)
                throw FontErrors.InvalidType1("a subroutine has no index or no length.");

            // RD or -|, then one space and the bytes, then NP or |
            tokenizer.Read();
            var start = tokenizer.Position + 1;
            var binary = tokenizer.ReadBinary((int)length.Number);
            subroutines.Add(((int)index.Number, start, binary.Length));
            while (IsSubroutineTerminator(tokenizer.Peek(), text))
            {
                tokenizer.Read();
            }
        }
    }

    /// <summary>Determines whether a token is part of what stores a subroutine in its array: <c>NP</c>, <c>|</c>, or <c>noaccess put</c>.</summary>
    private static bool IsSubroutineTerminator(PostScriptToken token, byte[] text)
        => token.IsExecutable(text, "NP"u8) || token.IsExecutable(text, "|"u8) || token.IsExecutable(text, "noaccess"u8)
        || token.IsExecutable(text, "readonly"u8) || token.IsExecutable(text, "executeonly"u8) || token.IsExecutable(text, "put"u8);

    /// <summary>Reads the entries <c>/name length RD binary ND</c> of the charstrings, up to <c>end</c>.</summary>
    private static void ReadCharStrings(ref PostScriptTokenizer tokenizer, byte[] text, List<(string Name, int Start, int Length)> charstrings)
    {
        while (true)
        {
            var token = tokenizer.Read();
            if (token.Kind == PostScriptTokenKind.EndOfData || token.IsExecutable(text, "end"u8))
                return;

            if (token.Kind != PostScriptTokenKind.LiteralName)
            {
                // "dict dup begin", and ND, |- or "noaccess def" after each charstring
                tokenizer.SkipValue(token);
                continue;
            }

            var length = tokenizer.Read();
            if (!length.IsInteger)
                throw FontErrors.InvalidType1("a charstring has no length.");

            tokenizer.Read();
            var start = tokenizer.Position + 1;
            var binary = tokenizer.ReadBinary((int)length.Number);
            charstrings.Add((System.Text.Encoding.Latin1.GetString(text, token.Start, token.Length), start, binary.Length));
        }
    }

    /// <summary>Decrypts a charstring; a negative <c>lenIV</c> means that the charstrings are not encrypted (5.7).</summary>
    private static byte[] DecryptCharstring(ReadOnlySpan<byte> data, int lenIV) => lenIV < 0 ? data.ToArray() : Decrypt(data, CharstringKey, lenIV);
}
