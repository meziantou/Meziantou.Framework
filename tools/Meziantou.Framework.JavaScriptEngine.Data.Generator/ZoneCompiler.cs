using System.Runtime.InteropServices;
using Meziantou.Framework.JavaScriptEngine.TimeZones;

namespace Meziantou.Framework.JavaScriptEngine.Data.Generator;

/// <summary>A local time type of a compiled zone.</summary>
[StructLayout(LayoutKind.Auto)]
internal readonly record struct CompiledType(int OffsetSeconds, bool IsDst, string Abbreviation);

/// <summary>A compiled recurring rule entry (its type is an index in <see cref="CompiledZone.Types"/>).</summary>
[StructLayout(LayoutKind.Auto)]
internal readonly record struct CompiledRuleEntry(RuleLine Rule, int TypeIndex);

/// <summary>A compiled zone: its transitions until <see cref="LastExplicitYear"/>, then the recurring rules.</summary>
internal sealed record CompiledZone(string Name, List<CompiledType> Types, int InitialType, List<(long Time, int Type)> Transitions, int LastExplicitYear, int StandardOffsetSeconds, List<CompiledRuleEntry> RecurringRules);

/// <summary>Compiles the zones like zic (the outzone and writezone functions of zic.c).</summary>
internal sealed class ZoneCompiler(TzdbSource source)
{
    private readonly int _minYear = ComputeMinYear(source);

