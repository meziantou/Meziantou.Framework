using Meziantou.Framework.JavaScriptEngine.Ast;

namespace Meziantou.Framework.JavaScriptEngine.Parsing;

internal sealed partial class Parser
{
    private const int RelationalPrecedence = 7;

    private readonly List<DestructuringErrors> _errorsPool = [];

    private DestructuringErrors RentErrors()
    {
        if (_errorsPool.Count > 0)
        {
            var result = _errorsPool[^1];
            _errorsPool.RemoveAt(_errorsPool.Count - 1);
            result.ShorthandAssign = result.TrailingComma = result.ParenthesizedAssign = result.ParenthesizedBind = result.DoubleProto = -1;
            return result;
        }

        return new DestructuringErrors();
    }

    private void ReturnErrors(DestructuringErrors errors) => _errorsPool.Add(errors);

    /// <summary>Gets whether <paramref name="node"/> is an arrow function that was just parsed without parentheses: nothing can follow it in the expression.</summary>
    private bool IsBareArrow(Node node) => node.Type == NodeType.ArrowFunctionExpression && node.End == _prevEnd;

    private Node ParseExpression(bool noIn = false, DestructuringErrors? errors = null, bool forAwait = false)
    {
        var start = _token.Start;
        var expression = ParseAssignment(noIn, errors, forAwait);
        if (_token.Kind == TokenKind.Comma)
        {
            var sequence = new SequenceExpression();
            sequence.Expressions.Add(expression);
            while (Eat(TokenKind.Comma))
            {
                sequence.Expressions.Add(ParseAssignment(noIn, errors, forAwait));
            }

            return Finish(sequence, start);
        }

        return expression;
    }

    private Node ParseAssignment(bool noIn = false, DestructuringErrors? errors = null, bool forAwait = false)
    {
        EnterRecursion();
        if (IsContextual("yield") && InGenerator)
            return ParseYield(noIn);

        var ownErrors = false;
        int oldParenthesizedAssign = -1, oldTrailingComma = -1, oldDoubleProto = -1;
        if (errors is not null)
        {
            oldParenthesizedAssign = errors.ParenthesizedAssign;
            oldTrailingComma = errors.TrailingComma;
            oldDoubleProto = errors.DoubleProto;
            errors.ParenthesizedAssign = errors.TrailingComma = -1;
        }
        else
        {
            errors = RentErrors();
            ownErrors = true;
        }

        var start = _token.Start;
        if (_token.Kind is TokenKind.LeftParen or TokenKind.Identifier)
        {
            _potentialArrowAt = start;
            _potentialArrowInForAwait = forAwait;
        }

        var left = ParseConditional(noIn, errors);
        if (GetAssignmentOperator(_token.Kind) is { } op)
        {
            if (IsBareArrow(left))
                Unexpected();

            var isWebCompatCall = IsWebCompatCallTarget(left) && op is not (AssignmentOperator.AndAssign or AssignmentOperator.OrAssign or AssignmentOperator.NullishAssign);
            if (op == AssignmentOperator.Assign && !isWebCompatCall)
            {
                left = ToAssignable(left, isBinding: false, errors);
            }

            if (!ownErrors)
            {
                errors.ParenthesizedAssign = errors.TrailingComma = errors.DoubleProto = -1;
            }

            if (errors.ShorthandAssign >= left.Start)
            {
                errors.ShorthandAssign = -1;
            }

            if (isWebCompatCall)
            {
                // Annex B: f() = 1 throws a ReferenceError at runtime in sloppy mode
            }
            else if (op == AssignmentOperator.Assign)
            {
                CheckLValPattern(left, BindingKind.None);
            }
            else
            {
                CheckLValSimple(left, BindingKind.None);
            }

            Next();
            var right = ParseAssignment(noIn);
            if (oldDoubleProto >= 0)
            {
                errors.DoubleProto = oldDoubleProto;
            }

            if (ownErrors)
            {
                ReturnErrors(errors);
            }

            return Finish(new AssignmentExpression { Operator = op, Left = left, Right = right }, start);
        }

        if (ownErrors)
        {
            CheckExpressionErrors(errors);
            ReturnErrors(errors);
        }

        if (oldParenthesizedAssign >= 0)
        {
            errors.ParenthesizedAssign = oldParenthesizedAssign;
        }

        if (oldTrailingComma >= 0)
        {
            errors.TrailingComma = oldTrailingComma;
        }

        return left;
    }

