using System.Globalization;

namespace SpecGen;

/// <summary>
/// Generates the data of the complex-script shapers, ported from the generators of HarfBuzz 14.6.0 (MIT license):
/// <list type="bullet">
/// <item>the syllable machines of the Indic, Khmer, Myanmar and Universal Shaping Engine shapers and the categories of their
/// characters (<see cref="SyllableMachineGenerator"/>);</item>
/// <item>the Indic categories and positions (gen-indic-table.py) from IndicSyllabicCategory.txt, IndicPositionalCategory.txt and
/// Blocks.txt, with the overrides of HarfBuzz;</item>
/// <item>the USE categories (gen-use-table.py) from IndicSyllabicCategory.txt, IndicPositionalCategory.txt, ArabicShaping.txt,
/// DerivedCoreProperties.txt, UnicodeData.txt, Blocks.txt, Scripts.txt and the additional categories of Microsoft
/// (tests/Meziantou.Framework.WebPlatform.SpecData/harfbuzz/src/ms-use);</item>
/// <item>the prohibited vowel sequences (gen-vowel-constraints.py, ms-use/IndicShapingInvalidCluster.txt);</item>
/// <item>the Arabic presentation forms of the fallback shaping (gen-arabic-table.py, UnicodeData.txt).</item>
/// </list>
/// </summary>
internal static class ComplexShapingGenerator
{
    public static void Generate(GeneratorContext context)
    {
        var indic = SyllableMachineGenerator.Read(Machine(context, "indic"), "indic");
        var khmer = SyllableMachineGenerator.Read(Machine(context, "khmer"), "khmer");
        var myanmar = SyllableMachineGenerator.Read(Machine(context, "myanmar"), "myanmar");
        var use = SyllableMachineGenerator.Read(Machine(context, "use"), "use");

        var writer = new CodeWriter("tests/Meziantou.Framework.WebPlatform.SpecData/harfbuzz/src/hb-ot-shaper-*-machine.rl");
        writer.Line("namespace Meziantou.Framework.WebPlatform.Css.Fonts;").Line();
        SyllableMachineGenerator.WriteCategories(writer, "IndicCategory", "The categories of the characters of the Indic, Khmer and Myanmar shapers (the exports of their syllable machines).", [indic, khmer, myanmar]);
        SyllableMachineGenerator.WriteCategories(writer, "UseCategory", "The categories of the characters of the Universal Shaping Engine (the exports of its syllable machine).", [use]);
        writer.Line("/// <summary>The positions of the characters in an Indic syllable, from left to right (HarfBuzz's <c>ot_position_t</c>, hb-ot-shaper-indic.hh).</summary>");
        writer.Line("internal enum IndicPosition : byte");
        writer.Line("{");
        foreach (var position in Positions)
        {
            writer.Line("    " + string.Concat(position.Split('_').Select(p => p[0] + p[1..].ToLowerInvariant())) + ",");
        }

        writer.Line("}").Line();
        SyllableMachineGenerator.Write(writer, indic, "IndicSyllableMachine", "IndicSyllableType", "Indic shaper");
        SyllableMachineGenerator.Write(writer, khmer, "KhmerSyllableMachine", "KhmerSyllableType", "Khmer shaper");
        SyllableMachineGenerator.Write(writer, myanmar, "MyanmarSyllableMachine", "MyanmarSyllableType", "Myanmar shaper");
        SyllableMachineGenerator.Write(writer, use, "UseSyllableMachine", "UseSyllableType", "Universal Shaping Engine");
        context.WriteFile(context.Source("Css", "Fonts", "Shaping", "SyllableMachines.g.cs"), writer.ToString().TrimEnd('\n') + "\n");

        var data = new CodeWriter("tests/Meziantou.Framework.WebPlatform.SpecData/unicode/*.txt and tests/Meziantou.Framework.WebPlatform.SpecData/harfbuzz/src/ms-use/*.txt");
        data.Line("namespace Meziantou.Framework.WebPlatform.Unicode;").Line();
        GenerateIndicCategories(context, data, indic, khmer, myanmar);
        GenerateUseCategories(context, data, use);
        GenerateVowelConstraints(context, data);
        GenerateArabicPresentationForms(context, data);
        context.WriteFile(context.Source("Unicode", "ComplexScriptData.g.cs"), data.ToString().TrimEnd('\n') + "\n");
    }

    private static string Machine(GeneratorContext context, string name) => context.SpecData("harfbuzz", "src", $"hb-ot-shaper-{name}-machine.rl");

    /// <summary>The positions of hb-ot-shaper-indic.hh, in the order of their values.</summary>
    private static readonly string[] Positions =
    [
        "START", "RA_TO_BECOME_REPH", "PRE_M", "PRE_C", "BASE_C", "AFTER_MAIN", "ABOVE_C", "BEFORE_SUB", "BELOW_C", "AFTER_SUB",
        "BEFORE_POST", "POST_C", "AFTER_POST", "SMVD", "END",
    ];

    /// <summary>Reads a UCD file into a dictionary (the last value of a code point wins).</summary>
    private static Dictionary<int, string> ReadProperty(string path, int field = 1, Func<string, string>? transform = null)
    {
        var values = new Dictionary<int, string>();
        foreach (var (start, end, fields) in UnicodeGenerator.ReadDataFile(path))
        {
            var value = transform is null ? fields[field] : transform(fields[field]);
            for (var u = start; u <= end; u++)
            {
                values[u] = value;
            }
        }

        return values;
    }

    // ----------------------------------------------------------------------------------------------------------------
    // Indic (gen-indic-table.py)
    // ----------------------------------------------------------------------------------------------------------------

