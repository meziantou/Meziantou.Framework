using System.Buffers.Binary;
using System.Globalization;
using System.Text;

namespace Meziantou.Framework.Fonts;

/// <summary>
/// Converts a Type 1 font to an OpenType font with CFF outlines, which is then read like any other font: the glyphs
/// of the font program in its order (<c>.notdef</c> first), their outlines as Type 2 charstrings
/// (<see cref="Type1CharstringConverter"/>), and the tables the font dictionary and an AFM file describe.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item><description>
/// The units per em are the inverse of the scale of the font matrix (1000 for the usual matrix), and the outlines are
/// transformed by the rest of the matrix (the slant of an oblique font made with its matrix).
/// </description></item>
/// <item><description>
/// The character map gives the glyph of each name of one character (<see cref="Type1GlyphNames"/>); a character two
/// glyphs have is the one of the first glyph. The encoding of the font is the encoding of the CFF table.
/// </description></item>
/// <item><description>
/// The ascender and the descender are the ones of the AFM file, or the top and the bottom of the bounding box of the
/// font; the line gap makes the height of a line 1.2 em when the ascender and the descender give less. The cap height
/// and the x-height are the ones of the AFM file, or the tops of the glyphs <c>H</c> and <c>x</c>.
/// </description></item>
/// <item><description>The kerning pairs of the AFM file become a <c>kern</c> table.</description></item>
/// </list>
/// Converting the charstrings runs every one of them: the operations of each glyph are charged to its own
/// <see cref="GlyphBudget"/>, and the operations of the whole font to a budget in proportion with its size.
/// </remarks>
internal static class Type1FontConverter
{
    private const int Escape = CffDictReader.Escape;

    // The standard strings of CFF, by name (Technical Note #5176, appendix A)
    private static readonly Lazy<Dictionary<string, int>> StandardStringIds = new(() =>
    {
        var result = new Dictionary<string, int>(CffStandardStrings.Names.Length, StringComparer.Ordinal);
        for (var i = 0; i < CffStandardStrings.Names.Length; i++)
        {
            result.TryAdd(CffStandardStrings.Names[i], i);
        }

        return result;
    });

    /// <summary>
    /// Converts the bytes of a PFB or a PFA file to the bytes of an OpenType font. The decrypted font program and the
    /// converted charstrings are rented from an allocation scope of the environment; the tables of the font are
    /// checked against <see cref="FontResourceLimits.MaxFontBytes"/> before they are allocated.
    /// </summary>
    public static byte[] Convert(ReadOnlySpan<byte> data, AfmFontMetrics? metrics, FontEnvironment environment)
    {
        var limits = environment.Limits;
        var scope = environment.CreateAllocator("Type 1");
        using var font = Type1Font.Read(data, scope);
        using var output = new FontByteWriter(scope);

        // The glyphs: .notdef first, then the glyphs of the font program in its order
        var glyphs = new List<int>(font.CharStrings.Count + 1);
        var glyphsByName = new Dictionary<string, int>(font.CharStrings.Count, StringComparer.Ordinal);
        for (var i = 0; i < font.CharStrings.Count; i++)
        {
            glyphsByName.Add(font.CharStrings[i].Name, i);
        }

        // -1 is a .notdef glyph the font does not have: an empty glyph
        glyphs.Add(glyphsByName.GetValueOrDefault(".notdef", -1));
        for (var i = 0; i < font.CharStrings.Count; i++)
        {
            if (i != glyphs[0])
            {
                glyphs.Add(i);
            }
        }

        if (glyphs.Count > ushort.MaxValue)
            throw FontErrors.Unsupported("The Type 1 font has more glyphs than the 65,535 glyphs an OpenType font can have.", "Type 1 fonts of more than 65,535 glyphs");

        var (unitsPerEm, matrix) = GetUnitsPerEm(font.FontMatrix);
        var glyphNames = glyphs.ConvertAll(index => index < 0 ? ".notdef" : font.CharStrings[index].Name);

        // The charstrings, one after the other in the output (offsets[glyph] is where the charstring of a glyph starts),
        // the advance widths and the bounds of the glyphs
        var offsets = new int[glyphs.Count + 1];
        var advances = new int[glyphs.Count];
        var bounds = new (double XMin, double YMin, double XMax, double YMax)?[glyphs.Count];
        var converter = new Type1CharstringConverter(font, glyphsByName, matrix);
        var writer = new Type2CharstringWriter(output);
        var fontBudget = new GlyphBudget(new FontResourceLimits { MaxGlyphComplexity = limits.MaxGlyphComplexity + (64L * data.Length) });
        for (var glyph = 0; glyph < glyphs.Count; glyph++)
        {
            offsets[glyph] = output.Length;
            if (glyphs[glyph] < 0)
            {
                output.Append(1)[0] = 14;
                continue;
            }

            var budget = new GlyphBudget(limits);
            try
            {
                converter.Convert(font.GetCharstring(glyphs[glyph]), writer, ref budget);
            }
            catch (InvalidFontException exception)
            {
                throw new InvalidFontException($"{exception.Message} (glyph '{glyphNames[glyph]}')", exception);
            }

            fontBudget.Charge(converter.Operations);
            advances[glyph] = (int)Math.Clamp(converter.AdvanceWidth, 0, ushort.MaxValue);
            if (writer.HasPoints)
            {
                bounds[glyph] = (writer.XMin, writer.YMin, writer.XMax, writer.YMax);
            }

            if (output.Length > limits.MaxFontBytes)
                throw new FontResourceLimitException(FontResourceLimitKind.FontBytes, limits.MaxFontBytes, output.Length);
        }

        offsets[glyphs.Count] = output.Length;

        var afmScale = unitsPerEm / 1000.0;
        var info = new FontInfo(font, metrics, unitsPerEm, matrix, glyphNames, advances, bounds, afmScale);
        var tables = new SortedDictionary<uint, byte[]>
        {
            [FontTags.Cff] = WriteCff(font, info, glyphNames, output.Written, offsets, limits),
            [FontTags.Head] = WriteHead(info),
            [FontTags.Hhea] = WriteHhea(info),
            [FontTags.Maxp] = WriteMaxp(glyphs.Count),
            [FontTags.Hmtx] = WriteHmtx(info),
            [FontTags.Cmap] = WriteCmap(info.CharacterMap),
            [FontTags.Post] = WritePost(info),
            [FontTags.Name] = WriteName(info),
            [FontTags.Os2] = WriteOs2(info),
        };

        if (metrics is not null && WriteKern(metrics, glyphNames, afmScale) is { } kern)
        {
            tables[FontTags.Kern] = kern;
        }

        var size = tables.Values.Sum(table => SfntWriter.Pad(table.Length)) + SfntWriter.GetDirectoryLength(tables.Count);
        if (size > limits.MaxFontBytes)
            throw new FontResourceLimitException(FontResourceLimitKind.FontBytes, limits.MaxFontBytes, size);

        return FontSubsetter.WriteSfnt(FontTags.OpenTypeCff, tables);
    }

