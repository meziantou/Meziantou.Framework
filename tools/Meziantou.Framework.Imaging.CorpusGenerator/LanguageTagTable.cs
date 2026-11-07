using System.Globalization;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using Meziantou.Framework.Imaging.CorpusGenerator.Infrastructure;

namespace Meziantou.Framework.Imaging.CorpusGenerator;

/// <summary>
/// Transcribes the registry of language system tags of the OpenType specification into the source of the library: for
/// each ISO 639 code a language is written with in a BCP 47 language tag, the tags of the language systems a font can
/// have for it.
/// </summary>
/// <remarks>
/// <para>
/// No source is committed. The registry is the page "Language system tags" of the OpenType specification
/// (<see cref="RegistryUrl"/>), saved as HTML; the page changes around its table, so what is pinned is the SHA-256 of
/// the rows of the table, as the generator reads them. The codes of two letters come from the code list of ISO 639-2,
/// as its registration authority (the Library of Congress) publishes it (<see cref="Iso639Url"/>), pinned by the
/// SHA-256 of the file. The language subtag registry of BCP 47 (<see cref="SubtagsUrl"/>) says which language a
/// macrolanguage encompasses and which subtag replaces a deprecated one; it gets new records all the time, so what is
/// pinned is the SHA-256 of the records the generator uses.
/// </para>
/// <para>
/// The registry gives, for each tag, the ISO 639 codes of the languages the tag is for: several tags can have the same
/// code (a language and a group it belongs to, two forms of a language). The tags of a code are ordered here, since
/// the first one a font has is the one that is used: a tag that is not deprecated before one that is, the tag of fewer
/// languages first, then the tag made of the letters of the code, then the order of the registry.
/// </para>
/// <para>
/// A language that belongs to a macrolanguage also has the tags of the macrolanguage, after its own (a text in
/// Standard Arabic, <c>arb</c>, is drawn as Arabic, <c>ar</c>), and a deprecated subtag the tags of the subtag that
/// replaces it.
/// </para>
/// </remarks>
internal static partial class LanguageTagTable
{
    private const string RegistryUrl = "https://learn.microsoft.com/en-us/typography/opentype/spec/languagetags";
    private const string RegistryVersion = "OpenType 1.9.1, page of 2024-12-05";
    private const string RegistryRowsSha256 = "45ac06295d1aa5aabb67a46a2f5956fabf7a9bfe2752744c16fa6ef3595c955d";
    private const string Iso639Url = "https://www.loc.gov/standards/iso639-2/ISO-639-2_utf-8.txt";
    private const string Iso639Sha256 = "42b71885e4dc885559fda5ad059fd81838cf4782cedb5fd08cc3431f7d067371";
    private const string SubtagsUrl = "https://www.iana.org/assignments/language-subtag-registry/language-subtag-registry";
    private const string SubtagsSha256 = "9513badd684161d745a760882e795f8e6574fe24c0db739e9f02df1d630ef864";
    private const string TableSource = "src/Meziantou.Framework.Fonts/OpenType/OpenTypeLanguageTable.g.cs";

    // The tags the rules written by hand in OpenTypeLanguage.cs name: the registry must have them.
    private static readonly string[] TagsOfTheRules = ["APPH", "IPPH", "UPPH", "SYRE", "SYRJ", "SYRN", "ZHS ", "ZHT ", "ZHH ", "ZHTM", "ZHP ", "PGR ", "KGE ", "NOR ", "QUZ ", "MOL ", "MOR "];

