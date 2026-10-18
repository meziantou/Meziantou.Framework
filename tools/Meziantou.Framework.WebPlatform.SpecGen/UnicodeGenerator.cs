using System.Globalization;
using System.Text;

namespace SpecGen;

/// <summary>
/// Generates the Unicode data needed by UTS #46 (IDNA): the IDNA mapping table, NFC normalization data,
/// the General_Category=Mark property and Joining_Type, and the ID_Start and ID_Continue properties (identifiers of the
/// URL Pattern names); the bidi properties (<see cref="GenerateBidi"/>) and the text segmentation properties of the layout.
/// The data comes from the Unicode Character Database so results do not depend on the OS or ICU version.
/// </summary>
internal static class UnicodeGenerator
{
    public static void Generate(GeneratorContext context)
    {
        var writer = new CodeWriter("tests/Meziantou.Framework.WebPlatform.SpecData/unicode/*.txt");
        writer.Line("namespace Meziantou.Framework.WebPlatform.Unicode;").Line();

        GenerateIdnaMapping(context, writer);
        GenerateNormalization(context, writer);
        GenerateMarks(context, writer);
        GenerateJoiningType(context, writer);
        GenerateIdentifiers(context, writer);

        context.WriteFile(context.Source("Unicode", "UnicodeData.g.cs"), writer.ToString());

        GenerateTextShaping(context);
        GenerateScripts(context);
        GenerateTextSegmentation(context);
        GenerateBidi(context);
    }

    /// <summary>
    /// Generates the Script and Script_Extensions properties (UAX #24, https://www.unicode.org/reports/tr24/): the script of
    /// each code point (a two-stage lookup table of <c>UnicodeScript</c>), the ISO 15924 code of each script (from
    /// PropertyValueAliases.txt, used for the OpenType script tags) and the Script_Extensions sets of the code points that
    /// are used by several scripts. The text shaping splits the text into runs of one script with them.
    /// </summary>
    private static void GenerateScripts(GeneratorContext context)
    {
        var writer = new CodeWriter("tests/Meziantou.Framework.WebPlatform.SpecData/unicode/*.txt");
        writer.Line("namespace Meziantou.Framework.WebPlatform.Unicode;").Line();

        // The scripts: Unknown, Common and Inherited first, then the other scripts by ISO 15924 code
        var aliasesPath = context.SpecData("unicode", "PropertyValueAliases.txt");
        var scripts = new List<(string Code, string Name)>();
        foreach (var rawLine in File.ReadLines(aliasesPath))
        {
            var hash = rawLine.IndexOf('#', StringComparison.Ordinal);
            var line = hash >= 0 ? rawLine[..hash] : rawLine;
            var fields = line.Split(';').Select(f => f.Trim()).ToArray();
            if (fields is ["sc", var code, var name, ..])
            {
                scripts.Add((code, name));
            }
        }

        string[] first = ["Zzzz", "Zyyy", "Zinh"];
        scripts = [.. scripts.OrderBy(s => Array.IndexOf(first, s.Code) is var index and >= 0 ? index : first.Length).ThenBy(s => s.Code, StringComparer.Ordinal)];
        if (scripts.Count > 255)
            throw new InvalidOperationException("Too many scripts for a byte");

        var scriptsPath = context.SpecData("unicode", "Scripts.txt");
        writer.Line($"/// <summary>The values of the Script property (https://www.unicode.org/reports/tr24/), Unicode {Version(scriptsPath)}: Unknown, Common and Inherited, then the scripts in the order of their ISO 15924 code.</summary>");
        writer.Line("internal enum UnicodeScript : byte");
        writer.Line("{");
        foreach (var (code, name) in scripts)
        {
            writer.Line($"    /// <summary>{name} ({code}).</summary>");
            writer.Line("    " + name.Replace("_", "", StringComparison.Ordinal) + ",");
        }

        writer.Line("}").Line();

        var names = scripts.Select(s => s.Name).ToArray();
        var values = ReadEnumeratedProperty(scriptsPath, names);
        WriteTrie(writer, "ScriptData", "The Script property of each code point (<see cref=\"UnicodeScript\"/>).", values);

        writer.Line("/// <summary>The ISO 15924 code of each <see cref=\"UnicodeScript\"/>, as a big-endian tag (e.g. <c>Latn</c> is 0x4C61746E).</summary>");
        using (writer.Block("internal static partial class ScriptCodes"))
        {
            WriteArray(writer, "Codes", "uint", scripts.Select(s => ((uint)s.Code[0] << 24) | ((uint)s.Code[1] << 16) | ((uint)s.Code[2] << 8) | s.Code[3]));
        }

        // Script_Extensions: the ranges of code points whose value is not their Script, and their sets of scripts
        var extensionsPath = context.SpecData("unicode", "ScriptExtensions.txt");
        var codes = scripts.Select(s => s.Code).ToList();
        var sets = new List<string>();
        var ranges = new List<(int Start, int End, int Set)>();
        foreach (var (start, end, fields) in ReadDataFile(extensionsPath).OrderBy(r => r.Start))
        {
            var set = string.Join(' ', fields[1].Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(code => codes.IndexOf(code) is var index and >= 0 ? index : throw new InvalidOperationException($"Unknown script {code}")).Order());
            var setIndex = sets.IndexOf(set);
            if (setIndex < 0)
            {
                setIndex = sets.Count;
                sets.Add(set);
            }

            if (ranges.Count > 0 && ranges[^1].End == start - 1 && ranges[^1].Set == setIndex)
            {
                ranges[^1] = (ranges[^1].Start, end, setIndex);
            }
            else
            {
                ranges.Add((start, end, setIndex));
            }
        }

        var setOffsets = new List<int>();
        var setValues = new List<int>();
        foreach (var set in sets)
        {
            setOffsets.Add(setValues.Count);
            setValues.AddRange(set.Split(' ').Select(int.Parse));
        }

        setOffsets.Add(setValues.Count);
        writer.Line();
        writer.Line($"/// <summary>The Script_Extensions property (Unicode {Version(extensionsPath)}) of the code points whose value is not their Script property: the sets of <see cref=\"UnicodeScript\"/> of the ranges of code points.</summary>");
        using (writer.Block("internal static partial class ScriptExtensionsData"))
        {
            WriteArray(writer, "RangeStarts", "int", ranges.Select(r => r.Start));
            WriteArray(writer, "RangeEnds", "int", ranges.Select(r => r.End));
            WriteArray(writer, "RangeSets", "byte", ranges.Select(r => r.Set));
            writer.Line("/// <summary>The scripts of set i are SetValues[SetOffsets[i]..SetOffsets[i + 1]].</summary>");
            WriteArray(writer, "SetOffsets", "ushort", setOffsets);
            WriteArray(writer, "SetValues", "byte", setValues);
        }

        context.WriteFile(context.Source("Unicode", "ScriptData.g.cs"), writer.ToString());
    }