    private static AssignmentOperator? GetAssignmentOperator(TokenKind kind) => kind switch
    {
        TokenKind.Equal => AssignmentOperator.Assign,
        TokenKind.PlusEqual => AssignmentOperator.AddAssign,
        TokenKind.MinusEqual => AssignmentOperator.SubtractAssign,
        TokenKind.StarEqual => AssignmentOperator.MultiplyAssign,
        TokenKind.SlashEqual => AssignmentOperator.DivideAssign,
        TokenKind.PercentEqual => AssignmentOperator.ModuloAssign,
        TokenKind.StarStarEqual => AssignmentOperator.ExponentiateAssign,
        TokenKind.LeftShiftEqual => AssignmentOperator.LeftShiftAssign,
        TokenKind.RightShiftEqual => AssignmentOperator.RightShiftAssign,
        TokenKind.UnsignedRightShiftEqual => AssignmentOperator.UnsignedRightShiftAssign,
        TokenKind.BarEqual => AssignmentOperator.BitwiseOrAssign,
        TokenKind.CaretEqual => AssignmentOperator.BitwiseXorAssign,
        TokenKind.AmpersandEqual => AssignmentOperator.BitwiseAndAssign,
        TokenKind.AmpersandAmpersandEqual => AssignmentOperator.AndAssign,
        TokenKind.BarBarEqual => AssignmentOperator.OrAssign,
        TokenKind.QuestionQuestionEqual => AssignmentOperator.NullishAssign,
        _ => null,
    };

    private YieldExpression ParseYield(bool noIn)
    {
        var start = _token.Start;
        if (_yieldPos < 0)
        {
            _yieldPos = start;
        }

        Next();
        if (_token.Kind == TokenKind.Semicolon || CanInsertSemicolon() || (_token.Kind != TokenKind.Star && !StartsExpression(_token.Kind)))
            return Finish(new YieldExpression(), start);

        var isDelegate = Eat(TokenKind.Star);
        var argument = ParseAssignment(noIn);
        return Finish(new YieldExpression { Argument = argument, Delegate = isDelegate }, start);
    }

    private static bool StartsExpression(TokenKind kind) => kind switch
    {
        TokenKind.Identifier or TokenKind.PrivateName or TokenKind.NumericLiteral or TokenKind.BigIntLiteral or TokenKind.StringLiteral or
        TokenKind.Template or TokenKind.RegularExpression or TokenKind.LeftBrace or TokenKind.LeftParen or TokenKind.LeftBracket or
        TokenKind.Plus or TokenKind.Minus or TokenKind.PlusPlus or TokenKind.MinusMinus or TokenKind.Exclamation or TokenKind.Tilde or
        TokenKind.Slash or TokenKind.SlashEqual or TokenKind.Class or TokenKind.Delete or TokenKind.False or TokenKind.Function or
        TokenKind.Import or TokenKind.New or TokenKind.Null or TokenKind.Super or TokenKind.This or TokenKind.True or TokenKind.TypeOf or
        TokenKind.Void => true,
        _ => false,
    };

    private Node ParseConditional(bool noIn, DestructuringErrors? errors)
    {
        var start = _token.Start;
        var expression = ParseBinaryExpression(noIn, errors);
        if (HasExpressionErrors(errors) || IsBareArrow(expression))
            return expression;

        if (Eat(TokenKind.Question))
        {
            var consequent = ParseAssignment(noIn: false);
            Expect(TokenKind.Colon);
            var alternate = ParseAssignment(noIn);
            return Finish(new ConditionalExpression { Test = expression, Consequent = consequent, Alternate = alternate }, start);
        }

        return expression;
    }

    private Node ParseBinaryExpression(bool noIn, DestructuringErrors? errors)
    {
        var start = _token.Start;
        if (_token.Kind == TokenKind.PrivateName)
            return ParseBinaryRest(ParsePrivateInLeftOperand(-1, noIn), start, -1, noIn);

        var expression = ParseUnary(errors, sawUnary: false, incDec: false, noIn);
        if (HasExpressionErrors(errors) || IsBareArrow(expression))
            return expression;

        return ParseBinaryRest(expression, start, -1, noIn);
    }

    /// <summary>Parses <c>#x</c> as the left operand of <c>#x in obj</c> (RelationalExpression : PrivateIdentifier in ShiftExpression).</summary>
    private PrivateIdentifier ParsePrivateInLeftOperand(int minPrecedence, bool noIn)
    {
        if (noIn || minPrecedence >= RelationalPrecedence)
            Unexpected();

        var identifier = ParsePrivateIdentifierUse();
        if (_token.Kind != TokenKind.In)
            Unexpected();

        return identifier;
    }