    private static void GenerateIndicCategories(GeneratorContext context, CodeWriter writer, params SyllableMachineGenerator.RagelMachine[] machines)
    {
        int[] allowedSingles = [0x00A0, 0x25CC];
        string[] allowedBlocks =
        [
            "Basic Latin", "Latin-1 Supplement", "Devanagari", "Bengali", "Gurmukhi", "Gujarati", "Oriya", "Tamil", "Telugu", "Kannada",
            "Malayalam", "Myanmar", "Khmer", "Vedic Extensions", "General Punctuation", "Superscripts and Subscripts", "Devanagari Extended",
            "Myanmar Extended-B", "Myanmar Extended-A", "Myanmar Extended-C",
        ];

        var syllabicPath = context.SpecData("unicode", "IndicSyllabicCategory.txt");
        var positionalPath = context.SpecData("unicode", "IndicPositionalCategory.txt");
        var blocksPath = context.SpecData("unicode", "Blocks.txt");
        var files = new[] { ReadProperty(syllabicPath), ReadProperty(positionalPath), ReadProperty(blocksPath) };

        // Merge the data of the code points of the syllabic and positional categories
        string[] defaults = ["Other", "Not_Applicable", "No_Block"];
        var combined = new Dictionary<int, string[]>();
        for (var i = 0; i < files.Length; i++)
        {
            foreach (var (u, value) in files[i])
            {
                if (i == 2 && !combined.ContainsKey(u))
                    continue;

                if (!combined.TryGetValue(u, out var entry))
                {
                    entry = [.. defaults];
                    combined[u] = entry;
                }

                entry[i] = value;
            }
        }

        var categoryMap = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Other"] = "X",
            ["Avagraha"] = "Symbol",
            ["Bindu"] = "SM",
            ["Brahmi_Joining_Number"] = "PLACEHOLDER",
            ["Cantillation_Mark"] = "A",
            ["Consonant"] = "C",
            ["Consonant_Dead"] = "C",
            ["Consonant_Final"] = "CM",
            ["Consonant_Head_Letter"] = "C",
            ["Consonant_Initial_Postfixed"] = "C",
            ["Consonant_Killer"] = "M",
            ["Consonant_Medial"] = "CM",
            ["Consonant_Placeholder"] = "PLACEHOLDER",
            ["Consonant_Preceding_Repha"] = "Repha",
            ["Consonant_Prefixed"] = "X",
            ["Consonant_Subjoined"] = "CM",
            ["Consonant_Succeeding_Repha"] = "CM",
            ["Consonant_With_Stacker"] = "CS",
            ["Gemination_Mark"] = "SM",
            ["Invisible_Stacker"] = "H",
            ["Joiner"] = "ZWJ",
            ["Modifying_Letter"] = "X",
            ["Non_Joiner"] = "ZWNJ",
            ["Nukta"] = "N",
            ["Number"] = "PLACEHOLDER",
            ["Number_Joiner"] = "PLACEHOLDER",
            ["Pure_Killer"] = "M",
            ["Register_Shifter"] = "RS",
            ["Syllable_Modifier"] = "SM",
            ["Tone_Letter"] = "X",
            ["Tone_Mark"] = "N",
            ["Virama"] = "H",
            ["Visarga"] = "SM",
            ["Vowel"] = "V",
            ["Vowel_Dependent"] = "M",
            ["Vowel_Independent"] = "V",
        };

        var positionMap = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Not_Applicable"] = "END",
            ["Left"] = "PRE_C",
            ["Top"] = "ABOVE_C",
            ["Bottom"] = "BELOW_C",
            ["Right"] = "POST_C",

