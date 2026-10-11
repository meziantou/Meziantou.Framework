#pragma warning disable MA0004 // Use Task.ConfigureAwait
#pragma warning disable MA0047 // Declare types in namespaces
#pragma warning disable MA0048 // File name must match type name
using Meziantou.Framework;

/// <summary>Writes the tables of the normalization forms (Unicode Standard Annex #15).</summary>
internal static class NormalizationWriter
{
    private const int HangulBase = 0xAC00;
    private const int HangulCount = 11172;

    /// <summary>The greatest length of the full canonical decomposition of a code point that the reader supports.</summary>
    private const int MaxCanonicalLength = 4;

    public static async Task<bool> WriteAsync(FullPath outputPath)
    {
        Console.WriteLine("Loading Unicode normalization data...");
        var combiningClass = await PropertyTablesWriter.LoadCombiningClassAsync();

        // One step of decomposition, from UnicodeData.txt: a canonical mapping has no tag.
        var canonical = new SortedDictionary<int, int[]>();
        var compatibility = new Dictionary<int, int[]>();
        foreach (var rawLine in (await Ucd.DownloadAsync("ucd/UnicodeData.txt")).Split('\n'))
        {
            var fields = rawLine.Trim().Split(';');
            if (fields.Length < 15 || fields[5].Length == 0)
                continue;

            var codePoint = int.Parse(fields[0], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            var mapping = fields[5];
            var isCompatibility = mapping[0] == '<';
            if (isCompatibility)
            {
                mapping = mapping[(mapping.IndexOf('>', StringComparison.Ordinal) + 1)..];
            }

            var values = mapping.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(value => int.Parse(value, NumberStyles.HexNumber, CultureInfo.InvariantCulture)).ToArray();
            if (isCompatibility)
            {
                compatibility.Add(codePoint, values);
            }
            else
            {
                if (values.Length is not (1 or 2))
                    throw new InvalidOperationException($"The canonical decomposition of U+{codePoint:X4} is not one or two code points");

                canonical.Add(codePoint, values);
            }
        }

        // The reader rebuilds a full canonical decomposition by following the first element: check what it relies on.
        foreach (var (codePoint, values) in canonical)
        {
            if (values.Length == 2 && (canonical.ContainsKey(values[1]) || IsHangulSyllable(values[1])))
                throw new InvalidOperationException($"The second element of the canonical decomposition of U+{codePoint:X4} has a decomposition");

            if (Decompose(codePoint, canonical, compatibility: null, combiningClass).Count > MaxCanonicalLength)
                throw new InvalidOperationException($"The canonical decomposition of U+{codePoint:X4} is longer than {MaxCanonicalLength} code points");
        }

        // The full compatibility decomposition is stored for the code points where it differs from the canonical one.
        var compatibilityKeys = new List<int>();
        var compatibilityEntries = new List<int>();
        var pool = new List<ushort>();
        var poolOffsets = new Dictionary<string, int>(StringComparer.Ordinal);
        var maxCompatibilityLength = 0;
        foreach (var codePoint in canonical.Keys.Concat(compatibility.Keys).Order())
        {
            var full = Decompose(codePoint, canonical, compatibility, combiningClass);
            if (full.SequenceEqual(Decompose(codePoint, canonical, compatibility: null, combiningClass)))
                continue;

            var text = string.Concat(full.Select(char.ConvertFromUtf32));
            if (!poolOffsets.TryGetValue(text, out var offset))
            {
                offset = pool.Count;
                poolOffsets.Add(text, offset);
                pool.AddRange(text.Select(c => (ushort)c));
            }

            if (text.Length >= 64)
                throw new InvalidOperationException($"The compatibility decomposition of U+{codePoint:X4} is too long");

            maxCompatibilityLength = Math.Max(maxCompatibilityLength, text.Length);
            compatibilityKeys.Add(codePoint);
            compatibilityEntries.Add((offset << 6) | text.Length);
        }

        // Primary composites: the canonical pairs that are not excluded from composition.
        var exclusions = new HashSet<int>();
        var quickCheck = new byte[Trie.CodePointCount + 1];
        foreach (var line in Ucd.ParseLines(await Ucd.DownloadAsync("ucd/DerivedNormalizationProps.txt")))
        {
            if (line.IsMissing)
                continue;

            for (var codePoint = line.Start; codePoint <= line.End; codePoint++)
            {
                switch (line.Fields[0])
                {
                    case "Full_Composition_Exclusion":
                        exclusions.Add(codePoint);
                        break;

                    case "NFD_QC":
                        quickCheck[codePoint] |= 0x01;
                        break;

                    case "NFKD_QC":
                        quickCheck[codePoint] |= 0x02;
                        break;

                    case "NFC_QC":
                        quickCheck[codePoint] |= (byte)(line.Fields[1] == "N" ? 0x04 : 0x08);
                        break;

                    case "NFKC_QC":
                        quickCheck[codePoint] |= (byte)(line.Fields[1] == "N" ? 0x10 : 0x20);
                        break;
                }
            }
        }

        var compositions = new SortedDictionary<long, int>();
        foreach (var (codePoint, values) in canonical)
        {
            if (values.Length == 2 && !exclusions.Contains(codePoint))
            {
                compositions.Add(((long)values[0] << 21) | (uint)values[1], codePoint);
            }
        }

        // One table for the two values every normalization step reads: the combining class and the quick check flags.
        var tuples = new List<byte>();
        var tupleIndexes = new Dictionary<int, int>();
        var leaves = new byte[Trie.CodePointCount + 1];
        for (var step = 0; step < leaves.Length; step++)
        {
            // The default entry first, so that its index is 0.
            var i = step == 0 ? Trie.CodePointCount : step - 1;
            var key = combiningClass[i] | (quickCheck[i] << 8);
            if (!tupleIndexes.TryGetValue(key, out var index))
            {
                index = tupleIndexes.Count;
                if (index > byte.MaxValue)
                    throw new InvalidOperationException("Too many distinct normalization entries for a one-byte index");

                tupleIndexes.Add(key, index);
                tuples.Add(combiningClass[i]);
                tuples.Add(quickCheck[i]);
            }

            leaves[i] = (byte)index;
        }

        var sizes = new List<(string Name, int Bytes)>();
        var tables = new StringBuilder();
        PropertyTablesWriter.AppendTrie(tables, sizes, "Normalization", leaves, """
            Index of a 2-byte entry of NormalizationTuples: Canonical_Combining_Class, then the quick check flags:
            NFD_QC=No (bit 0), NFKD_QC=No (bit 1), NFC_QC=No (bit 2) or Maybe (bit 3), NFKC_QC=No (bit 4) or Maybe (bit 5).
            """);
        PropertyTablesWriter.AppendBytes(tables, sizes, "NormalizationTuples", [.. tuples], "See GetNormalization.");

        // Fixed-size records of three-byte little-endian fields, searched by their first fields.
        PropertyTablesWriter.AppendBytes(tables, sizes, "CanonicalMappings",
            PropertyTablesWriter.EncodeUInt24(canonical.SelectMany(pair => new[] { pair.Key, pair.Value[0], pair.Value.Length == 2 ? pair.Value[1] : 0 })),
            "Canonical decomposition mappings: 9-byte entries sorted by code point: the code point, then the one or two code points of its mapping (the second is 0 for a singleton). Hangul syllables are decomposed by arithmetic.");
        PropertyTablesWriter.AppendBytes(tables, sizes, "CompatibilityMappings",
            PropertyTablesWriter.EncodeUInt24(compatibilityKeys.SelectMany((key, index) => new[] { key, compatibilityEntries[index] >> 6, compatibilityEntries[index] & 0x3F })),
            "Full compatibility decompositions that differ from the canonical one: 9-byte entries sorted by code point: the code point, then the offset and the length of its decomposition in CompatibilityPool, in UTF-16 code units.");
        var poolBytes = new byte[pool.Count * 2];
        for (var i = 0; i < pool.Count; i++)
        {
            poolBytes[i * 2] = (byte)pool[i];
            poolBytes[(i * 2) + 1] = (byte)(pool[i] >> 8);
        }

        PropertyTablesWriter.AppendBytes(tables, sizes, "CompatibilityPool", poolBytes, "UTF-16 code units of the full compatibility decompositions, in little-endian order.");
        PropertyTablesWriter.AppendBytes(tables, sizes, "Compositions",
            PropertyTablesWriter.EncodeUInt24(compositions.SelectMany(pair => new[] { (int)(pair.Key >> 21), (int)(pair.Key & 0x1FFFFF), pair.Value })),
            "Primary composites: 9-byte entries sorted by their first two fields: the two code points of the pair, then the composite.");

        var sizeLines = string.Join('\n', sizes.Select(size => string.Create(CultureInfo.InvariantCulture, $"//   - {size.Name}: {size.Bytes:N0} bytes")));
        var content = string.Create(CultureInfo.InvariantCulture, $$"""
            // <auto-generated />
            // Generated from the Unicode Character Database {{Ucd.Version}}
            // Data sources: {{Ucd.GetUrl("ucd/UnicodeData.txt")}}
            //               {{Ucd.GetUrl("ucd/DerivedNormalizationProps.txt")}}
            // Sizes ({{sizes.Sum(size => size.Bytes):N0}} bytes):
            {{sizeLines}}
            //
            // Specification: https://www.unicode.org/reports/tr15/
            // DO NOT MODIFY THIS FILE MANUALLY - regenerate using the Unicode generator tool

            #nullable enable

            using System.Runtime.CompilerServices;

            namespace Meziantou.Framework;

            /// <summary>The tables of the normalization forms.</summary>
            internal static class UnicodeNormalizationTables
            {
                /// <summary>The greatest length, in UTF-16 code units, of the full compatibility decomposition of a code point.</summary>
                internal const int MaxCompatibilityDecompositionLength = {{maxCompatibilityLength}};

            {{PropertyTablesWriter.ReadUInt16Method}}
            {{tables.ToString().TrimEnd()}}
            }

            """).ReplaceLineEndings("\n");

        Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"  Normalization tables: {sizes.Sum(size => size.Bytes):N0} bytes"));
        return await GeneratorFiles.WriteTextIfChanged(outputPath / "UnicodeNormalizationTables.g.cs", content);
    }

    private static bool IsHangulSyllable(int codePoint) => codePoint is >= HangulBase and < HangulBase + HangulCount;

    /// <summary>Computes the full decomposition of a code point, in canonical order.</summary>
    private static List<int> Decompose(int codePoint, SortedDictionary<int, int[]> canonical, Dictionary<int, int[]>? compatibility, byte[] combiningClass)
    {
        var result = new List<int>();
        Expand(codePoint);

        // Canonical ordering: a stable sort of each run of characters whose combining class is not 0.
        for (var i = 1; i < result.Count; i++)
        {
            var value = result[i];
            var valueClass = combiningClass[value];
            if (valueClass == 0)
                continue;

            var j = i;
            while (j > 0 && combiningClass[result[j - 1]] > valueClass)
            {
                result[j] = result[j - 1];
                j--;
            }

            result[j] = value;
        }

        return result;

        void Expand(int value)
        {
            if (IsHangulSyllable(value))
            {
                var index = value - HangulBase;
                result.Add(0x1100 + (index / 588));
                result.Add(0x1161 + (index % 588 / 28));
                if (index % 28 != 0)
                {
                    result.Add(0x11A7 + (index % 28));
                }
            }
            else if (canonical.TryGetValue(value, out var mapping) || (compatibility is not null && compatibility.TryGetValue(value, out mapping)))
            {
                foreach (var item in mapping)
                {
                    Expand(item);
                }
            }
            else
            {
                result.Add(value);
            }
        }
    }
}
