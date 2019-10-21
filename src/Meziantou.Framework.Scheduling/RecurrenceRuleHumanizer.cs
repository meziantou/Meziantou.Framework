#nullable disable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Meziantou.Framework.Scheduling
{
    public abstract class RecurrenceRuleHumanizer
    {
        protected static readonly CultureInfo EnglishCultureInfo = new CultureInfo("en");
        protected static readonly CultureInfo FrenchCultureInfo = new CultureInfo("fr");

        public static IDictionary<CultureInfo, RecurrenceRuleHumanizer> SupportedHumanizers { get; }

        static RecurrenceRuleHumanizer()
        {
            SupportedHumanizers = new Dictionary<CultureInfo, RecurrenceRuleHumanizer>
            {
                { EnglishCultureInfo, new RecurrenceRuleHumanizerEnglish() },
                { FrenchCultureInfo, new RecurrenceRuleHumanizerFrench() },
            };
        }

        public static string GetText(RecurrenceRule rrule)
        {
            return GetText(rrule, cultureInfo: null);
        }

        public static string GetText(RecurrenceRule rrule, CultureInfo cultureInfo)
        {
            if (rrule == null)
                throw new ArgumentNullException(nameof(rrule));

            if (cultureInfo == null)
            {
                cultureInfo = CultureInfo.CurrentUICulture;
            }

            if (!SupportedHumanizers.TryGetValue(cultureInfo, out var humanizer))
            {
                if (!cultureInfo.IsNeutralCulture)
                {
                    return GetText(rrule, cultureInfo.Parent);
                }
            }

            if (humanizer != null)
            {
                if (rrule is DailyRecurrenceRule dailyRecurrenceRule)
                    return humanizer.GetText(dailyRecurrenceRule, cultureInfo);

                if (rrule is WeeklyRecurrenceRule weeklyRecurrenceRule)
                    return humanizer.GetText(weeklyRecurrenceRule, cultureInfo);

                if (rrule is MonthlyRecurrenceRule monthlyRecurrenceRule)
                    return humanizer.GetText(monthlyRecurrenceRule, cultureInfo);

                if (rrule is YearlyRecurrenceRule yearlyRecurrenceRule)
                    return humanizer.GetText(yearlyRecurrenceRule, cultureInfo);
            }

            return null;
        }

        protected abstract string GetText(DailyRecurrenceRule rrule, CultureInfo cultureInfo);
        protected abstract string GetText(WeeklyRecurrenceRule rrule, CultureInfo cultureInfo);
        protected abstract string GetText(MonthlyRecurrenceRule rrule, CultureInfo cultureInfo);
        protected abstract string GetText(YearlyRecurrenceRule rrule, CultureInfo cultureInfo);

        protected static void ListToHumanText<T>(StringBuilder sb, CultureInfo cultureInfo, IList<T> list, string separator, string lastSeparator)
        {
            if (list == null)
                return;

            for (var i = 0; i < list.Count; i++)
            {
                if (i > 0)
                {
                    if (i < list.Count - 1)
                    {
                        sb.Append(separator);
                    }
                    else
                    {
                        sb.Append(lastSeparator);
                    }
                }

                sb.AppendFormat(cultureInfo, "{0}", list[i]);
            }
        }

        protected static string ListToHumanText<T>(CultureInfo cultureInfo, IList<T> list, string separator, string lastSeparator)
        {
            var sb = new StringBuilder();
            ListToHumanText(sb, cultureInfo, list, separator, lastSeparator);
            return sb.ToString();
        }

        protected static bool IsWeekday(ICollection<DayOfWeek> daysOfWeek)
        {
            return daysOfWeek.Count == 5 &&
                   daysOfWeek.Contains(DayOfWeek.Monday) &&
                   daysOfWeek.Contains(DayOfWeek.Tuesday) &&
                   daysOfWeek.Contains(DayOfWeek.Wednesday) &&
                   daysOfWeek.Contains(DayOfWeek.Thursday) &&
                   daysOfWeek.Contains(DayOfWeek.Friday);
        }

        protected static bool IsWeekendDay(ICollection<DayOfWeek> daysOfWeek)
        {
            return daysOfWeek.Count == 2 &&
                   daysOfWeek.Contains(DayOfWeek.Sunday) &&
                   daysOfWeek.Contains(DayOfWeek.Saturday);
        }

        protected static bool IsFullWeek(ICollection<DayOfWeek> daysOfWeek)
        {
            return daysOfWeek.Count == 7 &&
                   daysOfWeek.Contains(DayOfWeek.Monday) &&
                   daysOfWeek.Contains(DayOfWeek.Tuesday) &&
                   daysOfWeek.Contains(DayOfWeek.Wednesday) &&
                   daysOfWeek.Contains(DayOfWeek.Thursday) &&
                   daysOfWeek.Contains(DayOfWeek.Friday) &&
                   daysOfWeek.Contains(DayOfWeek.Saturday) &&
                   daysOfWeek.Contains(DayOfWeek.Sunday);
        }
    }
}
