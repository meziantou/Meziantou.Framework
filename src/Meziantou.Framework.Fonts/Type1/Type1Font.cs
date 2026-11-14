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
/// <para>
/// The decrypted font program is rented from an allocation scope, and the subroutines and the charstrings are
/// decrypted in place: they are ranges of it. Dispose the font to return it.
/// </para>
/// </remarks>
internal sealed class Type1Font : IDisposable
{
    // The keys of eexec and charstring encryption, and the constants of the cipher (7.1)
    private const int EexecKey = 55665;
    private const int CharstringKey = 4330;
    private const int CipherMultiplier = 52845;
    private const int CipherIncrement = 22719;

    // The glyphs an OpenType font can have
    private const int MaxGlyphs = 65535;

    private FontBuffer? _program;
    private int _programLength;

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

    /// <summary>Gets where the decrypted subroutines are in <see cref="Program"/>, by their index; the font defines the indexes it uses, which can leave gaps.</summary>
    public Dictionary<int, (int Start, int Length)> Subroutines { get; } = [];

    /// <summary>Gets the names of the glyphs and where their decrypted charstrings are in <see cref="Program"/>, in the order of the font program; a name is given once.</summary>
    public List<(string Name, int Start, int Length)> CharStrings { get; } = [];

    /// <summary>Gets the decrypted part of the font program, without its four random bytes.</summary>
    public ReadOnlySpan<byte> Program => _program is null ? default : _program.Span.Slice(4, _programLength);

    /// <summary>Gets the decrypted charstring of a glyph of <see cref="CharStrings"/>.</summary>
    public ReadOnlySpan<byte> GetCharstring(int index) => Program.Slice(CharStrings[index].Start, CharStrings[index].Length);

    /// <summary>Gets the decrypted charstring of a subroutine.</summary>
    public bool TryGetSubroutine(int index, out ReadOnlySpan<byte> charstring)
    {
        if (Subroutines.TryGetValue(index, out var range))
        {
            charstring = Program.Slice(range.Start, range.Length);
            return true;
        }

        charstring = default;
        return false;
    }

    public void Dispose()
    {
        _program?.Dispose();
        _program = null;
    }

    /// <summary>Gets the numbers of the private dictionary that describe the alignment zones and the stems (5.6 to 5.8).</summary>
    public Dictionary<string, double[]> PrivateValues { get; } = new(StringComparer.Ordinal);

    /// <summary>Determines whether data starts with the signature of a PFB file or of a PFA file.</summary>
    public static bool HasSignature(ReadOnlySpan<byte> data)
        => (data.Length >= 2 && data[0] == 0x80 && data[1] == 1)
        || data.StartsWith("%!PS-AdobeFont"u8)
        || data.StartsWith("%!FontType1"u8);

    /// <summary>Reads a PFB or a PFA file; the decrypted font program is rented from the scope.</summary>
    public static Type1Font Read(ReadOnlySpan<byte> data, FontAllocator scope)
    {
        Type1Font? font = new();
        try
        {
            using var clearTextWriter = new FontByteWriter(scope);
            using var encryptedWriter = new FontByteWriter(scope);
            ReadOnlySpan<byte> clearText;
            ReadOnlySpan<byte> encrypted;
            if (data.Length >= 2 && data[0] == 0x80)
            {
                ReadSegments(data, clearTextWriter, encryptedWriter);
                clearText = clearTextWriter.Written;
                encrypted = encryptedWriter.Written;
            }
            else
            {
                SplitAtEexec(data, out clearText, out encrypted);
            }

            font.ReadClearText(clearText);

            // 7.2: the first four bytes are random; they are hexadecimal digits when the encrypted part is in hexadecimal
            var isHex = encrypted.Length >= 4 && encrypted[..4].IndexOfAnyExcept("0123456789abcdefABCDEF"u8) < 0;
            var program = scope.Rent(Math.Max(4, isHex ? (encrypted.Length + 1) / 2 : encrypted.Length), clear: true);
            font._program = program;
            var length = encrypted.Length;
            if (isHex)
            {
                length = PostScriptTokenizer.DecodeHex(encrypted, program.Span);
            }
            else
            {
                encrypted.CopyTo(program.Span);
            }

            DecryptInPlace(program.Span[..length], EexecKey);
            font._programLength = Math.Max(0, length - 4);
            font.ReadPrivate(program.Span.Slice(4, font._programLength));
            if (font.CharStrings.Count == 0)
                throw FontErrors.InvalidType1("the font has no CharStrings dictionary.");

            var result = font;
            font = null;
            return result;
        }
        finally
        {
            font?.Dispose();
        }
    }

    /// <summary>Decrypts the eexec part or a charstring in place (7.1); the first bytes are random.</summary>
    private static void DecryptInPlace(Span<byte> data, int key)
    {
        var r = key;
        for (var i = 0; i < data.Length; i++)
        {
            var cipher = data[i];
            data[i] = (byte)(cipher ^ (r >> 8));
            r = (((cipher + r) * CipherMultiplier) + CipherIncrement) & 0xFFFF;
        }
    }

