using System.Runtime.InteropServices;
using Meziantou.Framework.JavaScriptEngine.TimeZones;

namespace Meziantou.Framework.JavaScriptEngine;

/// <summary>The possible epoch nanoseconds of a local date and time in a time zone (none, one or two values, sorted).</summary>
[StructLayout(LayoutKind.Auto)]
internal readonly struct PossibleEpochNanoseconds
{
    private readonly Int128 _first;
    private readonly Int128 _second;

    public PossibleEpochNanoseconds(int count, Int128 first, Int128 second)
    {
        Count = count;
        _first = first;
        _second = second;
    }

    public int Count { get; }

    public Int128 this[int index] => index == 0 ? _first : _second;
}

internal static partial class TemporalOperations
{
    private static readonly Int128 NsPerMinute = 60_000_000_000;

    /// <summary>SystemTimeZoneIdentifier(): the primary identifier of the time zone of the engine.</summary>
    public static TemporalTimeZone SystemTimeZoneIdentifier(JsEngine engine)
    {
        var record = engine.SystemTimeZone;
        if (record.Identifier != record.PrimaryIdentifier && TimeZoneDatabase.Default.TryGetTimeZone(record.PrimaryIdentifier, out var primary))
        {
            record = primary;
        }

        return TemporalTimeZone.FromNamed(record);
    }

    /// <summary>ToTemporalTimeZoneIdentifier(temporalTimeZoneLike)</summary>
    public static TemporalTimeZone ToTemporalTimeZoneIdentifier(JsValue temporalTimeZoneLike)
    {
        if (temporalTimeZoneLike.Cell is JsZonedDateTime zonedDateTime)
            return zonedDateTime.TimeZone;

        if (temporalTimeZoneLike.Cell is not JsString str)
            return ThrowHelper.ThrowTypeError<TemporalTimeZone>("The time zone must be a string or a Temporal.ZonedDateTime");

        var parseResult = ParseTemporalTimeZoneString(str.ToString());
        if (parseResult.IsOffset)
            return TemporalTimeZone.FromOffset(parseResult.OffsetMinutes);

        return GetAvailableNamedTimeZone(parseResult.Name!);
    }

    /// <summary>GetAvailableNamedTimeZoneIdentifier(timeZoneIdentifier): a RangeError when there is no such time zone.</summary>
    public static TemporalTimeZone GetAvailableNamedTimeZone(string name)
    {
        if (string.Equals(name, "UTC", StringComparison.OrdinalIgnoreCase))
            return TemporalTimeZone.Utc;

        if (!TimeZoneDatabase.Default.TryGetTimeZone(name, out var record))
            return ThrowHelper.ThrowRangeError<TemporalTimeZone>("The time zone '" + name + "' is not supported");

        return TemporalTimeZone.FromNamed(record);
    }

    /// <summary>ParseTemporalTimeZoneString(timeZoneString)</summary>
    public static TimeZoneIdentifierParseResult ParseTemporalTimeZoneString(string timeZoneString)
    {
        if (TemporalParser.TryParseTimeZoneIdentifier(timeZoneString, out var identifier))
            return identifier;

        var result = TemporalParser.ParseIsoDateTime(timeZoneString, TemporalParser.AnyGoal);
        var timeZoneResult = result.TimeZone;
        if (timeZoneResult.TimeZoneAnnotation is { } annotation)
            return ParseTimeZoneIdentifier(annotation);

        if (timeZoneResult.Z)
            return new TimeZoneIdentifierParseResult("UTC", 0);

        if (timeZoneResult.OffsetString is { } offsetString)
            return ParseTimeZoneIdentifier(offsetString);

        return ThrowHelper.ThrowRangeError<TimeZoneIdentifierParseResult>("The string '" + timeZoneString + "' does not contain a time zone");
    }

    /// <summary>ParseTimeZoneIdentifier(identifier)</summary>
    public static TimeZoneIdentifierParseResult ParseTimeZoneIdentifier(string identifier)
    {
        if (!TemporalParser.TryParseTimeZoneIdentifier(identifier, out var result))
            return ThrowHelper.ThrowRangeError<TimeZoneIdentifierParseResult>("The time zone identifier '" + identifier + "' is not valid");

        return result;
    }

