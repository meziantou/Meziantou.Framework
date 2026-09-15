namespace Meziantou.Framework.Scheduling;

internal sealed class RecurrenceRuleHumanizerFrench : RecurrenceRuleHumanizer
{
    private const string FromEndSuffix = " en partant de la fin";

    protected override string GetText(RuleParts rule)
    {
        ArgumentNullException.ThrowIfNull(rule);

        // COUNT=0 produces no occurrence
        if (rule.Occurrences is 0)
            return "jamais";

        var sb = new StringBuilder();
        AppendInterval(sb, rule);
        var daySetPositions = GetDaySetPositions(rule);
        AppendDays(sb, rule, daySetPositions?.DayPositions);
        if (daySetPositions is not null)
        {
            AppendTimes(sb, daySetPositions.Hours, daySetPositions.Minutes, daySetPositions.Seconds);
        }
        else
        {
            AppendTimes(sb, rule.ByHours, rule.ByMinutes, rule.BySeconds);
            if (rule.BySetPositions.Count > 0)
            {
                // BYSETPOS selects among the instances of every period of the frequency, not among all the occurrences
                sb.Append(", uniquement ");
                sb.Append(GetOrdinalNounList(rule.BySetPositions, useWords: true, feminine: true, "occurrence", " et "));
                sb.Append(rule.Frequency switch
                {
                    Frequency.Secondly => " de chaque seconde",
                    Frequency.Minutely => " de chaque minute",
                    Frequency.Hourly => " de chaque heure",
                    Frequency.Daily => " de chaque jour",
                    Frequency.Weekly => " de chaque semaine",
                    Frequency.Monthly => " de chaque mois",
                    _ => " de chaque année",
                });

                if (rule.Occurrences is not null || rule.EndDate is not null)
                {
                    sb.Append(',');
                }
            }
        }

        AppendEnd(sb, rule);
        if (IsWeekStartSignificant(rule))
        {
            sb.Append(", les semaines commençant le ");
            sb.Append(DayOfWeekToString(rule.WeekStart));
        }

        return sb.ToString();
    }

    private static void AppendInterval(StringBuilder sb, RuleParts rule)
    {
        var (prefix, unit) = rule.Frequency switch
        {
            Frequency.Secondly => ("toutes les", "secondes"),
            Frequency.Minutely => ("toutes les", "minutes"),
            Frequency.Hourly => ("toutes les", "heures"),
            Frequency.Daily => ("tous les", "jours"),
            Frequency.Weekly => ("toutes les", "semaines"),
            Frequency.Monthly => ("tous les", "mois"),
            _ => ("tous les", "ans"),
        };

        sb.Append(prefix);
        if (rule.Interval is not 1)
        {
            sb.Append(' ');
            sb.Append(ToInvariantString(rule.Interval));
        }

        sb.Append(' ');
        sb.Append(unit);
    }

