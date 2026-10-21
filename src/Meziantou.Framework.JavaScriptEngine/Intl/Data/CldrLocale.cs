namespace Meziantou.Framework.JavaScriptEngine.Intl.Data;

/// <summary>The symbols of a numbering system in a locale (numbers.json, symbols-numberSystem-*).</summary>
internal sealed record NumberSymbols(
    string Decimal,
    string Group,
    string List,
    string PercentSign,
    string PlusSign,
    string MinusSign,
    string ApproximatelySign,
    string Exponential,
    string SuperscriptingExponent,
    string PerMille,
    string Infinity,
    string NaN,
    string TimeSeparator);

/// <summary>
/// A locale of the CLDR data. The getters resolve the inheritance: a value missing in the locale is the one of its
/// parent (CLDR parentLocales, e.g. "en-AU" -> "en-001" -> "en" -> "und"). The instances are immutable and thread-safe.
/// </summary>
internal sealed class CldrLocale
{
    /// <summary>
    /// The value of CLDR meaning "no value" (U+2205 three times): a locale stores it for a value of its parent that it
    /// does not inherit (e.g. the short metazone names of "en", such as "EST", are not used by "en-001").
    /// </summary>
    public const string NoValueMarker = "\u2205\u2205\u2205";

    private readonly CldrLocaleData _data;
    private readonly int _index;
    private NumberSymbols? _latnSymbols;

    internal CldrLocale(CldrLocaleData data, string name, int index)
    {
        _data = data;
        Name = name;
        _index = index;
    }

    /// <summary>Gets the name of the locale in CLDR (e.g. "fr-CA", "zh-Hant", "und" for the root).</summary>
    public string Name { get; }

    /// <summary>Gets the parent locale, or null for the root.</summary>
    public CldrLocale? Parent { get; internal set; }

    public bool IsRoot => Parent is null;

    /// <summary>Gets the language subtag of the locale ("und" for the root).</summary>
    public string Language => Name.Split('-')[0];

    /// <summary>Gets the root table of the data that the locale stores itself (the values that differ from its parent), or null.</summary>
    public CldrTable? GetOwnTable(CldrCategory category) => _data.GetOwnTable(category, _index);

    /// <summary>Gets a string at a path of a category (e.g. (Numbers, "symbols-numberSystem-latn/decimal")), with inheritance.</summary>
    public bool TryGetString(CldrCategory category, ReadOnlySpan<char> path, [NotNullWhen(true)] out string? value)
    {
        for (var locale = this; locale is not null; locale = locale.Parent)
        {
            if (locale.GetOwnTable(category) is { } table && table.TryGetNode(path, out var node) && node.IsString)
            {
                value = node.GetString();
                if (value is NoValueMarker)
                {
                    value = null;
                    return false;
                }

                return true;
            }
        }

        value = null;
        return false;
    }

    /// <summary>Gets a string at a path of a category, with inheritance, or null when it does not exist.</summary>
    public string? GetString(CldrCategory category, string path) => TryGetString(category, path, out var value) ? value : null;

    /// <summary>Gets whether a path exists in a category (a string or a table), with inheritance.</summary>
    public bool Contains(CldrCategory category, ReadOnlySpan<char> path)
    {
        for (var locale = this; locale is not null; locale = locale.Parent)
        {
            if (locale.GetOwnTable(category) is { } table)
            {
                if (path.IsEmpty)
                    return true;

                if (table.TryGetNode(path, out var node))
                    return !node.IsString || node.GetString() is not NoValueMarker;
            }
        }

        return false;
    }

    /// <summary>
    /// Gets the keys of the table at a path of a category (an empty path for the root of the category), with
    /// inheritance (the union of the keys of the locale and its ancestors), sorted by ordinal. E.g. (Gregorian,
    /// "dateTimeFormats/availableFormats") gives the skeletons.
    /// </summary>
    public IReadOnlyList<string> GetKeys(CldrCategory category, string path)
    {
        var result = new SortedSet<string>(StringComparer.Ordinal);
        var removed = new HashSet<string>(StringComparer.Ordinal);
        for (var locale = this; locale is not null; locale = locale.Parent)
        {
            if (locale.GetOwnTable(category) is not { } table)
                continue;

            if (path.Length > 0)
            {
                if (!table.TryGetNode(path, out var node) || !node.IsTable)
                    continue;

                table = node.GetTable();
            }

            for (var i = 0; i < table.Count; i++)
            {
                var key = table.GetKey(i);
                if (result.Contains(key) || removed.Contains(key))
                    continue;

                if (IsNoValue(table.GetValue(i)))
                {
                    removed.Add(key);
                }
                else
                {
                    result.Add(key);
                }
            }
        }

        return [.. result];
    }

    // A "no value" marker, or a table whose values are all "no value" markers
    private static bool IsNoValue(CldrNode node)
    {
        if (node.IsString)
            return node.GetString() is NoValueMarker;

        var table = node.GetTable();
        for (var i = 0; i < table.Count; i++)
        {
            if (!IsNoValue(table.GetValue(i)))
                return false;
        }

        return table.Count > 0;
    }

    /// <summary>Gets the default numbering system of the locale (e.g. "latn", "arab" for "ar-EG").</summary>
    public string GetDefaultNumberingSystem() => GetString(CldrCategory.Numbers, "defaultNumberingSystem") ?? "latn";

    /// <summary>Gets the symbols of a numbering system, or of "latn" when the locale has none for it.</summary>
    public NumberSymbols GetNumberSymbols(string numberingSystem)
    {
        if (numberingSystem is "latn" && Volatile.Read(ref _latnSymbols) is { } cached)
            return cached;

        var prefix = Contains(CldrCategory.Numbers, "symbols-numberSystem-" + numberingSystem) ? "symbols-numberSystem-" + numberingSystem + "/" : "symbols-numberSystem-latn/";
        var result = new NumberSymbols(
            Get("decimal"),
            Get("group"),
            Get("list"),
            Get("percentSign"),
            Get("plusSign"),
            Get("minusSign"),
            Get("approximatelySign"),
            Get("exponential"),
            Get("superscriptingExponent"),
            Get("perMille"),
            Get("infinity"),
            Get("nan"),
            Get("timeSeparator"));
        if (numberingSystem is "latn")
        {
            result = CldrData.InitializeOnce(ref _latnSymbols, result);
        }

        return result;

        string Get(string name) => GetString(CldrCategory.Numbers, prefix + name) ?? GetString(CldrCategory.Numbers, "symbols-numberSystem-latn/" + name) ?? "";
    }

    /// <summary>
    /// Gets a pattern of the numbers of a numbering system, falling back to "latn" (e.g. ("arab", "decimalFormats",
    /// "standard") reads "decimalFormats-numberSystem-arab/standard").
    /// </summary>
    public string? GetNumberPattern(string numberingSystem, string group, string path)
    {
        return GetString(CldrCategory.Numbers, group + "-numberSystem-" + numberingSystem + "/" + path)
            ?? GetString(CldrCategory.Numbers, group + "-numberSystem-latn/" + path);
    }

    public override string ToString() => Name;
}