    /// <summary>TimeZoneEquals(one, two)</summary>
    public static bool TimeZoneEquals(TemporalTimeZone one, TemporalTimeZone two)
    {
        if (ReferenceEquals(one, two) || one.Identifier == two.Identifier)
            return true;

        if (!one.IsOffset && !two.IsOffset)
            return string.Equals(one.PrimaryIdentifier, two.PrimaryIdentifier, StringComparison.OrdinalIgnoreCase);

        return false;
    }

    /// <summary>GetOffsetNanosecondsFor(timeZone, epochNs)</summary>
    public static long GetOffsetNanosecondsFor(TemporalTimeZone timeZone, Int128 epochNs)
    {
        if (timeZone.Zone is not { } zone)
            return timeZone.OffsetMinutes * 60_000_000_000L;

        return zone.GetOffsetNanoseconds(epochNs);
    }

    /// <summary>GetISOPartsFromEpoch(epochNanoseconds) with an offset added (GetISODateTimeFor).</summary>
    public static IsoDateTime EpochNanosecondsToIsoDateTime(Int128 localNanoseconds)
    {
        var days = TemporalMath.FloorDivide(localNanoseconds, TemporalMath.NsPerDay);
        var timeNs = (long)(localNanoseconds - (days * TemporalMath.NsPerDay));
        var date = EpochDaysToIsoDate((long)days);
        return new IsoDateTime(date, NanosecondsToTimeRecord(timeNs, 0));
    }

    /// <summary>GetISODateTimeFor(timeZone, epochNs)</summary>
    public static IsoDateTime GetIsoDateTimeFor(TemporalTimeZone timeZone, Int128 epochNs)
    {
        var offsetNanoseconds = GetOffsetNanosecondsFor(timeZone, epochNs);
        return EpochNanosecondsToIsoDateTime(epochNs + offsetNanoseconds);
    }

    /// <summary>GetEpochNanosecondsFor(timeZone, isoDateTime, disambiguation)</summary>
    public static Int128 GetEpochNanosecondsFor(TemporalTimeZone timeZone, IsoDateTime isoDateTime, TemporalDisambiguation disambiguation)
    {
        var possibleEpochNs = GetPossibleEpochNanoseconds(timeZone, isoDateTime);
        return DisambiguatePossibleEpochNanoseconds(possibleEpochNs, timeZone, isoDateTime, disambiguation);
    }

    /// <summary>DisambiguatePossibleEpochNanoseconds(possibleEpochNs, timeZone, isoDateTime, disambiguation)</summary>
    public static Int128 DisambiguatePossibleEpochNanoseconds(PossibleEpochNanoseconds possibleEpochNs, TemporalTimeZone timeZone, IsoDateTime isoDateTime, TemporalDisambiguation disambiguation)
    {
        var n = possibleEpochNs.Count;
        if (n == 1)
            return possibleEpochNs[0];

        if (n != 0)
        {
            return disambiguation switch
            {
                TemporalDisambiguation.Earlier or TemporalDisambiguation.Compatible => possibleEpochNs[0],
                TemporalDisambiguation.Later => possibleEpochNs[n - 1],
                _ => ThrowHelper.ThrowRangeError<Int128>("The local date and time is ambiguous in the time zone " + timeZone.Identifier),
            };
        }

        if (disambiguation == TemporalDisambiguation.Reject)
            return ThrowHelper.ThrowRangeError<Int128>("The local date and time does not exist in the time zone " + timeZone.Identifier);

        // The offsets before and after the transition that skips the local time
        var utcNs = GetUtcEpochNanoseconds(isoDateTime);
        var offsetBefore = GetOffsetNanosecondsFor(timeZone, utcNs - TemporalMath.NsPerDay);
        var offsetAfter = GetOffsetNanosecondsFor(timeZone, utcNs + TemporalMath.NsPerDay);
        var nanoseconds = offsetAfter - offsetBefore;
        if (disambiguation == TemporalDisambiguation.Earlier)
        {
            var earlierTime = AddTime(isoDateTime.Time, -nanoseconds);
            var earlierDate = AddDaysToIsoDate(isoDateTime.Date, earlierTime.Days);
            var earlier = GetPossibleEpochNanoseconds(timeZone, new IsoDateTime(earlierDate, earlierTime.WithoutDays()));
            if (earlier.Count == 0)
                return ThrowHelper.ThrowRangeError<Int128>("The local date and time does not exist in the time zone " + timeZone.Identifier);

            return earlier[0];
        }

        var laterTime = AddTime(isoDateTime.Time, nanoseconds);
        var laterDate = AddDaysToIsoDate(isoDateTime.Date, laterTime.Days);
        var later = GetPossibleEpochNanoseconds(timeZone, new IsoDateTime(laterDate, laterTime.WithoutDays()));
        if (later.Count == 0)
            return ThrowHelper.ThrowRangeError<Int128>("The local date and time does not exist in the time zone " + timeZone.Identifier);

        return later[later.Count - 1];
    }

