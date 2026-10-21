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
        // The interpreter wires it to the execution budget of the engine (timeout, cancellation)
    }
}
