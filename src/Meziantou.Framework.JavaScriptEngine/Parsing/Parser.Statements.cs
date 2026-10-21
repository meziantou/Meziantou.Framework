using Meziantou.Framework.JavaScriptEngine.Ast;

namespace Meziantou.Framework.JavaScriptEngine.Parsing;

internal sealed partial class Parser
{
    private enum StatementContext : byte
    {
        /// <summary>A StatementListItem: the declarations are allowed.</summary>
        Declaration,

        /// <summary>A StatementListItem of a case clause: the declarations are allowed, except <c>using</c>.</summary>
        CaseClause,

        /// <summary>A Statement (e.g. the body of a loop): no declarations.</summary>
        Statement,

        /// <summary>The body of an if statement: a function declaration is allowed in sloppy mode (Annex B).</summary>
        IfBody,

        /// <summary>The body of a label in a StatementListItem: a function declaration is allowed in sloppy mode (Annex B).</summary>
        Label,

        /// <summary>The body of a label in a Statement: no declarations.</summary>
        LabelInStatement,
    }

    private Node ParseStatementListItem(bool topLevel = false) => ParseStatement(StatementContext.Declaration, topLevel);

    private Node ParseStatement(StatementContext context, bool topLevel = false)
    {
        EnterRecursion();
        var start = _token.Start;
        var isDeclarationContext = context is StatementContext.Declaration or StatementContext.CaseClause;
        switch (_token.Kind)
        {
            case TokenKind.Break:
            case TokenKind.Continue:
                return ParseBreakContinue(start, _token.Kind == TokenKind.Break);
            case TokenKind.Debugger:
                Next();
                Semicolon();
                return Finish(new DebuggerStatement(), start);
            case TokenKind.Do:
                return ParseDoWhile(start);
            case TokenKind.For:
                return ParseFor(start);
            case TokenKind.Function:
                if (!isDeclarationContext && (Strict || (context != StatementContext.IfBody && context != StatementContext.Label)))
                {
                    Raise(start, Strict ? "In strict mode code, functions can only be declared at top level or inside a block" : "In non-strict mode code, functions can only be declared at top level, inside a block, or as the body of an if statement");
                }

                return ParseFunctionStatement(start, isAsync: false, isHanging: context == StatementContext.IfBody, isAnnexB: !isDeclarationContext);
            case TokenKind.Class:
                if (!isDeclarationContext)
                    Unexpected();

                return ParseClassDeclaration(start, allowAnonymous: false);
            case TokenKind.If:
                return ParseIf(start);
            case TokenKind.Return:
                return ParseReturn(start);
            case TokenKind.Switch:
                return ParseSwitch(start);
            case TokenKind.Throw:
                return ParseThrow(start);
            case TokenKind.Try:
                return ParseTry(start);
            case TokenKind.Const:
            case TokenKind.Var:
            {
                var kind = _token.Kind == TokenKind.Var ? VariableDeclarationKind.Var : VariableDeclarationKind.Const;
                if (kind != VariableDeclarationKind.Var && !isDeclarationContext)
                    Raise(start, "Lexical declaration cannot appear in a single-statement context");

                Next();
                var declaration = ParseVar(start, kind, isFor: false);
                Semicolon();
                return Finish(declaration, start);
            }

            case TokenKind.While:
                return ParseWhile(start);
            case TokenKind.With:
                return ParseWith(start);
            case TokenKind.LeftBrace:
                return ParseBlock(createScope: true);
            case TokenKind.Semicolon:
                Next();
                return Finish(new EmptyStatement(), start);
            case TokenKind.Import:
            {
                var next = PeekToken();
                if (next.Kind is TokenKind.LeftParen or TokenKind.Dot)
                    break;

                if (!_isModule)
                    Raise(start, "Cannot use import statement outside a module");

                if (!topLevel)
                    Raise(start, "An import declaration can only be used at the top level of a module");

                return ParseImportDeclaration(start);
            }

            case TokenKind.Export:
                if (!_isModule)
                    Raise(start, "Unexpected token 'export'");

                if (!topLevel)
                    Raise(start, "An export declaration can only be used at the top level of a module");

                return ParseExportDeclaration(start);
            case TokenKind.Identifier:
                if (IsContextual("let"))
                {
                    var next = PeekToken();
                    if (isDeclarationContext ? next.Kind is TokenKind.Identifier or TokenKind.LeftBracket or TokenKind.LeftBrace : next.Kind == TokenKind.LeftBracket)
                    {
                        if (!isDeclarationContext)
                            Raise(start, "Lexical declaration cannot appear in a single-statement context");

                        Next();
                        var declaration = ParseVar(start, VariableDeclarationKind.Let, isFor: false);
                        Semicolon();
                        return Finish(declaration, start);
                    }
                }
                else if (IsContextual("async"))
                {
                    var next = PeekToken();
                    if (next.Kind == TokenKind.Function && !next.NewLineBefore)
                    {
                        if (!isDeclarationContext)
                            Raise(start, "Async functions can only be declared at the top level or inside a block");

                        Next();
                        return ParseFunctionStatement(start, isAsync: true, isHanging: false, isAnnexB: false);
                    }
                }
                else if (IsContextual("using"))
                {
                    var next = PeekToken();
                    if (next.Kind == TokenKind.Identifier && !next.NewLineBefore)
                    {
                        CheckUsingDeclarationPosition(start, context);
                        Next();
                        var declaration = ParseVar(start, VariableDeclarationKind.Using, isFor: false);
                        Semicolon();
                        return Finish(declaration, start);
                    }
                }
                else if (IsContextual("await") && CanAwait && IsAwaitUsingDeclaration())
                {
                    CheckUsingDeclarationPosition(start, context);
                    Next();
                    Next();
                    var declaration = ParseVar(start, VariableDeclarationKind.AwaitUsing, isFor: false);
                    Semicolon();
                    return Finish(declaration, start);
                }

                break;
        }

        // Expression statement or labelled statement
        var startsWithIdentifier = _token.Kind == TokenKind.Identifier;
        var expression = ParseExpression();
        if (startsWithIdentifier && expression is Identifier identifier && identifier.Start == start && identifier.End == _prevEnd && _token.Kind == TokenKind.Colon)
        {
            Next();
            return ParseLabeledStatement(start, identifier, context);
        }

        Semicolon();
        return Finish(new ExpressionStatement { Expression = expression }, start);
    }