    private static int GetBinaryPrecedence(TokenKind kind) => kind switch
    {
        TokenKind.QuestionQuestion or TokenKind.BarBar => 1,
        TokenKind.AmpersandAmpersand => 2,
        TokenKind.Bar => 3,
        TokenKind.Caret => 4,
        TokenKind.Ampersand => 5,
        TokenKind.EqualEqual or TokenKind.NotEqual or TokenKind.EqualEqualEqual or TokenKind.NotEqualEqual => 6,
        TokenKind.LessThan or TokenKind.GreaterThan or TokenKind.LessThanEqual or TokenKind.GreaterThanEqual or TokenKind.InstanceOf or TokenKind.In => RelationalPrecedence,
        TokenKind.LeftShift or TokenKind.RightShift or TokenKind.UnsignedRightShift => 8,
        TokenKind.Plus or TokenKind.Minus => 9,
        TokenKind.Star or TokenKind.Slash or TokenKind.Percent => 10,
        _ => 0,
    };

    private Node ParseBinaryRest(Node left, int leftStart, int minPrecedence, bool noIn)
    {
        while (true)
        {
            var kind = _token.Kind;
            var precedence = GetBinaryPrecedence(kind);
            if (precedence == 0 || (noIn && kind == TokenKind.In) || precedence <= minPrecedence)
                return left;

            var logical = kind is TokenKind.BarBar or TokenKind.AmpersandAmpersand;
            var coalesce = kind == TokenKind.QuestionQuestion;
            if (coalesce)
            {
                // The right operand of ?? cannot contain && or || (without parentheses)
                precedence = 2;
            }

            Next();
            var rightStart = _token.Start;
            Node right;
            if (_token.Kind == TokenKind.PrivateName)
            {
                right = ParseBinaryRest(ParsePrivateInLeftOperand(precedence, noIn), rightStart, precedence, noIn);
            }
            else
            {
                right = ParseUnary(errors: null, sawUnary: false, incDec: false, noIn);
                if (IsBareArrow(right))
                    Unexpected();

                right = ParseBinaryRest(right, rightStart, precedence, noIn);
            }

            Node node;
            if (logical || coalesce)
            {
                var op = kind switch
                {
                    TokenKind.BarBar => LogicalOperator.Or,
                    TokenKind.AmpersandAmpersand => LogicalOperator.And,
                    _ => LogicalOperator.NullishCoalescing,
                };
                node = FinishAt(new LogicalExpression { Operator = op, Left = left, Right = right }, leftStart, _prevEnd);
                if ((logical && _token.Kind == TokenKind.QuestionQuestion) || (coalesce && _token.Kind is TokenKind.BarBar or TokenKind.AmpersandAmpersand))
                    Raise(_token.Start, "Unexpected token: '??' cannot be mixed with '||' or '&&' without parentheses");
            }
            else
            {
                node = FinishAt(new BinaryExpression { Operator = GetBinaryOperator(kind), Left = left, Right = right }, leftStart, _prevEnd);
            }

            left = node;
        }
    }

    private static BinaryOperator GetBinaryOperator(TokenKind kind) => kind switch
    {
        TokenKind.EqualEqual => BinaryOperator.Equal,
        TokenKind.NotEqual => BinaryOperator.NotEqual,
        TokenKind.EqualEqualEqual => BinaryOperator.StrictEqual,
        TokenKind.NotEqualEqual => BinaryOperator.StrictNotEqual,
        TokenKind.LessThan => BinaryOperator.LessThan,
        TokenKind.LessThanEqual => BinaryOperator.LessThanOrEqual,
        TokenKind.GreaterThan => BinaryOperator.GreaterThan,
        TokenKind.GreaterThanEqual => BinaryOperator.GreaterThanOrEqual,
        TokenKind.LeftShift => BinaryOperator.LeftShift,
        TokenKind.RightShift => BinaryOperator.RightShift,
        TokenKind.UnsignedRightShift => BinaryOperator.UnsignedRightShift,
        TokenKind.Plus => BinaryOperator.Add,
        TokenKind.Minus => BinaryOperator.Subtract,
        TokenKind.Star => BinaryOperator.Multiply,
        TokenKind.Slash => BinaryOperator.Divide,
        TokenKind.Percent => BinaryOperator.Modulo,
        TokenKind.Bar => BinaryOperator.BitwiseOr,
        TokenKind.Caret => BinaryOperator.BitwiseXor,
        TokenKind.Ampersand => BinaryOperator.BitwiseAnd,
        TokenKind.In => BinaryOperator.In,
        TokenKind.InstanceOf => BinaryOperator.InstanceOf,
        _ => throw new InvalidOperationException("Not a binary operator: " + kind),
    };