    /// <summary>
    /// Writes the joining types of the cursive scripts for the shaping (ArabicShaping.txt, as HarfBuzz's
    /// gen-arabic-table.py does): the Joining_Type of the listed code points, the Syriac joining groups ALAPH and DALATH RISH
    /// (which have their own forms), and X for the other code points (T for the marks and the format characters, else U).
    /// Also writes the scripts that have joining data (the scripts of the characters that join, as gen-arabic-joining-list.py).
    /// </summary>
    private static void GenerateArabicJoining(GeneratorContext context, CodeWriter writer)
    {
        var path = context.SpecData("unicode", "ArabicShaping.txt");
        var values = new byte[0x110000];
        Array.Fill(values, (byte)7);
        var joining = new List<int>();
        foreach (var (start, _, fields) in ReadDataFile(path))
        {
            var value = fields[3] switch
            {
                "ALAPH" => 4,
                "DALATH RISH" => 5,
                _ => fields[2] switch
                {
                    "U" => 0,
                    "L" => 1,
                    "R" => 2,
                    "D" or "C" => 3,
                    "T" => 6,
                    _ => throw new InvalidOperationException($"Unknown joining type {fields[2]}"),
                },
            };

            values[start] = (byte)value;
            if (fields[2] is not ("T" or "U"))
            {
                joining.Add(start);
            }
        }

        writer.Line();
        writer.Line($"/// <summary>The joining types of the shaping of the cursive scripts (ArabicShaping.txt, Unicode {Version(path)}), with the joining groups of Syriac that have their own forms.</summary>");
        writer.Line("internal enum ArabicJoiningType : byte");
        writer.Line("{");
        writer.Line("    /// <summary>Non_Joining.</summary>");
        writer.Line("    U,");
        writer.Line("    /// <summary>Left_Joining.</summary>");
        writer.Line("    L,");
        writer.Line("    /// <summary>Right_Joining.</summary>");
        writer.Line("    R,");
        writer.Line("    /// <summary>Dual_Joining and Join_Causing.</summary>");
        writer.Line("    D,");
        writer.Line("    /// <summary>Right_Joining, Joining_Group=ALAPH.</summary>");
        writer.Line("    Alaph,");
        writer.Line("    /// <summary>Right_Joining, Joining_Group=DALATH_RISH.</summary>");
        writer.Line("    DalathRish,");
        writer.Line("    /// <summary>Transparent.</summary>");
        writer.Line("    T,");
        writer.Line("    /// <summary>Not listed: T for the marks (Mn, Me) and the format characters (Cf), else U.</summary>");
        writer.Line("    X,");
        writer.Line("}").Line();
        WriteRangeTable(writer, "ArabicJoiningData", "The <see cref=\"ArabicJoiningType\"/> of each code point.", values);

        // The scripts of the joining characters (except Common and Inherited)
        var scriptsPath = context.SpecData("unicode", "Scripts.txt");
        var scriptOfCodePoint = new Dictionary<int, string>();
        foreach (var (start, end, fields) in ReadDataFile(scriptsPath))
        {
            for (var codePoint = start; codePoint <= end; codePoint++)
            {
                scriptOfCodePoint[codePoint] = fields[1];
            }
        }

        var joiningScripts = joining.Select(codePoint => scriptOfCodePoint.GetValueOrDefault(codePoint, "Unknown")).Where(script => script is not ("Common" or "Inherited" or "Unknown")).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();
        writer.Line("/// <summary>The scripts that have joining characters in ArabicShaping.txt (the shapers apply the joining features to them).</summary>");
        using (writer.Block("internal static partial class ArabicJoiningScripts"))
        {
            writer.Line("public static bool HasJoining(UnicodeScript script) => script is " + string.Join(" or ", joiningScripts.Select(script => "UnicodeScript." + script.Replace("_", "", StringComparison.Ordinal))) + ";");
        }
    }

