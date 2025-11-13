using System.Runtime.InteropServices;

namespace Meziantou.Framework;

/// <summary>Represents a date and time that can be formatted as a human-readable relative time string (e.g., "2 hours ago", "in 3 days").</summary>
/// <example>
/// <code>
/// // Basic usage
/// var pastDate = RelativeDate.Get(DateTime.UtcNow.AddHours(-2));
/// Console.WriteLine(pastDate); // "2 hours ago"
/// 
/// var futureDate = RelativeDate.Get(DateTime.UtcNow.AddDays(3));
/// Console.WriteLine(futureDate); // "in 3 days"
/// 
/// // With localization
/// var date = RelativeDate.Get(DateTime.UtcNow.AddMinutes(-30));
/// Console.WriteLine(date.ToString(null, CultureInfo.GetCultureInfo("fr"))); // "il y a 30 minutes"
/// Console.WriteLine(date.ToString(null, CultureInfo.InvariantCulture)); // "30 minutes ago"
/// 
/// // With custom TimeProvider for testing
/// var fakeTimeProvider = new FakeTimeProvider();
/// fakeTimeProvider.SetUtcNow(new DateTimeOffset(2024, 1, 1, 12, 0, 0, TimeSpan.Zero));
/// var testDate = RelativeDate.Get(new DateTime(2024, 1, 1, 10, 0, 0, DateTimeKind.Utc), fakeTimeProvider);
/// Console.WriteLine(testDate); // "2 hours ago"
/// </code>
/// </example>
/// <remarks>
/// This type provides localized relative date/time strings for multiple languages including English and French.
/// The default localization can be customized by setting <see cref="LocalizationProvider.Current"/>.
/// </remarks>
[StructLayout(LayoutKind.Auto)]
public readonly struct RelativeDate : IComparable, IComparable<RelativeDate>, IEquatable<RelativeDate>, IFormattable
{
    private TimeProvider TimeProvider { get; }
    private DateTime DateTime { get; }

    /// <summary>Initializes a new instance of the <see cref="RelativeDate"/> struct with the specified date and time provider.</summary>
    /// <param name="dateTime">The date and time. Must have <see cref="DateTimeKind.Local"/> or <see cref="DateTimeKind.Utc"/>.</param>
    /// <param name="timeProvider">The time provider to use for calculating the relative time. If <see langword="null"/>, <see cref="TimeProvider.System"/> is used.</param>
    /// <exception cref="ArgumentException"><paramref name="dateTime"/> has <see cref="DateTimeKind.Unspecified"/>.</exception>
    public RelativeDate(DateTime dateTime, TimeProvider? timeProvider)
    {
        if (dateTime.Kind == DateTimeKind.Unspecified)
            throw new ArgumentException("Cannot determine if the argument is a local datetime or UTC datetime", nameof(dateTime));

        DateTime = dateTime.ToUniversalTime();
        TimeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <summary>Initializes a new instance of the <see cref="RelativeDate"/> struct with the specified date and system time provider.</summary>
    /// <param name="dateTime">The date and time. Must have <see cref="DateTimeKind.Local"/> or <see cref="DateTimeKind.Utc"/>.</param>
    /// <exception cref="ArgumentException"><paramref name="dateTime"/> has <see cref="DateTimeKind.Unspecified"/>.</exception>
    public RelativeDate(DateTime dateTime)
        : this(dateTime, timeProvider: null)
    {
    }

    /// <summary>Creates a <see cref="RelativeDate"/> from a <see cref="DateTime"/> using the system time provider.</summary>
    /// <param name="dateTime">The date and time. Must have <see cref="DateTimeKind.Local"/> or <see cref="DateTimeKind.Utc"/>.</param>
    /// <returns>A new <see cref="RelativeDate"/> instance.</returns>
    /// <exception cref="ArgumentException"><paramref name="dateTime"/> has <see cref="DateTimeKind.Unspecified"/>.</exception>
    public static RelativeDate Get(DateTime dateTime) => new(dateTime);

    /// <summary>Creates a <see cref="RelativeDate"/> from a <see cref="DateTimeOffset"/> using the system time provider.</summary>
    /// <param name="dateTime">The date and time offset.</param>
    /// <returns>A new <see cref="RelativeDate"/> instance.</returns>
    public static RelativeDate Get(DateTimeOffset dateTime) => new(dateTime.UtcDateTime);

    /// <summary>Creates a <see cref="RelativeDate"/> from a <see cref="DateTime"/> with a custom time provider.</summary>
    /// <param name="dateTime">The date and time. Must have <see cref="DateTimeKind.Local"/> or <see cref="DateTimeKind.Utc"/>.</param>
    /// <param name="timeProvider">The time provider to use for calculating the relative time. If <see langword="null"/>, <see cref="TimeProvider.System"/> is used.</param>
    /// <returns>A new <see cref="RelativeDate"/> instance.</returns>
    /// <exception cref="ArgumentException"><paramref name="dateTime"/> has <see cref="DateTimeKind.Unspecified"/>.</exception>
    public static RelativeDate Get(DateTime dateTime, TimeProvider? timeProvider) => new(dateTime, timeProvider);

    /// <summary>Creates a <see cref="RelativeDate"/> from a <see cref="DateTimeOffset"/> with a custom time provider.</summary>
    /// <param name="dateTime">The date and time offset.</param>
    /// <param name="timeProvider">The time provider to use for calculating the relative time. If <see langword="null"/>, <see cref="TimeProvider.System"/> is used.</param>
    /// <returns>A new <see cref="RelativeDate"/> instance.</returns>
    public static RelativeDate Get(DateTimeOffset dateTime, TimeProvider? timeProvider) => new(dateTime.UtcDateTime, timeProvider);

    /// <summary>Returns a localized string representation of the relative date (e.g., "2 hours ago", "in 3 days").</summary>
    /// <returns>A localized relative date string.</returns>
    public override string ToString() => ToString(format: null, formatProvider: null);

    /// <summary>Formats the relative date using the specified format and culture.</summary>
    /// <param name="format">The format string (currently not used).</param>
    /// <param name="formatProvider">An <see cref="IFormatProvider"/> that supplies culture-specific formatting information, typically a <see cref="CultureInfo"/>.</param>
    /// <returns>A localized relative date string.</returns>
    /// <remarks>
    /// The method returns strings like:
    /// <list type="bullet">
    /// <item><description>"now" - for the current moment</description></item>
    /// <item><description>"one second ago" / "in one second" - for ±1 second</description></item>
    /// <item><description>"X seconds ago" / "in X seconds" - for &lt;1 minute</description></item>
    /// <item><description>"a minute ago" / "in a minute" - for 1-2 minutes</description></item>
    /// <item><description>"X minutes ago" / "in X minutes" - for &lt;45 minutes</description></item>
    /// <item><description>"an hour ago" / "in an hour" - for 45-90 minutes</description></item>
    /// <item><description>"X hours ago" / "in X hours" - for &lt;24 hours</description></item>
    /// <item><description>"yesterday" / "tomorrow" - for 24-48 hours</description></item>
    /// <item><description>"X days ago" / "in X days" - for &lt;30 days</description></item>
    /// <item><description>"one month ago" / "in one month" - for 30-60 days</description></item>
    /// <item><description>"X months ago" / "in X months" - for &lt;365 days</description></item>
    /// <item><description>"one year ago" / "in one year" - for 365-730 days</description></item>
    /// <item><description>"X years ago" / "in X years" - for ≥730 days</description></item>
    /// </list>
    /// </remarks>
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

    /// <summary>Compares this instance to another <see cref="RelativeDate"/> and returns an integer that indicates their relative order.</summary>
    /// <param name="other">The <see cref="RelativeDate"/> to compare with this instance.</param>
    /// <returns>
    /// A value less than zero if this instance is earlier than <paramref name="other"/>;
    /// zero if they represent the same date and time;
    /// greater than zero if this instance is later than <paramref name="other"/>.
    /// </returns>
    public int CompareTo(RelativeDate other) => DateTime.CompareTo(other.DateTime);

    public override bool Equals(object? obj) => obj is RelativeDate date && Equals(date);

    /// <summary>Determines whether the specified <see cref="RelativeDate"/> is equal to the current instance.</summary>
    public bool Equals(RelativeDate other) => DateTime == other.DateTime;

    public override int GetHashCode() => -10323184 + DateTime.GetHashCode();

    /// <summary>Determines whether two <see cref="RelativeDate"/> instances are equal.</summary>
    public static bool operator ==(RelativeDate date1, RelativeDate date2) => date1.Equals(date2);

    /// <summary>Determines whether two <see cref="RelativeDate"/> instances are not equal.</summary>
    public static bool operator !=(RelativeDate date1, RelativeDate date2) => !(date1 == date2);

    /// <summary>Determines whether one <see cref="RelativeDate"/> is less than another.</summary>
    public static bool operator <(RelativeDate left, RelativeDate right) => left.CompareTo(right) < 0;

    /// <summary>Determines whether one <see cref="RelativeDate"/> is less than or equal to another.</summary>
    public static bool operator <=(RelativeDate left, RelativeDate right) => left.CompareTo(right) <= 0;

    /// <summary>Determines whether one <see cref="RelativeDate"/> is greater than another.</summary>
    public static bool operator >(RelativeDate left, RelativeDate right) => left.CompareTo(right) > 0;

    /// <summary>Determines whether one <see cref="RelativeDate"/> is greater than or equal to another.</summary>
    public static bool operator >=(RelativeDate left, RelativeDate right) => left.CompareTo(right) >= 0;
}
