namespace Meziantou.Framework.WebPlatform.Scripting;

/// <summary>
/// The severity of a <see cref="ConsoleMessage"/>: the log levels of the WebDriver BiDi log entries
/// (https://w3c.github.io/webdriver-bidi/#types-log-logentry).
/// </summary>
public enum ConsoleMessageLevel
{
    /// <summary>The messages of <c>console.debug()</c>.</summary>
    Debug,

    /// <summary>The messages of <c>console.log()</c>, <c>console.info()</c> and the other methods that are not a warning or an error.</summary>
    Info,

    /// <summary>The messages of <c>console.warn()</c>, and the warnings of the counting and timing methods (e.g. a missing label).</summary>
    Warning,

    /// <summary>The messages of <c>console.error()</c> and <c>console.assert()</c>.</summary>
    Error,
}
