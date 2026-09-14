namespace Meziantou.Framework.Scheduling;

/// <summary>Builds a <see cref="TimeZoneInfo"/> from a VTIMEZONE component (RFC 5545 section 3.6.5).</summary>
/// <remarks>
/// <para>The sub-components are expanded into the changes of the UTC offset they describe, and those changes become one
/// adjustment rule per year, as the runtime builds them for the Windows time zones: a year holds at most one daylight saving
/// period, whose fixed date transitions may run from the start of the year or until its end, and a standard offset of its own.
/// Both the .NET 10 and the .NET 11 implementations of <see cref="TimeZoneInfo"/> read such rules the same way, whereas a rule
/// bounded by instants within a year is not honored by the latter.</para>
/// <para>A year changing its offset more often, such as a daylight saving period interrupted for a month, cannot be expressed:
/// its shortest periods are merged into the preceding ones.</para>
/// <para>The ongoing pattern, a STANDARD and a DAYLIGHT sub-component whose yearly RRULE never ends and that a
/// <see cref="TimeZoneInfo.TransitionTime"/> can express (such as BYDAY=-1SU or BYMONTHDAY=22), becomes a single open-ended rule
/// from the year after the last onset of the other sub-components. Any other open-ended recurrence is expanded for
/// <see cref="YearsToExpandOpenEndedRecurrence"/> years, after which the offset stays the one of the last change.</para>
/// <para>Before the first change, the offset is the TZOFFSETFROM of that change.</para>
/// <para>.NET Standard 2.0 cannot change the standard offset in an adjustment rule, so there the years whose standard offset
/// differs from the base one keep the base offset.</para>
/// </remarks>
internal static class VTimeZoneReader
{
    /// <summary>Bounds the work a crafted recurrence can cause; a real time zone has one onset a year.</summary>
    private const int MaxOnsetsPerObservance = 2000;

    /// <summary>Bounds the work a component made of many sub-components can cause.</summary>
    private const int MaxOnsets = 20000;

    /// <summary>The number of years inspected to recognize the transition an open-ended recurrence describes.</summary>
    private const int YearsToRecognizeTransition = 28;

    /// <summary>The number of years an open-ended recurrence no <see cref="TimeZoneInfo.TransitionTime"/> expresses is expanded for.</summary>
    private const int YearsToExpandOpenEndedRecurrence = 300;

#if NET6_0_OR_GREATER
    /// <summary>The number of years the open-ended adjustment rule is checked against the recurrences it replaces.</summary>
    private const int YearsToVerifyOngoingRule = 30;
#endif

    private const int LastSupportedYear = 9998;

    /// <summary>Creates the time zone described by the content lines of a VTIMEZONE component, its delimiters excluded.</summary>
    /// <returns>The time zone, whose identifier is <paramref name="id"/>, or <see langword="null"/> when the component cannot be represented.</returns>
    public static TimeZoneInfo? Create(string id, List<ContentLine> lines)
    {
        try
        {
            if (!TryReadObservances(lines, out var observances))
                return null;

            return CreateWithOngoingRule(id, observances) ?? CreateFromTransitions(id, observances);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidTimeZoneException or FormatException or OverflowException)
        {
            // TimeZoneInfo validates the rules as a whole (ordering, offset ranges), which is simpler to check by creating it.
            return null;
        }
    }

