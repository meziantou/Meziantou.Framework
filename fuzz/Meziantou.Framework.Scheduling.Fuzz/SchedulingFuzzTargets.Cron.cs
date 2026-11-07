using System.Globalization;
using Meziantou.Framework.Fuzzing;

namespace Meziantou.Framework.Scheduling.Fuzz;

public static partial class SchedulingFuzzTargets
{
    /// <summary>The days after the start in which the occurrences of a cron expression are compared with those of <see cref="CronReference"/>.</summary>
    private const int CronReferenceWindowDays = 800;

    private static string[] CronTokens =>
    [
        "*", "?", "*/", "/", "-", ",", " ", "\t", "L", "LW", "L-", "W", "#", "@yearly", "@annually", "@monthly", "@weekly", "@daily", "@midnight", "@hourly",
        "JAN", "FEB", "MAR", "APR", "MAY", "JUN", "JUL", "AUG", "SEP", "OCT", "NOV", "DEC", "SUN", "MON", "TUE", "WED", "THU", "FRI", "SAT",
        "0", "1", "5", "7", "12", "15", "23", "29", "30", "31", "59", "60", "1970", "2024", "9999", "10000", "2147483648",
    ];

    /// <summary>
    /// The first bytes select a start date and a time zone, the rest is the expression. <c>Parse</c> and <c>TryParse</c> agree
    /// with each other and with <see cref="CronReference"/>; an expression equals the one parsed from its text and the one
    /// listing the values of its fields, with the same hash code. The occurrences are the whole seconds matching every field,
    /// in order: in the first days after the start they are exactly those <see cref="CronReference"/> finds by scanning every
    /// day. The evaluations in a time zone are consistent with the wall-clock one.
    /// </summary>
    private static void Cron(ReadOnlySpan<byte> data)
    {
        var start = ReadStart(ref data, allowLocal: true);
        var timeZone = FuzzInput.Pick(TimeZones, FuzzInput.ReadByte(ref data));
        var text = FuzzText.Decode(data);

        var parsed = CronExpression.TryParse(text, out var expression);
        FuzzAssert.Equal(parsed, CronExpression.TryParse(text.AsSpan(), out var spanExpression), "TryParse(string) and TryParse(ReadOnlySpan<char>) disagree");
        var referenceParsed = CronReference.TryParse(text, out var reference);
        FuzzAssert.Equal(referenceParsed, parsed, $"The expression and the reference disagree on whether {FuzzAssert.Format(text)} is valid");
        if (!parsed)
        {
            ExpectFormatException(() => CronExpression.Parse(text), "Parse(string)");
            ExpectFormatException(() => CronExpression.Parse(text.AsSpan()), "Parse(ReadOnlySpan<char>)");
            return;
        }

        FuzzAssert.True(expression is not null && spanExpression is not null && reference is not null, "TryParse returned true without an expression");
        var expressionText = expression.ToString();
        FuzzAssert.Equal(text.Trim(' ', '\t'), expressionText, "ToString() is not the text without its leading and trailing spaces and tabs");
        FuzzAssert.Equal(expressionText, spanExpression.ToString(), "The expressions parsed from a string and from a span differ");
        CheckEqual(expression, spanExpression, "the expression parsed from a span");
        CheckEqual(expression, CronExpression.Parse(text), "the expression returned by Parse");
        CheckEqual(expression, CronExpression.Parse(expressionText), "the expression parsed from its text");

        var canonicalText = reference.ToCanonicalText();
        if (!CronExpression.TryParse(canonicalText, out var canonical))
            FuzzAssert.Fail($"The values of the fields of {FuzzAssert.Format(text)}, {FuzzAssert.Format(canonicalText)}, are not a valid expression");

        CheckEqual(expression, canonical, "the expression listing the values of its fields, " + FuzzAssert.Format(canonicalText));
        FuzzAssert.False(expression.Equals(null), "An expression equals null");
        FuzzAssert.False(expression == null, "An expression equals null");

        var description = $"{FuzzAssert.Format(expressionText)} from {Format(start)}";
        var occurrences = expression.GetNextOccurrences(start).Take(MaxOccurrences).ToList();
        for (var i = 0; i < occurrences.Count; i++)
        {
            var occurrence = occurrences[i];
            var what = $"{description}: the occurrence {Format(occurrence)}";
            FuzzAssert.Equal(start.Kind, occurrence.Kind, $"{what} does not have the kind of the start");
            FuzzAssert.Equal(0L, occurrence.Ticks % TimeSpan.TicksPerSecond, $"{what} is not a whole second");
            FuzzAssert.True(occurrence >= start, $"{what} precedes the start");
            FuzzAssert.True(i is 0 || occurrences[i - 1] < occurrence, $"{what} does not follow {Format(occurrences[Math.Max(i - 1, 0)])}");
            FuzzAssert.True(reference.Matches(occurrence), $"{what} does not match the fields of the expression");
        }

        // No occurrence is skipped: in the first days after the start, the occurrences are those found by scanning every day
        var windowEnd = DateTime.MaxValue.Ticks - start.Ticks > TimeSpan.TicksPerDay * CronReferenceWindowDays ? start.AddDays(CronReferenceWindowDays) : DateTime.MaxValue;
        var expected = reference.GetOccurrences(start, windowEnd).Take(MaxOccurrences).ToList();
        var inWindow = occurrences.TakeWhile(occurrence => occurrence <= windowEnd).ToList();
        FuzzAssert.True(inWindow.SequenceEqual(expected), $"{description}: the occurrences up to {Format(windowEnd)} are not those that match the fields{Environment.NewLine}Expected: {Format(expected)}{Environment.NewLine}Actual:   {Format(inWindow)}");

        // The evaluation in a time zone reads the start as a wall-clock time
        var wallClockStart = DateTime.SpecifyKind(start, DateTimeKind.Unspecified);
        var wallClockOccurrences = start.Kind is DateTimeKind.Unspecified ? occurrences : expression.GetNextOccurrences(wallClockStart).Take(MaxOccurrences).ToList();
        var zoned = expression.GetNextOccurrences(start, timeZone).Take(MaxOccurrences).ToList();
        CheckZonedOccurrences(zoned, timeZone, description + " in " + timeZone.Id);
        if (!timeZone.SupportsDaylightSavingTime)
        {
            CheckFixedOffsetOccurrences(zoned, wallClockOccurrences, timeZone.BaseUtcOffset, description + " in " + timeZone.Id);
        }
        else
        {
            // A wall-clock time made invalid by a forward transition is moved by the length of the gap
            var gap = timeZone.GetAdjustmentRules()[0].DaylightDelta;
            foreach (var occurrence in zoned)
            {
                var wallClock = occurrence.DateTime;
                FuzzAssert.True(reference.Matches(wallClock) || (wallClock.Ticks >= gap.Ticks && reference.Matches(wallClock - gap)), $"{description}: the occurrence {Format(occurrence)} in {timeZone.Id} does not match the fields of the expression");
            }
        }

        // The DateTimeOffset overload starts at an instant and returns every occurrence after it
        if (TryCreateInstant(wallClockStart, timeZone, out var instant))
        {
            var fromInstant = expression.GetNextOccurrences(instant, timeZone).Take(MaxOccurrences).ToList();
            CheckZonedOccurrences(fromInstant, timeZone, description + " from the instant " + Format(instant) + " in " + timeZone.Id);
            FuzzAssert.True(fromInstant.All(occurrence => occurrence >= instant), $"{description}: an occurrence precedes the instant {Format(instant)} in {timeZone.Id}: {Format(fromInstant)}");
        }
    }