    private Node ParseUnary(DestructuringErrors? errors, bool sawUnary, bool incDec, bool noIn)
    {
        EnterRecursion();
        var start = _token.Start;
        Node expression;
        var kind = _token.Kind;
        if (IsContextual("await") && CanAwait)
        {
            expression = ParseAwait(noIn);
            sawUnary = true;
        }
        else if (kind is TokenKind.PlusPlus or TokenKind.MinusMinus)
        {
            Next();
            var argument = ParseUnary(errors: null, sawUnary: true, incDec: true, noIn);
            CheckExpressionErrors(errors);
            if (!IsWebCompatCallTarget(argument))
            {
                CheckLValSimple(argument, BindingKind.None);
            }

            expression = Finish(new UpdateExpression { Operator = kind == TokenKind.PlusPlus ? UpdateOperator.Increment : UpdateOperator.Decrement, Prefix = true, Argument = argument }, start);
        }
        else if (GetUnaryOperator(kind) is { } op)
        {
            Next();
            var argument = ParseUnary(errors: null, sawUnary: true, incDec: false, noIn);
            CheckExpressionErrors(errors);
            if (op == UnaryOperator.Delete)
            {
                if (Strict && argument is Identifier)
                    Raise(start, "Delete of an unqualified identifier in strict mode");

                if (IsPrivateFieldAccess(argument))
                    Raise(start, "Private fields can not be deleted");
            }

            sawUnary = true;
            expression = Finish(new UnaryExpression { Operator = op, Argument = argument }, start);
        }
        else
        {
            expression = ParseExpressionSubscripts(errors, noIn);
            if (HasExpressionErrors(errors) || IsBareArrow(expression))
                return expression;

            while (_token.Kind is TokenKind.PlusPlus or TokenKind.MinusMinus && !_token.NewLineBefore)
            {
                if (!IsWebCompatCallTarget(expression))
                {
                    CheckLValSimple(expression, BindingKind.None);
                }

                var updateOperator = _token.Kind == TokenKind.PlusPlus ? UpdateOperator.Increment : UpdateOperator.Decrement;
                Next();
                expression = Finish(new UpdateExpression { Operator = updateOperator, Prefix = false, Argument = expression }, start);
            }
        }

        if (!incDec && _token.Kind == TokenKind.StarStar)
        {
            if (sawUnary)
                Raise(_token.Start, "Unary operator used immediately before exponentiation expression. Parenthesis must be used to disambiguate operator precedence");

            Next();
            var right = ParseUnary(errors: null, sawUnary: false, incDec: false, noIn);
            return Finish(new BinaryExpression { Operator = BinaryOperator.Exponentiate, Left = expression, Right = right }, start);
        }

        return expression;
    }

    private static UnaryOperator? GetUnaryOperator(TokenKind kind) => kind switch
    {
        TokenKind.Minus => UnaryOperator.Minus,
        TokenKind.Plus => UnaryOperator.Plus,
        TokenKind.Exclamation => UnaryOperator.LogicalNot,
        TokenKind.Tilde => UnaryOperator.BitwiseNot,
        TokenKind.TypeOf => UnaryOperator.TypeOf,
        TokenKind.Void => UnaryOperator.Void,
        TokenKind.Delete => UnaryOperator.Delete,
        _ => null,
    };

    private static bool IsPrivateFieldAccess(Node node)
    {
        if (node is ChainExpression chain)
        {
            node = chain.Expression;
        }

        return node is MemberExpression { Property: PrivateIdentifier };
    }

    private AwaitExpression ParseAwait(bool noIn)
    {
        var start = _token.Start;
        if (_awaitPos < 0)
        {
            _awaitPos = start;
        }

        Next();
        var argument = ParseUnary(errors: null, sawUnary: true, incDec: false, noIn);
        return Finish(new AwaitExpression { Argument = argument }, start);
    }

    private Node ParseExpressionSubscripts(DestructuringErrors? errors, bool noIn)
    {
        var start = _token.Start;
        var expression = ParsePrimary(errors, noIn, forNew: false);
        if (IsBareArrow(expression))
            return expression;

        var result = ParseSubscripts(expression, start, noCalls: false, noIn);
        if (errors is not null && result.Type == NodeType.MemberExpression)
        {
            if (errors.ParenthesizedAssign >= result.Start)
            {
                errors.ParenthesizedAssign = -1;
            }

            if (errors.ParenthesizedBind >= result.Start)
            {
                errors.ParenthesizedBind = -1;
            }

            if (errors.TrailingComma >= result.Start)
            {
                errors.TrailingComma = -1;
            }
        }

        return result;
    }