    /// <summary>
    /// Generates the properties used by the text shaping of the layout: Vertical_Orientation (UAX #50,
    /// https://www.unicode.org/reports/tr50/), the orientation of the characters in vertical text for
    /// <c>text-orientation: mixed</c>, and Default_Ignorable_Code_Point (the invisible characters that have no glyph).
    /// </summary>
    private static void GenerateTextShaping(GeneratorContext context)
    {
        var writer = new CodeWriter("tests/Meziantou.Framework.WebPlatform.SpecData/unicode/*.txt");
        writer.Line("namespace Meziantou.Framework.WebPlatform.Unicode;").Line();

        var path = context.SpecData("unicode", "VerticalOrientation.txt");
        string[] names = ["R", "U", "Tr", "Tu"];
        var values = new byte[0x110000];
        foreach (var (start, end, fields) in ReadDataFile(path))
        {
            var index = Array.IndexOf(names, fields[1]);
            if (index < 0)
                throw new InvalidOperationException($"Unknown vertical orientation {fields[1]}");

            Array.Fill(values, (byte)index, start, end - start + 1);
        }

        writer.Line($"/// <summary>Vertical_Orientation property values (https://www.unicode.org/reports/tr50/), Unicode {Version(path)}.</summary>");
        writer.Line("internal enum VerticalOrientation : byte");
        writer.Line("{");
        writer.Line("    /// <summary>Rotated 90 degrees clockwise (displayed sideways).</summary>");
        writer.Line("    R,");
        writer.Line("    /// <summary>Upright.</summary>");
        writer.Line("    U,");
        writer.Line("    /// <summary>Transformed (a vertical alternate glyph), else rotated.</summary>");
        writer.Line("    Tr,");
        writer.Line("    /// <summary>Transformed (a vertical alternate glyph), else upright.</summary>");
        writer.Line("    Tu,");
        writer.Line("}").Line();
        WriteRangeTable(writer, "VerticalOrientationData", "Vertical_Orientation of each code point (<see cref=\"VerticalOrientation\"/>).", values);

        var corePath = context.SpecData("unicode", "DerivedCoreProperties.txt");
        var ranges = new List<(int Start, int End)>();
        foreach (var (start, end, _) in ReadDataFile(corePath).Where(r => r.Fields[1] == "Default_Ignorable_Code_Point").OrderBy(r => r.Start))
        {
            if (ranges.Count > 0 && ranges[^1].End == start - 1)
            {
                ranges[^1] = (ranges[^1].Start, end);
            }
            else
            {
                ranges.Add((start, end));
            }
        }

        writer.Line($"/// <summary>Code points with the Default_Ignorable_Code_Point property (Unicode {Version(corePath)}).</summary>");
        using (writer.Block("internal static partial class DefaultIgnorableData"))
        {
            WriteArray(writer, "RangeStarts", "int", ranges.Select(r => r.Start));
            WriteArray(writer, "RangeEnds", "int", ranges.Select(r => r.End));
        }

        // The Han script (the ideographs of text-autospace and the CJK justification opportunities, https://drafts.csswg.org/css-text-4/#text-autospace-property)
        var scriptsPath = context.SpecData("unicode", "Scripts.txt");
        var han = new List<(int Start, int End)>();
        foreach (var (start, end, _) in ReadDataFile(scriptsPath).Where(r => r.Fields[1] == "Han").OrderBy(r => r.Start))
        {
            if (han.Count > 0 && han[^1].End == start - 1)
            {
                han[^1] = (han[^1].Start, end);
            }
            else
            {
                han.Add((start, end));
            }
        }

        writer.Line();
        writer.Line($"/// <summary>Code points with the Script property Han (Unicode {Version(scriptsPath)}).</summary>");
        using (writer.Block("internal static partial class HanScriptData"))
        {
            WriteArray(writer, "RangeStarts", "int", han.Select(r => r.Start));
            WriteArray(writer, "RangeEnds", "int", han.Select(r => r.End));
        }

        GenerateArabicJoining(context, writer);
        GenerateSpecialCasing(context, writer);
        GenerateEmojiPresentation(context, writer);

        context.WriteFile(context.Source("Unicode", "TextShapingData.g.cs"), writer.ToString());
    }

    /// <summary>
    /// Writes the emoji properties of <c>font-variant-emoji</c> (https://drafts.csswg.org/css-fonts-4/#font-variant-emoji-prop):
    /// the Emoji property (the emoji characters of the emoji presentation sequences, UTS #51 ED-3 and ED-9a, which are the
    /// Emoji Presentation Participating Code Points of Chrome) and the Emoji_Presentation property (emoji-data.txt).
    /// </summary>
    private static void GenerateEmojiPresentation(GeneratorContext context, CodeWriter writer)
    {
        var path = context.SpecData("unicode", "emoji-data.txt");
        writer.Line();
        writer.Line($"/// <summary>The Emoji and Emoji_Presentation properties (emoji-data.txt, Unicode {Version(path)}), as sorted ranges.</summary>");
        using (writer.Block("internal static partial class EmojiPresentationData"))
        {
            foreach (var (property, name) in (ReadOnlySpan<(string, string)>)[("Emoji", "Emoji"), ("Emoji_Presentation", "Presentation")])
            {
                var ranges = new List<(int Start, int End)>();
                foreach (var (start, end, _) in ReadDataFile(path).Where(r => r.Fields[1] == property).OrderBy(r => r.Start))
                {
                    if (ranges.Count > 0 && ranges[^1].End == start - 1)
                    {
                        ranges[^1] = (ranges[^1].Start, end);
                    }
                    else
                    {
                        ranges.Add((start, end));
                    }
                }

                WriteArray(writer, name + "RangeStarts", "int", ranges.Select(r => r.Start));
                WriteArray(writer, name + "RangeEnds", "int", ranges.Select(r => r.End));
            }
        }
    }

    /// <summary>
    /// Writes the unconditional full case mappings of SpecialCasing.txt (the mappings to several characters, e.g. <c>ß</c> to
    /// <c>SS</c> and the Latin ligatures, and <c>İ</c> to <c>i̇</c>): <c>text-transform</c> and the synthesized small capitals
    /// use them (https://drafts.csswg.org/css-text-4/#text-transform-property). The mappings that depend on the language or
    /// on the context (Turkish, Lithuanian, the final sigma) are not included.
    /// </summary>
    private static void GenerateSpecialCasing(GeneratorContext context, CodeWriter writer)
    {
        var path = context.SpecData("unicode", "SpecialCasing.txt");
        var upper = new SortedDictionary<int, int[]>();
        var title = new SortedDictionary<int, int[]>();
        var lower = new SortedDictionary<int, int[]>();
        foreach (var (start, _, fields) in ReadDataFile(path))
        {
            // code; lower; title; upper; (condition_list;)?
            if (fields.Length > 5 && fields[4].Length > 0)
                continue;

            var lowerMapping = ParseCodePoints(fields[1]);
            var titleMapping = ParseCodePoints(fields[2]);
            var upperMapping = ParseCodePoints(fields[3]);
            if (lowerMapping is not [var l] || l != start)
            {
                lower[start] = lowerMapping;
            }

            if (upperMapping is not [var u] || u != start)
            {
                upper[start] = upperMapping;
            }

            // The titlecase mappings that are not the uppercase mapping (e.g. ﬁ → Fi; the others are the uppercase mappings)
            if (!titleMapping.SequenceEqual(upperMapping))
            {
                title[start] = titleMapping;
            }
        }

        writer.Line();
        writer.Line($"/// <summary>The unconditional full case mappings of SpecialCasing.txt (Unicode {Version(path)}): the code points, and the UTF-16 mapping of each one (from <c>Starts[i]</c> to <c>Starts[i + 1]</c>).</summary>");
        using (writer.Block("internal static partial class SpecialCasingData"))
        {
            WriteMappings("Uppercase", upper);
            WriteMappings("Titlecase", title);
            WriteMappings("Lowercase", lower);
        }

        void WriteMappings(string name, SortedDictionary<int, int[]> mappings)
        {
            var chars = new List<int>();
            var starts = new List<int>();
            foreach (var mapping in mappings.Values)
            {
                starts.Add(chars.Count);
                foreach (var codePoint in mapping)
                {
                    chars.AddRange(char.ConvertFromUtf32(codePoint).Select(c => (int)c));
                }
            }

            starts.Add(chars.Count);
            WriteArray(writer, name + "CodePoints", "int", mappings.Keys);
            WriteArray(writer, name + "Starts", "ushort", starts.Select(start => (ushort)start));
            WriteArray(writer, name + "Chars", "ushort", chars.Select(c => (ushort)c));
        }

        static int[] ParseCodePoints(string value) => [.. value.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(item => int.Parse(item, NumberStyles.HexNumber, CultureInfo.InvariantCulture))];
    }