    public CompiledZone Compile(ZoneDefinition zone)
    {
        var types = new List<CompiledType>();
        var transitions = new List<(long Time, int Type)>();
        var defaultType = -1;

        var lines = zone.Lines;
        var lastLine = lines[^1];
        var lastRules = lastLine.RulesKind == ZoneRulesKind.Named ? GetRules(lastLine.RuleName!) : [];

        // The transitions are explicit until the year after the last year a non-recurring rule (or the start of the last
        // line) applies: from there, only the recurring rules apply.
        var lastExplicitYear = lines.Count > 1 ? lines[^2].UntilYear : _minYear;
        foreach (var rule in lastRules)
        {
            lastExplicitYear = Math.Max(lastExplicitYear, rule.IsRecurring ? rule.FromYear : rule.ToYear);
        }

        var hasRecurringRules = lastRules.Exists(rule => rule.IsRecurring);
        if (hasRecurringRules)
        {
            lastExplicitYear++;
        }

        long startTime = 0;
        var startIsStd = false;
        var startIsUt = false;
        var save = 0;
        var standardOffset = 0;
        for (var i = 0; i < lines.Count; i++)
        {
            var line = lines[i];
            save = 0;
            var useStart = i > 0;
            var useUntil = i < lines.Count - 1;
            standardOffset = line.StandardOffsetSeconds;
            string? startAbbreviation = null;
            var startOffset = line.StandardOffsetSeconds;
            if (line.RulesKind != ZoneRulesKind.Named)
            {
                save = line.FixedSaveSeconds;
                var isDst = line.RulesKind == ZoneRulesKind.FixedSave && line.FixedIsDst;
                var type = AddType(types, line.StandardOffsetSeconds + save, isDst, FormatAbbreviation(line, letters: null, isDst, save));
                if (useStart)
                {
                    transitions.Add((startTime, type));
                    useStart = false;
                }
                else
                {
                    defaultType = type;
                }
            }
            else
            {
                var rules = GetRules(line.RuleName!);
                var maxYear = useUntil ? line.UntilYear : lastExplicitYear;
                for (var year = _minYear; year <= maxYear; year++)
                {
                    // The rules to do in the year, with their local time
                    var todo = new List<(RuleLine Rule, long Local)>();
                    foreach (var rule in rules)
                    {
                        if (year >= rule.FromYear && year <= rule.ToYear)
                        {
                            todo.Add((rule, GetLocalSeconds(year, rule.Month, rule.Day, rule.AtSeconds)));
                        }
                    }

                    while (true)
                    {
                        long untilTime = 0;
                        if (useUntil)
                        {
                            // The UNTIL time in UT, with the current standard offset and daylight saving
                            untilTime = GetLocalSeconds(line.UntilYear, line.UntilMonth, line.UntilDay, line.UntilTimeSeconds);
                            if (line.UntilKind != TimeZoneTimeKind.Universal)
                            {
                                untilTime -= standardOffset;
                            }

                            if (line.UntilKind == TimeZoneTimeKind.Wall)
                            {
                                untilTime -= save;
                            }
                        }

                        // The rule that takes effect first in the year
                        var k = -1;
                        long kTime = 0;
                        for (var j = 0; j < todo.Count; j++)
                        {
                            var rule = todo[j].Rule;
                            long offset = rule.AtKind == TimeZoneTimeKind.Universal ? 0 : standardOffset;
                            if (rule.AtKind == TimeZoneTimeKind.Wall)
                            {
                                offset += save;
                            }

                            var jTime = todo[j].Local - offset;
                            if (k < 0 || jTime < kTime)
                            {
                                k = j;
                                kTime = jTime;
                            }
                            else if (jTime == kTime)
                            {
                                throw new InvalidDataException($"{zone.Name}: two rules for the same instant");
                            }
                        }

                        if (k < 0)
                            break;

                        var current = todo[k].Rule;
                        todo.RemoveAt(k);
                        if (useUntil && kTime >= untilTime)
                        {
                            if (startAbbreviation is null && line.StandardOffsetSeconds + current.SaveSeconds == startOffset)
                            {
                                startAbbreviation = FormatAbbreviation(line, current.Letter, current.IsDst, current.SaveSeconds);
                            }

                            break;
                        }

                        save = current.SaveSeconds;
                        if (useStart && kTime == startTime)
                        {
                            useStart = false;
                        }

                        if (useStart)
                        {
                            if (kTime < startTime)
                            {
                                startOffset = line.StandardOffsetSeconds + save;
                                startAbbreviation = FormatAbbreviation(line, current.Letter, current.IsDst, current.SaveSeconds);
                                continue;
                            }

                            if (startAbbreviation is null && startOffset == line.StandardOffsetSeconds + save)
                            {
                                startAbbreviation = FormatAbbreviation(line, current.Letter, current.IsDst, current.SaveSeconds);
                            }
                        }

                        var abbreviation = FormatAbbreviation(line, current.Letter, current.IsDst, current.SaveSeconds);
                        var type = AddType(types, line.StandardOffsetSeconds + current.SaveSeconds, current.IsDst, abbreviation);
                        if (defaultType < 0 && !current.IsDst)
                        {
                            defaultType = type;
                        }

                        transitions.Add((kTime, type));
                    }
                }
            }

            if (useStart)
            {
                var isDst = startOffset != line.StandardOffsetSeconds;
                if (startAbbreviation is null && !line.Format.Contains("%s", StringComparison.Ordinal))
                {
                    startAbbreviation = FormatAbbreviation(line, letters: null, isDst, save);
                }

                if (startAbbreviation is null)
                    throw new InvalidDataException($"{zone.Name}: cannot determine the time zone abbreviation to use just after the until time");

                var type = AddType(types, startOffset, isDst, startAbbreviation);
                if (defaultType < 0 && !isDst)
                {
                    defaultType = type;
                }

                transitions.Add((startTime, type));
            }

            // The start of the next line
            if (useUntil)
            {
                startIsStd = line.UntilKind == TimeZoneTimeKind.Standard;
                startIsUt = line.UntilKind == TimeZoneTimeKind.Universal;
                startTime = GetLocalSeconds(line.UntilYear, line.UntilMonth, line.UntilDay, line.UntilTimeSeconds);
                if (!startIsStd && !startIsUt)
                {
                    startTime -= save;
                }

                if (!startIsUt)
                {
                    startTime -= standardOffset;
                }
            }
        }

        if (defaultType < 0)
        {
            defaultType = 0;
        }

        // writezone: sort, then merge the transitions that would not advance the local time and remove the ones that do not change the type
        var sorted = transitions.Select((transition, index) => (transition.Time, transition.Type, index)).OrderBy(t => t.Time).ThenBy(t => t.index).Select(t => (t.Time, t.Type)).ToList();
        var optimized = new List<(long Time, int Type)>();
        foreach (var transition in sorted)
        {
            if (optimized.Count > 0)
            {
                var previous = optimized[^1];
                // zic uses the type 0 (the first type added) before the first transition
                var offsetBeforePrevious = optimized.Count == 1 ? types[0].OffsetSeconds : types[optimized[^2].Type].OffsetSeconds;
                if (transition.Time + types[previous.Type].OffsetSeconds <= previous.Time + offsetBeforePrevious)
                {
                    optimized[^1] = (previous.Time, transition.Type);
                    continue;
                }
            }

            if (optimized.Count == 0 || types[optimized[^1].Type] != types[transition.Type])
            {
                optimized.Add(transition);
            }
        }

        // Remove the transitions that do not change the local time type (zic keeps the first one, it is not needed here)
        var result = new List<(long Time, int Type)>();
        var currentType = defaultType;
        foreach (var transition in optimized)
        {
            if (types[transition.Type] == types[currentType])
                continue;

            result.Add(transition);
            currentType = transition.Type;
        }

        var recurringRules = new List<CompiledRuleEntry>();
        if (hasRecurringRules)
        {
            foreach (var rule in lastRules)
            {
                if (!rule.IsRecurring)
                    continue;

                var type = AddType(types, lastLine.StandardOffsetSeconds + rule.SaveSeconds, rule.IsDst, FormatAbbreviation(lastLine, rule.Letter, rule.IsDst, rule.SaveSeconds));
                recurringRules.Add(new CompiledRuleEntry(rule, type));
            }
        }

        return new CompiledZone(zone.Name, types, defaultType, result, lastExplicitYear, lastLine.StandardOffsetSeconds, recurringRules);
    }

