using System.IO.Compression;
using System.Xml.Linq;
using Meziantou.Framework.JavaScriptEngine.Intl.Data;

namespace Meziantou.Framework.JavaScriptEngine.Data.Generator.Cldr;

/// <summary>
/// Builds locales.bin (read by CldrLocaleData). cldr-json contains the resolved data of each locale: the generator keeps,
/// for each locale and category, only the values that differ from the resolved data of its parent. The data of a category
/// and a language (e.g. Numbers of "fr", "fr-CA", "fr-CH"...) is a chunk, in which identical strings and identical
/// tables are stored once.
/// </summary>
internal sealed class LocaleDataGenerator(CldrJsonSource source, LocaleSet locales, TreeTable? rootNumbers = null)
{
    // ECMA-402 IsSanctionedSingleUnitIdentifier
    private static readonly HashSet<string> SanctionedUnits = new(StringComparer.Ordinal)
    {
        "acre", "bit", "byte", "celsius", "centimeter", "day", "degree", "fahrenheit", "fluid-ounce", "foot", "gallon",
        "gigabit", "gigabyte", "gram", "hectare", "hour", "inch", "kilobit", "kilobyte", "kilogram", "kilometer", "liter",
        "megabit", "megabyte", "meter", "microsecond", "mile", "mile-scandinavian", "milliliter", "millimeter",
        "millisecond", "minute", "month", "nanosecond", "ounce", "percent", "petabyte", "pound", "second", "stone",
        "terabit", "terabyte", "week", "yard", "year",
    };

    private static readonly CldrCategory[] Categories = Enum.GetValues<CldrCategory>();

    private readonly Dictionary<string, TreeTable?[]> _resolvedCache = new(StringComparer.Ordinal);
    private readonly HashSet<string> _parents = locales.Locales.Select(locales.GetParent).Where(parent => parent is not null).ToHashSet(StringComparer.Ordinal)!;

    /// <summary>Gets the number of values of the parents that the resolved data of a child does not have (stored as <see cref="CldrLocale.NoValueMarker"/>).</summary>
    public int MissingInChildCount { get; private set; }

    /// <summary>Gets the category of each chunk (null for the index).</summary>
    public List<CldrCategory?> ChunkCategories { get; } = [null];

    public List<byte[]> Build()
    {
        var deltas = new Dictionary<(CldrCategory Category, string Locale), TreeTable>();
        foreach (var locale in locales.Locales)
        {
            var resolved = GetResolved(locale);
            var parent = locales.GetParent(locale);
            var parentResolved = parent is null ? null : GetResolved(parent);
            foreach (var category in Categories)
            {
                var tree = resolved[(int)category];
                var parentTree = parentResolved?[(int)category];
                var delta = parentResolved is null ? tree : tree?.Delta(parentTree);

                // A value of the parent that the child does not have must not be inherited (the CLDR marker "no value")
                if (parentTree is not null)
                {
                    foreach (var (path, _) in parentTree.EnumerateLeaves())
                    {
                        if (tree?.GetPath(path) is null)
                        {
                            delta ??= new TreeTable();
                            var table = delta;
                            var segments = path.Split('/');
                            foreach (var segment in segments[..^1])
                            {
                                if (table.TryGetValue(segment, out var existing) && existing is string)
                                    throw new InvalidDataException($"{locale} {category} {path}: the parent has a table where the child has a string");

                                table = table.GetOrAddTable(segment);
                            }

                            table[segments[^1]] = CldrLocale.NoValueMarker;
                            MissingInChildCount++;
                        }
                    }
                }

                if (delta is not null)
                {
                    deltas.Add((category, locale), delta);
                }
            }

            if (!_parents.Contains(locale))
            {
                _resolvedCache.Remove(locale);
            }
        }

        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var delta in deltas.Values)
        {
            delta.CollectKeys(keys);
        }

