namespace Meziantou.Framework.Scheduling;

public static class RecurrenceRuleExtensions
{
    /// <summary>Gets the next occurrence of the recurrence starting from the specified date.</summary>
    /// <param name="startDate">The date to start searching for the next occurrence.</param>
    /// <returns>The next occurrence date, or <see langword="null"/> if there are no more occurrences.</returns>
    public static DateTime? GetNextOccurrence(this IRecurrenceRule recurrenceRule, DateTime startDate)
    {
        ArgumentNullException.ThrowIfNull(recurrenceRule);
        return recurrenceRule.GetNextOccurrences(startDate).FirstOrDefault();
    }
}