    /// <summary>
    /// Gets the units per em of the font matrix (Type 1 Font Format, 5.3): the inverse of the scale of its
    /// determinant, and the transform from the units of the charstrings to these units, which is the identity for a
    /// matrix that only scales.
    /// </summary>
    private static (int UnitsPerEm, double[] Matrix) GetUnitsPerEm(double[] fontMatrix)
    {
        var determinant = (fontMatrix[0] * fontMatrix[3]) - (fontMatrix[1] * fontMatrix[2]);
        var scale = Math.Sqrt(Math.Abs(determinant));
        var unitsPerEm = scale > 0 ? Math.Round(1 / scale, MidpointRounding.AwayFromZero) : 0;
        if (!(unitsPerEm is >= 16 and <= 16384))
            throw FontErrors.Unsupported("The font matrix of the Type 1 font gives a size of the em outside the 16 to 16,384 units of an OpenType font.", "Type 1 font matrix");

        var matrix = new double[6];
        for (var i = 0; i < 6; i++)
        {
            matrix[i] = fontMatrix[i] * unitsPerEm;
        }

        // A matrix that scales by the inverse of a whole number gives the identity: avoid a rounding of 1 - 1e-16
        for (var i = 0; i < 4; i++)
        {
            var rounded = Math.Round(matrix[i], MidpointRounding.AwayFromZero);
            if (Math.Abs(matrix[i] - rounded) < 1e-9)
            {
                matrix[i] = rounded;
            }
        }

        return ((int)unitsPerEm, matrix);
    }

    /// <summary>What the tables of the font are written from.</summary>
    private sealed class FontInfo
    {
        public FontInfo(Type1Font font, AfmFontMetrics? metrics, int unitsPerEm, double[] matrix, List<string> glyphNames, int[] advances, (double XMin, double YMin, double XMax, double YMax)?[] bounds, double afmScale)
        {
            Font = font;
            UnitsPerEm = unitsPerEm;
            Matrix = matrix;
            Advances = advances;
            Bounds = bounds;

            // The bounding box of the font, transformed; the bounds of the glyphs when the font gives none
            var box = font.FontBBox;
            if (box[0] == 0 && box[1] == 0 && box[2] == 0 && box[3] == 0)
            {
                var union = bounds.Where(b => b is not null).Select(b => b!.Value).ToList();
                FontBox = union.Count == 0 ? (0, 0, 0, 0) : (union.Min(b => b.XMin), union.Min(b => b.YMin), union.Max(b => b.XMax), union.Max(b => b.YMax));
            }
            else
            {
                double[] xs = [Transform(box[0], box[1]).X, Transform(box[0], box[3]).X, Transform(box[2], box[1]).X, Transform(box[2], box[3]).X];
                double[] ys = [Transform(box[0], box[1]).Y, Transform(box[0], box[3]).Y, Transform(box[2], box[1]).Y, Transform(box[2], box[3]).Y];
                FontBox = (xs.Min(), ys.Min(), xs.Max(), ys.Max());
            }

            XMin = ToInt16(Math.Floor(FontBox.XMin));
            YMin = ToInt16(Math.Floor(FontBox.YMin));
            XMax = ToInt16(Math.Ceiling(FontBox.XMax));
            YMax = ToInt16(Math.Ceiling(FontBox.YMax));

            Ascender = metrics?.Ascender is { } ascender ? ToInt16(ascender * afmScale) : YMax;
            Descender = metrics?.Descender is { } descender ? ToInt16(descender * afmScale) : YMin;
            LineGap = ToInt16(Math.Max(0, Math.Round(unitsPerEm * 1.2, MidpointRounding.AwayFromZero) - (Ascender - Descender)));

            int GetTop(string name)
            {
                var index = glyphNames.IndexOf(name);
                return index >= 0 && bounds[index] is { } glyphBounds ? ToInt16(glyphBounds.YMax) : 0;
            }

            CapHeight = metrics?.CapHeight is { } capHeight ? ToInt16(capHeight * afmScale) : GetTop("H");
            XHeight = metrics?.XHeight is { } xHeight ? ToInt16(xHeight * afmScale) : GetTop("x");

            // The character map
            for (var glyph = 1; glyph < glyphNames.Count; glyph++)
            {
                if (Type1GlyphNames.TryGetCodePoint(glyphNames[glyph], out var codePoint))
                {
                    CharacterMap.TryAdd(codePoint, glyph);
                }
            }

            // The style, from the names of the font
            var weight = font.Weight?.Replace(" ", "", StringComparison.Ordinal).ToUpperInvariant() ?? "";
            WeightClass = weight switch
            {
                "THIN" or "HAIRLINE" => 100,
                "EXTRALIGHT" or "ULTRALIGHT" => 200,
                "LIGHT" => 300,
                "MEDIUM" => 500,
                "SEMIBOLD" or "DEMIBOLD" or "DEMI" => 600,
                "BOLD" => 700,
                "EXTRABOLD" or "ULTRABOLD" => 800,
                "BLACK" or "HEAVY" or "ULTRA" => 900,
                _ => 400,
            };

            var names = (font.FullName ?? font.FontName).ToUpperInvariant();
            IsItalic = font.ItalicAngle != 0 || names.Contains("ITALIC", StringComparison.Ordinal) || names.Contains("OBLIQUE", StringComparison.Ordinal);
            IsBold = WeightClass >= 700;
            IsRegularWeight = WeightClass == 400 || (IsBold && WeightClass == 700);
            WeightName = string.IsNullOrEmpty(font.Weight) ? null : font.Weight;
        }