    private Node ParseSubscripts(Node baseNode, int start, bool noCalls, bool noIn)
    {
        var maybeAsyncArrow = baseNode is Identifier { Name: "async" } identifier && _prevEnd == identifier.End && !CanInsertSemicolon() && identifier.End - identifier.Start == 5 && _potentialArrowAt == identifier.Start;
        var optionalChained = false;
        while (true)
        {
            var element = ParseSubscript(baseNode, start, noCalls, maybeAsyncArrow, optionalChained, noIn, out var optional);
            if (optional)
            {
                optionalChained = true;
            }

            if (ReferenceEquals(element, baseNode) || element.Type == NodeType.ArrowFunctionExpression)
            {
                if (optionalChained)
                {
                    element = Finish(new ChainExpression { Expression = element }, start);
                }

                return element;
            }

            baseNode = element;
        }
    }

    private Node ParseSubscript(Node baseNode, int start, bool noCalls, bool maybeAsyncArrow, bool optionalChained, bool noIn, out bool optional)
    {
        optional = false;
        if (_token.Kind == TokenKind.QuestionDot)
        {
            if (noCalls)
                Raise(_token.Start, "Invalid optional chain from new expression");

            optional = true;
            Next();
        }

        var computed = Eat(TokenKind.LeftBracket);
        if (computed || (optional && _token.Kind != TokenKind.LeftParen && _token.Kind != TokenKind.Template) || Eat(TokenKind.Dot))
        {
            Node property;
            if (computed)
            {
                property = ParseExpression();
                Expect(TokenKind.RightBracket);
            }
            else if (_token.Kind == TokenKind.PrivateName)
            {
                if (baseNode is Super)
                    Raise(_token.Start, "Unexpected private field");

                property = ParsePrivateIdentifierUse();
            }
            else
            {
                property = ParseIdentifierName();
            }

            return Finish(new MemberExpression { Object = baseNode, Property = property, Computed = computed, Optional = optional }, start);
        }

        if (!noCalls && _token.Kind == TokenKind.LeftParen)
        {
            Next();
            var errors = RentErrors();
            var oldYieldPos = _yieldPos;
            var oldAwaitPos = _awaitPos;
            var oldAwaitIdentPos = _awaitIdentPos;
            _yieldPos = -1;
            _awaitPos = -1;
            _awaitIdentPos = -1;
            var arguments = ParseArguments(errors);
            if (maybeAsyncArrow && !optional && !CanInsertSemicolon() && _token.Kind == TokenKind.Arrow)
            {
                CheckPatternErrors(errors, isAssign: false);
                ReturnErrors(errors);
                CheckYieldAwaitInDefaultParams();
                if (_awaitIdentPos >= 0)
                    Raise(_awaitIdentPos, "Cannot use 'await' as identifier inside an async function");

                _yieldPos = oldYieldPos;
                _awaitPos = oldAwaitPos;
                _awaitIdentPos = oldAwaitIdentPos;
                Next();
                return ParseArrowExpression(start, arguments, isAsync: true, noIn);
            }

            CheckExpressionErrors(errors);
            ReturnErrors(errors);
            if (oldYieldPos >= 0)
            {
                _yieldPos = oldYieldPos;
            }

            if (oldAwaitPos >= 0)
            {
                _awaitPos = oldAwaitPos;
            }

            if (oldAwaitIdentPos >= 0)
            {
                _awaitIdentPos = oldAwaitIdentPos;
            }

            return Finish(new CallExpression { Callee = baseNode, Arguments = arguments, Optional = optional }, start);
        }

        if (_token.Kind == TokenKind.Template)
        {
            if (optional || optionalChained)
                Raise(_token.Start, "Invalid tagged template on optional chain");

            var quasi = ParseTemplate(isTagged: true);
            return Finish(new TaggedTemplateExpression { Tag = baseNode, Quasi = quasi }, start);
        }

        return baseNode;
    }

    /// <summary>Parses the arguments of a call, after the '('.</summary>
    private List<Node> ParseArguments(DestructuringErrors? errors)
    {
        var arguments = new List<Node>();
        var first = true;
        while (!Eat(TokenKind.RightParen))
        {
            if (!first)
            {
                Expect(TokenKind.Comma);
                if (Eat(TokenKind.RightParen))
                    break;
            }
            else
            {
                first = false;
            }

            if (_token.Kind == TokenKind.Ellipsis)
            {
                arguments.Add(ParseSpread(errors));
                if (errors is not null && _token.Kind == TokenKind.Comma && errors.TrailingComma < 0)
                {
                    errors.TrailingComma = _token.Start;
                }
            }
            else
            {
                arguments.Add(ParseAssignment(noIn: false, errors));
            }
        }

        return arguments;
    }

