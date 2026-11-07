using Meziantou.Framework.JavaScriptEngine.Ast;
using Meziantou.Framework.JavaScriptEngine.Text;

namespace Meziantou.Framework.JavaScriptEngine.Parsing;

/// <summary>
/// What the parser needs to parse a function again for its lazy compilation: where it is, its kind, and the parts of its
/// context that change its syntax tree. The function was parsed with the rest of its source text, so its early errors and
/// the ones of its context (the private names, <c>super</c>...) were already reported.
/// </summary>
/// <param name="Start">The start of the function (<see cref="Node.Start"/>).</param>
/// <param name="End">The end of the function (<see cref="Node.End"/>).</param>
/// <param name="ParametersStart">The offset of the '(' of the parameters (unused for the arrow functions).</param>
/// <param name="Kind">The kind of the function.</param>
/// <param name="IsAsync">Whether the function is async.</param>
/// <param name="IsGenerator">Whether the function is a generator.</param>
/// <param name="IsStrict">Whether the function is strict mode code (the code that encloses it is parsed as strict when its own body has the directive: it is equivalent for valid code).</param>
/// <param name="IsDerivedConstructor">Whether the function is the constructor of a class that has an <c>extends</c> clause.</param>
/// <param name="ExcludesIn">Whether the concise body of an arrow function excludes the <c>in</c> operator.</param>
/// <param name="Name">The name of a function declaration or expression.</param>
/// <param name="NameStart">The start of the name.</param>
internal readonly record struct LazyFunctionSyntax(int Start, int End, int ParametersStart, FunctionKind Kind, bool IsAsync, bool IsGenerator, bool IsStrict, bool IsDerivedConstructor, bool ExcludesIn, string? Name, int NameStart)
{
    public static LazyFunctionSyntax From(FunctionNode function) => new(
        function.Start,
        function.End,
        function.ParametersStart,
        function.Kind,
        function.IsAsync,
        function.IsGenerator,
        function.IsStrict,
        function.IsDerivedConstructor,
        function.ExcludesIn,
        function.Id?.Name,
        function.Id?.Start ?? 0);
}

internal sealed partial class Parser
{
    // The context of the function was checked when the source text was parsed: everything is allowed
    private static readonly ParserOptions LazyFunctionOptions = new()
    {
        IsLazyFunction = true,
        AllowNewTarget = true,
        AllowSuperProperty = true,
        AllowSuperCall = true,
    };

    private static readonly ParserOptions LazyModuleFunctionOptions = new()
    {
        IsModule = true,
        IsLazyFunction = true,
        AllowNewTarget = true,
        AllowSuperProperty = true,
        AllowSuperCall = true,
    };

    /// <summary>Parses again a function of a source text that was parsed (the lazy compilation of its body).</summary>
    /// <param name="source">The source text that contains the function.</param>
    /// <param name="isModule">Whether the source text is a module.</param>
    /// <param name="syntax">The function.</param>
    public static FunctionNode ParseLazyFunction(SourceText source, bool isModule, in LazyFunctionSyntax syntax)
    {
        var parser = new Parser(source, isModule ? LazyModuleFunctionOptions : LazyFunctionOptions, isModule, allowHashbang: false);
        return parser.ParseLazyFunction(syntax);
    }

    private FunctionNode ParseLazyFunction(in LazyFunctionSyntax syntax)
    {
        Strict = syntax.IsStrict;
        EnterScope(ScopeFlags.Top);
        FunctionNode function;
        switch (syntax.Kind)
        {
            case FunctionKind.Arrow:
                // The parameters are a cover grammar: the arrow function is parsed as the assignment expression it is
                _lexer.MoveTo(syntax.Start);
                Next();
                if (ParseAssignment(syntax.ExcludesIn) is not ArrowFunctionExpression arrow)
                    throw new InvalidOperationException("The lazily compiled code is not an arrow function");

                function = arrow.Function;
                break;
            case FunctionKind.Normal:
            {
                _lexer.MoveTo(syntax.ParametersStart);
                Next();
                var id = syntax.Name is null ? null : new Identifier { Name = _lexer.Intern(syntax.Name), Start = syntax.NameStart, End = syntax.NameStart + syntax.Name.Length };
                function = new FunctionNode { Id = id, IsAsync = syntax.IsAsync, IsGenerator = syntax.IsGenerator, Kind = FunctionKind.Normal, Start = syntax.Start };
                EnterScope(GetFunctionFlags(syntax.IsAsync, syntax.IsGenerator));
                ParseFunctionParametersAndBody(function);
                break;
            }

            default:
                _lexer.MoveTo(syntax.ParametersStart);
                Next();
                function = ParseMethod(syntax.Start, syntax.IsGenerator, syntax.IsAsync, syntax.Kind, allowDirectSuper: syntax.IsDerivedConstructor);
                function.IsDerivedConstructor = syntax.IsDerivedConstructor;
                break;
        }

        if (function.Start != syntax.Start || function.End != syntax.End)
            throw new InvalidOperationException("The lazily compiled function does not match the function of the source text");

        return function;
    }
}
