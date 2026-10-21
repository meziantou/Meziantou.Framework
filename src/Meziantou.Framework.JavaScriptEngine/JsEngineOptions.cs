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

    /// <summary>
    /// Gets or sets the identifier of the IANA time zone of the local time of the scripts (e.g. "Europe/Paris"), or null to
    /// use the local time zone of <see cref="TimeProvider"/> (a Windows identifier is mapped to its IANA identifier, and an
    /// unknown time zone is UTC). The engine uses its own copy of the IANA time zone database, so the local times do not
    /// depend on the operating system.
    /// </summary>
    public string? TimeZone { get; set; }

    /// <summary>Gets or sets the loader of the modules imported by the scripts and the modules, or null when the scripts cannot import modules.</summary>
    public JsModuleLoader? ModuleLoader { get; set; }

    /// <summary>Gets or sets the host-defined operations (promise rejection tracking, job callbacks, compilation of strings...), or null for the default ones.</summary>
    public JsHost? Host { get; set; }
}
