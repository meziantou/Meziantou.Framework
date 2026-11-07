namespace Meziantou.Framework.JavaScriptEngine;

/// <summary>The options of a <see cref="JsEngine"/>: the limits of the scripts and the host services.</summary>
/// <remarks>
/// <para>
/// The engine copies the options when it is created: changing the instance given to the constructor afterwards does not
/// change the engine, so one instance can configure several engines. <see cref="JsEngine.Options"/> is the copy of the
/// engine.
/// </para>
/// <para>
/// The limits (<see cref="RecursionLimit"/>, <see cref="Timeout"/>, <see cref="MemoryLimit"/>, <see cref="MaxStatements"/>,
/// <see cref="RegExpTimeout"/> and <see cref="CancellationToken"/>) can be changed through <see cref="JsEngine.Options"/>
/// on the thread of the engine: the change applies from the next check of the limits. So can <see cref="CanBlock"/> and
/// <see cref="Debugger"/>. The other options configure the engine for its whole life: setting them on
/// <see cref="JsEngine.Options"/> throws an <see cref="InvalidOperationException"/>.
/// </para>
/// </remarks>
public sealed class JsEngineOptions
{
    // The copy of an engine: the options that configure the engine cannot change
    private bool _isEngineCopy;
    private bool _enableV8StackTraceApi;
    private bool _allowStringCompilation = true;
    private JsHost? _host;
    private JsModuleLoader? _moduleLoader;
    private string _defaultLocale = "en-US";
    private string? _timeZone;
    private TimeProvider _timeProvider = TimeProvider.System;

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

    /// <summary>
    /// Gets or sets whether the scripts can block the thread with <c>Atomics.wait</c> (the [[CanBlock]] field of the agent).
    /// When false, <c>Atomics.wait</c> throws a TypeError, as on the main thread of a browser; <c>Atomics.waitAsync</c> still
    /// works. A blocking wait is bounded by <see cref="Timeout"/> and <see cref="CancellationToken"/> either way.
    /// </summary>
    public bool CanBlock { get; set; } = true;

    /// <summary>
    /// Gets or sets whether the realms have the stack trace API of V8 (https://v8.dev/docs/stack-trace-api), used by some
    /// npm packages: <c>Error.captureStackTrace(object, constructorOpt)</c> defines the <c>stack</c> property of an object,
    /// and <c>Error.stackTraceLimit</c> (10 by default) sets the number of frames of the stack traces. They are not part of
    /// ECMAScript, so they are disabled by default. <c>Error.prepareStackTrace</c> is not supported.
    /// </summary>
    /// <exception cref="InvalidOperationException">The options are the ones of an engine (<see cref="JsEngine.Options"/>).</exception>
    public bool EnableV8StackTraceApi
    {
        get => _enableV8StackTraceApi;
        set
        {
            EnsureNotEngineCopy();
            _enableV8StackTraceApi = value;
        }
    }

    /// <summary>
    /// Gets or sets whether the scripts can compile code given as a string: <c>eval</c>, <c>new Function(...)</c>, the
    /// constructors of the generator and async functions, and <c>ShadowRealm.prototype.evaluate</c>. When it is false, they
    /// throw an EvalError, before <see cref="JsHost.EnsureCanCompileStrings"/> is called. The default is true.
    /// </summary>
    /// <exception cref="InvalidOperationException">The options are the ones of an engine (<see cref="JsEngine.Options"/>).</exception>
    public bool AllowStringCompilation
    {
        get => _allowStringCompilation;
        set
        {
            EnsureNotEngineCopy();
            _allowStringCompilation = value;
        }
    }

    /// <summary>Gets or sets the token that stops the running script when it is canceled.</summary>
    public CancellationToken CancellationToken { get; set; }

    /// <summary>Gets or sets the clock of <c>Date.now()</c>, <c>new Date()</c> and the time zone of the dates.</summary>
    /// <exception cref="InvalidOperationException">The options are the ones of an engine (<see cref="JsEngine.Options"/>).</exception>
    public TimeProvider TimeProvider
    {
        get => _timeProvider;
        set
        {
            EnsureNotEngineCopy();
            _timeProvider = value;
        }
    }

