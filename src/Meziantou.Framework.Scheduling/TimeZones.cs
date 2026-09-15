namespace Meziantou.Framework.Scheduling;

/// <summary>Resolves the time zones referenced by an iCalendar TZID parameter.</summary>
/// <remarks>
/// <para>On .NET 6 and later both IANA identifiers (<c>America/New_York</c>) and Windows identifiers
/// (<c>Eastern Standard Time</c>) are accepted on every platform.</para>
/// <para>On .NET Framework the runtime provides no conversion between the two, so an IANA identifier
/// does not resolve on Windows. Those callers should resolve the <see cref="TimeZoneInfo"/> themselves
/// and use the overloads that take one.</para>
/// </remarks>
internal static class TimeZones
{
    /// <summary>Finds the time zone with the specified identifier.</summary>
    /// <param name="timeZoneId">The time zone identifier, as written in a TZID property parameter.</param>
    /// <returns>The time zone matching <paramref name="timeZoneId"/>.</returns>
    /// <exception cref="TimeZoneNotFoundException">No time zone matches <paramref name="timeZoneId"/>.</exception>
    public static TimeZoneInfo Find(string timeZoneId)
    {
        ArgumentNullException.ThrowIfNull(timeZoneId);

        if (TryFind(timeZoneId, out var timeZone))
            return timeZone;

        throw new TimeZoneNotFoundException($"The time zone '{timeZoneId}' was not found");
    }

    /// <summary>Attempts to find the time zone with the specified identifier.</summary>
    /// <param name="timeZoneId">The time zone identifier, as written in a TZID property parameter.</param>
    /// <param name="timeZone">When successful, contains the time zone matching <paramref name="timeZoneId"/>.</param>
    /// <returns><see langword="true"/> if the time zone was found; otherwise, <see langword="false"/>.</returns>
    public static bool TryFind(string timeZoneId, [NotNullWhen(returnValue: true)] out TimeZoneInfo? timeZone)
    {
        ArgumentNullException.ThrowIfNull(timeZoneId);

#if NET6_0_OR_GREATER
        return TimeZoneInfo.TryFindSystemTimeZoneById(timeZoneId, out timeZone);
#else
        // TimeZoneInfo.TryFindSystemTimeZoneById does not exist before .NET 6.
        try
        {
            timeZone = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
            return true;
        }
        catch (TimeZoneNotFoundException)
        {
            timeZone = null;
            return false;
        }
        catch (InvalidTimeZoneException)
        {
            timeZone = null;
            return false;
        }
#endif
    }

    /// <summary>Attempts to find the time zone with the specified identifier, reporting any failure to read the time zone database as not found.</summary>
    /// <param name="timeZoneId">The time zone identifier, as written in a TZID property parameter.</param>
    /// <param name="timeZone">When successful, contains the time zone matching <paramref name="timeZoneId"/>.</param>
    /// <returns><see langword="true"/> if the time zone was found; otherwise, <see langword="false"/>.</returns>
    /// <remarks>
    /// Parsing a calendar must not throw from a Try method: on .NET Framework, reading the registry can fail with a
    /// <see cref="System.Security.SecurityException"/>, an <see cref="UnauthorizedAccessException"/> or an <see cref="IOException"/>,
    /// and an identifier the platform cannot even look up fails with an <see cref="ArgumentException"/>.
    /// </remarks>
    public static bool TryFindWithoutThrowing(string timeZoneId, [NotNullWhen(returnValue: true)] out TimeZoneInfo? timeZone)
    {
        try
        {
            return TryFind(timeZoneId, out timeZone);
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException or ArgumentException or InvalidTimeZoneException or TimeZoneNotFoundException)
        {
            timeZone = null;
            return false;
        }
    }
}