    private static void AppendDays(StringBuilder sb, RuleParts rule, IList<int>? daySetPositions)
    {
        var days = rule.ByDays;
        var hasOrdinalDays = rule.HasOrdinalDays;
        var isYearly = rule.Frequency is Frequency.Yearly;
        var monthsRendered = false;
        var positional = false;

        if (rule.ByMonthDays.Count is 0 && rule.ByYearDays.Count is 0)
        {
            if (daySetPositions is not null)
            {
                // "le premier et le dernier lundi ou mardi": BYSETPOS selects among the days matching any of the listed weekdays
                sb.Append(' ');
                sb.Append(GetOrdinalNounList(daySetPositions, useWords: true, feminine: false, GetSetPositionDayNames(rule.DaysOfWeek), " et "));
                positional = true;
            }
            else if (rule.ByWeekNumbers.Count > 0)
            {
                // "le lundi de la semaine 20": the week numbers select the weeks, and the week days the days of those weeks
                var daysOfWeek = rule.DaysOfWeek;
                if (days.Count is 0 || hasOrdinalDays || IsFullWeek(daysOfWeek))
                {
                    sb.Append(" chaque jour");
                }
                else if (IsWeekday(daysOfWeek))
                {
                    sb.Append(" du lundi au vendredi");
                }
                else if (IsWeekendDay(daysOfWeek))
                {
                    sb.Append(" le weekend");
                }
                else
                {
                    sb.Append(" le ");
                    sb.Append(JoinEt([.. daysOfWeek.Select(DayOfWeekToString)]));
                }

                sb.Append(' ');
                sb.Append(GetWeekNumbersText(rule.ByWeekNumbers, withDe: true, isCondition: false));
                if (hasOrdinalDays)
                {
                    sb.Append(" si c'est ");
                    sb.Append(GetDayConditionText(rule));
                }
            }
            else if (days.Count > 0)
            {
                if (hasOrdinalDays)
                {
                    sb.Append(' ');
                    sb.Append(JoinEt([.. days.Select(day => day.Ordinal is { } ordinal ? GetOrdinalNounList([ordinal], useWords: true, feminine: false, DayOfWeekToString(day.DayOfWeek), " et ") : "tous les " + DayOfWeekToString(day.DayOfWeek) + "s")]));
                    positional = true;
                }
                else
                {
                    var daysOfWeek = rule.DaysOfWeek;
                    if (rule.Frequency is Frequency.Monthly or Frequency.Yearly && IsWeekday(daysOfWeek))
                    {
                        sb.Append(" en semaine");
                    }
                    else if (rule.Frequency is Frequency.Monthly or Frequency.Yearly && IsWeekendDay(daysOfWeek))
                    {
                        sb.Append(" le weekend");
                    }
                    else
                    {
                        sb.Append(" le ");
                        sb.Append(JoinEt([.. daysOfWeek.Select(DayOfWeekToString)]));
                    }
                }
            }
        }
        else
        {
            var monthDaysAllPositive = rule.ByMonthDays.All(day => day > 0);

            // "le vendredi 13"
            var isWeekdayOfMonthDay = days.Count is 1 && !hasOrdinalDays && rule.ByMonthDays.Count > 0 && monthDaysAllPositive && rule.ByYearDays.Count is 0;

            // Every day part restricts the days selected by the other ones, so the first one selects the days and the other ones are conditions
            var conditions = new List<string>();
            sb.Append(' ');
            if (rule.ByYearDays.Count > 0)
            {
                sb.Append(GetOrdinalNounList(rule.ByYearDays, useWords: false, feminine: false, "jour", " et ")).Append(" de l'année");
                if (rule.ByMonthDays.Count > 0)
                {
                    conditions.Add(GetOrdinalNounList(rule.ByMonthDays, useWords: false, feminine: false, "jour", " ou ") + " du mois");
                }
            }
            else if (isWeekdayOfMonthDay)
            {
                sb.Append(JoinEt([.. rule.ByMonthDays.Select(day => "le " + DayOfWeekToString(days[0].DayOfWeek) + " " + GetDateDayText(day))]));
            }
            else if (isYearly && rule.ByMonths.Count is 1 && monthDaysAllPositive)
            {
                sb.Append(JoinEt([.. rule.ByMonthDays.Select(day => "le " + GetDateDayText(day))])).Append(' ').Append(MonthToString(rule.ByMonths[0]));
                monthsRendered = true;
            }
            else
            {
                sb.Append(GetOrdinalNounList(rule.ByMonthDays, useWords: false, feminine: false, "jour", " et "));
                if (isYearly)
                {
                    if (rule.ByMonths.Count > 0)
                    {
                        sb.Append(' ').Append(GetMonthsTextWithDe(rule.ByMonths));
                        monthsRendered = true;
                    }
                    else
                    {
                        sb.Append(" de chaque mois");
                    }
                }
            }

            if (rule.ByWeekNumbers.Count > 0)
            {
                conditions.Add("dans " + GetWeekNumbersText(rule.ByWeekNumbers, withDe: false, isCondition: true));
            }

            if (days.Count > 0 && !isWeekdayOfMonthDay)
            {
                conditions.Add(GetDayConditionText(rule));
            }

            if (conditions.Count > 0)
            {
                sb.Append(" si c'est ");
                sb.Append(JoinEt(conditions));
            }
        }

        if (!monthsRendered && rule.ByMonths.Count > 0)
        {
            if (positional && isYearly)
            {
                sb.Append(' ');
                sb.Append(GetMonthsTextWithDe(rule.ByMonths));
            }
            else
            {
                sb.Append(" en ");
                sb.Append(JoinEt([.. rule.ByMonths.Select(MonthToString)]));
            }
        }
    }

