namespace Meziantou.Framework.JavaScriptEngine;

public sealed partial class JsEngine
{
    /// <summary>
    /// Called periodically by the regular expression matcher (every few tens of thousands of steps), so that a
    /// pathological pattern cannot run forever: it throws a <see cref="JsTerminationException"/> when the execution
    /// limits of the engine are exceeded.
    /// </summary>
    /// <param name="matchStart">The timestamp of the start of the match (<see cref="System.Diagnostics.Stopwatch.GetTimestamp"/>), for <see cref="JsEngineOptions.RegExpTimeout"/>.</param>
    internal void CheckRegExpBudget(long matchStart)
    {
        CheckLimits();
        if (Options.RegExpTimeout is { } timeout && matchStart != 0 && System.Diagnostics.Stopwatch.GetElapsedTime(matchStart) > timeout)
            throw new JsTerminationException(JsTerminationReason.Timeout, "The regular expression exceeded its time limit");
    }
}