    private SpreadElement ParseSpread(DestructuringErrors? errors)
    {
        var start = _token.Start;
        Next();
        var argument = ParseAssignment(noIn: false, errors);
        return Finish(new SpreadElement { Argument = argument }, start);
    }

    private Node ParsePrimary(DestructuringErrors? errors, bool noIn, bool forNew)
    {
        var start = _token.Start;
        switch (_token.Kind)
        {
            case TokenKind.Super:
            {
                Next();
                if (_token.Kind == TokenKind.LeftParen)
                {
                    if (forNew || !AllowSuperCall)
                        Raise(start, "'super' keyword unexpected here");
                }
                else if (_token.Kind is TokenKind.LeftBracket or TokenKind.Dot)
                {
                    if (!AllowSuperProperty)
                        Raise(start, "'super' keyword unexpected here");
                }
                else
                {
                    Raise(start, "'super' keyword unexpected here");
                }

                return Finish(new Super(), start);
            }

            case TokenKind.This:
                Next();
                return Finish(new ThisExpression(), start);
            case TokenKind.Identifier:
            {
                var canBeArrow = _potentialArrowAt == start;
                var escaped = _token.IsEscaped;
                var forAwait = _potentialArrowInForAwait;
                var identifier = ParseIdentifierReference();
                if (!escaped && identifier.Name == "async" && !CanInsertSemicolon() && _token.Kind == TokenKind.Function)
                    return ParseFunctionExpression(start, isAsync: true);

                if (canBeArrow && !CanInsertSemicolon())
                {
                    if (_token.Kind == TokenKind.Arrow)
                    {
                        Next();
                        return ParseArrowExpression(start, [identifier], isAsync: false, noIn);
                    }

                    if (!escaped && identifier.Name == "async" && _token.Kind == TokenKind.Identifier && (!forAwait || !IsContextual("of")))
                    {
                        var parameter = ParseIdentifierReference();
                        if (CanInsertSemicolon() || _token.Kind != TokenKind.Arrow)
                            Unexpected();

                        Next();
                        return ParseArrowExpression(start, [parameter], isAsync: true, noIn);
                    }
                }

                return identifier;
            }

            case TokenKind.NumericLiteral:
            {
                var value = _token.Number;
                Next();
                return Finish(new NumericLiteral { Value = value }, start);
            }

            case TokenKind.BigIntLiteral:
            {
                var value = _token.BigInt;
                Next();
                return Finish(new BigIntLiteral { Value = value }, start);
            }

            case TokenKind.StringLiteral:
            {
                var value = _token.Value!;
                Next();
                return Finish(new StringLiteral { Value = value }, start);
            }

            case TokenKind.Null:
                Next();
                return Finish(new NullLiteral(), start);
            case TokenKind.True:
            case TokenKind.False:
            {
                var value = _token.Kind == TokenKind.True;
                Next();
                return Finish(new BooleanLiteral { Value = value }, start);
            }

            case TokenKind.Slash:
            case TokenKind.SlashEqual:
                return ParseRegularExpressionLiteral();
            case TokenKind.LeftParen:
            {
                var canBeArrow = _potentialArrowAt == start;
                var expression = ParseParenthesizedOrArrow(start, canBeArrow, noIn);
                if (errors is not null)
                {
                    if (errors.ParenthesizedAssign < 0 && !IsSimpleAssignTarget(expression))
                    {
                        errors.ParenthesizedAssign = start;
                    }

                    if (errors.ParenthesizedBind < 0)
                    {
                        errors.ParenthesizedBind = start;
                    }
                }

                return expression;
            }

            case TokenKind.LeftBracket:
            {
                Next();
                var array = new ArrayExpression();
                var first = true;
                while (!Eat(TokenKind.RightBracket))
                {
                    if (!first)
                    {
                        Expect(TokenKind.Comma);
                        if (Eat(TokenKind.RightBracket))
                            break;
                    }
                    else
                    {
                        first = false;
                    }

                    if (_token.Kind == TokenKind.Comma)
                    {
                        array.Elements.Add(null);
                    }
                    else if (_token.Kind == TokenKind.Ellipsis)
                    {
                        array.Elements.Add(ParseSpread(errors));
                        if (errors is not null && _token.Kind == TokenKind.Comma && errors.TrailingComma < 0)
                        {
                            errors.TrailingComma = _token.Start;
                        }
                    }
                    else
                    {
                        array.Elements.Add(ParseAssignment(noIn: false, errors));
                    }
                }

                return Finish(array, start);
            }

            case TokenKind.LeftBrace:
                return ParseObject(isPattern: false, errors);
            case TokenKind.Function:
                return ParseFunctionExpression(start, isAsync: false);
            case TokenKind.Class:
                return ParseClassExpression(start);
            case TokenKind.New:
                return ParseNew();
            case TokenKind.Template:
                return ParseTemplate(isTagged: false);
            case TokenKind.Import:
                return ParseImportCallOrMeta(forNew);
            default:
                throw CreateUnexpected(_token);
        }
    }