    public static int Run(GeneratorOptions options)
    {
        var registryBytes = File.ReadAllBytes(options.LanguageRegistry!);
        var rows = ReadRegistry(Encoding.UTF8.GetString(registryBytes));
        var rowsHash = Bytes.Sha256Hex(Encoding.UTF8.GetBytes(string.Concat(rows.Select(row => $"{row.Name}\t{row.TagCell}\t{row.CodesCell}\n"))));
        if (!string.Equals(rowsHash, RegistryRowsSha256, StringComparison.Ordinal))
            throw new FatalException($"The rows of the registry have sha256 {rowsHash} but {RegistryRowsSha256} is pinned ({RegistryUrl}, {RegistryVersion}). Review the differences of the registry, then update the pinned value.");

        var isoBytes = File.ReadAllBytes(options.Iso639!);
        var isoHash = Bytes.Sha256Hex(isoBytes);
        if (!string.Equals(isoHash, Iso639Sha256, StringComparison.Ordinal))
            throw new FatalException($"The ISO 639-2 code list has sha256 {isoHash} but {Iso639Sha256} is pinned ({Iso639Url}).");

        var tags = rows.Select(ParseRow).ToList();
        foreach (var tag in TagsOfTheRules)
        {
            if (!tags.Exists(item => item.Tag == tag))
                throw new FatalException($"The registry has no tag '{tag}', which a rule of OpenTypeLanguage.cs names.");
        }

        // The tags of each code of three letters, by order of preference.
        var byCode = new SortedDictionary<string, List<(string Tag, int CodeCount, bool IsDeprecated, int Order)>>(StringComparer.Ordinal);
        for (var i = 0; i < tags.Count; i++)
        {
            foreach (var code in tags[i].Codes)
            {
                if (!byCode.TryGetValue(code, out var list))
                {
                    byCode.Add(code, list = []);
                }

                list.Add((tags[i].Tag, tags[i].Codes.Length, tags[i].IsDeprecated, i));
            }
        }

        var lists = new SortedDictionary<string, string[]>(StringComparer.Ordinal);
        foreach (var (code, list) in byCode)
        {
            lists.Add(code, [.. list
                .OrderBy(item => item.IsDeprecated)
                .ThenBy(item => item.CodeCount)
                .ThenBy(item => item.Tag.TrimEnd() != code.ToUpperInvariant())
                .ThenBy(item => item.Order)
                .Select(item => item.Tag)
                .Distinct(StringComparer.Ordinal)]);
        }

        // A code of two letters has the tags of the code of three letters of the same language.
        var twoLetterCount = 0;
        foreach (var line in Encoding.UTF8.GetString(isoBytes).TrimStart('\uFEFF').Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var fields = line.Split('|');
            if (fields.Length != 5)
                throw new FatalException($"Unexpected line in the ISO 639-2 code list: {line}");

            var (bibliographic, terminologic, twoLetters) = (fields[0], fields[1], fields[2]);
            if (twoLetters.Length == 0)
                continue;

            if (twoLetters.Length != 2 || bibliographic.Length != 3)
                throw new FatalException($"Unexpected codes in the ISO 639-2 code list: {line}");

            if (byCode.ContainsKey(terminologic.Length == 3 ? terminologic : bibliographic))
            {
                lists.Add(twoLetters, lists[terminologic.Length == 3 ? terminologic : bibliographic]);
                twoLetterCount++;
            }
        }

        // The languages of a macrolanguage and the deprecated subtags, from the registry of BCP 47.
        var subtags = ReadSubtags(File.ReadAllText(options.LanguageSubtags!, Encoding.UTF8));
        var subtagsHash = Bytes.Sha256Hex(Encoding.UTF8.GetBytes(string.Concat(subtags.Select(item => $"{item.Subtag}\t{item.Macrolanguage}\t{item.PreferredValue}\n"))));
        if (!string.Equals(subtagsHash, SubtagsSha256, StringComparison.Ordinal))
            throw new FatalException($"The records of the language subtag registry have sha256 {subtagsHash} but {SubtagsSha256} is pinned ({SubtagsUrl}). Review the differences, then update the pinned value.");

        // A subtag has its own tags, then the ones of the subtag that replaces it, then the ones of its macrolanguage.
        var own = new Dictionary<string, string[]>(lists, StringComparer.Ordinal);
        var macrolanguages = subtags.Where(item => item.Macrolanguage is not null).ToDictionary(item => item.Subtag, item => item.Macrolanguage!, StringComparer.Ordinal);
        var macrolanguageCount = 0;
        var deprecatedCount = 0;
        foreach (var (subtag, _, preferredValue) in subtags)
        {
            IEnumerable<string> list = own.GetValueOrDefault(subtag, []);
            if (preferredValue is not null)
            {
                list = list.Concat(own.GetValueOrDefault(preferredValue, []));
                if (macrolanguages.TryGetValue(preferredValue, out var macrolanguageOfPreferredValue))
                {
                    list = list.Concat(own.GetValueOrDefault(macrolanguageOfPreferredValue, []));
                }
            }

            if (macrolanguages.TryGetValue(subtag, out var macrolanguage))
            {
                list = list.Concat(own.GetValueOrDefault(macrolanguage, []));
            }

            string[] result = [.. list.Distinct(StringComparer.Ordinal)];
            if (result.Length == 0)
                continue;

            lists[subtag] = result;
            macrolanguageCount += preferredValue is null ? 1 : 0;
            deprecatedCount += preferredValue is null ? 0 : 1;
        }

        var source = Render(lists, rowsHash, isoHash, subtagsHash, tags.Count);
        var target = CorpusDriver.RepoRoot / TableSource;
        if (File.Exists(target) && string.Equals(File.ReadAllText(target, Encoding.UTF8), source, StringComparison.Ordinal))
        {
            Console.WriteLine($"{TableSource} is reproducible ({tags.Count} tags, {lists.Count} language codes: {byCode.Count} codes of the registry, {twoLetterCount} of two letters, {macrolanguageCount} languages of a macrolanguage, {deprecatedCount} deprecated subtags).");
            return 0;
        }

        if (!options.Write)
        {
            Console.WriteLine($"{TableSource} differs from the generator output. Review, then rerun with --write.");
            return 1;
        }

        File.WriteAllText(target, source, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        Console.WriteLine($"{TableSource} written ({tags.Count} tags, {lists.Count} language codes: {byCode.Count} codes of the registry, {twoLetterCount} of two letters, {macrolanguageCount} languages of a macrolanguage, {deprecatedCount} deprecated subtags). Review the diff before committing.");
        return 0;
    }

    /// <summary>Reads the rows of the table of the registry: the text of their three cells, without markup.</summary>
    private static List<(string Name, string TagCell, string CodesCell)> ReadRegistry(string html)
    {
        var rows = new List<(string, string, string)>();
        foreach (Match match in RowRegex().Matches(html))
        {
            rows.Add((Clean(match.Groups["name"].Value), Clean(match.Groups["tag"].Value), Clean(match.Groups["codes"].Value)));
        }

        if (rows.Count < 600)
            throw new FatalException($"The registry has {rows.Count} rows: the file is not the page {RegistryUrl}.");

        return rows;

        static string Clean(string cell) => WebUtility.HtmlDecode(MarkupRegex().Replace(cell, "")).Trim();
    }

    /// <summary>
    /// Reads the records of the language subtag registry of BCP 47 that are languages of two or three letters with a
    /// macrolanguage or a preferred value, in the order of the file.
    /// </summary>
    private static List<(string Subtag, string? Macrolanguage, string? PreferredValue)> ReadSubtags(string registry)
    {
        var result = new List<(string, string?, string?)>();
        foreach (var record in registry.ReplaceLineEndings("\n").Split("\n%%\n"))
        {
            var fields = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var line in record.Split('\n'))
            {
                // A line that starts with a space continues the field before it; no field that is read here is that long.
                var separator = line.IndexOf(": ", StringComparison.Ordinal);
                if (separator > 0 && line[0] != ' ')
                {
                    fields.TryAdd(line[..separator], line[(separator + 2)..].Trim());
                }
            }

            if (fields.GetValueOrDefault("Type") != "language" || !fields.TryGetValue("Subtag", out var subtag) || subtag.Length is not (2 or 3))
                continue;

            var macrolanguage = fields.GetValueOrDefault("Macrolanguage");
            var preferredValue = fields.GetValueOrDefault("Preferred-Value");
            if (macrolanguage is not null || preferredValue is not null)
            {
                result.Add((subtag, macrolanguage, preferredValue));
            }
        }

        if (result.Count < 400)
            throw new FatalException($"The language subtag registry has {result.Count} languages with a macrolanguage or a preferred value: the file is not {SubtagsUrl}.");

        return result;
    }