    // Names of the Line_Break property values (UAX #14), in the order of the generated enum
    private static readonly string[] LineBreakClasses =
    [
        "BK", "CR", "LF", "CM", "NL", "SG", "WJ", "ZW", "GL", "SP", "ZWJ", "B2", "BA", "BB", "HY", "HH", "CB", "CL", "CP", "EX", "IN", "NS",
        "OP", "QU", "IS", "NU", "PO", "PR", "SY", "AI", "AK", "AL", "AP", "AS", "CJ", "EB", "EM", "H2", "H3", "HL", "ID", "JL", "JV", "JT",
        "RI", "SA", "VF", "VI", "XX",
    ];

    private static readonly string[] EastAsianWidths = ["N", "A", "H", "F", "Na", "W"];

    private static readonly string[] GraphemeClusterBreaks = ["Other", "CR", "LF", "Control", "Extend", "ZWJ", "Regional_Indicator", "Prepend", "SpacingMark", "L", "V", "T", "LV", "LVT"];

    private static readonly string[] IndicConjunctBreaks = ["None", "Linker", "Consonant", "Extend"];

    private static readonly string[] WordBreaks = ["Other", "CR", "LF", "Newline", "Extend", "ZWJ", "Regional_Indicator", "Format", "Katakana", "Hebrew_Letter", "ALetter", "Single_Quote", "Double_Quote", "MidNumLet", "MidLetter", "MidNum", "Numeric", "ExtendNumLet", "WSegSpace"];

    // General_Category short names in the order of System.Globalization.UnicodeCategory
    private static readonly string[] GeneralCategories =
    [
        "Lu", "Ll", "Lt", "Lm", "Lo", "Mn", "Mc", "Me", "Nd", "Nl", "No", "Zs", "Zl", "Zp", "Cc", "Cf", "Cs", "Co", "Pc", "Pd", "Ps", "Pe", "Pi",
        "Pf", "Po", "Sm", "Sc", "Sk", "So", "Cn",
    ];

    /// <summary>
    /// Generates the properties used by the text segmentation algorithms of the layout: Line_Break and East_Asian_Width (UAX #14,
    /// UAX #11), Grapheme_Cluster_Break, Indic_Conjunct_Break, Word_Break and Extended_Pictographic (UAX #29), General_Category,
    /// and Script=Hangul (CSS Text segment break transformation, word boundaries). Each property is a two-stage lookup table (<see cref="WriteTrie"/>),
    /// which is faster than the binary search of the range tables for the properties read for every character of a text.
    /// </summary>
    private static void GenerateTextSegmentation(GeneratorContext context)
    {
        var writer = new CodeWriter("tests/Meziantou.Framework.WebPlatform.SpecData/unicode/*.txt");
        writer.Line("namespace Meziantou.Framework.WebPlatform.Unicode;").Line();

        var lineBreakPath = context.SpecData("unicode", "LineBreak.txt");
        var lineBreak = ReadEnumeratedProperty(lineBreakPath, LineBreakClasses);
        WriteEnum(writer, "LineBreakClass", $"Line_Break property values (https://www.unicode.org/reports/tr14/), Unicode {Version(lineBreakPath)}.", LineBreakClasses);
        WriteTrie(writer, "LineBreakData", "Line_Break of each code point (<see cref=\"LineBreakClass\"/>).", lineBreak);

        var eastAsianWidth = ReadEnumeratedProperty(context.SpecData("unicode", "EastAsianWidth.txt"), EastAsianWidths);
        WriteEnum(writer, "EastAsianWidth", "East_Asian_Width property values (https://www.unicode.org/reports/tr11/).", EastAsianWidths);
        WriteTrie(writer, "EastAsianWidthData", "East_Asian_Width of each code point (<see cref=\"EastAsianWidth\"/>).", eastAsianWidth);

        var generalCategory = ReadEnumeratedProperty(context.SpecData("unicode", "DerivedGeneralCategory.txt"), GeneralCategories);
        WriteTrie(writer, "GeneralCategoryData", "General_Category of each code point, as a <see cref=\"System.Globalization.UnicodeCategory\"/>.", generalCategory);

        // Grapheme_Cluster_Break (bits 0-3), Indic_Conjunct_Break (bits 4-5), Extended_Pictographic (bit 6) and Script=Hangul (bit 7)
        var graphemeClusterBreak = ReadEnumeratedProperty(context.SpecData("unicode", "GraphemeBreakProperty.txt"), GraphemeClusterBreaks);
        var indicConjunctBreak = ReadEnumeratedProperty(context.SpecData("unicode", "DerivedCoreProperties.txt"), IndicConjunctBreaks, property: "InCB");
        var extendedPictographic = ReadBinaryProperty(context.SpecData("unicode", "emoji-data.txt"), "Extended_Pictographic");
        var hangul = ReadBinaryProperty(context.SpecData("unicode", "Scripts.txt"), "Hangul");
        var segmentation = new byte[0x110000];
        for (var codePoint = 0; codePoint < segmentation.Length; codePoint++)
        {
            segmentation[codePoint] = (byte)(graphemeClusterBreak[codePoint] | (indicConjunctBreak[codePoint] << 4) | (extendedPictographic[codePoint] << 6) | (hangul[codePoint] << 7));
        }

        WriteEnum(writer, "GraphemeClusterBreak", "Grapheme_Cluster_Break property values (https://www.unicode.org/reports/tr29/#Grapheme_Cluster_Break_Property_Values).", GraphemeClusterBreaks);
        WriteEnum(writer, "IndicConjunctBreak", "Indic_Conjunct_Break property values (https://www.unicode.org/reports/tr44/#Indic_Conjunct_Break).", IndicConjunctBreaks);
        WriteTrie(writer, "SegmentationData", "Grapheme_Cluster_Break (bits 0-3), Indic_Conjunct_Break (bits 4-5), Extended_Pictographic (bit 6) and Script=Hangul (bit 7) of each code point.", segmentation);

        // Word_Break (bits 0-4) and Extended_Pictographic (bit 5)
        var wordBreak = ReadEnumeratedProperty(context.SpecData("unicode", "WordBreakProperty.txt"), WordBreaks);
        var words = new byte[0x110000];
        for (var codePoint = 0; codePoint < words.Length; codePoint++)
        {
            words[codePoint] = (byte)(wordBreak[codePoint] | (extendedPictographic[codePoint] << 5));
        }

        WriteEnum(writer, "WordBreak", "Word_Break property values (https://www.unicode.org/reports/tr29/#Word_Boundaries).", WordBreaks);
        WriteTrie(writer, "WordBreakData", "Word_Break (bits 0-4) and Extended_Pictographic (bit 5) of each code point.", words);

        context.WriteFile(context.Source("Unicode", "TextSegmentationData.g.cs"), writer.ToString());
    }

