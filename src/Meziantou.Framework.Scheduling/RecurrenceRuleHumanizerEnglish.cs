using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace Meziantou.Framework.Scheduling
{
    public class RecurrenceRuleHumanizerEnglish : RecurrenceRuleHumanizer
    {
        [Flags]
        private enum WeekdayHumanTextOptions
        {
            None = 0,
            AbbrDays = 1,
            AbbrWeekdays = 2,
            AbbrWeekendDays = 4,
            Plural = 8
        }

        private static string GetWeekdayHumanText(IList<ByDay> daysOfWeek, WeekdayHumanTextOptions options)
        {
            if (daysOfWeek.Count == 0)
                return null;

            return GetWeekdayHumanText(daysOfWeek.Where(dow => !dow.Ordinal.HasValue).Select(dow => dow.DayOfWeek).ToList(), ", ", " and ", options);
        }

        private static void GetEndHumanText(RecurrenceRule rrule, StringBuilder sb)
        {
            if (rrule.Occurrences.HasValue)
            {
                sb.Append(" for ");
                sb.Append(rrule.Occurrences.Value);
                if (rrule.Occurrences.Value <= 1)
                {
                    sb.Append(" time");
                }
                else
                {
                    sb.Append(" times");
                }
            }

            if (rrule.EndDate.HasValue)
            {
                sb.Append(" until ");
                sb.Append(rrule.EndDate.Value.ToString("MMMM d, yyyy", EnglishCultureInfo));
            }
        }

        private static string GetWeekdayHumanText(IList<DayOfWeek> daysOfWeek, string separator = ", ", string lastSeparator = " and ", WeekdayHumanTextOptions options = WeekdayHumanTextOptions.None)
        {
            if (options.HasFlag(WeekdayHumanTextOptions.AbbrWeekdays) && IsWeekday(daysOfWeek))
            {
                if (options.HasFlag(WeekdayHumanTextOptions.Plural))
                    return "weekdays";
                return "weekday";
            }

            if (options.HasFlag(WeekdayHumanTextOptions.AbbrDays) && IsFullWeek(daysOfWeek))
            {
                if (options.HasFlag(WeekdayHumanTextOptions.Plural))
                    return "days";
                return "day";
            }

            if (options.HasFlag(WeekdayHumanTextOptions.AbbrWeekendDays) && IsWeekendDay(daysOfWeek))
            {
                if (options.HasFlag(WeekdayHumanTextOptions.Plural))
                    return "weekend days";
                return "weekend day";
            }

            return ListToHumanText(EnglishCultureInfo, daysOfWeek, separator, lastSeparator);
        }

        private static string GetByMonthdayHumanText(int monthday)
        {
            if (monthday > 0)
            {
                return Extensions.ToEnglishOrdinal(monthday);
            }

            if (monthday == -1)
            {
                return "last day";
            }

            return null;
        }

        private static string GetBySetPosHumanText(int setPosition)
        {
            switch (setPosition)
            {
                case -1:
                    return "last";
                case 1:
                    return "first";
                case 2:
                    return "second";
                case 3:
                    return "third";
                case 4:
                    return "fourth";
            }

            return Extensions.ToEnglishOrdinal(setPosition);
        }

        protected override string GetText(DailyRecurrenceRule rrule, CultureInfo cultureInfo)
        {
            if (rrule == null) throw new ArgumentNullException(nameof(rrule));
            if (cultureInfo == null) throw new ArgumentNullException(nameof(cultureInfo));

            var sb = new StringBuilder();
            sb.Append("every");
            if (rrule.Interval == 1)
            {
                sb.Append(" day");
            }
            else if (rrule.Interval == 2)
            {
                sb.Append(" other day");
            }
            else if (rrule.Interval > 2)
            {
                sb.Append(" ");
                sb.Append(rrule.Interval);
                sb.Append(" days");
            }

            GetEndHumanText(rrule, sb);
            return sb.ToString();
        }

        protected override string GetText(WeeklyRecurrenceRule rrule, CultureInfo cultureInfo)
        {
            if (rrule == null) throw new ArgumentNullException(nameof(rrule));
            if (cultureInfo == null) throw new ArgumentNullException(nameof(cultureInfo));
            var sb = new StringBuilder();
            sb.Append("every");
            if (rrule.Interval == 1)
            {
                sb.Append(" week");
            }
            else if (rrule.Interval == 2)
            {
                sb.Append(" other week");
            }
            else if (rrule.Interval > 2)
            {
                sb.Append(" ");
                sb.Append(rrule.Interval);
                sb.Append(" weeks");
            }

            if (rrule.ByWeekDays != null && rrule.ByWeekDays.Any())
            {
                sb.Append(" on ");
                sb.Append(GetWeekdayHumanText(rrule.ByWeekDays, options: WeekdayHumanTextOptions.None));
            }

            GetEndHumanText(rrule, sb);
            return sb.ToString();
        }

        protected override string GetText(MonthlyRecurrenceRule rrule, CultureInfo cultureInfo)
        {
            if (rrule == null) throw new ArgumentNullException(nameof(rrule));
            if (cultureInfo == null) throw new ArgumentNullException(nameof(cultureInfo));

            var sb = new StringBuilder();
            sb.Append("every");
            if (rrule.Interval == 1)
            {
                sb.Append(" month");
            }
            else if (rrule.Interval == 2)
            {
                sb.Append(" other month");
            }
            else if (rrule.Interval > 2)
            {
                sb.Append(" ");
                sb.Append(rrule.Interval);
                sb.Append(" months");
            }

            if (rrule.ByMonthDays != null && rrule.ByMonthDays.Any())
            {
                if (rrule.ByMonthDays.Any(day => day < 0))
                {
                    sb.Append(" on the ");
                }
                else
                {
                    sb.Append(" the ");
                }
                ListToHumanText(sb, EnglishCultureInfo, rrule.ByMonthDays.Select(GetByMonthdayHumanText).ToList(), ", ", " and ");
            }

            if (rrule.ByWeekDays != null && rrule.ByWeekDays.Any())
            {
                sb.Append(" on ");
                if (rrule.BySetPositions != null && rrule.BySetPositions.Any())
                {
                    sb.Append("the ");
                    sb.Append(GetBySetPosHumanText(rrule.BySetPositions[0]));
                    sb.Append(" ");
                }
                sb.Append(GetWeekdayHumanText(rrule.ByWeekDays, options: WeekdayHumanTextOptions.AbbrDays | WeekdayHumanTextOptions.AbbrWeekdays | WeekdayHumanTextOptions.AbbrWeekendDays));
            }

            GetEndHumanText(rrule, sb);
            return sb.ToString();
        }

        protected override string GetText(YearlyRecurrenceRule rrule, CultureInfo cultureInfo)
        {
            if (rrule == null) throw new ArgumentNullException(nameof(rrule));
            if (cultureInfo == null) throw new ArgumentNullException(nameof(cultureInfo));

            var sb = new StringBuilder();
            sb.Append("every");
            if (rrule.Interval == 1)
            {
                sb.Append(" year");
            }
            else if (rrule.Interval == 2)
            {
                sb.Append(" other year");
            }
            else if (rrule.Interval > 2)
            {
                sb.Append(" ");
                sb.Append(rrule.Interval);
                sb.Append(" years");
            }

            if (rrule.ByMonthDays != null && rrule.ByMonthDays.Any())
            {
                if (rrule.ByMonthDays.Any(day => day < 0))
                {
                    sb.Append(" on the ");
                    ListToHumanText(sb, EnglishCultureInfo, rrule.ByMonthDays.Select(GetByMonthdayHumanText).ToList(), ", ", " and ");

                    if (rrule.ByMonths != null && rrule.ByMonths.Any())
                    {
                        sb.Append(" of ");
                        sb.Append(rrule.ByMonths[0]);
                    }
                }
                else
                {
                    if (rrule.ByMonths != null && rrule.ByMonths.Any())
                    {
                        sb.Append(" on ");
                        sb.Append(rrule.ByMonths[0]);
                    }

                    sb.Append(" the ");
                    ListToHumanText(sb, EnglishCultureInfo, rrule.ByMonthDays.Select(GetByMonthdayHumanText).ToList(), ", ", " and ");
                }
            }

            if (rrule.ByWeekDays != null && rrule.ByWeekDays.Any())
            {
                sb.Append(" on ");
                if (rrule.BySetPositions != null && rrule.BySetPositions.Any())
                {
                    sb.Append("the ");
                    sb.Append(GetBySetPosHumanText(rrule.BySetPositions[0]));
                    sb.Append(" ");
                }
                sb.Append(GetWeekdayHumanText(rrule.ByWeekDays, options: WeekdayHumanTextOptions.AbbrDays | WeekdayHumanTextOptions.AbbrWeekdays | WeekdayHumanTextOptions.AbbrWeekendDays));

                if (rrule.ByMonths != null && rrule.ByMonths.Any())
                {
                    sb.Append(" of ");
                    sb.Append(rrule.ByMonths[0]);
                }
            }

            GetEndHumanText(rrule, sb);
            return sb.ToString();
        }
    }
}