    private List<RuleLine> GetRules(string name)
    {
        return source.Rules.TryGetValue(name, out var rules) ? rules : throw new InvalidDataException("Unknown rule: " + name);
    }

    private static int AddType(List<CompiledType> types, int offset, bool isDst, string abbreviation)
    {
        var type = new CompiledType(offset, isDst, abbreviation);
        var index = types.IndexOf(type);
        if (index >= 0)
            return index;

        types.Add(type);
        return types.Count - 1;
    }

    /// <summary>The local date and time (without offset) of a day of a year, like rpytime in zic.</summary>
    public static long GetLocalSeconds(int year, int month, DaySpec day, int timeSeconds)
    {
        var entry = new TimeZoneRuleEntry((byte)month, day.Kind, (byte)day.Day, (byte)day.Weekday, timeSeconds, TimeZoneTimeKind.Wall, 0, 0);
        return entry.GetLocalSeconds(year);
    }

    /// <summary>doabbr in zic: the abbreviation of a FORMAT ("%s", "A/B" or "%z").</summary>
    private static string FormatAbbreviation(ZoneLine line, string? letters, bool isDst, int save)
    {
        var format = line.Format;
        var slash = format.IndexOf('/', StringComparison.Ordinal);
        if (slash >= 0)
            return isDst ? format[(slash + 1)..] : format[..slash];

        if (format.Contains("%z", StringComparison.Ordinal))
            return format.Replace("%z", FormatOffset(line.StandardOffsetSeconds + save), StringComparison.Ordinal);

        return format.Replace("%s", letters ?? "%s", StringComparison.Ordinal);
    }

    private static string FormatOffset(int offset)
    {
        var sign = offset < 0 ? '-' : '+';
        offset = Math.Abs(offset);
        var seconds = offset % 60;
        var minutes = offset / 60 % 60;
        var hours = offset / 3600;
        var result = sign + hours.ToString("00", CultureInfo.InvariantCulture);
        if (minutes != 0 || seconds != 0)
        {
            result += minutes.ToString("00", CultureInfo.InvariantCulture);
            if (seconds != 0)
            {
                result += seconds.ToString("00", CultureInfo.InvariantCulture);
            }
        }

        return result;
    }

    private static int ComputeMinYear(TzdbSource source)
    {
        var min = int.MaxValue;
        foreach (var rules in source.Rules.Values)
        {
            foreach (var rule in rules)
            {
                if (rule.FromYear != int.MinValue)
                {
                    min = Math.Min(min, rule.FromYear);
                }
            }
        }

        foreach (var zone in source.Zones.Values)
        {
            foreach (var line in zone.Lines)
            {
                if (line.HasUntil)
                {
                    min = Math.Min(min, line.UntilYear);
                }
            }
        }

        return min;
    }
}