    /// <summary>
    /// Reads an enumerated property of a UCD file (<c>code points; value</c>, or <c>code points; property; value</c> when
    /// <paramref name="property"/> is set) into the index of the value of each code point in <paramref name="names"/>. The
    /// <c># @missing</c> lines define the default values; long value names are matched without their underscores.
    /// </summary>
    private static byte[] ReadEnumeratedProperty(string path, string[] names, string? property = null)
    {
        int IndexOf(string value)
        {
            for (var i = 0; i < names.Length; i++)
            {
                if (string.Equals(names[i].Replace("_", "", StringComparison.Ordinal), value.Replace("_", "", StringComparison.Ordinal), StringComparison.Ordinal))
                    return i;
            }

            throw new InvalidOperationException($"Unknown property value '{value}' in {Path.GetFileName(path)}");
        }

        var values = new byte[0x110000];
        var valueField = property is null ? 1 : 2;
        foreach (var line in File.ReadLines(path))
        {
            const string Prefix = "# @missing:";
            if (!line.StartsWith(Prefix, StringComparison.Ordinal))
                continue;

            var fields = line[Prefix.Length..].Split(';').Select(f => f.Trim()).ToArray();
            if (property is not null && fields[1] != property)
                continue;

            var (start, end) = ParseRange(fields[0]);
            Array.Fill(values, (byte)IndexOf(fields[valueField]), start, end - start + 1);
        }

        foreach (var (start, end, fields) in ReadDataFile(path))
        {
            if (property is not null && fields[1] != property)
                continue;

            Array.Fill(values, (byte)IndexOf(fields[valueField]), start, end - start + 1);
        }

        return values;
    }

    /// <summary>Reads a binary property (<c>code points; property</c>): 1 for the code points that have it, 0 otherwise.</summary>
    private static byte[] ReadBinaryProperty(string path, string property)
    {
        var values = new byte[0x110000];
        foreach (var (start, end, fields) in ReadDataFile(path))
        {
            if (fields[1] == property)
            {
                Array.Fill(values, (byte)1, start, end - start + 1);
            }
        }

        return values;
    }

    private static void WriteEnum(CodeWriter writer, string name, string summary, IEnumerable<string> values)
    {
        writer.Line($"/// <summary>{summary}</summary>");
        writer.Line($"internal enum {name} : byte");
        writer.Line("{");
        foreach (var value in values)
        {
            writer.Line("    " + value.Replace("_", "", StringComparison.Ordinal) + ",");
        }

        writer.Line("}").Line();
    }

    /// <summary>
    /// Writes a two-stage lookup table of a property of every code point: the value of <c>codePoint</c> is
    /// <c>Blocks[(Index[codePoint &gt;&gt; Shift] &lt;&lt; Shift) | (codePoint &amp; ((1 &lt;&lt; Shift) - 1))]</c>.
    /// The identical blocks are shared, and the block size giving the smallest table is used.
    /// </summary>
    internal static void WriteTrie(CodeWriter writer, string className, string summary, byte[] values)
    {
        (int Shift, List<int> Index, List<byte> Blocks)? best = null;
        for (var shift = 4; shift <= 10; shift++)
        {
            var blockSize = 1 << shift;
            var index = new List<int>(values.Length >> shift);
            var blocks = new List<byte>();
            var blockNumbers = new Dictionary<string, int>(StringComparer.Ordinal);
            for (var start = 0; start < values.Length; start += blockSize)
            {
                var key = Convert.ToHexString(values, start, blockSize);
                if (!blockNumbers.TryGetValue(key, out var number))
                {
                    number = blockNumbers.Count;
                    blockNumbers.Add(key, number);
                    blocks.AddRange(values.AsSpan(start, blockSize));
                }

                index.Add(number);
            }

            if (blockNumbers.Count > ushort.MaxValue)
                continue;

            if (best is null || (index.Count * 2) + blocks.Count < (best.Value.Index.Count * 2) + best.Value.Blocks.Count)
            {
                best = (shift, index, blocks);
            }
        }

        var (bestShift, bestIndex, bestBlocks) = best!.Value;
        for (var codePoint = 0; codePoint < values.Length; codePoint++)
        {
            if (bestBlocks[(bestIndex[codePoint >> bestShift] << bestShift) | (codePoint & ((1 << bestShift) - 1))] != values[codePoint])
                throw new InvalidOperationException($"Invalid lookup table {className} at {codePoint:X4}");
        }

        writer.Line($"/// <summary>{summary}</summary>");
        using (writer.Block($"internal static partial class {className}"))
        {
            writer.Line($"public const int Shift = {bestShift};").Line();
            writer.Line("/// <summary>The block of each range of 2^<see cref=\"Shift\"/> code points.</summary>");
            WriteArray(writer, "Index", "ushort", bestIndex);
            WriteArray(writer, "Blocks", "byte", bestBlocks);
        }

        writer.Line();
    }

    internal static IEnumerable<(int Start, int End, string[] Fields)> ReadDataFile(string path)
    {
        foreach (var rawLine in File.ReadLines(path))
        {
            var hash = rawLine.IndexOf('#', StringComparison.Ordinal);
            var line = hash >= 0 ? rawLine[..hash] : rawLine;
            if (string.IsNullOrWhiteSpace(line))
                continue;

            var fields = line.Split(';').Select(f => f.Trim()).ToArray();
            var (start, end) = ParseRange(fields[0]);
            yield return (start, end, fields);
        }
    }

