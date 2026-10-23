using System.Runtime.InteropServices;
using Meziantou.Framework.JavaScriptEngine.TimeZones;

namespace Meziantou.Framework.JavaScriptEngine.Data.Generator;

/// <summary>A day of a month in the tz source: a day number, "lastSun", "Sun&gt;=8" or "Sun&lt;=25".</summary>
[StructLayout(LayoutKind.Auto)]
internal readonly record struct DaySpec(TimeZoneRuleDayKind Kind, int Day, int Weekday);

/// <summary>A Rule line.</summary>
internal sealed record RuleLine(string Name, int FromYear, int ToYear, int Month, DaySpec Day, int AtSeconds, TimeZoneTimeKind AtKind, int SaveSeconds, bool IsDst, string Letter)
{
    public const int MaxYear = int.MaxValue;

    public bool IsRecurring => ToYear == MaxYear;
}

/// <summary>The RULES field of a Zone line.</summary>
internal enum ZoneRulesKind
{
    None,
    FixedSave,
    Named,
}

/// <summary>A Zone line or a continuation line.</summary>
internal sealed record ZoneLine(int StandardOffsetSeconds, ZoneRulesKind RulesKind, int FixedSaveSeconds, bool FixedIsDst, string? RuleName, string Format, bool HasUntil, int UntilYear, int UntilMonth, DaySpec UntilDay, int UntilTimeSeconds, TimeZoneTimeKind UntilKind);

/// <summary>A zone: its name and its lines.</summary>
internal sealed record ZoneDefinition(string Name, List<ZoneLine> Lines, List<string> SourceLines);

/// <summary>
/// The content of the tz source files, read in "main" form like zic with the build options
/// <c>PACKRATDATA=backzone PACKRATLIST=zone.tab</c> (what ICU uses, and what ECMA-402 recommends): a zone of backzone
/// replaces the Link of the same name when the zone is listed in zone.tab, so that each country has its own zone with
/// its history before 1970.
/// </summary>
internal sealed class TzdbSource
{
    /// <summary>The files compiled by zic, in this order (TDATA without factory, then PACKRATDATA).</summary>
    public static readonly string[] DataFiles = ["africa", "antarctica", "asia", "australasia", "europe", "northamerica", "southamerica", "etcetera", "backward"];

    public const string BackzoneFile = "backzone";
    public const string ZoneTabFile = "zone.tab";

