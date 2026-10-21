using System.Runtime.CompilerServices;
using Meziantou.Framework.JavaScriptEngine.Ast;
using Meziantou.Framework.JavaScriptEngine.Text;

namespace Meziantou.Framework.JavaScriptEngine.Parsing;

/// <summary>
/// The recursive descent parser of ECMAScript, with all the early errors. The cover grammars are parsed as expressions
/// and converted to patterns (<see cref="ToAssignable"/>); the errors that depend on the final interpretation are
/// recorded in a <see cref="DestructuringErrors"/> until the interpretation is known.
/// </summary>
internal sealed partial class Parser
{
    private readonly Lexer _lexer;
    private readonly SourceText _source;
    private readonly ParserOptions _options;
    private readonly bool _isModule;
    private Token _token;
    private int _prevEnd;
    private bool _strict;

    // The positions of the first yield/await expressions and of the first 'await' identifier, to report them when the
    // expressions are the parameters of an arrow function (-1: none)
    private int _yieldPos = -1;
    private int _awaitPos = -1;
    private int _awaitIdentPos = -1;
    private int _potentialArrowAt = -1;
    private bool _potentialArrowInForAwait;

    private readonly List<Label> _labels = [];
    private int _labelsBase;

    private Parser(SourceText source, ParserOptions options, bool isModule, bool allowHashbang = true)
    {
        _source = source;
        _options = options;
        _isModule = isModule;
        _lexer = new Lexer(source, isModule, allowHashbang);
        foreach (var name in ContextualNames)
        {
            _lexer.Intern(name);
        }
    }

    private static readonly string[] ContextualNames =
    [
        "async", "await", "yield", "let", "static", "of", "get", "set", "as", "from", "target", "meta", "using",
        "constructor", "prototype", "eval", "arguments", "__proto__", "use strict", "default", "implements",
        "interface", "package", "private", "protected", "public", "enum", "with", "type",
    ];

    private bool Strict
    {
        get => _strict;
        set
        {
            _strict = value;
            _lexer.Strict = value;
        }
    }

    public static Program ParseScript(SourceText source, ParserOptions options)
    {
        var parser = new Parser(source, options, options.IsModule);
        return parser.ParseProgram();
    }

    public static Program ParseModule(SourceText source)
    {
        var parser = new Parser(source, ParserOptions.Module, isModule: true);
        return parser.ParseProgram();
    }

    internal static JsSyntaxErrorException CreateError(SourceText source, int position, string message)
    {
        position = Math.Clamp(position, 0, source.Length);
        var (line, column) = source.GetLineAndColumn(position);
        return new JsSyntaxErrorException(message, source.Url, position, line, column);
    }

    private Program ParseProgram()
    {
        var program = new Program { IsModule = _isModule };
        Strict = _isModule || _options.IsStrict;
        EnterScope(ScopeFlags.Top);
        Next();
        ParseTopLevel(program.Body);
        if (_token.Kind != TokenKind.EndOfFile)
            Unexpected();

        if (_isModule)
        {
            CheckLocalExports();
        }

        ExitScope();
        program.IsStrict = _strict;
        program.Start = 0;
        program.End = _source.Length;
        return program;
    }

    private void ParseTopLevel(List<Node> body)
    {
        var allowDirectives = true;
        var octalPosition = -1;
        while (_token.Kind != TokenKind.EndOfFile)
        {
            if (allowDirectives)
            {
                if (_token.Kind == TokenKind.StringLiteral)
                {
                    var stringToken = _token;
                    var statement = ParseStatementListItem(topLevel: true);
                    body.Add(statement);
                    if (!ProcessDirective(statement, stringToken, ref octalPosition, out var useStrict))
                    {
                        allowDirectives = false;
                    }
                    else if (useStrict && !_strict)
                    {
                        Strict = true;
                        _token = _lexer.Rescan(_token);
                    }

                    continue;
                }

                allowDirectives = false;
            }

            body.Add(ParseStatementListItem(topLevel: true));
        }
    }

    /// <summary>
    /// Checks whether <paramref name="statement"/> is a directive (a string literal alone in an expression statement)
    /// and records its raw text.
    /// </summary>
    private bool ProcessDirective(Node statement, in Token stringToken, ref int octalPosition, out bool useStrict)
    {
        useStrict = false;
        if (statement is not ExpressionStatement { Expression: StringLiteral literal } expressionStatement || literal.Start != stringToken.Start || literal.End != stringToken.End)
            return false;

        var raw = _source.Text.Substring(stringToken.Start + 1, stringToken.End - stringToken.Start - 2);
        expressionStatement.Directive = raw;
        if ((stringToken.Flags & TokenFlags.LegacyOctal) != 0 && octalPosition < 0)
        {
            octalPosition = stringToken.EscapePosition;
        }

        if (raw == "use strict")
        {
            useStrict = true;
            if (octalPosition >= 0)
                Raise(octalPosition, "Octal escape sequences are not allowed in strict mode");
        }

        return true;
    }

    // Tokens

    private void Next()
    {
        _prevEnd = _token.End;
        _token = _lexer.NextToken();
    }

    private Token PeekToken() => _lexer.Peek(_token.End);

    private bool Eat(TokenKind kind)
    {
        if (_token.Kind == kind)
        {
            Next();
            return true;
        }

        return false;
    }

    private void Expect(TokenKind kind)
    {
        if (_token.Kind != kind)
            Unexpected();

        Next();
    }

    /// <summary>Gets whether the current token is the contextual keyword <paramref name="name"/> written without escapes.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool IsContextual(string name) => _token.Kind == TokenKind.Identifier && _token.Value == name && !_token.IsEscaped;

