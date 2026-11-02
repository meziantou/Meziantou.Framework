namespace Meziantou.Framework.JavaScriptEngine.Samples;

#region JavaScriptEngine_FixedTimeProvider
/// <summary>
/// A clock that always gives the same time: <c>Date.now()</c>, <c>new Date()</c> and <c>Temporal.Now</c> use it. The local
/// time zone is the one of the machine, unless one is given.
/// </summary>
public sealed class FixedTimeProvider(DateTimeOffset utcNow, TimeZoneInfo? localTimeZone = null) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => utcNow;

    public override TimeZoneInfo LocalTimeZone => localTimeZone ?? base.LocalTimeZone;
}
#endregion
