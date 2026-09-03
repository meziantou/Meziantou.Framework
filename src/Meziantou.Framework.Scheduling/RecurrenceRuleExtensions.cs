namespace Meziantou.Framework.Scheduling;

public static class RecurrenceRuleExtensions
{
    /// <summary>Gets the next occurrence of the recurrence starting from the specified date.</summary>
    /// <param name="startDate">The date to start searching for the next occurrence.</param>
    /// <returns>The next occurrence date, or <see langword="null"/> if there are no more occurrences.</returns>
    public static DateTime? GetNextOccurrence(this IRecurrenceRule recurrenceRule, DateTime startDate)
    {
        ArgumentNullException.ThrowIfNull(recurrenceRule);

        foreach (var occurrence in recurrenceRule.GetNextOccurrences(startDate))
            return occurrence;

        return null;
    }

    /// <summary>Gets the next occurrence of the recurrence starting from the specified date.</summary>
    /// <param name="startDate">The date to start searching for the next occurrence.</param>
    /// <returns>The next occurrence date with the same offset as <paramref name="startDate"/>, or <see langword="null"/> if there are no more occurrences.</returns>
    /// <remarks>The occurrences are computed using the local time of <paramref name="startDate"/>, and the offset of <paramref name="startDate"/> is applied to the result.</remarks>
    public static DateTimeOffset? GetNextOccurrence(this IRecurrenceRule recurrenceRule, DateTimeOffset startDate)
    {
        ArgumentNullException.ThrowIfNull(recurrenceRule);

        foreach (var occurrence in recurrenceRule.GetNextOccurrences(startDate.DateTime))
            return new DateTimeOffset(DateTime.SpecifyKind(occurrence, DateTimeKind.Unspecified), startDate.Offset);

        return null;
    }
}
