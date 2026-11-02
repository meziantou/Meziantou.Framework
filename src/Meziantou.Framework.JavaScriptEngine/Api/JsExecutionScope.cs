namespace Meziantou.Framework.JavaScriptEngine;

/// <summary>
/// The scope returned by <see cref="JsEngine.EnterExecutionScope"/>: the calls made until it is disposed are one entry
/// into JavaScript, bounded by its <see cref="JsExecutionLimits"/>. Dispose the scopes in the reverse order they were
/// entered, on the thread that entered them.
/// </summary>
public sealed class JsExecutionScope : IDisposable
{
    private readonly JsEngine _engine;
    private bool _disposed;

    internal JsExecutionScope(JsEngine engine, JsEngine? previousEngine, JsExecutionLimits limits, long startTimestamp, long startCounter, long startAllocatedBytes)
    {
        _engine = engine;
        PreviousEngine = previousEngine;
        Limits = limits;
        StartTimestamp = startTimestamp;
        StartCounter = startCounter;
        StartAllocatedBytes = startAllocatedBytes;
    }

    /// <summary>Gets the limits of the scope.</summary>
    public JsExecutionLimits Limits { get; }

    internal JsEngine? PreviousEngine { get; }

    internal long StartTimestamp { get; }

    internal long StartCounter { get; }

    internal long StartAllocatedBytes { get; }

    /// <summary>Ends the scope.</summary>
    /// <exception cref="InvalidOperationException">An inner scope is not disposed yet.</exception>
    public void Dispose()
    {
        if (_disposed)
            return;

        _engine.ExitExecutionScope(this);
        _disposed = true;
    }
}
