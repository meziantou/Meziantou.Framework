using System.Globalization;
using System.Runtime.InteropServices;

namespace Meziantou.Framework;

[StructLayout(LayoutKind.Auto)]
public readonly struct RelativeDate : IComparable, IComparable<RelativeDate>, IEquatable<RelativeDate>, IFormattable
{
    private TimeProvider TimeProvider { get; }
    private DateTime DateTime { get; }

    public RelativeDate(DateTime dateTime, TimeProvider? timeProvider)
    {
        if (dateTime.Kind == DateTimeKind.Unspecified)
            throw new ArgumentException("Cannot determine if the argument is a local datetime or UTC datetime", nameof(dateTime));

        DateTime = dateTime.ToUniversalTime();
        TimeProvider = timeProvider ?? TimeProvider.System;
    }

    public RelativeDate(DateTime dateTime)
        : this(dateTime, timeProvider: null)
    {
    }

    public static RelativeDate Get(DateTime dateTime) => new(dateTime);

    public static RelativeDate Get(DateTimeOffset dateTime) => new(dateTime.UtcDateTime);

    public static RelativeDate Get(DateTime dateTime, TimeProvider? timeProvider) => new(dateTime, timeProvider);

    public static RelativeDate Get(DateTimeOffset dateTime, TimeProvider? timeProvider) => new(dateTime.UtcDateTime, timeProvider);

    public override string ToString() => ToString(format: null, formatProvider: null);

    public string ToString(string? format, IFormatProvider? formatProvider)
    {
        var now = TimeProvider.GetUtcNow().UtcDateTime;

        var delta = now - DateTime;
        var culture = formatProvider as CultureInfo;

        if (delta < TimeSpan.Zero)
        {
            delta = -delta;
            if (delta < TimeSpan.FromMinutes(1))
            {
                return delta.Seconds <= 1 ?
                    GetString("InOneSecond", culture) :
                    GetString("InManySeconds", culture, delta.Seconds);
            }

            if (delta < TimeSpan.FromMinutes(2))
                return GetString("InAMinute", culture);

            if (delta < TimeSpan.FromMinutes(45))
                return GetString("InManyMinutes", culture, delta.Minutes);

            if (delta < TimeSpan.FromMinutes(90))
                return GetString("InAnHour", culture);

            if (delta < TimeSpan.FromHours(24))
                return GetString("InManyHours", culture, delta.Hours);

            if (delta < TimeSpan.FromHours(48))
                return GetString("Tomorrow", culture);

            if (delta < TimeSpan.FromDays(30))
                return GetString("InManyDays", culture, delta.Days);

            if (delta < TimeSpan.FromDays(365)) // We don't care about leap year
            {
                var months = Convert.ToInt32(Math.Floor((double)delta.Days / 30));
                return months <= 1 ?
                    GetString("InOneMonth", culture) :
                    GetString("InManyMonths", culture, months);
            }
            else
            {
                var years = Convert.ToInt32(Math.Floor((double)delta.Days / 365));
                return years <= 1 ?
                    GetString("InOneYear", culture) :
                    GetString("InManyYears", culture, years);
            }
        }

        if (delta == TimeSpan.Zero)
            return GetString("Now", culture);

        if (delta < TimeSpan.FromMinutes(1))
        {
            return delta.Seconds <= 1 ?
                GetString("OneSecondAgo", culture) :
                GetString("ManySecondsAgo", culture, delta.Seconds);
        }

        if (delta < TimeSpan.FromMinutes(2))
            return GetString("AMinuteAgo", culture);

        if (delta < TimeSpan.FromMinutes(45))
            return GetString("ManyMinutesAgo", culture, delta.Minutes);

        if (delta < TimeSpan.FromMinutes(90))
            return GetString("AnHourAgo", culture);

        if (delta < TimeSpan.FromHours(24))
            return GetString("ManyHoursAgo", culture, delta.Hours);

        if (delta < TimeSpan.FromHours(48))
            return GetString("Yesterday", culture);

        if (delta < TimeSpan.FromDays(30))
            return GetString("ManyDaysAgo", culture, delta.Days);

        if (delta < TimeSpan.FromDays(365)) // We don't care about leap year
        {
            var months = Convert.ToInt32(Math.Floor((double)delta.Days / 30));
            return months <= 1 ?
                GetString("OneMonthAgo", culture) :
                GetString("ManyMonthsAgo", culture, months);
        }
        else
        {
            var years = Convert.ToInt32(Math.Floor((double)delta.Days / 365));
            return years <= 1 ?
                GetString("OneYearAgo", culture) :
                GetString("ManyYearsAgo", culture, years);
        }
    }

    private static string GetString(string name, CultureInfo? culture) => LocalizationProvider.Current.GetString(name, culture);

    private static string GetString(string name, CultureInfo? culture, int value) => string.Format(culture, LocalizationProvider.Current.GetString(name, culture), value);

    int IComparable.CompareTo(object? obj)
    {
        if (obj is RelativeDate rd)
            return CompareTo(rd);

        return CompareTo(default);
    }

    public int CompareTo(RelativeDate other) => DateTime.CompareTo(other.DateTime);

    public override bool Equals(object? obj) => obj is RelativeDate date && Equals(date);

    public bool Equals(RelativeDate other) => DateTime == other.DateTime;

    public override int GetHashCode() => -10323184 + DateTime.GetHashCode();

    public static bool operator ==(RelativeDate date1, RelativeDate date2) => date1.Equals(date2);
    public static bool operator !=(RelativeDate date1, RelativeDate date2) => !(date1 == date2);
    public static bool operator <(RelativeDate left, RelativeDate right) => left.CompareTo(right) < 0;
    public static bool operator <=(RelativeDate left, RelativeDate right) => left.CompareTo(right) <= 0;
    public static bool operator >(RelativeDate left, RelativeDate right) => left.CompareTo(right) > 0;
    public static bool operator >=(RelativeDate left, RelativeDate right) => left.CompareTo(right) >= 0;
}
