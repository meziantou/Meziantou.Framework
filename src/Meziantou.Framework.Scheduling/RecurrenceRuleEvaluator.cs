namespace Meziantou.Framework.Scheduling;

/// <summary>Evaluates a recurrence rule with the expansion algorithm of RFC 5545 section 3.3.10.</summary>
/// <remarks>
/// <para>The periods of the rule (a year, a month, a week, a day, an hour, a minute or a second) are visited on the
/// grid defined by the start date and the interval. For each period, the days are selected by BYMONTH, BYWEEKNO,
/// BYYEARDAY, BYMONTHDAY and BYDAY, then expanded by BYHOUR, BYMINUTE and BYSECOND. The resulting set is sorted,
/// BYSETPOS is applied to it, and only then are the instances before the start date removed.</para>
/// <para>Dates are handled as day numbers (days since 0001-01-01) so a period never materializes a
/// <see cref="DateTime"/> per candidate day.</para>
/// </remarks>
internal sealed class RecurrenceRuleEvaluator
{
    private const long TicksPerSecond = TimeSpan.TicksPerSecond;
    private const long TicksPerDay = TimeSpan.TicksPerDay;
    private const int SecondsPerHour = 3600;
    private const int SecondsPerDay = 86400;
    private const int MaxYear = 9999;
    private const long DaysPer400Years = 146097;
    private const long MonthsPer400Years = 4800;
    private const long SecondsPer400Years = DaysPer400Years * SecondsPerDay;

    private static readonly long MaxDayNumber = DateTime.MaxValue.Ticks / TicksPerDay;
    private static readonly long MaxSecondNumber = DateTime.MaxValue.Ticks / TicksPerSecond;
    private static readonly int[] DaysToMonth365 = [0, 31, 59, 90, 120, 151, 181, 212, 243, 273, 304, 334, 365];
    private static readonly int[] DaysToMonth366 = [0, 31, 60, 91, 121, 152, 182, 213, 244, 274, 305, 335, 366];

    private readonly Frequency _frequency;
    private readonly long _interval;
    private readonly int _weekStart;
    private readonly DateTimeKind _kind;
    private readonly long _startTicks;
    private readonly long _startDayNumber;
    private readonly int _startYear;
    private readonly int _startMonth;
    private readonly long _endTicks;
    private readonly long _fractionTicks;
    private readonly long _periodsPerCycle;

    private readonly bool _hasMonthFilter;
    private readonly int _monthMask;

    private readonly bool _hasWeekNumberFilter;
    private readonly ulong _positiveWeekNumbers;
    private readonly ulong _negativeWeekNumbers;

    private readonly bool _hasYearDayFilter;
    private readonly ulong[] _positiveYearDays = new ulong[6];
    private readonly ulong[] _negativeYearDays = new ulong[6];

    private readonly bool _hasMonthDayFilter;
    private readonly ulong _positiveMonthDays;
    private readonly ulong _negativeMonthDays;

    private readonly bool _hasDayFilter;
    private readonly int _weekDayMask;
    private readonly (int DayOfWeek, int Ordinal)[] _ordinalWeekDays;
    private readonly bool _ordinalWeekDaysSpanTheYear;

    private readonly bool _hasHourFilter;
    private readonly ulong _hourMask;
    private readonly bool _hasMinuteFilter;
    private readonly ulong _minuteMask;
    private readonly bool _hasSecondFilter;
    private readonly ulong _secondMask;

    private readonly int[] _hours;
    private readonly int[] _minutes;
    private readonly int[] _seconds;
    private readonly int[]? _setPositions;