    /// <summary>Gets whether the current <c>await</c> token starts <c>await using x</c>.</summary>
    private bool IsAwaitUsingDeclaration()
    {
        var next = PeekToken();
        if (!IsContextual(next, "using") || next.NewLineBefore)
            return false;

        var nextNext = _lexer.Peek(next.End);
        return nextNext.Kind == TokenKind.Identifier && !nextNext.NewLineBefore;
    }

    private void CheckUsingDeclarationPosition(int start, StatementContext context)
    {
        if (context == StatementContext.CaseClause)
            Raise(start, "Using declarations are not allowed directly in a case clause");

        if (context != StatementContext.Declaration)
            Raise(start, "Lexical declaration cannot appear in a single-statement context");

        if (!_isModule && _scopeCount == 1)
            Raise(start, "Using declarations are not allowed at the top level of a script");
    }

    private BlockStatement ParseBlock(bool createScope)
    {
        var start = _token.Start;
        Expect(TokenKind.LeftBrace);
        if (createScope)
        {
            EnterScope(ScopeFlags.None);
        }

        var block = new BlockStatement();
        while (_token.Kind != TokenKind.RightBrace)
        {
            if (_token.Kind == TokenKind.EndOfFile)
                Unexpected();

            block.Body.Add(ParseStatementListItem());
        }

        if (createScope)
        {
            ExitScope();
        }

        Next();
        return Finish(block, start);
    }

    private Node ParseBreakContinue(int start, bool isBreak)
    {
        Next();
        Identifier? label = null;
        if (!Eat(TokenKind.Semicolon) && !CanInsertSemicolon())
        {
            if (_token.Kind != TokenKind.Identifier)
                Unexpected();

            label = ParseIdentifierReference();
            Semicolon();
        }

        var found = false;
        for (var i = _labels.Count - 1; i >= _labelsBase; i--)
        {
            var current = _labels[i];
            if (label is null)
            {
                if (current.Kind != LabelKind.None && (isBreak || current.Kind == LabelKind.Loop))
                {
                    found = true;
                    break;
                }
            }
            else if (current.Name == label.Name)
            {
                if (isBreak || current.Kind == LabelKind.Loop)
                {
                    found = true;
                    break;
                }

                Raise(start, "Illegal continue statement: '" + label.Name + "' does not denote an iteration statement");
            }
        }

        if (!found)
        {
            if (label is not null)
                Raise(label.Start, "Undefined label '" + label.Name + "'");

            Raise(start, isBreak ? "Illegal break statement" : "Illegal continue statement: no surrounding iteration statement");
        }

        return isBreak ? Finish(new BreakStatement { Label = label }, start) : Finish(new ContinueStatement { Label = label }, start);
    }

