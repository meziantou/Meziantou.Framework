using System.Collections.Concurrent;
using System.Globalization;
using System.Runtime.InteropServices;

namespace Meziantou.Framework.JavaScriptEngine.Intl.Data;

/// <summary>The fraction digits and rounding increments of a currency (supplemental currencyData.json).</summary>
[StructLayout(LayoutKind.Auto)]
internal readonly record struct CurrencyDigits(int Digits, int Rounding, int CashDigits, int CashRounding);

/// <summary>The hour cycles of a region or a locale (supplemental timeData.json), e.g. allowed ["h", "hb", "H", "hB"] and preferred "h" for "US".</summary>
internal sealed record TimeData(IReadOnlyList<string> Allowed, string Preferred);

/// <summary>The week data of a region (supplemental weekData.json); the days are 1 (Monday) to 7 (Sunday).</summary>
[StructLayout(LayoutKind.Auto)]
internal readonly record struct WeekData(int FirstDay, int MinimalDays, int WeekendStart, int WeekendEnd);

/// <summary>A rule of the day periods of a language (supplemental dayPeriods.json): the minutes since midnight, <c>At</c> for "midnight" and "noon", else [From, Before).</summary>
[StructLayout(LayoutKind.Auto)]
internal readonly record struct DayPeriodRule(string Period, int? At, int From, int Before);

/// <summary>A period during which a time zone uses a metazone (supplemental metaZones.json); the bounds are in seconds since the epoch, null when unbounded.</summary>
[StructLayout(LayoutKind.Auto)]
internal readonly record struct MetazonePeriod(string Metazone, long? From, long? To);

internal enum LocaleAliasType
{
    Language,
    Script,
    Territory,
    Variant,
    Subdivision,
    Zone,
}

/// <summary>
/// The supplemental data of CLDR (Intl/Data/supplemental.bin), one data tree per section, each section in its own chunk.
/// The sections and their content (the keys of cldr-json, with the leading '_' of the attributes kept):
/// <list type="bullet">
/// <item>likelySubtags: "und-TW" = "zh-Hant-TW"</item>
/// <item>aliases: languageAlias, scriptAlias, territoryAlias, variantAlias, subdivisionAlias, zoneAlias (nested by '/'): key = replacement (space-separated for several regions)</item>
/// <item>plurals, ordinals: language = { "pluralRule-count-one" = "i = 1 and v = 0 @integer 1" }</item>
/// <item>pluralRanges: language = { "pluralRange-start-one-end-other" = "other" }</item>
/// <item>currencyData: fractions = { "CHF" = { _digits, _rounding, _cashDigits, _cashRounding }, "DEFAULT" = ... }</item>
/// <item>timeData: region or locale = { _allowed, _preferred }</item>
/// <item>weekData: minDays, firstDay, weekendStart, weekendEnd = { region = value }, weekOfPreference</item>
/// <item>calendarPreferenceData: region = "gregorian islamic-umalqura ..."</item>
/// <item>calendarData: calendar = { calendarSystem, eras = { index = { _code, _aliases, _start, _end } }, inheritEras }</item>
/// <item>numberingSystems: name = { _type ("numeric" or "algorithmic"), _digits, _rules }</item>
/// <item>metaZones: zones = { "Europe" = { "Paris" = { "0" = { mzone, from, to } } } } (from and to in seconds since the epoch), mapZones = { metazone = { territory = zone } }</item>
/// <item>dayPeriods: language = { period = { _at | _from, _before } }</item>
/// <item>primaryZones: region = zone</item>
/// <item>bcp47: key (e.g. "ca", "co", "nu", "tz") = { _alias, type = { _alias, _preferred, _deprecated } }</item>
/// </list>
/// </summary>
internal sealed class CldrSupplemental
{
    private readonly CldrChunkedFile _file;
    private readonly CldrKeys _keys;
    private readonly Dictionary<string, (int Chunk, int Table)> _sections;
    private readonly CldrTreeChunk?[] _chunks;
    private readonly ConcurrentDictionary<string, PluralRules?> _cardinalRules = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, PluralRules?> _ordinalRules = new(StringComparer.Ordinal);
    private LikelySubtags? _likelySubtags;