    private RecurrenceRuleEvaluator(
        Frequency frequency,
        RecurrenceRule rule,
        DateTime startDate,
        DateTime? endBound,
        IList<DayOfWeek>? weekDays,
        IList<ByDay>? byDays,
        IList<int>? months,
        IList<int>? monthDays,
        IList<int>? yearDays,
        IList<int>? weekNumbers)
    {
        _frequency = frequency;
        _interval = rule.Interval;
        _weekStart = (int)rule.WeekStart;
        _kind = startDate.Kind;
        _startTicks = startDate.Ticks;
        _startDayNumber = startDate.Ticks / TicksPerDay;
        _startYear = startDate.Year;
        _startMonth = startDate.Month;
        _endTicks = endBound?.Ticks ?? long.MaxValue;

        var byHours = rule.ByHours;
        var byMinutes = rule.ByMinutes;
        var bySeconds = rule.BySeconds;
        var hasTimeParts = !IsEmpty(byHours) || !IsEmpty(byMinutes) || !IsEmpty(bySeconds);

        // Sub-second precision cannot be expressed by the rule, so it is kept from the start date only when no time part replaces the time of day
        _fractionTicks = hasTimeParts ? 0 : startDate.Ticks % TicksPerSecond;

        // BYMONTH
        if (!IsEmpty(months))
        {
            _hasMonthFilter = true;
            foreach (var month in months)
            {
                if (month is >= 1 and <= 12)
                {
                    _monthMask |= 1 << month;
                }
            }
        }

        // BYWEEKNO
        if (!IsEmpty(weekNumbers))
        {
            _hasWeekNumberFilter = true;
            foreach (var weekNumber in weekNumbers)
            {
                if (weekNumber is >= 1 and <= 53)
                {
                    _positiveWeekNumbers |= 1UL << weekNumber;
                }
                else if (weekNumber is <= -1 and >= -53)
                {
                    _negativeWeekNumbers |= 1UL << -weekNumber;
                }
            }
        }

        // BYYEARDAY
        if (!IsEmpty(yearDays))
        {
            _hasYearDayFilter = true;
            foreach (var yearDay in yearDays)
            {
                if (yearDay is >= 1 and <= 366)
                {
                    SetBit(_positiveYearDays, yearDay);
                }
                else if (yearDay is <= -1 and >= -366)
                {
                    SetBit(_negativeYearDays, -yearDay);
                }
            }
        }

        // BYMONTHDAY
        if (!IsEmpty(monthDays))
        {
            _hasMonthDayFilter = true;
            foreach (var monthDay in monthDays)
            {
                if (monthDay is >= 1 and <= 31)
                {
                    _positiveMonthDays |= 1UL << monthDay;
                }
                else if (monthDay is <= -1 and >= -31)
                {
                    _negativeMonthDays |= 1UL << -monthDay;
                }
            }
        }

        // BYDAY
        var ordinalWeekDays = new List<(int DayOfWeek, int Ordinal)>();
        if (!IsEmpty(weekDays))
        {
            _hasDayFilter = true;
            foreach (var weekDay in weekDays)
            {
                if (weekDay is >= DayOfWeek.Sunday and <= DayOfWeek.Saturday)
                {
                    _weekDayMask |= 1 << (int)weekDay;
                }
            }
        }

        if (!IsEmpty(byDays))
        {
            _hasDayFilter = true;
            foreach (var byDay in byDays)
            {
                if (byDay is null || byDay.DayOfWeek is < DayOfWeek.Sunday or > DayOfWeek.Saturday)
                    continue;

                if (byDay.Ordinal is null)
                {
                    _weekDayMask |= 1 << (int)byDay.DayOfWeek;
                }
                else if (byDay.Ordinal.Value is not 0 && !ordinalWeekDays.Contains(((int)byDay.DayOfWeek, byDay.Ordinal.Value)))
                {
                    ordinalWeekDays.Add(((int)byDay.DayOfWeek, byDay.Ordinal.Value));
                }
            }
        }

        _ordinalWeekDays = [.. ordinalWeekDays];

        // RFC 5545: within a YEARLY rule, a numbered BYDAY refers to the year, unless BYMONTH narrows it to the month
        _ordinalWeekDaysSpanTheYear = frequency is Frequency.Yearly && !_hasMonthFilter;

        // Default day selection when the rule does not specify one (RFC 5545 section 3.3.10: the missing parts are taken from DTSTART)
        if (!_hasWeekNumberFilter && !_hasYearDayFilter && !_hasMonthDayFilter && !_hasDayFilter)
        {
            switch (frequency)
            {
                case Frequency.Yearly:
                    if (!_hasMonthFilter)
                    {
                        _hasMonthFilter = true;
                        _monthMask = 1 << startDate.Month;
                    }

                    _hasMonthDayFilter = true;
                    _positiveMonthDays = 1UL << startDate.Day;
                    break;

                case Frequency.Monthly:
                    _hasMonthDayFilter = true;
                    _positiveMonthDays = 1UL << startDate.Day;
                    break;
            }
        }

        if (frequency is Frequency.Weekly && !_hasDayFilter)
        {
            _hasDayFilter = true;
            _weekDayMask = 1 << (int)startDate.DayOfWeek;
        }

        // BYHOUR, BYMINUTE and BYSECOND expand a period longer than their unit, and limit the other ones
        _hasHourFilter = !IsEmpty(byHours);
        _hourMask = CreateMask(byHours, max: 23, normalizeLeapSecond: false);
        _hours = frequency >= Frequency.Daily ? CreateList(_hourMask, _hasHourFilter, startDate.Hour, max: 23) : [];

        _hasMinuteFilter = !IsEmpty(byMinutes);
        _minuteMask = CreateMask(byMinutes, max: 59, normalizeLeapSecond: false);
        _minutes = frequency >= Frequency.Hourly ? CreateList(_minuteMask, _hasMinuteFilter, startDate.Minute, max: 59) : [];

        _hasSecondFilter = !IsEmpty(bySeconds);
        _secondMask = CreateMask(bySeconds, max: 59, normalizeLeapSecond: true);
        _seconds = frequency >= Frequency.Minutely ? CreateList(_secondMask, _hasSecondFilter, startDate.Second, max: 59) : [];

        var setPositions = rule.BySetPositions;
        if (!IsEmpty(setPositions))
        {
            _setPositions = [.. setPositions.Where(position => position is not 0)];
        }

        // The Gregorian calendar, week days included, repeats every 400 years, so the instances of a period only depend on
        // where its start falls within that cycle. The starts visit this many distinct positions of the cycle before they
        // repeat: once that many consecutive periods produced nothing, no later period can produce anything either. This ends
        // the rules whose interval never lands on a matching period, such as FREQ=DAILY;INTERVAL=7;BYDAY=TU from a Monday,
        // instead of scanning every period up to DateTime.MaxValue.
        _periodsPerCycle = frequency switch
        {
            Frequency.Yearly => 400 / GreatestCommonDivisor(_interval, 400),
            Frequency.Monthly => MonthsPer400Years / GreatestCommonDivisor(_interval, MonthsPer400Years),
            Frequency.Weekly => DaysPer400Years / GreatestCommonDivisor(7 * _interval, DaysPer400Years),
            Frequency.Daily => DaysPer400Years / GreatestCommonDivisor(_interval, DaysPer400Years),
            _ => SecondsPer400Years / GreatestCommonDivisor(GetStepInSeconds(), SecondsPer400Years),
        };
    }