    /// <summary>Creates the time zone with an open-ended rule for the ongoing pattern, when there is one the runtime reproduces.</summary>
    private static TimeZoneInfo? CreateWithOngoingRule(string id, List<Observance> observances)
    {
        FindOngoingObservances(observances, out var standard, out var daylight);
        if (standard is null || daylight is null || standard.OffsetTo != daylight.OffsetFrom || daylight.OffsetTo != standard.OffsetFrom)
            return null;

        var patternStartYear = Math.Max(standard.Start.Year, daylight.Start.Year);
        if (!TryRecognizeTransition(daylight, patternStartYear, out var daylightStart) ||
            !TryRecognizeTransition(standard, patternStartYear, out var daylightEnd))
        {
            return null;
        }

        // The open-ended rule starts with the first year whose onsets all come from the pattern.
        var lastOtherYear = patternStartYear;
        var onsetCount = 0;
        foreach (var observance in observances)
        {
            if (observance == standard || observance == daylight)
            {
                foreach (var date in observance.RecurrenceDates)
                {
                    lastOtherYear = Math.Max(lastOtherYear, date.Year);
                }

                continue;
            }

            foreach (var onset in Expand(observance, LastSupportedYear + 1))
            {
                lastOtherYear = Math.Max(lastOtherYear, onset.Local.Year);
                if (++onsetCount > MaxOnsets)
                    return null;
            }
        }

        var ongoingFromYear = lastOtherYear + 1;
        if (ongoingFromYear > LastSupportedYear)
            return null;

        var baseUtcOffset = standard.OffsetTo;
#if NET6_0_OR_GREATER
        var lastExpandedYear = Math.Min(ongoingFromYear + YearsToVerifyOngoingRule, LastSupportedYear);
#else
        var lastExpandedYear = ongoingFromYear;
#endif
        var transitions = Normalize(ExpandAll(observances, lastExpandedYear));
        var years = CreateYearRules(transitions.FindAll(transition => transition.Local.Year < ongoingFromYear), baseUtcOffset, ongoingFromYear - 1);
        years.Add([new RuleSpecification(new DateTime(ongoingFromYear, 1, 1), DateTime.MaxValue.Date, daylight.OffsetTo - standard.OffsetTo, daylightStart, daylightEnd, TimeSpan.Zero)]);
        var timeZone = CreateTimeZone(id, standard.Name ?? id, daylight.Name ?? id, baseUtcOffset, years, transitions);

#if NET6_0_OR_GREATER
        // A transition the runtime computes differently, such as one close to the end of a year, is not worth an approximation:
        // expanding the recurrences instead is exact.
        if (!Verify(timeZone, transitions, new DateTime(ongoingFromYear, 1, 2), new DateTime(lastExpandedYear, 12, 31)))
            return null;
#endif

        return timeZone;
    }

    /// <summary>Creates the time zone from the changes of offset the observances describe.</summary>
    private static TimeZoneInfo CreateFromTransitions(string id, List<Observance> observances)
    {
        var lastYear = LastSupportedYear;
        foreach (var observance in observances)
        {
            if (observance.RecurrenceRule is not null && observance.IsForever)
            {
                lastYear = Math.Min(lastYear, Math.Max(observance.Start.Year, 1) + YearsToExpandOpenEndedRecurrence);
            }
        }

        var transitions = Normalize(ExpandAll(observances, lastYear));
        var baseUtcOffset = transitions.Count > 0 ? transitions[^1].To : observances[^1].OffsetTo;
        var years = CreateYearRules(transitions, baseUtcOffset, transitions.Count > 0 ? transitions[^1].Local.Year : 0);
        return CreateTimeZone(id, FindName(observances, isDaylight: false) ?? id, FindName(observances, isDaylight: true) ?? id, baseUtcOffset, years, transitions);
    }

    private static void FindOngoingObservances(List<Observance> observances, out Observance? ongoingStandard, out Observance? ongoingDaylight)
    {
        ongoingStandard = null;
        ongoingDaylight = null;
        foreach (var observance in observances)
        {
            if (observance.RecurrenceRule is null || !observance.IsForever)
                continue;

            if (observance.IsDaylight)
            {
                if (ongoingDaylight is null || observance.Start > ongoingDaylight.Start)
                {
                    ongoingDaylight = observance;
                }
            }
            else if (ongoingStandard is null || observance.Start > ongoingStandard.Start)
            {
                ongoingStandard = observance;
            }
        }
    }

    private static string? FindName(List<Observance> observances, bool isDaylight)
    {
        string? result = null;
        foreach (var observance in observances)
        {
            if (observance.IsDaylight == isDaylight && observance.Name is not null)
            {
                result = observance.Name;
            }
        }

        return result;
    }

    private static List<Onset> ExpandAll(List<Observance> observances, int lastYear)
    {
        var onsets = new List<Onset>();
        foreach (var observance in observances)
        {
            onsets.AddRange(Expand(observance, lastYear + 1));
            if (onsets.Count > MaxOnsets)
                throw new FormatException("The VTIMEZONE component describes too many onsets");
        }

        return onsets;
    }