        public Type1Font Font { get; }

        public int UnitsPerEm { get; }

        public double[] Matrix { get; }

        public int[] Advances { get; }

        public (double XMin, double YMin, double XMax, double YMax)?[] Bounds { get; }

        public (double XMin, double YMin, double XMax, double YMax) FontBox { get; }

        public int XMin { get; }

        public int YMin { get; }

        public int XMax { get; }

        public int YMax { get; }

        public int Ascender { get; }

        public int Descender { get; }

        public int LineGap { get; }

        public int CapHeight { get; }

        public int XHeight { get; }

        public SortedDictionary<int, int> CharacterMap { get; } = [];

        public int WeightClass { get; }

        public string? WeightName { get; }

        public bool IsItalic { get; }

        public bool IsBold { get; }

        /// <summary>Gets a value indicating whether the weight is regular or bold: the four styles of a family of the legacy names.</summary>
        public bool IsRegularWeight { get; }

        public (double X, double Y) Transform(double x, double y)
            => ((Matrix[0] * x) + (Matrix[2] * y) + Matrix[4], (Matrix[1] * x) + (Matrix[3] * y) + Matrix[5]);

        public int GetLeftSideBearing(int glyph) => Bounds[glyph] is { } bounds ? ToInt16(Math.Floor(bounds.XMin)) : 0;
    }

    private static int ToInt16(double value) => (int)Math.Clamp(Math.Round(value, MidpointRounding.AwayFromZero), short.MinValue, short.MaxValue);

