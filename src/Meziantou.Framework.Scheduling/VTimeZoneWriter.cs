namespace Meziantou.Framework.Scheduling;

/// <summary>Writes the VTIMEZONE component (RFC 5545 section 3.6.5) describing a <see cref="TimeZoneInfo"/>.</summary>
/// <remarks>
/// The sub-components are derived from the UTC offsets the time zone reports rather than from the fields of its adjustment
/// rules. The runtime builds those rules from the time zone database in ways that do not round-trip: a transition at "26:00"
/// keeps the day of the week it is counted from, a rule materialized for a single year looks like a yearly one, and a change
/// of the standard offset is not a daylight saving time rule at all. Reading the rules can therefore shift a transition by a
/// day, repeat a year-specific transition forever, or miss an offset change.
/// </remarks>
internal static class VTimeZoneWriter
{
    // The probing starts one day before the first covered year and ends one day after the last one.
    private const int MinYear = 2;
    private const int MaxYear = 9998;

    // The day of the week a month starts on and whether the year is a leap year repeat every 28 years, so a pattern holding
    // for that many consecutive years of the open-ended rule holds for every later year.
    private const int OngoingPatternVerificationYears = 28;

    // The kinds are declared from the most to the least preferred, when several describe the same transitions.
    private enum PatternKind
    {
        NthDayOfWeek,
        LastDayOfWeek,
        DayOfMonth,
        DayOfWeekInMonthDays,
        DayOfWeekInYearDays,
    }

    public static void Write(TextWriter writer, TimeZoneInfo timeZone, DateTime referenceDate)
    {
        Utilities.WriteLine(writer, "BEGIN:VTIMEZONE");

        // The identifier was validated before any output was written. The property value is TEXT, so a comma, a semicolon or a
        // backslash is escaped.
        Utilities.WriteLine(writer, "TZID:" + Utilities.EscapeText(timeZone.Id));

        // The sub-components are anchored in the year of the events rather than in the year a rule starts, so a consumer
        // applies the transitions to them instead of only to later occurrences.
        var firstYear = Math.Min(Math.Max(referenceDate.Year, MinYear), MaxYear);
        var lastYear = GetLastCoveredYear(timeZone, firstYear, out var ongoingFromYear);
        var transitions = FindTransitions(timeZone, firstYear, lastYear);
        if (transitions.Count == 0)
        {
            // RFC 5545 section 3.6.5 requires at least one sub-component, so a time zone whose offset does not change is
            // described by a single STANDARD whose FROM and TO offsets are equal.
            var offset = Utilities.UtcOffsetToString(timeZone.GetUtcOffset(new DateTime(firstYear, 1, 1, 0, 0, 0, DateTimeKind.Utc)));
            Utilities.WriteLine(writer, "BEGIN:STANDARD");
            Utilities.WriteLine(writer, "DTSTART:19700101T000000");
            Utilities.WriteLine(writer, "TZOFFSETFROM:" + offset);
            Utilities.WriteLine(writer, "TZOFFSETTO:" + offset);
            Utilities.WriteLine(writer, "END:STANDARD");
        }
        else
        {
            foreach (var component in CreateComponents(transitions, lastYear, ongoingFromYear))
            {
                component.Write(writer);
            }
        }

        Utilities.WriteLine(writer, "END:VTIMEZONE");
    }

    /// <summary>
    /// Gets the last year whose transitions are written. Past the last adjustment rule, the offsets of a time zone either never
    /// change again or follow its open-ended rule; the latter is probed for long enough to prove a yearly pattern reproduces it.
    /// </summary>
    private static int GetLastCoveredYear(TimeZoneInfo timeZone, int firstYear, out int? ongoingFromYear)
    {
        var lastRuleYear = firstYear;
        var isOngoing = false;
        foreach (var rule in timeZone.GetAdjustmentRules())
        {
            int year;
            if (rule.DateEnd == DateTime.MaxValue.Date)
            {
                year = rule.DateStart.Year;
                if (rule.DaylightDelta != TimeSpan.Zero)
                {
                    isOngoing = true;
                }
            }
            else
            {
                year = rule.DateEnd.Year;
            }

            lastRuleYear = Math.Max(lastRuleYear, year);
        }

        if (isOngoing)
        {
            // The rule may start during its first year, so only the following years are known to follow it entirely.
            ongoingFromYear = Math.Min(lastRuleYear + 1, MaxYear);
            return Math.Min(lastRuleYear + 1 + OngoingPatternVerificationYears, MaxYear);
        }

        ongoingFromYear = null;
        return Math.Min(lastRuleYear + 1, MaxYear);
    }

