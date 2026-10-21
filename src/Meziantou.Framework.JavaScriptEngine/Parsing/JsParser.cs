using Meziantou.Framework.JavaScriptEngine.Ast;
using Meziantou.Framework.JavaScriptEngine.Text;

namespace Meziantou.Framework.JavaScriptEngine.Parsing;

/// <summary>Parses ECMAScript source text into a syntax tree, with all the early errors of the specification.</summary>
internal static class JsParser
{
    /// <summary>Parses a script (or the code of an eval) and reports the early errors.</summary>
    /// <exception cref="JsSyntaxErrorException">The text is not a valid script.</exception>
    public static Program ParseScript(SourceText source, ParserOptions options) => Parser.ParseScript(source, options);

    /// <summary>Parses a module and reports the early errors.</summary>
    /// <exception cref="JsSyntaxErrorException">The text is not a valid module.</exception>
    public static Program ParseModule(SourceText source) => Parser.ParseModule(source);

    /// <summary>
    /// Parses the source of a dynamic function (https://tc39.es/ecma262/#sec-createdynamicfunction): the parameters and the
    /// body are checked separately, then the assembled source text (<c>function anonymous(parameters\n) {\nbody\n}</c>,
    /// with the prefix of the kind) is parsed. The positions of the returned function refer to the assembled source text.
    /// The function has no name (<see cref="FunctionNode.Id"/> is null): 'anonymous' is not a binding of its scope.
    /// </summary>
    /// <exception cref="JsSyntaxErrorException">The parameters or the body are not valid.</exception>
    public static (FunctionNode Function, SourceText Source) ParseDynamicFunction(string parameters, string body, bool isAsync, bool isGenerator) => Parser.ParseDynamicFunction(parameters, body, isAsync, isGenerator);
}
