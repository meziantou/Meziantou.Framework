namespace Meziantou.Framework.WebPlatform.Scripting;

/// <summary>
/// A location in the source of a running script (see <see cref="ScriptEngine.GetCurrentScriptLocation"/>): the source file,
/// line number and column number of the violations of the content security policies
/// (https://w3c.github.io/webappsec-csp/#create-violation-for-global, step 2).
/// </summary>
/// <param name="Url">The URL of the script (the URL of the document for an inline script or an event handler), as requested by the page (before the redirects).</param>
/// <param name="LineNumber">The line (1-based).</param>
/// <param name="ColumnNumber">The column (1-based).</param>
public readonly record struct ScriptLocation(string Url, int LineNumber, int ColumnNumber);
