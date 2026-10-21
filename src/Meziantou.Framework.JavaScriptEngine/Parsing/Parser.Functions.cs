using Meziantou.Framework.JavaScriptEngine.Ast;

namespace Meziantou.Framework.JavaScriptEngine.Parsing;

internal sealed partial class Parser
{
    /// <summary>Parses a function declaration; the current token is <c>function</c> (after <c>async</c>).</summary>
    /// <param name="start">The start of the declaration (the <c>async</c> keyword, if any).</param>
    /// <param name="isAsync">Whether the declaration starts with <c>async</c>.</param>
    /// <param name="isHanging">Whether the declaration is the body of an if statement (Annex B): its name is not declared in the enclosing scope.</param>
    /// <param name="isAnnexB">Whether the declaration is in a position allowed only by the Annex B (the body of an if statement, a labelled statement): it cannot be a generator.</param>
    /// <param name="allowAnonymous">Whether the name is optional (<c>export default function () {}</c>).</param>
    private FunctionDeclaration ParseFunctionStatement(int start, bool isAsync, bool isHanging, bool isAnnexB, bool allowAnonymous = false)
    {
        Next();
        var isGenerator = Eat(TokenKind.Star);
        if (isGenerator && isAnnexB)
            Raise(start, "Generators can only be declared at the top level or inside a block");

        Identifier? id = null;
        if (_token.Kind == TokenKind.Identifier)
        {
            id = ParseBindingIdentifier();
            if (!isHanging)
            {
                var kind = Strict || isGenerator || isAsync ? (TreatFunctionsAsVar ? BindingKind.Var : BindingKind.Lexical) : BindingKind.Function;
                CheckLValSimple(id, kind);
            }
        }
        else if (!allowAnonymous)
        {
            Unexpected();
        }

        var function = ParseFunctionRest(start, id, isAsync, isGenerator, isExpression: false);
        return FinishAt(new FunctionDeclaration { Function = function }, start, function.End);
    }

    /// <summary>Parses a function expression; the current token is <c>function</c>.</summary>
    private FunctionExpression ParseFunctionExpression(int start, bool isAsync)
    {
        Next();
        var isGenerator = Eat(TokenKind.Star);
        var function = ParseFunctionRest(start, id: null, isAsync, isGenerator, isExpression: true);
        return FinishAt(new FunctionExpression { Function = function }, start, function.End);
    }

    private FunctionNode ParseFunctionRest(int start, Identifier? id, bool isAsync, bool isGenerator, bool isExpression)
    {
        var oldYieldPos = _yieldPos;
        var oldAwaitPos = _awaitPos;
        var oldAwaitIdentPos = _awaitIdentPos;
        _yieldPos = -1;
        _awaitPos = -1;
        _awaitIdentPos = -1;
        EnterScope(GetFunctionFlags(isAsync, isGenerator));
        if (isExpression && _token.Kind == TokenKind.Identifier)
        {
            id = ParseBindingIdentifier();
        }

        var function = new FunctionNode { Id = id, IsAsync = isAsync, IsGenerator = isGenerator, Kind = FunctionKind.Normal, Start = start };
        Expect(TokenKind.LeftParen);
        ParseBindingList(function.Params, TokenKind.RightParen, allowEmpty: false, allowTrailingComma: true);
        CheckYieldAwaitInDefaultParams();
        ParseFunctionBody(function, isArrow: false, isMethod: false, noIn: false);
        _yieldPos = oldYieldPos;
        _awaitPos = oldAwaitPos;
        _awaitIdentPos = oldAwaitIdentPos;
        function.End = _prevEnd;
        return function;
    }

    private static ScopeFlags GetFunctionFlags(bool isAsync, bool isGenerator)
    {
        var flags = ScopeFlags.Function;
        if (isAsync)
        {
            flags |= ScopeFlags.Async;
        }

        if (isGenerator)
        {
            flags |= ScopeFlags.Generator;
        }

        return flags;
    }