    public static IEnumerable<DateTime> Evaluate(
        Frequency frequency,
        RecurrenceRule rule,
        DateTime startDate,
        DateTime? endBound,
        IList<DayOfWeek>? weekDays = null,
        IList<ByDay>? byDays = null,
        IList<int>? months = null,
        IList<int>? monthDays = null,
        IList<int>? yearDays = null,
        IList<int>? weekNumbers = null)
    {
        var evaluator = new RecurrenceRuleEvaluator(frequency, rule, startDate, endBound, weekDays, byDays, months, monthDays, yearDays, weekNumbers);
        return evaluator.Enumerate();
    }

    private IEnumerable<DateTime> Enumerate()
    {
        if (!IsSatisfiable())
            yield break;

        var days = new int[_frequency switch
        {
            Frequency.Yearly => 366,
            Frequency.Monthly => 31,
            Frequency.Weekly => 7,
            _ => 1,
        }];

        var hour = new int[1];
        var minute = new int[1];
        var second = new int[1];
        var hours = _frequency >= Frequency.Daily ? _hours : hour;
        var minutes = _frequency >= Frequency.Hourly ? _minutes : minute;
        var seconds = _frequency >= Frequency.Minutely ? _seconds : second;

        var timesPerDay = (long)hours.Length * minutes.Length * seconds.Length;
        var timesPerHour = (long)minutes.Length * seconds.Length;
        var setPositions = _setPositions;
        var indexes = new long[setPositions?.Length ?? 0];

        long periodIndex = 0;

        // The index of the first period after the last one that produced an instance, whether or not it precedes the start date
        long firstPeriodAfterLastInstance = 0;
        while (true)
        {
            var scanLimit = firstPeriodAfterLastInstance + _periodsPerCycle;
            int dayCount;
            if (_frequency >= Frequency.Daily)
            {
                dayCount = MoveToNextDayPeriod(ref periodIndex, days, scanLimit);
            }
            else
            {
                dayCount = MoveToNextTimePeriod(ref periodIndex, days, scanLimit, out hour[0], out minute[0], out second[0]);
            }

            if (dayCount is 0)
                yield break;

            if (setPositions is null)
            {
                firstPeriodAfterLastInstance = periodIndex;
                for (var dayIndex = 0; dayIndex < dayCount; dayIndex++)
                {
                    foreach (var h in hours)
                    {
                        foreach (var m in minutes)
                        {
                            foreach (var s in seconds)
                            {
                                var ticks = GetTicks(days[dayIndex], h, m, s);
                                if (ticks > _endTicks)
                                    yield break;

                                if (ticks >= _startTicks)
                                    yield return new DateTime(ticks, _kind);
                            }
                        }
                    }
                }
            }
            else
            {
                var indexCount = SelectSetPositions(setPositions, dayCount * timesPerDay, indexes);
                if (indexCount > 0)
                {
                    firstPeriodAfterLastInstance = periodIndex;
                }

                for (var i = 0; i < indexCount; i++)
                {
                    var index = indexes[i];
                    var dayIndex = index / timesPerDay;
                    var timeIndex = index % timesPerDay;
                    var ticks = GetTicks(
                        days[dayIndex],
                        hours[timeIndex / timesPerHour],
                        minutes[timeIndex % timesPerHour / seconds.Length],
                        seconds[timeIndex % seconds.Length]);

                    if (ticks > _endTicks)
                        yield break;

                    if (ticks >= _startTicks)
                        yield return new DateTime(ticks, _kind);
                }
            }
        }
    }

