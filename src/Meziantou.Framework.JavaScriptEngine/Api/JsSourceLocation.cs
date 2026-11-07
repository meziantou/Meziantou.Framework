namespace Meziantou.Framework.JavaScriptEngine;

/// <summary>A position in the source text of a script or a module: its URL, and the 1-based line and column.</summary>
public readonly record struct JsSourceLocation(string? Url, int Line, int Column);