    /// <summary>Finds every change of the UTC offset whose local onset is in <paramref name="firstYear"/> to <paramref name="lastYear"/>.</summary>
    private static List<Transition> FindTransitions(TimeZoneInfo timeZone, int firstYear, int lastYear)
    {
        var transitions = new List<Transition>();

        // The offsets are sampled at every midnight UTC, whatever the covered years, so the transitions found for a given
        // year do not depend on the events. An offset reported for less than a day between two samples is not seen: on Unix,
        // the runtime reports one between local and UTC midnight where two adjustment rules meet.
        var sample = new DateTime(firstYear, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddDays(-1);
        var end = new DateTime(lastYear + 1, 1, 2, 0, 0, 0, DateTimeKind.Utc);
        var offset = timeZone.GetUtcOffset(sample);
        while (sample < end)
        {
            var nextSample = sample.AddDays(1);
            var intervalStart = sample;
            while (timeZone.GetUtcOffset(nextSample) != offset)
            {
                // The time zone database states transitions to the second, so the first second with the new offset is the
                // transition. The runtime may apply it a fraction of a second earlier, as for a rule ending at 23:59:59.999.
                var before = intervalStart;
                var after = nextSample;
                while (after.Ticks - before.Ticks > TimeSpan.TicksPerSecond)
                {
                    var middle = new DateTime(before.Ticks + ((after.Ticks - before.Ticks) / TimeSpan.TicksPerSecond / 2 * TimeSpan.TicksPerSecond), DateTimeKind.Utc);
                    if (timeZone.GetUtcOffset(middle) == offset)
                    {
                        before = middle;
                    }
                    else
                    {
                        after = middle;
                    }
                }

                var newOffset = timeZone.GetUtcOffset(after);
                var transition = new Transition(after, offset, newOffset, timeZone.IsDaylightSavingTime(after));
                if (transition.LocalOnset.Year >= firstYear && transition.LocalOnset.Year <= lastYear)
                {
                    transitions.Add(transition);
                }

                intervalStart = after;
                offset = newOffset;
            }

            sample = nextSample;
        }

        return transitions;
    }

    private static List<SubComponent> CreateComponents(List<Transition> transitions, int lastYear, int? ongoingFromYear)
    {
        var groups = new List<List<Transition>>();
        var groupIndexes = new Dictionary<(bool IsDaylight, TimeSpan From, TimeSpan To), int>();
        foreach (var transition in transitions)
        {
            var key = (transition.IsDaylight, transition.From, transition.To);
            if (!groupIndexes.TryGetValue(key, out var index))
            {
                index = groups.Count;
                groupIndexes.Add(key, index);
                groups.Add([]);
            }

            groups[index].Add(transition);
        }

        var components = new List<SubComponent>();
        foreach (var group in groups)
        {
            var singles = new List<Transition>();

            // Runs are grown from the most recent transition backwards, so the run reaching the end of the covered years,
            // which may be written as an open-ended recurrence, is as long as possible.
            var last = group.Count - 1;
            while (last >= 0)
            {
                var candidates = GetCandidatePatterns(group[last].LocalOnset.Date);
                var first = last;
                while (first > 0)
                {
                    var previous = group[first - 1].LocalOnset;
                    if (previous.Year != group[first].LocalOnset.Year - 1 || previous.TimeOfDay != group[last].LocalOnset.TimeOfDay)
                        break;

                    var remaining = candidates.FindAll(candidate => candidate.Matches(previous.Date));
                    if (remaining.Count == 0)
                        break;

                    candidates = remaining;
                    first--;
                }

                if (first == last)
                {
                    singles.Add(group[first]);
                }
                else
                {
                    var pattern = candidates[0];
                    foreach (var candidate in candidates)
                    {
                        if (candidate.Kind < pattern.Kind)
                        {
                            pattern = candidate;
                        }
                    }

                    var rule = pattern.ToRecurrenceRule();
                    var isOngoing = ongoingFromYear is not null &&
                        last == group.Count - 1 &&
                        group[last].LocalOnset.Year == lastYear &&
                        group[first].LocalOnset.Year <= ongoingFromYear;
                    if (!isOngoing)
                    {
                        // UNTIL is inclusive and, in a VTIMEZONE, MUST be a UTC time (RFC 5545 section 3.6.5).
                        rule += ";UNTIL=" + group[last].Utc.ToString(Utilities.UtcDateTimeFormat, CultureInfo.InvariantCulture);
                    }

                    components.Add(new SubComponent(group[first], rule, []));
                }

                last = first - 1;
            }

            if (singles.Count > 0)
            {
                // The transitions that follow no yearly pattern are listed as recurrence dates of a single sub-component.
                singles.Reverse();
                components.Add(new SubComponent(singles[0], recurrenceRule: null, singles.GetRange(1, singles.Count - 1)));
            }
        }

        components.Sort((a, b) => a.First.Utc.CompareTo(b.First.Utc));
        return components;
    }

    /// <summary>Gets the yearly patterns that produce <paramref name="date"/> in its own year.</summary>
    private static List<DatePattern> GetCandidatePatterns(DateTime date)
    {
        var result = new List<DatePattern>();
        for (var days = 0; days < 7; days++)
        {
            // The date is the first occurrence of its day of the week on or after each of the 7 days preceding it.
            var windowStart = date.AddDays(-days);
            var month = windowStart.Month;
            var day = windowStart.Day;
            if (day + 6 <= GetMinDaysInMonth(month))
            {
                result.Add((day - 1) % 7 == 0
                    ? new DatePattern(PatternKind.NthDayOfWeek, month, day, date.DayOfWeek)
                    : new DatePattern(PatternKind.DayOfWeekInMonthDays, month, day, date.DayOfWeek));
            }
            else if (day + 6 > GetMaxDaysInMonth(month) && month is not (2 or 12))
            {
                // A window spilling over the next month is stated in days of the year, whose numbering does not depend on
                // leap years when counted from the start of January or from the end of the year for the other months.
                result.Add(new DatePattern(PatternKind.DayOfWeekInYearDays, month, day, date.DayOfWeek));
            }
        }

        if (date.Day + 7 > DateTime.DaysInMonth(date.Year, date.Month))
        {
            result.Add(new DatePattern(PatternKind.LastDayOfWeek, date.Month, day: 0, date.DayOfWeek));
        }

        result.Add(new DatePattern(PatternKind.DayOfMonth, date.Month, date.Day, date.DayOfWeek));
        return result;
    }

    private static int GetMinDaysInMonth(int month) => DateTime.DaysInMonth(2001, month);

    private static int GetMaxDaysInMonth(int month) => DateTime.DaysInMonth(2000, month);

    private readonly struct Transition
    {
        public Transition(DateTime utc, TimeSpan from, TimeSpan to, bool isDaylight)
        {
            Utc = utc;
            From = from;
            To = to;
            IsDaylight = isDaylight;
        }

        public DateTime Utc { get; }
        public TimeSpan From { get; }
        public TimeSpan To { get; }
        public bool IsDaylight { get; }

        // The DTSTART and RDATE of a sub-component are local times expressed in the TZOFFSETFROM offset.
        public DateTime LocalOnset => DateTime.SpecifyKind(Utc + From, DateTimeKind.Unspecified);
    }

    private sealed class DatePattern
    {
        public DatePattern(PatternKind kind, int month, int day, DayOfWeek dayOfWeek)
        {
            Kind = kind;
            Month = month;
            Day = day;
            DayOfWeek = dayOfWeek;
        }

        public PatternKind Kind { get; }
        public int Month { get; }

        // The day of the month for DayOfMonth, and the first day of the 7-day window for the other kinds but LastDayOfWeek.
        public int Day { get; }
        public DayOfWeek DayOfWeek { get; }

        public bool Matches(DateTime date)
        {
            var year = date.Year;
            return Kind switch
            {
                PatternKind.DayOfMonth => Day <= DateTime.DaysInMonth(year, Month) && date == new DateTime(year, Month, Day),
                PatternKind.LastDayOfWeek => date == GetLastDayOfWeek(new DateTime(year, Month, DateTime.DaysInMonth(year, Month))),
                _ => date == GetFirstDayOfWeek(new DateTime(year, Month, Day)),
            };
        }

        public string ToRecurrenceRule()
        {
            var dayOfWeek = Utilities.DayOfWeekToString(DayOfWeek);
            var yearlyInMonth = "FREQ=YEARLY;BYMONTH=" + Month.ToString(CultureInfo.InvariantCulture);
            return Kind switch
            {
                PatternKind.NthDayOfWeek => yearlyInMonth + ";BYDAY=" + (((Day - 1) / 7) + 1).ToString(CultureInfo.InvariantCulture) + dayOfWeek,
                PatternKind.LastDayOfWeek => yearlyInMonth + ";BYDAY=-1" + dayOfWeek,
                PatternKind.DayOfMonth => yearlyInMonth + ";BYMONTHDAY=" + Day.ToString(CultureInfo.InvariantCulture),
                PatternKind.DayOfWeekInMonthDays => yearlyInMonth + ";BYDAY=" + dayOfWeek + ";BYMONTHDAY=" + JoinSevenDays(Day),

                // With BYYEARDAY, BYDAY limits the days instead of expanding them (RFC 5545 section 3.3.10). A day after
                // February is counted from the end of the year: -1 is December 31 whether or not the year is a leap year.
                _ => "FREQ=YEARLY;BYYEARDAY=" + JoinSevenDays(Month is 1 ? Day : new DateTime(2001, Month, Day).DayOfYear - 366) + ";BYDAY=" + dayOfWeek,
            };
        }

        private DateTime GetFirstDayOfWeek(DateTime onOrAfter) => onOrAfter.AddDays(((int)DayOfWeek - (int)onOrAfter.DayOfWeek + 7) % 7);

        private DateTime GetLastDayOfWeek(DateTime onOrBefore) => onOrBefore.AddDays(-(((int)onOrBefore.DayOfWeek - (int)DayOfWeek + 7) % 7));

        private static string JoinSevenDays(int first)
        {
            var sb = new StringBuilder();
            for (var i = 0; i < 7; i++)
            {
                if (i > 0)
                {
                    sb.Append(',');
                }

                sb.Append((first + i).ToString(CultureInfo.InvariantCulture));
            }

            return sb.ToString();
        }
    }

    private sealed class SubComponent
    {
        public SubComponent(Transition first, string? recurrenceRule, List<Transition> additionalOnsets)
        {
            First = first;
            RecurrenceRule = recurrenceRule;
            AdditionalOnsets = additionalOnsets;
        }

        public Transition First { get; }
        public string? RecurrenceRule { get; }
        public List<Transition> AdditionalOnsets { get; }

        public void Write(TextWriter writer)
        {
            var name = First.IsDaylight ? "DAYLIGHT" : "STANDARD";
            Utilities.WriteLine(writer, "BEGIN:" + name);
            Utilities.WriteLine(writer, "DTSTART:" + First.LocalOnset.ToString(Utilities.FloatingDateTimeFormat, CultureInfo.InvariantCulture));
            Utilities.WriteLine(writer, "TZOFFSETFROM:" + Utilities.UtcOffsetToString(First.From));
            Utilities.WriteLine(writer, "TZOFFSETTO:" + Utilities.UtcOffsetToString(First.To));
            if (RecurrenceRule is not null)
            {
                Utilities.WriteLine(writer, "RRULE:" + RecurrenceRule);
            }

            foreach (var onset in AdditionalOnsets)
            {
                Utilities.WriteLine(writer, "RDATE:" + onset.LocalOnset.ToString(Utilities.FloatingDateTimeFormat, CultureInfo.InvariantCulture));
            }

            Utilities.WriteLine(writer, "END:" + name);
        }
    }
}