    /// <summary>Orders the onsets and keeps the ones that change the offset, each starting from the offset actually in effect.</summary>
    private static List<Transition> Normalize(List<Onset> onsets)
    {
        var ordered = new List<(Onset Onset, int Index)>(onsets.Count);
        for (var i = 0; i < onsets.Count; i++)
        {
            ordered.Add((onsets[i], i));
        }

        // The index keeps the sort stable, so the result does not depend on the sort algorithm.
        ordered.Sort((a, b) => a.Onset.Utc != b.Onset.Utc ? a.Onset.Utc.CompareTo(b.Onset.Utc) : a.Index.CompareTo(b.Index));

        var result = new List<Transition>();
        if (ordered.Count is 0)
            return result;

        var offset = ordered[0].Onset.Observance.OffsetFrom;
        foreach (var (onset, _) in ordered)
        {
            if (result.Count > 0 && result[^1].Utc == onset.Utc)
            {
                // Of two onsets at the same instant, the last one wins.
                offset = result[^1].From;
                result.RemoveAt(result.Count - 1);
            }

            if (onset.Observance.OffsetTo == offset)
                continue;

            result.Add(new Transition(onset.Utc, offset, onset.Observance.OffsetTo, onset.Observance.IsDaylight));
            offset = onset.Observance.OffsetTo;
        }

        return result;
    }

    /// <summary>Describes each year from the first change to <paramref name="lastYear"/> by the rules that can express it, the preferred one first.</summary>
    private static List<List<RuleSpecification>> CreateYearRules(List<Transition> transitions, TimeSpan baseUtcOffset, int lastYear)
    {
        var rules = new List<List<RuleSpecification>>();
        if (transitions.Count is 0)
            return rules;

        var offset = transitions[0].From;
        var firstYear = transitions[0].Local.Year;
        if (firstYear > 1)
        {
            rules.Add([RuleSpecification.CreateConstant(DateTime.MinValue, new DateTime(firstYear - 1, 12, 31), offset - baseUtcOffset)]);
        }

        var index = 0;
        for (var year = firstYear; year <= lastYear; year++)
        {
            var yearTransitions = new List<Transition>();
            while (index < transitions.Count && transitions[index].Local.Year <= year)
            {
                yearTransitions.Add(transitions[index]);
                index++;
            }

            rules.Add(CreateYearRule(year, offset, yearTransitions, baseUtcOffset));
            if (yearTransitions.Count > 0)
            {
                offset = yearTransitions[^1].To;
            }
        }

        return rules;
    }

    private static List<RuleSpecification> CreateYearRule(int year, TimeSpan startOffset, List<Transition> transitions, TimeSpan baseUtcOffset)
    {
        var yearStart = new DateTime(year, 1, 1);
        var yearEnd = new DateTime(year, 12, 31);

        // offsets[i] is in effect from instants[i - 1] to instants[i].
        var offsets = new List<TimeSpan> { startOffset };
        var instants = new List<Transition>();
        foreach (var transition in transitions)
        {
            // A change at the very start of the year is read as the offset of the whole year, as the runtime reserves that
            // transition time for a daylight saving period continuing the previous year.
            if (instants.Count is 0 && transition.Local < yearStart.AddSeconds(1))
            {
                offsets[0] = transition.To;
                continue;
            }

            instants.Add(transition);
            offsets.Add(transition.To);
        }

        // A rule describes at most two offsets and one daylight saving period, so a year changing more often loses its
        // shortest periods, each taking the offset of a neighbor, until it fits.
        while (true)
        {
            var candidates = CreateYearRuleCandidates(yearStart, yearEnd, offsets, instants, baseUtcOffset);
            if (candidates.Count > 0)
                return candidates;

            RemoveShortestPeriod(yearStart, offsets, instants);
        }
    }

