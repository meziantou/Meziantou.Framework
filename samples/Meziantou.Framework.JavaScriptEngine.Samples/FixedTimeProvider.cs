namespace Meziantou.Framework.JavaScriptEngine.Samples;

#region JavaScriptEngine_FixedTimeProvider
/// <summary>A clock that always gives the same time: <c>Date.now()</c>, <c>new Date()</c> and <c>Temporal.Now</c> use it.</summary>
public sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => utcNow;
}
#endregion
