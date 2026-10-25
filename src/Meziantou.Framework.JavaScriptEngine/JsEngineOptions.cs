namespace Meziantou.Framework.JavaScriptEngine;

/// <summary>The options of a <see cref="JsEngine"/>: the limits of the scripts and the host services.</summary>
public sealed class JsEngineOptions
{
    /// <summary>Gets or sets the maximum depth of the calls (JavaScript functions and re-entries from .NET); a deeper call throws a RangeError.</summary>
    public int RecursionLimit { get; set; } = 10_000;

    /// <summary>Gets or sets the maximum duration of an entry into JavaScript (a script, a callback...), or null for no limit.</summary>
    public TimeSpan? Timeout { get; set; }

    /// <summary>
    /// Gets or sets the maximum number of bytes allocated by the thread during an entry into JavaScript (a script, a
    /// callback...), or null for no limit. The allocations are measured with <see cref="GC.GetAllocatedBytesForCurrentThread"/>.
    /// </summary>
    public long? MemoryLimit { get; set; }

    /// <summary>
    /// Gets or sets the maximum number of statements of an entry into JavaScript, or null for no limit. The engine counts
    /// the iterations of the loops and the calls, which bound the work of a script as the statements do.
    /// </summary>
    public long? MaxStatements { get; set; }

    /// <summary>
    /// Gets or sets the maximum duration of a regular expression match, or null for no limit (the matches are still bounded
    /// by <see cref="Timeout"/>). A match that exceeds it stops the script (<see cref="JsTerminationReason.Timeout"/>).
    /// </summary>
    public TimeSpan? RegExpTimeout { get; set; }

    /// <summary>Gets or sets the token that stops the running script when it is canceled.</summary>
    public CancellationToken CancellationToken { get; set; }

    /// <summary>Gets or sets the clock of <c>Date.now()</c>, <c>new Date()</c> and the time zone of the dates.</summary>
    public TimeProvider TimeProvider { get; set; } = TimeProvider.System;

    /// <summary>
    /// Gets or sets the identifier of the IANA time zone of the local time of the scripts (e.g. "Europe/Paris"), or null to
    /// use the local time zone of <see cref="TimeProvider"/> (a Windows identifier is mapped to its IANA identifier, and an
    /// unknown time zone is UTC). The engine uses its own copy of the IANA time zone database, so the local times do not
    /// depend on the operating system. An identifier that is not a time zone of the database (such as an offset) throws an
    /// <see cref="InvalidOperationException"/> when the scripts first use the local time zone.
    /// </summary>
    public string? TimeZone { get; set; }

    /// <summary>
    /// Gets or sets the default locale of Intl (ECMA-402 DefaultLocale), a BCP 47 language tag such as "fr-FR". The
    /// engine uses its own copy of the CLDR data, so the results do not depend on the operating system; a tag that is not
    /// valid or that matches no locale of the data is replaced by "en-US".
    /// </summary>
    public string DefaultLocale { get; set; } = "en-US";

    /// <summary>Gets or sets the loader of the modules imported by the scripts and the modules, or null when the scripts cannot import modules.</summary>
    public JsModuleLoader? ModuleLoader { get; set; }

    /// <summary>Gets or sets the host-defined operations (promise rejection tracking, job callbacks, compilation of strings...), or null for the default ones.</summary>
    public JsHost? Host { get; set; }
}