    private static void AppendTimes(StringBuilder sb, IList<int> hours, IList<int> minutes, IList<int> seconds)
    {
        var times = GetListedTimes(hours, minutes, seconds);
        if (times is not null)
        {
            sb.Append(" à ");
            sb.Append(JoinEt([.. times.Select(time => FormatTime(time.Hour, time.Minute, time.Second))]));
            return;
        }

        var parts = new List<string>();
        AddTimeComponent(parts, hours, "à l'heure ", "aux heures ");
        AddTimeComponent(parts, minutes, "à la minute ", "aux minutes ");
        AddTimeComponent(parts, seconds, "à la seconde ", "aux secondes ");
        if (parts.Count > 0)
        {
            sb.Append(' ');
            sb.Append(string.Join(", ", parts));
        }

        static void AddTimeComponent(List<string> parts, IList<int> values, string singular, string plural)
        {
            if (values.Count is 0)
                return;

            parts.Add((values.Count is 1 ? singular : plural) + JoinEt([.. values.Select(ToInvariantString)]));
        }
    }

    private static void AppendEnd(StringBuilder sb, RuleParts rule)
    {
        if (rule.Occurrences is { } occurrences)
        {
            sb.Append(" pour ");
            sb.Append(ToInvariantString(occurrences));
            sb.Append(" fois");
        }

        if (rule.EndDate is { } endDate)
        {
            sb.Append(" jusqu'au ");
            sb.Append(GetDateDayText(endDate.Day));
            sb.Append(' ');
            sb.Append(MonthToString(endDate.Month));
            sb.Append(' ');
            sb.Append(ToInvariantString(endDate.Year));

            // A DATE-TIME ends the recurrence at its time of day, midnight included, so only a DATE omits it
            if (!rule.IsEndDateDate)
            {
                sb.Append(" à ");
                sb.Append(FormatTime(endDate.Hour, endDate.Minute, endDate.Second is 0 ? null : endDate.Second));
                if (endDate.Kind is DateTimeKind.Utc)
                {
                    sb.Append(" UTC");
                }
            }
        }
    }

    private static string JoinEt(IList<string> items) => JoinList(items, ", ", " et ");

    private static string FormatTime(int hour, int minute, int? second)
    {
        var result = ToInvariantString(hour) + "h" + ToTwoDigitString(minute);
        if (second is { } s)
        {
            result += "m" + ToTwoDigitString(s) + "s";
        }

        return result;
    }

    private static string GetSetPositionDayNames(List<DayOfWeek> daysOfWeek)
    {
        if (IsWeekday(daysOfWeek))
            return "jour de semaine";

        if (IsWeekendDay(daysOfWeek))
            return "jour de weekend";

        if (IsFullWeek(daysOfWeek))
            return "jour";

        return JoinList([.. daysOfWeek.Select(DayOfWeekToString)], ", ", " ou ");
    }

    private static string GetDayConditionText(RuleParts rule)
    {
        if (!rule.HasOrdinalDays)
        {
            var daysOfWeek = rule.DaysOfWeek;
            if (IsWeekday(daysOfWeek))
                return "un jour de semaine";

            if (IsWeekendDay(daysOfWeek))
                return "un jour de weekend";
        }

        // Within a YEARLY rule, a numbered day is counted in the year unless BYMONTH narrows it to the month
        var scope = rule.Frequency is Frequency.Yearly && rule.ByMonths.Count is 0 ? " de l'année" : "";
        return JoinList([.. rule.ByDays.Select(day => day.Ordinal is { } ordinal ? GetOrdinalNounList([ordinal], useWords: true, feminine: false, DayOfWeekToString(day.DayOfWeek), " et ") + scope : "un " + DayOfWeekToString(day.DayOfWeek))], ", ", " ou ");
    }

    private static string GetMonthsTextWithDe(IList<int> months)
    {
        return JoinEt([.. months.Select(month =>
        {
            var name = MonthToString(month);
            return (StartsWithVowel(name) ? "d'" : "de ") + name;
        })]);
    }

