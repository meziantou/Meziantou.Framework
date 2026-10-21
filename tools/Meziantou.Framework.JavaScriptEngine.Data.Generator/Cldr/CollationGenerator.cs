using System.Globalization;
using System.IO.Compression;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Meziantou.Framework.JavaScriptEngine.Intl.Data.Collation;

namespace Meziantou.Framework.JavaScriptEngine.Data.Generator.Cldr;

/// <summary>
/// Builds collation.bin (read by CollationData): the root collation from allkeys_CLDR.txt, the implicit weights of
/// the siniform scripts from FractionalUCA.txt, the Unified_Ideograph ranges from PropList.txt, and the tailoring
/// rules of the CLDR XML (common/collation/*.xml) of the languages of the embedded locales. The rules are stored as
/// text, with the imports inlined and the comments removed, and are checked with the parser of the engine.
/// </summary>
internal static partial class CollationGenerator
{
    // The BCP 47 values of the collation types (bcp47/collation.json) that differ from the CLDR types of the XML
    private static readonly Dictionary<string, string> Bcp47Types = new(StringComparer.Ordinal)
    {
        ["phonebk"] = "phonebook",
        ["trad"] = "traditional",
        ["dict"] = "dictionary",
        ["gb2312"] = "gb2312han",
    };

    public static List<byte[]> Build(ZipArchive common, string propList, LocaleSet locales, IReadOnlyDictionary<string, string> collationParents, out string ucaVersion)
    {
        var allKeys = ReadEntry(common, "common/uca/allkeys_CLDR.txt");
        var root = BuildRootTable(allKeys, ReadEntry(common, "common/uca/FractionalUCA.txt"), propList, out ucaVersion);

        // The tailorings of every XML file, by locale and type
        var tailorings = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);
        var defaults = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var entry in common.Entries.Where(entry => entry.FullName.StartsWith("common/collation/", StringComparison.Ordinal) && entry.FullName.EndsWith(".xml", StringComparison.Ordinal)).OrderBy(entry => entry.FullName, StringComparer.Ordinal))
        {
            var locale = Path.GetFileNameWithoutExtension(entry.Name).Replace('_', '-');
            if (locale is "root")
            {
                locale = LocaleSet.Root;
            }

            using var stream = entry.Open();
            var document = XDocument.Load(stream);
            var types = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var collation in document.Descendants("collation"))
            {
                // As ICU, the proposed alternatives and the draft data below "contributed" are not included (e.g. the
                // provisional "eor" of de, whose [reorder others digit] ICU does not have)
                if (collation.Attribute("alt") is not null || (string?)collation.Attribute("draft") is "unconfirmed" or "provisional")
                    continue;

                var type = (string)collation.Attribute("type")!;
                var rules = (string?)collation.Element("cr") ?? "";
                types.Add(type, rules);
            }

            if (document.Descendants("defaultCollation").FirstOrDefault() is { } defaultCollation)
            {
                defaults.Add(locale, defaultCollation.Value.Trim());
            }

            tailorings.Add(locale, types);
        }

        // Keep the languages of the embedded locales; inline the imports, remove the comments and check the syntax
        var languages = locales.Locales.Select(LocaleSet.GetGroup).ToHashSet(StringComparer.Ordinal);
        var trees = new SortedDictionary<string, TreeTable>(StringComparer.Ordinal);
        foreach (var (locale, types) in tailorings.OrderBy(item => item.Key, StringComparer.Ordinal))
        {
            if (!languages.Contains(LocaleSet.GetGroup(locale)))
                continue;

            var tree = new TreeTable();
            foreach (var (type, _) in types.OrderBy(item => item.Key, StringComparer.Ordinal))
            {
                if (type.StartsWith("private-", StringComparison.Ordinal))
                    continue;

                var rules = Resolve(locale, type, tailorings, depth: 0);
                try
                {
                    _ = CollationTailoring.Parse(rules);
                }
                catch (FormatException ex)
                {
                    throw new InvalidDataException($"Invalid collation rules of {locale} {type}: {ex.Message}", ex);
                }

                tree[type] = rules;
            }

            if (defaults.TryGetValue(locale, out var defaultType))
            {
                tree["_default"] = defaultType;
            }

            if (tree.Count > 0)
            {
                trees.Add(locale, tree);
            }
        }

        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var tree in trees.Values)
        {
            tree.CollectKeys(keys);
        }

        var treeKeys = new TreeKeys(keys);
        var chunks = new List<byte[]> { Array.Empty<byte>(), root };
        var locations = new List<(string Locale, int Chunk, int Table)>();
        foreach (var group in trees.GroupBy(item => LocaleSet.GetGroup(item.Key), StringComparer.Ordinal).OrderBy(group => group.Key, StringComparer.Ordinal))
        {
            var writer = new TreeChunkWriter(treeKeys);
            foreach (var (locale, tree) in group)
            {
                locations.Add((locale, chunks.Count, writer.AddTable(tree)));
            }

            chunks.Add(writer.ToArray());
        }

        var header = new BinaryBuffer();
        treeKeys.Write(header);
        header.WriteString(ucaVersion);
        header.WriteVarUInt(1);
        locations.Sort((a, b) => string.CompareOrdinal(a.Locale, b.Locale));
        header.WriteVarUInt(locations.Count);
        foreach (var (locale, chunk, table) in locations)
        {
            header.WriteString(locale);
            header.WriteVarUInt(chunk);
            header.WriteVarUInt(table);
        }

        header.WriteVarUInt(collationParents.Count);
        foreach (var (locale, parent) in collationParents.OrderBy(item => item.Key, StringComparer.Ordinal))
        {
            header.WriteString(locale);
            header.WriteString(parent);
        }

        chunks[0] = header.ToArray();
        return chunks;
    }

    public static string ReadEntry(ZipArchive archive, string name)
    {
        var entry = archive.GetEntry(name) ?? throw new FileNotFoundException("Missing file in the CLDR archive: " + name);
        using var reader = new StreamReader(entry.Open(), Encoding.UTF8);
        return reader.ReadToEnd();
    }

    // Inlines the imports ("[import de-u-co-phonebk]", "[import hr]") and removes the comments and the blank lines
    private static string Resolve(string locale, string type, Dictionary<string, Dictionary<string, string>> tailorings, int depth)
    {
        if (depth > 10)
            throw new InvalidDataException("Recursive collation import: " + locale + " " + type);

        if (!tailorings.TryGetValue(locale, out var types) || !types.TryGetValue(type, out var rules))
            throw new InvalidDataException($"Missing imported collation: {locale} {type}");

        var result = new StringBuilder();
        foreach (var rawLine in rules.Split('\n'))
        {
            var line = RemoveComment(rawLine).Trim();
            if (line.Length == 0)
                continue;

            var import = ImportRegex().Match(line);
            if (import.Success)
            {
                var source = import.Groups["source"].Value;
                var separator = source.IndexOf("-u-co-", StringComparison.Ordinal);
                var importedLocale = separator < 0 ? source : source[..separator];
                var importedType = separator < 0 ? "standard" : source[(separator + 6)..];
                importedType = Bcp47Types.GetValueOrDefault(importedType, importedType);
                importedLocale = importedLocale.Replace('_', '-');
                var imported = Resolve(importedLocale, importedType, tailorings, depth + 1);
                if (imported.Length > 0)
                {
                    result.Append(imported).Append('\n');
                }

                if (import.Index + import.Length < line.Length)
                    throw new InvalidDataException("Unexpected text after an import: " + line);

                continue;
            }

            result.Append(line).Append('\n');
        }

        return result.ToString().TrimEnd('\n');
    }

    // '#' starts a comment, except in a quoted text or after a backslash
    private static string RemoveComment(string line)
    {
        var quoted = false;
        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];
            if (c == '\\')
            {
                i++;
            }
            else if (c == '\'')
            {
                quoted = !quoted;
            }
            else if (c == '#' && !quoted)
            {
                return line[..i];
            }
        }

        return line;
    }

    private static byte[] BuildRootTable(string allKeys, string fractionalUca, string propList, out string ucaVersion)
    {
        ucaVersion = GetUcaVersion(allKeys);
        var entries = new List<(int[] Key, List<CollationElement> Elements)>();
        foreach (var rawLine in allKeys.Split('\n'))
        {
            var line = rawLine;
            var comment = line.IndexOf('#', StringComparison.Ordinal);
            if (comment >= 0)
            {
                line = line[..comment];
            }

            line = line.Trim();
            if (line.Length == 0 || line[0] == '@')
                continue;

            var separator = line.IndexOf(';', StringComparison.Ordinal);
            var key = line[..separator].Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(item => int.Parse(item, NumberStyles.HexNumber, CultureInfo.InvariantCulture)).ToArray();
            var elements = new List<CollationElement>();
            foreach (Match match in ElementRegex().Matches(line[(separator + 1)..]))
            {
                elements.Add(CollationElement.Create(
                    int.Parse(match.Groups["p"].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture),
                    int.Parse(match.Groups["s"].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture),
                    int.Parse(match.Groups["t"].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture),
                    match.Groups["v"].Value == "*"));
            }

            if (elements.Count == 0)
                throw new InvalidDataException("Invalid line of allkeys_CLDR.txt: " + rawLine);

            entries.Add((key, elements));
        }

        entries.Sort((a, b) => a.Key.AsSpan().SequenceCompareTo(b.Key));
        var output = new BinaryBuffer();
        output.WriteString(ucaVersion);
        output.WriteVarUInt(entries.Count);
        foreach (var (key, elements) in entries)
        {
            output.WriteVarUInt(key.Length);
            foreach (var codePoint in key)
            {
                output.WriteVarUInt(codePoint);
            }

            output.WriteVarUInt(elements.Count);
            foreach (var element in elements)
            {
                output.WriteVarUInt(element.Value);
            }
        }

        // The implicit weights of the siniform scripts: FractionalUCA.txt gives the DUCET weights in a comment
        //   17000; [80 7D 04, 05, 05]	# Tang Lo	[FB00.0020.0002][8000.0000.0000]	* TANGUT IDEOGRAPH-17000
        var ranges = new List<(int Start, int End, int Base, int FirstSecondWeight)>();
        foreach (Match match in SiniformRegex().Matches(fractionalUca))
        {
            var codePoint = int.Parse(match.Groups["cp"].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            var @base = int.Parse(match.Groups["base"].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            var second = int.Parse(match.Groups["second"].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            if (ranges.Count > 0 && ranges[^1].End == codePoint - 1 && ranges[^1].Base == @base && ranges[^1].FirstSecondWeight + (codePoint - ranges[^1].Start) == second)
            {
                ranges[^1] = ranges[^1] with { End = codePoint };
            }
            else
            {
                ranges.Add((codePoint, codePoint, @base, second));
            }
        }

        if (ranges.Count == 0)
            throw new InvalidDataException("No implicit weights of the siniform scripts in FractionalUCA.txt");

        output.WriteVarUInt(ranges.Count);
        foreach (var (start, end, @base, second) in ranges)
        {
            output.WriteVarUInt(start);
            output.WriteVarUInt(end);
            output.WriteVarUInt(@base);
            output.WriteVarUInt(second);
        }

        var ideographs = new List<(int Start, int End)>();
        foreach (Match match in PropListRegex().Matches(propList))
        {
            var start = int.Parse(match.Groups["start"].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            var end = match.Groups["end"].Success ? int.Parse(match.Groups["end"].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture) : start;
            ideographs.Add((start, end));
        }

        if (ideographs.Count == 0)
            throw new InvalidDataException("No Unified_Ideograph in PropList.txt");

        output.WriteVarUInt(ideographs.Count);
        foreach (var (start, end) in ideographs)
        {
            output.WriteVarUInt(start);
            output.WriteVarUInt(end);
        }

        return output.ToArray();
    }

    public static string GetUcaVersion(string allKeys)
    {
        var match = VersionRegex().Match(allKeys);
        return match.Success ? match.Groups["version"].Value : throw new InvalidDataException("No @version in allkeys_CLDR.txt");
    }

    [GeneratedRegex(@"^\s*\[import\s+(?<source>[A-Za-z0-9_-]+)\s*\]", RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture, matchTimeoutMilliseconds: 10000)]
    private static partial Regex ImportRegex();

    [GeneratedRegex(@"\[(?<v>[.*])(?<p>[0-9A-F]{4})\.(?<s>[0-9A-F]{4})\.(?<t>[0-9A-F]{4})\]", RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture, matchTimeoutMilliseconds: 10000)]
    private static partial Regex ElementRegex();

    [GeneratedRegex(@"^(?<cp>[0-9A-F]{4,6});[^#\n]*#[^\[\n]*\[(?<base>FB[0-3][0-9A-F])\.0020\.0002\]\[(?<second>[0-9A-F]{4})\.0000\.0000\]", RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture | RegexOptions.Multiline, matchTimeoutMilliseconds: 10000)]
    private static partial Regex SiniformRegex();

    [GeneratedRegex(@"^(?<start>[0-9A-F]{4,6})(\.\.(?<end>[0-9A-F]{4,6}))?\s*;\s*Unified_Ideograph\b", RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture | RegexOptions.Multiline, matchTimeoutMilliseconds: 10000)]
    private static partial Regex PropListRegex();

    [GeneratedRegex(@"^@version\s+(?<version>[0-9.]+)", RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture | RegexOptions.Multiline, matchTimeoutMilliseconds: 10000)]
    private static partial Regex VersionRegex();
}