    private long GetTicks(int dayNumber, int hour, int minute, int second)
    {
        return (dayNumber * TicksPerDay) + (((hour * SecondsPerHour) + (minute * 60L) + second) * TicksPerSecond) + _fractionTicks;
    }

    /// <summary>Detects the rules that can never produce an occurrence, so that their enumeration ends instead of scanning every period up to <see cref="DateTime.MaxValue"/>.</summary>
    private bool IsSatisfiable()
    {
        if (_hasMonthFilter && _monthMask is 0)
            return false;

        if (_hasWeekNumberFilter && _positiveWeekNumbers is 0 && _negativeWeekNumbers is 0)
            return false;

        if (_hasYearDayFilter && _positiveYearDays.All(value => value is 0) && _negativeYearDays.All(value => value is 0))
            return false;

        if (_hasMonthDayFilter && _positiveMonthDays is 0 && _negativeMonthDays is 0)
            return false;

        if (_hasDayFilter && _weekDayMask is 0 && _ordinalWeekDays.Length is 0)
            return false;

        if ((_hasHourFilter && _hourMask is 0) || (_hasMinuteFilter && _minuteMask is 0) || (_hasSecondFilter && _secondMask is 0))
            return false;

        if (_setPositions is not null)
        {
            // A period never holds more instances than this, so a position beyond it never selects anything
            long maxDays = _frequency switch
            {
                Frequency.Yearly => 366,
                Frequency.Monthly => 31,
                Frequency.Weekly => CountBits((ulong)_weekDayMask),
                _ => 1,
            };

            var maxPeriodSize = maxDays * _frequency switch
            {
                Frequency.Secondly => 1,
                Frequency.Minutely => _seconds.Length,
                Frequency.Hourly => (long)_minutes.Length * _seconds.Length,
                _ => (long)_hours.Length * _minutes.Length * _seconds.Length,
            };

            if (!_setPositions.Any(position => Math.Abs((long)position) <= maxPeriodSize))
                return false;
        }

        if (!HasAnyIncludedDay())
            return false;

        if (_frequency < Frequency.Daily)
        {
            // The periods start at the times of day congruent to the start modulo gcd(step, 1 day). When none of the
            // times of day allowed by BYHOUR, BYMINUTE and BYSECOND is congruent, no period ever matches.
            var step = GetStepInSeconds();
            var divisor = GreatestCommonDivisor(step, SecondsPerDay);
            var residue = GetFirstTimePeriod() % SecondsPerDay % divisor;

            for (var h = 0; h < 24; h++)
            {
                if (_hasHourFilter && !HasBit(_hourMask, h))
                    continue;

                if (_frequency is Frequency.Hourly)
                {
                    if (h * SecondsPerHour % divisor == residue)
                        return true;

                    continue;
                }

                for (var m = 0; m < 60; m++)
                {
                    if (_hasMinuteFilter && !HasBit(_minuteMask, m))
                        continue;

                    if (_frequency is Frequency.Minutely)
                    {
                        if (((h * SecondsPerHour) + (m * 60)) % divisor == residue)
                            return true;

                        continue;
                    }

                    for (var s = 0; s < 60; s++)
                    {
                        if (_hasSecondFilter && !HasBit(_secondMask, s))
                            continue;

                        if (((h * SecondsPerHour) + (m * 60) + s) % divisor == residue)
                            return true;
                    }
                }
            }

            return false;
        }

        return true;
    }