    private static (string Tag, string[] Codes, bool IsDeprecated) ParseRow((string Name, string TagCell, string CodesCell) row)
    {
        var tag = TagRegex().Match(row.TagCell);
        if (!tag.Success)
            throw new FatalException($"Unexpected tag in the registry: {row.TagCell} ({row.Name}).");

        // The codes, then a note for some rows ("See also...", "Cf. BCP 47...").
        var codes = CodesRegex().Match(row.CodesCell);
        var note = row.CodesCell[codes.Length..];
        if (note.Length > 0 && !note.StartsWith("See", StringComparison.Ordinal) && !note.StartsWith("Cf.", StringComparison.Ordinal))
            throw new FatalException($"Unexpected codes in the registry: {row.CodesCell} ({row.Name}).");

        return (tag.Groups["tag"].Value.PadRight(4), codes.Length == 0 ? [] : codes.Value.Split(',', StringSplitOptions.TrimEntries), tag.Groups["deprecated"].Success);
    }

    private static string Render(SortedDictionary<string, string[]> lists, string rowsHash, string isoHash, string subtagsHash, int tagCount)
    {
        // The lists of tags, each one stored once: its length, then its tags.
        var tagLists = new List<byte>();
        var offsets = new Dictionary<string, int>(StringComparer.Ordinal);
        var records = new List<byte>();
        foreach (var (code, tags) in lists)
        {
            var key = string.Concat(tags);
            if (!offsets.TryGetValue(key, out var offset))
            {
                offset = tagLists.Count;
                offsets.Add(key, offset);
                tagLists.Add(checked((byte)tags.Length));
                tagLists.AddRange(Encoding.ASCII.GetBytes(key));
            }

            records.AddRange(Encoding.ASCII.GetBytes(code.PadRight(3)));
            records.Add(checked((byte)(offset >> 8)));
            records.Add((byte)offset);
        }

        if (tagLists.Count > ushort.MaxValue)
            throw new FatalException("The lists of tags do not fit the 16-bit positions of the table.");

        var builder = new StringBuilder();
        builder.Append("// <auto-generated>\n");
        builder.Append("// Transcribed by tools/Meziantou.Framework.Imaging.CorpusGenerator (language-tags) from the registry of language\n");
        builder.Append(CultureInfo.InvariantCulture, $"// system tags of the OpenType specification ({RegistryUrl},\n");
        builder.Append(CultureInfo.InvariantCulture, $"// {RegistryVersion}; {tagCount} tags, SHA-256 of the rows {rowsHash})\n");
        builder.Append(CultureInfo.InvariantCulture, $"// from the code list of ISO 639-2 ({Iso639Url},\n");
        builder.Append(CultureInfo.InvariantCulture, $"// SHA-256 {isoHash})\n");
        builder.Append(CultureInfo.InvariantCulture, $"// and from the language subtag registry of BCP 47 ({SubtagsUrl},\n");
        builder.Append(CultureInfo.InvariantCulture, $"// SHA-256 of its macrolanguages and preferred values {subtagsHash}).\n");
        builder.Append("// Do not edit this file: run the generator again.\n");
        builder.Append("// </auto-generated>\n\n");
        builder.Append("namespace Meziantou.Framework.Fonts;\n\n");
        builder.Append("internal static partial class OpenTypeLanguage\n{\n");
        builder.Append(CultureInfo.InvariantCulture, $"    private const int CodeCount = {lists.Count};\n\n");
        builder.Append("    /// <summary>\n");
        builder.Append("    /// Gets the language subtags that have language system tags, in increasing order: records of five bytes, the\n");
        builder.Append("    /// three letters of the subtag (one of two letters ends with a space), then the position of its tags in\n");
        builder.Append("    /// <see cref=\"TagLists\"/>, on 16 bits, the most significant byte first.\n");
        builder.Append("    /// </summary>\n");
        builder.Append("    private static ReadOnlySpan<byte> Codes =>\n    [\n");
        AppendBytes(builder, records, 20);
        builder.Append("    ];\n\n");
        builder.Append("    /// <summary>Gets the lists of tags: the number of tags of a list, then the four letters of each one, by order of preference.</summary>\n");
        builder.Append("    private static ReadOnlySpan<byte> TagLists =>\n    [\n");
        AppendBytes(builder, tagLists, 20);
        builder.Append("    ];\n}\n");
        return builder.ToString();
    }