    private static List<RuleSpecification> CreateYearRuleCandidates(DateTime yearStart, DateTime yearEnd, List<TimeSpan> offsets, List<Transition> instants, TimeSpan baseUtcOffset)
    {
        var result = new List<RuleSpecification>();
        if (instants.Count is 0)
        {
            result.Add(RuleSpecification.CreateConstant(yearStart, yearEnd, offsets[0] - baseUtcOffset));
            return result;
        }

        if (instants.Count > 2 || (instants.Count is 2 && offsets[0] != offsets[2]))
            return result;

        // The start of a daylight saving period is a local standard time, and its end a local daylight saving time: both are
        // the local time in the offset in effect before the change. January 1 at midnight marks the start or the end of the year.
        var first = instants[0].Utc + offsets[0];
        var second = instants.Count is 2 ? instants[1].Utc + offsets[1] : yearStart;
        if (!IsWithinYear(first) || (instants.Count is 2 && !IsWithinYear(second)))
            return result;

        // Either offset can be the standard one. The kind of the first change decides which is preferred; the other one is kept
        // for when the runtime does not reproduce the first around a year boundary.
        foreach (var isFirstChangeToDaylight in new[] { instants[0].IsDaylight, !instants[0].IsDaylight })
        {
            RuleSpecification rule;
            if (isFirstChangeToDaylight)
            {
                // Daylight saving time from the first change to the second one, or to the end of the year.
                rule = new RuleSpecification(yearStart, yearEnd, offsets[1] - offsets[0], CreateFixedTransition(first), CreateFixedTransition(second), offsets[0] - baseUtcOffset);
            }
            else
            {
                // Daylight saving time from the start of the year to the first change, and from the second change on, as in
                // the southern hemisphere.
                rule = new RuleSpecification(yearStart, yearEnd, offsets[0] - offsets[1], CreateFixedTransition(instants.Count is 2 ? second : yearStart), CreateFixedTransition(first), offsets[1] - baseUtcOffset);
            }

            if (rule.DaylightDelta >= TimeSpan.FromHours(-23) && rule.DaylightDelta <= TimeSpan.FromHours(14) && !rule.DaylightTransitionStart.Equals(rule.DaylightTransitionEnd))
            {
                result.Add(rule);
            }
        }

        return result;

        bool IsWithinYear(DateTime local) => local >= yearStart.AddSeconds(1) && local < yearStart.AddYears(1);
    }

    /// <summary>Gives the shortest period of a year the offset of a neighbor, and drops the changes left without effect.</summary>
    private static void RemoveShortestPeriod(DateTime yearStart, List<TimeSpan> offsets, List<Transition> instants)
    {
        var shortest = 0;
        var shortestDuration = TimeSpan.MaxValue;
        for (var i = 0; i < offsets.Count; i++)
        {
            var start = i is 0 ? yearStart - offsets[0] : instants[i - 1].Utc;
            var end = i == instants.Count ? yearStart.AddYears(1) - offsets[i] : instants[i].Utc;
            if (end - start < shortestDuration)
            {
                shortest = i;
                shortestDuration = end - start;
            }
        }

        if (shortest is 0)
        {
            instants.RemoveAt(0);
            offsets.RemoveAt(0);
        }
        else
        {
            instants.RemoveAt(shortest - 1);
            offsets.RemoveAt(shortest);
        }

        for (var i = instants.Count - 1; i >= 0; i--)
        {
            if (offsets[i] == offsets[i + 1])
            {
                instants.RemoveAt(i);
                offsets.RemoveAt(i + 1);
            }
        }
    }

    private static void AddRule(List<RuleSpecification> rules, RuleSpecification rule)
    {
        if (rules.Count > 0 && rules[^1].TryExtend(rule))
            return;

        rules.Add(rule);
    }

    /// <summary>Creates the time zone from the rules of each year, choosing for each year a rule the runtime reproduces.</summary>
    private static TimeZoneInfo CreateTimeZone(string id, string standardName, string daylightName, TimeSpan baseUtcOffset, List<List<RuleSpecification>> years, List<Transition> transitions)
    {
        var choices = new int[years.Count];
        var timeZone = CreateTimeZone(id, standardName, daylightName, baseUtcOffset, years, choices);

#if NET6_0_OR_GREATER
        // The runtime computes the offsets near the start and the end of a year from the rules of both years, in ways that
        // differ between versions, so the rule of a year is replaced by another expressing it when its year is not reproduced.
        for (var i = 0; i < years.Count; i++)
        {
            // The rule before the first change spans the whole past, of which only its end needs checking.
            var from = years[i][0].Start == DateTime.MinValue ? years[i][0].End.AddDays(-1) : years[i][0].Start.AddDays(-1);
            var to = years[i][0].End == DateTime.MaxValue.Date ? years[i][0].Start.AddDays(2) : years[i][0].End.AddDays(2);
            if (Verify(timeZone, transitions, from, to))
                continue;

            // The year is checked with its start and its end, which also depend on the rules of the neighboring years.
            var nextCandidateCount = i + 1 < years.Count ? years[i + 1].Count : 1;
            var isReproduced = false;
            for (var choice = 0; choice < years[i].Count && !isReproduced; choice++)
            {
                for (var nextChoice = 0; nextChoice < nextCandidateCount && !isReproduced; nextChoice++)
                {
                    if (choice == choices[i] && (i + 1 == years.Count || nextChoice == choices[i + 1]))
                        continue;

                    var previousChoice = choices[i];
                    var previousNextChoice = i + 1 < years.Count ? choices[i + 1] : 0;
                    choices[i] = choice;
                    if (i + 1 < years.Count)
                    {
                        choices[i + 1] = nextChoice;
                    }

                    var candidate = CreateTimeZone(id, standardName, daylightName, baseUtcOffset, years, choices);
                    if (Verify(candidate, transitions, from, to))
                    {
                        timeZone = candidate;
                        isReproduced = true;
                    }
                    else
                    {
                        choices[i] = previousChoice;
                        if (i + 1 < years.Count)
                        {
                            choices[i + 1] = previousNextChoice;
                        }
                    }
                }
            }
        }
#else
        _ = transitions;
#endif

        return timeZone;
    }

