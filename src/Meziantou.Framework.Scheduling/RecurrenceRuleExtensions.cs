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
    /// <remarks>The occurrences are computed using the local time of <paramref name="startDate"/>, and the offset of <paramref name="startDate"/> is applied to the result.
    /// The offset is therefore the same for every occurrence; use an overload that takes a <see cref="TimeZoneInfo"/> to follow daylight saving transitions.</remarks>
    public static DateTimeOffset? GetNextOccurrence(this IRecurrenceRule recurrenceRule, DateTimeOffset startDate)
    {
        ArgumentNullException.ThrowIfNull(recurrenceRule);

        foreach (var occurrence in recurrenceRule.GetNextOccurrences(startDate.DateTime))
            return new DateTimeOffset(DateTime.SpecifyKind(occurrence, DateTimeKind.Unspecified), startDate.Offset);

        return null;
    }

    /// <summary>Gets all occurrences of the recurrence, reading <paramref name="startDate"/> as a wall-clock time in <paramref name="timeZone"/>.</summary>
    /// <param name="startDate">The wall-clock time to start generating occurrences from. Its <see cref="DateTime.Kind"/> is ignored.</param>
    /// <param name="timeZone">The time zone the recurrence is expressed in.</param>
    /// <returns>An enumerable sequence of occurrences, each carrying the UTC offset in effect at that occurrence.</returns>
    public static IEnumerable<DateTimeOffset> GetNextOccurrences(this IRecurrenceRule recurrenceRule, DateTime startDate, TimeZoneInfo timeZone)
    {
        ArgumentNullException.ThrowIfNull(recurrenceRule);
        ArgumentNullException.ThrowIfNull(timeZone);

        // Extension methods bind statically, so a variable typed as IRecurrenceRule would otherwise miss the
        // implementations that bound UNTIL by instant instead of by wall clock.
        if (recurrenceRule is RecurrenceRule rule)
            return rule.GetNextOccurrences(startDate, timeZone);

        if (recurrenceRule is CronExpression cronExpression)
            return cronExpression.GetNextOccurrences(startDate, timeZone);

        return Utilities.ToDateTimeOffsets(recurrenceRule.GetNextOccurrences(DateTime.SpecifyKind(startDate, DateTimeKind.Unspecified)), timeZone);
    }

    /// <summary>Gets all occurrences of the recurrence, starting from the instant <paramref name="startDate"/> denotes.</summary>
    /// <param name="startDate">The instant to start generating occurrences from. It is reduced to a wall-clock time in <paramref name="timeZone"/>.</param>
    /// <param name="timeZone">The time zone the recurrence is expressed in.</param>
    /// <returns>An enumerable sequence of occurrences, each carrying the UTC offset in effect at that occurrence.</returns>
    public static IEnumerable<DateTimeOffset> GetNextOccurrences(this IRecurrenceRule recurrenceRule, DateTimeOffset startDate, TimeZoneInfo timeZone)
    {
        ArgumentNullException.ThrowIfNull(recurrenceRule);
        ArgumentNullException.ThrowIfNull(timeZone);

        return recurrenceRule.GetNextOccurrences(TimeZoneInfo.ConvertTime(startDate, timeZone).DateTime, timeZone);
    }

    /// <summary>Gets all occurrences of the recurrence, reading <paramref name="startDate"/> as a wall-clock time in the specified time zone.</summary>
    /// <param name="startDate">The wall-clock time to start generating occurrences from. Its <see cref="DateTime.Kind"/> is ignored.</param>
    /// <param name="timeZoneId">The identifier of the time zone the recurrence is expressed in.</param>
    /// <returns>An enumerable sequence of occurrences, each carrying the UTC offset in effect at that occurrence.</returns>
    /// <exception cref="TimeZoneNotFoundException">No time zone matches <paramref name="timeZoneId"/>.</exception>
    /// <remarks>On .NET Framework the runtime provides no conversion between IANA and Windows identifiers, so an
    /// IANA identifier such as <c>America/New_York</c> does not resolve on Windows. Resolve the
    /// <see cref="TimeZoneInfo"/> there and use the overload that takes one.</remarks>
    public static IEnumerable<DateTimeOffset> GetNextOccurrences(this IRecurrenceRule recurrenceRule, DateTime startDate, string timeZoneId)
    {
        ArgumentNullException.ThrowIfNull(recurrenceRule);

        return recurrenceRule.GetNextOccurrences(startDate, TimeZones.Find(timeZoneId));
    }

    /// <summary>Gets all occurrences of the recurrence, starting from the instant <paramref name="startDate"/> denotes.</summary>
    /// <param name="startDate">The instant to start generating occurrences from. It is reduced to a wall-clock time in the specified time zone.</param>
    /// <param name="timeZoneId">The identifier of the time zone the recurrence is expressed in.</param>
    /// <returns>An enumerable sequence of occurrences, each carrying the UTC offset in effect at that occurrence.</returns>
    /// <exception cref="TimeZoneNotFoundException">No time zone matches <paramref name="timeZoneId"/>.</exception>
    /// <remarks>On .NET Framework the runtime provides no conversion between IANA and Windows identifiers, so an
    /// IANA identifier such as <c>America/New_York</c> does not resolve on Windows. Resolve the
    /// <see cref="TimeZoneInfo"/> there and use the overload that takes one.</remarks>
    public static IEnumerable<DateTimeOffset> GetNextOccurrences(this IRecurrenceRule recurrenceRule, DateTimeOffset startDate, string timeZoneId)
    {
        ArgumentNullException.ThrowIfNull(recurrenceRule);

        return recurrenceRule.GetNextOccurrences(startDate, TimeZones.Find(timeZoneId));
    }

    /// <summary>Gets the next occurrence of the recurrence, reading <paramref name="startDate"/> as a wall-clock time in <paramref name="timeZone"/>.</summary>
    /// <param name="startDate">The wall-clock time to start searching from. Its <see cref="DateTime.Kind"/> is ignored.</param>
    /// <param name="timeZone">The time zone the recurrence is expressed in.</param>
    /// <returns>The next occurrence with the UTC offset in effect at that occurrence, or <see langword="null"/> if there are no more occurrences.</returns>
    public static DateTimeOffset? GetNextOccurrence(this IRecurrenceRule recurrenceRule, DateTime startDate, TimeZoneInfo timeZone)
    {
        ArgumentNullException.ThrowIfNull(recurrenceRule);

        foreach (var occurrence in recurrenceRule.GetNextOccurrences(startDate, timeZone))
            return occurrence;

        return null;
    }

    /// <summary>Gets the next occurrence of the recurrence, starting from the instant <paramref name="startDate"/> denotes.</summary>
    /// <param name="startDate">The instant to start searching from. It is reduced to a wall-clock time in <paramref name="timeZone"/>.</param>
    /// <param name="timeZone">The time zone the recurrence is expressed in.</param>
    /// <returns>The next occurrence with the UTC offset in effect at that occurrence, or <see langword="null"/> if there are no more occurrences.</returns>
    public static DateTimeOffset? GetNextOccurrence(this IRecurrenceRule recurrenceRule, DateTimeOffset startDate, TimeZoneInfo timeZone)
    {
        ArgumentNullException.ThrowIfNull(recurrenceRule);

        foreach (var occurrence in recurrenceRule.GetNextOccurrences(startDate, timeZone))
            return occurrence;

        return null;
    }

    /// <summary>Gets the next occurrence of the recurrence, reading <paramref name="startDate"/> as a wall-clock time in the specified time zone.</summary>
    /// <param name="startDate">The wall-clock time to start searching from. Its <see cref="DateTime.Kind"/> is ignored.</param>
    /// <param name="timeZoneId">The identifier of the time zone the recurrence is expressed in.</param>
    /// <returns>The next occurrence with the UTC offset in effect at that occurrence, or <see langword="null"/> if there are no more occurrences.</returns>
    /// <exception cref="TimeZoneNotFoundException">No time zone matches <paramref name="timeZoneId"/>.</exception>
    /// <remarks>On .NET Framework the runtime provides no conversion between IANA and Windows identifiers, so an
    /// IANA identifier such as <c>America/New_York</c> does not resolve on Windows. Resolve the
    /// <see cref="TimeZoneInfo"/> there and use the overload that takes one.</remarks>
    public static DateTimeOffset? GetNextOccurrence(this IRecurrenceRule recurrenceRule, DateTime startDate, string timeZoneId)
    {
        ArgumentNullException.ThrowIfNull(recurrenceRule);

        return recurrenceRule.GetNextOccurrence(startDate, TimeZones.Find(timeZoneId));
    }

    /// <summary>Gets the next occurrence of the recurrence, starting from the instant <paramref name="startDate"/> denotes.</summary>
    /// <param name="startDate">The instant to start searching from. It is reduced to a wall-clock time in the specified time zone.</param>
    /// <param name="timeZoneId">The identifier of the time zone the recurrence is expressed in.</param>
    /// <returns>The next occurrence with the UTC offset in effect at that occurrence, or <see langword="null"/> if there are no more occurrences.</returns>
    /// <exception cref="TimeZoneNotFoundException">No time zone matches <paramref name="timeZoneId"/>.</exception>
    /// <remarks>On .NET Framework the runtime provides no conversion between IANA and Windows identifiers, so an
    /// IANA identifier such as <c>America/New_York</c> does not resolve on Windows. Resolve the
    /// <see cref="TimeZoneInfo"/> there and use the overload that takes one.</remarks>
    public static DateTimeOffset? GetNextOccurrence(this IRecurrenceRule recurrenceRule, DateTimeOffset startDate, string timeZoneId)
    {
        ArgumentNullException.ThrowIfNull(recurrenceRule);

        return recurrenceRule.GetNextOccurrence(startDate, TimeZones.Find(timeZoneId));
    }
}