    private DoWhileStatement ParseDoWhile(int start)
    {
        Next();
        _labels.Add(new Label { Kind = LabelKind.Loop });
        var body = ParseStatement(StatementContext.Statement);
        _labels.RemoveAt(_labels.Count - 1);
        Expect(TokenKind.While);
        var test = ParseParenthesizedExpression();
        Eat(TokenKind.Semicolon);
        return Finish(new DoWhileStatement { Body = body, Test = test }, start);
    }

    private WhileStatement ParseWhile(int start)
    {
        Next();
        var test = ParseParenthesizedExpression();
        _labels.Add(new Label { Kind = LabelKind.Loop });
        var body = ParseStatement(StatementContext.Statement);
        _labels.RemoveAt(_labels.Count - 1);
        return Finish(new WhileStatement { Test = test, Body = body }, start);
    }

    private WithStatement ParseWith(int start)
    {
        if (Strict)
            Raise(start, "Strict mode code may not include a with statement");

        Next();
        var obj = ParseParenthesizedExpression();
        var body = ParseStatement(StatementContext.Statement);
        return Finish(new WithStatement { Object = obj, Body = body }, start);
    }

    private IfStatement ParseIf(int start)
    {
        Next();
        var test = ParseParenthesizedExpression();
        var consequent = ParseStatement(StatementContext.IfBody);
        Node? alternate = null;
        if (Eat(TokenKind.Else))
        {
            alternate = ParseStatement(StatementContext.IfBody);
        }

        return Finish(new IfStatement { Test = test, Consequent = consequent, Alternate = alternate }, start);
    }

    private ReturnStatement ParseReturn(int start)
    {
        if (!InFunction)
            Raise(start, "Illegal return statement");

        Next();
        Node? argument = null;
        if (!Eat(TokenKind.Semicolon) && !CanInsertSemicolon())
        {
            argument = ParseExpression();
            Semicolon();
        }

        return Finish(new ReturnStatement { Argument = argument }, start);
    }

    private ThrowStatement ParseThrow(int start)
    {
        Next();
        if (_token.NewLineBefore)
            Raise(_prevEnd, "Illegal newline after throw");

        var argument = ParseExpression();
        Semicolon();
        return Finish(new ThrowStatement { Argument = argument }, start);
    }

    private SwitchStatement ParseSwitch(int start)
    {
        Next();
        var discriminant = ParseParenthesizedExpression();
        var node = new SwitchStatement { Discriminant = discriminant };
        Expect(TokenKind.LeftBrace);
        _labels.Add(new Label { Kind = LabelKind.Switch });
        EnterScope(ScopeFlags.None);
        var sawDefault = false;
        SwitchCase? current = null;
        while (_token.Kind != TokenKind.RightBrace)
        {
            if (_token.Kind is TokenKind.Case or TokenKind.Default)
            {
                if (current is not null)
                {
                    current.End = _prevEnd;
                }

                var caseStart = _token.Start;
                current = new SwitchCase { Start = caseStart };
                if (_token.Kind == TokenKind.Case)
                {
                    Next();
                    current.Test = ParseExpression();
                }
                else
                {
                    if (sawDefault)
                        Raise(caseStart, "More than one default clause in switch statement");

                    sawDefault = true;
                    Next();
                }

                Expect(TokenKind.Colon);
                current.End = _prevEnd;
                node.Cases.Add(current);
            }
            else
            {
                if (current is null)
                    Unexpected();

                current.Consequent.Add(ParseStatement(StatementContext.CaseClause));
                current.End = _prevEnd;
            }
        }

        ExitScope();
        _labels.RemoveAt(_labels.Count - 1);
        Next();
        return Finish(node, start);
    }

    private TryStatement ParseTry(int start)
    {
        Next();
        var block = ParseBlock(createScope: true);
        CatchClause? handler = null;
        if (_token.Kind == TokenKind.Catch)
        {
            var catchStart = _token.Start;
            Next();
            Node? param = null;
            if (Eat(TokenKind.LeftParen))
            {
                param = ParseBindingAtom();
                var simple = param is Identifier;
                EnterScope(simple ? ScopeFlags.SimpleCatch : ScopeFlags.None);
                CheckLValPattern(param, simple ? BindingKind.SimpleCatch : BindingKind.Lexical);
                Expect(TokenKind.RightParen);
            }
            else
            {
                EnterScope(ScopeFlags.None);
            }

            var body = ParseBlock(createScope: false);
            ExitScope();
            handler = Finish(new CatchClause { Param = param, Body = body }, catchStart);
        }

        BlockStatement? finalizer = null;
        if (Eat(TokenKind.Finally))
        {
            finalizer = ParseBlock(createScope: true);
        }

        if (handler is null && finalizer is null)
            Raise(_token.Start, "Missing catch or finally after try");

        return Finish(new TryStatement { Block = block, Handler = handler, Finalizer = finalizer }, start);
    }