    private static (int Start, int End) ParseRange(string value)
    {
        var separator = value.IndexOf("..", StringComparison.Ordinal);
        if (separator < 0)
        {
            var codePoint = int.Parse(value, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            return (codePoint, codePoint);
        }

        return (int.Parse(value[..separator], NumberStyles.HexNumber, CultureInfo.InvariantCulture), int.Parse(value[(separator + 2)..], NumberStyles.HexNumber, CultureInfo.InvariantCulture));
    }

    internal static string Version(string path)
    {
        foreach (var line in File.ReadLines(path).Take(10))
        {
            var match = System.Text.RegularExpressions.Regex.Match(line, @"(\d+\.\d+\.\d+)");
            if (match.Success)
                return match.Groups[1].Value;
        }

        return "unknown";
    }

    private static void GenerateIdnaMapping(GeneratorContext context, CodeWriter writer)
    {
        var path = context.SpecData("unicode", "IdnaMappingTable.txt");
        var starts = new List<int>();
        var statuses = new List<int>();
        var offsets = new List<int>();
        var lengths = new List<int>();
        var data = new StringBuilder();
        var dataCache = new Dictionary<string, int>(StringComparer.Ordinal);
        var expected = 0;
        foreach (var (start, end, fields) in ReadDataFile(path))
        {
            if (start != expected)
                throw new InvalidOperationException($"IdnaMappingTable.txt is not contiguous at {start:X4}");

            expected = end + 1;
            var status = fields[1] switch
            {
                "valid" => 0,
                "ignored" => 1,
                "mapped" => 2,
                "deviation" => 3,
                "disallowed" => 4,
                _ => throw new InvalidOperationException($"Unknown IDNA status '{fields[1]}'"),
            };

            var mapping = "";
            if (fields.Length > 2 && fields[1] is "mapped" or "deviation" && fields[2].Length > 0)
            {
                mapping = string.Concat(fields[2].Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(cp => char.ConvertFromUtf32(int.Parse(cp, NumberStyles.HexNumber, CultureInfo.InvariantCulture))));
            }

            var offset = 0;
            if (mapping.Length > 0 && !dataCache.TryGetValue(mapping, out offset))
            {
                offset = data.Length;
                dataCache.Add(mapping, offset);
                data.Append(mapping);
            }

            // Merge adjacent ranges with the same status and mapping
            if (starts.Count > 0 && statuses[^1] == status && lengths[^1] == mapping.Length && (mapping.Length == 0 || offsets[^1] == offset))
                continue;

            starts.Add(start);
            statuses.Add(status);
            offsets.Add(offset);
            lengths.Add(mapping.Length);
        }

        if (expected != 0x110000)
            throw new InvalidOperationException("IdnaMappingTable.txt does not cover all code points");

        writer.Line($"/// <summary>IDNA Mapping Table (UTS #46), Unicode {Version(path)}.</summary>");
        using (writer.Block("internal static partial class IdnaMappingTable"))
        {
            writer.Line($"public const string UnicodeVersion = \"{Version(path)}\";").Line();
            WriteArray(writer, "RangeStarts", "int", starts);
            writer.Line("/// <summary>0: valid, 1: ignored, 2: mapped, 3: deviation, 4: disallowed</summary>");
            WriteArray(writer, "RangeStatuses", "byte", statuses);
            WriteArray(writer, "RangeMappingOffsets", "int", offsets);
            WriteArray(writer, "RangeMappingLengths", "byte", lengths);
            writer.Line($"public const string MappingData = {CodeWriter.Literal(data.ToString())};");
        }

        writer.Line();
    }

    private static void GenerateNormalization(GeneratorContext context, CodeWriter writer)
    {
        var unicodeDataPath = context.SpecData("unicode", "UnicodeData.txt");
        var combiningClasses = new SortedDictionary<int, int>();
        var decompositions = new Dictionary<int, int[]>();

        int? rangeStart = null;
        foreach (var line in File.ReadLines(unicodeDataPath))
        {
            var fields = line.Split(';');
            var codePoint = int.Parse(fields[0], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            var name = fields[1];
            var ccc = int.Parse(fields[3], CultureInfo.InvariantCulture);

            // Ranges (e.g. CJK ideographs) are listed as First/Last pairs; they have ccc=0 and no decomposition.
            if (name.EndsWith(", First>", StringComparison.Ordinal))
            {
                rangeStart = codePoint;
                continue;
            }

            if (name.EndsWith(", Last>", StringComparison.Ordinal))
            {
                if (ccc != 0)
                    throw new InvalidOperationException("Unexpected combining class for range");

                rangeStart = null;
                continue;
            }

            if (ccc != 0)
            {
                combiningClasses[codePoint] = ccc;
            }

            var decomposition = fields[5];
            if (decomposition.Length > 0 && decomposition[0] != '<')
            {
                decompositions[codePoint] = decomposition.Split(' ').Select(cp => int.Parse(cp, NumberStyles.HexNumber, CultureInfo.InvariantCulture)).ToArray();
            }
        }

        _ = rangeStart;

        var exclusions = new HashSet<int>();
        foreach (var (start, end, fields) in ReadDataFile(context.SpecData("unicode", "DerivedNormalizationProps.txt")))
        {
            if (fields[1] == "Full_Composition_Exclusion")
            {
                for (var cp = start; cp <= end; cp++)
                {
                    exclusions.Add(cp);
                }
            }
        }

        // Full canonical decomposition
        int[] Decompose(int codePoint) => decompositions.TryGetValue(codePoint, out var parts) ? parts.SelectMany(Decompose).ToArray() : [codePoint];

        var fullDecompositions = decompositions.Keys.Order().Select(cp => (CodePoint: cp, Decomposition: Decompose(cp))).ToList();

        // Primary composites: canonical decompositions of two code points, not excluded
        var compositions = new SortedDictionary<long, int>();
        foreach (var (codePoint, parts) in decompositions)
        {
            if (parts.Length == 2 && !exclusions.Contains(codePoint))
            {
                compositions.Add(((long)parts[0] << 21) | (uint)parts[1], codePoint);
            }
        }

        writer.Line("/// <summary>Data for Unicode Normalization Form C (canonical decomposition and composition). Hangul syllables are handled algorithmically.</summary>");
        using (writer.Block("internal static partial class NormalizationData"))
        {
            // Combining classes as ranges of identical values
            var cccStarts = new List<int>();
            var cccEnds = new List<int>();
            var cccValues = new List<int>();
            foreach (var (codePoint, value) in combiningClasses)
            {
                if (cccStarts.Count > 0 && cccEnds[^1] == codePoint - 1 && cccValues[^1] == value)
                {
                    cccEnds[^1] = codePoint;
                    continue;
                }

                cccStarts.Add(codePoint);
                cccEnds.Add(codePoint);
                cccValues.Add(value);
            }

            WriteArray(writer, "CombiningClassRangeStarts", "int", cccStarts);
            WriteArray(writer, "CombiningClassRangeEnds", "int", cccEnds);
            WriteArray(writer, "CombiningClassValues", "byte", cccValues);

            var decompositionOffsets = new List<int>();
            var decompositionData = new List<int>();
            foreach (var (_, parts) in fullDecompositions)
            {
                decompositionOffsets.Add(decompositionData.Count);
                decompositionData.AddRange(parts);
            }

            decompositionOffsets.Add(decompositionData.Count);
            WriteArray(writer, "DecompositionKeys", "int", fullDecompositions.Select(d => d.CodePoint));
            writer.Line("/// <summary>Offsets in <see cref=\"DecompositionData\"/>; the decomposition of key i is [offsets[i], offsets[i+1]).</summary>");
            WriteArray(writer, "DecompositionOffsets", "int", decompositionOffsets);
            WriteArray(writer, "DecompositionData", "int", decompositionData);

            writer.Line("/// <summary>Keys are (first &lt;&lt; 21) | second.</summary>");
            WriteArray(writer, "CompositionKeys", "long", compositions.Keys);
            WriteArray(writer, "CompositionValues", "int", compositions.Values);
        }

        writer.Line();
    }

    private static void GenerateMarks(GeneratorContext context, CodeWriter writer)
    {
        var ranges = new List<(int Start, int End)>();
        foreach (var (start, end, fields) in ReadDataFile(context.SpecData("unicode", "DerivedGeneralCategory.txt")).OrderBy(r => r.Start))
        {
            if (fields[1] is not ("Mn" or "Mc" or "Me"))
                continue;

            if (ranges.Count > 0 && ranges[^1].End == start - 1)
            {
                ranges[^1] = (ranges[^1].Start, end);
            }
            else
            {
                ranges.Add((start, end));
            }
        }

        writer.Line("/// <summary>Code points with General_Category=Mark (Mn, Mc, Me).</summary>");
        using (writer.Block("internal static partial class MarkData"))
        {
            WriteArray(writer, "RangeStarts", "int", ranges.Select(r => r.Start));
            WriteArray(writer, "RangeEnds", "int", ranges.Select(r => r.End));
        }

        writer.Line();
    }

    private static void GenerateIdentifiers(GeneratorContext context, CodeWriter writer)
    {
        var path = context.SpecData("unicode", "DerivedCoreProperties.txt");
        foreach (var property in new[] { "ID_Start", "ID_Continue" })
        {
            var ranges = new List<(int Start, int End)>();
            foreach (var (start, end, fields) in ReadDataFile(path).Where(r => r.Fields[1] == property).OrderBy(r => r.Start))
            {
                if (ranges.Count > 0 && ranges[^1].End == start - 1)
                {
                    ranges[^1] = (ranges[^1].Start, end);
                }
                else
                {
                    ranges.Add((start, end));
                }
            }

            writer.Line($"/// <summary>Code points with the {property} property (Unicode {Version(path)}).</summary>");
            using (writer.Block($"internal static partial class {property.Replace("_", "", StringComparison.Ordinal)}Data"))
            {
                WriteArray(writer, "RangeStarts", "int", ranges.Select(r => r.Start));
                WriteArray(writer, "RangeEnds", "int", ranges.Select(r => r.End));
            }

            writer.Line();
        }
    }

    private static readonly string[] BidiClasses = ["L", "R", "AL", "EN", "ES", "ET", "AN", "CS", "NSM", "BN", "B", "S", "WS", "ON", "LRE", "LRO", "RLE", "RLO", "PDF", "LRI", "RLI", "FSI", "PDI"];

    private static readonly Dictionary<string, string> BidiLongNames = new(StringComparer.Ordinal)
    {
        ["Left_To_Right"] = "L",
        ["Right_To_Left"] = "R",
        ["Arabic_Letter"] = "AL",
        ["European_Number"] = "EN",
        ["European_Separator"] = "ES",
        ["European_Terminator"] = "ET",
        ["Arabic_Number"] = "AN",
        ["Common_Separator"] = "CS",
        ["Nonspacing_Mark"] = "NSM",
        ["Boundary_Neutral"] = "BN",
        ["Paragraph_Separator"] = "B",
        ["Segment_Separator"] = "S",
        ["White_Space"] = "WS",
        ["Other_Neutral"] = "ON",
    };

    /// <summary>
    /// Generates the properties of the Unicode Bidirectional Algorithm (UAX #9, https://www.unicode.org/reports/tr9/):
    /// Bidi_Class (<c>DerivedBidiClass.txt</c>), Bidi_Paired_Bracket_Type and Bidi_Paired_Bracket (<c>BidiBrackets.txt</c>),
    /// Bidi_Mirrored (<c>UnicodeData.txt</c>) and Bidi_Mirroring_Glyph (<c>BidiMirroring.txt</c>). Bidi_Class, Bidi_Paired_Bracket_Type
    /// and Bidi_Mirrored are packed in one two-stage lookup table (they are read for every character of a paragraph); the brackets and
    /// the mirroring glyphs are sorted code point tables.
    /// </summary>
    private static void GenerateBidi(GeneratorContext context)
    {
        var writer = new CodeWriter("tests/Meziantou.Framework.WebPlatform.SpecData/unicode/*.txt");
        writer.Line("namespace Meziantou.Framework.WebPlatform.Unicode;").Line();

        // The @missing lines use the long names of the values (e.g. Left_To_Right), the data lines the short ones
        var path = context.SpecData("unicode", "DerivedBidiClass.txt");
        var bidiClass = new byte[0x110000];
        foreach (var line in File.ReadLines(path))
        {
            const string Prefix = "# @missing:";
            if (!line.StartsWith(Prefix, StringComparison.Ordinal))
                continue;

            var fields = line[Prefix.Length..].Split(';').Select(f => f.Trim()).ToArray();
            var (start, end) = ParseRange(fields[0]);
            Array.Fill(bidiClass, (byte)Array.IndexOf(BidiClasses, BidiLongNames[fields[1]]), start, end - start + 1);
        }

        foreach (var (start, end, fields) in ReadDataFile(path))
        {
            var index = Array.IndexOf(BidiClasses, fields[1]);
            if (index < 0)
                throw new InvalidOperationException($"Unknown bidi class {fields[1]}");

            Array.Fill(bidiClass, (byte)index, start, end - start + 1);
        }

        var brackets = new SortedDictionary<int, int>();
        var bracketTypes = new byte[0x110000];
        foreach (var (start, end, fields) in ReadDataFile(context.SpecData("unicode", "BidiBrackets.txt")))
        {
            if (start != end)
                throw new InvalidOperationException("Unexpected range in BidiBrackets.txt");

            bracketTypes[start] = fields[2] switch
            {
                "o" => 1,
                "c" => 2,
                _ => throw new InvalidOperationException($"Unknown bracket type {fields[2]}"),
            };
            brackets.Add(start, int.Parse(fields[1], NumberStyles.HexNumber, CultureInfo.InvariantCulture));
        }

        var mirroring = new SortedDictionary<int, int>();
        foreach (var (start, end, fields) in ReadDataFile(context.SpecData("unicode", "BidiMirroring.txt")))
        {
            if (start != end)
                throw new InvalidOperationException("Unexpected range in BidiMirroring.txt");

            mirroring.Add(start, int.Parse(fields[1], NumberStyles.HexNumber, CultureInfo.InvariantCulture));
        }

        // Field 9 of UnicodeData.txt; the ranges (First/Last pairs) are not mirrored
        var mirrored = new byte[0x110000];
        foreach (var line in File.ReadLines(context.SpecData("unicode", "UnicodeData.txt")))
        {
            var fields = line.Split(';');
            if (fields[9] == "Y")
            {
                mirrored[int.Parse(fields[0], NumberStyles.HexNumber, CultureInfo.InvariantCulture)] = 1;
            }
        }

        var packed = new byte[0x110000];
        for (var codePoint = 0; codePoint < packed.Length; codePoint++)
        {
            packed[codePoint] = (byte)(bidiClass[codePoint] | (bracketTypes[codePoint] << 5) | (mirrored[codePoint] << 7));
        }

        writer.Line($"/// <summary>Bidi_Class property values (https://www.unicode.org/reports/tr9/#Bidirectional_Character_Types), Unicode {Version(path)}.</summary>");
        writer.Line("internal enum BidiClass : byte");
        writer.Line("{");
        foreach (var name in BidiClasses)
        {
            writer.Line("    " + name + ",");
        }

        writer.Line("}").Line();
        WriteEnum(writer, "BidiPairedBracketType", "Bidi_Paired_Bracket_Type property values (https://www.unicode.org/reports/tr9/#Paired_Brackets).", ["None", "Open", "Close"]);
        WriteTrie(writer, "BidiData", "Bidi_Class (bits 0-4, <see cref=\"BidiClass\"/>), Bidi_Paired_Bracket_Type (bits 5-6, <see cref=\"BidiPairedBracketType\"/>) and Bidi_Mirrored (bit 7) of each code point.", packed);

        writer.Line("/// <summary>Bidi_Paired_Bracket of the code points whose Bidi_Paired_Bracket_Type is not None (BidiBrackets.txt), sorted by code point.</summary>");
        using (writer.Block("internal static partial class BidiBracketData"))
        {
            WriteArray(writer, "CodePoints", "int", brackets.Keys);
            WriteArray(writer, "PairedBrackets", "int", brackets.Values);
        }

        writer.Line();
        writer.Line("/// <summary>Bidi_Mirroring_Glyph of the code points that have one (BidiMirroring.txt), sorted by code point.</summary>");
        using (writer.Block("internal static partial class BidiMirroringData"))
        {
            WriteArray(writer, "CodePoints", "int", mirroring.Keys);
            WriteArray(writer, "Glyphs", "int", mirroring.Values);
        }

        context.WriteFile(context.Source("Unicode", "BidiData.g.cs"), writer.ToString());
    }

    private static void GenerateJoiningType(GeneratorContext context, CodeWriter writer)
    {
        var values = new byte[0x110000];
        string[] names = ["U", "C", "D", "R", "L", "T"];
        foreach (var (start, end, fields) in ReadDataFile(context.SpecData("unicode", "DerivedJoiningType.txt")))
        {
            var index = Array.IndexOf(names, fields[1]);
            if (index < 0)
                throw new InvalidOperationException($"Unknown joining type {fields[1]}");

            Array.Fill(values, (byte)index, start, end - start + 1);
        }

        writer.Line("internal enum JoiningType : byte");
        writer.Line("{");
        foreach (var name in names)
        {
            writer.Line("    " + name + ",");
        }

        writer.Line("}").Line();
        WriteRangeTable(writer, "JoiningTypeData", "Joining_Type of each code point.", values);
    }

    private static void WriteRangeTable(CodeWriter writer, string className, string summary, byte[] values)
    {
        var starts = new List<int>();
        var rangeValues = new List<int>();
        for (var cp = 0; cp < values.Length; cp++)
        {
            if (cp == 0 || values[cp] != values[cp - 1])
            {
                starts.Add(cp);
                rangeValues.Add(values[cp]);
            }
        }

        writer.Line($"/// <summary>{summary}</summary>");
        using (writer.Block($"internal static partial class {className}"))
        {
            WriteArray(writer, "RangeStarts", "int", starts);
            WriteArray(writer, "RangeValues", "byte", rangeValues);
        }

        writer.Line();
    }

    internal static void WriteArray<T>(CodeWriter writer, string name, string type, IEnumerable<T> values)
        where T : IFormattable
    {
        writer.Line($"public static ReadOnlySpan<{type}> {name} =>");
        writer.Line("[");
        writer.Numbers(values, perLine: 16);
        writer.Line("];").Line();
    }
}
