using Meziantou.Framework.JavaScriptEngine.Ast;
using Meziantou.Framework.JavaScriptEngine.Text;

namespace Meziantou.Framework.JavaScriptEngine.Parsing;

internal sealed partial class Parser
{
    /// <summary>https://tc39.es/ecma262/#sec-createdynamicfunction (steps 16-23)</summary>
    public static (FunctionNode Function, SourceText Source) ParseDynamicFunction(string parameters, string body, bool isAsync, bool isGenerator)
    {
        // The parameters and the body must be valid on their own (e.g. a comment cannot start in the parameters and end in the body)
        var parametersParser = new Parser(new SourceText(parameters), ParserOptions.Script, isModule: false, allowHashbang: false);
        parametersParser.CheckDynamicFunctionParameters(isAsync, isGenerator);

        var bodyParser = new Parser(new SourceText(body), ParserOptions.Script, isModule: false, allowHashbang: false);
        bodyParser.CheckDynamicFunctionBody(isAsync, isGenerator);

        var prefix = (isAsync, isGenerator) switch
        {
            (false, false) => "function",
            (false, true) => "function*",
            (true, false) => "async function",
            (true, true) => "async function*",
        };

        var sourceText = new SourceText(prefix + " anonymous(" + parameters + "\n) {\n" + body + "\n}");
        var parser = new Parser(sourceText, ParserOptions.Script, isModule: false, allowHashbang: false);
        var function = parser.ParseDynamicFunctionExpression(isAsync);
        function.Id = null;
        return (function, sourceText);
    }

    private void CheckDynamicFunctionParameters(bool isAsync, bool isGenerator)
    {
        EnterScope(ScopeFlags.Top);
        EnterScope(GetFunctionFlags(isAsync, isGenerator));
        Next();
        var function = new FunctionNode();
        ParseBindingList(function.Params, TokenKind.EndOfFile, allowEmpty: false, allowTrailingComma: true);
        CheckYieldAwaitInDefaultParams();
        function.HasSimpleParameterList = IsSimpleParameterList(function.Params);
        CheckParams(function, allowDuplicates: function.HasSimpleParameterList);
    }

    private void CheckDynamicFunctionBody(bool isAsync, bool isGenerator)
    {
        EnterScope(ScopeFlags.Top);
        EnterScope(GetFunctionFlags(isAsync, isGenerator));
        Next();
        var function = new FunctionNode { HasSimpleParameterList = true };
        ParseFunctionStatements([], function, isArrow: false, isMethod: false, oldStrict: false, TokenKind.EndOfFile);
    }

    private FunctionNode ParseDynamicFunctionExpression(bool isAsync)
    {
        EnterScope(ScopeFlags.Top);
        Next();
        var start = _token.Start;
        if (isAsync)
        {
            ExpectContextual("async");
        }

        if (_token.Kind != TokenKind.Function)
            Unexpected();

        var expression = ParseFunctionExpression(start, isAsync);
        if (_token.Kind != TokenKind.EndOfFile)
            Unexpected();

        return expression.Function;
    }
}
