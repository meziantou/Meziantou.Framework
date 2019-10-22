using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace Meziantou.Framework.Scheduling
{
    public sealed class RecurrenceRuleHumanizerFrench : RecurrenceRuleHumanizer
    {
        private static readonly char[] s_precededByApostropheChars = { 'a', 'e', 'i', 'o', 'u', 'y', 'h' };

        protected override string GetText(DailyRecurrenceRule rrule, CultureInfo cultureInfo)
        {
            if (rrule == null)
                throw new ArgumentNullException(nameof(rrule));
            if (cultureInfo == null)
                throw new ArgumentNullException(nameof(cultureInfo));

            var sb = new StringBuilder();
            sb.Append("tous les");
            if (rrule.Interval == 1)
            {
                sb.Append(" jours");
            }
            else
            {
                sb.Append(' ');
                sb.Append(rrule.Interval);
                sb.Append(" jours");
            }

            GetHumanEnd(rrule, sb);
            return sb.ToString();
        }

        protected override string GetText(WeeklyRecurrenceRule rrule, CultureInfo cultureInfo)
        {
            if (rrule == null)
                throw new ArgumentNullException(nameof(rrule));
            if (cultureInfo == null)
                throw new ArgumentNullException(nameof(cultureInfo));

            var sb = new StringBuilder();
            sb.Append("toutes les");
            if (rrule.Interval == 1)
            {
                sb.Append(" semaines");
            }
            else
            {
                sb.Append(' ');
                sb.Append(rrule.Interval);
                sb.Append(" semaines");
            }

            if (rrule.ByWeekDays != null && rrule.ByWeekDays.Any())
            {
                sb.Append(" le ");
                sb.Append(GetWeekdayHumanText(rrule.ByWeekDays, options: WeekdayHumanTextOptions.None));
            }

            GetHumanEnd(rrule, sb);
            return sb.ToString();
        }

        protected override string GetText(MonthlyRecurrenceRule rrule, CultureInfo cultureInfo)
        {
            if (rrule == null)
                throw new ArgumentNullException(nameof(rrule));
            if (cultureInfo == null)
                throw new ArgumentNullException(nameof(cultureInfo));

            var sb = new StringBuilder();
            sb.Append("tous les");
            if (rrule.Interval == 1)
            {
                sb.Append(" mois");
            }
            else
            {
                sb.Append(' ');
                sb.Append(rrule.Interval);
                sb.Append(" mois");
            }

            if (rrule.ByMonthDays != null && rrule.ByMonthDays.Any())
            {
                sb.Append(" le ");
                ListToHumanText(sb, FrenchCultureInfo, rrule.ByMonthDays.Select(GetByMonthdayOrdinalHumanText).ToList(), ", ", " et ");
                sb.Append(" jour");
            }

            if (rrule.ByWeekDays != null && rrule.ByWeekDays.Any())
            {
                if (rrule.BySetPositions != null && rrule.BySetPositions.Any())
                {
                    sb.Append(" le ");
                    sb.Append(GetBySetPosHumanText(rrule.BySetPositions[0]));
                }
                sb.Append(' ');
                sb.Append(GetWeekdayHumanText(rrule.ByWeekDays, options: WeekdayHumanTextOptions.AbbrDays | WeekdayHumanTextOptions.AbbrWeekdays | WeekdayHumanTextOptions.AbbrWeekendDays));
            }

            GetHumanEnd(rrule, sb);
            return sb.ToString();
        }

        protected override string GetText(YearlyRecurrenceRule rrule, CultureInfo cultureInfo)
        {
            if (rrule == null)
                throw new ArgumentNullException(nameof(rrule));
            if (cultureInfo == null)
                throw new ArgumentNullException(nameof(cultureInfo));

            var sb = new StringBuilder();
            sb.Append("tous les");
            if (rrule.Interval == 1)
            {
                sb.Append(" ans");
            }
            else
            {
                sb.Append(' ');
                sb.Append(rrule.Interval);
                sb.Append(" ans");
            }

            if (rrule.ByMonthDays != null && rrule.ByMonthDays.Any())
            {
                if (rrule.ByMonthDays.Any(day => day < 0))
                {
                    sb.Append(" le ");
                    ListToHumanText(sb, FrenchCultureInfo, rrule.ByMonthDays.Select(GetByMonthdayOrdinalHumanText).ToList(), ", ", " et ");
                    sb.Append(" jour");
                    if (rrule.ByMonths != null && rrule.ByMonths.Any())
                    {
                        var monthsList = ListToHumanText(FrenchCultureInfo, rrule.ByMonths.Select(MonthToString).ToList(), ", ", " et ");
                        if (MustPrecedeByApostrophe(monthsList))
                        {
                            sb.Append(" d'");
                        }
                        else
                        {
                            sb.Append(" de ");
                        }
                        sb.Append(monthsList);
                    }
                }
                else
                {
                    sb.Append(" le ");
                    ListToHumanText(sb, FrenchCultureInfo, rrule.ByMonthDays.Select(md => md.ToString(cultureInfo)).ToList(), ", ", " et ");
                    if (rrule.ByMonths != null && rrule.ByMonths.Any())
                    {
                        sb.Append(' ');
                        ListToHumanText(sb, FrenchCultureInfo, rrule.ByMonths.Select(MonthToString).ToList(), ", ", " et ");
                    }
                }
            }

            if (rrule.ByWeekDays != null && rrule.ByWeekDays.Any())
            {
                if (rrule.BySetPositions != null && rrule.BySetPositions.Any())
                {
                    sb.Append(" le ");
                    sb.Append(GetBySetPosHumanText(rrule.BySetPositions[0]));
                }
                sb.Append(' ');
                sb.Append(GetWeekdayHumanText(rrule.ByWeekDays, options: WeekdayHumanTextOptions.AbbrDays | WeekdayHumanTextOptions.AbbrWeekdays | WeekdayHumanTextOptions.AbbrWeekendDays));
                if (rrule.ByMonths != null && rrule.ByMonths.Any())
                {
                    var monthsList = ListToHumanText(FrenchCultureInfo, rrule.ByMonths.Select(MonthToString).ToList(), ", ", " et ");
                    if (MustPrecedeByApostrophe(monthsList))
                    {
                        sb.Append(" d'");
                    }
                    else
                    {
                        sb.Append(" de ");
                    }
                    sb.Append(monthsList);
                }
            }

            GetHumanEnd(rrule, sb);
            return sb.ToString();
        }

        private static bool MustPrecedeByApostrophe(string str)
        {
            if (string.IsNullOrEmpty(str))
                return false;

            return s_precededByApostropheChars.Contains(str[0]);
        }

        private static string? GetWeekdayHumanText(ICollection<ByDay> daysOfWeek, WeekdayHumanTextOptions options)
        {
            if (!daysOfWeek.Any())
                return null;

            return GetWeekdayHumanText(daysOfWeek.Where(dow => !dow.Ordinal.HasValue).Select(dow => dow.DayOfWeek).ToList(), ", ", " et ", options);
        }

        private static string GetBySetPosHumanText(int setPosition)
        {
            return setPosition switch
            {
                -1 => "dernier",
                1 => "premier",
                2 => "deuxième",
                3 => "troisième",
                4 => "quatrième",
                _ => Extensions.ToFrenchOrdinal(setPosition),
            };
        }

        private static void GetHumanEnd(RecurrenceRule rrule, StringBuilder sb)
        {
            if (rrule.Occurrences.HasValue)
            {
                sb.Append(" pour ");
                sb.Append(rrule.Occurrences.Value);
                sb.Append(" fois");
            }

            if (rrule.EndDate.HasValue)
            {
                sb.Append(" jusqu'au ");
                sb.AppendFormat(FrenchCultureInfo, "{0:d MMMM yyyy}", rrule.EndDate.Value);
            }
        }

        private static string? GetByMonthdayOrdinalHumanText(int monthday)
        {
            if (monthday > 0)
            {
                return Extensions.ToFrenchOrdinal(monthday);
            }

            if (monthday == -1)
            {
                return "dernier";
            }

            return null;
        }

        private static string GetWeekdayHumanText(ICollection<DayOfWeek> daysOfWeek, string separator = ", ", string lastSeparator = " et ", WeekdayHumanTextOptions options = WeekdayHumanTextOptions.None)
        {
            if (options.HasFlag(WeekdayHumanTextOptions.AbbrWeekdays) && IsWeekday(daysOfWeek))
            {
                if (options.HasFlag(WeekdayHumanTextOptions.Plural))
                    return "jours de semaine";
                return "jour de semaine";
            }

            if (options.HasFlag(WeekdayHumanTextOptions.AbbrDays) && IsFullWeek(daysOfWeek))
            {
                if (options.HasFlag(WeekdayHumanTextOptions.Plural))
                    return "jours";
                return "jour";
            }

            if (options.HasFlag(WeekdayHumanTextOptions.AbbrWeekendDays) && IsWeekendDay(daysOfWeek))
            {
                if (options.HasFlag(WeekdayHumanTextOptions.Plural))
                    return "jours de weekend";
                return "jour de weekend";
            }

            return ListToHumanText(FrenchCultureInfo, daysOfWeek.Select(DayOfWeekToString).ToList(), separator, lastSeparator);
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
                _ => throw new ArgumentOutOfRangeException(nameof(dayOfWeek), dayOfWeek, message: null),
            };
        }

        private static string MonthToString(Month month)
        {
            return month switch
            {
                Month.January => "janvier",
                Month.February => "février",
                Month.March => "mars",
                Month.April => "avril",
                Month.May => "mai",
                Month.June => "juin",
                Month.July => "juillet",
                Month.August => "aout",
                Month.September => "septembre",
                Month.October => "octobre",
                Month.November => "novembre",
                Month.December => "décembre",
                _ => throw new ArgumentOutOfRangeException(nameof(month), month, message: null),
            };
        }

        [Flags]
        private enum WeekdayHumanTextOptions
        {
            None = 0,
            AbbrDays = 1,
            AbbrWeekdays = 2,
            AbbrWeekendDays = 4,
            Plural = 8,
        }
    }
}
