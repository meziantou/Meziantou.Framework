namespace Meziantou.Framework.JavaScriptEngine;

/// <summary>Why the engine stopped a script.</summary>
public enum JsTerminationReason
{
    /// <summary>The execution was canceled (the cancellation token of the engine).</summary>
    Canceled,

    /// <summary>The script ran longer than <see cref="JsEngineOptions.Timeout"/>.</summary>
    Timeout,

    /// <summary>The script allocated more than <see cref="JsEngineOptions.MemoryLimit"/>.</summary>
    MemoryLimit,

    /// <summary>The script executed more statements than <see cref="JsEngineOptions.MaxStatements"/>.</summary>
    StatementLimit,
}
