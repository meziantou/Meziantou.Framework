using Meziantou.Framework.JavaScriptEngine;

namespace Meziantou.Framework.WebPlatform.JavaScript;

/// <summary>
/// Options of a <see cref="JavaScriptScriptEngine"/>. The defaults limit the resources a script can use, as the documents
/// may be untrusted: each limit applies to each entry into JavaScript (a script, an event listener, a timer callback...).
/// </summary>
public sealed class JavaScriptScriptEngineOptions
{
    /// <summary>Gets the maximum duration of an entry into JavaScript. The default is 10 seconds. Null disables the limit.</summary>
    public TimeSpan? Timeout { get; init; } = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Gets the maximum number of bytes allocated by the thread during an entry into JavaScript. The default is 256 MB.
    /// Null disables the limit.
    /// </summary>
    public long? MemoryLimit { get; init; } = 256 * 1024 * 1024;

    /// <summary>
    /// Gets the maximum number of statements of an entry into JavaScript. The default is null: no limit. The engine counts
    /// the iterations of the loops and the calls, which bound the work of a script as the statements do.
    /// </summary>
    public int? MaxStatements { get; init; }

    /// <summary>
    /// Gets the maximum depth of the JavaScript calls: a deeper call throws a RangeError. The default is 512. Null is the
    /// limit of the engine (<see cref="JsEngineOptions.RecursionLimit"/>, 10,000 calls): the frames of the engine are not on
    /// the .NET stack, so there is always a limit.
    /// </summary>
    public int? RecursionLimit { get; init; } = 512;

    /// <summary>
    /// Gets the maximum duration of a regular expression match: a match that exceeds it stops the script. The default is 2
    /// seconds. Null disables the limit (the matches are still bounded by <see cref="Timeout"/>).
    /// </summary>
    public TimeSpan? RegexTimeout { get; init; } = TimeSpan.FromSeconds(2);

    /// <summary>Gets the clock of <c>Date.now()</c> and <c>new Date()</c>, and the local time zone of the dates. The default is <see cref="TimeProvider.System"/>.</summary>
    public TimeProvider? TimeProvider { get; init; }

    /// <summary>
    /// Gets a callback that configures the options of the engine, after the options of the library. The host hooks
    /// (<see cref="JsEngineOptions.Host"/>) and the module loader (<see cref="JsEngineOptions.ModuleLoader"/>) are the ones
    /// of the library and must not be replaced.
    /// </summary>
    public Action<JsEngineOptions>? ConfigureEngine { get; init; }
}
