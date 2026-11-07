#pragma warning disable MA0004 // Use Task.ConfigureAwait
#pragma warning disable MA0047 // Declare types in namespaces
#pragma warning disable MA0048 // File name must match type name
using Meziantou.Framework;

/// <summary>
/// Writes the binary properties of the Unicode Character Database (UAX #44) behind
/// <c>UnicodeProperties.HasBinaryProperty</c>, as sorted lists of ranges.
/// </summary>
internal static class BinaryPropertiesWriter
{
    // The files that list binary properties, as "XXXX..YYYY ; Property_Name" lines. The lines that have more fields are
    // the values of enumerated or string properties, which are not binary.
    private static readonly string[] Files =
    [
        "ucd/PropList.txt",
        "ucd/DerivedCoreProperties.txt",
        "ucd/DerivedNormalizationProps.txt",
        "ucd/extracted/DerivedBinaryProperties.txt",
        "ucd/emoji/emoji-data.txt",
    ];

    public static async Task<bool> WriteAsync(FullPath outputPath)
    {
        Console.WriteLine("Loading the binary properties...");
        var filePath = outputPath / "UnicodeBinaryProperties.g.cs";
        var aliases = await LoadPropertyAliasesAsync();

        var ranges = new SortedDictionary<string, List<(int Start, int End)>>(StringComparer.Ordinal);
        foreach (var file in Files)
        {
            foreach (var line in Ucd.ParseLines(await Ucd.DownloadAsync(file)))
            {
                if (line.IsMissing || line.Fields.Length != 1)
                    continue;

                var name = line.Fields[0];

                // The contributory properties (Other_Alphabetic...) only exist to derive other ones, and the
                // Expands_On_* properties are deprecated and have no use.
                if (name.StartsWith("Other_", StringComparison.Ordinal) || name.StartsWith("Expands_On_", StringComparison.Ordinal))
                    continue;

                if (!aliases.ContainsKey(name))
                    throw new InvalidOperationException($"The property '{name}' of {file} is not in PropertyAliases.txt");

                if (!ranges.TryGetValue(name, out var list))
                {
                    list = [];
                    ranges.Add(name, list);
                }

                list.Add((line.Start, line.End));
            }
        }

        var identifiers = ranges.Keys.Select(GeneratorFiles.ToIdentifier).ToList();
        var ids = StableEnum.Resolve(filePath, "UnicodeBinaryProperty", identifiers);
        if (ids.Count != ranges.Count)
            throw new InvalidOperationException("The committed UnicodeBinaryProperty enumeration has members that are no longer properties: decide what to do with them");

        var count = ids.Values.Max() + 1;
        var members = new StringBuilder();
        var records = new List<byte>();
        var offsets = new int[count + 1];
        var mergedRanges = new List<(int Start, int End)>[count];
        var totalRanges = 0;
        foreach (var (name, list) in ranges)
        {
            var identifier = GeneratorFiles.ToIdentifier(name);
            var id = ids[identifier];
            mergedRanges[id] = Merge(list);
            totalRanges += mergedRanges[id].Count;
        }

        for (var id = 0; id < count; id++)
        {
            offsets[id] = records.Count / 6;
            foreach (var (start, end) in mergedRanges[id])
            {
                records.AddRange(PropertyTablesWriter.EncodeUInt24([start, end]));
            }
        }

        offsets[count] = records.Count / 6;

        foreach (var (name, identifier) in ranges.Keys.Select(name => (name, GeneratorFiles.ToIdentifier(name))).OrderBy(item => ids[item.Item2]))
        {
            var otherNames = aliases[name].Where(alias => !string.Equals(alias, name, StringComparison.Ordinal)).ToList();
            var aliasText = otherNames.Count == 0 ? "" : " (" + string.Join(", ", otherNames.Select(alias => $"<c>{GeneratorFiles.EscapeXmlText(alias)}</c>")) + ")";
            if (members.Length > 0)
            {
                members.Append('\n');
            }

            members.Append(CultureInfo.InvariantCulture, $"    /// <summary>The <c>{GeneratorFiles.EscapeXmlText(name)}</c> property{aliasText}.</summary>\n");
            members.Append(CultureInfo.InvariantCulture, $"    {identifier} = {ids[identifier]},\n");
        }

        var tables = new StringBuilder();
        var sizes = new List<(string Name, int Bytes)>();
        PropertyTablesWriter.AppendBytes(tables, sizes, "RangeOffsets", PropertyTablesWriter.EncodeUInt24(offsets), "The index of the first range of each property in Ranges, then the number of ranges: 3 bytes each, little-endian.");
        PropertyTablesWriter.AppendBytes(tables, sizes, "Ranges", [.. records], "6-byte records: the first and the last code point of a range, 3 bytes each, little-endian. The ranges of a property are sorted and neither overlap nor touch.");

        var content = string.Create(CultureInfo.InvariantCulture, $$"""
            // <auto-generated />
            // Generated from the Unicode Character Database {{Ucd.Version}}
            // Data source: {{string.Join(", ", Files.Select(Ucd.GetUrl))}}
            // Statistics:
            //   - Properties: {{ranges.Count}}
            //   - Ranges: {{totalRanges}}
            //   - Encoded size: {{sizes.Sum(size => size.Bytes):N0}} bytes
            //
            // The numeric values are stable: a member keeps its value when the data is regenerated, and a property added by a
            // later version of the Unicode Standard takes the next free one.
            // Specification: https://www.unicode.org/reports/tr44/
            // DO NOT MODIFY THIS FILE MANUALLY - regenerate using the Unicode generator tool

            #nullable enable

            namespace Meziantou.Framework;

            /// <summary>Represents a binary property of the Unicode Character Database.</summary>
            /// <remarks>
            /// The contributory properties (<c>Other_Alphabetic</c>...) and the deprecated <c>Expands_On_*</c> properties are not
            /// included. The numeric values are stable across the versions of this package.
            /// </remarks>
            /// <seealso href="https://www.unicode.org/reports/tr44/#Property_Index" />
            public enum UnicodeBinaryProperty : byte
            {
            {{members.ToString().TrimEnd()}}
            }

            /// <summary>The code points of each binary property, as sorted ranges.</summary>
            internal static class UnicodeBinaryPropertyTables
            {
                /// <summary>One more than the greatest numeric value of <see cref="UnicodeBinaryProperty"/>.</summary>
                internal const int PropertyCount = {{count}};
            {{tables.ToString().TrimEnd()}}
            }

            """).ReplaceLineEndings("\n");

        Console.WriteLine($"  Binary properties: {ranges.Count} properties, {totalRanges} ranges");
        return await GeneratorFiles.WriteTextIfChanged(filePath, content);
    }