    /// <summary>
    /// Gets whether <paramref name="node"/> is a call used as an assignment target in sloppy mode: it is not an early error
    /// but a ReferenceError at runtime, for web compatibility (https://tc39.es/ecma262/#sec-static-semantics-assignmenttargettype).
    /// </summary>
    private bool IsWebCompatCallTarget(Node node) => !Strict && node.Type == NodeType.CallExpression;

    private static bool IsSimpleAssignTarget(Node node) => node.Type is NodeType.Identifier or NodeType.MemberExpression;

    private Node ParseParenthesizedOrArrow(int start, bool canBeArrow, bool noIn)
    {
        Next();
        var innerStart = _token.Start;
        var list = new List<Node>();
        var first = true;
        var lastIsComma = false;
        var errors = RentErrors();
        var oldYieldPos = _yieldPos;
        var oldAwaitPos = _awaitPos;
        _yieldPos = -1;
        _awaitPos = -1;
        var spreadStart = -1;
        while (_token.Kind != TokenKind.RightParen)
        {
            if (!first)
            {
                Expect(TokenKind.Comma);
                if (_token.Kind == TokenKind.RightParen)
                {
                    lastIsComma = true;
                    break;
                }
            }
            else
            {
                first = false;
            }

            if (_token.Kind == TokenKind.Ellipsis)
            {
                spreadStart = _token.Start;
                list.Add(ParseRestBinding());
                if (_token.Kind == TokenKind.Comma)
                    Raise(_token.Start, "Comma is not permitted after the rest element");

                break;
            }

            list.Add(ParseAssignment(noIn: false, errors));
        }

        var innerEnd = _prevEnd;
        var closeStart = _token.Start;
        Expect(TokenKind.RightParen);
        if (canBeArrow && !CanInsertSemicolon() && _token.Kind == TokenKind.Arrow)
        {
            CheckPatternErrors(errors, isAssign: false);
            ReturnErrors(errors);
            CheckYieldAwaitInDefaultParams();
            _yieldPos = oldYieldPos;
            _awaitPos = oldAwaitPos;
            Next();
            return ParseArrowExpression(start, list, isAsync: false, noIn);
        }

        if (list.Count == 0 || lastIsComma)
            Unexpected(closeStart);

        if (spreadStart >= 0)
            Unexpected(spreadStart);

        CheckExpressionErrors(errors);
        ReturnErrors(errors);
        if (oldYieldPos >= 0)
        {
            _yieldPos = oldYieldPos;
        }

        if (oldAwaitPos >= 0)
        {
            _awaitPos = oldAwaitPos;
        }

        if (list.Count > 1)
        {
            var sequence = new SequenceExpression { Expressions = list };
            return FinishAt(sequence, innerStart, innerEnd);
        }

        return list[0];
    }

    private Node ParseNew()
    {
        var start = _token.Start;
        Next();
        if (_token.Kind == TokenKind.Dot)
        {
            Next();
            if (_token.Kind != TokenKind.Identifier || _token.Value != "target")
                Raise(_token.Start, "The only valid meta property for new is 'new.target'");

            if (_token.IsEscaped)
                Raise(_token.Start, "'new.target' must not contain escaped characters");

            Next();
            if (!AllowNewTarget)
                Raise(start, "new.target expression is not allowed here");

            return Finish(new MetaProperty { Kind = MetaPropertyKind.NewTarget }, start);
        }

        var calleeStart = _token.Start;
        if (_token.Kind == TokenKind.Import && PeekToken().Kind == TokenKind.LeftParen)
            Raise(calleeStart, "Cannot use new with import");

        var callee = ParseSubscripts(ParsePrimary(errors: null, noIn: false, forNew: true), calleeStart, noCalls: true, noIn: false);

        var node = new NewExpression { Callee = callee };
        if (Eat(TokenKind.LeftParen))
        {
            node.Arguments = ParseArguments(errors: null);
        }

        return Finish(node, start);
    }