    private static TimeZoneInfo CreateTimeZone(string id, string standardName, string daylightName, TimeSpan baseUtcOffset, List<List<RuleSpecification>> years, int[] choices)
    {
        var specifications = new List<RuleSpecification>(years.Count);
        for (var i = 0; i < years.Count; i++)
        {
            var candidate = years[i][choices[i]];
            AddRule(specifications, new RuleSpecification(candidate.Start, candidate.End, candidate.DaylightDelta, candidate.DaylightTransitionStart, candidate.DaylightTransitionEnd, candidate.BaseUtcOffsetDelta));
        }

        var adjustmentRules = new List<TimeZoneInfo.AdjustmentRule>(specifications.Count);
        foreach (var specification in specifications)
        {
            if (specification.ToAdjustmentRule() is { } adjustmentRule)
            {
                adjustmentRules.Add(adjustmentRule);
            }
        }

        if (adjustmentRules.Count is 0)
            return TimeZoneInfo.CreateCustomTimeZone(id, baseUtcOffset, id, standardName);

        return TimeZoneInfo.CreateCustomTimeZone(id, baseUtcOffset, id, standardName, daylightName, [.. adjustmentRules]);
    }

#if NET6_0_OR_GREATER
    /// <summary>Checks the time zone reports the offsets of <paramref name="transitions"/> between two dates, at and around each change and each year boundary.</summary>
    private static bool Verify(TimeZoneInfo timeZone, List<Transition> transitions, DateTime from, DateTime to)
    {
        if (transitions.Count is 0)
            return true;

        for (var i = FindLastTransitionAtOrBefore(transitions, from) + 1; i < transitions.Count && transitions[i].Utc < to; i++)
        {
            var transition = transitions[i];
            if (GetOffset(transition.Utc.AddTicks(-1)) != transition.From || GetOffset(transition.Utc) != transition.To)
                return false;
        }

        for (var year = Math.Max(from.Year, 2); year <= Math.Min(to.Year, LastSupportedYear); year++)
        {
            for (var hours = -15; hours <= 15; hours++)
            {
                var instant = new DateTime(year, 1, 1).AddHours(hours);
                if (instant >= from && instant <= to && GetOffset(instant) != GetExpectedOffset(instant))
                    return false;
            }
        }

        return true;

        TimeSpan GetOffset(DateTime instant) => timeZone.GetUtcOffset(DateTime.SpecifyKind(instant, DateTimeKind.Utc));

        TimeSpan GetExpectedOffset(DateTime instant)
        {
            var index = FindLastTransitionAtOrBefore(transitions, instant);
            return index < 0 ? transitions[0].From : transitions[index].To;
        }
    }

    private static int FindLastTransitionAtOrBefore(List<Transition> transitions, DateTime instant)
    {
        var low = 0;
        var high = transitions.Count - 1;
        while (low <= high)
        {
            var middle = low + ((high - low) / 2);
            if (transitions[middle].Utc <= instant)
            {
                low = middle + 1;
            }
            else
            {
                high = middle - 1;
            }
        }

        return high;
    }
#endif