        var treeKeys = new TreeKeys(keys);
        var localeIndexes = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var i = 0; i < locales.Locales.Count; i++)
        {
            localeIndexes.Add(locales.Locales[i], i);
        }

        var chunks = new List<byte[]> { Array.Empty<byte>() };
        var roots = Categories.Select(_ => new (int Chunk, int Table)[locales.Locales.Count]).ToArray();
        var groups = locales.Locales.GroupBy(LocaleSet.GetGroup, StringComparer.Ordinal).OrderBy(group => group.Key, StringComparer.Ordinal).ToList();
        foreach (var category in Categories)
        {
            foreach (var group in groups)
            {
                var writer = new TreeChunkWriter(treeKeys);
                foreach (var locale in group.Order(StringComparer.Ordinal))
                {
                    if (deltas.TryGetValue((category, locale), out var delta))
                    {
                        roots[(int)category][localeIndexes[locale]] = (chunks.Count, writer.AddTable(delta));
                    }
                }

                if (!writer.IsEmpty)
                {
                    chunks.Add(writer.ToArray());
                    ChunkCategories.Add(category);
                }
            }
        }

        var header = new BinaryBuffer();
        treeKeys.Write(header);
        header.WriteVarUInt(locales.Locales.Count);
        foreach (var locale in locales.Locales)
        {
            header.WriteString(locale);
            var parent = locales.GetParent(locale);
            header.WriteVarUInt(parent is null ? 0 : localeIndexes[parent] + 1);
        }

        header.WriteVarUInt(locales.Aliases.Count);
        foreach (var (alias, target) in locales.Aliases)
        {
            header.WriteString(alias);
            header.WriteVarUInt(localeIndexes[target]);
        }

        header.WriteVarUInt(Categories.Length);
        foreach (var category in Categories)
        {
            header.WriteString(category.ToString());
            for (var i = 0; i < locales.Locales.Count; i++)
            {
                header.WriteVarUInt(roots[(int)category][i].Chunk);
                header.WriteVarUInt(roots[(int)category][i].Table);
            }
        }

        chunks[0] = header.ToArray();
        return chunks;
    }

    /// <summary>Checks that the runtime resolves the same value as cldr-json for every value of every locale; returns the number of differences.</summary>
    public int Verify(CldrLocaleData data)
    {
        var errors = 0;
        foreach (var name in locales.Locales)
        {
            if (!data.TryGetLocale(name, out var locale) || !string.Equals(locale.Name, name, StringComparison.Ordinal))
            {
                Console.WriteLine("Missing locale: " + name);
                errors++;
                continue;
            }

            var resolved = ReadResolved(name);
            var parentResolved = locales.GetParent(name) is { } parentName ? ReadResolved(parentName) : null;
            foreach (var category in Categories)
            {
                if (parentResolved?[(int)category] is { } parentTree)
                {
                    foreach (var (path, _) in parentTree.EnumerateLeaves())
                    {
                        if (resolved[(int)category]?.GetPath(path) is null && locale.TryGetString(category, path, out var unexpected))
                        {
                            Console.WriteLine($"{name} {category} {path}: expected no value, got '{unexpected}'");
                            errors++;
                        }
                    }
                }

                if (resolved[(int)category] is not { } tree)
                    continue;

                foreach (var (path, value) in tree.EnumerateLeaves())
                {
                    if (value == CldrLocale.NoValueMarker)
                        throw new InvalidDataException("cldr-json contains the marker of no value: " + name + " " + path);

                    if (!locale.TryGetString(category, path, out var actual) || !string.Equals(actual, value, StringComparison.Ordinal))
                    {
                        if (errors < 20)
                        {
                            Console.WriteLine($"{name} {category} {path}: expected '{value}', got '{actual}'");
                        }

                        errors++;
                    }
                }
            }
        }

        foreach (var (alias, target) in locales.Aliases)
        {
            if (!data.TryGetLocale(alias, out var locale) || !string.Equals(locale.Name, target, StringComparison.Ordinal))
            {
                Console.WriteLine($"The alias {alias} does not resolve to {target}");
                errors++;
            }
        }

        return errors;
    }

    private TreeTable?[] GetResolved(string locale)
    {
        if (!_resolvedCache.TryGetValue(locale, out var result))
        {
            result = ReadResolved(locale);
            _resolvedCache.Add(locale, result);
        }

        return result;
    }

    private TreeTable?[] ReadResolved(string locale)
    {
        var merged = new TreeTable();
        foreach (var (package, fileName) in source.GetMainFiles(locale))
        {
            if (!IsIncluded(package, fileName))
                continue;

            using var document = source.Read(package + "/main/" + locale + "/" + fileName);
            var tree = (TreeTable)CldrJsonSource.ToTree(document.RootElement.GetProperty("main").GetProperty(locale));
            CldrJsonSource.Merge(merged, tree);
        }

        FlattenValues(merged);
        var result = new TreeTable?[Categories.Length];
        result[(int)CldrCategory.Numbers] = AddRootNumbers(Without(merged.GetPath("numbers"), "currencies", "minimalPairs"));
        result[(int)CldrCategory.Currencies] = merged.GetPath("numbers/currencies") as TreeTable;
        result[(int)CldrCategory.Units] = FilterUnits(merged.GetPath("units") as TreeTable);
        result[(int)CldrCategory.Gregorian] = merged.GetPath("dates/calendars/gregorian") as TreeTable;
        result[(int)CldrCategory.Calendars] = Without(merged.GetPath("dates/calendars"), "gregorian");
        result[(int)CldrCategory.TimeZoneNames] = merged.GetPath("dates/timeZoneNames") as TreeTable;
        result[(int)CldrCategory.Fields] = merged.GetPath("dates/fields") as TreeTable;
        result[(int)CldrCategory.DisplayNames] = Without(merged.GetPath("localeDisplayNames"), "subdivisions", "measurementSystemNames");

        var misc = new TreeTable();
        foreach (var key in (string[])["listPatterns", "layout"])
        {
            if (merged.TryGetValue(key, out var value))
            {
                misc[key] = value;
            }
        }

        result[(int)CldrCategory.Misc] = misc.Count == 0 ? null : misc;
        return result;

        static TreeTable? Without(object? value, params string[] keys)
        {
            if (value is not TreeTable table)
                return null;

            var result = table.Clone();
            foreach (var key in keys)
            {
                result.Remove(key);
            }

            return result.Count == 0 ? null : result;
        }
    }

    // The data of the root for the numbering systems other than latn, which cldr-json does not have: every locale
    // inherits it unless it has its own (e.g. the symbols of "arab" for "en-u-nu-arab")
    private TreeTable? AddRootNumbers(TreeTable? numbers)
    {
        if (rootNumbers is null)
            return numbers;

        numbers ??= new TreeTable();
        foreach (var (key, value) in rootNumbers.Entries)
        {
            if (!numbers.TryGetValue(key, out _))
            {
                numbers[key] = ((TreeTable)value).Clone();
            }
        }

        return numbers;
    }

    /// <summary>
    /// Reads the numbers data of the numbering systems other than latn of common/main/root.xml that are not aliases to
    /// latn (the symbols of "arab" and "arabext", the percent and currency patterns of "arab"), with the paths of
    /// cldr-json.
    /// </summary>
    public static TreeTable ReadRootNumbers(ZipArchive commonArchive)
    {
        var document = XDocument.Parse(CollationGenerator.ReadEntry(commonArchive, "common/main/root.xml"));
        var result = new TreeTable();
        var numbers = document.Root!.Element("numbers") ?? throw new InvalidDataException("root.xml has no numbers");
        foreach (var element in numbers.Elements())
        {
            var numberingSystem = (string?)element.Attribute("numberSystem");
            if (numberingSystem is null or "latn" || element.Element("alias") is not null)
                continue;

            var key = element.Name.LocalName + "-numberSystem-" + numberingSystem;
            switch (element.Name.LocalName)
            {
                case "symbols":
                    var symbols = result.GetOrAddTable(key);
                    foreach (var symbol in element.Elements())
                    {
                        symbols[symbol.Name.LocalName] = symbol.Value;
                    }

                    break;

                case "percentFormats":
                    if (element.Element("percentFormatLength")?.Element("percentFormat")?.Element("pattern") is { } percentPattern)
                    {
                        result.GetOrAddTable(key)["standard"] = percentPattern.Value;
                    }

                    break;

                case "currencyFormats":
                    var standard = element.Element("currencyFormatLength")?.Elements("currencyFormat").FirstOrDefault(format => (string?)format.Attribute("type") == "standard");
                    if (standard is null)
                        break;

                    var formats = result.GetOrAddTable(key);
                    foreach (var pattern in standard.Elements("pattern"))
                    {
                        formats[(string?)pattern.Attribute("alt") is { } alt ? "standard-" + alt : "standard"] = pattern.Value;
                    }

                    var accounting = element.Element("currencyFormatLength")?.Elements("currencyFormat").FirstOrDefault(format => (string?)format.Attribute("type") == "accounting");
                    var accountingPattern = accounting?.Element("alias") is null ? accounting?.Element("pattern")?.Value : null;
                    formats["accounting"] = accountingPattern ?? (string)formats["standard"];
                    break;
            }
        }

        return result;
    }

    // A pattern with attributes, { "_value": "d/M/yy", "_numbers": "M=romanlow" }, becomes two strings: the pattern
    // and "<key>-numbers"
    private static void FlattenValues(TreeTable table)
    {
        foreach (var (key, value) in table.Entries.ToList())
        {
            if (value is not TreeTable child)
                continue;

            if (child.TryGetValue("_value", out var pattern))
            {
                table[key] = pattern;
                foreach (var (attribute, attributeValue) in child.Entries)
                {
                    if (attribute is "_value")
                        continue;

                    if (!attribute.StartsWith('_', StringComparison.Ordinal) || attributeValue is not string)
                        throw new InvalidDataException("Unexpected attribute of a value: " + attribute);

                    table[key + "-" + attribute[1..]] = attributeValue;
                }
            }
            else
            {
                FlattenValues(child);
            }
        }
    }

    private static bool IsIncluded(string package, string fileName)
    {
        return package switch
        {
            "cldr-numbers-full" => fileName is "numbers.json" or "currencies.json",
            "cldr-dates-full" => fileName is "ca-gregorian.json" or "ca-generic.json" or "dateFields.json" or "timeZoneNames.json",
            "cldr-localenames-full" => fileName is "languages.json" or "scripts.json" or "territories.json" or "variants.json" or "localeDisplayNames.json",
            "cldr-misc-full" => fileName is "listPatterns.json" or "layout.json",
            "cldr-units-full" => fileName is "units.json",
            _ => package.StartsWith("cldr-cal-", StringComparison.Ordinal) && fileName.StartsWith("ca-", StringComparison.Ordinal),
        };
    }

    // Keeps the units of ECMA-402, under their identifier ("length-kilometer" -> "kilometer"), the compound units of two
    // of them that CLDR has ("speed-kilometer-per-hour" -> "kilometer-per-hour"), the compound pattern "per" and the
    // duration patterns
    private static TreeTable? FilterUnits(TreeTable? units)
    {
        if (units is null)
            return null;

        var result = new TreeTable();
        foreach (var (key, value) in units.Entries)
        {
            if (key.StartsWith("durationUnit-type-", StringComparison.Ordinal))
            {
                result[key] = value;
                continue;
            }

            if (key is not ("long" or "short" or "narrow"))
                continue;

            var width = new TreeTable();
            foreach (var (unitKey, unitValue) in ((TreeTable)value).Entries)
            {
                if (unitKey is "per")
                {
                    width[unitKey] = unitValue;
                    continue;
                }

                var separator = unitKey.IndexOf('-', StringComparison.Ordinal);
                if (separator > 0 && IsSanctionedUnit(unitKey[(separator + 1)..]))
                {
                    var id = unitKey[(separator + 1)..];
                    if (width.TryGetValue(id, out _))
                        throw new InvalidDataException("Several CLDR units have the identifier " + id);

                    width[id] = unitValue;
                }
            }

            result[key] = width;
        }

        return result;

        static bool IsSanctionedUnit(string id)
        {
            if (SanctionedUnits.Contains(id))
                return true;

            var per = id.IndexOf("-per-", StringComparison.Ordinal);
            return per > 0 && SanctionedUnits.Contains(id[..per]) && SanctionedUnits.Contains(id[(per + 5)..]);
        }
    }
}