    /// <summary>
    /// Reads the segments of a PFB file (Technical Note #5040, "Supporting Downloadable PostScript Language Fonts", 3.3):
    /// a marker 128, a type (1: text, 2: binary, 3: end of file) and a length of 32 bits in little-endian order. The
    /// text before the first binary segment is the clear text; the binary segments that follow are the encrypted part.
    /// </summary>
    private static void ReadSegments(ReadOnlySpan<byte> data, FontByteWriter clearText, FontByteWriter encrypted)
    {
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
                if (encrypted.Length > 0)
                    break;

                segment.CopyTo(clearText.Append(segment.Length));
            }
            else
            {
                segment.CopyTo(encrypted.Append(segment.Length));
            }

            if (position == data.Length)
                break;
        }

        if (encrypted.Length == 0)
            throw FontErrors.InvalidType1("the PFB file has no binary segment.");
    }

    /// <summary>Splits a PFA file at the <c>eexec</c> operator: the encrypted part starts after the white space that follows it (7.2).</summary>
    private static void SplitAtEexec(ReadOnlySpan<byte> data, out ReadOnlySpan<byte> clearText, out ReadOnlySpan<byte> encrypted)
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

            clearText = data[..token.Start];
            encrypted = data[position..];
            return;
        }
    }

    /// <summary>Reads the entries of the font dictionary and of the FontInfo dictionary (chapter 5).</summary>
    private void ReadClearText(ReadOnlySpan<byte> text)
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

            var key = System.Text.Encoding.Latin1.GetString(text.Slice(token.Start, token.Length));
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
    /// Reads the encoding (5.3): <c>StandardEncoding</c>, or an array of 256 names, then the names that
    /// <c>dup code /name put</c> assigns up to the <c>def</c> that stores it. A copy of the standard encoding that
    /// assigns other names (<c>StandardEncoding 256 array copy dup 65 /Agrave put</c>) is a custom encoding.
    /// </summary>
    private void ReadEncoding(ref PostScriptTokenizer tokenizer, ReadOnlySpan<byte> text, PostScriptToken value)
    {
        string?[] encoding;
        var isStandard = value.IsExecutable(text, "StandardEncoding"u8);
        if (isStandard)
        {
            encoding = (string?[])CffFont.StandardEncoding.Value.Clone();
        }
        else if (value.Kind == PostScriptTokenKind.Number)
        {
            encoding = new string?[256];
        }
        else
        {
            tokenizer.SkipValue(value);
            return;
        }

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
                var assigned = glyphName == ".notdef" ? null : glyphName;
                isStandard &= string.Equals(encoding[(int)code.Number], assigned, StringComparison.Ordinal);
                encoding[(int)code.Number] = assigned;
            }
        }

        Encoding = encoding;
        HasStandardEncoding = isStandard;
    }

    /// <summary>
    /// Reads the private dictionary (chapter 5.5 to 5.8): <c>lenIV</c>, the hinting values, the subroutines
    /// (<c>dup index length RD binary NP</c>) and the charstrings (<c>/name length RD binary ND</c>).
    /// </summary>
    private void ReadPrivate(Span<byte> text)
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

            var key = System.Text.Encoding.Latin1.GetString(text.Slice(token.Start, token.Length));
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

            Subroutines[index] = DecryptCharstring(text, start, length, lenIV);
        }

        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (name, start, length) in charstrings)
        {
            if (!names.Add(name))
                continue;

            if (CharStrings.Count == MaxGlyphs)
                throw FontErrors.Unsupported($"The Type 1 font has more glyphs than the {MaxGlyphs} glyphs an OpenType font can have.", "Type 1 fonts of more than 65,535 glyphs");

            var (charstringStart, charstringLength) = DecryptCharstring(text, start, length, lenIV);
            CharStrings.Add((name, charstringStart, charstringLength));
        }
    }

    /// <summary>Reads the entries <c>dup index length RD binary NP</c> of the subroutines, up to the first token that is not <c>dup</c>.</summary>
    private static void ReadSubroutines(ref PostScriptTokenizer tokenizer, ReadOnlySpan<byte> text, List<(int Index, int Start, int Length)> subroutines)
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
    private static bool IsSubroutineTerminator(PostScriptToken token, ReadOnlySpan<byte> text)
        => token.IsExecutable(text, "NP"u8) || token.IsExecutable(text, "|"u8) || token.IsExecutable(text, "noaccess"u8)
        || token.IsExecutable(text, "readonly"u8) || token.IsExecutable(text, "executeonly"u8) || token.IsExecutable(text, "put"u8);

    /// <summary>Reads the entries <c>/name length RD binary ND</c> of the charstrings, up to <c>end</c>.</summary>
    private static void ReadCharStrings(ref PostScriptTokenizer tokenizer, ReadOnlySpan<byte> text, List<(string Name, int Start, int Length)> charstrings)
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
            charstrings.Add((System.Text.Encoding.Latin1.GetString(text.Slice(token.Start, token.Length)), start, binary.Length));
        }
    }

    /// <summary>
    /// Decrypts a charstring in place and gets where it is without its random bytes; a negative <c>lenIV</c> means that
    /// the charstrings are not encrypted (5.7).
    /// </summary>
    private static (int Start, int Length) DecryptCharstring(Span<byte> text, int start, int length, int lenIV)
    {
        if (lenIV < 0)
            return (start, length);

        DecryptInPlace(text.Slice(start, length), CharstringKey);
        var skipped = Math.Min(lenIV, length);
        return (start + skipped, length - skipped);
    }
}
