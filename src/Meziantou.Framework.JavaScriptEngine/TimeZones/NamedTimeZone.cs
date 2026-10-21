using System.Runtime.InteropServices;
namespace Meziantou.Framework.JavaScriptEngine.TimeZones;

/// <summary>The local time of a zone during a period: its UTC offset, whether it is daylight saving time, and its abbreviation.</summary>
[StructLayout(LayoutKind.Auto)]
internal readonly record struct TimeZoneLocalTimeType(int OffsetSeconds, bool IsDaylightSavingTime, string Abbreviation);

/// <summary>
/// A zone of the time zone database: the transitions computed by the generator, then the rules that repeat every year.
/// Instances are immutable and thread-safe.
/// </summary>
internal sealed class NamedTimeZone
{
    /// <summary>The last year searched for a transition (the Date and Temporal range ends in +275760).</summary>
    private const long MaxSearchYear = 275_762;

    private const long NanosecondsPerSecond = 1_000_000_000;

    private readonly long[] _transitionTimes;
    private readonly byte[] _transitionTypes;
    private readonly TimeZoneLocalTimeType[] _types;
    private readonly int _initialType;
    private readonly TimeZoneRecurringRule? _rule;
    private readonly bool _ruleChangesOffset;

    internal NamedTimeZone(string id, TimeZoneLocalTimeType[] types, int initialType, long[] transitionTimes, byte[] transitionTypes, TimeZoneRecurringRule? rule)
    {
        Id = id;
        _types = types;
        _initialType = initialType;
        _transitionTimes = transitionTimes;
        _transitionTypes = transitionTypes;
        _rule = rule;
        if (rule is not null)
        {
            foreach (var entry in rule.Entries)
            {
                if (types[entry.TypeIndex].OffsetSeconds != types[rule.Entries[0].TypeIndex].OffsetSeconds)
                {
                    _ruleChangesOffset = true;
                }
            }
        }
    }

    /// <summary>Gets the name of the zone in the database (a Zone line, not a Link).</summary>
    public string Id { get; }

    /// <summary>Gets the number of explicit transitions (for the tests).</summary>
    internal int ExplicitTransitionCount => _transitionTimes.Length;

    /// <summary>Gets the recurring rules (for the tests).</summary>
    internal TimeZoneRecurringRule? RecurringRule => _rule;

    /// <summary>Gets the UTC offset, in seconds, at an instant.</summary>
    public int GetOffsetSeconds(long epochSeconds) => GetLocalTimeType(epochSeconds).OffsetSeconds;

    /// <summary>Gets the UTC offset, in nanoseconds, at an instant (GetNamedTimeZoneOffsetNanoseconds).</summary>
    public long GetOffsetNanoseconds(Int128 epochNanoseconds) => GetOffsetSeconds(FloorToSeconds(epochNanoseconds)) * NanosecondsPerSecond;

    /// <summary>Gets the local time type (offset, daylight saving time, abbreviation) at an instant.</summary>
    public TimeZoneLocalTimeType GetLocalTimeType(long epochSeconds) => _types[GetTypeIndex(epochSeconds)];

    private int GetTypeIndex(long epochSeconds)
    {
        var times = _transitionTimes;
        if (times.Length == 0)
            return GetTypeIndexFromRule(epochSeconds, _initialType);

        var last = times[^1];
        if (epochSeconds >= last)
            return GetTypeIndexFromRule(epochSeconds, _transitionTypes[^1]);

        if (epochSeconds < times[0])
            return _initialType;

        // The last transition at or before the instant
        var index = Array.BinarySearch(times, epochSeconds);
        if (index < 0)
        {
            index = ~index - 1;
        }

        return _transitionTypes[index];
    }

    private int GetTypeIndexFromRule(long epochSeconds, int lastExplicitType)
    {
        var rule = _rule;
        if (rule is null)
            return lastExplicitType;

        var lastExplicitTime = _transitionTimes.Length == 0 ? long.MinValue : _transitionTimes[^1];
        var year = CalendarMath.YearFromDays(CalendarMath.FloorDivide(epochSeconds, CalendarMath.SecondsPerDay));
        var count = rule.Entries.Length;
        Span<long> times = stackalloc long[count];
        Span<int> types = stackalloc int[count];

        // A rule of the next year can apply before the end of the UTC year (and one of the year before the year after it)
        var result = lastExplicitType;
        var resultTime = long.MinValue;
        for (var y = Math.Max(year - 1, rule.FirstYear); y <= year + 1; y++)
        {
            var transitionCount = rule.GetTransitions(y, times, types);
            for (var i = 0; i < transitionCount; i++)
            {
                var time = times[i];
                if (time <= epochSeconds && time > lastExplicitTime && time >= resultTime)
                {
                    resultTime = time;
                    result = types[i];
                }
            }
        }

        return result;
    }