    /// <summary>Parses the parameters and the body of a method, a getter, a setter or a class constructor; the current token is '('.</summary>
    private FunctionNode ParseMethod(int start, bool isGenerator, bool isAsync, FunctionKind kind, bool allowDirectSuper)
    {
        var oldYieldPos = _yieldPos;
        var oldAwaitPos = _awaitPos;
        var oldAwaitIdentPos = _awaitIdentPos;
        _yieldPos = -1;
        _awaitPos = -1;
        _awaitIdentPos = -1;
        var flags = GetFunctionFlags(isAsync, isGenerator) | ScopeFlags.Super;
        if (allowDirectSuper)
        {
            flags |= ScopeFlags.DirectSuper;
        }

        EnterScope(flags);
        var function = new FunctionNode { Kind = kind, IsAsync = isAsync, IsGenerator = isGenerator, Start = start };
        var parametersStart = _token.Start;
        Expect(TokenKind.LeftParen);
        ParseBindingList(function.Params, TokenKind.RightParen, allowEmpty: false, allowTrailingComma: true);
        CheckYieldAwaitInDefaultParams();
        if (kind == FunctionKind.Getter && function.Params.Count != 0)
            Raise(parametersStart, "Getter must not have any formal parameters");

        if (kind == FunctionKind.Setter)
        {
            if (function.Params.Count != 1)
                Raise(parametersStart, "Setter must have exactly one formal parameter");

            if (function.Params[0] is RestElement)
                Raise(function.Params[0].Start, "Setter function argument must not be a rest parameter");
        }

        ParseFunctionBody(function, isArrow: false, isMethod: true, noIn: false);
        _yieldPos = oldYieldPos;
        _awaitPos = oldAwaitPos;
        _awaitIdentPos = oldAwaitIdentPos;
        function.End = _prevEnd;
        return function;
    }

    private ArrowFunctionExpression ParseArrowExpression(int start, List<Node> parameters, bool isAsync, bool noIn)
    {
        var oldYieldPos = _yieldPos;
        var oldAwaitPos = _awaitPos;
        var oldAwaitIdentPos = _awaitIdentPos;
        _yieldPos = -1;
        _awaitPos = -1;
        _awaitIdentPos = -1;
        EnterScope(GetFunctionFlags(isAsync, isGenerator: false) | ScopeFlags.Arrow);
        var function = new FunctionNode { Kind = FunctionKind.Arrow, IsAsync = isAsync, Start = start };
        ToAssignableList(parameters, isBinding: true);
        function.Params = parameters;
        ParseFunctionBody(function, isArrow: true, isMethod: false, noIn);
        _yieldPos = oldYieldPos;
        _awaitPos = oldAwaitPos;
        _awaitIdentPos = oldAwaitIdentPos;
        function.End = _prevEnd;
        return FinishAt(new ArrowFunctionExpression { Function = function }, start, function.End);
    }

    private static bool IsSimpleParameterList(List<Node> parameters)
    {
        foreach (var parameter in parameters)
        {
            if (parameter.Type != NodeType.Identifier)
                return false;
        }

        return true;
    }

    /// <summary>Parses the body of a function (the scope of the function is the current scope, and it is exited).</summary>
    private void ParseFunctionBody(FunctionNode function, bool isArrow, bool isMethod, bool noIn)
    {
        var oldStrict = Strict;
        var oldLabelsBase = _labelsBase;
        _labelsBase = _labels.Count;
        var isSimple = IsSimpleParameterList(function.Params);
        function.HasSimpleParameterList = isSimple;
        if (isArrow && _token.Kind != TokenKind.LeftBrace)
        {
            function.Body = ParseAssignment(noIn);
            function.IsExpressionBody = true;
            function.IsStrict = Strict;
            CheckParams(function, allowDuplicates: false);
        }
        else
        {
            var bodyStart = _token.Start;
            Expect(TokenKind.LeftBrace);
            var block = new BlockStatement();
            ParseFunctionStatements(block.Body, function, isArrow, isMethod, oldStrict, TokenKind.RightBrace);
            function.IsStrict = Strict;
            Strict = oldStrict;
            Next();
            function.Body = Finish(block, bodyStart);
        }

        _labelsBase = oldLabelsBase;
        ExitScope();
    }

    /// <summary>
    /// Parses the directive prologue and the statements of a function body until <paramref name="end"/>, checks the
    /// parameters (once the strictness of the function is known) and returns whether the body has a 'use strict' directive.
    /// </summary>
    private bool ParseFunctionStatements(List<Node> body, FunctionNode function, bool isArrow, bool isMethod, bool oldStrict, TokenKind end)
    {
        var octalPosition = -1;
        var useStrict = false;
        while (_token.Kind == TokenKind.StringLiteral)
        {
            var stringToken = _token;
            var statement = ParseStatementListItem();
            body.Add(statement);
            if (!ProcessDirective(statement, stringToken, ref octalPosition, out var isUseStrict))
                break;

            if (isUseStrict)
            {
                useStrict = true;
                if (!function.HasSimpleParameterList)
                    Raise(stringToken.Start, "Illegal 'use strict' directive in function with non-simple parameter list");

                if (!Strict)
                {
                    Strict = true;
                    _token = _lexer.Rescan(_token);
                }
            }
        }

        CheckParams(function, allowDuplicates: !oldStrict && !useStrict && !isArrow && !isMethod && function.HasSimpleParameterList);
        if (Strict && function.Id is not null)
        {
            CheckLValSimple(function.Id, BindingKind.Outside);
        }

        while (_token.Kind != end)
        {
            if (_token.Kind == TokenKind.EndOfFile)
                Unexpected();

            body.Add(ParseStatementListItem());
        }

        return useStrict;
    }

