namespace Meziantou.Framework.JavaScriptEngine;

/// <summary>The options of a <see cref="JsEngine"/>: the limits of the scripts and the host services.</summary>
public sealed class JsEngineOptions
{
    /// <summary>Gets or sets the maximum depth of the calls (JavaScript functions and re-entries from .NET); a deeper call throws a RangeError.</summary>
    public int RecursionLimit { get; set; } = 10_000;

    /// <summary>Gets or sets the maximum duration of an entry into JavaScript (a script, a callback...), or null for no limit.</summary>
    public TimeSpan? Timeout { get; set; }

    /// <summary>Gets or sets the token that stops the running script when it is canceled.</summary>
    public CancellationToken CancellationToken { get; set; }

    /// <summary>Gets or sets the clock of <c>Date.now()</c>, <c>new Date()</c> and the time zone of the dates.</summary>
    public TimeProvider TimeProvider { get; set; } = TimeProvider.System;
}