    private static void CheckEqual(CronExpression expected, CronExpression actual, string what)
    {
        FuzzAssert.True(expected.Equals(actual) && actual.Equals(expected) && expected == actual && !(expected != actual) && expected.Equals((object)actual), $"{FuzzAssert.Format(expected.ToString())} does not equal {what}");
        FuzzAssert.Equal(expected.GetHashCode(), actual.GetHashCode(), $"{FuzzAssert.Format(expected.ToString())} and {what} have different hash codes");
    }

    private static IEnumerable<FuzzSeed> GetCronSeeds()
    {
        var literals = GetStringLiterals("CronExpressionTests.cs").Where(literal => literal.Length < 200).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal);
        var index = 0;
        foreach (var literal in literals)
        {
            // The second notable start, 2024-01-01, in the time zone selected by the second byte
            yield return FuzzSeed.FromText(string.Create(CultureInfo.InvariantCulture, $"cron-{index}"), [0x01, (byte)(index % TimeZones.Length)], literal);
            index++;
        }

        yield return FuzzSeed.FromText("cron-max-start", [0x0F, 0x00], "* * * * * *");
        yield return FuzzSeed.FromText("cron-last-second", [0x0D, 0x05], "59 59 23 31 12 * 9999");
        yield return FuzzSeed.FromText("cron-local-start", [0x40, 0x01], "0 30 2 * * SUN#2");
        yield return FuzzSeed.FromText("cron-built-start", [0xA0, 0x07, 0xE8, 0x01, 0x1C, 0x17, 0x3B, 0x3B, 0x80, 0x04], "0 0 L-3,LW,15W * 5L,1#5");
        yield return FuzzSeed.FromText("cron-never", [0x01, 0x02], "0 0 30 2 *");
        yield return FuzzSeed.FromText("cron-reversed", [0x01, 0x03], "*/7 22-2/3 30-2 11-2 FRI-MON");
    }
}