    /// <summary>
    /// Gets the instants whose local time is the given local date and time, in seconds since 1970-01-01 (GetNamedTimeZoneEpochNanoseconds):
    /// none in a gap, two in an overlap. The sub-second part of the local time does not change the result: add it to each instant.
    /// </summary>
    /// <returns>The number of instants written to <paramref name="destination"/> (sorted), up to its length.</returns>
    public int GetPossibleEpochSeconds(long localSeconds, Span<long> destination)
    {
        // The offsets in effect around the local time: the offsets are less than a day, so any instant whose local time is
        // the given one is in the window
        const long Window = 2 * CalendarMath.SecondsPerDay;
        Span<int> offsets = stackalloc int[8];
        var offsetCount = 0;
        AddOffset(offsets, ref offsetCount, GetOffsetSeconds(localSeconds - Window));
        var current = localSeconds - Window;
        while (offsetCount < offsets.Length && TryGetNextTransitionCore(current, out var time, out var typeIndex) && time <= localSeconds + Window)
        {
            AddOffset(offsets, ref offsetCount, _types[typeIndex].OffsetSeconds);
            current = time;
        }

        var count = 0;
        Span<long> results = stackalloc long[8];
        for (var i = 0; i < offsetCount; i++)
        {
            var candidate = localSeconds - offsets[i];
            if (GetOffsetSeconds(candidate) == offsets[i])
            {
                results[count++] = candidate;
            }
        }

        results = results[..count];
        results.Sort();
        var written = 0;
        for (var i = 0; i < results.Length && written < destination.Length; i++)
        {
            if (i > 0 && results[i] == results[i - 1])
                continue;

            destination[written++] = results[i];
        }

        return written;

        static void AddOffset(Span<int> offsets, ref int count, int offset)
        {
            if (offsets[..count].Contains(offset))
                return;

            offsets[count++] = offset;
        }
    }

    /// <summary>
    /// Gets the UTC offset, in seconds, of a local date and time with the disambiguation of UTC(t) of ECMA-262: the offset
    /// of the earlier instant for a repeated local time, the offset before the transition for a skipped local time.
    /// </summary>
    public int GetOffsetSecondsForLocalTime(long localSeconds)
    {
        Span<long> instants = stackalloc long[2];
        if (GetPossibleEpochSeconds(localSeconds, instants) > 0)
            return GetOffsetSeconds(instants[0]);

        // In a gap: the offset in effect just before the transition that skips the local time
        const long Window = 2 * CalendarMath.SecondsPerDay;
        var current = localSeconds - Window;
        while (TryGetNextTransitionCore(current, out var time, out var typeIndex) && time <= localSeconds + Window)
        {
            var before = GetOffsetSeconds(time - 1);
            if (time + before <= localSeconds && localSeconds < time + _types[typeIndex].OffsetSeconds)
                return before;

            current = time;
        }

        return GetOffsetSeconds(localSeconds);
    }

    /// <summary>Gets the transitions in a range, including the ones that change only the abbreviation (for the tests and the generator).</summary>
    internal List<(long Time, TimeZoneLocalTimeType Type)> GetTransitions(long startExclusive, long endInclusive)
    {
        var result = new List<(long Time, TimeZoneLocalTimeType Type)>();
        var current = startExclusive;
        while (TryGetNextTransitionCore(current, out var time, out var typeIndex) && time <= endInclusive)
        {
            result.Add((time, _types[typeIndex]));
            current = time;
        }

        return result;
    }

    /// <summary>Gets the first instant strictly after the given one where the UTC offset changes (GetNamedTimeZoneNextTransition).</summary>
    public long? GetNextTransition(long epochSeconds)
    {
        var current = epochSeconds;
        while (TryGetNextTransitionCore(current, out var time, out var typeIndex))
        {
            if (_types[typeIndex].OffsetSeconds != GetOffsetSeconds(time - 1))
                return time;

            current = time;
        }

        return null;
    }

    /// <summary>Gets the last instant strictly before the given one where the UTC offset changes (GetNamedTimeZonePreviousTransition).</summary>
    public long? GetPreviousTransition(long epochSeconds)
    {
        var current = epochSeconds;
        while (TryGetPreviousTransitionCore(current, out var time, out var typeIndex))
        {
            if (_types[typeIndex].OffsetSeconds != GetOffsetSeconds(time - 1))
                return time;

            current = time;
        }

        return null;
    }

