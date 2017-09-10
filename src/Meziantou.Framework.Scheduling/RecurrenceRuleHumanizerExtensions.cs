using System.Globalization;

namespace Meziantou.Framework.Scheduling
{
    public static class RecurrenceRuleHumanizerExtensions
    {
        public static string GetHumanText(this RecurrenceRule rrule)
        {
            return RecurrenceRuleHumanizer.GetText(rrule);
        }

        public static string GetHumanText(this RecurrenceRule rrule, CultureInfo cultureInfo)
        {
            return RecurrenceRuleHumanizer.GetText(rrule, cultureInfo);
        }
    }
}