    /// <summary>
    /// Gets or sets the identifier of the IANA time zone of the local time of the scripts (e.g. "Europe/Paris"), or null to
    /// use the local time zone of <see cref="TimeProvider"/> (a Windows identifier is mapped to its IANA identifier). The
    /// engine uses its own copy of the IANA time zone database, so the local times do not depend on the operating system.
    /// A local time zone that is not in the database, such as a zone created by <see cref="TimeZoneInfo.CreateCustomTimeZone(string, TimeSpan, string, string)"/>,
    /// uses its own rules: a zone without daylight saving time is an offset time zone ("+05:00"), and a zone with daylight
    /// saving time keeps its identifier. Set this property to "UTC" to use UTC instead. An identifier that is not a time
    /// zone of the database (such as an offset) throws an <see cref="InvalidOperationException"/> when the scripts first
    /// use the local time zone.
    /// </summary>
    /// <exception cref="InvalidOperationException">The options are the ones of an engine (<see cref="JsEngine.Options"/>).</exception>
    public string? TimeZone
    {
        get => _timeZone;
        set
        {
            EnsureNotEngineCopy();
            _timeZone = value;
        }
    }

    /// <summary>
    /// Gets or sets the default locale of Intl (ECMA-402 DefaultLocale), a BCP 47 language tag such as "fr-FR". The
    /// engine uses its own copy of the CLDR data, so the results do not depend on the operating system; a tag that is not
    /// valid or that matches no locale of the data is replaced by "en-US".
    /// </summary>
    /// <exception cref="InvalidOperationException">The options are the ones of an engine (<see cref="JsEngine.Options"/>).</exception>
    public string DefaultLocale
    {
        get => _defaultLocale;
        set
        {
            EnsureNotEngineCopy();
            _defaultLocale = value;
        }
    }

    /// <summary>Gets or sets the loader of the modules imported by the scripts and the modules, or null when the scripts cannot import modules.</summary>
    /// <exception cref="InvalidOperationException">The options are the ones of an engine (<see cref="JsEngine.Options"/>).</exception>
    public JsModuleLoader? ModuleLoader
    {
        get => _moduleLoader;
        set
        {
            EnsureNotEngineCopy();
            _moduleLoader = value;
        }
    }

    /// <summary>
    /// Gets or sets the debugger that receives the execution events of the scripts (a statement runs, a <c>debugger</c>
    /// statement), or null. The code compiled while it is null raises no event and does not pay for it.
    /// </summary>
    public JsDebugger? Debugger { get; set; }

    /// <summary>Gets or sets the host-defined operations (promise rejection tracking, job callbacks, compilation of strings...), or null for the default ones.</summary>
    /// <exception cref="InvalidOperationException">The options are the ones of an engine (<see cref="JsEngine.Options"/>).</exception>
    public JsHost? Host
    {
        get => _host;
        set
        {
            EnsureNotEngineCopy();
            _host = value;
        }
    }

    /// <summary>Creates the copy of the options used by an engine.</summary>
    internal JsEngineOptions CreateEngineCopy()
    {
        return new JsEngineOptions
        {
            RecursionLimit = RecursionLimit,
            Timeout = Timeout,
            MemoryLimit = MemoryLimit,
            MaxStatements = MaxStatements,
            RegExpTimeout = RegExpTimeout,
            CanBlock = CanBlock,
            CancellationToken = CancellationToken,
            TimeProvider = TimeProvider,
            TimeZone = TimeZone,
            DefaultLocale = DefaultLocale,
            ModuleLoader = ModuleLoader,
            Host = Host,
            EnableV8StackTraceApi = EnableV8StackTraceApi,
            AllowStringCompilation = AllowStringCompilation,
            Debugger = Debugger,
            _isEngineCopy = true,
        };
    }

    private void EnsureNotEngineCopy()
    {
        if (_isEngineCopy)
            throw new InvalidOperationException("This option configures the engine for its whole life: set it in the options given to the constructor of the engine");
    }
}