    /// <summary>GetPossibleEpochNanoseconds(timeZone, isoDateTime)</summary>
    public static PossibleEpochNanoseconds GetPossibleEpochNanoseconds(TemporalTimeZone timeZone, IsoDateTime isoDateTime)
    {
        PossibleEpochNanoseconds result;
        if (timeZone.Zone is not { } zone)
        {
            var localNs = GetUtcEpochNanoseconds(isoDateTime);
            var epochNs = localNs - (timeZone.OffsetMinutes * NsPerMinute);
            var balanced = EpochNanosecondsToIsoDateTime(epochNs);
            CheckIsoDaysRange(balanced.Date);
            result = new PossibleEpochNanoseconds(1, epochNs, 0);
        }
        else
        {
            result = GetNamedTimeZoneEpochNanoseconds(zone, isoDateTime);
        }

        for (var i = 0; i < result.Count; i++)
        {
            if (!IsValidEpochNanoseconds(result[i]))
                return ThrowHelper.ThrowRangeError<PossibleEpochNanoseconds>("The date and time is outside the range of Temporal");
        }

        return result;
    }

    /// <summary>GetNamedTimeZoneEpochNanoseconds(timeZoneIdentifier, isoDateTime)</summary>
    private static PossibleEpochNanoseconds GetNamedTimeZoneEpochNanoseconds(NamedTimeZone zone, IsoDateTime isoDateTime)
    {
        var localNs = GetUtcEpochNanoseconds(isoDateTime);
        var localSeconds = TemporalMath.FloorDivide(localNs, 1_000_000_000);
        var subSecond = localNs - (localSeconds * 1_000_000_000);

        // Beyond the range of the explicit transitions and rules, the arithmetic of the zone is not needed: such dates are rejected by the callers
        if (localSeconds > long.MaxValue / 2 || localSeconds < long.MinValue / 2)
            return ThrowHelper.ThrowRangeError<PossibleEpochNanoseconds>("The date and time is outside the range of Temporal");

        Span<long> instants = stackalloc long[2];
        var count = zone.GetPossibleEpochSeconds((long)localSeconds, instants);
        return count switch
        {
            0 => default,
            1 => new PossibleEpochNanoseconds(1, ((Int128)instants[0] * 1_000_000_000) + subSecond, 0),
            _ => new PossibleEpochNanoseconds(2, ((Int128)instants[0] * 1_000_000_000) + subSecond, ((Int128)instants[1] * 1_000_000_000) + subSecond),
        };
    }

    /// <summary>GetStartOfDay(timeZone, isoDate)</summary>
    public static Int128 GetStartOfDay(TemporalTimeZone timeZone, IsoDate isoDate)
    {
        var isoDateTime = new IsoDateTime(isoDate, TimeRecord.Midnight);
        var possibleEpochNs = GetPossibleEpochNanoseconds(timeZone, isoDateTime);
        if (possibleEpochNs.Count != 0)
            return possibleEpochNs[0];

        // Midnight is skipped by a transition: the start of the day is the transition
        var zone = timeZone.Zone!;
        var localNs = GetUtcEpochNanoseconds(isoDateTime);
        Int128? current = localNs - (2 * TemporalMath.NsPerDay);
        for (var i = 0; i < 16 && current is { } after; i++)
        {
            var transition = zone.GetNextTransition(after);
            if (transition is not { } t)
                break;

            var offsetBefore = zone.GetOffsetNanoseconds(t - 1);
            var offsetAfter = zone.GetOffsetNanoseconds(t);
            if (t + offsetBefore <= localNs && localNs < t + offsetAfter)
            {
                if (!IsValidEpochNanoseconds(t))
                    return ThrowHelper.ThrowRangeError<Int128>("The date and time is outside the range of Temporal");

                return t;
            }

            current = t;
        }

        return ThrowHelper.ThrowRangeError<Int128>("Cannot find the start of the day in the time zone " + timeZone.Identifier);
    }