    /// <summary>Gets a value indicating whether the day parts select at least one day, which BYMONTH=2;BYMONTHDAY=30 never does.</summary>
    /// <remarks>The Gregorian calendar repeats every 400 years, week days included, so a day that exists at all exists within 400 consecutive years. A satisfiable rule usually finds one in its first month.</remarks>
    private bool HasAnyIncludedDay()
    {
        var lastYear = Math.Min(_startYear + 399L, MaxYear);
        for (long year = _startYear; year <= lastYear; year++)
        {
            var yearDayNumber = DaysBeforeYear(year);
            var daysToMonth = IsLeapYear(year) ? DaysToMonth366 : DaysToMonth365;
            for (var month = 1; month <= 12; month++)
            {
                if (_hasMonthFilter && !HasBit(_monthMask, month))
                    continue;

                for (var day = 1; day <= daysToMonth[month] - daysToMonth[month - 1]; day++)
                {
                    var dayOfYear = daysToMonth[month - 1] + day;
                    if (IsDayIncluded(year, month, day, dayOfYear, yearDayNumber + dayOfYear - 1))
                        return true;
                }
            }
        }

        return false;
    }

    /// <summary>Moves to the next period holding at least one day, and returns its number of days, or 0 when there is none before <paramref name="scanLimit"/>.</summary>
    private int MoveToNextDayPeriod(ref long periodIndex, int[] days, long scanLimit)
    {
        var startDayNumber = _startDayNumber;
        var startOfFirstWeek = startDayNumber - Modulo(GetDayOfWeek(startDayNumber) - _weekStart, 7);
        while (true)
        {
            if (periodIndex >= scanLimit)
                return 0;

            var dayCount = 0;
            switch (_frequency)
            {
                case Frequency.Yearly:
                    {
                        var year = _startYear + (periodIndex * _interval);
                        if (year > MaxYear)
                            return 0;

                        var yearDayNumber = DaysBeforeYear(year);
                        if (IsAfterEnd(yearDayNumber * TicksPerDay))
                            return 0;

                        periodIndex++;
                        var daysToMonth = IsLeapYear(year) ? DaysToMonth366 : DaysToMonth365;
                        for (var month = 1; month <= 12; month++)
                        {
                            if (_hasMonthFilter && !HasBit(_monthMask, month))
                                continue;

                            var daysInMonth = daysToMonth[month] - daysToMonth[month - 1];
                            for (var day = 1; day <= daysInMonth; day++)
                            {
                                var dayOfYear = daysToMonth[month - 1] + day;
                                var dayNumber = yearDayNumber + dayOfYear - 1;
                                if (IsDayIncluded(year, month, day, dayOfYear, dayNumber))
                                {
                                    days[dayCount++] = (int)dayNumber;
                                }
                            }
                        }

                        break;
                    }

                case Frequency.Monthly:
                    {
                        var monthIndex = (_startYear * 12L) + _startMonth - 1 + (periodIndex * _interval);
                        var year = monthIndex / 12;
                        var month = (int)(monthIndex % 12) + 1;
                        if (year > MaxYear)
                            return 0;

                        var daysToMonth = IsLeapYear(year) ? DaysToMonth366 : DaysToMonth365;
                        var firstDayNumber = DaysBeforeYear(year) + daysToMonth[month - 1];
                        if (IsAfterEnd(firstDayNumber * TicksPerDay))
                            return 0;

                        periodIndex++;
                        if (_hasMonthFilter && !HasBit(_monthMask, month))
                            continue;

                        var daysInMonth = daysToMonth[month] - daysToMonth[month - 1];
                        for (var day = 1; day <= daysInMonth; day++)
                        {
                            var dayOfYear = daysToMonth[month - 1] + day;
                            var dayNumber = firstDayNumber + day - 1;
                            if (IsDayIncluded(year, month, day, dayOfYear, dayNumber))
                            {
                                days[dayCount++] = (int)dayNumber;
                            }
                        }

                        break;
                    }

                case Frequency.Weekly:
                    {
                        var weekDayNumber = startOfFirstWeek + (periodIndex * 7 * _interval);
                        if (weekDayNumber > MaxDayNumber || IsAfterEnd(weekDayNumber * TicksPerDay))
                            return 0;

                        periodIndex++;
                        for (var i = 0; i < 7; i++)
                        {
                            var dayNumber = weekDayNumber + i;
                            if (dayNumber < 0)
                                continue;

                            if (dayNumber > MaxDayNumber)
                                break;

                            GetDate(dayNumber, out var year, out var month, out var day, out var dayOfYear);
                            if (IsDayIncluded(year, month, day, dayOfYear, dayNumber))
                            {
                                days[dayCount++] = (int)dayNumber;
                            }
                        }

                        break;
                    }

                default:
                    {
                        var dayNumber = startDayNumber + (periodIndex * _interval);
                        if (dayNumber > MaxDayNumber || IsAfterEnd(dayNumber * TicksPerDay))
                            return 0;

                        GetDate(dayNumber, out var year, out var month, out var day, out var dayOfYear);
                        if (_hasMonthFilter && !HasBit(_monthMask, month))
                        {
                            var firstDayOfNextMonth = dayNumber - day + DaysInMonth(year, month) + 1;
                            periodIndex = Advance(periodIndex, firstDayOfNextMonth - startDayNumber, _interval);
                            continue;
                        }

                        periodIndex++;
                        if (IsDayIncluded(year, month, day, dayOfYear, dayNumber))
                        {
                            days[dayCount++] = (int)dayNumber;
                        }

                        break;
                    }
            }

            if (dayCount > 0)
                return dayCount;
        }
    }

