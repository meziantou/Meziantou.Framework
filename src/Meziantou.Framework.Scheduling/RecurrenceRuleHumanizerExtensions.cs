using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace Meziantou.Framework.Scheduling
{
    public static class RecurrenceRuleHumanizerExtensions
    {
        [SuppressMessage("Usage", "MA0011:IFormatProvider is missing", Justification = "By design")]
        public static string? GetHumanText(this RecurrenceRule rrule)
        {
            return RecurrenceRuleHumanizer.GetText(rrule);
        }

        public static string? GetHumanText(this RecurrenceRule rrule, CultureInfo cultureInfo)
        {
            return RecurrenceRuleHumanizer.GetText(rrule, cultureInfo);
        }
    }
}