    /// <summary>Recognizes the yearly transition an open-ended observance describes, from the onsets it produces.</summary>
    private static bool TryRecognizeTransition(Observance observance, int patternStartYear, out TimeZoneInfo.TransitionTime transition)
    {
        transition = default;

        // The first year is skipped: a producer such as Outlook writes a DTSTART that does not follow the rule.
        var firstYear = patternStartYear + 1;
        var endYear = Math.Min(firstYear + YearsToRecognizeTransition, DateTime.MaxValue.Year);
        var onsets = new List<DateTime>();
        foreach (var onset in Expand(observance, endYear))
        {
            if (onset.Local.Year >= firstYear)
            {
                onsets.Add(onset.Local);
            }
        }

        if (onsets.Count is 0 || onsets.Count != endYear - firstYear)
            return false;

        var reference = onsets[0];
        var isFixed = true;
        var isLastWeek = true;
        var week = GetWeek(reference);
        for (var i = 0; i < onsets.Count; i++)
        {
            var onset = onsets[i];
            if (onset.Year != reference.Year + i || onset.Month != reference.Month || onset.TimeOfDay != reference.TimeOfDay)
                return false;

            isFixed &= onset.Day == reference.Day;
            if (onset.DayOfWeek != reference.DayOfWeek)
            {
                isLastWeek = false;
                week = null;
                continue;
            }

            isLastWeek &= onset.Day + 7 > DateTime.DaysInMonth(onset.Year, onset.Month);
            if (week != GetWeek(onset))
            {
                week = null;
            }
        }

        var timeOfDay = new DateTime(1, 1, 1) + reference.TimeOfDay;
        if (week is { } value)
        {
            transition = TimeZoneInfo.TransitionTime.CreateFloatingDateRule(timeOfDay, reference.Month, value, reference.DayOfWeek);
            return true;
        }

        if (isLastWeek)
        {
            // TimeZoneInfo.TransitionTime.Week 5 denotes the last occurrence in the month.
            transition = TimeZoneInfo.TransitionTime.CreateFloatingDateRule(timeOfDay, reference.Month, 5, reference.DayOfWeek);
            return true;
        }

        if (isFixed)
        {
            transition = TimeZoneInfo.TransitionTime.CreateFixedDateRule(timeOfDay, reference.Month, reference.Day);
            return true;
        }

        return false;

        static int? GetWeek(DateTime date)
        {
            var week = ((date.Day - 1) / 7) + 1;
            return week <= 4 ? week : null;
        }
    }

    private static TimeZoneInfo.TransitionTime CreateFixedTransition(DateTime local)
    {
        return TimeZoneInfo.TransitionTime.CreateFixedDateRule(new DateTime(1, 1, 1) + local.TimeOfDay, local.Month, local.Day);
    }

    /// <summary>Lists the onsets of an observance in the years before <paramref name="maxYearExclusive"/>.</summary>
    private static IEnumerable<Onset> Expand(Observance observance, int maxYearExclusive)
    {
        var count = 0;
        if (observance.RecurrenceRule is null)
        {
            if (observance.Start.Year < maxYearExclusive)
            {
                yield return new Onset(observance, observance.Start);
            }
        }
        else
        {
            foreach (var occurrence in observance.RecurrenceRule.GetNextOccurrences(observance.Start))
            {
                if (occurrence.Year >= maxYearExclusive || count >= MaxOnsetsPerObservance)
                    break;

                if (observance.UntilUtc is { } untilUtc && occurrence - observance.OffsetFrom > untilUtc)
                    break;

                if (observance.UntilLocal is { } untilLocal && occurrence > untilLocal)
                    break;

                count++;
                yield return new Onset(observance, occurrence);
            }
        }

        foreach (var date in observance.RecurrenceDates)
        {
            if (date.Year < maxYearExclusive && count < MaxOnsetsPerObservance)
            {
                count++;
                yield return new Onset(observance, date);
            }
        }
    }

    private static bool TryReadObservances(List<ContentLine> lines, out List<Observance> observances)
    {
        observances = [];
        Observance? current = null;
        var depth = 0;
        foreach (var line in lines)
        {
            if (string.Equals(line.Name, "BEGIN", StringComparison.OrdinalIgnoreCase))
            {
                depth++;
                if (depth is 1)
                {
                    var name = line.Value.ToUpperInvariant();
                    current = name is "STANDARD" or "DAYLIGHT" ? new Observance(isDaylight: name is "DAYLIGHT") : null;
                }

                continue;
            }

            if (string.Equals(line.Name, "END", StringComparison.OrdinalIgnoreCase))
            {
                depth--;
                if (depth is 0 && current is not null)
                {
                    if (!current.TryComplete())
                        return false;

                    observances.Add(current);
                    current = null;
                }

                continue;
            }

            if (depth is not 1 || current is null)
                continue;

            if (!current.TryReadProperty(line))
                return false;
        }

        return observances.Count > 0;
    }