    /// <summary>Writes the CFF table (Technical Note #5176): one font whose glyphs have names, with the converted charstrings.</summary>
    private static byte[] WriteCff(Type1Font font, FontInfo info, List<string> glyphNames, ReadOnlySpan<byte> charstrings, int[] offsets, FontResourceLimits limits)
    {
        // A string identifier is a number of 16 bits, at most 64,999 (section 10)
        const int MaxStringId = 64999;
        var strings = new List<byte[]>();
        var customStrings = new Dictionary<string, int>(StringComparer.Ordinal);
        int GetStringId(string value)
        {
            if (StandardStringIds.Value.TryGetValue(value, out var standard))
                return standard;

            if (!customStrings.TryGetValue(value, out var id))
            {
                id = CffStandardStrings.Names.Length + strings.Count;
                if (id > MaxStringId)
                    throw FontErrors.Unsupported("The Type 1 font has more glyph names and strings than a CFF table can hold.", "Type 1 fonts of more than 64,609 glyph names");

                customStrings.Add(value, id);
                strings.Add(Encoding.Latin1.GetBytes(value));
            }

            return id;
        }

        // The Top DICT without its positions (table 9)
        var top = new List<(int Operator, double[] Operands)>();
        void AddString(int @operator, string? value)
        {
            if (!string.IsNullOrEmpty(value))
            {
                top.Add((@operator, [GetStringId(value)]));
            }
        }

        AddString(0, font.Version);
        AddString(1, font.Notice);
        AddString(Escape + 0, font.Copyright);
        AddString(2, font.FullName);
        AddString(3, font.FamilyName);
        AddString(4, font.Weight);
        if (font.IsFixedPitch)
        {
            top.Add((Escape + 1, [1]));
        }

        if (font.ItalicAngle != 0)
        {
            top.Add((Escape + 2, [font.ItalicAngle]));
        }

        top.Add((Escape + 3, [Math.Round(font.UnderlinePosition * info.Matrix[3], 3, MidpointRounding.AwayFromZero)]));
        top.Add((Escape + 4, [Math.Round(font.UnderlineThickness * info.Matrix[3], 3, MidpointRounding.AwayFromZero)]));
        if (info.UnitsPerEm != 1000)
        {
            top.Add((Escape + 7, [1.0 / info.UnitsPerEm, 0, 0, 1.0 / info.UnitsPerEm, 0, 0]));
        }

        top.Add((5, [info.XMin, info.YMin, info.XMax, info.YMax]));

        // The charset: the name of each glyph after .notdef (format 0, section 13)
        var charset = new byte[1 + ((glyphNames.Count - 1) * 2)];
        for (var glyph = 1; glyph < glyphNames.Count; glyph++)
        {
            BinaryPrimitives.WriteUInt16BigEndian(charset.AsSpan(1 + ((glyph - 1) * 2)), (ushort)GetStringId(glyphNames[glyph]));
        }

        // The encoding (section 12): the standard encoding, or supplements that give the name of each code. There are at
        // most 255 supplements: when the font assigns more codes, the glyphs 1, 2, ... that have a code take one of
        // their codes in the format 0 part, which gives codes to the glyphs in their order. Only a font that assigns
        // the 256 codes and whose glyph 1 has no code loses the assignment of its last code.
        byte[]? encoding = null;
        if (!font.HasStandardEncoding)
        {
            var glyphSet = new HashSet<string>(glyphNames, StringComparer.Ordinal);
            var assignments = new List<(int Code, string Name)>();
            for (var code = 0; code < 256; code++)
            {
                if (font.Encoding[code] is { } name && glyphSet.Contains(name))
                {
                    assignments.Add((code, name));
                }
            }

            var mainCodes = new List<byte>();
            if (assignments.Count > 255)
            {
                for (var glyph = 1; glyph < glyphNames.Count && assignments.FindIndex(a => a.Name == glyphNames[glyph]) is var assignment and >= 0; glyph++)
                {
                    mainCodes.Add((byte)assignments[assignment].Code);
                    assignments.RemoveAt(assignment);
                }
            }

            var supplements = assignments.Take(255).ToList();
            var bytes = new List<byte> { 0x80, (byte)mainCodes.Count };
            bytes.AddRange(mainCodes);
            bytes.Add((byte)supplements.Count);
            foreach (var (code, name) in supplements)
            {
                var sid = GetStringId(name);
                bytes.AddRange([(byte)code, (byte)(sid >> 8), (byte)sid]);
            }

            encoding = [.. bytes];
        }

        // The private DICT: the alignment zones and the stems (table 23), in the units of the font
        var privateEntries = new List<(int Operator, double[] Operands)>();
        void AddPrivate(string key, int @operator, double scale, bool isDelta)
        {
            if (!font.PrivateValues.TryGetValue(key, out var values) || values.Length == 0)
                return;

            var operands = new double[values.Length];
            var previous = 0.0;
            for (var i = 0; i < values.Length; i++)
            {
                var value = Math.Round(values[i] * scale, 6, MidpointRounding.AwayFromZero);
                operands[i] = isDelta ? value - previous : value;
                previous = value;
            }

            privateEntries.Add((@operator, isDelta ? operands : operands[..1]));
        }

        var yScale = info.Matrix[3];
        var xScale = info.Matrix[0];
        AddPrivate("BlueValues", 6, yScale, isDelta: true);
        AddPrivate("OtherBlues", 7, yScale, isDelta: true);
        AddPrivate("FamilyBlues", 8, yScale, isDelta: true);
        AddPrivate("FamilyOtherBlues", 9, yScale, isDelta: true);
        AddPrivate("StdHW", 10, yScale, isDelta: false);
        AddPrivate("StdVW", 11, xScale, isDelta: false);
        AddPrivate("BlueScale", Escape + 9, 1, isDelta: false);
        AddPrivate("BlueShift", Escape + 10, yScale, isDelta: false);
        AddPrivate("BlueFuzz", Escape + 11, yScale, isDelta: false);
        AddPrivate("StemSnapH", Escape + 12, yScale, isDelta: true);
        AddPrivate("StemSnapV", Escape + 13, xScale, isDelta: true);
        AddPrivate("ForceBold", Escape + 14, 1, isDelta: false);
        AddPrivate("LanguageGroup", Escape + 17, 1, isDelta: false);
        AddPrivate("ExpansionFactor", Escape + 18, 1, isDelta: false);
        var privateDict = new List<byte>();
        CffSubsetWriter.WriteEntries(privateDict, privateEntries);

        var nameIndex = CffSubsetWriter.WriteIndex([Encoding.Latin1.GetBytes(font.FontName.Length == 0 ? "Untitled" : font.FontName)]);
        var stringIndex = CffSubsetWriter.WriteIndex(strings);
        byte[] globalSubroutines = [0, 0];

        // The CharStrings INDEX (section 5), written in place from the charstrings and their offsets
        var glyphCount = offsets.Length - 1;
        var offsetSize = charstrings.Length + 1 <= 0xFF ? 1 : charstrings.Length + 1 <= 0xFFFF ? 2 : charstrings.Length + 1 <= 0xFFFFFF ? 3 : 4;
        var charStringsIndexLength = 3L + ((glyphCount + 1L) * offsetSize) + charstrings.Length;

        // Every position is written on five bytes: the length of the Top DICT does not depend on them
        byte[] BuildTopDict(int charsetOffset, int encodingOffset, int charStringsOffset, int privateOffset)
        {
            var dict = new List<byte>();
            CffSubsetWriter.WriteEntries(dict, top);
            CffSubsetWriter.WriteFixedInteger(dict, charsetOffset);
            CffSubsetWriter.WriteOperator(dict, 15);
            if (encoding is not null)
            {
                CffSubsetWriter.WriteFixedInteger(dict, encodingOffset);
                CffSubsetWriter.WriteOperator(dict, 16);
            }

            CffSubsetWriter.WriteFixedInteger(dict, charStringsOffset);
            CffSubsetWriter.WriteOperator(dict, 17);
            CffSubsetWriter.WriteFixedInteger(dict, privateDict.Count);
            CffSubsetWriter.WriteFixedInteger(dict, privateOffset);
            CffSubsetWriter.WriteOperator(dict, 18);
            return [.. dict];
        }

        const int HeaderLength = 4;
        var topDictIndexLength = CffSubsetWriter.WriteIndex([BuildTopDict(0, 0, 0, 0)]).Length;
        var charsetOffset = HeaderLength + nameIndex.Length + topDictIndexLength + stringIndex.Length + globalSubroutines.Length;
        var encodingOffset = charsetOffset + charset.Length;
        var charStringsOffset = encodingOffset + (encoding?.Length ?? 0);
        var tableLength = charStringsOffset + charStringsIndexLength + privateDict.Count;
        if (tableLength > limits.MaxFontBytes)
            throw new FontResourceLimitException(FontResourceLimitKind.FontBytes, limits.MaxFontBytes, tableLength);

        var privateOffset = (int)(charStringsOffset + charStringsIndexLength);
        var topDictIndex = CffSubsetWriter.WriteIndex([BuildTopDict(charsetOffset, encodingOffset, charStringsOffset, privateOffset)]);

        var output = new byte[privateOffset + privateDict.Count];
        var position = 0;
        void Append(ReadOnlySpan<byte> bytes)
        {
            bytes.CopyTo(output.AsSpan(position));
            position += bytes.Length;
        }

        Append([1, 0, HeaderLength, 4]);
        Append(nameIndex);
        Append(topDictIndex);
        Append(stringIndex);
        Append(globalSubroutines);
        Append(charset);
        Append(encoding);

        var index = output.AsSpan(position);
        BinaryPrimitives.WriteUInt16BigEndian(index, (ushort)glyphCount);
        index[2] = (byte)offsetSize;
        for (var i = 0; i <= glyphCount; i++)
        {
            var offset = offsets[i] + 1;
            for (var b = 0; b < offsetSize; b++)
            {
                index[3 + (i * offsetSize) + b] = (byte)(offset >> ((offsetSize - 1 - b) * 8));
            }
        }

        position += 3 + ((glyphCount + 1) * offsetSize);
        Append(charstrings);
        Append([.. privateDict]);
        return output;
    }