    /// <summary>Gets the first transition strictly after the epoch nanoseconds, in epoch nanoseconds.</summary>
    public Int128? GetNextTransition(Int128 epochNanoseconds)
    {
        var result = GetNextTransition(FloorToSeconds(epochNanoseconds));
        return result is null ? null : (Int128)result.Value * NanosecondsPerSecond;
    }

    /// <summary>Gets the last transition strictly before the epoch nanoseconds, in epoch nanoseconds.</summary>
    public Int128? GetPreviousTransition(Int128 epochNanoseconds)
    {
        var seconds = FloorToSeconds(epochNanoseconds);

        // A transition at the floored second is before the instant when the instant has a fractional part
        if ((Int128)seconds * NanosecondsPerSecond != epochNanoseconds)
        {
            seconds++;
        }

        var result = GetPreviousTransition(seconds);
        return result is null ? null : (Int128)result.Value * NanosecondsPerSecond;
    }

    /// <summary>Gets the first transition (explicit or computed, the offset may not change) strictly after an instant.</summary>
    private bool TryGetNextTransitionCore(long epochSeconds, out long time, out int typeIndex)
    {
        var times = _transitionTimes;
        if (times.Length > 0 && epochSeconds < times[^1])
        {
            var index = Array.BinarySearch(times, epochSeconds);
            index = index < 0 ? ~index : index + 1;
            time = times[index];
            typeIndex = _transitionTypes[index];
            return true;
        }

        var rule = _rule;
        if (rule is not null && _ruleChangesOffset)
        {
            var lastExplicitTime = times.Length == 0 ? long.MinValue : times[^1];
            var after = Math.Max(epochSeconds, lastExplicitTime);
            var year = Math.Max(rule.FirstYear, CalendarMath.YearFromDays(CalendarMath.FloorDivide(after, CalendarMath.SecondsPerDay)) - 1);
            var count = rule.Entries.Length;
            Span<long> yearTimes = stackalloc long[count];
            Span<int> yearTypes = stackalloc int[count];
            for (; year <= MaxSearchYear; year++)
            {
                var transitionCount = rule.GetTransitions(year, yearTimes, yearTypes);
                for (var i = 0; i < transitionCount; i++)
                {
                    if (yearTimes[i] > after)
                    {
                        time = yearTimes[i];
                        typeIndex = yearTypes[i];
                        return true;
                    }
                }
            }
        }

        time = 0;
        typeIndex = 0;
        return false;
    }

    /// <summary>Gets the last transition (explicit or computed, the offset may not change) strictly before an instant.</summary>
    private bool TryGetPreviousTransitionCore(long epochSeconds, out long time, out int typeIndex)
    {
        var times = _transitionTimes;
        var rule = _rule;
        var lastExplicitTime = times.Length == 0 ? long.MinValue : times[^1];
        if (rule is not null && _ruleChangesOffset && epochSeconds > lastExplicitTime)
        {
            var year = Math.Min(MaxSearchYear, CalendarMath.YearFromDays(CalendarMath.FloorDivide(epochSeconds, CalendarMath.SecondsPerDay)) + 1);
            var count = rule.Entries.Length;
            Span<long> yearTimes = stackalloc long[count];
            Span<int> yearTypes = stackalloc int[count];
            var reachedExplicitTransitions = false;
            for (; year >= rule.FirstYear && !reachedExplicitTransitions; year--)
            {
                var transitionCount = rule.GetTransitions(year, yearTimes, yearTypes);
                for (var i = transitionCount - 1; i >= 0; i--)
                {
                    if (yearTimes[i] <= lastExplicitTime)
                    {
                        reachedExplicitTransitions = true;
                        break;
                    }

                    if (yearTimes[i] < epochSeconds)
                    {
                        time = yearTimes[i];
                        typeIndex = yearTypes[i];
                        return true;
                    }
                }
            }
        }

        if (times.Length > 0 && epochSeconds > times[0])
        {
            var index = Array.BinarySearch(times, epochSeconds);
            index = index < 0 ? ~index - 1 : index - 1;
            time = times[index];
            typeIndex = _transitionTypes[index];
            return true;
        }

        time = 0;
        typeIndex = 0;
        return false;
    }

    private static long FloorToSeconds(Int128 epochNanoseconds)
    {
        var quotient = epochNanoseconds / NanosecondsPerSecond;
        if (epochNanoseconds < 0 && quotient * NanosecondsPerSecond != epochNanoseconds)
        {
            quotient--;
        }

        return (long)quotient;
    }

    /// <inheritdoc/>
    public override string ToString() => Id;
}