    /// <summary>GetNamedTimeZoneNextTransition(timeZoneIdentifier, epochNanoseconds)</summary>
    public static Int128? GetNamedTimeZoneNextTransition(NamedTimeZone zone, Int128 epochNanoseconds)
    {
        var result = zone.GetNextTransition(epochNanoseconds);
        if (result is { } value && value > TemporalMath.NsMaxInstant)
            return null;

        return result;
    }

    /// <summary>GetNamedTimeZonePreviousTransition(timeZoneIdentifier, epochNanoseconds)</summary>
    public static Int128? GetNamedTimeZonePreviousTransition(NamedTimeZone zone, Int128 epochNanoseconds)
    {
        var result = zone.GetPreviousTransition(epochNanoseconds);
        if (result is { } value && value < TemporalMath.NsMinInstant)
            return null;

        return result;
    }

    /// <summary>FormatOffsetTimeZoneIdentifier(offsetMinutes, style)</summary>
    public static string FormatOffsetTimeZoneIdentifier(int offsetMinutes, bool separated = true)
    {
        var sign = offsetMinutes >= 0 ? '+' : '-';
        var absoluteMinutes = Math.Abs(offsetMinutes);
        var hour = absoluteMinutes / 60;
        var minute = absoluteMinutes % 60;
        return sign + FormatTimeString(hour, minute, 0, 0, PrecisionMinute, separated);
    }

    /// <summary>FormatUTCOffsetNanoseconds(offsetNanoseconds)</summary>
    public static string FormatUtcOffsetNanoseconds(long offsetNanoseconds)
    {
        var sign = offsetNanoseconds >= 0 ? '+' : '-';
        var absoluteNanoseconds = Math.Abs(offsetNanoseconds);
        var hour = (int)(absoluteNanoseconds / 3_600_000_000_000);
        var minute = (int)(absoluteNanoseconds / 60_000_000_000 % 60);
        var second = (int)(absoluteNanoseconds / 1_000_000_000 % 60);
        var subSecondNanoseconds = (int)(absoluteNanoseconds % 1_000_000_000);
        var precision = second == 0 && subSecondNanoseconds == 0 ? PrecisionMinute : PrecisionAuto;
        return sign + FormatTimeString(hour, minute, second, subSecondNanoseconds, precision);
    }

    /// <summary>FormatDateTimeUTCOffsetRounded(offsetNanoseconds)</summary>
    public static string FormatDateTimeUtcOffsetRounded(long offsetNanoseconds)
    {
        var rounded = TemporalMath.RoundNumberToIncrement(offsetNanoseconds, 60_000_000_000, TemporalRoundingMode.HalfExpand);
        return FormatOffsetTimeZoneIdentifier((int)(rounded / 60_000_000_000));
    }

    /// <summary>ParseDateTimeUTCOffset(offsetString)</summary>
    public static long ParseDateTimeUtcOffset(string offsetString)
    {
        if (!TemporalParser.TryParseDateTimeUtcOffset(offsetString, out var result, out _))
            return ThrowHelper.ThrowRangeError<long>("The UTC offset '" + offsetString + "' is not valid");

        return result;
    }

    /// <summary>ToOffsetString(argument)</summary>
    public static string ToOffsetString(JsValue argument)
    {
        var offset = JsOperations.ToPrimitive(argument, JsOperations.PreferredType.String);
        if (offset.Cell is not JsString str)
            return ThrowHelper.ThrowTypeError<string>("The offset must be a string");

        var text = str.ToString();
        ParseDateTimeUtcOffset(text);
        return text;
    }
}
