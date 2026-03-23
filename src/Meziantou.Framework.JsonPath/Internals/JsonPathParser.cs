using System.Runtime.InteropServices;

namespace Meziantou.Framework.Json.Internals;

/// <summary>
/// Recursive descent parser for JSONPath expressions (RFC 9535).
/// Follows the ABNF grammar from Appendix A.
/// </summary>
[StructLayout(LayoutKind.Auto)]
internal ref struct JsonPathParser
{
    private JsonPathLexer _lexer;
    private JsonPathToken _current;

    private JsonPathParser(ReadOnlySpan<char> input)
    {
        _lexer = new JsonPathLexer(input);
        _current = _lexer.NextToken();
    }

    public static JsonPathExpression Parse(ReadOnlySpan<char> input)
    {
        var parser = new JsonPathParser(input);
        return parser.ParseQuery();
    }

    public static bool TryParse(ReadOnlySpan<char> input, [NotNullWhen(true)] out JsonPathExpression? result)
    {
        try
        {
            result = Parse(input);
            return true;
        }
        catch (FormatException)
        {
            result = null;
            return false;
        }
    }

    // jsonpath-query = root-identifier segments
    private JsonPathExpression ParseQuery()
    {
        // RFC 9535: no leading whitespace allowed before '$'
        if (_current.Position != 0)
        {
            throw new FormatException("JSONPath query must start with '$' at position 0.");
        }

        Expect(JsonPathTokenKind.RootIdentifier, "Expected '$' at start of JSONPath query.");
        var segments = ParseSegments();

        if (_current.Kind != JsonPathTokenKind.EndOfInput)
        {
            throw new FormatException($"Unexpected token '{_current.Kind}' at position {_current.Position}. Expected end of expression.");
        }

        // Reject trailing whitespace: EndOfInput position reflects where content ended before whitespace
        if (_current.Position != _lexer.InputLength)
        {
            throw new FormatException("JSONPath query must not contain trailing whitespace.");
        }

        return new JsonPathExpression(segments);
    }

    // segments = *(S segment)
    private Segment[] ParseSegments()
    {
        var segments = new List<Segment>();
        while (TryParseSegment(out var segment))
        {
            segments.Add(segment);
        }

        return [.. segments];
    }

    // segment = child-segment / descendant-segment
    private bool TryParseSegment([NotNullWhen(true)] out Segment? segment)
    {
        switch (_current.Kind)
        {
            case JsonPathTokenKind.OpenBracket:
                // child-segment: bracketed-selection
                segment = ParseBracketedSelection(SegmentKind.Child);
                return true;

            case JsonPathTokenKind.Dot:
                // child-segment: "." (wildcard-selector / member-name-shorthand)
                // RFC 9535: no whitespace allowed between "." and what follows
                var dotPos = _current.Position;
                Advance();
                if (_current.Position != dotPos + 1)
                {
                    throw new FormatException($"Unexpected whitespace after '.' at position {dotPos}.");
                }

                if (_current.Kind is JsonPathTokenKind.Asterisk)
                {
                    Advance();
                    segment = new Segment(SegmentKind.Child, [WildcardSelector.Instance]);
                    return true;
                }

                if (_current.Kind is JsonPathTokenKind.Identifier || _current.Kind is JsonPathTokenKind.True || _current.Kind is JsonPathTokenKind.False || _current.Kind is JsonPathTokenKind.Null)
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

                throw new FormatException($"Expected member name or '*' after '.' at position {_current.Position}.");

            case JsonPathTokenKind.DoubleDot:
                // descendant-segment: ".." (bracketed-selection / wildcard-selector / member-name-shorthand)
                // RFC 9535: no whitespace allowed between ".." and what follows
                var doubleDotPos = _current.Position;
                Advance();
                if (_current.Position != doubleDotPos + 2)
                {
                    throw new FormatException($"Unexpected whitespace after '..' at position {doubleDotPos}.");
                }

                if (_current.Kind is JsonPathTokenKind.OpenBracket)
                {
                    segment = ParseBracketedSelection(SegmentKind.Descendant);
                    return true;
                }

                if (_current.Kind is JsonPathTokenKind.Asterisk)
                {
                    Advance();
                    segment = new Segment(SegmentKind.Descendant, [WildcardSelector.Instance]);
                    return true;
                }

                if (_current.Kind is JsonPathTokenKind.Identifier || _current.Kind is JsonPathTokenKind.True || _current.Kind is JsonPathTokenKind.False || _current.Kind is JsonPathTokenKind.Null)
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

                throw new FormatException($"Expected '[', '*', or member name after '..' at position {_current.Position}.");

            default:
                segment = null;
                return false;
        }
    }

    // bracketed-selection = "[" S selector *(S "," S selector) S "]"
    private Segment ParseBracketedSelection(SegmentKind segmentKind)
    {
        Expect(JsonPathTokenKind.OpenBracket, "Expected '['.");
        var selectors = new List<Selector>();
        selectors.Add(ParseSelector());

        while (_current.Kind is JsonPathTokenKind.Comma)
        {
            Advance();
            selectors.Add(ParseSelector());
        }

        Expect(JsonPathTokenKind.CloseBracket, "Expected ']' to close bracket selection.");

        return new Segment(segmentKind, [.. selectors]);
    }

    // selector = name-selector / wildcard-selector / slice-selector / index-selector / filter-selector
    private Selector ParseSelector()
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
                throw new FormatException($"Unexpected token '{_current.Kind}' at position {_current.Position}. Expected a selector.");
        }
    }

    // filter-selector = "?" S logical-expr
    private FilterSelector ParseFilterSelector()
    {
        Expect(JsonPathTokenKind.QuestionMark, "Expected '?'.");
        var expr = ParseLogicalOrExpression();
        return new FilterSelector(expr);
    }

    // index-selector or start of slice-selector
    private Selector ParseIndexOrSlice()
    {
        var number = _current;
        Advance();

        // Check if followed by ':' (slice) or not (index)
        if (_current.Kind is JsonPathTokenKind.Colon)
        {
            return ParseSlice(GetLongValue(number));
        }

        return new IndexSelector(GetLongValue(number));
    }

    // slice-selector = [start S] ":" S [end S] [":" [S step]]
    private SliceSelector ParseSlice(long? start)
    {
        Expect(JsonPathTokenKind.Colon, "Expected ':' in slice selector.");

        long? end = null;
        if (_current.Kind is JsonPathTokenKind.NumberLiteral)
        {
            end = GetLongValue(_current);
            Advance();
        }

        long? step = null;
        if (_current.Kind is JsonPathTokenKind.Colon)
        {
            Advance();
            if (_current.Kind is JsonPathTokenKind.NumberLiteral)
            {
                step = GetLongValue(_current);
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
    private LogicalExpression ParseLogicalOrExpression()
    {
        var left = ParseLogicalAndExpression();
        while (_current.Kind is JsonPathTokenKind.Or)
        {
            Advance();
            var right = ParseLogicalAndExpression();
            left = new OrExpression(left, right);
        }

        return left;
    }

    // logical-and-expr
    private LogicalExpression ParseLogicalAndExpression()
    {
        var left = ParseBasicExpression();
        while (_current.Kind is JsonPathTokenKind.And)
        {
            Advance();
            var right = ParseBasicExpression();
            left = new AndExpression(left, right);
        }

        return left;
    }

    // basic-expr = paren-expr / comparison-expr / test-expr
    private LogicalExpression ParseBasicExpression()
    {
        // paren-expr = [logical-not-op S] "(" S logical-expr S ")"
        // test-expr = [logical-not-op S] (filter-query / function-expr)
        var negated = false;
        if (_current.Kind is JsonPathTokenKind.ExclamationMark)
        {
            negated = true;
            Advance();
        }

        LogicalExpression expr;

        if (_current.Kind is JsonPathTokenKind.OpenParen)
        {
            // paren-expr
            Advance();
            expr = ParseLogicalOrExpression();
            Expect(JsonPathTokenKind.CloseParen, "Expected ')' to close parenthesized expression.");
        }
        else if (_current.Kind is JsonPathTokenKind.CurrentNodeIdentifier or JsonPathTokenKind.RootIdentifier)
        {
            // Could be: test-expr (existence test) or comparison-expr
            expr = ParseTestOrComparisonExpression();
        }
        else if (_current.Kind is JsonPathTokenKind.Identifier && IsLowercaseAlpha(_current.StringValue!) && _lexer.PeekChar() == '(')
        {
            // Could be test-expr (function-expr) or comparison-expr starting with function-expr
            var funcExpr = ParseFunctionCallExpression();

            // If followed by a comparison operator, this is a comparison expression
            if (IsComparisonOperator(_current.Kind))
            {
                // comparison-expr: function-expr as comparable on the left
                if (funcExpr.ResultType != FunctionExpressionType.ValueType)
                {
                    throw new FormatException($"Function '{funcExpr.Name}' does not return ValueType and cannot be used in a comparison at position {_current.Position}.");
                }

                var leftComparable = new FunctionCallComparable(funcExpr);
                var op = ParseComparisonOperator();
                var rightComparable = ParseComparable();
                expr = new ComparisonExpression(leftComparable, op, rightComparable);
            }
            else
            {
                // Used as test-expr: result type must be LogicalType or NodesType
                if (funcExpr.ResultType is FunctionExpressionType.ValueType)
                {
                    throw new FormatException($"Function '{funcExpr.Name}' returns ValueType and cannot be used as a test expression at position {_current.Position}.");
                }

                expr = funcExpr;
            }
        }
        else if (IsComparableStart())
        {
            // Must be comparison-expr starting with literal or function-expr
            expr = ParseComparisonExpression();
        }
        else
        {
            throw new FormatException($"Unexpected token '{_current.Kind}' at position {_current.Position} in filter expression.");
        }

        if (negated)
        {
            expr = new NotExpression(expr);
        }

        return expr;
    }

    // Parse filter query or singular query used in test or comparison context
    private LogicalExpression ParseTestOrComparisonExpression()
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
            // Restore position if singular query parse failed
            _lexer.RestorePosition(savedPos);
            _current = savedToken;
        }

        // Parse as filter-query for existence test
        var query = ParseFilterQuery();

        // Check if followed by a comparison operator — this would be an error for non-singular queries
        if (IsComparisonOperator())
        {
            throw new FormatException($"Non-singular query cannot be used in a comparison at position {_current.Position}.");
        }

        return new ExistenceTestExpression(query);
    }

    // comparison-expr = comparable S comparison-op S comparable
    private ComparisonExpression ParseComparisonExpression()
    {
        var left = ParseComparable();
        return ParseComparisonExpressionRhs(left);
    }

    private ComparisonExpression ParseComparisonExpressionRhs(Comparable left)
    {
        var op = ParseComparisonOperator();
        var right = ParseComparable();
        return new ComparisonExpression(left, op, right);
    }

    // comparable = literal / singular-query / function-expr
    private Comparable ParseComparable()
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
                    var value = _current.NumberValue;
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
                    return new SingularQueryComparable(query);
                }

            case JsonPathTokenKind.Identifier when IsLowercaseAlpha(_current.StringValue!) && _lexer.PeekChar() == '(':
                {
                    var funcExpr = ParseFunctionCallExpression();
                    if (funcExpr.ResultType != FunctionExpressionType.ValueType)
                    {
                        throw new FormatException($"Function '{funcExpr.Name}' does not return ValueType and cannot be used in a comparison at position {_current.Position}.");
                    }

                    return new FunctionCallComparable(funcExpr);
                }

            default:
                throw new FormatException($"Unexpected token '{_current.Kind}' at position {_current.Position}. Expected a comparable value.");
        }
    }

    // filter-query = rel-query / jsonpath-query
    // rel-query = current-node-identifier segments
    // jsonpath-query = root-identifier segments
    private FilterQuery ParseFilterQuery()
    {
        if (_current.Kind is JsonPathTokenKind.CurrentNodeIdentifier)
        {
            Advance();
            var segments = ParseSegments();
            return new RelativeFilterQuery(segments);
        }

        if (_current.Kind is JsonPathTokenKind.RootIdentifier)
        {
            Advance();
            var segments = ParseSegments();
            return new AbsoluteFilterQuery(segments);
        }

        throw new FormatException($"Expected '@' or '$' at position {_current.Position} for filter query.");
    }

    // singular-query = rel-singular-query / abs-singular-query
    // rel-singular-query = current-node-identifier singular-query-segments
    // abs-singular-query = root-identifier singular-query-segments
    // singular-query-segments = *(S (name-segment / index-segment))
    private SingularQuery ParseSingularQuery()
    {
        if (!TryParseSingularQuery(out var query))
        {
            throw new FormatException($"Expected singular query at position {_current.Position}.");
        }

        return query;
    }

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

                if (_current.Kind is JsonPathTokenKind.StringLiteral)
                {
                    var name = _current.StringValue!;
                    Advance();
                    if (_current.Kind is JsonPathTokenKind.CloseBracket)
                    {
                        Advance();
                        segments.Add(new SingularQuerySegment(name));
                        continue;
                    }
                }
                else if (_current.Kind is JsonPathTokenKind.NumberLiteral)
                {
                    var index = GetLongValue(_current);
                    Advance();
                    if (_current.Kind is JsonPathTokenKind.CloseBracket)
                    {
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
                Advance();
                if (_current.Kind is JsonPathTokenKind.Identifier || _current.Kind is JsonPathTokenKind.True || _current.Kind is JsonPathTokenKind.False || _current.Kind is JsonPathTokenKind.Null)
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
    private FunctionCallExpression ParseFunctionCallExpression()
    {
        var name = _current.StringValue!;
        var pos = _current.Position;
        Advance(); // consume function name

        Expect(JsonPathTokenKind.OpenParen, $"Expected '(' after function name '{name}'.");

        var funcDef = GetFunctionDefinition(name, pos);
        var args = new List<FunctionArgument>();

        if (_current.Kind != JsonPathTokenKind.CloseParen)
        {
            args.Add(ParseFunctionArgument(name, funcDef.ParameterTypes, 0));
            var argIndex = 1;
            while (_current.Kind is JsonPathTokenKind.Comma)
            {
                Advance();
                args.Add(ParseFunctionArgument(name, funcDef.ParameterTypes, argIndex));
                argIndex++;
            }
        }

        Expect(JsonPathTokenKind.CloseParen, $"Expected ')' after function arguments for '{name}'.");

        if (args.Count != funcDef.ParameterTypes.Length)
        {
            throw new FormatException($"Function '{name}' expects {funcDef.ParameterTypes.Length} argument(s) but got {args.Count} at position {pos}.");
        }

        return new FunctionCallExpression(name, [.. args], funcDef.ResultType);
    }

    // function-argument = literal / filter-query / logical-expr / function-expr
    private FunctionArgument ParseFunctionArgument(string functionName, FunctionExpressionType[] paramTypes, int argIndex)
    {
        if (argIndex >= paramTypes.Length)
        {
            throw new FormatException($"Too many arguments for function '{functionName}' at position {_current.Position}.");
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
                throw new FormatException($"Unknown parameter type for function '{functionName}' at position {_current.Position}.");
        }
    }

    private FunctionArgument ParseValueTypeArgument()
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
                    var value = _current.NumberValue;
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
                    return FunctionArgument.FromFilterQuery(query.IsRelative
                        ? new RelativeFilterQuery(SingularQueryToSegments(query))
                        : new AbsoluteFilterQuery(SingularQueryToSegments(query)));
                }

            case JsonPathTokenKind.Identifier when IsLowercaseAlpha(_current.StringValue!) && _lexer.PeekChar() == '(':
                {
                    var func = ParseFunctionCallExpression();
                    if (func.ResultType != FunctionExpressionType.ValueType)
                    {
                        throw new FormatException($"Function '{func.Name}' does not return ValueType at position {_current.Position}.");
                    }

                    return FunctionArgument.FromFunctionCall(func);
                }

            default:
                throw new FormatException($"Expected a value (literal, singular query, or function) at position {_current.Position}.");
        }
    }

    private FunctionArgument ParseLogicalTypeArgument()
    {
        // LogicalType argument: logical-expr / function-expr (LogicalType or NodesType)
        var expr = ParseLogicalOrExpression();
        return FunctionArgument.FromLogicalExpression(expr);
    }

    private FunctionArgument ParseNodesTypeArgument()
    {
        // NodesType argument: filter-query (which includes singular-query)
        var query = ParseFilterQuery();
        return FunctionArgument.FromFilterQuery(query);
    }

    // ---- Helper methods ----

    private ComparisonOperator ParseComparisonOperator()
    {
        var op = _current.Kind switch
        {
            JsonPathTokenKind.Equal => ComparisonOperator.Equal,
            JsonPathTokenKind.NotEqual => ComparisonOperator.NotEqual,
            JsonPathTokenKind.LessThan => ComparisonOperator.LessThan,
            JsonPathTokenKind.LessThanOrEqual => ComparisonOperator.LessThanOrEqual,
            JsonPathTokenKind.GreaterThan => ComparisonOperator.GreaterThan,
            JsonPathTokenKind.GreaterThanOrEqual => ComparisonOperator.GreaterThanOrEqual,
            _ => throw new FormatException($"Expected comparison operator at position {_current.Position}, got '{_current.Kind}'."),
        };
        Advance();
        return op;
    }

    private bool IsComparisonOperator() => IsComparisonOperator(_current.Kind);

    private static bool IsComparisonOperator(JsonPathTokenKind kind) => kind is JsonPathTokenKind.Equal or JsonPathTokenKind.NotEqual
            or JsonPathTokenKind.LessThan or JsonPathTokenKind.LessThanOrEqual
            or JsonPathTokenKind.GreaterThan or JsonPathTokenKind.GreaterThanOrEqual;

    private bool IsComparableStart() => _current.Kind is JsonPathTokenKind.StringLiteral or JsonPathTokenKind.NumberLiteral
            or JsonPathTokenKind.True or JsonPathTokenKind.False or JsonPathTokenKind.Null
            || (_current.Kind is JsonPathTokenKind.Identifier && IsLowercaseAlpha(_current.StringValue!) && _lexer.PeekChar() == '(');

    private void Expect(JsonPathTokenKind kind, string errorMessage)
    {
        if (_current.Kind != kind)
        {
            throw new FormatException($"{errorMessage} Got '{_current.Kind}' at position {_current.Position}.");
        }

        Advance();
    }

    private void Advance()
    {
        _current = _lexer.NextToken();
    }

    private static long GetLongValue(JsonPathToken token)
    {
        if (!token.IsIntegerLiteral)
        {
            throw new FormatException($"Index/slice value must be an integer at position {token.Position}.");
        }

        var value = token.NumberValue;
        if (value > 9007199254740991L || value < -9007199254740991L)
        {
            throw new FormatException($"Index/slice value must be an integer in the I-JSON range at position {token.Position}.");
        }

        return (long)value;
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

    private static FunctionDefinition GetFunctionDefinition(string name, int position)
    {
        return name switch
        {
            "length" => new FunctionDefinition([FunctionExpressionType.ValueType], FunctionExpressionType.ValueType),
            "count" => new FunctionDefinition([FunctionExpressionType.NodesType], FunctionExpressionType.ValueType),
            "match" => new FunctionDefinition([FunctionExpressionType.ValueType, FunctionExpressionType.ValueType], FunctionExpressionType.LogicalType),
            "search" => new FunctionDefinition([FunctionExpressionType.ValueType, FunctionExpressionType.ValueType], FunctionExpressionType.LogicalType),
            "value" => new FunctionDefinition([FunctionExpressionType.NodesType], FunctionExpressionType.ValueType),
            _ => throw new FormatException($"Unknown function '{name}' at position {position}."),
        };
    }

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
