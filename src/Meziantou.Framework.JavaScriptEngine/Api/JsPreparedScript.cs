using System.Diagnostics.CodeAnalysis;
using Meziantou.Framework.JavaScriptEngine.Compiler;
using Meziantou.Framework.JavaScriptEngine.Text;

namespace Meziantou.Framework.JavaScriptEngine;

/// <summary>
/// A parsed and compiled script. It is immutable: it can run in several realms and engines, on several threads (e.g. the
/// scripts of a test harness, parsed once for all the pages). The syntax errors of the whole script are reported by
/// <see cref="Parse(string, string?, int, int)"/>, but the body of a nested function is compiled on its first call (once, whichever engine calls it).
/// </summary>
public sealed class JsPreparedScript
{
    private JsPreparedScript(SourceText source, FunctionTemplate template)
    {
        Source = source;
        Template = template;
    }

    internal SourceText Source { get; }

    internal FunctionTemplate Template { get; }

    /// <summary>Gets the URL (or the file name) of the script.</summary>
    public string? Url => Source.Url;

    /// <summary>Parses and compiles a script.</summary>
    /// <param name="code">The source text.</param>
    /// <param name="url">The URL (or the file name) of the script, used in the stack traces and the error locations.</param>
    /// <param name="lineOffset">The number of lines before the code in its document (e.g. an inline script of an HTML document).</param>
    /// <param name="columnOffset">The column of the code in the first line of its document (0-based).</param>
    /// <exception cref="JsSyntaxErrorException">The script is not valid.</exception>
    public static JsPreparedScript Parse(string code, string? url = null, int lineOffset = 0, int columnOffset = 0) => Parse(code, url, lineOffset, columnOffset, unboundNameFunctionStart: -1);

    /// <summary>
    /// Parses and compiles a script in which the named function expression that starts at <paramref name="unboundNameFunctionStart"/>
    /// does not bind its name in its scope, as if it were anonymous: its name is the name given by its context (e.g. the
    /// key of a property definition) and its source text keeps the name (the function of an event handler content attribute).
    /// </summary>
    /// <param name="code">The source text.</param>
    /// <param name="url">The URL (or the file name) of the script, used in the stack traces and the error locations.</param>
    /// <param name="lineOffset">The number of lines before the code in its document (e.g. an inline script of an HTML document).</param>
    /// <param name="columnOffset">The column of the code in the first line of its document (0-based).</param>
    /// <param name="unboundNameFunctionStart">The index in <paramref name="code"/> of the <c>function</c> keyword of the function expression whose name is not bound, or -1 for none.</param>
    /// <exception cref="JsSyntaxErrorException">The script is not valid.</exception>
    public static JsPreparedScript Parse(string code, string? url, int lineOffset, int columnOffset, int unboundNameFunctionStart)
    {
        ArgumentNullException.ThrowIfNull(code);
        var source = new SourceText(code, url, lineOffset, columnOffset) { UnboundNameFunctionStart = unboundNameFunctionStart };
        var program = Parsing.JsParser.ParseScript(source, Parsing.ParserOptions.Script);
        return new JsPreparedScript(source, CodeGenerator.CompileProgram(program, source, ProgramKind.Script, isStrict: false));
    }

    /// <summary>Parses and compiles a script; returns false and the syntax error when the script is not valid.</summary>
    public static bool TryParse(string code, string? url, int lineOffset, int columnOffset, [NotNullWhen(true)] out JsPreparedScript? script, [NotNullWhen(false)] out JsSyntaxErrorException? error) => TryParse(code, url, lineOffset, columnOffset, unboundNameFunctionStart: -1, out script, out error);

    /// <summary>Parses and compiles a script (see <see cref="Parse(string, string?, int, int, int)"/>); returns false and the syntax error when the script is not valid.</summary>
    /// <param name="code">The source text.</param>
    /// <param name="url">The URL (or the file name) of the script, used in the stack traces and the error locations.</param>
    /// <param name="lineOffset">The number of lines before the code in its document (e.g. an inline script of an HTML document).</param>
    /// <param name="columnOffset">The column of the code in the first line of its document (0-based).</param>
    /// <param name="unboundNameFunctionStart">The index in <paramref name="code"/> of the <c>function</c> keyword of the function expression whose name is not bound, or -1 for none.</param>
    /// <param name="script">The compiled script, when the script is valid.</param>
    /// <param name="error">The syntax error, when the script is not valid.</param>
    public static bool TryParse(string code, string? url, int lineOffset, int columnOffset, int unboundNameFunctionStart, [NotNullWhen(true)] out JsPreparedScript? script, [NotNullWhen(false)] out JsSyntaxErrorException? error)
    {
        try
        {
            script = Parse(code, url, lineOffset, columnOffset, unboundNameFunctionStart);
            error = null;
            return true;
        }
        catch (JsSyntaxErrorException ex)
        {
            script = null;
            error = ex;
            return false;
        }
    }
}