            // The position of the last part of the split sequences
            ["Bottom_And_Right"] = "POST_C",
            ["Left_And_Right"] = "POST_C",
            ["Top_And_Bottom"] = "BELOW_C",
            ["Top_And_Bottom_And_Left"] = "BELOW_C",
            ["Top_And_Bottom_And_Right"] = "POST_C",
            ["Top_And_Left"] = "ABOVE_C",
            ["Top_And_Left_And_Right"] = "POST_C",
            ["Top_And_Right"] = "POST_C",
            ["Overstruck"] = "AFTER_MAIN",
            ["Visual_order_left"] = "PRE_M",
        };

        // The overrides of HarfBuzz (see gen-indic-table.py for the reasons)
        var categoryOverrides = new Dictionary<int, string>
        {
            // The variation selectors appear in the Myanmar grammar, and these characters in the Myanmar specification
            [0x2015] = "PLACEHOLDER",
            [0x2022] = "PLACEHOLDER",
            [0x25FB] = "PLACEHOLDER",
            [0x25FC] = "PLACEHOLDER",
            [0x25FD] = "PLACEHOLDER",
            [0x25FE] = "PLACEHOLDER",

            // Indic
            [0x0930] = "Ra",
            [0x09B0] = "Ra",
            [0x09F0] = "Ra",
            [0x0A30] = "Ra",
            [0x0AB0] = "Ra",
            [0x0B30] = "Ra",
            [0x0BB0] = "Ra",
            [0x0C30] = "Ra",
            [0x0CB0] = "Ra",
            [0x0D30] = "Ra",
            [0x0953] = "SM",
            [0x0954] = "SM",
            [0x0A40] = "MPst",
            [0x0A72] = "C",
            [0x0A73] = "C",
            [0x1CED] = "A",
            [0xA8F2] = "Symbol",
            [0xA8F3] = "Symbol",
            [0xA8F4] = "Symbol",
            [0xA8F5] = "Symbol",
            [0xA8F6] = "Symbol",
            [0xA8F7] = "Symbol",
            [0x1CE9] = "Symbol",
            [0x1CEA] = "Symbol",
            [0x1CEB] = "Symbol",
            [0x1CEC] = "Symbol",
            [0x1CEE] = "Symbol",
            [0x1CEF] = "Symbol",
            [0x1CF0] = "Symbol",
            [0x1CF1] = "Symbol",
            [0x0A51] = "M",
            [0x11301] = "SM",
            [0x11302] = "SM",
            [0x11303] = "SM",
            [0x1133B] = "N",
            [0x1133C] = "N",
            [0x0AFB] = "N",
            [0x0B55] = "N",
            [0x09FC] = "PLACEHOLDER",
            [0x0C80] = "PLACEHOLDER",
            [0x0D04] = "PLACEHOLDER",
            [0x25CC] = "DOTTEDCIRCLE",

            // Khmer
            [0x179A] = "Ra",
            [0x17CC] = "Robatic",
            [0x17C9] = "Robatic",
            [0x17CA] = "Robatic",
            [0x17C6] = "Xgroup",
            [0x17CB] = "Xgroup",
            [0x17CD] = "Xgroup",
            [0x17CE] = "Xgroup",
            [0x17CF] = "Xgroup",
            [0x17D0] = "Xgroup",
            [0x17D1] = "Xgroup",
            [0x17C7] = "Ygroup",
            [0x17C8] = "Ygroup",
            [0x17DD] = "Ygroup",
            [0x17D3] = "Ygroup",
            [0x17D9] = "PLACEHOLDER",

            // Myanmar (https://learn.microsoft.com/typography/script-development/myanmar#analyze)
            [0x104E] = "C",
            [0x1004] = "Ra",
            [0x101B] = "Ra",
            [0x105A] = "Ra",
            [0x1032] = "A",
            [0x1036] = "A",
            [0x103A] = "As",
            [0x103E] = "MH",
            [0x1060] = "ML",
            [0x103C] = "MR",
            [0x103D] = "MW",
            [0x1082] = "MW",
            [0x103B] = "MY",
            [0x105E] = "MY",
            [0x105F] = "MY",
            [0x1063] = "PT",
            [0x1064] = "PT",
            [0x1069] = "PT",
            [0x106A] = "PT",
            [0x106B] = "PT",
            [0x106C] = "PT",
            [0x106D] = "PT",
            [0xAA7B] = "PT",
            [0x1038] = "SM",
            [0x1087] = "SM",
            [0x1088] = "SM",
            [0x1089] = "SM",
            [0x108A] = "SM",
            [0x108B] = "SM",
            [0x108C] = "SM",
            [0x108D] = "SM",
            [0x108F] = "SM",
            [0x109A] = "SM",
            [0x109B] = "SM",
            [0x109C] = "SM",
            [0x104A] = "PLACEHOLDER",
        };

        for (var u = 0xFE00; u <= 0xFE0F; u++)
        {
            categoryOverrides[u] = "VS";
        }

        for (var u = 0x1CE2; u <= 0x1CE8; u++)
        {
            categoryOverrides[u] = "A";
        }

        var positionOverrides = new Dictionary<int, string>
        {
            [0x0A51] = "BELOW_C",
            [0x0B01] = "BEFORE_SUB",
        };

        var data = new Dictionary<int, (string Category, string Position, string Block)>();
        foreach (var (u, (category, position, block)) in combined.Select(p => (p.Key, (p.Value[0], p.Value[1], p.Value[2]))))
        {
            if (!allowedSingles.Contains(u) && !allowedBlocks.Contains(block))
                continue;

            var mapped = categoryMap[category];
            if (mapped == "SM" && position == "Not_Applicable")
            {
                mapped = "SMPst";
            }

            data[u] = (mapped, positionMap[position], block);
        }

        // The overrides (in the order of HarfBuzz's dictionary, which only matters for the code points listed twice)
        foreach (var (u, category) in categoryOverrides)
        {
            var position = data.TryGetValue(u, out var existing) ? existing.Position : "END";
            data[u] = (category, position, files[2][u]);
        }

        // Only some categories have a position
        string[] positionedCategories = ["CM", "SM", "RS", "H", "M", "MPst"];
        string[] consonantCategories = ["C", "CS", "Ra", "CM", "V", "PLACEHOLDER", "DOTTEDCIRCLE"];
        string[] matraCategories = ["M", "MPst"];
        string[] smvdCategories = ["SM", "SMPst", "VD", "A", "Symbol"];
        foreach (var u in data.Keys.ToList())
        {
            var (category, position, block) = data[u];
            if (!positionedCategories.Contains(category))
            {
                position = "END";
            }

            if (consonantCategories.Contains(category))
            {
                position = "BASE_C";
            }
            else if (matraCategories.Contains(category))
            {
                if (block.StartsWith("Khmer", StringComparison.Ordinal) || block.StartsWith("Myanmar", StringComparison.Ordinal))
                {
                    category = position switch
                    {
                        "PRE_C" => "VPre",
                        "ABOVE_C" => "VAbv",
                        "BELOW_C" => "VBlw",
                        "POST_C" => "VPst",
                        _ => throw new InvalidOperationException($"Unexpected position {position} of U+{u:X4}"),
                    };
                }
                else
                {
                    position = MatraPosition(u, position, block);
                }
            }
            else if (smvdCategories.Contains(category))
            {
                position = "SMVD";
            }

            data[u] = (category, position, block);
        }

        foreach (var (u, position) in positionOverrides)
        {
            var category = data.TryGetValue(u, out var existing) ? existing.Category : "X";
            data[u] = (category, position, files[2][u]);
        }

        // The values: the category in the low byte, the position in the high byte
        int Category(string name)
        {
            foreach (var machine in machines)
            {
                foreach (var export in machine.Exports)
                {
                    if (export.Name == name)
                        return export.Value;
                }
            }

            throw new InvalidOperationException($"Unknown Indic category {name}");
        }

        int Value(string category, string position) => Category(category) | (Array.IndexOf(Positions, position) << 8);

        var defaultValue = Value("X", "END");
        var uniqueValues = new List<int> { defaultValue };
        var values = new byte[0x110000];
        foreach (var (u, (category, position, _)) in data)
        {
            var value = Value(category, position);
            var index = uniqueValues.IndexOf(value);
            if (index < 0)
            {
                index = uniqueValues.Count;
                uniqueValues.Add(value);
            }

            values[u] = (byte)index;
        }

        writer.Line($"/// <summary>");
        writer.Line($"/// The categories of the characters of the Indic, Khmer and Myanmar shapers (HarfBuzz's <c>hb_indic_get_categories</c>,");
        writer.Line($"/// gen-indic-table.py): <c>Values[Get(codePoint)]</c> is the category (<c>IndicCategory</c>) in the low byte and the position");
        writer.Line($"/// (<c>IndicPosition</c>) in the high byte. From IndicSyllabicCategory.txt (Unicode {UnicodeGenerator.Version(syllabicPath)}),");
        writer.Line($"/// IndicPositionalCategory.txt and Blocks.txt, with the overrides of HarfBuzz.");
        writer.Line($"/// </summary>");
        using (writer.Block("internal static partial class IndicCategoryData"))
        {
            writer.Line("/// <summary>The values of the categories (the trie gives their index).</summary>");
            UnicodeGenerator.WriteArray(writer, "Values", "ushort", uniqueValues.Select(v => (ushort)v));
            WriteTrie(writer, values);
        }

        writer.Line();
    }

    private static string MatraPosition(int u, string position, string block) => position switch
    {
        "PRE_C" => "PRE_M",
        "POST_C" => block switch
        {
            "Devanagari" => "AFTER_SUB",
            "Bengali" or "Gurmukhi" or "Gujarati" or "Oriya" or "Tamil" or "Malayalam" => "AFTER_POST",
            "Telugu" => u <= 0x0C42 ? "BEFORE_SUB" : "AFTER_SUB",
            "Kannada" => u is < 0x0CC3 or > 0x0CD6 ? "BEFORE_SUB" : "AFTER_SUB",
            _ => "AFTER_SUB",
        },
        "ABOVE_C" => block switch
        {
            "Devanagari" or "Gujarati" or "Tamil" => "AFTER_SUB",
            "Gurmukhi" => "AFTER_POST",
            "Oriya" => "AFTER_MAIN",
            "Telugu" or "Kannada" => "BEFORE_SUB",
            _ => "AFTER_SUB",
        },
        "BELOW_C" => block switch
        {
            "Devanagari" or "Bengali" or "Oriya" => "AFTER_SUB",
            "Gurmukhi" or "Gujarati" or "Tamil" or "Malayalam" => "AFTER_POST",
            "Telugu" or "Kannada" => "BEFORE_SUB",
            _ => "AFTER_SUB",
        },
        _ => throw new InvalidOperationException($"Unexpected matra position {position} of U+{u:X4}"),
    };

    // ----------------------------------------------------------------------------------------------------------------
    // Universal Shaping Engine (gen-use-table.py)
    // ----------------------------------------------------------------------------------------------------------------

    private static void GenerateUseCategories(GeneratorContext context, CodeWriter writer, SyllableMachineGenerator.RagelMachine machine)
    {
        string[] disabledScripts = ["Arabic", "Lao", "Samaritan", "Syriac", "Thai"];

        var syllabicPath = context.SpecData("unicode", "IndicSyllabicCategory.txt");
        var syllabic = ReadProperty(syllabicPath);
        var positional = ReadProperty(context.SpecData("unicode", "IndicPositionalCategory.txt"));

        // The additional categories of Microsoft override the ones of Unicode
        foreach (var (u, value) in ReadProperty(context.SpecData("harfbuzz", "src", "ms-use", "IndicSyllabicCategory-Additional.txt"), transform: v => v == "Consonant_Final_Modifier" ? "Syllable_Modifier" : v))
        {
            syllabic[u] = value;
        }

        foreach (var (u, value) in ReadProperty(context.SpecData("harfbuzz", "src", "ms-use", "IndicPositionalCategory-Additional.txt"), transform: v => v == "NA" ? "Not_Applicable" : v))
        {
            positional[u] = value;
        }

        var joining = ReadProperty(context.SpecData("unicode", "ArabicShaping.txt"), field: 2);
        var ignorable = new HashSet<int>();
        foreach (var (start, end, fields) in UnicodeGenerator.ReadDataFile(context.SpecData("unicode", "DerivedCoreProperties.txt")))
        {
            if (fields[1] == "Default_Ignorable_Code_Point")
            {
                for (var u = start; u <= end; u++)
                {
                    ignorable.Add(u);
                }
            }
        }

        // UnicodeData.txt: the lines are read one by one (the ranges are not expanded, as gen-use-table.py)
        var generalCategories = new Dictionary<int, string>();
        foreach (var (start, _, fields) in UnicodeGenerator.ReadDataFile(context.SpecData("unicode", "UnicodeData.txt")))
        {
            generalCategories[start] = fields[2];
        }

        var scripts = ReadProperty(context.SpecData("unicode", "Scripts.txt"));

        // The code points of the first four files
        var codePoints = new SortedSet<int>(syllabic.Keys.Concat(positional.Keys).Concat(joining.Keys).Concat(ignorable));
        var values = new byte[0x110000];
        foreach (var u in codePoints)
        {
            if (disabledScripts.Contains(scripts.GetValueOrDefault(u, "Unknown")))
                continue;

            var uisc = syllabic.GetValueOrDefault(u, "Other");
            var uipc = positional.GetValueOrDefault(u, "Not_Applicable");
            var ajt = joining.GetValueOrDefault(u, "X");
            var udi = ignorable.Contains(u);
            var ugc = generalCategories.GetValueOrDefault(u, "Cn");
            var use = MapToUse(u, uisc, uipc, ajt, udi, ugc);
            values[u] = (byte)machine.GetCategory(use);
        }

        writer.Line("/// <summary>");
        writer.Line("/// The categories of the characters of the Universal Shaping Engine (HarfBuzz's <c>hb_use_get_category</c>, gen-use-table.py,");
        writer.Line($"/// https://learn.microsoft.com/typography/script-development/use): the <c>UseCategory</c> of each code point, from the Unicode");
        writer.Line($"/// Character Database (Unicode {UnicodeGenerator.Version(syllabicPath)}) and the additional categories of Microsoft.");
        writer.Line("/// </summary>");
        using (writer.Block("internal static partial class UseCategoryData"))
        {
            WriteTrie(writer, values);
        }

        writer.Line();
    }

    /// <summary>The USE category of a character (HarfBuzz's <c>map_to_use</c>).</summary>
    private static string MapToUse(int u, string uisc, string uipc, string ajt, bool udi, string ugc)
    {
        // The overrides of HarfBuzz
        if (u is >= 0x1CE2 and <= 0x1CE8)
        {
            uisc = "Cantillation_Mark";
        }

        if (u is (>= 0x0F18 and <= 0x0F19) or (>= 0x0F3E and <= 0x0F3F))
        {
            uisc = "Vowel_Dependent";
        }

        if (u == 0x1CED)
        {
            uisc = "Tone_Mark";
        }

        bool IsBase() => uisc is "Number" or "Consonant" or "Consonant_Head_Letter" or "Tone_Letter" or "Vowel_Independent"
            || (ajt is "C" or "D" or "L" or "R" && uisc != "Joiner")
            || (ugc == "Lo" && uisc is "Avagraha" or "Bindu" or "Consonant_Final" or "Consonant_Medial" or "Consonant_Subjoined" or "Vowel" or "Vowel_Dependent");
        bool IsBaseOther() => uisc == "Consonant_Placeholder" || u is 0x2015 or 0x2022 or 0x25FB or 0x25FC or 0x25FD or 0x25FE;
        bool IsCgj() => uisc == "Joiner" || (udi && ugc is "Mc" or "Me" or "Mn");
        bool IsSymbolModifier() => uisc == "Symbol_Modifier";
        bool IsHalantOrVowelModifier() => u == 0x0DCA;
        bool IsSakot() => u == 0x1A60;
        bool IsWordJoiner() => (udi && u is not (0x115F or 0x1160 or 0x3164 or 0xFFA0 or 0x1BCA0 or 0x1BCA1 or 0x1BCA2 or 0x1BCA3) && uisc == "Other" && !IsCgj()) || ugc == "Cn";

        var predicates = new (string Category, bool Value)[]
        {
            ("B", IsBase()),
            ("N", uisc == "Brahmi_Joining_Number"),
            ("GB", IsBaseOther()),
            ("CGJ", IsCgj()),
            ("F", (uisc == "Consonant_Final" && ugc != "Lo") || uisc == "Consonant_Succeeding_Repha"),
            ("FM", uisc == "Syllable_Modifier"),
            ("M", (uisc == "Consonant_Medial" && ugc != "Lo") || uisc == "Consonant_Initial_Postfixed"),
            ("CM", uisc is "Nukta" or "Gemination_Mark" or "Consonant_Killer"),
            ("SUB", uisc == "Consonant_Subjoined" && ugc != "Lo"),
            ("CS", uisc == "Consonant_With_Stacker"),
            ("H", uisc == "Virama" && !IsHalantOrVowelModifier()),
            ("HVM", IsHalantOrVowelModifier()),
            ("HN", uisc == "Number_Joiner"),
            ("IS", uisc == "Invisible_Stacker" && !IsSakot()),
            ("G", uisc == "Hieroglyph"),
            ("HM", uisc == "Hieroglyph_Modifier"),
            ("HR", uisc == "Hieroglyph_Mirror"),
            ("J", uisc == "Hieroglyph_Joiner"),
            ("SB", uisc is "Hieroglyph_Mark_Begin" or "Hieroglyph_Segment_Begin"),
            ("SE", uisc is "Hieroglyph_Mark_End" or "Hieroglyph_Segment_End"),
            ("ZWNJ", uisc == "Non_Joiner"),
            ("O", (ugc == "Po" || uisc is "Consonant_Dead" or "Joiner" or "Modifying_Letter" or "Other") && !IsBase() && !IsBaseOther() && !IsCgj() && !IsSymbolModifier() && !IsWordJoiner()),
            ("RK", uisc == "Reordering_Killer"),
            ("R", uisc is "Consonant_Preceding_Repha" or "Consonant_Prefixed"),
            ("Sk", IsSakot()),
            ("SM", IsSymbolModifier()),
            ("V", uisc == "Pure_Killer" || (ugc != "Lo" && uisc is "Vowel" or "Vowel_Dependent")),
            ("VM", uisc is "Tone_Mark" or "Cantillation_Mark" or "Register_Shifter" or "Visarga" || (ugc != "Lo" && uisc == "Bindu")),
            ("WJ", IsWordJoiner()),
        };

        var matches = predicates.Where(p => p.Value).Select(p => p.Category).ToList();
        if (matches.Count != 1)
            throw new InvalidOperationException($"U+{u:X4} ({uisc}, {ugc}, {ajt}) has {matches.Count} USE categories");

        var use = matches[0];
        if (u is 0x11302 or 0x11303 or 0x114C1)
        {
            uipc = "Top";
        }

        var positions = use switch
        {
            "F" => new Dictionary<string, string[]>(StringComparer.Ordinal) { ["Abv"] = ["Top"], ["Blw"] = ["Bottom"], ["Pst"] = ["Right"] },
            "M" => new(StringComparer.Ordinal) { ["Abv"] = ["Top"], ["Blw"] = ["Bottom", "Bottom_And_Left", "Bottom_And_Right"], ["Pst"] = ["Right"], ["Pre"] = ["Left", "Top_And_Bottom_And_Left"] },
            "CM" => new(StringComparer.Ordinal) { ["Abv"] = ["Top"], ["Blw"] = ["Bottom", "Overstruck"] },
            "V" => new(StringComparer.Ordinal) { ["Abv"] = ["Top", "Top_And_Bottom", "Top_And_Bottom_And_Right", "Top_And_Right"], ["Blw"] = ["Bottom", "Overstruck", "Bottom_And_Right"], ["Pst"] = ["Right"], ["Pre"] = ["Left", "Top_And_Left", "Top_And_Left_And_Right", "Left_And_Right"] },
            "VM" => new(StringComparer.Ordinal) { ["Abv"] = ["Top"], ["Blw"] = ["Bottom", "Overstruck"], ["Pst"] = ["Right"], ["Pre"] = ["Left"] },
            "SM" => new(StringComparer.Ordinal) { ["Abv"] = ["Top"], ["Blw"] = ["Bottom"] },
            "FM" => new(StringComparer.Ordinal) { ["Abv"] = ["Top"], ["Blw"] = ["Bottom"], ["Pst"] = ["Not_Applicable"] },
            _ => null,
        };

        string[] categoriesWithoutPosition = ["H", "HM", "HR", "HVM", "IS", "B", "R", "RK", "SUB"];
        if (uipc is not ("Not_Applicable" or "Visual_Order_Left") && u != 0x0F7F && positions is null && !categoriesWithoutPosition.Contains(use))
            throw new InvalidOperationException($"U+{u:X4} ({use}) has the position {uipc}");

        if (positions is not null)
        {
            var suffixes = positions.Where(p => p.Value.Contains(uipc)).Select(p => p.Key).ToList();
            if (suffixes.Count != 1)
                throw new InvalidOperationException($"U+{u:X4} ({use}) has the position {uipc}");

            use += suffixes[0];
        }

        return use;
    }

    // ----------------------------------------------------------------------------------------------------------------
    // Vowel constraints (gen-vowel-constraints.py)
    // ----------------------------------------------------------------------------------------------------------------

    private static void GenerateVowelConstraints(GeneratorContext context, CodeWriter writer)
    {
        var scripts = ReadProperty(context.SpecData("unicode", "Scripts.txt"));
        var path = context.SpecData("harfbuzz", "src", "ms-use", "IndicShapingInvalidCluster.txt");
        var constraints = new Dictionary<string, ConstraintSet>(StringComparer.Ordinal);
        var order = new List<string>();
        var inHeader = true;
        foreach (var rawLine in File.ReadLines(path))
        {
            // The header ends with a line "#"
            if (inHeader)
            {
                inHeader = rawLine.Trim() != "#";
                continue;
            }

            var hash = rawLine.IndexOf('#', StringComparison.Ordinal);
            var line = hash >= 0 ? rawLine[..hash] : rawLine;
            var constraint = line.Split(';')[0].Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(cp => int.Parse(cp, NumberStyles.HexNumber, CultureInfo.InvariantCulture)).ToList();
            if (constraint.Count == 0)
                continue;

            if (constraint.Count < 2)
                throw new InvalidOperationException("A prohibited sequence is too short");

            var script = scripts[constraint[0]];
            if (constraints.TryGetValue(script, out var set))
            {
                set.Add(constraint);
            }
            else
            {
                constraints[script] = new ConstraintSet(constraint);
                order.Add(script);
            }
        }

        writer.Line("/// <summary>");
        writer.Line("/// The prohibited vowel sequences of the Indic scripts (HarfBuzz's <c>_hb_preprocess_text_vowel_constraints</c>,");
        writer.Line("/// gen-vowel-constraints.py, from IndicShapingInvalidCluster.txt of Microsoft): sequences of independent vowels and vowel");
        writer.Line("/// signs that look like another vowel. A dotted circle is inserted before the last character of such a sequence. Each");
        writer.Line("/// sequence is its length followed by its code points.");
        writer.Line("/// </summary>");
        using (writer.Block("internal static partial class VowelConstraintData"))
        {
            writer.Line("/// <summary>The prohibited sequences of a script.</summary>");
            writer.Line("public static ReadOnlySpan<int> GetSequences(UnicodeScript script) => script switch");
            writer.Line("{");
            foreach (var script in order)
            {
                writer.Line($"    UnicodeScript.{script.Replace("_", "", StringComparison.Ordinal)} => {script.Replace("_", "", StringComparison.Ordinal)},");
            }

            writer.Line("    _ => [],");
            writer.Line("};").Line();
            foreach (var script in order)
            {
                var sequences = new List<int>();
                foreach (var sequence in constraints[script].Flatten([]))
                {
                    sequences.Add(sequence.Count);
                    sequences.AddRange(sequence);
                }

                writer.Line($"private static ReadOnlySpan<int> {script.Replace("_", "", StringComparison.Ordinal)} =>");
                writer.Line("[");
                writer.Line(string.Join(", ", sequences.Select(v => v < 16 ? v.ToString(CultureInfo.InvariantCulture) : "0x" + v.ToString("X4", CultureInfo.InvariantCulture))) + ",");
                writer.Line("];").Line();
            }
        }

        writer.Line();
    }

    /// <summary>A set of prohibited sequences (HarfBuzz's <c>ConstraintSet</c>: a shorter sequence replaces the longer ones it starts).</summary>
    private sealed class ConstraintSet(List<int> constraint)
    {
        private List<int>? _list = constraint;
        private SortedDictionary<int, ConstraintSet>? _children;

        public void Add(List<int> constraint)
        {
            if (constraint.Count == 0)
                return;

            var first = constraint[0];
            var rest = constraint[1..];
            if (_list is not null)
            {
                if (constraint.Count <= _list.Count && constraint.SequenceEqual(_list.Take(constraint.Count)))
                {
                    _list = constraint;
                }
                else if (!(_list.Count <= constraint.Count && _list.SequenceEqual(constraint.Take(_list.Count))))
                {
                    _children = new SortedDictionary<int, ConstraintSet> { [_list[0]] = new ConstraintSet(_list[1..]) };
                    _list = null;
                }
            }

            if (_children is not null)
            {
                if (_children.TryGetValue(first, out var child))
                {
                    child.Add(rest);
                }
                else
                {
                    _children[first] = new ConstraintSet(rest);
                }
            }
        }

        public IEnumerable<List<int>> Flatten(List<int> prefix)
        {
            if (_list is not null)
            {
                yield return [.. prefix, .. _list];
                yield break;
            }

            foreach (var (first, child) in _children!)
            {
                foreach (var sequence in child.Flatten([.. prefix, first]))
                {
                    yield return sequence;
                }
            }
        }
    }

    // ----------------------------------------------------------------------------------------------------------------
    // Arabic presentation forms (gen-arabic-table.py)
    // ----------------------------------------------------------------------------------------------------------------

    private static void GenerateArabicPresentationForms(GeneratorContext context, CodeWriter writer)
    {
        // The ligatures used by the fallback shaping
        int[] ligatureSubset =
        [
            0xF2EE, 0xFC08, 0xFC0E, 0xFC12, 0xFC32, 0xFC3F, 0xFC40, 0xFC41, 0xFC42, 0xFC43, 0xFC44, 0xFC4E, 0xFC5E, 0xFC60, 0xFC61, 0xFC62,
            0xFC6A, 0xFC6D, 0xFC6F, 0xFC70, 0xFC73, 0xFC75, 0xFC86, 0xFC8F, 0xFC91, 0xFC94, 0xFC9C, 0xFC9D, 0xFC9E, 0xFC9F, 0xFCA1, 0xFCA2,
            0xFCA3, 0xFCA4, 0xFCA8, 0xFCAA, 0xFCAC, 0xFCB0, 0xFCC9, 0xFCCA, 0xFCCB, 0xFCCC, 0xFCCD, 0xFCCE, 0xFCCF, 0xFCD0, 0xFCD1, 0xFCD2,
            0xFCD3, 0xFCD5, 0xFCDA, 0xFCDB, 0xFCDC, 0xFCDD, 0xFD30, 0xFD88, 0xFEF5, 0xFEF6, 0xFEF7, 0xFEF8, 0xFEF9, 0xFEFA, 0xFEFB, 0xFEFC,
            0xF201, 0xF211, 0xF2EE,
        ];

        var path = context.SpecData("unicode", "UnicodeData.txt");
        var lines = File.ReadAllLines(path).ToList();

        // The ligatures of the private use area of the Windows fonts
        lines.Add("F201;PUA ARABIC LIGATURE LELLAH ISOLATED FORM;Lo;0;AL;<isolated> 0644 0644 0647;;;;N;;;;;");
        lines.Add("F211;PUA ARABIC LIGATURE LAM WITH MEEM WITH JEEM INITIAL FORM;Lo;0;AL;<initial> 0644 0645 062C;;;;N;;;;;");
        lines.Add("F2EE;PUA ARABIC LIGATURE SHADDA WITH FATHATAN ISOLATED FORM;Lo;0;AL;<isolated> 0020 064B 0651;;;;N;;;;;");

        string[] forms = ["initial", "medial", "final", "isolated"];
        var shapes = new Dictionary<int, Dictionary<string, int>>();
        var ligatures = new List<(int[] Items, List<(string? Shape, int Ligature)> Shapes)>();
        foreach (var line in lines)
        {
            var fields = line.Split(';');
            if (!fields[5].StartsWith('<', StringComparison.Ordinal))
                continue;

            var items = fields[5].Split(' ');
            string? shape = items[0][1..^1];
            var characters = items[1..].Select(x => int.Parse(x, NumberStyles.HexNumber, CultureInfo.InvariantCulture)).ToArray();
            var c = int.Parse(fields[0], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            if (!forms.Contains(shape))
                continue;

            if (characters.Length != 1)
            {
                // The mark ligatures start with a space and are in visual order
                if (characters[0] == 0x0020)
                {
                    characters = [.. characters[1..].Reverse()];
                    shape = null;
                }

                if (!ligatureSubset.Contains(c))
                    continue;

                var index = ligatures.FindIndex(l => l.Items.SequenceEqual(characters));
                if (index < 0)
                {
                    ligatures.Add((characters, []));
                    index = ligatures.Count - 1;
                }

                var shapeIndex = ligatures[index].Shapes.FindIndex(s => s.Shape == shape);
                if (shapeIndex >= 0)
                {
                    ligatures[index].Shapes[shapeIndex] = (shape, c);
                }
                else
                {
                    ligatures[index].Shapes.Add((shape, c));
                }
            }
            else
            {
                if (!shapes.TryGetValue(characters[0], out var characterShapes))
                {
                    characterShapes = [];
                    shapes[characters[0]] = characterShapes;
                }

                characterShapes[shape] = c;
            }
        }

        // The ligatures of 2 and 3 characters, by the presentation form of their first component
        var ligatures2 = new List<(int First, List<int[]> Ligatures)>();
        var ligatures3 = new List<(int First, List<int[]> Ligatures)>();
        var markLigatures = new List<(int First, List<int[]> Ligatures)>();
        static void Add(List<(int First, List<int[]> Ligatures)> list, int first, int[] ligature)
        {
            var index = list.FindIndex(l => l.First == first);
            if (index < 0)
            {
                list.Add((first, []));
                index = list.Count - 1;
            }

            list[index].Ligatures.Add(ligature);
        }

        foreach (var (key, keyShapes) in ligatures)
        {
            foreach (var (shape, c) in keyShapes)
            {
                if (key.Length == 3)
                {
                    int[] components = shape switch
                    {
                        "isolated" => [shapes[key[0]]["initial"], shapes[key[1]]["medial"], shapes[key[2]]["final"]],
                        "final" => [shapes[key[0]]["medial"], shapes[key[1]]["medial"], shapes[key[2]]["final"]],
                        "initial" => [shapes[key[0]]["initial"], shapes[key[1]]["medial"], shapes[key[2]]["medial"]],
                        _ => throw new InvalidOperationException("Unexpected shape " + shape),
                    };
                    Add(ligatures3, components[0], [components[1], components[2], c]);
                }
                else if (key.Length == 2)
                {
                    if (shape is null)
                    {
                        Add(markLigatures, key[0], [key[1], c]);
                        continue;
                    }

                    int[] components = shape switch
                    {
                        "isolated" => [shapes[key[0]]["initial"], shapes[key[1]]["final"]],
                        "final" => [shapes[key[0]]["medial"], shapes[key[1]]["final"]],
                        "initial" => [shapes[key[0]]["initial"], shapes[key[1]]["medial"]],
                        _ => throw new InvalidOperationException("Unexpected shape " + shape),
                    };
                    Add(ligatures2, components[0], [components[1], c]);
                }
                else
                {
                    throw new InvalidOperationException("Unexpected number of ligature components");
                }
            }
        }

        var first = shapes.Keys.Min();
        var last = shapes.Keys.Max();
        writer.Line("/// <summary>");
        writer.Line("/// The Arabic presentation forms of the fallback shaping of the fonts without the Arabic features (HarfBuzz's");
        writer.Line($"/// hb-ot-shaper-arabic-table.hh, gen-arabic-table.py), from the decompositions of UnicodeData.txt (Unicode {UnicodeGenerator.Version(context.SpecData("unicode", "Scripts.txt"))}).");
        writer.Line("/// </summary>");
        using (writer.Block("internal static partial class ArabicPresentationFormsData"))
        {
            writer.Line($"public const int First = 0x{first:X4};");
            writer.Line($"public const int Last = 0x{last:X4};").Line();
            writer.Line("/// <summary>The initial, medial, final and isolated forms of the characters from <see cref=\"First\"/> to <see cref=\"Last\"/> (0: none).</summary>");
            var table = new List<int>();
            for (var u = first; u <= last; u++)
            {
                foreach (var form in forms)
                {
                    table.Add(shapes.TryGetValue(u, out var characterShapes) && characterShapes.TryGetValue(form, out var value) ? value : 0);
                }
            }

            WriteHexArray(writer, "Forms", "ushort", table, perLine: 16);
            writer.Line("/// <summary>The ligatures of 3 presentation forms, sorted by first form: the first form, the number of ligatures, then the second form, the third form and the ligature of each.</summary>");
            WriteHexArray(writer, "Ligatures3", "ushort", Flatten(ligatures3), perLine: 16);
            writer.Line("/// <summary>The ligatures of 2 presentation forms (lam-alef...), sorted by first form: the first form, the number of ligatures, then the second form and the ligature of each.</summary>");
            WriteHexArray(writer, "Ligatures2", "ushort", Flatten(ligatures2), perLine: 16);
            writer.Line("/// <summary>The ligatures of 2 marks (shadda with another mark), in the same format.</summary>");
            WriteHexArray(writer, "MarkLigatures", "ushort", Flatten(markLigatures), perLine: 16);
        }

        static List<int> Flatten(List<(int First, List<int[]> Ligatures)> list)
        {
            var result = new List<int>();
            foreach (var (firstForm, items) in list.OrderBy(l => l.First))
            {
                result.Add(firstForm);
                result.Add(items.Count);
                foreach (var item in items)
                {
                    result.AddRange(item);
                }
            }

            return result;
        }
    }

    /// <summary>
    /// Writes a three-stage lookup table of a byte of every code point (<c>Get</c>): the code point is split in three parts,
    /// the first selects a block of the second stage, which selects a block of values. The identical blocks are shared, and
    /// the block sizes giving the smallest table are used.
    /// </summary>
    private static void WriteTrie(CodeWriter writer, byte[] values)
    {
        (int Shift1, int Shift2, int[] Index1, int[] Index2, byte[] Data, int Size)? best = null;
        for (var shift2 = 3; shift2 <= 8; shift2++)
        {
            // The blocks of values
            var blockSize = 1 << shift2;
            var dataBlocks = new Dictionary<string, int>(StringComparer.Ordinal);
            var data = new List<byte>();
            var blockNumbers = new List<int>();
            for (var start = 0; start < values.Length; start += blockSize)
            {
                var key = Convert.ToHexString(values, start, blockSize);
                if (!dataBlocks.TryGetValue(key, out var number))
                {
                    number = dataBlocks.Count;
                    dataBlocks[key] = number;
                    data.AddRange(values.AsSpan(start, blockSize));
                }

                blockNumbers.Add(number);
            }

            for (var shift1 = 2; shift1 <= 8; shift1++)
            {
                // The blocks of block numbers
                var groupSize = 1 << shift1;
                var groups = new Dictionary<string, int>(StringComparer.Ordinal);
                var index2 = new List<int>();
                var index1 = new List<int>();
                for (var start = 0; start < blockNumbers.Count; start += groupSize)
                {
                    var group = blockNumbers.Skip(start).Take(groupSize).ToArray();
                    var key = string.Join(',', group);
                    if (!groups.TryGetValue(key, out var number))
                    {
                        number = groups.Count;
                        groups[key] = number;
                        index2.AddRange(group);
                    }

                    index1.Add(number);
                }

                var size = (index1.Count * (groups.Count > 256 ? 2 : 1)) + (index2.Count * (dataBlocks.Count > 256 ? 2 : 1)) + data.Count;
                if (best is null || size < best.Value.Size)
                {
                    best = (shift1, shift2, [.. index1], [.. index2], [.. data], size);
                }
            }
        }

        var (s1, s2, i1, i2, d, _) = best!.Value;
        for (var codePoint = 0; codePoint < values.Length; codePoint++)
        {
            if (d[(i2[(i1[codePoint >> (s1 + s2)] << s1) | ((codePoint >> s2) & ((1 << s1) - 1))] << s2) | (codePoint & ((1 << s2) - 1))] != values[codePoint])
                throw new InvalidOperationException($"Invalid lookup table at {codePoint:X4}");
        }

        var index1Type = i2.Length / (1 << s1) > 256 ? "ushort" : "byte";
        var index2Type = d.Length / (1 << s2) > 256 ? "ushort" : "byte";
        writer.Line("/// <summary>The value of a code point.</summary>");
        writer.Line("public static byte Get(int codePoint) => (uint)codePoint > 0x10FFFF ? (byte)0 : Data[(Index2[(Index1[codePoint >> " + (s1 + s2).ToString(CultureInfo.InvariantCulture) + "] << " + s1.ToString(CultureInfo.InvariantCulture) + ") | ((codePoint >> " + s2.ToString(CultureInfo.InvariantCulture) + ") & " + ((1 << s1) - 1).ToString(CultureInfo.InvariantCulture) + ")] << " + s2.ToString(CultureInfo.InvariantCulture) + ") | (codePoint & " + ((1 << s2) - 1).ToString(CultureInfo.InvariantCulture) + ")];").Line();
        UnicodeGenerator.WriteArray(writer, "Index1", index1Type, i1);
        UnicodeGenerator.WriteArray(writer, "Index2", index2Type, i2);
        UnicodeGenerator.WriteArray(writer, "Data", "byte", d);
    }

    private static void WriteHexArray(CodeWriter writer, string name, string type, IEnumerable<int> values, int perLine)
    {
        writer.Line($"public static ReadOnlySpan<{type}> {name} =>");
        writer.Line("[");
        foreach (var chunk in values.Chunk(perLine))
        {
            writer.Line(string.Join(", ", chunk.Select(v => v < 16 ? v.ToString(CultureInfo.InvariantCulture) : "0x" + v.ToString("X4", CultureInfo.InvariantCulture))) + ",");
        }

        writer.Line("];").Line();
    }
}