    private static bool TryParseUtcOffset(string value, out TimeSpan offset)
    {
        // utc-offset = time-numzone = ("+" / "-") time-hour time-minute [time-second] (RFC 5545 section 3.3.14)
        offset = default;
        if (value.Length is not (5 or 7) || value[0] is not ('+' or '-'))
            return false;

        for (var i = 1; i < value.Length; i++)
        {
            if (!char.IsAsciiDigit(value[i]))
                return false;
        }

        var hours = ((value[1] - '0') * 10) + (value[2] - '0');
        var minutes = ((value[3] - '0') * 10) + (value[4] - '0');
        var seconds = value.Length is 7 ? ((value[5] - '0') * 10) + (value[6] - '0') : 0;
        if (minutes > 59 || seconds > 59)
            return false;

        // TimeZoneInfo only accepts whole minutes, which a local mean time such as +005328 is rounded to.
        var totalMinutes = (hours * 60) + minutes + (seconds >= 30 ? 1 : 0);
        offset = TimeSpan.FromMinutes(value[0] is '-' ? -totalMinutes : totalMinutes);
        return true;
    }

    private sealed class Observance(bool isDaylight)
    {
        private DateTime? _start;
        private bool _isStartUtc;
        private TimeSpan? _offsetFrom;
        private TimeSpan? _offsetTo;
        private readonly List<(DateTime Value, bool IsUtc)> _recurrenceDates = [];


        public bool IsDaylight { get; } = isDaylight;

        /// <summary>The first onset, a local time expressed in <see cref="OffsetFrom"/>.</summary>
        public DateTime Start { get; private set; }

        public TimeSpan OffsetFrom { get; private set; }

        public TimeSpan OffsetTo { get; private set; }

        public string? Name { get; private set; }

        public RecurrenceRule? RecurrenceRule { get; private set; }

        public bool IsForever => UntilUtc is null && UntilLocal is null && RecurrenceRule?.Occurrences is null;

        public DateTime? UntilUtc { get; private set; }

        public DateTime? UntilLocal { get; private set; }

        public List<DateTime> RecurrenceDates { get; } = [];

        public bool TryReadProperty(ContentLine line)
        {
            switch (line.Name.ToUpperInvariant())
            {
                case "DTSTART":
                    if (!InternetCalendarParser.TryParseDateTime(line.Value, out var start))
                        return false;

                    _start = start;
                    _isStartUtc = start.Kind is DateTimeKind.Utc;
                    return true;

                case "TZOFFSETFROM":
                    if (!TryParseUtcOffset(line.Value, out var offsetFrom))
                        return false;

                    _offsetFrom = offsetFrom;
                    return true;

                case "TZOFFSETTO":
                    if (!TryParseUtcOffset(line.Value, out var offsetTo))
                        return false;

                    _offsetTo = offsetTo;
                    return true;

                case "TZNAME":
                    Name ??= line.GetTextValue();
                    return true;

                case "RRULE":
                    // A time zone changes at most a few times a year, so any other frequency is not a real observance.
                    if (!RecurrenceRule.TryParse(line.Value, out var recurrenceRule) || recurrenceRule is not YearlyRecurrenceRule)
                        return false;

                    if (recurrenceRule.EndDate is { } until)
                    {
                        if (until.Kind is DateTimeKind.Utc)
                        {
                            UntilUtc = until;
                        }
                        else
                        {
                            UntilLocal = until;
                        }

                        // The bound is applied by the reader, which knows the offset a UTC bound is compared in.
                        recurrenceRule.EndDate = null;
                    }

                    RecurrenceRule = recurrenceRule;
                    return true;

                case "RDATE":
                    foreach (var item in line.Value.Split(','))
                    {
                        // A PERIOD value starts at its first date-time.
                        var slash = item.IndexOf('/', StringComparison.Ordinal);
                        if (!InternetCalendarParser.TryParseDateTime(slash < 0 ? item : item[..slash], out var date))
                            return false;

                        _recurrenceDates.Add((date, date.Kind is DateTimeKind.Utc));
                    }

                    return true;

                default:
                    return true;
            }
        }