    /// <summary>
    /// The Zones of the tz source that ECMA-402 keeps as Links to a geographical zone. tzdata 2026d made the System V
    /// names Zones again (they were links from 2024b to 2026c); TC39-TG2 decided to keep them as aliases, as browsers
    /// and test262 do (https://github.com/tc39/ecma402/issues/1103).
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string> Ecma402Links = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["EST5EDT"] = "America/New_York",
        ["CST6CDT"] = "America/Chicago",
        ["MST7MDT"] = "America/Denver",
        ["PST8PDT"] = "America/Los_Angeles",
    };

    private static readonly string[] MonthNames = ["January", "February", "March", "April", "May", "June", "July", "August", "September", "October", "November", "December"];
    private static readonly string[] WeekdayNames = ["Sunday", "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday"];

    public Dictionary<string, List<RuleLine>> Rules { get; } = new(StringComparer.Ordinal);

    /// <summary>The zones, by name.</summary>
    public Dictionary<string, ZoneDefinition> Zones { get; } = new(StringComparer.Ordinal);

    /// <summary>The links (name to target), in the order of the source.</summary>
    public Dictionary<string, string> Links { get; } = new(StringComparer.Ordinal);

    /// <summary>The Rule and Link source lines that were kept (for the comparison with zic).</summary>
    public List<string> OtherSourceLines { get; } = [];

    public static TzdbSource Parse(Func<string, string> readFile)
    {
        var source = new TzdbSource();
        foreach (var file in DataFiles)
        {
            source.ParseFile(readFile(file), packratList: null);
        }

        var packratList = new HashSet<string>(StringComparer.Ordinal);
        foreach (var line in readFile(ZoneTabFile).Split('\n'))
        {
            if (line.Length == 0 || line[0] == '#')
                continue;

            var fields = line.Split('\t');
            packratList.Add(fields[2]);
        }

        source.ParseFile(readFile(BackzoneFile), packratList);
        source.ApplyEcma402Links();
        return source;
    }

    /// <summary>Replaces the Zones of <see cref="Ecma402Links"/> by Links (the zic input then contains the links too).</summary>
    private void ApplyEcma402Links()
    {
        foreach (var (name, target) in Ecma402Links)
        {
            if (!Zones.ContainsKey(target))
                throw new InvalidDataException($"The target of the ECMA-402 link {name} is not a zone: {target}");

            if (Zones.Remove(name))
            {
                Links.Add(name, target);
            }
            else if (!Links.ContainsKey(name))
            {
                throw new InvalidDataException("Unknown ECMA-402 link: " + name);
            }
        }
    }

    /// <summary>Gets the zic input equivalent to the source (the kept lines).</summary>
    public string GetZicInput()
    {
        var sb = new StringBuilder();
        foreach (var line in OtherSourceLines)
        {
            if (line.StartsWith("Link", StringComparison.Ordinal))
                continue;

            sb.Append(line).Append('\n');
        }

        foreach (var zone in Zones.Values)
        {
            foreach (var line in zone.SourceLines)
            {
                sb.Append(line).Append('\n');
            }
        }

        foreach (var (name, target) in Links)
        {
            sb.Append("Link\t").Append(target).Append('\t').Append(name).Append('\n');
        }

        return sb.ToString();
    }

    private void ParseFile(string content, HashSet<string>? packratList)
    {
        ZoneDefinition? currentZone = null;
        var ignoreZone = false;
        foreach (var rawLine in content.Split('\n'))
        {
            var line = rawLine;

            // "#PACKRATLIST zone.tab Link X Y" lines are links enabled by PACKRATLIST=zone.tab
            if (packratList is not null && line.StartsWith("#PACKRATLIST zone.tab ", StringComparison.Ordinal))
            {
                line = line["#PACKRATLIST zone.tab ".Length..];
            }

            var fields = SplitFields(line);
            if (fields.Count == 0)
                continue;

            if (currentZone is not null || ignoreZone)
            {
                // A continuation line starts with white space
                if (char.IsWhiteSpace(line[0]))
                {
                    if (!ignoreZone)
                    {
                        AddZoneLine(currentZone!, fields, 0, line);
                    }

                    continue;
                }

                currentZone = null;
                ignoreZone = false;
            }

            switch (fields[0])
            {
                case "Rule":
                    AddRule(fields);
                    OtherSourceLines.Add(line);
                    break;

                case "Zone":
                {
                    var name = fields[1];
                    if (packratList is not null && !packratList.Contains(name))
                    {
                        ignoreZone = true;
                        continue;
                    }

                    if (Zones.ContainsKey(name))
                        throw new InvalidDataException("Duplicate zone: " + name);

                    // A Zone overrides a Link of the same name (backzone)
                    Links.Remove(name);
                    currentZone = new ZoneDefinition(name, [], []);
                    Zones.Add(name, currentZone);
                    AddZoneLine(currentZone, fields, 2, line);
                    break;
                }

                case "Link":
                {
                    var target = fields[1];
                    var name = fields[2];
                    if (Zones.ContainsKey(name))
                        throw new InvalidDataException("A link has the name of a zone: " + name);

                    // A later Link overrides an earlier one (backzone)
                    Links.Remove(name);
                    Links.Add(name, target);
                    break;
                }

                default:
                    throw new InvalidDataException("Unexpected line: " + line);
            }
        }
    }

    private void AddZoneLine(ZoneDefinition zone, List<string> fields, int start, string sourceLine)
    {
        if (fields.Count - start < 3)
            throw new InvalidDataException("Invalid zone line: " + sourceLine);

        zone.SourceLines.Add(sourceLine);
        var standardOffset = ParseTime(fields[start], out _);
        var rules = fields[start + 1];
        var format = fields[start + 2];
        var rulesKind = ZoneRulesKind.None;
        var fixedSave = 0;
        var fixedIsDst = false;
        string? ruleName = null;
        if (rules != "-")
        {
            if (char.IsAsciiDigit(rules[0]) || rules[0] == '-')
            {
                rulesKind = ZoneRulesKind.FixedSave;
                fixedSave = ParseSave(rules, out fixedIsDst);
            }
            else
            {
                rulesKind = ZoneRulesKind.Named;
                ruleName = rules;
            }
        }

        var hasUntil = fields.Count > start + 3;
        var untilYear = 0;
        var untilMonth = 1;
        var untilDay = new DaySpec(TimeZoneRuleDayKind.DayOfMonth, 1, 0);
        var untilTime = 0;
        var untilKind = TimeZoneTimeKind.Wall;
        if (hasUntil)
        {
            untilYear = int.Parse(fields[start + 3], CultureInfo.InvariantCulture);
            if (fields.Count > start + 4)
            {
                untilMonth = ParseMonth(fields[start + 4]);
            }

            if (fields.Count > start + 5)
            {
                untilDay = ParseDay(fields[start + 5]);
            }

            if (fields.Count > start + 6)
            {
                untilTime = ParseTime(fields[start + 6], out untilKind);
            }
        }

        zone.Lines.Add(new ZoneLine(standardOffset, rulesKind, fixedSave, fixedIsDst, ruleName, format, hasUntil, untilYear, untilMonth, untilDay, untilTime, untilKind));
    }

    private void AddRule(List<string> fields)
    {
        // Rule NAME FROM TO - IN ON AT SAVE LETTER/S
        if (fields.Count != 10)
            throw new InvalidDataException("Invalid rule: " + string.Join(' ', fields));

        var name = fields[1];
        var from = ParseYear(fields[2], fromYear: null);
        var to = ParseYear(fields[3], from);
        var month = ParseMonth(fields[5]);
        var day = ParseDay(fields[6]);
        var at = ParseTime(fields[7], out var atKind);
        var save = ParseSave(fields[8], out var isDst);
        var letter = fields[9] == "-" ? "" : fields[9];
        if (!Rules.TryGetValue(name, out var list))
        {
            list = [];
            Rules.Add(name, list);
        }

        list.Add(new RuleLine(name, from, to, month, day, at, atKind, save, isDst, letter));
    }

    private static int ParseYear(string value, int? fromYear)
    {
        if (fromYear is not null)
        {
            if (IsAbbreviation(value, "only", 1))
                return fromYear.Value;

            if (IsAbbreviation(value, "maximum", 2))
                return RuleLine.MaxYear;
        }

        if (IsAbbreviation(value, "minimum", 2))
            return int.MinValue;

        return int.Parse(value, CultureInfo.InvariantCulture);
    }

    private static int ParseMonth(string value)
    {
        for (var i = 0; i < MonthNames.Length; i++)
        {
            if (IsAbbreviation(value, MonthNames[i], 3))
                return i + 1;
        }

        throw new InvalidDataException("Invalid month: " + value);
    }

    private static int ParseWeekday(string value)
    {
        for (var i = 0; i < WeekdayNames.Length; i++)
        {
            if (IsAbbreviation(value, WeekdayNames[i], 2))
                return i;
        }

        throw new InvalidDataException("Invalid weekday: " + value);
    }

    private static DaySpec ParseDay(string value)
    {
        if (value.StartsWith("last", StringComparison.Ordinal))
            return new DaySpec(TimeZoneRuleDayKind.LastWeekday, 0, ParseWeekday(value[4..]));

        var index = value.IndexOf(">=", StringComparison.Ordinal);
        if (index > 0)
            return new DaySpec(TimeZoneRuleDayKind.WeekdayOnOrAfter, int.Parse(value[(index + 2)..], CultureInfo.InvariantCulture), ParseWeekday(value[..index]));

        index = value.IndexOf("<=", StringComparison.Ordinal);
        if (index > 0)
            return new DaySpec(TimeZoneRuleDayKind.WeekdayOnOrBefore, int.Parse(value[(index + 2)..], CultureInfo.InvariantCulture), ParseWeekday(value[..index]));

        return new DaySpec(TimeZoneRuleDayKind.DayOfMonth, int.Parse(value, CultureInfo.InvariantCulture), 0);
    }

    /// <summary>Parses a SAVE field: a time, with an optional "s" (standard) or "d" (daylight saving) suffix.</summary>
    private static int ParseSave(string value, out bool isDst)
    {
        bool? explicitDst = null;
        if (value.EndsWith('s', StringComparison.Ordinal))
        {
            explicitDst = false;
            value = value[..^1];
        }
        else if (value.EndsWith('d', StringComparison.Ordinal))
        {
            explicitDst = true;
            value = value[..^1];
        }

        var save = ParseTime(value, out _);
        isDst = explicitDst ?? save != 0;
        return save;
    }

    /// <summary>Parses a time ([-]hh[:mm[:ss[.fraction]]]) with an optional suffix (w, s, u, g, z); the fraction is rounded half to even.</summary>
    public static int ParseTime(string value, out TimeZoneTimeKind kind)
    {
        kind = TimeZoneTimeKind.Wall;
        if (value == "-")
            return 0;

        switch (value[^1])
        {
            case 'w':
                value = value[..^1];
                break;
            case 's':
                kind = TimeZoneTimeKind.Standard;
                value = value[..^1];
                break;
            case 'u' or 'g' or 'z':
                kind = TimeZoneTimeKind.Universal;
                value = value[..^1];
                break;
        }

        var negative = value.StartsWith('-', StringComparison.Ordinal);
        if (negative)
        {
            value = value[1..];
        }

        var fraction = "";
        var dot = value.IndexOf('.', StringComparison.Ordinal);
        if (dot >= 0)
        {
            fraction = value[(dot + 1)..];
            value = value[..dot];
        }

        var parts = value.Split(':');
        var seconds = int.Parse(parts[0], CultureInfo.InvariantCulture) * 3600;
        if (parts.Length > 1)
        {
            seconds += int.Parse(parts[1], CultureInfo.InvariantCulture) * 60;
        }

        if (parts.Length > 2)
        {
            seconds += int.Parse(parts[2], CultureInfo.InvariantCulture);
        }

        if (fraction.Length > 0)
        {
            // Round half to even, like zic
            var firstDigit = fraction[0] - '0';
            var rest = fraction[1..].TrimEnd('0');
            if (firstDigit > 5 || (firstDigit == 5 && (rest.Length > 0 || seconds % 2 != 0)))
            {
                seconds++;
            }
        }

        return negative ? -seconds : seconds;
    }

    private static bool IsAbbreviation(string value, string word, int minimumLength)
    {
        return value.Length >= minimumLength && value.Length <= word.Length && word.StartsWith(value, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Splits a line into fields (white space separated, "#" starts a comment, double quotes group).</summary>
    private static List<string> SplitFields(string line)
    {
        var fields = new List<string>();
        var i = 0;
        while (i < line.Length)
        {
            var c = line[i];
            if (c == '#')
                break;

            if (char.IsWhiteSpace(c))
            {
                i++;
                continue;
            }

            var sb = new StringBuilder();
            while (i < line.Length && !char.IsWhiteSpace(line[i]) && line[i] != '#')
            {
                if (line[i] == '"')
                {
                    i++;
                    while (i < line.Length && line[i] != '"')
                    {
                        sb.Append(line[i++]);
                    }

                    i++;
                    continue;
                }

                sb.Append(line[i++]);
            }

            fields.Add(sb.ToString());
        }

        return fields;
    }
}
