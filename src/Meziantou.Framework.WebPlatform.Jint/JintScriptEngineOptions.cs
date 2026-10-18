namespace Meziantou.Framework.WebPlatform.Jint;

/// <summary>
/// Options of a <see cref="JintScriptEngine"/>. The defaults limit the resources a script can use, as the documents may be
/// untrusted: each limit applies to each entry into JavaScript (a script, an event listener, a timer callback...).
/// </summary>
public sealed class JintScriptEngineOptions
{
    /// <summary>Gets the maximum duration of an entry into JavaScript. The default is 10 seconds. Null disables the limit.</summary>
    public TimeSpan? Timeout { get; init; } = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Gets the maximum number of bytes allocated by the thread during an entry into JavaScript. The default is 256 MB.
    /// Null disables the limit.
    /// </summary>
    public long? MemoryLimit { get; init; } = 256 * 1024 * 1024;

    /// <summary>Gets the maximum number of statements of an entry into JavaScript. The default is null: no limit.</summary>
    public int? MaxStatements { get; init; }

    /// <summary>Gets the maximum depth of the JavaScript calls. The default is 512. Null disables the limit.</summary>
    public int? RecursionLimit { get; init; } = 512;

    /// <summary>Gets the maximum duration of a regular expression match. The default is 2 seconds. Null uses the default of Jint.</summary>
    public TimeSpan? RegexTimeout { get; init; } = TimeSpan.FromSeconds(2);

    /// <summary>
    /// Gets a callback that configures the options of the Jint engine, after the options of the library. The CLR interop of
    /// Jint (<c>AllowClr</c>) is disabled by default: enabling it gives the scripts access to .NET.
    /// </summary>
    public Action<Options>? ConfigureEngine { get; init; }
}
