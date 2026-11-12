namespace Meziantou.Framework.JavaScriptEngine;

/// <summary>Where a function was defined (see <see cref="JsObject.GetFunctionSourceLocation"/>).</summary>
/// <param name="Name">The name of the function ([[InitialName]]), or the empty string.</param>
/// <param name="ScriptOrModule">The script or the module that defines the function ([[ScriptOrModule]]), or null (e.g. a function created by the <c>Function</c> constructor).</param>
/// <param name="Url">The URL (or the file name) of the source text of the function, or null.</param>
/// <param name="Position">The position (0-based) of the source text of the function in the source text of its script.</param>
/// <param name="Line">The line (1-based) of the start of the function.</param>
/// <param name="Column">The column (1-based) of the start of the function.</param>
public readonly record struct JsFunctionSourceLocation(string Name, JsScriptOrModule? ScriptOrModule, string? Url, int Position, int Line, int Column);
