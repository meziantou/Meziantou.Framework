using Meziantou.Framework.JavaScriptEngine.Ast;

namespace Meziantou.Framework.JavaScriptEngine.Parsing;

internal sealed partial class Parser
{
    /// <summary>Parses a BindingIdentifier or a BindingPattern.</summary>
    private Node ParseBindingAtom()
    {
        EnterRecursion();
        switch (_token.Kind)
        {
            case TokenKind.LeftBracket:
            {
                var start = _token.Start;
                Next();
                var pattern = new ArrayPattern();
                ParseBindingList(pattern.Elements, TokenKind.RightBracket, allowEmpty: true, allowTrailingComma: true);
                return Finish(pattern, start);
            }

            case TokenKind.LeftBrace:
                return ParseObject(isPattern: true, errors: null);
            default:
                return ParseBindingIdentifier();
        }
    }

    private RestElement ParseRestBinding()
    {
        var start = _token.Start;
        Next();
        var argument = ParseBindingAtom();
        return Finish(new RestElement { Argument = argument }, start);
    }

    private Node ParseMaybeDefault(int start, Node? left)
    {
        left ??= ParseBindingAtom();
        if (!Eat(TokenKind.Equal))
            return left;

        var right = ParseAssignment();
        return Finish(new AssignmentPattern { Left = left, Right = right }, start);
    }

    /// <summary>Parses an object literal, or an object binding pattern when <paramref name="isPattern"/> is true.</summary>
    private Node ParseObject(bool isPattern, DestructuringErrors? errors)
    {
        var start = _token.Start;
        Next();
        var properties = new List<Node>();
        var first = true;
        var hasProto = false;
        while (!Eat(TokenKind.RightBrace))
        {
            if (!first)
            {
                Expect(TokenKind.Comma);
                if (Eat(TokenKind.RightBrace))
                    break;
            }
            else
            {
                first = false;
            }

            var property = ParseProperty(isPattern, errors);
            if (!isPattern && property is Property { Computed: false, Method: false, Shorthand: false, Kind: PropertyKind.Init } p &&
                p.Key is Identifier { Name: "__proto__" } or StringLiteral { Value: "__proto__" })
            {
                if (hasProto)
                {
                    if (errors is not null)
                    {
                        if (errors.DoubleProto < 0)
                        {
                            errors.DoubleProto = p.Key.Start;
                        }
                    }
                    else
                    {
                        Raise(p.Key.Start, "Duplicate __proto__ fields are not allowed in object literals");
                    }
                }

                hasProto = true;
            }

            properties.Add(property);
        }

        return isPattern ? Finish(new ObjectPattern { Properties = properties }, start) : Finish(new ObjectExpression { Properties = properties }, start);
    }