    /// <summary>Writes the <c>head</c> table (OpenType specification, "head - Font Header Table").</summary>
    private static byte[] WriteHead(FontInfo info)
    {
        var table = new byte[54];
        var span = table.AsSpan();
        BinaryPrimitives.WriteUInt32BigEndian(span, 0x00010000);
        BinaryPrimitives.WriteInt32BigEndian(span[4..], (int)Math.Round(ParseVersion(info.Font.Version) * 65536, MidpointRounding.AwayFromZero));
        BinaryPrimitives.WriteUInt32BigEndian(span[12..], 0x5F0F3CF5);

        // Bit 0: the baseline is at y = 0
        BinaryPrimitives.WriteUInt16BigEndian(span[16..], 0x0001);
        BinaryPrimitives.WriteUInt16BigEndian(span[18..], (ushort)info.UnitsPerEm);
        BinaryPrimitives.WriteInt16BigEndian(span[36..], (short)info.XMin);
        BinaryPrimitives.WriteInt16BigEndian(span[38..], (short)info.YMin);
        BinaryPrimitives.WriteInt16BigEndian(span[40..], (short)info.XMax);
        BinaryPrimitives.WriteInt16BigEndian(span[42..], (short)info.YMax);
        BinaryPrimitives.WriteUInt16BigEndian(span[44..], (ushort)((info.IsBold ? 1 : 0) | (info.IsItalic ? 2 : 0)));
        BinaryPrimitives.WriteUInt16BigEndian(span[46..], 3);
        BinaryPrimitives.WriteInt16BigEndian(span[48..], 2);
        return table;
    }

    /// <summary>Reads the number a version string starts with ("001.003" is 1.003); 1 when it has none.</summary>
    private static double ParseVersion(string? version)
    {
        if (version is null)
            return 1;

        var span = version.AsSpan().Trim();
        var length = 0;
        while (length < span.Length && length < 10 && (char.IsAsciiDigit(span[length]) || span[length] == '.'))
        {
            length++;
        }

        return double.TryParse(span[..length], NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var value) && value < 32768 ? value : 1;
    }

