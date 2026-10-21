using System.Diagnostics.CodeAnalysis;
using Meziantou.Framework.JavaScriptEngine.Compiler;
using Meziantou.Framework.JavaScriptEngine.Text;

namespace Meziantou.Framework.JavaScriptEngine;

/// <summary>
/// A parsed and compiled script. It is immutable: it can run in several realms and engines, on several threads (e.g. the
/// scripts of a test harness, parsed once for all the pages).
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
    public static JsPreparedScript Parse(string code, string? url = null, int lineOffset = 0, int columnOffset = 0)
    {
        ArgumentNullException.ThrowIfNull(code);
        var source = new SourceText(code, url, lineOffset, columnOffset);
        var program = Parsing.JsParser.ParseScript(source, Parsing.ParserOptions.Script);
        return new JsPreparedScript(source, CodeGenerator.CompileProgram(program, source, ProgramKind.Script, isStrict: false));
    }

    /// <summary>Parses and compiles a script; returns false and the syntax error when the script is not valid.</summary>
    public static bool TryParse(string code, string? url, int lineOffset, int columnOffset, [NotNullWhen(true)] out JsPreparedScript? script, [NotNullWhen(false)] out JsSyntaxErrorException? error)
    {
        try
        {
            script = Parse(code, url, lineOffset, columnOffset);
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
