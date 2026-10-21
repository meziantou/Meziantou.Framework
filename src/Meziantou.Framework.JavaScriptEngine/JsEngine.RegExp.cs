namespace Meziantou.Framework.JavaScriptEngine;

public sealed partial class JsEngine
{
    /// <summary>
    /// Called periodically by the regular expression matcher (every few tens of thousands of steps), so that a
    /// pathological pattern cannot run forever: it throws a <see cref="JsTerminationException"/> when the execution
    /// limits of the engine are exceeded.
    /// </summary>
    internal void CheckRegExpBudget()
    {
        var options = Options;
        if (options.CancellationToken.IsCancellationRequested)
            throw new JsTerminationException(JsTerminationReason.Canceled, "The script was canceled");

        if (options.Timeout is { } timeout && IsScriptRunning && System.Diagnostics.Stopwatch.GetElapsedTime(_executionStart) > timeout)
            throw new JsTerminationException(JsTerminationReason.Timeout, "The script exceeded its time limit");
    }
}