        public bool TryComplete()
        {
            if (_start is not { } start || _offsetFrom is not { } offsetFrom || _offsetTo is not { } offsetTo)
                return false;

            OffsetFrom = offsetFrom;
            OffsetTo = offsetTo;

            // The onsets are local times, so a UTC value a producer wrote anyway is expressed in the offset in effect before it.
            Start = ToLocal(start, _isStartUtc);
            foreach (var (value, isUtc) in _recurrenceDates)
            {
                RecurrenceDates.Add(ToLocal(value, isUtc));
            }

            return true;
        }

        private DateTime ToLocal(DateTime value, bool isUtc)
        {
            return DateTime.SpecifyKind(isUtc ? value + OffsetFrom : value, DateTimeKind.Unspecified);
        }
    }

    private readonly struct Onset(Observance observance, DateTime local)
    {
        public Observance Observance { get; } = observance;

        public DateTime Local { get; } = local;

        public DateTime Utc { get; } = local - observance.OffsetFrom;
    }

    /// <summary>A change of the UTC offset.</summary>
    private readonly struct Transition(DateTime utc, TimeSpan from, TimeSpan to, bool isDaylight)
    {
        public DateTime Utc { get; } = utc;

        public TimeSpan From { get; } = from;

        public TimeSpan To { get; } = to;

        public bool IsDaylight { get; } = isDaylight;

        public DateTime Local => Utc + From;
    }

    private sealed class RuleSpecification(DateTime start, DateTime end, TimeSpan daylightDelta, TimeZoneInfo.TransitionTime daylightTransitionStart, TimeZoneInfo.TransitionTime daylightTransitionEnd, TimeSpan baseUtcOffsetDelta)
    {
        public DateTime Start { get; } = start;

        public DateTime End { get; private set; } = end;

        public TimeSpan DaylightDelta { get; } = daylightDelta;

        public TimeZoneInfo.TransitionTime DaylightTransitionStart { get; } = daylightTransitionStart;

        public TimeZoneInfo.TransitionTime DaylightTransitionEnd { get; } = daylightTransitionEnd;

        public TimeSpan BaseUtcOffsetDelta { get; } = baseUtcOffsetDelta;

        public static RuleSpecification CreateConstant(DateTime start, DateTime end, TimeSpan baseUtcOffsetDelta)
        {
            // These two transitions are how the runtime itself marks a rule without daylight saving time.
            return new RuleSpecification(
                start,
                end,
                TimeSpan.Zero,
                TimeZoneInfo.TransitionTime.CreateFixedDateRule(DateTime.MinValue, 1, 1),
                TimeZoneInfo.TransitionTime.CreateFixedDateRule(DateTime.MinValue.AddMilliseconds(1), 1, 1),
                baseUtcOffsetDelta);
        }

        /// <summary>Merges a rule into this one when it continues it with the same offsets and transitions.</summary>
        public bool TryExtend(RuleSpecification next)
        {
            if (End == DateTime.MaxValue.Date ||
                End.AddDays(1) != next.Start ||
                DaylightDelta != next.DaylightDelta ||
                BaseUtcOffsetDelta != next.BaseUtcOffsetDelta ||
                !DaylightTransitionStart.Equals(next.DaylightTransitionStart) ||
                !DaylightTransitionEnd.Equals(next.DaylightTransitionEnd))
            {
                return false;
            }

            End = next.End;
            return true;
        }

        public TimeZoneInfo.AdjustmentRule? ToAdjustmentRule()
        {
            // A rule that neither changes the offset nor observes daylight saving time is what the base offset already says.
            if (DaylightDelta == TimeSpan.Zero && BaseUtcOffsetDelta == TimeSpan.Zero)
                return null;

#if NET6_0_OR_GREATER
            return TimeZoneInfo.AdjustmentRule.CreateAdjustmentRule(Start, End, DaylightDelta, DaylightTransitionStart, DaylightTransitionEnd, BaseUtcOffsetDelta);
#else
            // TimeZoneInfo.AdjustmentRule.BaseUtcOffsetDelta cannot be set before .NET 6.
            if (BaseUtcOffsetDelta != TimeSpan.Zero)
                return null;

            return TimeZoneInfo.AdjustmentRule.CreateAdjustmentRule(Start, End, DaylightDelta, DaylightTransitionStart, DaylightTransitionEnd);
#endif
        }
    }
}
