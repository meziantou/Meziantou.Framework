namespace Meziantou.Framework.JavaScriptEngine;

/// <summary>
/// The limits of an execution scope (<see cref="JsEngine.EnterExecutionScope"/>): they apply to all the code that runs
/// in the scope as a whole (the scripts, the calls, the jobs, the parsing of the sources), in addition to the limits of
/// <see cref="JsEngineOptions"/>. A limit that is exceeded stops the code with a <see cref="JsTerminationException"/>.
/// </summary>
public sealed class JsExecutionLimits
{
    internal static JsExecutionLimits None { get; } = new();

    /// <summary>Gets the maximum duration of the scope, or null for no limit.</summary>
    public TimeSpan? Timeout { get; init; }

    /// <summary>
    /// Gets the maximum number of bytes allocated by the thread in the scope, or null for no limit (see
    /// <see cref="JsEngineOptions.MemoryLimit"/>).
    /// </summary>
    public long? MemoryLimit { get; init; }

    /// <summary>Gets the maximum number of statements of the scope, or null for no limit (see <see cref="JsEngineOptions.MaxStatements"/>).</summary>
    public long? MaxStatements { get; init; }

    /// <summary>
    /// Gets the token that stops the code of the scope when it is canceled (<see cref="JsTerminationReason.Canceled"/>).
    /// It can be canceled from any thread, e.g. to stop a request whose client is gone.
    /// </summary>
    public CancellationToken CancellationToken { get; init; }
}