    /// <summary>Writes the <c>hhea</c> table.</summary>
    private static byte[] WriteHhea(FontInfo info)
    {
        var table = new byte[36];
        var span = table.AsSpan();
        BinaryPrimitives.WriteUInt32BigEndian(span, 0x00010000);
        BinaryPrimitives.WriteInt16BigEndian(span[4..], (short)info.Ascender);
        BinaryPrimitives.WriteInt16BigEndian(span[6..], (short)info.Descender);
        BinaryPrimitives.WriteInt16BigEndian(span[8..], (short)info.LineGap);
        BinaryPrimitives.WriteUInt16BigEndian(span[10..], (ushort)info.Advances.Max());

        int? minLeft = null;
        int? minRight = null;
        int? maxExtent = null;
        for (var glyph = 0; glyph < info.Advances.Length; glyph++)
        {
            if (info.Bounds[glyph] is not { } bounds)
                continue;

            var left = info.GetLeftSideBearing(glyph);
            var right = ToInt16(info.Advances[glyph] - Math.Ceiling(bounds.XMax));
            var extent = ToInt16(Math.Ceiling(bounds.XMax));
            minLeft = Math.Min(minLeft ?? left, left);
            minRight = Math.Min(minRight ?? right, right);
            maxExtent = Math.Max(maxExtent ?? extent, extent);
        }

        BinaryPrimitives.WriteInt16BigEndian(span[12..], (short)(minLeft ?? 0));
        BinaryPrimitives.WriteInt16BigEndian(span[14..], (short)(minRight ?? 0));
        BinaryPrimitives.WriteInt16BigEndian(span[16..], (short)(maxExtent ?? 0));

        // The slope of the caret: vertical, or the slant of the italic angle (degrees counterclockwise from vertical)
        var rise = info.UnitsPerEm;
        var run = ToInt16(Math.Tan(-info.Font.ItalicAngle * Math.PI / 180) * rise);
        BinaryPrimitives.WriteInt16BigEndian(span[18..], (short)(run == 0 ? 1 : rise));
        BinaryPrimitives.WriteInt16BigEndian(span[20..], (short)run);
        BinaryPrimitives.WriteUInt16BigEndian(span[34..], (ushort)info.Advances.Length);
        return table;
    }

    /// <summary>Writes the <c>maxp</c> table of a font with CFF outlines (version 0.5).</summary>
    private static byte[] WriteMaxp(int glyphCount)
    {
        var table = new byte[6];
        BinaryPrimitives.WriteUInt32BigEndian(table, 0x00005000);
        BinaryPrimitives.WriteUInt16BigEndian(table.AsSpan(4), (ushort)glyphCount);
        return table;
    }

    /// <summary>Writes the <c>hmtx</c> table: the advance and the left side bearing of every glyph.</summary>
    private static byte[] WriteHmtx(FontInfo info)
    {
        var table = new byte[info.Advances.Length * 4];
        for (var glyph = 0; glyph < info.Advances.Length; glyph++)
        {
            BinaryPrimitives.WriteUInt16BigEndian(table.AsSpan(glyph * 4), (ushort)info.Advances[glyph]);
            BinaryPrimitives.WriteInt16BigEndian(table.AsSpan((glyph * 4) + 2), (short)info.GetLeftSideBearing(glyph));
        }

        return table;
    }

    /// <summary>
    /// Writes the <c>cmap</c> table: a subtable of format 4 for the characters of the Basic Multilingual Plane (platform
    /// 3, encoding 1) and a subtable of format 12 for every character (platform 3, encoding 10).
    /// </summary>
    private static byte[] WriteCmap(SortedDictionary<int, int> characterMap)
    {
        // Runs of consecutive characters mapped to consecutive glyphs
        var groups = new List<(int Start, int End, int Glyph)>();
        foreach (var (codePoint, glyph) in characterMap)
        {
            if (groups.Count > 0 && groups[^1] is var last && last.End + 1 == codePoint && last.Glyph + (codePoint - last.Start) == glyph)
            {
                groups[^1] = (last.Start, codePoint, last.Glyph);
            }
            else
            {
                groups.Add((codePoint, codePoint, glyph));
            }
        }

        var format12 = new byte[16 + (groups.Count * 12)];
        BinaryPrimitives.WriteUInt16BigEndian(format12, 12);
        BinaryPrimitives.WriteUInt32BigEndian(format12.AsSpan(4), (uint)format12.Length);
        BinaryPrimitives.WriteUInt32BigEndian(format12.AsSpan(12), (uint)groups.Count);
        for (var i = 0; i < groups.Count; i++)
        {
            var group = format12.AsSpan(16 + (i * 12));
            BinaryPrimitives.WriteUInt32BigEndian(group, (uint)groups[i].Start);
            BinaryPrimitives.WriteUInt32BigEndian(group[4..], (uint)groups[i].End);
            BinaryPrimitives.WriteUInt32BigEndian(group[8..], (uint)groups[i].Glyph);
        }

        // Format 4: a segment per group of the plane (a group ends before 0xFFFF), then the final segment, with deltas
        var segments = groups.Where(g => g.End < 0xFFFF).Select(g => (Start: g.Start, End: g.End, Delta: g.Glyph - g.Start)).ToList();
        segments.Add((0xFFFF, 0xFFFF, 1));
        var format4Length = 16 + (segments.Count * 8);
        var hasFormat4 = format4Length <= ushort.MaxValue;
        var subtableCount = hasFormat4 ? 2 : 1;
        var headerLength = 4 + (subtableCount * 8);
        var table = new byte[headerLength + (hasFormat4 ? format4Length : 0) + format12.Length];
        var span = table.AsSpan();
        BinaryPrimitives.WriteUInt16BigEndian(span[2..], (ushort)subtableCount);
        var offset = headerLength;
        if (hasFormat4)
        {
            BinaryPrimitives.WriteUInt16BigEndian(span[4..], 3);
            BinaryPrimitives.WriteUInt16BigEndian(span[6..], 1);
            BinaryPrimitives.WriteUInt32BigEndian(span[8..], (uint)offset);
            var subtable = span.Slice(offset, format4Length);
            var segCount = segments.Count;
            var entrySelector = (int)Math.Floor(Math.Log2(segCount));
            var searchRange = 2 << entrySelector;
            BinaryPrimitives.WriteUInt16BigEndian(subtable, 4);
            BinaryPrimitives.WriteUInt16BigEndian(subtable[2..], (ushort)format4Length);
            BinaryPrimitives.WriteUInt16BigEndian(subtable[6..], (ushort)(segCount * 2));
            BinaryPrimitives.WriteUInt16BigEndian(subtable[8..], (ushort)searchRange);
            BinaryPrimitives.WriteUInt16BigEndian(subtable[10..], (ushort)entrySelector);
            BinaryPrimitives.WriteUInt16BigEndian(subtable[12..], (ushort)((segCount * 2) - searchRange));
            for (var i = 0; i < segCount; i++)
            {
                BinaryPrimitives.WriteUInt16BigEndian(subtable[(14 + (i * 2))..], (ushort)segments[i].End);
                BinaryPrimitives.WriteUInt16BigEndian(subtable[(16 + (segCount * 2) + (i * 2))..], (ushort)segments[i].Start);
                BinaryPrimitives.WriteUInt16BigEndian(subtable[(16 + (segCount * 4) + (i * 2))..], (ushort)segments[i].Delta);
            }

            offset += format4Length;
        }

        var record = 4 + ((subtableCount - 1) * 8);
        BinaryPrimitives.WriteUInt16BigEndian(span[record..], 3);
        BinaryPrimitives.WriteUInt16BigEndian(span[(record + 2)..], 10);
        BinaryPrimitives.WriteUInt32BigEndian(span[(record + 4)..], (uint)offset);
        format12.CopyTo(span[offset..]);
        return table;
    }