    private Node ParseProperty(bool isPattern, DestructuringErrors? errors)
    {
        var start = _token.Start;
        if (_token.Kind == TokenKind.Ellipsis)
        {
            Next();
            if (isPattern)
            {
                var argument = ParseBindingIdentifier();
                if (_token.Kind == TokenKind.Comma)
                    Raise(_token.Start, "Comma is not permitted after the rest element");

                return Finish(new RestElement { Argument = argument }, start);
            }

            var spreadArgument = ParseAssignment(noIn: false, errors);
            if (_token.Kind == TokenKind.Comma && errors is not null && errors.TrailingComma < 0)
            {
                errors.TrailingComma = _token.Start;
            }

            return Finish(new SpreadElement { Argument = spreadArgument }, start);
        }

        var isGenerator = false;
        var isAsync = false;
        if (!isPattern)
        {
            isGenerator = Eat(TokenKind.Star);
        }

        var keyToken = _token;
        var key = ParsePropertyName(out var computed);
        if (!isPattern && !isGenerator && !computed && IsContextual(keyToken, "async") && IsAsyncPropertyNameStart())
        {
            isAsync = true;
            isGenerator = Eat(TokenKind.Star);
            keyToken = _token;
            key = ParsePropertyName(out computed);
        }

        var property = new Property { Key = key, Computed = computed, Kind = PropertyKind.Init };
        if ((isGenerator || isAsync) && _token.Kind == TokenKind.Colon)
            Unexpected();

        if (Eat(TokenKind.Colon))
        {
            property.Value = isPattern ? ParseMaybeDefault(_token.Start, left: null) : ParseAssignment(noIn: false, errors);
        }
        else if (_token.Kind == TokenKind.LeftParen)
        {
            if (isPattern)
                Unexpected();

            property.Method = true;
            var function = ParseMethod(start, isGenerator, isAsync, FunctionKind.Method, allowDirectSuper: false);
            property.Value = FinishAt(new FunctionExpression { Function = function }, function.Start, function.End);
        }
        else if (!isPattern && !computed && (IsContextual(keyToken, "get") || IsContextual(keyToken, "set")) && _token.Kind is not (TokenKind.Comma or TokenKind.RightBrace or TokenKind.Equal))
        {
            if (isGenerator || isAsync)
                Unexpected();

            var isGetter = keyToken.Value == "get";
            property.Kind = isGetter ? PropertyKind.Get : PropertyKind.Set;
            property.Key = ParsePropertyName(out computed);
            property.Computed = computed;
            var function = ParseMethod(start, isGenerator: false, isAsync: false, isGetter ? FunctionKind.Getter : FunctionKind.Setter, allowDirectSuper: false);
            property.Value = FinishAt(new FunctionExpression { Function = function }, function.Start, function.End);
        }
        else if (!computed && keyToken.Kind == TokenKind.Identifier)
        {
            if (isGenerator || isAsync)
                Unexpected();

            var name = ((Identifier)key).Name;
            CheckUnreserved(name, key.Start);
            if (name == "await" && _awaitIdentPos < 0)
            {
                _awaitIdentPos = key.Start;
            }

            var value = FinishAt(new Identifier { Name = name }, key.Start, key.End);
            if (isPattern)
            {
                property.Value = ParseMaybeDefault(start, value);
            }
            else if (_token.Kind == TokenKind.Equal && errors is not null)
            {
                if (errors.ShorthandAssign < 0)
                {
                    errors.ShorthandAssign = _token.Start;
                }

                property.Value = ParseMaybeDefault(start, value);
            }
            else
            {
                property.Value = value;
            }

            property.Shorthand = true;
        }
        else
        {
            Unexpected();
        }

        return Finish(property, start);
    }

    /// <summary>Gets whether the current token can follow <c>async</c> to make an async method.</summary>
    private bool IsAsyncPropertyNameStart()
    {
        if (_token.NewLineBefore)
            return false;

        return _token.Kind is TokenKind.Identifier or TokenKind.NumericLiteral or TokenKind.BigIntLiteral or TokenKind.StringLiteral or TokenKind.LeftBracket or TokenKind.Star || _token.IsKeyword;
    }

    private Node ParsePropertyName(out bool computed)
    {
        var start = _token.Start;
        switch (_token.Kind)
        {
            case TokenKind.LeftBracket:
            {
                Next();
                var expression = ParseAssignment();
                Expect(TokenKind.RightBracket);
                computed = true;
                return expression;
            }

            case TokenKind.NumericLiteral:
            {
                computed = false;
                var value = _token.Number;
                Next();
                return Finish(new NumericLiteral { Value = value }, start);
            }

            case TokenKind.BigIntLiteral:
            {
                computed = false;
                var value = _token.BigInt;
                Next();
                return Finish(new BigIntLiteral { Value = value }, start);
            }

            case TokenKind.StringLiteral:
            {
                computed = false;
                var value = _token.Value!;
                Next();
                return Finish(new StringLiteral { Value = value }, start);
            }

            default:
                computed = false;
                return ParseIdentifierName();
        }
    }