    internal CldrSupplemental(CldrChunkedFile file)
    {
        _file = file;
        var reader = new CldrBinaryReader(file.GetChunk(0), 0);
        _keys = CldrKeys.Read(ref reader);
        var count = reader.ReadVarInt32();
        _sections = new Dictionary<string, (int, int)>(count, StringComparer.Ordinal);
        for (var i = 0; i < count; i++)
        {
            var name = reader.ReadString();
            var chunk = reader.ReadVarInt32();
            var table = reader.ReadVarInt32();
            _sections.Add(name, (chunk, table));
        }

        _chunks = new CldrTreeChunk?[file.ChunkCount];
    }

    /// <summary>Gets the names of the sections.</summary>
    public IEnumerable<string> SectionNames => _sections.Keys;

    public LikelySubtags LikelySubtags => Volatile.Read(ref _likelySubtags) ?? CldrData.InitializeOnce(ref _likelySubtags, new LikelySubtags(GetRequiredSection("likelySubtags")));

    /// <summary>Gets the root table of a section, or null when the section does not exist.</summary>
    public CldrTable? GetSection(string name)
    {
        if (!_sections.TryGetValue(name, out var location))
            return null;

        var chunk = Volatile.Read(ref _chunks[location.Chunk]);
        if (chunk is null)
        {
            chunk = new CldrTreeChunk(_keys, _file.GetChunk(location.Chunk));
            chunk = Interlocked.CompareExchange(ref _chunks[location.Chunk], chunk, comparand: null) ?? chunk;
        }

        return chunk.GetTable(location.Table);
    }

    /// <summary>Gets a string of a section (e.g. GetString("numberingSystems", "arab/_digits")).</summary>
    public string? GetString(string section, string path)
    {
        return GetSection(section) is { } table && table.TryGetNode(path, out var node) && node.IsString ? node.GetString() : null;
    }

    /// <summary>Gets the replacement of an alias of UTS 35 canonicalization (e.g. Language "iw" -> "he", Territory "SU" -> "RU AM AZ ...").</summary>
    public bool TryGetAlias(LocaleAliasType type, string key, [NotNullWhen(true)] out string? replacement)
    {
        var name = type switch
        {
            LocaleAliasType.Language => "languageAlias",
            LocaleAliasType.Script => "scriptAlias",
            LocaleAliasType.Territory => "territoryAlias",
            LocaleAliasType.Variant => "variantAlias",
            LocaleAliasType.Subdivision => "subdivisionAlias",
            _ => "zoneAlias",
        };

        if (GetSection("aliases") is { } aliases && aliases.TryGetNode(name, out var node) && node.IsTable && node.GetTable().TryGetNode(key, out var value) && value.IsString)
        {
            replacement = value.GetString();
            return true;
        }

        replacement = null;
        return false;
    }

    /// <summary>
    /// Gets the plural rules of a locale: the rules of the locale (e.g. "pt-PT"), else of its language, else of the root
    /// (a single "other" category).
    /// </summary>
    public PluralRules GetPluralRules(string locale, PluralRuleType type)
    {
        var cache = type is PluralRuleType.Cardinal ? _cardinalRules : _ordinalRules;
        var key = locale;
        while (true)
        {
            if (cache.TryGetValue(key, out var cached) && cached is not null)
                return cached;

            if (GetSection(type is PluralRuleType.Cardinal ? "plurals" : "ordinals") is { } section && section.TryGetValue(key, out var node) && node.IsTable)
            {
                var rules = PluralRules.Create(key, type, node.GetTable());
                return cache.GetOrAdd(key, rules)!;
            }

            var separator = key.LastIndexOf('-', StringComparison.Ordinal);
            if (separator < 0)
                break;

            key = key[..separator];
        }

        return cache.GetOrAdd("root", static (_, state) => state.Supplemental.CreateRootRules(state.Type), (Supplemental: this, Type: type))!;
    }

    private PluralRules CreateRootRules(PluralRuleType type)
    {
        if (GetSection(type is PluralRuleType.Cardinal ? "plurals" : "ordinals") is { } section && section.TryGetValue("root", out var node) && node.IsTable)
            return PluralRules.Create("root", type, node.GetTable());

        return PluralRules.CreateOtherOnly(type);
    }

