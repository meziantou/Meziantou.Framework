namespace Meziantou.Framework.Scheduling;

/// <summary>Provides extension methods for converting recurrence rules to human-readable text.</summary>
public static class RecurrenceRuleHumanizerExtensions
{
    /// <summary>Converts this recurrence rule to human-readable text using the current UI culture.</summary>
    /// <param name="rrule">The recurrence rule to convert.</param>
    /// <returns>A human-readable string representation of the recurrence rule, or <see langword="null"/> when the language of the culture is not supported or when the rule derives from <see cref="RecurrenceRule"/> outside of this library.</returns>
    /// <remarks>The language is read from the culture name (<c>fr-CA</c> is French), including in invariant globalization mode. The invariant culture is English.</remarks>
    public static string? GetHumanText(this RecurrenceRule rrule)
    {
        return RecurrenceRuleHumanizer.GetText(rrule, cultureInfo: null);
    }

    /// <summary>Converts this recurrence rule to human-readable text using the specified culture.</summary>
    /// <param name="rrule">The recurrence rule to convert.</param>
    /// <param name="cultureInfo">The culture to use for formatting, or <see langword="null"/> to use the current UI culture.</param>
    /// <returns>A human-readable string representation of the recurrence rule, or <see langword="null"/> when the language of the culture is not supported or when the rule derives from <see cref="RecurrenceRule"/> outside of this library.</returns>
    /// <remarks>The language is read from the culture name (<c>fr-CA</c> is French), including in invariant globalization mode. The invariant culture is English.</remarks>
    public static string? GetHumanText(this RecurrenceRule rrule, CultureInfo? cultureInfo)
    {
        return RecurrenceRuleHumanizer.GetText(rrule, cultureInfo);
    }
}