    /// <summary>Loads the names of each property from PropertyAliases.txt: the long name first, then the short name and the other aliases.</summary>
    private static async Task<Dictionary<string, List<string>>> LoadPropertyAliasesAsync()
    {
        var result = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        var content = await Ucd.DownloadAsync("ucd/PropertyAliases.txt");
        foreach (var rawLine in content.Split('\n'))
        {
            var line = rawLine.Trim();
            var commentIndex = line.IndexOf('#', StringComparison.Ordinal);
            if (commentIndex >= 0)
            {
                line = line[..commentIndex].Trim();
            }

            if (line.Length == 0)
                continue;

            var fields = line.Split(';', StringSplitOptions.TrimEntries);
            if (fields.Length < 2)
                continue;

            // short name; long name; other aliases
            List<string> names = [fields[1], fields[0], .. fields[2..]];
            result[fields[1]] = names.Distinct(StringComparer.Ordinal).ToList();
        }

        return result;
    }

    private static List<(int Start, int End)> Merge(List<(int Start, int End)> ranges)
    {
        var result = new List<(int Start, int End)>();
        foreach (var (start, end) in ranges.OrderBy(range => range.Start))
        {
            if (result.Count > 0 && start <= result[^1].End + 1)
            {
                result[^1] = (result[^1].Start, Math.Max(result[^1].End, end));
            }
            else
            {
                result.Add((start, end));
            }
        }

        return result;
    }
}