    private void CheckParams(FunctionNode function, bool allowDuplicates)
    {
        // The parameters are checked after they are parsed: the set is never used by two functions at the same time
        HashSet<string>? names = null;
        if (!allowDuplicates && function.Params.Count > 0)
        {
            names = _parameterNames;
            names.Clear();
        }

        foreach (var parameter in function.Params)
        {
            CheckLValInnerPattern(parameter, BindingKind.Var, names);
        }
    }

    private readonly HashSet<string> _parameterNames = new(StringComparer.Ordinal);

    private void ParseBindingList<T>(List<T> elements, TokenKind close, bool allowEmpty, bool allowTrailingComma)
        where T : Node?
    {
        var first = true;
        while (!Eat(close))
        {
            if (first)
            {
                first = false;
            }
            else
            {
                Expect(TokenKind.Comma);
            }

            if (allowEmpty && _token.Kind == TokenKind.Comma)
            {
                elements.Add(default!);
            }
            else if (allowTrailingComma && _token.Kind == close)
            {
                Next();
                break;
            }
            else if (_token.Kind == TokenKind.Ellipsis)
            {
                elements.Add((T)(Node)ParseRestBinding());
                if (_token.Kind == TokenKind.Comma)
                    Raise(_token.Start, "Comma is not permitted after the rest element");

                Expect(close);
                break;
            }
            else
            {
                elements.Add((T)ParseMaybeDefault(_token.Start, left: null));
            }
        }
    }

    // Classes

    private ClassDeclaration ParseClassDeclaration(int start, bool allowAnonymous)
    {
        var node = ParseClass(start, isStatement: true, allowAnonymous);
        return FinishAt(new ClassDeclaration { Class = node }, start, node.End);
    }

    private ClassExpression ParseClassExpression(int start)
    {
        var node = ParseClass(start, isStatement: false, allowAnonymous: true);
        return FinishAt(new ClassExpression { Class = node }, start, node.End);
    }

    private ClassNode ParseClass(int start, bool isStatement, bool allowAnonymous)
    {
        Next();
        var oldStrict = Strict;
        Strict = true;
        var node = new ClassNode { Start = start };
        if (_token.Kind == TokenKind.Identifier)
        {
            node.Id = ParseBindingIdentifier();
            CheckLValSimple(node.Id, isStatement ? BindingKind.Lexical : BindingKind.Outside);
        }
        else if (isStatement && !allowAnonymous)
        {
            Unexpected();
        }

        if (Eat(TokenKind.Extends))
        {
            node.SuperClass = ParseExpressionSubscripts(errors: null, noIn: false);
        }

        var privateNames = EnterClassBody();
        Expect(TokenKind.LeftBrace);
        var hadConstructor = false;
        while (_token.Kind != TokenKind.RightBrace)
        {
            if (_token.Kind == TokenKind.EndOfFile)
                Unexpected();

            var element = ParseClassElement(node.SuperClass is not null, privateNames);
            if (element is null)
                continue;

            if (element is MethodDefinition { Kind: MethodKind.Constructor })
            {
                if (hadConstructor)
                    Raise(element.Start, "A class may only have one constructor");

                hadConstructor = true;
            }

            node.Body.Add(element);
        }

        Strict = oldStrict;
        Next();
        ExitClassBody();
        node.End = _prevEnd;
        return node;
    }

    private bool IsClassElementNameStart() =>
        _token.Kind is TokenKind.Identifier or TokenKind.PrivateName or TokenKind.NumericLiteral or TokenKind.BigIntLiteral or TokenKind.StringLiteral or TokenKind.LeftBracket || _token.IsKeyword;

    private static bool IsKeyName(Node key, string name) => key switch
    {
        Identifier identifier => identifier.Name == name,
        StringLiteral literal => literal.Value == name,
        _ => false,
    };

