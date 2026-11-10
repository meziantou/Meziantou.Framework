namespace Meziantou.Framework.JavaScriptEngine.WebApis;

/// <summary>The severity of a message written by the <c>console</c> object.</summary>
public enum JsConsoleMessageLevel
{
    /// <summary>A message of <c>console.log</c>, and of the other methods that print information (<c>count</c>, <c>dir</c>, <c>group</c>, <c>table</c>, <c>timeLog</c>, <c>trace</c>...).</summary>
    Log,

    /// <summary>A message of <c>console.info</c>.</summary>
    Info,

    /// <summary>A message of <c>console.warn</c>, and the warnings of the console (e.g. a timer that does not exist).</summary>
    Warning,

    /// <summary>A message of <c>console.error</c>, or a failed <c>console.assert</c>.</summary>
    Error,

    /// <summary>A message of <c>console.debug</c>.</summary>
    Debug,
}
