using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Meziantou.Framework.Json.Internals;

/// <summary>
/// Recursive descent parser for JSONPath expressions (RFC 9535).
/// Follows the ABNF grammar from Appendix A.
/// </summary>
/// <remarks>
/// An invalid expression is reported without an exception, so that <see cref="TryParse"/> stays cheap: the first error is
/// recorded, and every parse method returns <see langword="null"/> (or <see langword="false"/>) from then on.
/// <see cref="Parse"/> throws a <see cref="FormatException"/> with that error.
/// </remarks>
[StructLayout(LayoutKind.Auto)]
internal ref struct JsonPathParser
{
    /// <summary>
    /// Maximum nesting depth of filter expressions and function calls. The parser is recursive, so an
    /// unbounded nesting level would overflow the stack, which cannot be caught and terminates the process.
    /// The value matches <see cref="System.Text.Json.JsonDocumentOptions.MaxDepth"/>'s default and is far
    /// above any practical query.
    /// </summary>
    private const int MaxNestingDepth = 64;

    private const string SingularQueryHint = "A singular query only has name and index segments, with no blank space inside their brackets.";

    // The parameters of the built-in functions. They are only read, so every call shares them.
    private static readonly FunctionExpressionType[] ValueParameter = [FunctionExpressionType.ValueType];
    private static readonly FunctionExpressionType[] NodesParameter = [FunctionExpressionType.NodesType];
    private static readonly FunctionExpressionType[] TwoValueParameters = [FunctionExpressionType.ValueType, FunctionExpressionType.ValueType];

    private JsonPathLexer _lexer;
    private JsonPathToken _current;
    private int _depth;
    private readonly JsonPathFunctionRegistry _functions;

    /// <summary>The first error found, either by the lexer or by the parser.</summary>
    private string? _error;

    private JsonPathParser(ReadOnlySpan<char> input, JsonPathFunctionRegistry functions)
    {
        _lexer = new JsonPathLexer(input);
        _depth = 0;
        _functions = functions;
        Advance();
    }

    public static JsonPathExpression Parse(ReadOnlySpan<char> input, JsonPathFunctionRegistry? functions = null)
    {
        var parser = new JsonPathParser(input, functions ?? JsonPathFunctionRegistry.Default);
        return parser.ParseQuery() ?? throw new FormatException(parser._error);
    }

    public static bool TryParse(ReadOnlySpan<char> input, [NotNullWhen(true)] out JsonPathExpression? result, JsonPathFunctionRegistry? functions = null)
    {
        var parser = new JsonPathParser(input, functions ?? JsonPathFunctionRegistry.Default);
        result = parser.ParseQuery();
        return result is not null;
    }

    // jsonpath-query = root-identifier segments
    private JsonPathExpression? ParseQuery()
    {
        // RFC 9535: no leading whitespace allowed before '$'
        if (_current.Position != 0)
        {
            return Fail<JsonPathExpression>("JSONPath query must start with '$' at position 0.");
        }

        if (!Expect(JsonPathTokenKind.RootIdentifier, "Expected '$' at start of JSONPath query."))
            return null;

        var segments = ParseSegments();
        if (segments is null)
            return null;

        if (_current.Kind != JsonPathTokenKind.EndOfInput)
        {
            return Fail<JsonPathExpression>($"Unexpected token '{_current.Kind}' at position {_current.Position}. Expected end of expression.");
        }

        // Reject trailing whitespace: EndOfInput position reflects where content ended before whitespace
        if (_current.Position != _lexer.InputLength)
        {
            return Fail<JsonPathExpression>("JSONPath query must not contain trailing whitespace.");
        }

        // Every error makes a parse method return null, which reaches this method; this is only a safety net
        if (_error is not null)
            return null;

        return new JsonPathExpression(segments);
    }

    // segments = *(S segment)
    private Segment[]? ParseSegments()
    {
        // Most queries have one or two segments, so the first ones are not collected in a list
        if (!TryParseSegment(out var first))
            return _error is null ? [] : null;

        if (!TryParseSegment(out var second))
            return _error is null ? [first] : null;

        var segments = new List<Segment> { first, second };
        while (TryParseSegment(out var segment))
        {
            segments.Add(segment);
        }

        if (_error is not null)
            return null;

        return [.. segments];
    }

    // segment = child-segment / descendant-segment
    // Returns false when there is no segment, or when the segment is invalid: _error then says why.
    private bool TryParseSegment([NotNullWhen(true)] out Segment? segment)
    {
        switch (_current.Kind)
        {
            case JsonPathTokenKind.OpenBracket:
                // child-segment: bracketed-selection
                segment = ParseBracketedSelection(SegmentKind.Child);
                return segment is not null;

            case JsonPathTokenKind.Dot:
                // child-segment: "." (wildcard-selector / member-name-shorthand)
                // RFC 9535: no whitespace allowed between "." and what follows
                var dotPos = _current.Position;
                Advance();
                if (_current.Position != dotPos + 1)
                {
                    segment = Fail<Segment>($"Unexpected whitespace after '.' at position {dotPos}.");
                    return false;
                }

                if (_current.Kind is JsonPathTokenKind.Asterisk)
                {
                    Advance();
                    segment = new Segment(SegmentKind.Child, [WildcardSelector.Instance]);
                    return true;
                }

                if (_current.Kind is JsonPathTokenKind.Identifier or JsonPathTokenKind.True or JsonPathTokenKind.False or JsonPathTokenKind.Null)
                {
                    var name = _current.Kind switch
                    {
                        JsonPathTokenKind.True => "true",
                        JsonPathTokenKind.False => "false",
                        JsonPathTokenKind.Null => "null",
                        _ => _current.StringValue!,
                    };
                    Advance();
                    segment = new Segment(SegmentKind.Child, [new NameSelector(name)]);
                    return true;
                }

                segment = Fail<Segment>($"Expected member name or '*' after '.' at position {_current.Position}.");
                return false;

            case JsonPathTokenKind.DoubleDot:
                // descendant-segment: ".." (bracketed-selection / wildcard-selector / member-name-shorthand)
                // RFC 9535: no whitespace allowed between ".." and what follows
                var doubleDotPos = _current.Position;
                Advance();
                if (_current.Position != doubleDotPos + 2)
                {
                    segment = Fail<Segment>($"Unexpected whitespace after '..' at position {doubleDotPos}.");
                    return false;
                }

                if (_current.Kind is JsonPathTokenKind.OpenBracket)
                {
                    segment = ParseBracketedSelection(SegmentKind.Descendant);
                    return segment is not null;
                }

                if (_current.Kind is JsonPathTokenKind.Asterisk)
                {
                    Advance();
                    segment = new Segment(SegmentKind.Descendant, [WildcardSelector.Instance]);
                    return true;
                }

                if (_current.Kind is JsonPathTokenKind.Identifier or JsonPathTokenKind.True or JsonPathTokenKind.False or JsonPathTokenKind.Null)
                {
                    var name = _current.Kind switch
                    {
                        JsonPathTokenKind.True => "true",
                        JsonPathTokenKind.False => "false",
                        JsonPathTokenKind.Null => "null",
                        _ => _current.StringValue!,
                    };
                    Advance();
                    segment = new Segment(SegmentKind.Descendant, [new NameSelector(name)]);
                    return true;
                }

                segment = Fail<Segment>($"Expected '[', '*', or member name after '..' at position {_current.Position}.");
                return false;

            default:
                segment = null;
                return false;
        }
    }

    // bracketed-selection = "[" S selector *(S "," S selector) S "]"
    private Segment? ParseBracketedSelection(SegmentKind segmentKind)
    {
        if (!Expect(JsonPathTokenKind.OpenBracket, "Expected '['."))
            return null;

        var selector = ParseSelector();
        if (selector is null)
            return null;

        // Most bracketed selections have a single selector, which needs no list
        Selector[] result;
        if (_current.Kind is not JsonPathTokenKind.Comma)
        {
            result = [selector];
        }
        else
        {
            var selectors = new List<Selector> { selector };
            while (_current.Kind is JsonPathTokenKind.Comma)
            {
                Advance();
                selector = ParseSelector();
                if (selector is null)
                    return null;

                selectors.Add(selector);
            }

            result = [.. selectors];
        }

        if (!Expect(JsonPathTokenKind.CloseBracket, "Expected ']' to close bracket selection."))
            return null;

        return new Segment(segmentKind, result);
    }

    // selector = name-selector / wildcard-selector / slice-selector / index-selector / filter-selector
    private Selector? ParseSelector()
    {
        switch (_current.Kind)
        {
            case JsonPathTokenKind.StringLiteral:
                {
                    var name = _current.StringValue!;
                    Advance();
                    return new NameSelector(name);
                }

            case JsonPathTokenKind.Asterisk:
                Advance();
                return WildcardSelector.Instance;

            case JsonPathTokenKind.QuestionMark:
                return ParseFilterSelector();

            case JsonPathTokenKind.NumberLiteral:
                return ParseIndexOrSlice();

            case JsonPathTokenKind.Colon:
                return ParseSlice(start: null);

            default:
                return Fail<Selector>($"Unexpected token '{_current.Kind}' at position {_current.Position}. Expected a selector.");
        }
    }

    // filter-selector = "?" S logical-expr
    private FilterSelector? ParseFilterSelector()
    {
        if (!Expect(JsonPathTokenKind.QuestionMark, "Expected '?'."))
            return null;

        var expr = ParseLogicalOrExpression();
        if (expr is null)
            return null;

        return new FilterSelector(expr);
    }

    // index-selector or start of slice-selector
    private Selector? ParseIndexOrSlice()
    {
        var number = _current;
        Advance();

        if (!TryGetLongValue(number, out var value))
            return null;

        // Check if followed by ':' (slice) or not (index)
        if (_current.Kind is JsonPathTokenKind.Colon)
        {
            return ParseSlice(value);
        }

        return new IndexSelector(value);
    }

    // slice-selector = [start S] ":" S [end S] [":" [S step]]
    private SliceSelector? ParseSlice(long? start)
    {
        if (!Expect(JsonPathTokenKind.Colon, "Expected ':' in slice selector."))
            return null;

        long? end = null;
        if (_current.Kind is JsonPathTokenKind.NumberLiteral)
        {
            if (!TryGetLongValue(_current, out var value))
                return null;

            end = value;
            Advance();
        }

        long? step = null;
        if (_current.Kind is JsonPathTokenKind.Colon)
        {
            Advance();
            if (_current.Kind is JsonPathTokenKind.NumberLiteral)
            {
                if (!TryGetLongValue(_current, out var value))
                    return null;

                step = value;
                Advance();
            }
        }

        return new SliceSelector(start, end, step);
    }

    // ---- Filter Expression Parsing ----
    // Follows RFC 9535 §2.3.5.1 grammar with operator precedence:
    // logical-expr = logical-or-expr
    // logical-or-expr = logical-and-expr *(S "||" S logical-and-expr)
    // logical-and-expr = basic-expr *(S "&&" S basic-expr)
    // basic-expr = paren-expr / comparison-expr / test-expr

    // logical-or-expr
    private LogicalExpression? ParseLogicalOrExpression()
    {
        var first = ParseLogicalAndExpression();
        if (first is null || _current.Kind is not JsonPathTokenKind.Or)
        {
            return first;
        }

        // A chain is kept flat so that the evaluator does not need a frame per operand.
        var operands = new List<LogicalExpression> { first };
        while (_current.Kind is JsonPathTokenKind.Or)
        {
            Advance();
            var operand = ParseLogicalAndExpression();
            if (operand is null)
                return null;

            operands.Add(operand);
        }

        return new OrExpression([.. operands]);
    }

    // logical-and-expr
    private LogicalExpression? ParseLogicalAndExpression()
    {
        var first = ParseBasicExpression();
        if (first is null || _current.Kind is not JsonPathTokenKind.And)
        {
            return first;
        }

        var operands = new List<LogicalExpression> { first };
        while (_current.Kind is JsonPathTokenKind.And)
        {
            Advance();
            var operand = ParseBasicExpression();
            if (operand is null)
                return null;

            operands.Add(operand);
        }

        return new AndExpression([.. operands]);
    }

    // basic-expr = paren-expr / comparison-expr / test-expr
    private LogicalExpression? ParseBasicExpression()
    {
        if (!EnterNestingLevel())
            return null;

        var result = ParseBasicExpressionCore();
        _depth--;
        return result;
    }

    private LogicalExpression? ParseBasicExpressionCore()
    {
        // paren-expr = [logical-not-op S] "(" S logical-expr S ")"
        // test-expr = [logical-not-op S] (filter-query / function-expr)
        var negated = false;
        var notPosition = _current.Position;
        if (_current.Kind is JsonPathTokenKind.ExclamationMark)
        {
            negated = true;
            Advance();
        }

        LogicalExpression? expr;

        var isParenthesized = _current.Kind is JsonPathTokenKind.OpenParen;
        if (isParenthesized)
        {
            // paren-expr
            Advance();
            expr = ParseLogicalOrExpression();
            if (expr is null || !Expect(JsonPathTokenKind.CloseParen, "Expected ')' to close parenthesized expression."))
                return null;
        }
        else if (_current.Kind is JsonPathTokenKind.CurrentNodeIdentifier or JsonPathTokenKind.RootIdentifier)
        {
            // Could be: test-expr (existence test) or comparison-expr
            expr = ParseTestOrComparisonExpression();
            if (expr is null)
                return null;
        }
        else if (_current.Kind is JsonPathTokenKind.Identifier && IsLowercaseAlpha(_current.StringValue!) && _lexer.PeekChar() == '(')
        {
            // Could be test-expr (function-expr) or comparison-expr starting with function-expr
            var funcExpr = ParseFunctionCallExpression();
            if (funcExpr is null)
                return null;

            // If followed by a comparison operator, this is a comparison expression
            if (IsComparisonOperator(_current.Kind))
            {
                // comparison-expr: function-expr as comparable on the left
                if (funcExpr.ResultType != FunctionExpressionType.ValueType)
                {
                    return Fail<LogicalExpression>($"Function '{funcExpr.Name}' does not return ValueType and cannot be used in a comparison at position {_current.Position}.");
                }

                var leftComparable = new FunctionCallComparable(funcExpr);
                if (!TryParseComparisonOperator(out var op))
                    return null;

                var rightComparable = ParseComparable();
                if (rightComparable is null)
                    return null;

                expr = new ComparisonExpression(leftComparable, op, rightComparable);
            }
            else
            {
                // Used as test-expr: result type must be LogicalType or NodesType
                if (funcExpr.ResultType is FunctionExpressionType.ValueType)
                {
                    return Fail<LogicalExpression>($"Function '{funcExpr.Name}' returns ValueType and cannot be used as a test expression at position {_current.Position}.");
                }

                expr = funcExpr;
            }
        }
        else if (IsComparableStart())
        {
            // Must be comparison-expr starting with literal or function-expr
            expr = ParseComparisonExpression();
            if (expr is null)
                return null;
        }
        else
        {
            return Fail<LogicalExpression>($"Unexpected token '{_current.Kind}' at position {_current.Position} in filter expression.");
        }

        if (negated)
        {
            // logical-not-op only prefixes a paren-expr or a test-expr. '!@.a == 1' does not mean '!(@.a == 1)':
            // it is not well-formed at all.
            if (!isParenthesized && expr is ComparisonExpression)
            {
                return Fail<LogicalExpression>($"'!' at position {notPosition} cannot negate a comparison. Enclose the comparison in parentheses.");
            }

            expr = new NotExpression(expr);
        }

        return expr;
    }

    // Parse filter query or singular query used in test or comparison context
    private LogicalExpression? ParseTestOrComparisonExpression()
    {
        // Parse the query (@... or $...)
        var savedPos = _lexer.SavePosition();
        var savedToken = _current;

        // Try to parse as singular query first (for comparison context)
        if (TryParseSingularQuery(out var singularQuery))
        {
            // Check if followed by a comparison operator
            if (IsComparisonOperator())
            {
                // It's a comparison: singular-query op comparable
                var left = new SingularQueryComparable(singularQuery);
                return ParseComparisonExpressionRhs(left);
            }

            // Not a comparison, restore and parse as filter query (existence test)
            _lexer.RestorePosition(savedPos);
            _current = savedToken;
        }
        else
        {
            if (_error is not null)
                return null;

            // Restore position if singular query parse failed
            _lexer.RestorePosition(savedPos);
            _current = savedToken;
        }

        // Parse as filter-query for existence test
        var query = ParseFilterQuery();
        if (query is null)
            return null;

        // Check if followed by a comparison operator — this would be an error for non-singular queries
        if (IsComparisonOperator())
        {
            return Fail<LogicalExpression>($"Non-singular query cannot be used in a comparison at position {_current.Position}. {SingularQueryHint}");
        }

        return new ExistenceTestExpression(query);
    }

    // comparison-expr = comparable S comparison-op S comparable
    private ComparisonExpression? ParseComparisonExpression()
    {
        var left = ParseComparable();
        if (left is null)
            return null;

        return ParseComparisonExpressionRhs(left);
    }

    private ComparisonExpression? ParseComparisonExpressionRhs(Comparable left)
    {
        if (!TryParseComparisonOperator(out var op))
            return null;

        var right = ParseComparable();
        if (right is null)
            return null;

        return new ComparisonExpression(left, op, right);
    }

    // comparable = literal / singular-query / function-expr
    private Comparable? ParseComparable()
    {
        switch (_current.Kind)
        {
            case JsonPathTokenKind.StringLiteral:
                {
                    var value = _current.StringValue;
                    Advance();
                    return new LiteralComparable(value);
                }

            case JsonPathTokenKind.NumberLiteral:
                {
                    var value = new NumberLiteral(_current.NumberValue, _current.StringValue!);
                    Advance();
                    return new LiteralComparable(value);
                }

            case JsonPathTokenKind.True:
                Advance();
                return LiteralComparable.True;

            case JsonPathTokenKind.False:
                Advance();
                return LiteralComparable.False;

            case JsonPathTokenKind.Null:
                Advance();
                return LiteralComparable.Null;

            case JsonPathTokenKind.CurrentNodeIdentifier:
            case JsonPathTokenKind.RootIdentifier:
                {
                    var query = ParseSingularQuery();
                    if (query is null)
                        return null;

                    return new SingularQueryComparable(query);
                }

            case JsonPathTokenKind.Identifier when IsLowercaseAlpha(_current.StringValue!) && _lexer.PeekChar() == '(':
                {
                    var funcExpr = ParseFunctionCallExpression();
                    if (funcExpr is null)
                        return null;

                    if (funcExpr.ResultType != FunctionExpressionType.ValueType)
                    {
                        return Fail<Comparable>($"Function '{funcExpr.Name}' does not return ValueType and cannot be used in a comparison at position {_current.Position}.");
                    }

                    return new FunctionCallComparable(funcExpr);
                }

            default:
                return Fail<Comparable>($"Unexpected token '{_current.Kind}' at position {_current.Position}. Expected a comparable value.");
        }
    }

    // filter-query = rel-query / jsonpath-query
    // rel-query = current-node-identifier segments
    // jsonpath-query = root-identifier segments
    private FilterQuery? ParseFilterQuery()
    {
        if (_current.Kind is JsonPathTokenKind.CurrentNodeIdentifier)
        {
            Advance();
            var segments = ParseSegments();
            if (segments is null)
                return null;

            return new RelativeFilterQuery(segments);
        }

        if (_current.Kind is JsonPathTokenKind.RootIdentifier)
        {
            Advance();
            var segments = ParseSegments();
            if (segments is null)
                return null;

            return new AbsoluteFilterQuery(segments);
        }

        return Fail<FilterQuery>($"Expected '@' or '$' at position {_current.Position} for filter query.");
    }

    // singular-query = rel-singular-query / abs-singular-query
    // rel-singular-query = current-node-identifier singular-query-segments
    // abs-singular-query = root-identifier singular-query-segments
    // singular-query-segments = *(S (name-segment / index-segment))
    private SingularQuery? ParseSingularQuery()
    {
        // A query that goes on past its last singular segment (with a wildcard, a slice, a descendant segment or
        // blank space inside the brackets) is still a query, just not a singular one. Nothing that may follow a
        // singular query starts with '[' or '..', so stopping at one of them means the query is not singular.
        var position = _current.Position;
        if (!TryParseSingularQuery(out var query) || _current.Kind is JsonPathTokenKind.OpenBracket or JsonPathTokenKind.DoubleDot)
        {
            return Fail<SingularQuery>($"Expected a singular query at position {position}. {SingularQueryHint}");
        }

        // A lexer error ends the query like any other token, so it is checked here rather than by the caller
        if (_error is not null)
            return null;

        return query;
    }

    // Returns false when the query is not a singular query, or when it is invalid: _error then says why.
    private bool TryParseSingularQuery([NotNullWhen(true)] out SingularQuery? query)
    {
        bool isRelative;
        if (_current.Kind is JsonPathTokenKind.CurrentNodeIdentifier)
        {
            isRelative = true;
        }
        else if (_current.Kind is JsonPathTokenKind.RootIdentifier)
        {
            isRelative = false;
        }
        else
        {
            query = null;
            return false;
        }

        Advance();
        var segments = new List<SingularQuerySegment>();

        while (true)
        {
            if (_current.Kind is JsonPathTokenKind.OpenBracket)
            {
                var savedPos = _lexer.SavePosition();
                var savedToken = _current;
                Advance();

                // name-segment = "[" name-selector "]" and index-segment = "[" index-selector "]" leave no room for
                // blank space, unlike a bracketed selection: "@[ 'a' ]" is a well-formed query, but not a singular one.
                if (_current.Position == savedToken.Position + 1 && _lexer.PeekChar() is ']')
                {
                    if (_current.Kind is JsonPathTokenKind.StringLiteral)
                    {
                        var name = _current.StringValue!;
                        Advance();
                        Advance();
                        segments.Add(new SingularQuerySegment(name));
                        continue;
                    }

                    if (_current.Kind is JsonPathTokenKind.NumberLiteral)
                    {
                        if (!TryGetLongValue(_current, out var index))
                        {
                            query = null;
                            return false;
                        }

                        Advance();
                        Advance();
                        segments.Add(new SingularQuerySegment(index));
                        continue;
                    }
                }

                // Not a valid singular query segment — restore
                _lexer.RestorePosition(savedPos);
                _current = savedToken;
                break;
            }
            else if (_current.Kind is JsonPathTokenKind.Dot)
            {
                // RFC 9535: no whitespace allowed between "." and what follows, in any segment
                var dotPos = _current.Position;
                Advance();
                if (_current.Position != dotPos + 1)
                {
                    query = Fail<SingularQuery>($"Unexpected whitespace after '.' at position {dotPos}.");
                    return false;
                }

                if (_current.Kind is JsonPathTokenKind.Identifier or JsonPathTokenKind.True or JsonPathTokenKind.False or JsonPathTokenKind.Null)
                {
                    var name = _current.Kind switch
                    {
                        JsonPathTokenKind.True => "true",
                        JsonPathTokenKind.False => "false",
                        JsonPathTokenKind.Null => "null",
                        _ => _current.StringValue!,
                    };
                    Advance();
                    segments.Add(new SingularQuerySegment(name));
                    continue;
                }

                // A dot not followed by a valid identifier is not a singular query segment
                // Restore the dot
                query = null;
                return false;
            }
            else
            {
                break;
            }
        }

        query = new SingularQuery(isRelative, [.. segments]);
        return true;
    }

    // function-expr = function-name "(" S [function-argument *(S "," S function-argument)] S ")"
    private FunctionCallExpression? ParseFunctionCallExpression()
    {
        // Nested function arguments recurse back into this method without going through
        // ParseBasicExpression, so this entry point needs its own guard.
        if (!EnterNestingLevel())
            return null;

        var result = ParseFunctionCallExpressionCore();
        _depth--;
        return result;
    }

    private FunctionCallExpression? ParseFunctionCallExpressionCore()
    {
        var name = _current.StringValue!;
        var pos = _current.Position;
        Advance(); // consume function name

        if (!ExpectInFunctionCall(JsonPathTokenKind.OpenParen, name))
            return null;

        if (!TryGetFunctionDefinition(name, pos, out var funcDef))
            return null;

        var args = new List<FunctionArgument>();

        if (_current.Kind != JsonPathTokenKind.CloseParen)
        {
            var arg = ParseFunctionArgument(name, funcDef.ParameterTypes, 0);
            if (arg is null)
                return null;

            args.Add(arg);
            var argIndex = 1;
            while (_current.Kind is JsonPathTokenKind.Comma)
            {
                Advance();
                arg = ParseFunctionArgument(name, funcDef.ParameterTypes, argIndex);
                if (arg is null)
                    return null;

                args.Add(arg);
                argIndex++;
            }
        }

        if (!ExpectInFunctionCall(JsonPathTokenKind.CloseParen, name))
            return null;

        if (args.Count != funcDef.ParameterTypes.Length)
        {
            return Fail<FunctionCallExpression>($"Function '{name}' expects {funcDef.ParameterTypes.Length} argument(s) but got {args.Count} at position {pos}.");
        }

        return new FunctionCallExpression(name, [.. args], funcDef.ResultType, _functions.GetFunction(name));
    }

    // function-argument = literal / filter-query / logical-expr / function-expr
    private FunctionArgument? ParseFunctionArgument(string functionName, FunctionExpressionType[] paramTypes, int argIndex)
    {
        if (argIndex >= paramTypes.Length)
        {
            return Fail<FunctionArgument>($"Too many arguments for function '{functionName}' at position {_current.Position}.");
        }

        var expectedType = paramTypes[argIndex];

        // Depending on expected type, parse appropriately (RFC 9535 §2.4.3)
        switch (expectedType)
        {
            case FunctionExpressionType.ValueType:
                return ParseValueTypeArgument();

            case FunctionExpressionType.LogicalType:
                return ParseLogicalTypeArgument();

            case FunctionExpressionType.NodesType:
                return ParseNodesTypeArgument();

            default:
                return Fail<FunctionArgument>($"Unknown parameter type for function '{functionName}' at position {_current.Position}.");
        }
    }

    private FunctionArgument? ParseValueTypeArgument()
    {
        // ValueType argument: literal / singular-query / function-expr (with ValueType result)
        switch (_current.Kind)
        {
            case JsonPathTokenKind.StringLiteral:
                {
                    var value = _current.StringValue;
                    Advance();
                    return FunctionArgument.FromLiteral(value);
                }

            case JsonPathTokenKind.NumberLiteral:
                {
                    var value = new NumberLiteral(_current.NumberValue, _current.StringValue!);
                    Advance();
                    return FunctionArgument.FromLiteral(value);
                }

            case JsonPathTokenKind.True:
                Advance();
                return FunctionArgument.FromLiteral(value: true);

            case JsonPathTokenKind.False:
                Advance();
                return FunctionArgument.FromLiteral(value: false);

            case JsonPathTokenKind.Null:
                Advance();
                return FunctionArgument.FromLiteral(value: null);

            case JsonPathTokenKind.CurrentNodeIdentifier:
            case JsonPathTokenKind.RootIdentifier:
                {
                    // Must be a singular query for ValueType
                    var query = ParseSingularQuery();
                    if (query is null)
                        return null;

                    return FunctionArgument.FromFilterQuery(query.IsRelative
                        ? new RelativeFilterQuery(SingularQueryToSegments(query))
                        : new AbsoluteFilterQuery(SingularQueryToSegments(query)));
                }

            case JsonPathTokenKind.Identifier when IsLowercaseAlpha(_current.StringValue!) && _lexer.PeekChar() == '(':
                {
                    var func = ParseFunctionCallExpression();
                    if (func is null)
                        return null;

                    if (func.ResultType != FunctionExpressionType.ValueType)
                    {
                        return Fail<FunctionArgument>($"Function '{func.Name}' does not return ValueType at position {_current.Position}.");
                    }

                    return FunctionArgument.FromFunctionCall(func);
                }

            default:
                return Fail<FunctionArgument>($"Expected a value (literal, singular query, or function) at position {_current.Position}.");
        }
    }

    private FunctionArgument? ParseLogicalTypeArgument()
    {
        // LogicalType argument: logical-expr / function-expr (LogicalType or NodesType)
        var expr = ParseLogicalOrExpression();
        if (expr is null)
            return null;

        return FunctionArgument.FromLogicalExpression(expr);
    }

    private FunctionArgument? ParseNodesTypeArgument()
    {
        if (_current.Kind is JsonPathTokenKind.Identifier && IsLowercaseAlpha(_current.StringValue!) && _lexer.PeekChar() == '(')
        {
            var function = ParseFunctionCallExpression();
            if (function is null)
                return null;

            if (function.ResultType is not FunctionExpressionType.NodesType)
            {
                return Fail<FunctionArgument>($"Function '{function.Name}' does not return NodesType at position {_current.Position}.");
            }

            return FunctionArgument.FromFunctionCall(function);
        }

        // NodesType argument: filter-query (which includes singular-query)
        var query = ParseFilterQuery();
        if (query is null)
            return null;

        return FunctionArgument.FromFilterQuery(query);
    }

    // ---- Helper methods ----

    private bool TryParseComparisonOperator(out ComparisonOperator op)
    {
        switch (_current.Kind)
        {
            case JsonPathTokenKind.Equal:
                op = ComparisonOperator.Equal;
                break;
            case JsonPathTokenKind.NotEqual:
                op = ComparisonOperator.NotEqual;
                break;
            case JsonPathTokenKind.LessThan:
                op = ComparisonOperator.LessThan;
                break;
            case JsonPathTokenKind.LessThanOrEqual:
                op = ComparisonOperator.LessThanOrEqual;
                break;
            case JsonPathTokenKind.GreaterThan:
                op = ComparisonOperator.GreaterThan;
                break;
            case JsonPathTokenKind.GreaterThanOrEqual:
                op = ComparisonOperator.GreaterThanOrEqual;
                break;
            default:
                op = default;
                FailComparisonOperator();
                return false;
        }

        Advance();
        return true;
    }

    private bool IsComparisonOperator() => IsComparisonOperator(_current.Kind);

    private static bool IsComparisonOperator(JsonPathTokenKind kind) => kind is JsonPathTokenKind.Equal or JsonPathTokenKind.NotEqual
            or JsonPathTokenKind.LessThan or JsonPathTokenKind.LessThanOrEqual
            or JsonPathTokenKind.GreaterThan or JsonPathTokenKind.GreaterThanOrEqual;

    private bool IsComparableStart() => _current.Kind is JsonPathTokenKind.StringLiteral or JsonPathTokenKind.NumberLiteral
            or JsonPathTokenKind.True or JsonPathTokenKind.False or JsonPathTokenKind.Null
            || (_current.Kind is JsonPathTokenKind.Identifier && IsLowercaseAlpha(_current.StringValue!) && _lexer.PeekChar() == '(');

    private bool Expect(JsonPathTokenKind kind, string errorMessage)
    {
        if (_current.Kind != kind)
        {
            FailExpected(errorMessage);
            return false;
        }

        Advance();
        return true;
    }

    private void Advance()
    {
        _current = _lexer.NextToken();

        // The lexer reports an invalid token once, when it reads it: that is when the parser would have stopped
        if (_current.Kind is JsonPathTokenKind.Error)
        {
            _error ??= _lexer.Error;
        }
    }

    /// <summary>Records an error, unless one was already found, which is the one reported.</summary>
    /// <returns><see langword="null"/>, for the caller to return.</returns>
    private T? Fail<T>(string error)
        where T : class
    {
        _error ??= error;
        return null;
    }

    // The methods below format the errors of small methods that run for most tokens. Keeping the formatting out of those
    // methods lets them be inlined.

    private bool ExpectInFunctionCall(JsonPathTokenKind kind, string functionName)
    {
        if (_current.Kind != kind)
        {
            FailExpected(kind is JsonPathTokenKind.OpenParen
                ? $"Expected '(' after function name '{functionName}'."
                : $"Expected ')' after function arguments for '{functionName}'.");
            return false;
        }

        Advance();
        return true;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void FailExpected(string errorMessage) => Fail<object>($"{errorMessage} Got '{_current.Kind}' at position {_current.Position}.");

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void FailNesting() => Fail<object>($"Filter expression nesting exceeds the maximum depth of {MaxNestingDepth} at position {_current.Position}.");

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void FailIndex(JsonPathToken token, bool outOfRange) => Fail<object>(outOfRange
        ? $"Index/slice value must be an integer in the I-JSON range at position {token.Position}."
        : $"Index/slice value must be an integer at position {token.Position}.");

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void FailComparisonOperator() => Fail<object>($"Expected comparison operator at position {_current.Position}, got '{_current.Kind}'.");

    private bool EnterNestingLevel()
    {
        if (++_depth > MaxNestingDepth)
        {
            FailNesting();
            return false;
        }

        return true;
    }

    private bool TryGetLongValue(JsonPathToken token, out long value)
    {
        value = 0;
        if (!token.IsIntegerLiteral)
        {
            FailIndex(token, outOfRange: false);
            return false;
        }

        var number = token.NumberValue;
        if (number > 9007199254740991L || number < -9007199254740991L)
        {
            FailIndex(token, outOfRange: true);
            return false;
        }

        value = (long)number;
        return true;
    }

    private static bool IsLowercaseAlpha(string s)
    {
        if (s.Length is 0)
        {
            return false;
        }

        return s[0] is >= 'a' and <= 'z';
    }

    private static Segment[] SingularQueryToSegments(SingularQuery query)
    {
        var segments = new Segment[query.Segments.Length];
        for (var i = 0; i < query.Segments.Length; i++)
        {
            var seg = query.Segments[i];
            Selector selector = seg.Kind is SingularQuerySegmentKind.Name
                ? new NameSelector(seg.Name!)
                : new IndexSelector(seg.Index);
            segments[i] = new Segment(SegmentKind.Child, [selector]);
        }

        return segments;
    }

    // ---- Function Registry ----

    private bool TryGetFunctionDefinition(string name, int position, out FunctionDefinition definition)
    {
        if (_functions.GetFunction(name) is { } function)
        {
            definition = new FunctionDefinition(function.ParameterTypes.Select(ConvertFunctionType).ToArray(), ConvertFunctionType(function.ResultType));
            return true;
        }

        switch (name)
        {
            case "length":
                definition = new FunctionDefinition(ValueParameter, FunctionExpressionType.ValueType);
                return true;
            case "count":
                definition = new FunctionDefinition(NodesParameter, FunctionExpressionType.ValueType);
                return true;
            case "match":
                definition = new FunctionDefinition(TwoValueParameters, FunctionExpressionType.LogicalType);
                return true;
            case "search":
                definition = new FunctionDefinition(TwoValueParameters, FunctionExpressionType.LogicalType);
                return true;
            case "value":
                definition = new FunctionDefinition(NodesParameter, FunctionExpressionType.ValueType);
                return true;
            default:
                definition = default;
                Fail<object>($"Unknown function '{name}' at position {position}.");
                return false;
        }
    }

    private static FunctionExpressionType ConvertFunctionType(JsonPathFunctionType type) => type switch
    {
        JsonPathFunctionType.Value => FunctionExpressionType.ValueType,
        JsonPathFunctionType.Logical => FunctionExpressionType.LogicalType,
        JsonPathFunctionType.Nodes => FunctionExpressionType.NodesType,
        _ => throw new InvalidOperationException("Invalid function signature."),
    };

    private readonly struct FunctionDefinition
    {
        public FunctionDefinition(FunctionExpressionType[] parameterTypes, FunctionExpressionType resultType)
        {
            ParameterTypes = parameterTypes;
            ResultType = resultType;
        }

        public FunctionExpressionType[] ParameterTypes { get; }

        public FunctionExpressionType ResultType { get; }
    }
}