    /// <summary>Converts an expression (a cover grammar) to an assignment pattern or to a binding pattern.</summary>
    private Node ToAssignable(Node node, bool isBinding, DestructuringErrors? errors)
    {
        EnterRecursion();
        switch (node)
        {
            case Identifier identifier:
                if (InAsync && identifier.Name == "await")
                    Raise(identifier.Start, "Cannot use 'await' as identifier inside an async function");

                return node;
            case ObjectPattern or ArrayPattern or AssignmentPattern or RestElement:
                return node;
            case ObjectExpression obj:
            {
                CheckPatternErrors(errors, isAssign: true);
                var pattern = new ObjectPattern { Start = obj.Start, End = obj.End, Properties = obj.Properties };
                for (var i = 0; i < pattern.Properties.Count; i++)
                {
                    var converted = ToAssignable(pattern.Properties[i], isBinding, errors: null);
                    if (converted is RestElement rest)
                    {
                        if (rest.Argument is ArrayPattern or ObjectPattern)
                            Raise(rest.Argument.Start, "`...` must be followed by an assignable reference in assignment contexts");

                        if (i != pattern.Properties.Count - 1)
                            Raise(rest.Start, "Rest element must be last element");
                    }

                    pattern.Properties[i] = converted;
                }

                return pattern;
            }

            case Property property:
                if (property.Kind != PropertyKind.Init || property.Method)
                    Raise(property.Key.Start, "Invalid destructuring assignment target");

                property.Value = ToAssignable(property.Value, isBinding, errors: null);
                return property;
            case ArrayExpression array:
            {
                CheckPatternErrors(errors, isAssign: true);
                var pattern = new ArrayPattern { Start = array.Start, End = array.End, Elements = array.Elements };
                ToAssignableList(pattern.Elements, isBinding);
                return pattern;
            }

            case SpreadElement spread:
            {
                var argument = ToAssignable(spread.Argument, isBinding, errors: null);
                if (argument is AssignmentPattern)
                    Raise(argument.Start, "Rest elements cannot have a default value");

                return new RestElement { Start = spread.Start, End = spread.End, Argument = argument };
            }

            case AssignmentExpression assignment:
            {
                if (assignment.Operator != AssignmentOperator.Assign)
                    Raise(assignment.Left.End, "Only '=' operator can be used for specifying default value.");

                var left = ToAssignable(assignment.Left, isBinding, errors: null);
                return new AssignmentPattern { Start = assignment.Start, End = assignment.End, Left = left, Right = assignment.Right };
            }

            case ChainExpression:
                Raise(node.Start, "Invalid left-hand side in assignment: optional chain");
                return node;
            case MemberExpression when !isBinding:
                return node;
            default:
                Raise(node.Start, isBinding ? "Invalid destructuring target" : "Invalid left-hand side in assignment");
                return node;
        }
    }

    private void ToAssignableList<T>(List<T> list, bool isBinding)
        where T : Node?
    {
        for (var i = 0; i < list.Count; i++)
        {
            var element = list[i];
            if (element is not null)
            {
                var converted = ToAssignable(element, isBinding, errors: null);
                if (converted is RestElement && i != list.Count - 1)
                    Raise(converted.Start, "Rest element must be last element");

                list[i] = (T)converted;
            }
        }
    }

    private void CheckLValSimple(Node node, BindingKind kind, HashSet<string>? clashes = null)
    {
        var isBinding = kind != BindingKind.None;
        switch (node)
        {
            case Identifier identifier:
            {
                var name = identifier.Name;
                if (Strict && (name is "eval" or "arguments" || IsStrictReservedWord(name)))
                    Raise(identifier.Start, "Unexpected eval, arguments or reserved word in strict mode");

                if (isBinding)
                {
                    if (kind == BindingKind.Lexical && name == "let")
                        Raise(identifier.Start, "let is disallowed as a lexically bound name");

                    if (clashes is not null && !clashes.Add(name))
                        Raise(identifier.Start, "Duplicate parameter name not allowed in this context");

                    if (kind != BindingKind.Outside)
                    {
                        DeclareName(name, kind, identifier.Start);
                    }
                }

                break;
            }

            case ChainExpression:
                Raise(node.Start, "Invalid left-hand side in assignment: optional chain");
                break;
            case MemberExpression:
                if (isBinding)
                    Raise(node.Start, "Invalid destructuring target");

                break;
            default:
                Raise(node.Start, isBinding ? "Invalid destructuring target" : "Invalid left-hand side in assignment");
                break;
        }
    }

    private void CheckLValPattern(Node node, BindingKind kind, HashSet<string>? clashes = null)
    {
        switch (node)
        {
            case ObjectPattern obj:
                foreach (var property in obj.Properties)
                {
                    CheckLValInnerPattern(property, kind, clashes);
                }

                break;
            case ArrayPattern array:
                foreach (var element in array.Elements)
                {
                    if (element is not null)
                    {
                        CheckLValInnerPattern(element, kind, clashes);
                    }
                }

                break;
            default:
                CheckLValSimple(node, kind, clashes);
                break;
        }
    }

    private void CheckLValInnerPattern(Node node, BindingKind kind, HashSet<string>? clashes)
    {
        EnterRecursion();
        switch (node)
        {
            case Property property:
                CheckLValInnerPattern(property.Value, kind, clashes);
                break;
            case AssignmentPattern assignment:
                CheckLValPattern(assignment.Left, kind, clashes);
                break;
            case RestElement rest:
                CheckLValPattern(rest.Argument, kind, clashes);
                break;
            default:
                CheckLValPattern(node, kind, clashes);
                break;
        }
    }
}
