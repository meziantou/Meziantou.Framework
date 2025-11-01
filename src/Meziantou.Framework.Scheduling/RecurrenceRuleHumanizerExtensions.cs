namespace Meziantou.Framework.Scheduling;

/// <summary>Provides extension methods for converting recurrence rules to human-readable text.</summary>
public static class RecurrenceRuleHumanizerExtensions
{
    /// <summary>Converts this recurrence rule to human-readable text using the current UI culture.</summary>
    /// <param name="rrule">The recurrence rule to convert.</param>
    /// <returns>A human-readable string representation of the recurrence rule.</returns>
    public static string? GetHumanText(this RecurrenceRule rrule)
    {
        return RecurrenceRuleHumanizer.GetText(rrule, cultureInfo: null);
    }

    /// <summary>Converts this recurrence rule to human-readable text using the specified culture.</summary>
    /// <param name="rrule">The recurrence rule to convert.</param>
    /// <param name="cultureInfo">The culture to use for formatting, or <see langword="null"/> to use the current UI culture.</param>
    /// <returns>A human-readable string representation of the recurrence rule.</returns>
    public static string? GetHumanText(this RecurrenceRule rrule, CultureInfo? cultureInfo)
    {
        return RecurrenceRuleHumanizer.GetText(rrule, cultureInfo);
    }
}