    /// <summary>Gets the text of week numbers such as "la semaine 20", "des semaines 1 et 2", or "la semaine 1 et la dernière semaine de l'année".</summary>
    /// <param name="weekNumbers">The week numbers.</param>
    /// <param name="withDe">Whether the text is preceded by "de".</param>
    /// <param name="isCondition"><see langword="true"/> when a day must be in one of the weeks, which reads "la semaine 1 ou 2".</param>
    private static string GetWeekNumbersText(IList<int> weekNumbers, bool withDe, bool isCondition)
    {
        var lastSeparator = isCondition ? " ou " : " et ";
        if (weekNumbers.All(week => week > 0))
        {
            var numbers = JoinList([.. weekNumbers.Select(ToInvariantString)], ", ", lastSeparator);
            if (weekNumbers.Count is 1 || isCondition)
                return (withDe ? "de la semaine " : "la semaine ") + numbers;

            return (withDe ? "des semaines " : "les semaines ") + numbers;
        }

        return JoinList([.. weekNumbers.Select(week =>
        {
            string item;
            if (week > 0)
            {
                item = "la semaine " + ToInvariantString(week);
            }
            else
            {
                var (text, fromEnd) = GetOrdinal(week, useWords: true, feminine: true);
                item = GetArticle(text, feminine: true) + text + " semaine" + (fromEnd ? FromEndSuffix : "");
            }

            return withDe ? "de " + item : item;
        })], ", ", lastSeparator) + " de l'année";
    }

    /// <summary>Gets a list such as "le premier et le dernier lundi", "l'avant-dernier jour", or "le 3e jour en partant de la fin".</summary>
    private static string GetOrdinalNounList(IList<int> values, bool useWords, bool feminine, string noun, string lastSeparator)
    {
        var items = values.Select(value => GetOrdinal(value, useWords, feminine)).ToList();
        if (items.TrueForAll(item => !item.FromEnd))
            return JoinList([.. items.Select(item => GetArticle(item.Text, feminine) + item.Text)], ", ", lastSeparator) + " " + noun;

        return JoinList([.. items.Select(item => GetArticle(item.Text, feminine) + item.Text + " " + noun + (item.FromEnd ? FromEndSuffix : ""))], ", ", lastSeparator);
    }

    private static (string Text, bool FromEnd) GetOrdinal(int value, bool useWords, bool feminine)
    {
        return value switch
        {
            1 when useWords => (feminine ? "première" : "premier", false),
            2 when useWords => ("deuxième", false),
            3 when useWords => ("troisième", false),
            4 when useWords => ("quatrième", false),
            1 => (feminine ? "1re" : "1er", false),
            -1 => (feminine ? "dernière" : "dernier", false),
            -2 => (feminine ? "avant-dernière" : "avant-dernier", false),
            int.MinValue => (ToInvariantString(value), false),
            < 0 => (GetOrdinal(-value, useWords, feminine).Text, true),
            _ => (ToInvariantString(value) + "e", false),
        };
    }

    private static string GetArticle(string word, bool feminine)
    {
        if (StartsWithVowel(word))
            return "l'";

        return feminine ? "la " : "le ";
    }

    private static bool StartsWithVowel(string str)
    {
        return str.Length > 0 && str[0] is 'a' or 'â' or 'e' or 'é' or 'è' or 'ê' or 'i' or 'î' or 'o' or 'ô' or 'u' or 'û' or 'y';
    }

    /// <summary>Gets the day number as written in a French date ("1er", "2", "31").</summary>
    private static string GetDateDayText(int day)
    {
        return day is 1 ? "1er" : ToInvariantString(day);
    }

    private static string DayOfWeekToString(DayOfWeek dayOfWeek)
    {
        return dayOfWeek switch
        {
            DayOfWeek.Sunday => "dimanche",
            DayOfWeek.Monday => "lundi",
            DayOfWeek.Tuesday => "mardi",
            DayOfWeek.Wednesday => "mercredi",
            DayOfWeek.Thursday => "jeudi",
            DayOfWeek.Friday => "vendredi",
            DayOfWeek.Saturday => "samedi",
            _ => ToInvariantString((int)dayOfWeek),
        };
    }

    private static string MonthToString(int month)
    {
        return month switch
        {
            1 => "janvier",
            2 => "février",
            3 => "mars",
            4 => "avril",
            5 => "mai",
            6 => "juin",
            7 => "juillet",
            8 => "août",
            9 => "septembre",
            10 => "octobre",
            11 => "novembre",
            12 => "décembre",
            _ => ToInvariantString(month),
        };
    }
}
