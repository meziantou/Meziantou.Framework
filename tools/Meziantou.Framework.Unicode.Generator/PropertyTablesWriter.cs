#pragma warning disable MA0004 // Use Task.ConfigureAwait
#pragma warning disable MA0047 // Declare types in namespaces
#pragma warning disable MA0048 // File name must match type name
using Meziantou.Framework;

/// <summary>Writes the per-code-point tables behind <c>UnicodeProperties</c> and the enumerations of their values.</summary>
internal static class PropertyTablesWriter
{
    // One more entry than there are code points: the last one holds the value of the property for an integer that
    // is not a code point, which is the default value of the property.
    private const int ValueCount = Trie.CodePointCount + 1;
    private const int DefaultIndex = Trie.CodePointCount;

    public static async Task<bool> WriteAsync(FullPath outputPath)
    {
        Console.WriteLine("Loading Unicode properties...");
        var aliases = await UcdAliases.LoadAsync();
        var enumsPath = outputPath / "UnicodePropertyEnums.g.cs";
        var tablesPath = outputPath / "UnicodeTables.g.cs";

        var lineBreakEnum = CreateEnum(enumsPath, aliases, "lb", "UnicodeLineBreakClass", "Unknown", "the Line_Break property of a code point", "https://www.unicode.org/reports/tr14/");
        var graphemeEnum = CreateEnum(enumsPath, aliases, "GCB", "UnicodeGraphemeClusterBreak", "Other", "the Grapheme_Cluster_Break property of a code point", "https://www.unicode.org/reports/tr29/");
        var conjunctEnum = CreateEnum(enumsPath, aliases, "InCB", "UnicodeIndicConjunctBreak", "None", "the Indic_Conjunct_Break property of a code point", "https://www.unicode.org/reports/tr44/");
        var wordEnum = CreateEnum(enumsPath, aliases, "WB", "UnicodeWordBreak", "Other", "the Word_Break property of a code point", "https://www.unicode.org/reports/tr29/");
        var widthEnum = CreateEnum(enumsPath, aliases, "ea", "UnicodeEastAsianWidth", "Neutral", "the East_Asian_Width property of a code point", "https://www.unicode.org/reports/tr11/");
        var bracketEnum = CreateEnum(enumsPath, aliases, "bpt", "UnicodeBidiPairedBracketType", "None", "the Bidi_Paired_Bracket_Type property of a code point", "https://www.unicode.org/reports/tr9/");
        var joiningTypeEnum = CreateEnum(enumsPath, aliases, "jt", "UnicodeJoiningType", "Non_Joining", "the Joining_Type property of a code point", "https://www.unicode.org/reports/tr44/");
        var joiningGroupEnum = CreateEnum(enumsPath, aliases, "jg", "UnicodeJoiningGroup", "No_Joining_Group", "the Joining_Group property of a code point", "https://www.unicode.org/reports/tr44/");
        var syllabicEnum = CreateEnum(enumsPath, aliases, "InSC", "UnicodeIndicSyllabicCategory", "Other", "the Indic_Syllabic_Category property of a code point", "https://www.unicode.org/reports/tr44/");
        var positionalEnum = CreateEnum(enumsPath, aliases, "InPC", "UnicodeIndicPositionalCategory", "Not_Applicable", "the Indic_Positional_Category property of a code point", "https://www.unicode.org/reports/tr44/");
        GeneratedEnum[] enums = [lineBreakEnum, graphemeEnum, conjunctEnum, wordEnum, widthEnum, bracketEnum, joiningTypeEnum, joiningGroupEnum, syllabicEnum, positionalEnum];

        var generalCategory = await LoadGeneralCategoryAsync();
        var bidiClass = await LoadBidiClassAsync(aliases);
        var mirrored = await LoadBinaryAsync("ucd/extracted/DerivedBinaryProperties.txt", "Bidi_Mirrored");
        var defaultIgnorable = await LoadBinaryAsync("ucd/DerivedCoreProperties.txt", "Default_Ignorable_Code_Point");
        var emoji = await LoadEmojiAsync();
        var lineBreak = await LoadEnumAsync("ucd/LineBreak.txt", aliases, lineBreakEnum, filter: null);
        var width = await LoadEnumAsync("ucd/EastAsianWidth.txt", aliases, widthEnum, filter: null);
        var grapheme = await LoadEnumAsync("ucd/auxiliary/GraphemeBreakProperty.txt", aliases, graphemeEnum, filter: null);
        var conjunct = await LoadEnumAsync("ucd/DerivedCoreProperties.txt", aliases, conjunctEnum, filter: "InCB");
        var word = await LoadEnumAsync("ucd/auxiliary/WordBreakProperty.txt", aliases, wordEnum, filter: null);
        var joiningType = await LoadEnumAsync("ucd/extracted/DerivedJoiningType.txt", aliases, joiningTypeEnum, filter: null);
        var joiningGroup = await LoadEnumAsync("ucd/extracted/DerivedJoiningGroup.txt", aliases, joiningGroupEnum, filter: null);
        var syllabic = await LoadEnumAsync("ucd/IndicSyllabicCategory.txt", aliases, syllabicEnum, filter: null);
        var positional = await LoadEnumAsync("ucd/IndicPositionalCategory.txt", aliases, positionalEnum, filter: null);
        var (bracketType, bracketPairs) = await LoadBracketsAsync(aliases, bracketEnum);
        var mirroringPairs = await LoadMirroringPairsAsync();
        var (lowerDelta, upperDelta) = await LoadSimpleCaseMappingsAsync();

        const int ExtendedPictographic = 1 << 5; // EmojiProperties.ExtendedPictographic
        RequireBelow(bidiClass, 32, "Bidi_Class");
        RequireBelow(bracketType, 4, "Bidi_Paired_Bracket_Type");
        RequireBelow(width, 8, "East_Asian_Width");
        RequireBelow(grapheme, 32, "Grapheme_Cluster_Break");
        RequireBelow(conjunct, 4, "Indic_Conjunct_Break");
        RequireBelow(generalCategory, 32, "General_Category");

        var generalTuples = new TupleTable(width: 2);
        var general = new byte[ValueCount];
        var bidi = new byte[ValueCount];
        var lineBreakTuples = new TupleTable(width: 2);
        var lineBreakLeaves = new byte[ValueCount];
        var segmentationTuples = new TupleTable(width: 2);
        var segmentation = new byte[ValueCount];
        var joiningTuples = new TupleTable(width: 2);
        var joining = new byte[ValueCount];
        var indicTuples = new TupleTable(width: 2);
        var indic = new byte[ValueCount];
        var caseTuples = new List<(int Lower, int Upper)>();
        var caseTupleIndexes = new Dictionary<(int Lower, int Upper), int>();
        var caseLeaves = new byte[ValueCount];

        // The default entry is processed first so that every default tuple has the index 0.
        for (var step = 0; step < ValueCount; step++)
        {
            var i = step == 0 ? DefaultIndex : step - 1;
            var category = (UnicodeCategory)generalCategory[i];
            var isPictographic = (emoji[i] & ExtendedPictographic) != 0;

            general[i] = generalTuples.GetIndex((byte)(generalCategory[i] | (defaultIgnorable[i] ? 0x20 : 0)), emoji[i]);
            bidi[i] = (byte)(bidiClass[i] | (bracketType[i] << 5) | (mirrored[i] ? 0x80 : 0));

            var lineBreakFlags = width[i]
                | (isPictographic ? 0x08 : 0)
                | (category is UnicodeCategory.OtherNotAssigned ? 0x10 : 0)
                | (category is UnicodeCategory.InitialQuotePunctuation ? 0x20 : 0)
                | (category is UnicodeCategory.FinalQuotePunctuation ? 0x40 : 0)
                | (category is UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark ? 0x80 : 0);
            lineBreakLeaves[i] = lineBreakTuples.GetIndex(lineBreak[i], (byte)lineBreakFlags);

            segmentation[i] = segmentationTuples.GetIndex((byte)(grapheme[i] | (conjunct[i] << 5) | (isPictographic ? 0x80 : 0)), word[i]);
            joining[i] = joiningTuples.GetIndex(joiningType[i], joiningGroup[i]);
            indic[i] = indicTuples.GetIndex(syllabic[i], positional[i]);

            var caseKey = (lowerDelta[i], upperDelta[i]);
            if (!caseTupleIndexes.TryGetValue(caseKey, out var caseIndex))
            {
                caseIndex = caseTuples.Count;
                if (caseIndex > byte.MaxValue)
                    throw new InvalidOperationException("Too many distinct simple case mappings for a one-byte index");

                caseTupleIndexes.Add(caseKey, caseIndex);
                caseTuples.Add(caseKey);
            }

            caseLeaves[i] = (byte)caseIndex;
        }

        var tables = new StringBuilder();
        var sizes = new List<(string Name, int Bytes)>();
        AppendTrie(tables, sizes, "General", general, """
            Index of a 2-byte entry of GeneralTuples: General_Category (bits 0-4, a System.Globalization.UnicodeCategory)
            and Default_Ignorable_Code_Point (bit 5), then the EmojiProperties flags.
            """);
        AppendBytes(tables, sizes, "GeneralTuples", generalTuples.Bytes);
        AppendTrie(tables, sizes, "Bidi", bidi, """
            Bidi_Class (bits 0-4, a UnicodeBidirectionalCategory, with the default values of the unassigned code points),
            Bidi_Paired_Bracket_Type (bits 5-6, a UnicodeBidiPairedBracketType) and Bidi_Mirrored (bit 7).
            """);
        AppendTrie(tables, sizes, "LineBreak", lineBreakLeaves, """
            Index of a 2-byte entry of LineBreakTuples: Line_Break (a UnicodeLineBreakClass), then East_Asian_Width
            (bits 0-2, a UnicodeEastAsianWidth), Extended_Pictographic (bit 3), General_Category=Cn (bit 4), Pi (bit 5),
            Pf (bit 6) and Mn or Mc (bit 7).
            """);
        AppendBytes(tables, sizes, "LineBreakTuples", lineBreakTuples.Bytes);
        AppendTrie(tables, sizes, "Segmentation", segmentation, """
            Index of a 2-byte entry of SegmentationTuples: Grapheme_Cluster_Break (bits 0-4, a UnicodeGraphemeClusterBreak),
            Indic_Conjunct_Break (bits 5-6, a UnicodeIndicConjunctBreak) and Extended_Pictographic (bit 7), then Word_Break
            (a UnicodeWordBreak).
            """);
        AppendBytes(tables, sizes, "SegmentationTuples", segmentationTuples.Bytes);
        AppendTrie(tables, sizes, "Joining", joining, """
            Index of a 2-byte entry of JoiningTuples: Joining_Type (a UnicodeJoiningType), then Joining_Group
            (a UnicodeJoiningGroup).
            """);
        AppendBytes(tables, sizes, "JoiningTuples", joiningTuples.Bytes);
        AppendTrie(tables, sizes, "Indic", indic, """
            Index of a 2-byte entry of IndicTuples: Indic_Syllabic_Category (a UnicodeIndicSyllabicCategory), then
            Indic_Positional_Category (a UnicodeIndicPositionalCategory).
            """);
        AppendBytes(tables, sizes, "IndicTuples", indicTuples.Bytes);
        AppendTrie(tables, sizes, "Case", caseLeaves, """
            Index of a pair of CaseTuples: the difference between the code point and its Simple_Lowercase_Mapping, then
            the difference with its Simple_Uppercase_Mapping.
            """);
        AppendInts(tables, sizes, "CaseTuples", caseTuples.SelectMany(tuple => new[] { tuple.Lower, tuple.Upper }).ToArray(), "Pairs of differences, see GetCase.");
        AppendInts(tables, sizes, "BidiMirroringPairs", mirroringPairs, "Bidi_Mirroring_Glyph: pairs of a code point and its mirrored code point, sorted by the first one.");
        AppendInts(tables, sizes, "BidiBracketPairs", bracketPairs, "Bidi_Paired_Bracket: pairs of a code point and its paired bracket, sorted by the first one.");

        var sizeLines = string.Join('\n', sizes.Select(size => string.Create(CultureInfo.InvariantCulture, $"//   - {size.Name}: {size.Bytes:N0} bytes")));
        var tablesContent = string.Create(CultureInfo.InvariantCulture, $$"""
            // <auto-generated />
            // Generated from the Unicode Character Database {{Ucd.Version}}
            // Data source: {{Ucd.GetUrl("ucd/")}}
            // Sizes ({{sizes.Sum(size => size.Bytes):N0}} bytes):
            {{sizeLines}}
            //
            // Specification: https://www.unicode.org/reports/tr44/
            // DO NOT MODIFY THIS FILE MANUALLY - regenerate using the Unicode generator tool

            #nullable enable

            using System.Runtime.CompilerServices;

            namespace Meziantou.Framework;

            /// <summary>The per-code-point tables of the Unicode properties, as three-stage tables (The Unicode Standard, section 5.1).</summary>
            internal static class UnicodeTables
            {
                /// <summary>The version of the Unicode Standard the tables were generated from.</summary>
                internal const string UnicodeVersion = "{{Ucd.Version}}";
            {{tables.ToString().TrimEnd()}}
            }

            """).ReplaceLineEndings("\n");

        var enumsContent = FormatEnums(enums);

        var updated = await GeneratorFiles.WriteTextIfChanged(tablesPath, tablesContent);
        updated |= await GeneratorFiles.WriteTextIfChanged(enumsPath, enumsContent);
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"  Property tables: {sizes.Sum(size => size.Bytes):N0} bytes"));
        return updated;
    }

    public static void AppendTrie(StringBuilder destination, List<(string Name, int Bytes)> sizes, string name, byte[] values, string comment)
    {
        var defaultValue = values.Length > Trie.CodePointCount ? values[DefaultIndex] : (byte)0;
        var trie = TrieBuilder.Build(values[..Trie.CodePointCount]);
        sizes.Add((name, trie.ByteCount));

        var bits2 = trie.Shift1 - trie.Shift2;
        destination.Append('\n');
        foreach (var line in comment.ReplaceLineEndings("\n").Split('\n'))
        {
            destination.Append(CultureInfo.InvariantCulture, $"    // {line}\n");
        }

        destination.Append(string.Create(CultureInfo.InvariantCulture, $$"""
                internal const int {{name}}Default = {{defaultValue}};

                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                internal static int Get{{name}}(int codePoint)
                {
                    if ((uint)codePoint > 0x10FFFF)
                        return {{name}}Default;

                    var block2 = {{name}}Stage1[codePoint >> {{trie.Shift1}}];
                    var block3 = {{name}}Stage2[(block2 << {{bits2}}) + ((codePoint >> {{trie.Shift2}}) & {{(1 << bits2) - 1}})];
                    return {{name}}Stage3[(block3 << {{trie.Shift2}}) + (codePoint & {{(1 << trie.Shift2) - 1}})];
                }

                private static ReadOnlySpan<ushort> {{name}}Stage1 =>
                [{{TrieBuilder.FormatElements<ushort>(trie.Stage1, "        ")}}
                ];

                private static ReadOnlySpan<ushort> {{name}}Stage2 =>
                [{{TrieBuilder.FormatElements<ushort>(trie.Stage2, "        ")}}
                ];

                private static ReadOnlySpan<byte> {{name}}Stage3 =>
                [{{TrieBuilder.FormatElements<byte>(trie.Stage3, "        ")}}
                ];

            """).ReplaceLineEndings("\n"));
    }

    private static void AppendBytes(StringBuilder destination, List<(string Name, int Bytes)> sizes, string name, List<byte> values)
    {
        sizes.Add((name, values.Count));
        destination.Append(string.Create(CultureInfo.InvariantCulture, $$"""

                internal static ReadOnlySpan<byte> {{name}} =>
                [{{TrieBuilder.FormatElements<byte>(values.ToArray(), "        ")}}
                ];

            """).ReplaceLineEndings("\n"));
    }

    private static void AppendInts(StringBuilder destination, List<(string Name, int Bytes)> sizes, string name, int[] values, string comment)
    {
        sizes.Add((name, values.Length * sizeof(int)));
        destination.Append(string.Create(CultureInfo.InvariantCulture, $$"""

                // {{comment}}
                internal static ReadOnlySpan<int> {{name}} =>
                [{{TrieBuilder.FormatElements<int>(values, "        ")}}
                ];

            """).ReplaceLineEndings("\n"));
    }

    private static GeneratedEnum CreateEnum(FullPath enumsPath, UcdAliases aliases, string property, string name, string defaultLongName, string summary, string url)
    {
        var longNames = aliases.GetLongNames(property);
        if (!longNames.Remove(defaultLongName))
            throw new InvalidOperationException($"The property '{property}' has no value '{defaultLongName}'");

        longNames.Insert(0, defaultLongName);
        var shortNames = aliases.GetShortNames(property);
        var ids = StableEnum.Resolve(enumsPath, name, longNames.Select(GeneratorFiles.ToIdentifier));
        var members = new List<GeneratedEnumMember>();
        foreach (var longName in longNames)
        {
            var identifier = GeneratorFiles.ToIdentifier(longName);
            members.Add(new GeneratedEnumMember(identifier, longName, shortNames[longName], ids[identifier]));
        }

        if (ids.Count != members.Count)
            throw new InvalidOperationException($"The committed enumeration '{name}' has members that the property '{property}' no longer has: decide what to do with them");

        if (ids[GeneratorFiles.ToIdentifier(defaultLongName)] != 0)
            throw new InvalidOperationException($"The default value of '{name}' must be 0");

        if (members.Count > 256)
            throw new InvalidOperationException($"The enumeration '{name}' does not fit in a byte");

        members.Sort((a, b) => a.Value.CompareTo(b.Value));
        return new GeneratedEnum(property, name, summary, url, members);
    }

    private static string FormatEnums(GeneratedEnum[] enums)
    {
        var sb = new StringBuilder();
        sb.Append(string.Create(CultureInfo.InvariantCulture, $$"""
            // <auto-generated />
            // Generated from the Unicode Character Database {{Ucd.Version}}
            // Data source: {{Ucd.GetUrl("ucd/PropertyValueAliases.txt")}}
            //
            // The numeric values are stable: a member keeps its value when the data is regenerated, and a value added by a
            // later version of the Unicode Standard takes the next free one.
            // DO NOT MODIFY THIS FILE MANUALLY - regenerate using the Unicode generator tool

            #nullable enable

            namespace Meziantou.Framework;

            """).ReplaceLineEndings("\n"));

        foreach (var item in enums)
        {
            sb.Append(string.Create(CultureInfo.InvariantCulture, $$"""

                /// <summary>Represents {{item.Summary}}.</summary>
                /// <remarks>The numeric values are stable across the versions of this package.</remarks>
                /// <seealso href="{{item.Url}}" />
                public enum {{item.Name}} : byte
                {

                """).ReplaceLineEndings("\n"));

            for (var i = 0; i < item.Members.Count; i++)
            {
                var member = item.Members[i];
                var alias = string.Equals(member.ShortName, member.LongName, StringComparison.Ordinal)
                    ? ""
                    : $" (<c>{GeneratorFiles.EscapeXmlText(member.ShortName)}</c>)";
                if (i > 0)
                {
                    sb.Append('\n');
                }

                sb.Append(CultureInfo.InvariantCulture, $"    /// <summary>The <c>{GeneratorFiles.EscapeXmlText(member.LongName)}</c> value{alias}.</summary>\n");
                sb.Append(CultureInfo.InvariantCulture, $"    {member.Identifier} = {member.Value},\n");
            }

            sb.Append("}\n");
        }

        return sb.ToString();
    }

    private static void RequireBelow(byte[] values, int limit, string property)
    {
        foreach (var value in values)
        {
            if (value >= limit)
                throw new InvalidOperationException($"A value of {property} does not fit in its bit field: {value}");
        }
    }

    private static async Task<byte[]> LoadEnumAsync(string file, UcdAliases aliases, GeneratedEnum target, string? filter)
    {
        var ids = target.Members.ToDictionary(member => member.LongName, member => (byte)member.Value, StringComparer.Ordinal);
        var result = new byte[ValueCount];
        var valueIndex = filter is null ? 0 : 1;
        var lines = Ucd.ParseLines(await Ucd.DownloadAsync(file));
        var count = 0;

        // The default values first, in the order of the file, then the listed values.
        foreach (var isMissing in new[] { true, false })
        {
            foreach (var line in lines)
            {
                if (line.IsMissing != isMissing)
                    continue;

                if (filter is not null && !string.Equals(line.Fields[0], filter, StringComparison.Ordinal))
                    continue;

                if (line.Fields.Length <= valueIndex)
                    continue;

                var id = ids[aliases.GetLongName(target.Property, line.Fields[valueIndex])];
                result.AsSpan(line.Start, line.End - line.Start + 1).Fill(id);
                count++;
            }
        }

        if (count == 0)
            throw new InvalidOperationException($"No value of '{target.Property}' in {file}");

        Console.WriteLine($"  Loaded {count} ranges of {target.Property}");
        return result;
    }

    private static async Task<bool[]> LoadBinaryAsync(string file, string property)
    {
        var result = new bool[ValueCount];
        var count = 0;
        foreach (var line in Ucd.ParseLines(await Ucd.DownloadAsync(file)))
        {
            if (line.IsMissing || !string.Equals(line.Fields[0], property, StringComparison.Ordinal))
                continue;

            result.AsSpan(line.Start, line.End - line.Start + 1).Fill(value: true);
            count++;
        }

        if (count == 0)
            throw new InvalidOperationException($"No value of '{property}' in {file}");

        return result;
    }

    private static async Task<byte[]> LoadEmojiAsync()
    {
        var result = new byte[ValueCount];
        foreach (var line in Ucd.ParseLines(await Ucd.DownloadAsync("ucd/emoji/emoji-data.txt")))
        {
            if (line.IsMissing)
                continue;

            var flag = line.Fields[0] switch
            {
                "Emoji" => EmojiProperties.Emoji,
                "Emoji_Presentation" => EmojiProperties.EmojiPresentation,
                "Emoji_Modifier" => EmojiProperties.EmojiModifier,
                "Emoji_Modifier_Base" => EmojiProperties.EmojiModifierBase,
                "Emoji_Component" => EmojiProperties.EmojiComponent,
                "Extended_Pictographic" => EmojiProperties.ExtendedPictographic,
                _ => EmojiProperties.None,
            };

            for (var codePoint = line.Start; codePoint <= line.End; codePoint++)
            {
                result[codePoint] |= (byte)flag;
            }
        }

        return result;
    }

    private static async Task<byte[]> LoadGeneralCategoryAsync()
    {
        var result = new byte[ValueCount];
        result.AsSpan().Fill((byte)UnicodeCategory.OtherNotAssigned);
        foreach (var line in Ucd.ParseLines(await Ucd.DownloadAsync("ucd/extracted/DerivedGeneralCategory.txt")))
        {
            if (line.IsMissing)
                continue;

            result.AsSpan(line.Start, line.End - line.Start + 1).Fill((byte)UcdParsers.ParseUnicodeCategory(line.Fields[0]));
        }

        return result;
    }

    internal static async Task<byte[]> LoadCombiningClassAsync()
    {
        var result = new byte[ValueCount];
        foreach (var line in Ucd.ParseLines(await Ucd.DownloadAsync("ucd/extracted/DerivedCombiningClass.txt")))
        {
            if (line.IsMissing)
                continue;

            result.AsSpan(line.Start, line.End - line.Start + 1).Fill(byte.Parse(line.Fields[0], NumberStyles.None, CultureInfo.InvariantCulture));
        }

        return result;
    }

    private static async Task<byte[]> LoadBidiClassAsync(UcdAliases aliases)
    {
        var shortNames = aliases.GetShortNames("bc");
        var result = new byte[ValueCount];
        var lines = Ucd.ParseLines(await Ucd.DownloadAsync("ucd/extracted/DerivedBidiClass.txt"));
        foreach (var isMissing in new[] { true, false })
        {
            foreach (var line in lines)
            {
                if (line.IsMissing != isMissing)
                    continue;

                var shortName = shortNames[aliases.GetLongName("bc", line.Fields[0])];
                result.AsSpan(line.Start, line.End - line.Start + 1).Fill((byte)UcdParsers.ParseBidirectionalCategory(shortName));
            }
        }

        return result;
    }

    private static async Task<(byte[] Types, int[] Pairs)> LoadBracketsAsync(UcdAliases aliases, GeneratedEnum target)
    {
        var ids = target.Members.ToDictionary(member => member.LongName, member => (byte)member.Value, StringComparer.Ordinal);
        var types = new byte[ValueCount];
        var pairs = new SortedDictionary<int, int>();
        foreach (var line in Ucd.ParseLines(await Ucd.DownloadAsync("ucd/BidiBrackets.txt")))
        {
            if (line.IsMissing || line.Start != line.End)
                continue;

            types[line.Start] = ids[aliases.GetLongName("bpt", line.Fields[1])];
            pairs.Add(line.Start, int.Parse(line.Fields[0], NumberStyles.HexNumber, CultureInfo.InvariantCulture));
        }

        return (types, pairs.SelectMany(pair => new[] { pair.Key, pair.Value }).ToArray());
    }

    private static async Task<int[]> LoadMirroringPairsAsync()
    {
        var pairs = new SortedDictionary<int, int>();
        foreach (var line in Ucd.ParseLines(await Ucd.DownloadAsync("ucd/BidiMirroring.txt")))
        {
            if (line.IsMissing || line.Start != line.End)
                continue;

            pairs.Add(line.Start, int.Parse(line.Fields[0], NumberStyles.HexNumber, CultureInfo.InvariantCulture));
        }

        return pairs.SelectMany(pair => new[] { pair.Key, pair.Value }).ToArray();
    }

    private static async Task<(int[] LowerDelta, int[] UpperDelta)> LoadSimpleCaseMappingsAsync()
    {
        var lower = new int[ValueCount];
        var upper = new int[ValueCount];
        var content = await Ucd.DownloadAsync("ucd/UnicodeData.txt");
        foreach (var rawLine in content.Split('\n'))
        {
            var fields = rawLine.Trim().Split(';');
            if (fields.Length < 15)
                continue;

            var codePoint = int.Parse(fields[0], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            if (fields[12].Length > 0)
            {
                upper[codePoint] = int.Parse(fields[12], NumberStyles.HexNumber, CultureInfo.InvariantCulture) - codePoint;
            }

            if (fields[13].Length > 0)
            {
                lower[codePoint] = int.Parse(fields[13], NumberStyles.HexNumber, CultureInfo.InvariantCulture) - codePoint;
            }
        }

        return (lower, upper);
    }

    private sealed class TupleTable(int width)
    {
        private readonly Dictionary<int, int> _indexes = [];

        public List<byte> Bytes { get; } = [];

        public byte GetIndex(byte first, byte second)
        {
            if (width != 2)
                throw new InvalidOperationException("Only two-byte entries are supported");

            var key = first | (second << 8);
            if (!_indexes.TryGetValue(key, out var index))
            {
                index = _indexes.Count;
                if (index > byte.MaxValue)
                    throw new InvalidOperationException("Too many distinct entries for a one-byte index");

                _indexes.Add(key, index);
                Bytes.Add(first);
                Bytes.Add(second);
            }

            return (byte)index;
        }
    }
}

internal sealed record GeneratedEnum(string Property, string Name, string Summary, string Url, List<GeneratedEnumMember> Members);

internal sealed record GeneratedEnumMember(string Identifier, string LongName, string ShortName, int Value);
