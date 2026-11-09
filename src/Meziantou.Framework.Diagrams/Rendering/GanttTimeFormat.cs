using System.Globalization;
using System.Text;

namespace Meziantou.Framework.Diagrams.Rendering;

/// <summary>Formats the dates of the axis of a Gantt chart as the <c>strftime</c> of d3, in English and without time zone.</summary>
internal static class GanttTimeFormat
{
    private static readonly DateTime Epoch = new(1970, 1, 1, 0, 0, 0, DateTimeKind.Unspecified);

    public static string Format(string format, DateTime date)
    {
        var builder = new StringBuilder();
        for (var i = 0; i < format.Length; i++)
        {
            if (format[i] != '%' || i + 1 >= format.Length)
            {
                builder.Append(format[i]);
                continue;
            }

            var c = format[++i];
            char? pad = null;
            if (c is '-' or '_' or '0' && i + 1 < format.Length)
            {
                pad = c is '-' ? '\0' : c is '_' ? ' ' : '0';
                c = format[++i];
            }

            builder.Append(Directive(c, pad ?? (c is 'e' ? ' ' : '0'), date));
        }

        return builder.ToString();
    }

    private static string Directive(char c, char pad, DateTime date)
    {
        var hour12 = date.Hour % 12 is 0 ? 12 : date.Hour % 12;
        return c switch
        {
            'a' => GanttDateFormat.DayNames[(int)date.DayOfWeek][..3],
            'A' => GanttDateFormat.DayNames[(int)date.DayOfWeek],
            'b' => GanttDateFormat.MonthNames[date.Month - 1][..3],
            'B' => GanttDateFormat.MonthNames[date.Month - 1],
            'c' => Format("%-m/%-d/%Y, %-I:%M:%S %p", date),
            'd' => Pad(date.Day, pad, 2),
            'e' => Pad(date.Day, pad, 2),
            'f' => Pad(date.Millisecond * 1000, pad, 6),
            'H' => Pad(date.Hour, pad, 2),
            'I' => Pad(hour12, pad, 2),
            'j' => Pad(date.DayOfYear, pad, 3),
            'L' => Pad(date.Millisecond, pad, 3),
            'm' => Pad(date.Month, pad, 2),
            'M' => Pad(date.Minute, pad, 2),
            'p' => date.Hour < 12 ? "AM" : "PM",
            'q' => (((date.Month - 1) / 3) + 1).ToString(CultureInfo.InvariantCulture),
            'Q' => Math.Floor((date - Epoch).TotalMilliseconds).ToString(CultureInfo.InvariantCulture),
            's' => Math.Floor((date - Epoch).TotalSeconds).ToString(CultureInfo.InvariantCulture),
            'S' => Pad(date.Second, pad, 2),
            'u' => (date.DayOfWeek is DayOfWeek.Sunday ? 7 : (int)date.DayOfWeek).ToString(CultureInfo.InvariantCulture),
            'U' => Pad((date.DayOfYear + 6 - (int)date.DayOfWeek) / 7, pad, 2),
            'V' => Pad(ISOWeek.GetWeekOfYear(date), pad, 2),
            'w' => ((int)date.DayOfWeek).ToString(CultureInfo.InvariantCulture),
            'W' => Pad((date.DayOfYear + 6 - (((int)date.DayOfWeek + 6) % 7)) / 7, pad, 2),
            'x' => Format("%-m/%-d/%Y", date),
            'X' => Format("%-I:%M:%S %p", date),
            'y' => Pad(date.Year % 100, pad, 2),
            'Y' => Pad(date.Year % 10_000, pad, 4),
            'G' => Pad(ISOWeek.GetYear(date) % 10_000, pad, 4),
            'g' => Pad(ISOWeek.GetYear(date) % 100, pad, 2),
            'Z' => "+0000",
            '%' => "%",
            _ => c.ToString(),
        };
    }

    private static string Pad(int value, char pad, int width)
    {
        var text = value.ToString(CultureInfo.InvariantCulture);
        return pad is '\0' || text.Length >= width ? text : new string(pad, width - text.Length) + text;
    }
}