    /// <summary>Moves to the next period matching the day and time parts, or returns 0 when there is none before <paramref name="scanLimit"/>.</summary>
    private int MoveToNextTimePeriod(ref long periodIndex, int[] days, long scanLimit, out int hour, out int minute, out int second)
    {
        var step = GetStepInSeconds();
        var firstPeriod = GetFirstTimePeriod();
        while (true)
        {
            var period = firstPeriod + (periodIndex * step);
            if (periodIndex >= scanLimit || period > MaxSecondNumber || IsAfterEnd(period * TicksPerSecond))
            {
                hour = minute = second = 0;
                return 0;
            }

            var dayNumber = period / SecondsPerDay;
            var dayStart = dayNumber * SecondsPerDay;
            var secondOfDay = (int)(period - dayStart);
            GetDate(dayNumber, out var year, out var month, out var day, out var dayOfYear);

            if (_hasMonthFilter && !HasBit(_monthMask, month))
            {
                var firstDayOfNextMonth = dayNumber - day + DaysInMonth(year, month) + 1;
                periodIndex = Advance(periodIndex, (firstDayOfNextMonth * SecondsPerDay) - firstPeriod, step);
                continue;
            }

            if (!IsDayIncluded(year, month, day, dayOfYear, dayNumber))
            {
                periodIndex = Advance(periodIndex, dayStart + SecondsPerDay - firstPeriod, step);
                continue;
            }

            hour = secondOfDay / SecondsPerHour;
            minute = secondOfDay / 60 % 60;
            second = secondOfDay % 60;

            if (_hasHourFilter && !HasBit(_hourMask, hour))
            {
                var nextHour = GetNextBit(_hourMask, hour + 1, 23);
                var target = nextHour < 0 ? dayStart + SecondsPerDay : dayStart + (nextHour * SecondsPerHour);
                periodIndex = Advance(periodIndex, target - firstPeriod, step);
                continue;
            }

            if (_frequency <= Frequency.Minutely && _hasMinuteFilter && !HasBit(_minuteMask, minute))
            {
                var hourStart = dayStart + (hour * SecondsPerHour);
                var nextMinute = GetNextBit(_minuteMask, minute + 1, 59);
                var target = nextMinute < 0 ? hourStart + SecondsPerHour : hourStart + (nextMinute * 60);
                periodIndex = Advance(periodIndex, target - firstPeriod, step);
                continue;
            }

            if (_frequency is Frequency.Secondly && _hasSecondFilter && !HasBit(_secondMask, second))
            {
                var minuteStart = dayStart + (hour * SecondsPerHour) + (minute * 60);
                var nextSecond = GetNextBit(_secondMask, second + 1, 59);
                var target = nextSecond < 0 ? minuteStart + 60 : minuteStart + nextSecond;
                periodIndex = Advance(periodIndex, target - firstPeriod, step);
                continue;
            }

            periodIndex++;
            days[0] = (int)dayNumber;
            return 1;
        }
    }

    private long GetStepInSeconds()
    {
        return _frequency switch
        {
            Frequency.Hourly => _interval * SecondsPerHour,
            Frequency.Minutely => _interval * 60,
            _ => _interval,
        };
    }