    /// <summary>
    /// Writes the <c>post</c> table without glyph names (version 3: the names are in the CFF table). The underline
    /// position of OpenType is the top of the line; the one of a Type 1 font is its center.
    /// </summary>
    private static byte[] WritePost(FontInfo info)
    {
        var font = info.Font;
        var table = new byte[32];
        var span = table.AsSpan();
        BinaryPrimitives.WriteUInt32BigEndian(span, 0x00030000);
        BinaryPrimitives.WriteInt32BigEndian(span[4..], (int)Math.Round(Math.Clamp(font.ItalicAngle, -180, 180) * 65536, MidpointRounding.AwayFromZero));
        var thickness = font.UnderlineThickness * info.Matrix[3];
        BinaryPrimitives.WriteInt16BigEndian(span[8..], (short)ToInt16((font.UnderlinePosition * info.Matrix[3]) + (thickness / 2)));
        BinaryPrimitives.WriteInt16BigEndian(span[10..], (short)ToInt16(thickness));
        BinaryPrimitives.WriteUInt32BigEndian(span[12..], font.IsFixedPitch ? 1u : 0u);
        return table;
    }

    /// <summary>
    /// Writes the <c>name</c> table, in English for the Windows platform: the family and the subfamily of the four
    /// styles of a family (regular, bold, italic, bold italic), and the typographic family and subfamily for the
    /// other weights.
    /// </summary>
    private static byte[] WriteName(FontInfo info)
    {
        var font = info.Font;
        var family = font.FamilyName ?? font.FullName ?? font.FontName;
        var styleSuffix = info.IsItalic ? "Italic" : null;
        var records = new SortedDictionary<int, string>();
        void Add(int nameId, string? value)
        {
            if (!string.IsNullOrEmpty(value))
            {
                records[nameId] = value;
            }
        }

        Add(0, font.Copyright ?? font.Notice);
        if (info.IsRegularWeight)
        {
            Add(NameTable.FamilyName, family);
            Add(NameTable.SubfamilyName, (info.IsBold, info.IsItalic) switch
            {
                (true, true) => "Bold Italic",
                (true, false) => "Bold",
                (false, true) => "Italic",
                _ => "Regular",
            });
        }
        else
        {
            Add(NameTable.FamilyName, family + " " + info.WeightName);
            Add(NameTable.SubfamilyName, info.IsItalic ? "Italic" : "Regular");
            Add(NameTable.TypographicFamilyName, family);
            Add(NameTable.TypographicSubfamilyName, styleSuffix is null ? info.WeightName : info.WeightName + " " + styleSuffix);
        }

        Add(3, font.FontName);
        Add(NameTable.FullName, font.FullName ?? font.FontName);
        Add(5, font.Version is null ? null : "Version " + font.Version);
        Add(NameTable.PostScriptName, font.FontName);

        var strings = new List<byte>();
        var table = new List<byte>();
        void WriteUInt16(int value) => table.AddRange([(byte)(value >> 8), (byte)value]);
        WriteUInt16(0);
        WriteUInt16(records.Count);
        WriteUInt16(6 + (records.Count * 12));
        foreach (var (nameId, value) in records)
        {
            var bytes = Encoding.BigEndianUnicode.GetBytes(value.Length > 4096 ? value[..4096] : value);
            WriteUInt16(3);
            WriteUInt16(1);
            WriteUInt16(0x0409);
            WriteUInt16(nameId);
            WriteUInt16(bytes.Length);
            WriteUInt16(strings.Count);
            strings.AddRange(bytes);
        }

        return [.. table, .. strings];
    }