    /// <summary>Gets the category of a plural range (supplemental pluralRanges.json), e.g. ("fr", "one", "other") -> "other"; null when the language has no data.</summary>
    public string? GetPluralRange(string language, string start, string end)
    {
        return GetSection("pluralRanges") is { } section && section.TryGetValue(language, out var node) && node.IsTable && node.GetTable().TryGetString("pluralRange-start-" + start + "-end-" + end, out var result) ? result : null;
    }

    /// <summary>Gets the digits of a currency (ISO 4217 code), or the default ones (2 digits) when the currency has no data.</summary>
    public CurrencyDigits GetCurrencyDigits(string currency)
    {
        var fractions = GetSection("currencyData")?.TryGetNode("fractions", out var node) == true ? node.GetTable() : null;
        if (fractions is null || (!fractions.TryGetValue(currency, out var entry) && !fractions.TryGetValue("DEFAULT", out entry)))
            return new CurrencyDigits(2, 0, 2, 0);

        var table = entry.GetTable();
        var digits = GetInt32(table, "_digits", 2);
        var rounding = GetInt32(table, "_rounding", 0);
        return new CurrencyDigits(digits, rounding, GetInt32(table, "_cashDigits", digits), GetInt32(table, "_cashRounding", rounding));
    }

    /// <summary>Gets the hour cycles of a key of timeData: a locale ("fr-CA"), a region ("US") or "001".</summary>
    public TimeData? GetTimeData(string key)
    {
        if (GetSection("timeData") is not { } section || !section.TryGetValue(key, out var node) || !node.IsTable)
            return null;

        var table = node.GetTable();
        table.TryGetString("_allowed", out var allowed);
        table.TryGetString("_preferred", out var preferred);
        return new TimeData((allowed ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries), preferred ?? "H");
    }

    /// <summary>Gets the hour cycles of a locale (UTS 35 "Time Data"): the entry of language-region, else of the region, else of "001".</summary>
    public TimeData GetTimeData(LanguageId maximizedLocale)
    {
        return GetTimeData(maximizedLocale.Language + "-" + maximizedLocale.Region)
            ?? GetTimeData(maximizedLocale.Region)
            ?? GetTimeData("001")
            ?? throw new InvalidDataException("The time data of the world (001) is missing");
    }

    /// <summary>Gets the week data of a region, with the values of the world (001) for the missing ones.</summary>
    public WeekData GetWeekData(string region)
    {
        var section = GetSection("weekData");
        return new WeekData(GetDay("firstDay"), int.Parse(GetValue("minDays") ?? "1", NumberStyles.None, CultureInfo.InvariantCulture), GetDay("weekendStart"), GetDay("weekendEnd"));

        string? GetValue(string name)
        {
            if (section is null || !section.TryGetValue(name, out var node) || !node.IsTable)
                return null;

            var table = node.GetTable();
            return table.TryGetString(region, out var value) || table.TryGetString("001", out value) ? value : null;
        }

        int GetDay(string name) => GetValue(name) switch
        {
            "mon" => 1,
            "tue" => 2,
            "wed" => 3,
            "thu" => 4,
            "fri" => 5,
            "sat" => 6,
            "sun" => 7,
            var value => throw new InvalidDataException("Invalid day in weekData: " + value),
        };
    }

    /// <summary>Gets the preferred calendars of a region (CLDR identifiers, e.g. "gregorian", "islamic-umalqura"), else of the world (001).</summary>
    public IReadOnlyList<string> GetCalendarPreferences(string region)
    {
        var section = GetSection("calendarPreferenceData");
        if (section is not null && (section.TryGetString(region, out var value) || section.TryGetString("001", out value)))
            return value.Split(' ');

        return ["gregorian"];
    }

    /// <summary>Gets the digits of a numbering system whose type is "numeric" (e.g. "arab" -> U+0660 to U+0669), or null.</summary>
    public string? GetNumberingSystemDigits(string numberingSystem)
    {
        if (GetSection("numberingSystems") is not { } section || !section.TryGetValue(numberingSystem, out var node) || !node.IsTable)
            return null;

        var table = node.GetTable();
        return table.TryGetString("_type", out var type) && type is "numeric" && table.TryGetString("_digits", out var digits) ? digits : null;
    }