    private Node? ParseClassElement(bool hasSuperClass, PrivateNameScope privateNames)
    {
        if (Eat(TokenKind.Semicolon))
            return null;

        var start = _token.Start;
        string? keyName = null;
        var keyStart = 0;
        var keyEnd = 0;
        var isGenerator = false;
        var isAsync = false;
        var isStatic = false;
        var kind = MethodKind.Method;
        var methodStart = start;
        if (IsContextual("static"))
        {
            keyStart = _token.Start;
            keyEnd = _token.End;
            Next();
            if (_token.Kind == TokenKind.LeftBrace)
                return ParseClassStaticBlock(start);

            if (IsClassElementNameStart() || _token.Kind == TokenKind.Star)
            {
                isStatic = true;
                methodStart = _token.Start;
            }
            else
            {
                keyName = "static";
            }
        }

        if (keyName is null && IsContextual("async"))
        {
            keyStart = _token.Start;
            keyEnd = _token.End;
            Next();
            if ((IsClassElementNameStart() || _token.Kind == TokenKind.Star) && !_token.NewLineBefore)
            {
                isAsync = true;
            }
            else
            {
                keyName = "async";
            }
        }

        if (keyName is null && Eat(TokenKind.Star))
        {
            isGenerator = true;
        }

        if (keyName is null && !isAsync && !isGenerator && (IsContextual("get") || IsContextual("set")))
        {
            var name = _token.Value!;
            keyStart = _token.Start;
            keyEnd = _token.End;
            Next();
            if (IsClassElementNameStart())
            {
                kind = name == "get" ? MethodKind.Get : MethodKind.Set;
            }
            else
            {
                keyName = name;
            }
        }

        Node key;
        bool computed;
        if (keyName is not null)
        {
            key = FinishAt(new Identifier { Name = keyName }, keyStart, keyEnd);
            computed = false;
        }
        else if (_token.Kind == TokenKind.PrivateName)
        {
            if (_token.Value == "constructor")
                Raise(_token.Start, "Classes may not have a private field named '#constructor'");

            var privateStart = _token.Start;
            var privateName = _token.Value!;
            Next();
            key = Finish(new PrivateIdentifier { Name = privateName }, privateStart);
            computed = false;
        }
        else
        {
            key = ParsePropertyName(out computed);
        }

        if (_token.Kind == TokenKind.LeftParen || kind != MethodKind.Method || isGenerator || isAsync)
        {
            var isConstructor = !isStatic && !computed && IsKeyName(key, "constructor");
            if (isConstructor)
            {
                if (kind != MethodKind.Method)
                    Raise(key.Start, "Class constructor may not be an accessor");

                if (isGenerator)
                    Raise(key.Start, "Class constructor may not be a generator");

                if (isAsync)
                    Raise(key.Start, "Class constructor may not be an async method");
            }
            else if (isStatic && !computed && IsKeyName(key, "prototype"))
            {
                Raise(key.Start, "Classes may not have a static property named 'prototype'");
            }

            var functionKind = isConstructor ? FunctionKind.ClassConstructor : kind switch
            {
                MethodKind.Get => FunctionKind.Getter,
                MethodKind.Set => FunctionKind.Setter,
                _ => FunctionKind.Method,
            };

            var function = ParseMethod(methodStart, isGenerator, isAsync, functionKind, allowDirectSuper: isConstructor && hasSuperClass);
            function.IsDerivedConstructor = isConstructor && hasSuperClass;
            var method = Finish(new MethodDefinition { Key = key, Value = function, Kind = isConstructor ? MethodKind.Constructor : kind, Static = isStatic, Computed = computed }, start);
            if (key is PrivateIdentifier privateKey)
            {
                DeclarePrivateName(privateNames, privateKey, kind switch
                {
                    MethodKind.Get => isStatic ? PrivateNameKind.StaticGetter : PrivateNameKind.Getter,
                    MethodKind.Set => isStatic ? PrivateNameKind.StaticSetter : PrivateNameKind.Setter,
                    _ => PrivateNameKind.Other,
                });
            }

            return method;
        }

        if (!computed && IsKeyName(key, "constructor"))
            Raise(key.Start, "Classes may not have a field named 'constructor'");

        if (isStatic && !computed && IsKeyName(key, "prototype"))
            Raise(key.Start, "Classes may not have a static property named 'prototype'");

        if (key is PrivateIdentifier privateFieldKey)
        {
            DeclarePrivateName(privateNames, privateFieldKey, PrivateNameKind.Other);
        }

        Node? value = null;
        if (Eat(TokenKind.Equal))
        {
            var oldYieldPos = _yieldPos;
            var oldAwaitPos = _awaitPos;
            var oldAwaitIdentPos = _awaitIdentPos;
            var oldLabelsBase = _labelsBase;
            _labelsBase = _labels.Count;
            EnterScope(ScopeFlags.ClassFieldInit | ScopeFlags.Super);
            value = ParseAssignment();
            ExitScope();
            _labelsBase = oldLabelsBase;
            _yieldPos = oldYieldPos;
            _awaitPos = oldAwaitPos;
            _awaitIdentPos = oldAwaitIdentPos;
        }

        Semicolon();
        return Finish(new PropertyDefinition { Key = key, Value = value, Static = isStatic, Computed = computed }, start);
    }