    /// <summary>Writes the <c>OS/2</c> table (version 4), whose metrics are the ones of <c>hhea</c>.</summary>
    private static byte[] WriteOs2(FontInfo info)
    {
        var table = new byte[96];
        var span = table.AsSpan();
        var em = info.UnitsPerEm;
        var advances = info.Advances.Where(a => a > 0).ToList();
        BinaryPrimitives.WriteUInt16BigEndian(span, 4);
        BinaryPrimitives.WriteInt16BigEndian(span[2..], (short)(advances.Count == 0 ? 0 : ToInt16(advances.Average())));
        BinaryPrimitives.WriteUInt16BigEndian(span[4..], (ushort)info.WeightClass);
        BinaryPrimitives.WriteUInt16BigEndian(span[6..], 5);

        // The subscripts and the superscripts: the usual values, in proportion with the em
        BinaryPrimitives.WriteInt16BigEndian(span[10..], (short)ToInt16(em * 0.65));
        BinaryPrimitives.WriteInt16BigEndian(span[12..], (short)ToInt16(em * 0.6));
        BinaryPrimitives.WriteInt16BigEndian(span[16..], (short)ToInt16(em * 0.075));
        BinaryPrimitives.WriteInt16BigEndian(span[18..], (short)ToInt16(em * 0.65));
        BinaryPrimitives.WriteInt16BigEndian(span[20..], (short)ToInt16(em * 0.6));
        BinaryPrimitives.WriteInt16BigEndian(span[24..], (short)ToInt16(em * 0.35));

        // The strikeout: as thick as the underline, at half the x-height
        BinaryPrimitives.WriteInt16BigEndian(span[26..], (short)ToInt16(info.Font.UnderlineThickness * info.Matrix[3]));
        BinaryPrimitives.WriteInt16BigEndian(span[28..], (short)ToInt16(info.XHeight > 0 ? info.XHeight / 2.0 : em * 0.25));
        "    "u8.CopyTo(span[58..]);

        // fsSelection: italic, bold or regular, and the typographic metrics are the metrics of the font
        var selection = (info.IsItalic ? 0x0001 : 0) | (info.IsBold ? 0x0020 : 0) | (!info.IsItalic && !info.IsBold ? 0x0040 : 0) | 0x0080;
        BinaryPrimitives.WriteUInt16BigEndian(span[62..], (ushort)selection);
        var first = info.CharacterMap.Count == 0 ? 0 : info.CharacterMap.Keys.First();
        var last = info.CharacterMap.Count == 0 ? 0 : info.CharacterMap.Keys.Last();
        BinaryPrimitives.WriteUInt16BigEndian(span[64..], (ushort)Math.Min(first, 0xFFFF));
        BinaryPrimitives.WriteUInt16BigEndian(span[66..], (ushort)Math.Min(last, 0xFFFF));
        BinaryPrimitives.WriteInt16BigEndian(span[68..], (short)info.Ascender);
        BinaryPrimitives.WriteInt16BigEndian(span[70..], (short)info.Descender);
        BinaryPrimitives.WriteInt16BigEndian(span[72..], (short)info.LineGap);
        BinaryPrimitives.WriteUInt16BigEndian(span[74..], (ushort)Math.Max(0, info.YMax));
        BinaryPrimitives.WriteUInt16BigEndian(span[76..], (ushort)Math.Max(0, -info.YMin));
        BinaryPrimitives.WriteInt16BigEndian(span[86..], (short)info.XHeight);
        BinaryPrimitives.WriteInt16BigEndian(span[88..], (short)info.CapHeight);
        BinaryPrimitives.WriteUInt16BigEndian(span[92..], 32);
        return table;
    }

    /// <summary>
    /// Writes the kerning pairs of an AFM file as a <c>kern</c> table of version 0: horizontal subtables of format 0,
    /// each one of at most 10,920 pairs (the length of a subtable is 16 bits). A pair of a glyph the font does not have
    /// is ignored; the first of two pairs of the same glyphs applies.
    /// </summary>
    private static byte[]? WriteKern(AfmFontMetrics metrics, List<string> glyphNames, double scale)
    {
        const int MaxPairsPerSubtable = 10920;
        var glyphs = new Dictionary<string, int>(glyphNames.Count, StringComparer.Ordinal);
        for (var i = 0; i < glyphNames.Count; i++)
        {
            glyphs.TryAdd(glyphNames[i], i);
        }

        var pairs = new SortedDictionary<uint, short>();
        foreach (var pair in metrics.KerningPairs)
        {
            if (pair.First is { } first && pair.Second is { } second && glyphs.TryGetValue(first, out var left) && glyphs.TryGetValue(second, out var right))
            {
                var value = ToInt16(pair.X * scale);
                if (value != 0)
                {
                    pairs.TryAdd(((uint)left << 16) | (uint)right, (short)value);
                }
            }
        }

        if (pairs.Count == 0)
            return null;

        var all = pairs.ToList();
        var subtableCount = (all.Count + MaxPairsPerSubtable - 1) / MaxPairsPerSubtable;
        var table = new List<byte>();
        void WriteUInt16(int value) => table.AddRange([(byte)(value >> 8), (byte)value]);
        WriteUInt16(0);
        WriteUInt16(subtableCount);
        for (var i = 0; i < subtableCount; i++)
        {
            var chunk = all.Skip(i * MaxPairsPerSubtable).Take(MaxPairsPerSubtable).ToList();
            var entrySelector = (int)Math.Floor(Math.Log2(chunk.Count));
            var searchRange = 6 << entrySelector;
            WriteUInt16(0);
            WriteUInt16(14 + (chunk.Count * 6));

            // Coverage: horizontal kerning values, format 0
            WriteUInt16(0x0001);
            WriteUInt16(chunk.Count);
            WriteUInt16(searchRange);
            WriteUInt16(entrySelector);
            WriteUInt16((chunk.Count * 6) - searchRange);
            foreach (var (key, value) in chunk)
            {
                WriteUInt16((int)(key >> 16));
                WriteUInt16((int)(key & 0xFFFF));
                WriteUInt16(value);
            }
        }

        return [.. table];
    }
}