    private static bool IsContextual(in Token token, string name) => token.Kind == TokenKind.Identifier && token.Value == name && !token.IsEscaped;

    private bool EatContextual(string name)
    {
        if (IsContextual(name))
        {
            Next();
            return true;
        }

        return false;
    }

    private void ExpectContextual(string name)
    {
        if (!EatContextual(name))
            Unexpected();
    }

    private bool CanInsertSemicolon() => _token.Kind is TokenKind.EndOfFile or TokenKind.RightBrace || _token.NewLineBefore;

    private void Semicolon()
    {
        if (!Eat(TokenKind.Semicolon) && !CanInsertSemicolon())
            Unexpected();
    }

    private T Finish<T>(T node, int start)
        where T : Node
    {
        node.Start = start;
        node.End = _prevEnd;
        return node;
    }

    private static T FinishAt<T>(T node, int start, int end)
        where T : Node
    {
        node.Start = start;
        node.End = end;
        return node;
    }

    // Errors

    [DoesNotReturn]
    private void Raise(int position, string message) => throw CreateError(_source, position, message);

    [DoesNotReturn]
    private void Unexpected() => throw CreateUnexpected(_token);

    [DoesNotReturn]
    private void Unexpected(in Token token) => throw CreateUnexpected(token);

    private JsSyntaxErrorException CreateUnexpected(in Token token)
    {
        var message = token.Kind switch
        {
            TokenKind.EndOfFile => "Unexpected end of input",
            TokenKind.Identifier => "Unexpected identifier '" + token.Value + "'",
            TokenKind.NumericLiteral or TokenKind.BigIntLiteral => "Unexpected number",
            TokenKind.StringLiteral => "Unexpected string",
            TokenKind.Template => "Unexpected template string",
            TokenKind.PrivateName => "Unexpected identifier '#" + token.Value + "'",
            _ => "Unexpected token '" + GetTokenText(token) + "'",
        };

        return CreateError(_source, token.Start, message);
    }

    [DoesNotReturn]
    private void Unexpected(int position) => Raise(position, "Unexpected token");

    private string GetTokenText(in Token token) => _source.Text.Substring(token.Start, Math.Min(token.End - token.Start, 50));

    private void EnterRecursion()
    {
        if (!RuntimeHelpers.TryEnsureSufficientExecutionStack())
            Raise(_token.Start, "Maximum call stack size exceeded: the code is nested too deeply");
    }

    // Reserved words

    private static bool IsReservedWord(string name) => name switch
    {
        "break" or "case" or "catch" or "class" or "const" or "continue" or "debugger" or "default" or "delete" or "do" or
        "else" or "enum" or "export" or "extends" or "false" or "finally" or "for" or "function" or "if" or "import" or
        "in" or "instanceof" or "new" or "null" or "return" or "super" or "switch" or "this" or "throw" or "true" or
        "try" or "typeof" or "var" or "void" or "while" or "with" => true,
        _ => false,
    };

    private static bool IsStrictReservedWord(string name) => name is "implements" or "interface" or "let" or "package" or "private" or "protected" or "public" or "static" or "yield";

    // Labels

    private enum LabelKind
    {
        None,
        Loop,
        Switch,
    }

    private sealed class Label
    {
        public string? Name;
        public LabelKind Kind;
        public int StatementStart;
    }

    /// <summary>The errors of an expression that are only errors when it is not converted to a pattern (or the reverse).</summary>
    private sealed class DestructuringErrors
    {
        /// <summary>A shorthand property with an initializer (<c>{a = 1}</c>): only valid in a pattern.</summary>
        public int ShorthandAssign = -1;

        /// <summary>A comma after a spread element: invalid in a pattern.</summary>
        public int TrailingComma = -1;

        /// <summary>A parenthesized expression that is not a simple target: invalid in an assignment pattern.</summary>
        public int ParenthesizedAssign = -1;

        /// <summary>A parenthesized expression: invalid in a binding pattern.</summary>
        public int ParenthesizedBind = -1;

        /// <summary>A duplicate <c>__proto__</c> property: only an error in an object literal.</summary>
        public int DoubleProto = -1;
    }

    private void CheckPatternErrors(DestructuringErrors? errors, bool isAssign)
    {
        if (errors is null)
            return;

        if (errors.TrailingComma >= 0)
            Raise(errors.TrailingComma, "Comma is not permitted after the rest element");

        var parens = isAssign ? errors.ParenthesizedAssign : errors.ParenthesizedBind;
        if (parens >= 0)
            Raise(parens, isAssign ? "Invalid destructuring assignment target" : "Invalid destructuring target: parenthesized pattern");
    }

    private bool HasExpressionErrors(DestructuringErrors? errors) => errors is not null && (errors.ShorthandAssign >= 0 || errors.DoubleProto >= 0);

    private void CheckExpressionErrors(DestructuringErrors? errors)
    {
        if (errors is null)
            return;

        if (errors.ShorthandAssign >= 0)
            Raise(errors.ShorthandAssign, "Invalid shorthand property initializer");

        if (errors.DoubleProto >= 0)
            Raise(errors.DoubleProto, "Duplicate __proto__ fields are not allowed in object literals");
    }

    private void CheckYieldAwaitInDefaultParams()
    {
        if (_yieldPos >= 0 && (_awaitPos < 0 || _yieldPos < _awaitPos))
            Raise(_yieldPos, "Yield expression not allowed in formal parameter");

        if (_awaitPos >= 0)
            Raise(_awaitPos, "Illegal await-expression in formal parameters of async function");
    }
}
