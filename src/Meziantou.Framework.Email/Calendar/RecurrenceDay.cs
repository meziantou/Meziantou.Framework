using System.Globalization;

namespace Meziantou.Framework.Email.Calendar;

/// <summary>A day of a recurrence rule (<c>BYDAY</c>): a day of the week, and its position in the month or the year (<c>2MO</c>, the second Monday; <c>-1FR</c>, the last Friday).</summary>
/// <param name="Day">The day of the week.</param>
/// <param name="Position">The position of the day in the month or the year (negative from the end), or 0 for every such day.</param>
[System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Auto)]
public readonly record struct RecurrenceDay(DayOfWeek Day, int Position = 0)
{
    internal static readonly string[] Names = ["SU", "MO", "TU", "WE", "TH", "FR", "SA"];

    /// <summary>Returns the day as it is written in a rule, such as <c>MO</c> or <c>-1FR</c>.</summary>
    /// <returns>The day.</returns>
    public override string ToString() => (Position == 0 ? "" : Position.ToString(CultureInfo.InvariantCulture)) + Names[(int)Day];

    internal static RecurrenceDay? Parse(string value)
    {
        value = value.Trim();
        if (value.Length < 2)
        {
            return null;
        }

        var index = Array.FindIndex(Names, name => value.EndsWith(name, StringComparison.OrdinalIgnoreCase));
        if (index < 0)
        {
            return null;
        }

        var position = value[..^2];
        if (position.Length == 0)
        {
            return new RecurrenceDay((DayOfWeek)index);
        }

        return int.TryParse(position, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var number) && number is >= -53 and <= 53 && number != 0 ? new RecurrenceDay((DayOfWeek)index, number) : null;
    }
}