    /// <summary>Gets the names of the numbering systems whose type is "numeric", sorted by ordinal.</summary>
    public IReadOnlyList<string> GetNumericNumberingSystems()
    {
        var result = new List<string>();
        if (GetSection("numberingSystems") is { } section)
        {
            for (var i = 0; i < section.Count; i++)
            {
                if (section.GetValue(i).GetTable().TryGetString("_type", out var type) && type is "numeric")
                {
                    result.Add(section.GetKey(i));
                }
            }
        }

        return result;
    }

    /// <summary>Gets the periods of the metazones used by a time zone (CLDR uses the old identifiers, e.g. "Asia/Calcutta").</summary>
    public IReadOnlyList<MetazonePeriod> GetMetazonePeriods(string timeZone)
    {
        if (GetSection("metaZones") is not { } section || !section.TryGetNode("zones/" + timeZone, out var node) || !node.IsTable)
            return [];

        var table = node.GetTable();
        var result = new MetazonePeriod[table.Count];
        for (var i = 0; i < table.Count; i++)
        {
            var period = table.GetValue(i).GetTable();
            var index = int.Parse(table.GetKey(i), NumberStyles.None, CultureInfo.InvariantCulture);
            period.TryGetString("mzone", out var metazone);
            result[index] = new MetazonePeriod(metazone!, GetInt64(period, "from"), GetInt64(period, "to"));
        }

        return result;
    }

    /// <summary>Gets the metazone of a time zone at an instant (seconds since the epoch), or null when the zone uses none.</summary>
    public string? GetMetazone(string timeZone, long epochSeconds)
    {
        foreach (var period in GetMetazonePeriods(timeZone))
        {
            if ((period.From is null || epochSeconds >= period.From) && (period.To is null || epochSeconds < period.To))
                return period.Metazone;
        }

        return null;
    }

    /// <summary>Gets the zone of a metazone in a territory (mapZone, "001" for the golden zone), e.g. ("Europe_Central", "001") -> "Europe/Paris".</summary>
    public string? GetMetazoneZone(string metazone, string territory)
    {
        return GetSection("metaZones") is { } section && section.TryGetNode("mapZones/" + metazone + "/" + territory, out var node) && node.IsString ? node.GetString() : null;
    }

    /// <summary>Gets the primary zone of a region with several zones (supplemental primaryZones.json), e.g. "DE" -> "Europe/Berlin".</summary>
    public string? GetPrimaryZone(string region) => GetString("primaryZones", region);

    /// <summary>Gets the day period rules of a language (supplemental dayPeriods.json), or an empty list.</summary>
    public IReadOnlyList<DayPeriodRule> GetDayPeriodRules(string language)
    {
        if (GetSection("dayPeriods") is not { } section || !section.TryGetValue(language, out var node) || !node.IsTable)
            return [];

        var table = node.GetTable();
        var result = new DayPeriodRule[table.Count];
        for (var i = 0; i < table.Count; i++)
        {
            var rule = table.GetValue(i).GetTable();
            result[i] = new DayPeriodRule(table.GetKey(i), GetMinutes(rule, "_at"), GetMinutes(rule, "_from") ?? 0, GetMinutes(rule, "_before") ?? 0);
        }

        return result;

        static int? GetMinutes(CldrTable rule, string name)
        {
            if (!rule.TryGetString(name, out var value))
                return null;

            var separator = value.IndexOf(':', StringComparison.Ordinal);
            return (int.Parse(value.AsSpan(0, separator), NumberStyles.None, CultureInfo.InvariantCulture) * 60) + int.Parse(value.AsSpan(separator + 1), NumberStyles.None, CultureInfo.InvariantCulture);
        }
    }

    private CldrTable GetRequiredSection(string name) => GetSection(name) ?? throw new InvalidDataException("The section of the supplemental data is missing: " + name);

    private static int GetInt32(CldrTable table, string key, int defaultValue)
    {
        return table.TryGetString(key, out var value) ? int.Parse(value, NumberStyles.None, CultureInfo.InvariantCulture) : defaultValue;
    }

    private static long? GetInt64(CldrTable table, string key)
    {
        return table.TryGetString(key, out var value) ? long.Parse(value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture) : null;
    }
}