    private Node ParseImportCallOrMeta(bool forNew)
    {
        var start = _token.Start;
        Next();
        var phase = Modules.ModulePhase.Evaluation;
        if (_token.Kind == TokenKind.Dot)
        {
            Next();
            if (_token.Kind == TokenKind.Identifier && _token.Value is "defer" or "source" && !_token.IsEscaped)
            {
                // import.defer(specifier, options) (https://tc39.es/proposal-defer-import-eval/) and import.source(specifier, options) (https://tc39.es/proposal-source-phase-imports/)
                phase = _token.Value == "defer" ? Modules.ModulePhase.Defer : Modules.ModulePhase.Source;
                Next();
            }
            else
            {
                if (_token.Kind != TokenKind.Identifier || _token.Value != "meta")
                    Raise(_token.Start, "The only valid meta property for import is 'import.meta'");

                if (_token.IsEscaped)
                    Raise(_token.Start, "'import.meta' must not contain escaped characters");

                Next();
                if (!_isModule)
                    Raise(start, "Cannot use 'import.meta' outside a module");

                return Finish(new MetaProperty { Kind = MetaPropertyKind.ImportMeta }, start);
            }
        }

        if (_token.Kind != TokenKind.LeftParen || forNew)
            Unexpected();

        Next();
        var source = ParseAssignment();
        Node? options = null;
        if (Eat(TokenKind.Comma) && _token.Kind != TokenKind.RightParen)
        {
            options = ParseAssignment();
            Eat(TokenKind.Comma);
        }

        Expect(TokenKind.RightParen);
        return Finish(new ImportExpression { Source = source, Options = options, Phase = phase }, start);
    }

    private TemplateLiteral ParseTemplate(bool isTagged)
    {
        var start = _token.Start;
        var template = new TemplateLiteral();
        while (true)
        {
            if (_token.Kind != TokenKind.Template)
                Unexpected();

            if ((_token.Flags & TokenFlags.InvalidEscape) != 0 && !isTagged)
                Raise(_token.EscapePosition, "Invalid escape sequence in template");

            var tail = (_token.Flags & TokenFlags.TemplateTail) != 0;
            var elementStart = _token.Start + 1;
            var elementEnd = _token.End - (tail ? 1 : 2);
            template.Quasis.Add(FinishAt(new TemplateElement { Cooked = _token.Value, Raw = _token.Raw ?? "", Tail = tail }, elementStart, elementEnd));
            if (tail)
            {
                Next();
                break;
            }

            Next();
            template.Expressions.Add(ParseExpression());
            if (_token.Kind != TokenKind.RightBrace)
                Unexpected();

            _token = _lexer.RescanTemplateContinuation(_token);
        }

        return Finish(template, start);
    }

    private Identifier ParseIdentifierReference()
    {
        if (_token.Kind != TokenKind.Identifier)
            Unexpected();

        var start = _token.Start;
        var name = _token.Value!;
        CheckUnreserved(name, start);
        if (name == "await" && _awaitIdentPos < 0)
        {
            _awaitIdentPos = start;
        }

        Next();
        return Finish(new Identifier { Name = name }, start);
    }

    private Identifier ParseBindingIdentifier() => ParseIdentifierReference();

    /// <summary>Parses an IdentifierName (any identifier, including the reserved words), e.g. a property name.</summary>
    private Identifier ParseIdentifierName()
    {
        var start = _token.Start;
        string name;
        if (_token.Kind == TokenKind.Identifier)
        {
            name = _token.Value!;
        }
        else if (_token.IsKeyword)
        {
            name = _lexer.Intern(_source.Text.AsSpan(_token.Start, _token.End - _token.Start));
        }
        else
        {
            throw CreateUnexpected(_token);
        }

        Next();
        return Finish(new Identifier { Name = name }, start);
    }

    private void CheckUnreserved(string name, int start)
    {
        switch (name)
        {
            case "yield":
                if (InGenerator)
                    Raise(start, "Cannot use 'yield' as identifier inside a generator");

                break;
            case "await":
                if (InAsync)
                    Raise(start, "Cannot use 'await' as identifier inside an async function");

                if (InClassStaticBlock)
                    Raise(start, "Cannot use 'await' in class static initialization block");

                if (_isModule)
                    Raise(start, "Unexpected reserved word 'await'");

                break;
            case "arguments":
                if (ArgumentsForbidden)
                    Raise(start, "'arguments' is not allowed in class field initializer or static initialization block");

                break;
        }

        if (IsReservedWord(name))
            Raise(start, "Keyword must not contain escaped characters");

        if (Strict && IsStrictReservedWord(name))
            Raise(start, "Unexpected strict mode reserved word '" + name + "'");
    }
}