    private LabeledStatement ParseLabeledStatement(int start, Identifier label, StatementContext context)
    {
        for (var i = _labelsBase; i < _labels.Count; i++)
        {
            if (_labels[i].Name == label.Name)
                Raise(label.Start, "Label '" + label.Name + "' has already been declared");
        }

        var kind = _token.Kind switch
        {
            TokenKind.For or TokenKind.While or TokenKind.Do => LabelKind.Loop,
            TokenKind.Switch => LabelKind.Switch,
            _ => LabelKind.None,
        };

        for (var i = _labels.Count - 1; i >= _labelsBase; i--)
        {
            var previous = _labels[i];
            if (previous.StatementStart == start)
            {
                previous.StatementStart = _token.Start;
                previous.Kind = kind;
            }
            else
            {
                break;
            }
        }

        _labels.Add(new Label { Name = label.Name, Kind = kind, StatementStart = _token.Start });
        var bodyContext = context is StatementContext.Declaration or StatementContext.CaseClause or StatementContext.Label ? StatementContext.Label : StatementContext.LabelInStatement;
        var body = ParseStatement(bodyContext);
        _labels.RemoveAt(_labels.Count - 1);
        return Finish(new LabeledStatement { Label = label, Body = body }, start);
    }

    private Node ParseParenthesizedExpression()
    {
        Expect(TokenKind.LeftParen);
        var expression = ParseExpression();
        Expect(TokenKind.RightParen);
        return expression;
    }

    private VariableDeclaration ParseVar(int start, VariableDeclarationKind kind, bool isFor)
    {
        var declaration = new VariableDeclaration { Kind = kind, Start = start };
        while (true)
        {
            var declaratorStart = _token.Start;
            Node id;
            if (kind is VariableDeclarationKind.Using or VariableDeclarationKind.AwaitUsing)
            {
                if (_token.Kind != TokenKind.Identifier)
                    Unexpected();

                id = ParseBindingIdentifier();
            }
            else
            {
                id = ParseBindingAtom();
            }

            CheckLValPattern(id, kind == VariableDeclarationKind.Var ? BindingKind.Var : BindingKind.Lexical);
            Node? init = null;
            if (Eat(TokenKind.Equal))
            {
                init = ParseAssignment(noIn: isFor);
            }
            else
            {
                var isForInOf = isFor && (_token.Kind == TokenKind.In || IsContextual("of"));
                if (!isForInOf)
                {
                    if (kind == VariableDeclarationKind.Const)
                        Raise(_prevEnd, "Missing initializer in const declaration");

                    if (kind is VariableDeclarationKind.Using or VariableDeclarationKind.AwaitUsing)
                        Raise(_prevEnd, "Missing initializer in using declaration");

                    if (id is not Identifier)
                        Raise(_prevEnd, "Missing initializer in destructuring declaration");
                }
            }

            declaration.Declarations.Add(Finish(new VariableDeclarator { Id = id, Init = init }, declaratorStart));
            if (!Eat(TokenKind.Comma))
                break;
        }

        declaration.End = _prevEnd;
        return declaration;
    }