    /// <summary>Gets the start of the first sub-daily period, in seconds since 0001-01-01.</summary>
    private long GetFirstTimePeriod()
    {
        var startSecond = _startTicks / TicksPerSecond;
        return _frequency switch
        {
            Frequency.Hourly => startSecond - (startSecond % SecondsPerHour),
            Frequency.Minutely => startSecond - (startSecond % 60),
            _ => startSecond,
        };
    }

    private bool IsAfterEnd(long ticks) => ticks > _endTicks;

    /// <summary>Gets the index of the first period at or after <paramref name="offset"/>, and always moves forward.</summary>
    private static long Advance(long periodIndex, long offset, long step)
    {
        var index = (offset + step - 1) / step;
        return Math.Max(periodIndex + 1, index);
    }

    private bool IsDayIncluded(long year, int month, int day, int dayOfYear, long dayNumber)
    {
        if (_hasMonthFilter && !HasBit(_monthMask, month))
            return false;

        if (_hasMonthDayFilter)
        {
            var daysInMonth = DaysInMonth(year, month);
            if (!HasBit(_positiveMonthDays, day) && !HasBit(_negativeMonthDays, daysInMonth - day + 1))
                return false;
        }

        if (_hasYearDayFilter)
        {
            var daysInYear = IsLeapYear(year) ? 366 : 365;
            if (!HasBit(_positiveYearDays, dayOfYear) && !HasBit(_negativeYearDays, daysInYear - dayOfYear + 1))
                return false;
        }

        if (_hasWeekNumberFilter && !IsWeekNumberIncluded(year, dayNumber))
            return false;

        if (_hasDayFilter)
        {
            var dayOfWeek = GetDayOfWeek(dayNumber);
            if (!HasBit(_weekDayMask, dayOfWeek) && !IsOrdinalWeekDayIncluded(year, month, day, dayOfYear, dayOfWeek))
                return false;
        }

        return true;
    }

    private bool IsOrdinalWeekDayIncluded(long year, int month, int day, int dayOfYear, int dayOfWeek)
    {
        foreach (var (ordinalDayOfWeek, ordinal) in _ordinalWeekDays)
        {
            if (ordinalDayOfWeek != dayOfWeek)
                continue;

            int position;
            int length;
            if (_ordinalWeekDaysSpanTheYear)
            {
                position = dayOfYear;
                length = IsLeapYear(year) ? 366 : 365;
            }
            else
            {
                position = day;
                length = DaysInMonth(year, month);
            }

            // The same week day comes back every 7 days, so its rank is derived from the distance to the bounds of the period
            var rank = ordinal > 0 ? ((position - 1) / 7) + 1 : -(((length - position) / 7) + 1);
            if (rank == ordinal)
                return true;
        }

        return false;
    }

    /// <summary>Week numbers follow RFC 5545: week 1 is the first week, starting on WKST, that contains at least 4 days of the year.</summary>
    private bool IsWeekNumberIncluded(long year, long dayNumber)
    {
        long weekYearStart;
        long nextWeekYearStart;

        var start = GetStartOfFirstWeek(year);
        if (dayNumber < start)
        {
            weekYearStart = GetStartOfFirstWeek(year - 1);
            nextWeekYearStart = start;
        }
        else
        {
            var next = GetStartOfFirstWeek(year + 1);
            if (dayNumber >= next)
            {
                weekYearStart = next;
                nextWeekYearStart = GetStartOfFirstWeek(year + 2);
            }
            else
            {
                weekYearStart = start;
                nextWeekYearStart = next;
            }
        }

        var weekNumber = (int)((dayNumber - weekYearStart) / 7) + 1;
        var numberOfWeeks = (int)((nextWeekYearStart - weekYearStart) / 7);
        return HasBit(_positiveWeekNumbers, weekNumber) || HasBit(_negativeWeekNumbers, numberOfWeeks - weekNumber + 1);
    }

    private long GetStartOfFirstWeek(long year)
    {
        var firstDayOfYear = DaysBeforeYear(year);
        var offset = Modulo(GetDayOfWeek(firstDayOfYear) - _weekStart, 7);
        return offset <= 3 ? firstDayOfYear - offset : firstDayOfYear + 7 - offset;
    }