    private static void AppendBytes(StringBuilder builder, List<byte> data, int perLine)
    {
        for (var i = 0; i < data.Count; i += perLine)
        {
            builder.Append("        ");
            var end = Math.Min(data.Count, i + perLine);
            for (var j = i; j < end; j++)
            {
                builder.Append(CultureInfo.InvariantCulture, $"0x{data[j]:X2},");
                if (j + 1 < end)
                {
                    builder.Append(' ');
                }
            }

            builder.Append('\n');
        }
    }

    [GeneratedRegex(@"<tr>\s*<td>(?<name>.*?)</td>\s*<td>(?<tag>.*?)</td>\s*<td>(?<codes>.*?)</td>\s*</tr>", RegexOptions.Singleline | RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture, matchTimeoutMilliseconds: 10_000)]
    private static partial Regex RowRegex();

    [GeneratedRegex("<[^>]*>", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 10_000)]
    private static partial Regex MarkupRegex();

    [GeneratedRegex(@"^'?(?<tag>[A-Z][A-Z0-9]{1,3}) *'?(?<deprecated> \(deprecated\))?$", RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture, matchTimeoutMilliseconds: 10_000)]
    private static partial Regex TagRegex();

    [GeneratedRegex(@"^[a-z]{3}(,\s*[a-z]{3})*", RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture, matchTimeoutMilliseconds: 10_000)]
    private static partial Regex CodesRegex();
}
