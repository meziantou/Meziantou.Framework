using Meziantou.Framework.JavaScriptEngine.TimeZones;

namespace Meziantou.Framework.JavaScriptEngine;

public sealed partial class JsEngine
{
    private TimeZoneIdentifier? _systemTimeZone;

    /// <summary>Gets the time zone of the local time (SystemTimeZoneIdentifier), resolved on first use from the options.</summary>
    internal TimeZoneIdentifier SystemTimeZone => _systemTimeZone ??= ResolveSystemTimeZone();

    /// <summary>Gets the current time, in milliseconds since the epoch (the time value of <c>Date.now()</c>).</summary>
    internal double GetCurrentTimeValue() => Options.TimeProvider.GetUtcNow().ToUnixTimeMilliseconds();

    private TimeZoneIdentifier ResolveSystemTimeZone()
    {
        var database = TimeZoneDatabase.Default;
        TimeZoneIdentifier result;
        if (Options.TimeZone is { } identifier)
        {
            if (database.TryGetTimeZone(identifier, out result))
                return result;

            throw new InvalidOperationException($"The time zone '{identifier}' of {nameof(JsEngineOptions)}.{nameof(JsEngineOptions.TimeZone)} is not a time zone of the IANA time zone database");
        }

        var localId = Options.TimeProvider.LocalTimeZone.Id;
        if (database.TryGetTimeZone(localId, out result))
            return result;

        if (database.TryGetIanaIdFromWindowsId(localId, out var ianaId) && database.TryGetTimeZone(ianaId, out result))
            return result;

        return database.TryGetTimeZone("UTC", out result) ? result : throw new InvalidOperationException("The time zone database does not contain UTC");
    }
}
