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
        var sentenceEnum = CreateEnum(enumsPath, aliases, "SB", "UnicodeSentenceBreak", "Other", "the Sentence_Break property of a code point", "https://www.unicode.org/reports/tr29/");
        var widthEnum = CreateEnum(enumsPath, aliases, "ea", "UnicodeEastAsianWidth", "Neutral", "the East_Asian_Width property of a code point", "https://www.unicode.org/reports/tr11/");
        var bracketEnum = CreateEnum(enumsPath, aliases, "bpt", "UnicodeBidiPairedBracketType", "None", "the Bidi_Paired_Bracket_Type property of a code point", "https://www.unicode.org/reports/tr9/");
        var joiningTypeEnum = CreateEnum(enumsPath, aliases, "jt", "UnicodeJoiningType", "Non_Joining", "the Joining_Type property of a code point", "https://www.unicode.org/reports/tr44/");
        var joiningGroupEnum = CreateEnum(enumsPath, aliases, "jg", "UnicodeJoiningGroup", "No_Joining_Group", "the Joining_Group property of a code point", "https://www.unicode.org/reports/tr44/");
        var syllabicEnum = CreateEnum(enumsPath, aliases, "InSC", "UnicodeIndicSyllabicCategory", "Other", "the Indic_Syllabic_Category property of a code point", "https://www.unicode.org/reports/tr44/");
        var positionalEnum = CreateEnum(enumsPath, aliases, "InPC", "UnicodeIndicPositionalCategory", "Not_Applicable", "the Indic_Positional_Category property of a code point", "https://www.unicode.org/reports/tr44/");
        var orientationEnum = CreateEnum(enumsPath, aliases, "vo", "UnicodeVerticalOrientation", "Rotated", "the Vertical_Orientation property of a code point", "https://www.unicode.org/reports/tr50/");
        GeneratedEnum[] enums = [lineBreakEnum, graphemeEnum, conjunctEnum, wordEnum, sentenceEnum, widthEnum, bracketEnum, joiningTypeEnum, joiningGroupEnum, syllabicEnum, positionalEnum, orientationEnum];

        var generalCategory = await LoadGeneralCategoryAsync();
        var bidiClass = await LoadBidiClassAsync(aliases);
        var mirrored = await LoadBinaryAsync("ucd/extracted/DerivedBinaryProperties.txt", "Bidi_Mirrored");
        var defaultIgnorable = await LoadBinaryAsync("ucd/DerivedCoreProperties.txt", "Default_Ignorable_Code_Point");
        var idStart = await LoadBinaryAsync("ucd/DerivedCoreProperties.txt", "ID_Start");
        var idContinue = await LoadBinaryAsync("ucd/DerivedCoreProperties.txt", "ID_Continue");
        var emoji = await LoadEmojiAsync();
        var lineBreak = await LoadEnumAsync("ucd/LineBreak.txt", aliases, lineBreakEnum, filter: null);
        var width = await LoadEnumAsync("ucd/EastAsianWidth.txt", aliases, widthEnum, filter: null);
        var grapheme = await LoadEnumAsync("ucd/auxiliary/GraphemeBreakProperty.txt", aliases, graphemeEnum, filter: null);
        var conjunct = await LoadEnumAsync("ucd/DerivedCoreProperties.txt", aliases, conjunctEnum, filter: "InCB");
        var word = await LoadEnumAsync("ucd/auxiliary/WordBreakProperty.txt", aliases, wordEnum, filter: null);
        var sentence = await LoadEnumAsync("ucd/auxiliary/SentenceBreakProperty.txt", aliases, sentenceEnum, filter: null);
        var ideographic = await LoadBinaryAsync("ucd/PropList.txt", "Ideographic");
        var (han, hiragana) = await LoadHanAndHiraganaAsync();
        var joiningType = await LoadEnumAsync("ucd/extracted/DerivedJoiningType.txt", aliases, joiningTypeEnum, filter: null);
        var joiningGroup = await LoadEnumAsync("ucd/extracted/DerivedJoiningGroup.txt", aliases, joiningGroupEnum, filter: null);
        var syllabic = await LoadEnumAsync("ucd/IndicSyllabicCategory.txt", aliases, syllabicEnum, filter: null);
        var positional = await LoadEnumAsync("ucd/IndicPositionalCategory.txt", aliases, positionalEnum, filter: null);
        var orientation = await LoadEnumAsync("ucd/VerticalOrientation.txt", aliases, orientationEnum, filter: null);
        var (bracketType, bracketPairs) = await LoadBracketsAsync(aliases, bracketEnum);
        var mirroringPairs = await LoadMirroringPairsAsync();
        var (lowerDelta, upperDelta, titleDelta) = await LoadSimpleCaseMappingsAsync();
        var (specialCasingRecords, specialCasingPool) = await LoadSpecialCasingAsync();

        const int ExtendedPictographic = 1 << 5; // EmojiProperties.ExtendedPictographic
        RequireBelow(bidiClass, 32, "Bidi_Class");
        RequireBelow(bracketType, 4, "Bidi_Paired_Bracket_Type");
        RequireBelow(width, 8, "East_Asian_Width");
        RequireBelow(grapheme, 32, "Grapheme_Cluster_Break");
        RequireBelow(conjunct, 4, "Indic_Conjunct_Break");
        RequireBelow(sentence, 16, "Sentence_Break");
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
        var caseTuples = new List<(int Lower, int Upper, int Title)>();
        var caseTupleIndexes = new Dictionary<(int Lower, int Upper, int Title), int>();
        var caseLeaves = new byte[ValueCount];

        // The default entry is processed first so that every default tuple has the index 0.
        for (var step = 0; step < ValueCount; step++)
        {
            var i = step == 0 ? DefaultIndex : step - 1;
            var category = (UnicodeCategory)generalCategory[i];
            var isPictographic = (emoji[i] & ExtendedPictographic) != 0;

            var generalFlags = generalCategory[i]
                | (defaultIgnorable[i] ? 0x20 : 0)
                | (idStart[i] ? 0x40 : 0)
                | (idContinue[i] ? 0x80 : 0);
            general[i] = generalTuples.GetIndex((byte)generalFlags, emoji[i]);
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

            var caseKey = (lowerDelta[i], upperDelta[i], titleDelta[i]);
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
            Index of a 2-byte entry of GeneralTuples: General_Category (bits 0-4, a System.Globalization.UnicodeCategory),
            Default_Ignorable_Code_Point (bit 5), ID_Start (bit 6) and ID_Continue (bit 7), then the EmojiProperties flags.
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
        var sentenceWord = new byte[ValueCount];
        var wordNames = GetLongNamesByValue(wordEnum);
        var lineBreakNames = GetLongNamesByValue(lineBreakEnum);
        var graphemeNames = GetLongNamesByValue(graphemeEnum);
        for (var i = 0; i < ValueCount; i++)
        {
            var kind = GetWordKind(i, (UnicodeCategory)generalCategory[i], wordNames[word[i]], lineBreakNames[lineBreak[i]], graphemeNames[grapheme[i]], ideographic[i], han[i], hiragana[i]);
            sentenceWord[i] = (byte)(sentence[i] | ((int)kind << 4));
        }

        AppendTrie(tables, sizes, "SentenceBreak", sentenceWord, """
            Sentence_Break (bits 0-3, a UnicodeSentenceBreak), then the UnicodeWordKind of a word that ends with the code
            point (bits 4-6).
            """);
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
        AppendTrie(tables, sizes, "VerticalOrientation", orientation, """
            Vertical_Orientation (a UnicodeVerticalOrientation), with the default values of the unassigned code points.
            """);
        AppendTrie(tables, sizes, "Case", caseLeaves, """
            Index of a 12-byte entry of CaseTuples: the differences between the code point and its Simple_Lowercase_Mapping,
            its Simple_Uppercase_Mapping and its Simple_Titlecase_Mapping.
            """);
        var caseBytes = new byte[caseTuples.Count * 12];
        for (var i = 0; i < caseTuples.Count; i++)
        {
            System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(caseBytes.AsSpan(i * 12), caseTuples[i].Lower);
            System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(caseBytes.AsSpan((i * 12) + 4), caseTuples[i].Upper);
            System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(caseBytes.AsSpan((i * 12) + 8), caseTuples[i].Title);
        }

        AppendBytes(tables, sizes, "CaseTuples", caseBytes, "12-byte entries: three little-endian 32-bit differences, see GetCase.");
        AppendBytes(tables, sizes, "SpecialCasingMappings", specialCasingRecords, "The unconditional full case mappings of SpecialCasing.txt: 6-byte entries sorted by code point: the code point (3 bytes), the offset of its mappings in SpecialCasingPool (2 bytes), then the lengths of its lowercase (bits 0-1), titlecase (bits 2-3) and uppercase (bits 4-5) mappings, little-endian.");
        AppendSpecialCasingFilter(tables, specialCasingRecords);
        AppendBytes(tables, sizes, "SpecialCasingPool", EncodeUInt24(specialCasingPool), "The code points of the full case mappings, 3 bytes each, little-endian: the lowercase, the titlecase then the uppercase mapping of each entry of SpecialCasingMappings.");
        AppendBytes(tables, sizes, "BidiMirroringPairs", EncodeUInt24(mirroringPairs), "Bidi_Mirroring_Glyph: 6-byte entries sorted by code point: the code point then its mirrored code point, 3 bytes each, little-endian.");
        AppendBytes(tables, sizes, "BidiBracketPairs", EncodeUInt24(bracketPairs), "Bidi_Paired_Bracket: 6-byte entries sorted by code point: the code point then its paired bracket, 3 bytes each, little-endian.");

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

            {{ReadUInt16Method}}
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

        // The stages are bytes, read in little-endian order when an index takes two of them: a span of bytes over
        // static data never allocates, whatever the build configuration and the tier of the JIT compiler.
        var read1 = trie.Stage1Width == 1
            ? $"{name}Stage1[codePoint >> {trie.Shift1}]"
            : $"ReadUInt16({name}Stage1, (codePoint >> {trie.Shift1}) * 2)";
        var index2 = $"(block2 << {bits2}) + ((codePoint >> {trie.Shift2}) & {(1 << bits2) - 1})";
        var read2 = trie.Stage2Width == 1
            ? $"{name}Stage2[{index2}]"
            : $"ReadUInt16({name}Stage2, ({index2}) * 2)";

        destination.Append(string.Create(CultureInfo.InvariantCulture, $$"""
                internal const int {{name}}Default = {{defaultValue}};

                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                internal static int Get{{name}}(int codePoint)
                {
                    if ((uint)codePoint > 0x10FFFF)
                        return {{name}}Default;

                    var block2 = {{read1}};
                    var block3 = {{read2}};
                    return {{name}}Stage3[(block3 << {{trie.Shift2}}) + (codePoint & {{(1 << trie.Shift2) - 1}})];
                }

                private static ReadOnlySpan<byte> {{name}}Stage1 =>
                [{{TrieBuilder.FormatElements<byte>(Trie.Encode(trie.Stage1, trie.Stage1Width), "        ")}}
                ];

                private static ReadOnlySpan<byte> {{name}}Stage2 =>
                [{{TrieBuilder.FormatElements<byte>(Trie.Encode(trie.Stage2, trie.Stage2Width), "        ")}}
                ];

                private static ReadOnlySpan<byte> {{name}}Stage3 =>
                [{{TrieBuilder.FormatElements<byte>(trie.Stage3, "        ")}}
                ];

            """).ReplaceLineEndings("\n"));
    }

    /// <summary>Emits a test that rules out most code points before SpecialCasingMappings is searched.</summary>
    private static void AppendSpecialCasingFilter(StringBuilder destination, byte[] records)
    {
        var codePoints = new List<int>();
        for (var offset = 0; offset < records.Length; offset += 6)
        {
            codePoints.Add(records[offset] | (records[offset + 1] << 8) | (records[offset + 2] << 16));
        }

        var pages = string.Join(" or ", codePoints.Select(codePoint => codePoint >> 8).Distinct().Order().Select(page => string.Create(CultureInfo.InvariantCulture, $"0x{page:X}")));
        destination.Append(string.Create(CultureInfo.InvariantCulture, $$"""

                /// <summary>Determines whether a code point can be in SpecialCasingMappings: it is in one of the blocks of 256 code points that have an entry, and is not below the first entry.</summary>
                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                internal static bool MayHaveSpecialCasing(int codePoint) => codePoint >= 0x{{codePoints.Min():X}} && (codePoint >> 8) is {{pages}};

            """).ReplaceLineEndings("\n"));
    }

    /// <summary>The helper the generated lookups call to read a two-byte index.</summary>
    public const string ReadUInt16Method = """
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            private static int ReadUInt16(ReadOnlySpan<byte> values, int offset) => values[offset] | (values[offset + 1] << 8);
        """;

    /// <summary>Encodes records of three-byte little-endian fields.</summary>
    public static byte[] EncodeUInt24(IEnumerable<int> values)
    {
        var result = new List<byte>();
        foreach (var value in values)
        {
            if ((uint)value > 0xFFFFFF)
                throw new InvalidOperationException("The value does not fit in three bytes: " + value);

            result.Add((byte)value);
            result.Add((byte)(value >> 8));
            result.Add((byte)(value >> 16));
        }

        return [.. result];
    }

    public static void AppendBytes(StringBuilder destination, List<(string Name, int Bytes)> sizes, string name, byte[] values, string comment)
    {
        sizes.Add((name, values.Length));
        destination.Append(string.Create(CultureInfo.InvariantCulture, $$"""

                // {{comment}}
                internal static ReadOnlySpan<byte> {{name}} =>
                [{{TrieBuilder.FormatElements<byte>(values, "        ")}}
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

    /// <summary>Gets the kind of a word segment that ends with a code point, as the rule status values of ICU give it.</summary>
    /// <remarks>
    /// The classes are those of the rules of ICU (icu4c/source/data/brkitr/rules/word.txt), where the status of a word
    /// is the one of its last character: <c>$Numeric</c> is a number (100), <c>$ALetterPlus</c>, <c>$Hebrew_Letter</c>
    /// and <c>$HangulSyllable</c> are letters (200), <c>$Katakana</c>, <c>$Hiragana</c> and <c>$Ideographic</c>
    /// are kana and ideographs (400), and a code point that is in several classes takes the greatest status.
    /// <c>$ALetterPlus</c> is ALetter without the Han, Hiragana, Katakana and Hangul syllables, with the Complex_Context
    /// characters that are not Extend or Control. The letters of the Han script that are not ideographs, such as the
    /// iteration mark U+3005, are ideographs too: ICU groups them with the ideographs by its dictionary.
    /// </remarks>
    private static UnicodeWordKind GetWordKind(int codePoint, UnicodeCategory category, string wordBreak, string lineBreak, string grapheme, bool ideographic, bool han, bool hiragana)
    {
        if (wordBreak is "Katakana" || hiragana)
            return UnicodeWordKind.Kana;

        var isLetter = category is UnicodeCategory.UppercaseLetter or UnicodeCategory.LowercaseLetter or UnicodeCategory.TitlecaseLetter or UnicodeCategory.ModifierLetter or UnicodeCategory.OtherLetter;
        if (ideographic || (han && isLetter))
            return UnicodeWordKind.Ideographic;

        if (codePoint is >= 0xAC00 and <= 0xD7A3)
            return UnicodeWordKind.Letter;

        if (wordBreak is "ALetter" or "Hebrew_Letter" && !han)
            return UnicodeWordKind.Letter;

        if (lineBreak is "Complex_Context" && wordBreak is not "Extend" && grapheme is not "Control")
            return UnicodeWordKind.Letter;

        if (wordBreak is "Numeric")
            return UnicodeWordKind.Number;

        return UnicodeWordKind.None;
    }

    private static string[] GetLongNamesByValue(GeneratedEnum target)
    {
        var result = new string[target.Members.Max(member => member.Value) + 1];
        foreach (var member in target.Members)
        {
            result[member.Value] = member.LongName;
        }

        return result;
    }

    private static async Task<(bool[] Han, bool[] Hiragana)> LoadHanAndHiraganaAsync()
    {
        var han = new bool[ValueCount];
        var hiragana = new bool[ValueCount];
        foreach (var line in Ucd.ParseLines(await Ucd.DownloadAsync("ucd/Scripts.txt")))
        {
            if (line.IsMissing)
                continue;

            var target = line.Fields[0] switch
            {
                "Han" => han,
                "Hiragana" => hiragana,
                _ => null,
            };
            target?.AsSpan(line.Start, line.End - line.Start + 1).Fill(value: true);
        }

        return (han, hiragana);
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

    internal static async Task<byte[]> LoadGeneralCategoryAsync()
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

    private static async Task<(int[] LowerDelta, int[] UpperDelta, int[] TitleDelta)> LoadSimpleCaseMappingsAsync()
    {
        var lower = new int[ValueCount];
        var upper = new int[ValueCount];
        var title = new int[ValueCount];
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

            // An empty Simple_Titlecase_Mapping is the Simple_Uppercase_Mapping (UAX #44)
            title[codePoint] = fields[14].Length > 0
                ? int.Parse(fields[14], NumberStyles.HexNumber, CultureInfo.InvariantCulture) - codePoint
                : upper[codePoint];
        }

        return (lower, upper, title);
    }

    /// <summary>
    /// Loads the unconditional mappings of SpecialCasing.txt: the full case mappings of the code points whose mapping is
    /// not their simple mapping. The mappings that depend on the language or on the context (a condition list) are not
    /// included.
    /// </summary>
    private static async Task<(byte[] Records, List<int> Pool)> LoadSpecialCasingAsync()
    {
        var mappings = new SortedDictionary<int, (int[] Lower, int[] Title, int[] Upper)>();
        foreach (var line in Ucd.ParseLines(await Ucd.DownloadAsync("ucd/SpecialCasing.txt")))
        {
            // code; lower; title; upper; (condition_list;)? (the fields after the code point)
            if (line.IsMissing || line.Start != line.End || (line.Fields.Length > 3 && line.Fields[3].Length > 0))
                continue;

            mappings.Add(line.Start, (ParseCodePoints(line.Fields[0]), ParseCodePoints(line.Fields[1]), ParseCodePoints(line.Fields[2])));
        }

        if (mappings.Count == 0)
            throw new InvalidOperationException("No unconditional mapping in SpecialCasing.txt");

        var records = new List<byte>();
        var pool = new List<int>();
        foreach (var (codePoint, (lower, title, upper)) in mappings)
        {
            if (lower.Length is 0 or > 3 || title.Length is 0 or > 3 || upper.Length is 0 or > 3)
                throw new InvalidOperationException(string.Create(CultureInfo.InvariantCulture, $"The case mappings of U+{codePoint:X4} do not fit in their bit fields"));

            if (pool.Count > ushort.MaxValue)
                throw new InvalidOperationException("SpecialCasingPool is too large for a two-byte offset");

            records.AddRange(EncodeUInt24([codePoint]));
            records.Add((byte)pool.Count);
            records.Add((byte)(pool.Count >> 8));
            records.Add((byte)(lower.Length | (title.Length << 2) | (upper.Length << 4)));
            pool.AddRange(lower);
            pool.AddRange(title);
            pool.AddRange(upper);
        }

        Console.WriteLine($"  Loaded {mappings.Count} unconditional special casings");
        return ([.. records], pool);

        static int[] ParseCodePoints(string value) => [.. value.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(item => int.Parse(item, NumberStyles.HexNumber, CultureInfo.InvariantCulture))];
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