    private StaticBlock ParseClassStaticBlock(int start)
    {
        Next();
        var oldLabelsBase = _labelsBase;
        _labelsBase = _labels.Count;
        var oldYieldPos = _yieldPos;
        var oldAwaitPos = _awaitPos;
        var oldAwaitIdentPos = _awaitIdentPos;
        EnterScope(ScopeFlags.ClassStaticBlock | ScopeFlags.Super);
        var block = new StaticBlock();
        while (_token.Kind != TokenKind.RightBrace)
        {
            if (_token.Kind == TokenKind.EndOfFile)
                Unexpected();

            block.Body.Add(ParseStatementListItem());
        }

        ExitScope();
        _yieldPos = oldYieldPos;
        _awaitPos = oldAwaitPos;
        _awaitIdentPos = oldAwaitIdentPos;
        _labelsBase = oldLabelsBase;
        Next();
        return Finish(block, start);
    }

    // Private names

    private enum PrivateNameKind
    {
        Other,
        Getter,
        Setter,
        StaticGetter,
        StaticSetter,
        GetterSetter,
    }

    private sealed class PrivateNameScope
    {
        public readonly Dictionary<string, PrivateNameKind> Declared = new(StringComparer.Ordinal);
        public readonly List<PrivateIdentifier> Used = [];
    }

    private readonly List<PrivateNameScope> _privateNameScopes = [];

    private PrivateNameScope EnterClassBody()
    {
        var scope = new PrivateNameScope();
        _privateNameScopes.Add(scope);
        return scope;
    }

    private void ExitClassBody()
    {
        var scope = _privateNameScopes[^1];
        _privateNameScopes.RemoveAt(_privateNameScopes.Count - 1);
        var parent = _privateNameScopes.Count > 0 ? _privateNameScopes[^1] : null;
        foreach (var used in scope.Used)
        {
            if (scope.Declared.ContainsKey(used.Name))
                continue;

            if (parent is not null)
            {
                parent.Used.Add(used);
            }
            else if (_options.PrivateNames is null || !_options.PrivateNames.Contains(used.Name))
            {
                Raise(used.Start, "Private field '#" + used.Name + "' must be declared in an enclosing class");
            }
        }
    }

    private void DeclarePrivateName(PrivateNameScope scope, PrivateIdentifier key, PrivateNameKind kind)
    {
        if (!scope.Declared.TryGetValue(key.Name, out var existing))
        {
            scope.Declared[key.Name] = kind;
            return;
        }

        if ((existing == PrivateNameKind.Getter && kind == PrivateNameKind.Setter) ||
            (existing == PrivateNameKind.Setter && kind == PrivateNameKind.Getter) ||
            (existing == PrivateNameKind.StaticGetter && kind == PrivateNameKind.StaticSetter) ||
            (existing == PrivateNameKind.StaticSetter && kind == PrivateNameKind.StaticGetter))
        {
            scope.Declared[key.Name] = PrivateNameKind.GetterSetter;
            return;
        }

        Raise(key.Start, "Identifier '#" + key.Name + "' has already been declared");
    }

    /// <summary>Parses a reference to a private name (<c>obj.#x</c>, <c>#x in obj</c>).</summary>
    private PrivateIdentifier ParsePrivateIdentifierUse()
    {
        var start = _token.Start;
        var name = _token.Value!;
        Next();
        var identifier = Finish(new PrivateIdentifier { Name = name }, start);
        if (_privateNameScopes.Count == 0)
        {
            if (_options.PrivateNames is null || !_options.PrivateNames.Contains(name))
                Raise(start, "Private field '#" + name + "' must be declared in an enclosing class");
        }
        else
        {
            _privateNameScopes[^1].Used.Add(identifier);
        }

        return identifier;
    }
}