    private static int SelectSetPositions(int[] setPositions, long count, long[] indexes)
    {
        var indexCount = 0;
        foreach (var position in setPositions)
        {
            var index = position > 0 ? position - 1L : count + position;
            if (index >= 0 && index < count)
            {
                indexes[indexCount++] = index;
            }
        }

        Array.Sort(indexes, 0, indexCount);

        var distinctCount = 0;
        for (var i = 0; i < indexCount; i++)
        {
            if (distinctCount is 0 || indexes[distinctCount - 1] != indexes[i])
            {
                indexes[distinctCount++] = indexes[i];
            }
        }

        return distinctCount;
    }

    private static bool IsEmpty<T>([NotNullWhen(false)] IList<T>? list) => list is null || list.Count is 0;

    private static ulong CreateMask(IList<int>? values, int max, bool normalizeLeapSecond)
    {
        ulong mask = 0;
        if (values is not null)
        {
            foreach (var value in values)
            {
                var normalized = normalizeLeapSecond && value is 60 ? 59 : value;
                if (normalized >= 0 && normalized <= max)
                {
                    mask |= 1UL << normalized;
                }
            }
        }

        return mask;
    }

    private static int[] CreateList(ulong mask, bool hasFilter, int defaultValue, int max)
    {
        if (!hasFilter)
            return [defaultValue];

        var result = new List<int>();
        for (var i = 0; i <= max; i++)
        {
            if (HasBit(mask, i))
            {
                result.Add(i);
            }
        }

        return [.. result];
    }

    private static bool HasBit(ulong mask, int bit) => bit is >= 0 and < 64 && ((mask >> bit) & 1) is not 0;

    private static bool HasBit(int mask, int bit) => bit is >= 0 and < 32 && ((mask >> bit) & 1) is not 0;

    private static bool HasBit(ulong[] mask, int bit) => bit >= 0 && (bit >> 6) < mask.Length && ((mask[bit >> 6] >> (bit & 63)) & 1) is not 0;

    private static void SetBit(ulong[] mask, int bit) => mask[bit >> 6] |= 1UL << (bit & 63);

    private static int GetNextBit(ulong mask, int start, int max)
    {
        for (var i = start; i <= max; i++)
        {
            if (HasBit(mask, i))
                return i;
        }

        return -1;
    }

    private static int CountBits(ulong value)
    {
        var count = 0;
        while (value is not 0)
        {
            value &= value - 1;
            count++;
        }

        return count;
    }

    private static long GreatestCommonDivisor(long a, long b)
    {
        while (b is not 0)
        {
            (a, b) = (b, a % b);
        }

        return a;
    }

    private static int Modulo(long value, int divisor)
    {
        var result = (int)(value % divisor);
        return result < 0 ? result + divisor : result;
    }

    /// <summary>0001-01-01 (day number 0) is a Monday.</summary>
    private static int GetDayOfWeek(long dayNumber) => Modulo(dayNumber + 1, 7);

    private static bool IsLeapYear(long year) => year % 4 == 0 && (year % 100 != 0 || year % 400 == 0);

    private static int DaysInMonth(long year, int month)
    {
        var daysToMonth = IsLeapYear(year) ? DaysToMonth366 : DaysToMonth365;
        return daysToMonth[month] - daysToMonth[month - 1];
    }

    /// <summary>Gets the day number of January 1st of <paramref name="year"/> in the proleptic Gregorian calendar.</summary>
    private static long DaysBeforeYear(long year)
    {
        // Year 0 is only needed to number the weeks of year 1; it is a leap year
        if (year <= 0)
            return -366;

        var previousYear = year - 1;
        return (previousYear * 365) + (previousYear / 4) - (previousYear / 100) + (previousYear / 400);
    }

    private static void GetDate(long dayNumber, out long year, out int month, out int day, out int dayOfYear)
    {
        var n = dayNumber;
        var y400 = n / 146097;
        n -= y400 * 146097;
        var y100 = n / 36524;
        if (y100 is 4)
        {
            y100 = 3;
        }

        n -= y100 * 36524;
        var y4 = n / 1461;
        n -= y4 * 1461;
        var y1 = n / 365;
        if (y1 is 4)
        {
            y1 = 3;
        }

        year = (y400 * 400) + (y100 * 100) + (y4 * 4) + y1 + 1;
        n -= y1 * 365;

        var daysToMonth = y1 is 3 && (y4 is not 24 || y100 is 3) ? DaysToMonth366 : DaysToMonth365;
        var m = (int)(n >> 5) + 1;
        while (n >= daysToMonth[m])
        {
            m++;
        }

        month = m;
        day = (int)(n - daysToMonth[m - 1]) + 1;
        dayOfYear = (int)n + 1;
    }
}