    private Node ParseFor(int start)
    {
        Next();
        var awaitAt = -1;
        if (IsContextual("await") && CanAwait)
        {
            awaitAt = _token.Start;
            Next();
        }

        _labels.Add(new Label { Kind = LabelKind.Loop });
        EnterScope(ScopeFlags.None);
        Expect(TokenKind.LeftParen);
        if (_token.Kind == TokenKind.Semicolon)
        {
            if (awaitAt >= 0)
                Unexpected(awaitAt);

            return ParseForRest(start, init: null);
        }

        var initStart = _token.Start;
        VariableDeclarationKind? declarationKind = null;
        var declarationTokens = 1;
        if (_token.Kind == TokenKind.Var)
        {
            declarationKind = VariableDeclarationKind.Var;
        }
        else if (_token.Kind == TokenKind.Const)
        {
            declarationKind = VariableDeclarationKind.Const;
        }
        else if (IsContextual("let"))
        {
            var next = PeekToken();
            if (next.Kind is TokenKind.Identifier or TokenKind.LeftBracket or TokenKind.LeftBrace)
            {
                declarationKind = VariableDeclarationKind.Let;
            }
        }
        else if (IsContextual("using"))
        {
            var next = PeekToken();
            if (next.Kind == TokenKind.Identifier && !next.NewLineBefore && !IsContextual(next, "of"))
            {
                declarationKind = VariableDeclarationKind.Using;
            }
            else if (IsContextual(next, "of") && !next.NewLineBefore)
            {
                // 'for (using of = ...' declares 'of'; 'for (using of x)' uses the identifier 'using'
                var nextNext = _lexer.Peek(next.End);
                if (nextNext.Kind is TokenKind.Equal or TokenKind.Semicolon)
                {
                    declarationKind = VariableDeclarationKind.Using;
                }
            }
        }
        else if (IsContextual("await") && CanAwait && IsAwaitUsingDeclaration())
        {
            declarationKind = VariableDeclarationKind.AwaitUsing;
            declarationTokens = 2;
        }

        if (declarationKind is { } kind)
        {
            for (var i = 0; i < declarationTokens; i++)
            {
                Next();
            }

            var declaration = ParseVar(initStart, kind, isFor: true);
            var isForIn = _token.Kind == TokenKind.In;
            if ((isForIn || IsContextual("of")) && declaration.Declarations.Count == 1)
            {
                if (awaitAt >= 0 && isForIn)
                    Unexpected(awaitAt);

                return ParseForInOf(start, declaration, isForIn, awaitAt >= 0);
            }

            if (awaitAt >= 0)
                Unexpected(awaitAt);

            return ParseForRest(start, declaration);
        }

        var startsWithLet = IsContextual("let");
        var containsEscape = _token.IsEscaped;
        var errors = new DestructuringErrors();
        var init = ParseExpression(noIn: true, errors, forAwait: awaitAt >= 0);
        var isOf = IsContextual("of");
        if (_token.Kind == TokenKind.In || isOf)
        {
            if (awaitAt >= 0 && !isOf)
                Unexpected(awaitAt);

            if (isOf && awaitAt < 0 && init is Identifier { Name: "async" } && init.Start == initStart && !containsEscape)
                Raise(init.Start, "The left-hand side of a for-of loop may not be 'async'");

            if (startsWithLet && isOf)
                Raise(init.Start, "The left-hand side of a for-of loop may not start with 'let'");

            if (isOf && init is SequenceExpression)
                Unexpected();

            if (!IsWebCompatCallTarget(init))
            {
                init = ToAssignable(init, isBinding: false, errors);
                CheckLValPattern(init, BindingKind.None);
            }

            return ParseForInOf(start, init, isForIn: !isOf, awaitAt >= 0);
        }

        CheckExpressionErrors(errors);
        if (awaitAt >= 0)
            Unexpected(awaitAt);

        return ParseForRest(start, init);
    }

    private ForStatement ParseForRest(int start, Node? init)
    {
        Expect(TokenKind.Semicolon);
        var test = _token.Kind == TokenKind.Semicolon ? null : ParseExpression();
        Expect(TokenKind.Semicolon);
        var update = _token.Kind == TokenKind.RightParen ? null : ParseExpression();
        Expect(TokenKind.RightParen);
        var body = ParseStatement(StatementContext.Statement);
        ExitScope();
        _labels.RemoveAt(_labels.Count - 1);
        return Finish(new ForStatement { Init = init, Test = test, Update = update, Body = body }, start);
    }

    private Node ParseForInOf(int start, Node left, bool isForIn, bool isAwait)
    {
        if (left is VariableDeclaration declaration)
        {
            var declarator = declaration.Declarations[0];
            if (declarator.Init is not null && (!isForIn || Strict || declaration.Kind != VariableDeclarationKind.Var || declarator.Id is not Identifier))
                Raise(declaration.Start, (isForIn ? "for-in" : "for-of") + " loop variable declaration may not have an initializer");

            if (isForIn && declaration.Kind is VariableDeclarationKind.Using or VariableDeclarationKind.AwaitUsing)
                Raise(declaration.Start, "Using declarations are not allowed in the head of a for-in loop");
        }

        Next();
        var right = isForIn ? ParseExpression() : ParseAssignment();
        Expect(TokenKind.RightParen);
        var body = ParseStatement(StatementContext.Statement);
        ExitScope();
        _labels.RemoveAt(_labels.Count - 1);
        return isForIn
            ? Finish(new ForInStatement { Left = left, Right = right, Body = body }, start)
            : Finish(new ForOfStatement { Left = left, Right = right, Body = body, IsAwait = isAwait }, start);
    }
}
