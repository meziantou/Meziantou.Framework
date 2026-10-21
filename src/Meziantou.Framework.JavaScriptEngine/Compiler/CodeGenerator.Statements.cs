using Meziantou.Framework.JavaScriptEngine.Ast;

namespace Meziantou.Framework.JavaScriptEngine.Compiler;

internal sealed partial class CodeGenerator
{
    // ------------------------------------------------------------------------------------------------------------------
    // Control flow: the targets of break, continue and return
    // ------------------------------------------------------------------------------------------------------------------

    private enum ControlKind
    {
        Loop,
        Switch,
        Label,
        Finally,
        ForOf,
    }

    private sealed class ControlScope
    {
        public required ControlKind Kind { get; init; }

        public ControlScope? Parent { get; init; }

        public HashSet<string> Labels { get; } = new(StringComparer.Ordinal);

        public Label BreakLabel { get; init; }

        public Label ContinueLabel { get; init; }

        /// <summary>Gets the scope at the target of a break.</summary>
        public required Scope BreakScope { get; init; }

        /// <summary>Gets the scope at the target of a continue.</summary>
        public Scope? ContinueScope { get; init; }

        public int IteratorRegister { get; init; } = -1;

        public bool IsAsyncIterator { get; init; }

        // Finally
        public int TypeRegister { get; init; } = -1;

        public int ValueRegister { get; init; } = -1;

        public Label FinallyLabel { get; init; }

        public List<(ControlScope? Target, bool IsContinue, int Id)> Jumps { get; } = [];
    }

    private const int CompletionNormal = 0;
    private const int CompletionThrow = 1;
    private const int CompletionReturn = 2;
    private const int CompletionFirstJump = 3;

    private ControlScope PushControl(ControlKind kind, Label breakLabel, Label continueLabel, Scope? continueScope, HashSet<string>? labels)
    {
        var control = new ControlScope
        {
            Kind = kind,
            Parent = _control,
            BreakLabel = breakLabel,
            ContinueLabel = continueLabel,
            BreakScope = _scope,
            ContinueScope = continueScope,
        };

        if (labels is not null)
        {
            control.Labels.UnionWith(labels);
        }

        _control = control;
        return control;
    }

    private void PopControl(ControlScope control)
    {
        _control = control.Parent;
    }

    /// <summary>Pops the environments from the current scope to a scope (exclusive).</summary>
    private void EmitPopScopesTo(Scope target)
    {
        for (var scope = _scope; scope is not null && scope != target; scope = scope.Parent)
        {
            if (scope.NeedsContext)
            {
                _builder.Emit(OpCode.PopScope);
            }
        }
    }

    /// <summary>Emits a break or a continue to a control scope: through the finally blocks and the for-of loops (which close their iterators).</summary>
    private void EmitJumpToControl(ControlScope target, bool isContinue)
    {
        var savedScope = _scope;
        for (var control = _control; control is not null && control != target; control = control.Parent)
        {
            if (control.Kind == ControlKind.ForOf)
            {
                // A continue of the for-of loop itself does not close its iterator
                if (isContinue && control.Parent == target)
                    continue;

                EmitPopScopesTo(control.BreakScope);
                _scope = control.BreakScope;
                _builder.Emit(OpCode.LoadLocal, control.IteratorRegister);
                EmitIteratorClose(control.IsAsyncIterator);
            }
            else if (control.Kind == ControlKind.Finally)
            {
                var id = CompletionFirstJump + control.Jumps.Count;
                control.Jumps.Add((target, isContinue, id));
                _builder.EmitPushNumber(id);
                _builder.Emit(OpCode.StoreLocal, control.TypeRegister);
                _builder.EmitJump(OpCode.Jump, control.FinallyLabel);
                _scope = savedScope;
                return;
            }
        }

        var targetScope = isContinue ? target.ContinueScope! : target.BreakScope;
        EmitPopScopesTo(targetScope);
        _builder.EmitJump(OpCode.Jump, isContinue ? target.ContinueLabel : target.BreakLabel);
        _scope = savedScope;
    }

    /// <summary>Emits a return of the value on the stack: through the finally blocks and the for-of loops.</summary>
    private void EmitReturn()
    {
        var savedScope = _scope;
        for (var control = _control; control is not null; control = control.Parent)
        {
            if (control.Kind == ControlKind.ForOf)
            {
                var value = _function.AllocateRegister();
                _builder.Emit(OpCode.StoreLocal, value);
                EmitPopScopesTo(control.BreakScope);
                _scope = control.BreakScope;
                _builder.Emit(OpCode.LoadLocal, control.IteratorRegister);
                EmitIteratorClose(control.IsAsyncIterator);
                _builder.Emit(OpCode.LoadLocal, value);
            }
            else if (control.Kind == ControlKind.Finally)
            {
                _builder.Emit(OpCode.StoreLocal, control.ValueRegister);
                _builder.EmitPushNumber(CompletionReturn);
                _builder.Emit(OpCode.StoreLocal, control.TypeRegister);
                _builder.EmitJump(OpCode.Jump, control.FinallyLabel);
                _scope = savedScope;
                return;
            }
        }

        _builder.Emit(OpCode.Return);
        _scope = savedScope;
    }

    private void EmitIteratorClose(bool isAsync)
    {
        if (!isAsync)
        {
            _builder.Emit(OpCode.IteratorClose);
            return;
        }

        EmitAsyncIteratorClose();
    }

    private ControlScope? FindBreakTarget(string? label)
    {
        for (var control = _control; control is not null; control = control.Parent)
        {
            if (label is null)
            {
                if (control.Kind is ControlKind.Loop or ControlKind.Switch)
                    return control;
            }
            else if (control.Labels.Contains(label))
            {
                return control;
            }
        }

        return null;
    }

    private ControlScope? FindContinueTarget(string? label)
    {
        for (var control = _control; control is not null; control = control.Parent)
        {
            if (control.Kind == ControlKind.Loop && (label is null || control.Labels.Contains(label)))
                return control;
        }

        return null;
    }

    // ------------------------------------------------------------------------------------------------------------------
    // Statements
    // ------------------------------------------------------------------------------------------------------------------

    private void EmitStatements(List<Node> statements)
    {
        foreach (var statement in statements)
        {
            EmitStatement(statement, labels: null);
        }
    }

    /// <summary>Sets the completion value of script and eval code to undefined (UpdateEmpty(..., undefined) of the statements that produce a value).</summary>
    private void ResetCompletion()
    {
        if (_completionRegister >= 0)
        {
            _builder.Emit(OpCode.PushUndefined);
            _builder.Emit(OpCode.StoreLocal, _completionRegister);
        }
    }

    private void EmitStatement(Node node, HashSet<string>? labels)
    {
        _builder.SetPosition(node.Start);
        switch (node)
        {
            case EmptyStatement:
                return;
            case DebuggerStatement:
                _builder.Emit(OpCode.Debugger);
                return;
            case ExpressionStatement statement:
                EmitExpression(statement.Expression);
                if (_completionRegister >= 0)
                {
                    _builder.Emit(OpCode.StoreLocal, _completionRegister);
                }
                else
                {
                    _builder.Emit(OpCode.Pop);
                }

                return;
            case VariableDeclaration declaration:
                EmitVariableDeclaration(declaration);
                return;
            case FunctionDeclaration declaration:
                EmitFunctionDeclarationStatement(declaration);
                return;
            case ClassDeclaration declaration:
                EmitClass(declaration.Class, declaration.Class.Id!.Name);
                EmitInitializeIdentifier(declaration.Class.Id);
                return;
            case BlockStatement block:
                EmitBlock(block, labels);
                return;
            case IfStatement ifStatement:
                EmitIf(ifStatement);
                return;
            case LabeledStatement labeled:
                EmitLabeled(labeled, labels);
                return;
            case WhileStatement whileStatement:
                EmitWhile(whileStatement, labels);
                return;
            case DoWhileStatement doWhile:
                EmitDoWhile(doWhile, labels);
                return;
            case ForStatement forStatement:
                EmitFor(forStatement, labels);
                return;
            case ForInStatement forIn:
                EmitForIn(forIn, labels);
                return;
            case ForOfStatement forOf:
                EmitForOf(forOf, labels);
                return;
            case BreakStatement breakStatement:
                EmitJumpToControl(FindBreakTarget(breakStatement.Label?.Name) ?? throw new InvalidOperationException("Invalid break"), isContinue: false);
                return;
            case ContinueStatement continueStatement:
                EmitJumpToControl(FindContinueTarget(continueStatement.Label?.Name) ?? throw new InvalidOperationException("Invalid continue"), isContinue: true);
                return;
            case ReturnStatement returnStatement:
                EmitReturnStatement(returnStatement);
                return;
            case ThrowStatement throwStatement:
                EmitExpression(throwStatement.Argument);
                _builder.SetPosition(throwStatement.Start);
                _builder.Emit(OpCode.Throw);
                return;
            case TryStatement tryStatement:
                EmitTry(tryStatement);
                return;
            case SwitchStatement switchStatement:
                EmitSwitch(switchStatement, labels);
                return;
            case WithStatement withStatement:
                EmitWith(withStatement);
                return;
            case ImportDeclaration or ExportAllDeclaration:
                return;
            case ExportNamedDeclaration export:
                if (export.Declaration is not null)
                {
                    EmitStatement(export.Declaration, labels: null);
                }

                return;
            case ExportDefaultDeclaration exportDefault:
                EmitExportDefault(exportDefault);
                return;
            default:
                throw new InvalidOperationException("Unexpected statement: " + node.Type);
        }
    }

    private void EmitFunctionDeclarationStatement(FunctionDeclaration declaration)
    {
        // Annex B.3.2: when the declaration is evaluated, the value of the block binding is copied to the var binding
        if (_function.AnnexBFunctions.Contains(declaration))
        {
            var name = declaration.Function.Id!.Name;
            var reference = _analysis.References[declaration.Function.Id];
            EmitLoadVariable(reference.Variable!, checkTdz: false);
            var varScope = _function.BodyVarScope ?? _function.Scope;
            var varBinding = varScope.Find(name)!;
            if (_function.Kind == FunctionInfoKind.Script)
            {
                _builder.Emit(OpCode.SetGlobalVar, GetNameKey(name));
            }
            else if (varBinding.Location == VariableLocation.Dynamic)
            {
                _builder.Emit(OpCode.SetVarBinding, GetNameKey(name));
            }
            else
            {
                EmitInitializeVariable(varBinding);
            }
        }
    }

    private void EmitBlock(BlockStatement block, HashSet<string>? labels)
    {
        var scope = _analysis.Scopes[block];
        var breakLabel = default(Label);
        ControlScope? control = null;
        if (labels is not null)
        {
            breakLabel = _builder.DefineLabel();
            control = PushControl(ControlKind.Label, breakLabel, default, continueScope: null, labels);
        }

        EnterScope(scope);
        EmitBlockFunctionDeclarations(block.Body, scope);
        EmitStatements(block.Body);
        ExitScope(scope);
        if (control is not null)
        {
            PopControl(control);
            _builder.MarkLabel(breakLabel);
        }
    }

    /// <summary>Instantiates the function declarations of a block when it is entered (BlockDeclarationInstantiation).</summary>
    private void EmitBlockFunctionDeclarations(List<Node> statements, Scope scope)
    {
        foreach (var statement in statements)
        {
            var declaration = statement switch
            {
                FunctionDeclaration f => f,
                LabeledStatement labeled => GetLabeledFunction(labeled),
                _ => null,
            };

            if (declaration is null)
                continue;

            var name = declaration.Function.Id!.Name;
            _builder.Emit(OpCode.CreateClosure, CompileNestedFunction(declaration.Function, name));
            EmitInitializeVariable(scope.Find(name)!);
        }
    }

    private void EmitVariableDeclaration(VariableDeclaration declaration)
    {
        foreach (var declarator in declaration.Declarations)
        {
            _builder.SetPosition(declarator.Start);
            if (declarator.Init is null)
            {
                // let x; initializes x to undefined; var x; does nothing
                if (declaration.Kind != VariableDeclarationKind.Var)
                {
                    _builder.Emit(OpCode.PushUndefined);
                    EmitBindingInitialization(declarator.Id, isDeclaration: true);
                }

                continue;
            }

            EmitNamedEvaluation(declarator.Init, declarator.Id);
            if (declaration.Kind is VariableDeclarationKind.Using or VariableDeclarationKind.AwaitUsing)
            {
                EmitAddDisposableResource(declaration.Kind == VariableDeclarationKind.AwaitUsing);
            }

            EmitBindingInitialization(declarator.Id, isDeclaration: declaration.Kind != VariableDeclarationKind.Var);
        }
    }

    private void EmitIf(IfStatement ifStatement)
    {
        ResetCompletion();
        EmitExpression(ifStatement.Test);
        var elseLabel = _builder.DefineLabel();
        _builder.EmitJump(OpCode.JumpIfFalse, elseLabel);
        EmitSubStatement(ifStatement.Consequent);
        if (ifStatement.Alternate is not null)
        {
            var endLabel = _builder.DefineLabel();
            _builder.EmitJump(OpCode.Jump, endLabel);
            _builder.MarkLabel(elseLabel);
            EmitSubStatement(ifStatement.Alternate);
            _builder.MarkLabel(endLabel);
        }
        else
        {
            _builder.MarkLabel(elseLabel);
        }
    }

    /// <summary>The statement of an if or a label: a function declaration there (Annex B.3.3) has its own block scope.</summary>
    private void EmitSubStatement(Node statement)
    {
        if (statement is FunctionDeclaration declaration && _analysis.GetImplicitBlockScope(declaration) is { } scope)
        {
            EnterScope(scope);
            EmitBlockFunctionDeclarations([declaration], scope);
            EmitStatement(declaration, labels: null);
            ExitScope(scope);
            return;
        }

        EmitStatement(statement, labels: null);
    }

    private void EmitLabeled(LabeledStatement labeled, HashSet<string>? labels)
    {
        var allLabels = labels is null ? new HashSet<string>(StringComparer.Ordinal) : new HashSet<string>(labels, StringComparer.Ordinal);
        allLabels.Add(labeled.Label.Name);
        switch (labeled.Body)
        {
            case WhileStatement or DoWhileStatement or ForStatement or ForInStatement or ForOfStatement or LabeledStatement or BlockStatement or SwitchStatement:
                EmitStatement(labeled.Body, allLabels);
                return;
            case FunctionDeclaration declaration:
                EmitStatement(declaration, labels: null);
                return;
        }

        // A labelled statement that is not a loop: break label jumps after it
        var breakLabel = _builder.DefineLabel();
        var control = PushControl(ControlKind.Label, breakLabel, default, continueScope: null, allLabels);
        EmitStatement(labeled.Body, labels: null);
        PopControl(control);
        _builder.MarkLabel(breakLabel);
    }

    private void EmitWhile(WhileStatement whileStatement, HashSet<string>? labels)
    {
        ResetCompletion();
        var testLabel = _builder.DefineLabel();
        var breakLabel = _builder.DefineLabel();
        _builder.MarkLabel(testLabel);
        EmitExpression(whileStatement.Test);
        _builder.EmitJump(OpCode.JumpIfFalse, breakLabel);
        var control = PushControl(ControlKind.Loop, breakLabel, testLabel, _scope, labels);
        EmitStatement(whileStatement.Body, labels: null);
        PopControl(control);
        _builder.EmitJump(OpCode.Jump, testLabel);
        _builder.MarkLabel(breakLabel);
    }

    private void EmitDoWhile(DoWhileStatement doWhile, HashSet<string>? labels)
    {
        ResetCompletion();
        var bodyLabel = _builder.DefineLabel();
        var continueLabel = _builder.DefineLabel();
        var breakLabel = _builder.DefineLabel();
        _builder.MarkLabel(bodyLabel);
        var control = PushControl(ControlKind.Loop, breakLabel, continueLabel, _scope, labels);
        EmitStatement(doWhile.Body, labels: null);
        PopControl(control);
        _builder.MarkLabel(continueLabel);
        EmitExpression(doWhile.Test);
        _builder.EmitJump(OpCode.JumpIfTrue, bodyLabel);
        _builder.MarkLabel(breakLabel);
    }

    private void EmitFor(ForStatement forStatement, HashSet<string>? labels)
    {
        ResetCompletion();
        var loopScope = _analysis.Scopes.GetValueOrDefault(forStatement);
        var copyEnvironment = false;
        if (loopScope is not null)
        {
            EnterScope(loopScope);
            copyEnvironment = loopScope.NeedsContext && forStatement.Init is VariableDeclaration { Kind: VariableDeclarationKind.Let };
        }

        if (forStatement.Init is VariableDeclaration declaration)
        {
            EmitVariableDeclaration(declaration);
        }
        else if (forStatement.Init is not null)
        {
            EmitExpression(forStatement.Init);
            _builder.Emit(OpCode.Pop);
        }

        // CreatePerIterationEnvironment: each iteration has its own copy of the let bindings
        if (copyEnvironment)
        {
            _builder.Emit(OpCode.CopyScope);
        }

        var testLabel = _builder.DefineLabel();
        var continueLabel = _builder.DefineLabel();
        var breakLabel = _builder.DefineLabel();
        _builder.MarkLabel(testLabel);
        if (forStatement.Test is not null)
        {
            EmitExpression(forStatement.Test);
            _builder.EmitJump(OpCode.JumpIfFalse, breakLabel);
        }

        var control = PushControl(ControlKind.Loop, breakLabel, continueLabel, _scope, labels);
        EmitStatement(forStatement.Body, labels: null);
        PopControl(control);
        _builder.MarkLabel(continueLabel);
        if (copyEnvironment)
        {
            _builder.Emit(OpCode.CopyScope);
        }

        if (forStatement.Update is not null)
        {
            EmitExpression(forStatement.Update);
            _builder.Emit(OpCode.Pop);
        }

        _builder.EmitJump(OpCode.Jump, testLabel);
        _builder.MarkLabel(breakLabel);
        if (loopScope is not null)
        {
            ExitScope(loopScope);
        }
    }

    /// <summary>Evaluates the expression of a for-in/of statement (in the TDZ scope of its lexical bindings).</summary>
    private void EmitForInOfExpression(Node node, Node right)
    {
        if (_analysis.TdzScopes.GetValueOrDefault(node) is { } tdzScope)
        {
            EnterScope(tdzScope);
            EmitExpression(right);
            ExitScope(tdzScope);
        }
        else
        {
            EmitExpression(right);
        }
    }

    /// <summary>Binds the value of an iteration of a for-in/of statement to its left side.</summary>
    private void EmitForInOfBinding(Node node, Node left)
    {
        if (left is VariableDeclaration declaration)
        {
            var target = declaration.Declarations[0].Id;
            if (declaration.Kind is VariableDeclarationKind.Using or VariableDeclarationKind.AwaitUsing)
            {
                EmitAddDisposableResource(declaration.Kind == VariableDeclarationKind.AwaitUsing);
            }

            EmitBindingInitialization(target, isDeclaration: declaration.Kind != VariableDeclarationKind.Var);
        }
        else
        {
            EmitAssignmentTarget(left);
        }

        _ = node;
    }

    private void EmitForIn(ForInStatement forIn, HashSet<string>? labels)
    {
        ResetCompletion();

        // Annex B: for (var x = init in obj)
        if (forIn.Left is VariableDeclaration { Kind: VariableDeclarationKind.Var } varDeclaration && varDeclaration.Declarations[0].Init is { } initializer)
        {
            EmitNamedEvaluation(initializer, varDeclaration.Declarations[0].Id);
            EmitBindingInitialization(varDeclaration.Declarations[0].Id, isDeclaration: false);
        }

        EmitForInOfExpression(forIn, forIn.Right);
        var doneLabel = _builder.DefineLabel();
        var breakLabel = _builder.DefineLabel();
        var nextLabel = _builder.DefineLabel();

        // for (x in null/undefined) does not run the body
        _builder.EmitJump(OpCode.JumpIfNullishKeep, doneLabel);
        _builder.Emit(OpCode.ForInPrepare);
        var enumerator = _function.AllocateRegister();
        _builder.Emit(OpCode.StoreLocal, enumerator);
        _builder.MarkLabel(nextLabel);
        _builder.Emit(OpCode.LoadLocal, enumerator);
        _builder.EmitJump(OpCode.ForInNext, breakLabel);
        var iterationScope = _analysis.Scopes.GetValueOrDefault(forIn);
        if (iterationScope is not null)
        {
            EnterScope(iterationScope);
        }

        EmitForInOfBinding(forIn, forIn.Left);
        var continueLabel = _builder.DefineLabel();
        var control = new ControlScope
        {
            Kind = ControlKind.Loop,
            Parent = _control,
            BreakLabel = breakLabel,
            ContinueLabel = continueLabel,
            BreakScope = iterationScope?.Parent ?? _scope,
            ContinueScope = _scope,
        };

        if (labels is not null)
        {
            control.Labels.UnionWith(labels);
        }

        _control = control;
        EmitStatement(forIn.Body, labels: null);
        PopControl(control);
        _builder.MarkLabel(continueLabel);
        if (iterationScope is not null)
        {
            ExitScope(iterationScope);
        }

        _builder.EmitJump(OpCode.Jump, nextLabel);
        _builder.MarkLabel(doneLabel);
        _builder.Emit(OpCode.Pop);
        _builder.MarkLabel(breakLabel);
    }

    private void EmitForOf(ForOfStatement forOf, HashSet<string>? labels)
    {
        ResetCompletion();
        EmitForInOfExpression(forOf, forOf.Right);
        _builder.Emit(forOf.IsAwait ? OpCode.GetAsyncIterator : OpCode.GetIterator);
        var iterator = _function.AllocateRegister();
        _builder.Emit(OpCode.StoreLocal, iterator);
        var environment = _function.AllocateRegister();
        _builder.Emit(OpCode.SaveEnvironment, environment);

        var nextLabel = _builder.DefineLabel();
        var breakLabel = _builder.DefineLabel();
        var continueLabel = _builder.DefineLabel();
        var handlerLabel = _builder.DefineLabel();
        _builder.MarkLabel(nextLabel);
        _builder.Emit(OpCode.LoadLocal, iterator);
        if (forOf.IsAwait)
        {
            _builder.Emit(OpCode.Dup);
            _builder.Emit(OpCode.IteratorCallNext);
            _builder.Emit(OpCode.Await);
            _builder.EmitJump(OpCode.IteratorStepFinish, breakLabel);
        }
        else
        {
            _builder.EmitJump(OpCode.IteratorNext, breakLabel);
        }

        // An exception of the binding or the body closes the iterator (not an exception of next())
        var depth = _builder.StackDepth - 1;
        var regionStart = _builder.Position;
        var outerScope = _scope;
        var iterationScope = _analysis.Scopes.GetValueOrDefault(forOf);
        var loopControl = new ControlScope
        {
            Kind = ControlKind.Loop,
            Parent = _control,
            BreakLabel = breakLabel,
            ContinueLabel = continueLabel,
            BreakScope = outerScope,
            ContinueScope = iterationScope ?? outerScope,
        };

        if (labels is not null)
        {
            loopControl.Labels.UnionWith(labels);
        }

        var control = new ControlScope
        {
            Kind = ControlKind.ForOf,
            Parent = loopControl,
            BreakScope = outerScope,
            IteratorRegister = iterator,
            IsAsyncIterator = forOf.IsAwait,
        };

        _control = control;
        if (iterationScope is not null)
        {
            EnterScope(iterationScope);
        }

        EmitForInOfBinding(forOf, forOf.Left);
        EmitStatement(forOf.Body, labels: null);
        _builder.MarkLabel(continueLabel);
        if (iterationScope is not null)
        {
            ExitScope(iterationScope);
        }

        var regionEnd = _builder.Position;
        _control = loopControl.Parent;
        _builder.EmitJump(OpCode.Jump, nextLabel);

        // The exception handler: close the iterator and rethrow
        _builder.SetLabelDepth(handlerLabel, depth + 1);
        _builder.MarkLabel(handlerLabel);
        _builder.AddHandler(regionStart, regionEnd, handlerLabel, depth, environment);
        _scope = outerScope;
        _builder.Emit(OpCode.LoadLocal, iterator);
        if (forOf.IsAwait)
        {
            EmitAsyncIteratorCloseOnThrow();
        }
        else
        {
            _builder.Emit(OpCode.IteratorCloseOnThrow);
        }

        _builder.Emit(OpCode.Throw);
        _builder.MarkLabel(breakLabel);
        _scope = outerScope;
    }

    private void EmitReturnStatement(ReturnStatement returnStatement)
    {
        if (returnStatement.Argument is null)
        {
            if (_function.Kind == FunctionInfoKind.ClassConstructor && _function.FunctionNode!.IsDerivedConstructor)
            {
                EmitLoadThis(_function.Node, checkInitialized: true);
            }
            else
            {
                _builder.Emit(OpCode.PushUndefined);
            }
        }
        else
        {
            EmitExpression(returnStatement.Argument);

            // An async generator awaits the value it returns
            if (_function.FunctionNode is { IsAsync: true, IsGenerator: true })
            {
                _builder.Emit(OpCode.Await);
            }

            // A derived constructor that returns undefined returns its this
            if (_function.Kind == FunctionInfoKind.ClassConstructor && _function.FunctionNode!.IsDerivedConstructor)
            {
                var hasValue = _builder.DefineLabel();
                _builder.EmitJump(OpCode.JumpIfNotUndefinedKeep, hasValue);
                _builder.Emit(OpCode.Pop);
                EmitLoadThis(_function.Node, checkInitialized: true);
                _builder.MarkLabel(hasValue);
            }
        }

        _builder.SetPosition(returnStatement.Start);
        EmitReturn();
    }

    private void EmitTry(TryStatement tryStatement)
    {
        ResetCompletion();
        var environment = _function.AllocateRegister();
        _builder.Emit(OpCode.SaveEnvironment, environment);
        var depth = _builder.StackDepth;
        var scope = _scope;
        ControlScope? finallyControl = null;
        var finallyLabel = _builder.DefineLabel();
        var finallyHandler = _builder.DefineLabel();
        var endLabel = _builder.DefineLabel();
        if (tryStatement.Finalizer is not null)
        {
            finallyControl = new ControlScope
            {
                Kind = ControlKind.Finally,
                Parent = _control,
                BreakScope = scope,
                TypeRegister = _function.AllocateRegister(),
                ValueRegister = _function.AllocateRegister(),
                FinallyLabel = finallyLabel,
            };

            _control = finallyControl;
        }

        var regionStart = _builder.Position;
        EmitStatement(tryStatement.Block, labels: null);
        var tryEnd = _builder.Position;
        if (tryStatement.Handler is { } handler)
        {
            var afterCatch = _builder.DefineLabel();
            _builder.EmitJump(OpCode.Jump, afterCatch);
            var catchLabel = _builder.DefineLabel();
            _builder.SetLabelDepth(catchLabel, depth + 1);
            _builder.MarkLabel(catchLabel);
            _builder.AddHandler(regionStart, tryEnd, catchLabel, depth, environment);
            _scope = scope;
            EmitCatch(handler);
            _builder.MarkLabel(afterCatch);
        }

        var regionEnd = _builder.Position;
        if (finallyControl is null)
        {
            _builder.MarkLabel(endLabel);
            return;
        }

        _control = finallyControl.Parent;

        // Normal completion of the try (and catch) blocks
        _builder.EmitPushNumber(CompletionNormal);
        _builder.Emit(OpCode.StoreLocal, finallyControl.TypeRegister);
        _builder.EmitJump(OpCode.Jump, finallyLabel);

        // Exceptions of the try and catch blocks run the finally block, then are rethrown
        _builder.SetLabelDepth(finallyHandler, depth + 1);
        _builder.MarkLabel(finallyHandler);
        _builder.AddHandler(regionStart, regionEnd, finallyHandler, depth, environment);
        _builder.Emit(OpCode.StoreLocal, finallyControl.ValueRegister);
        _builder.EmitPushNumber(CompletionThrow);
        _builder.Emit(OpCode.StoreLocal, finallyControl.TypeRegister);

        // The finally block (the completion value of the statement is not the one of the finally block)
        _builder.MarkLabel(finallyLabel);
        _builder.Emit(OpCode.RestoreEnvironment, environment);
        _scope = scope;
        var savedCompletion = -1;
        if (_completionRegister >= 0)
        {
            savedCompletion = _function.AllocateRegister();
            _builder.Emit(OpCode.LoadLocal, _completionRegister);
            _builder.Emit(OpCode.StoreLocal, savedCompletion);
            ResetCompletion();
        }

        EmitStatement(tryStatement.Finalizer!, labels: null);
        if (savedCompletion >= 0)
        {
            _builder.Emit(OpCode.LoadLocal, savedCompletion);
            _builder.Emit(OpCode.StoreLocal, _completionRegister);
        }

        // Dispatch the completion of the try statement
        var notThrow = _builder.DefineLabel();
        _builder.Emit(OpCode.LoadLocal, finallyControl.TypeRegister);
        _builder.EmitPushNumber(CompletionThrow);
        _builder.Emit(OpCode.StrictEqual);
        _builder.EmitJump(OpCode.JumpIfFalse, notThrow);
        _builder.Emit(OpCode.LoadLocal, finallyControl.ValueRegister);
        _builder.Emit(OpCode.Throw);
        _builder.MarkLabel(notThrow);

        var notReturn = _builder.DefineLabel();
        _builder.Emit(OpCode.LoadLocal, finallyControl.TypeRegister);
        _builder.EmitPushNumber(CompletionReturn);
        _builder.Emit(OpCode.StrictEqual);
        _builder.EmitJump(OpCode.JumpIfFalse, notReturn);
        _builder.Emit(OpCode.LoadLocal, finallyControl.ValueRegister);
        EmitReturn();
        _builder.MarkLabel(notReturn);

        foreach (var (target, isContinue, id) in finallyControl.Jumps)
        {
            var next = _builder.DefineLabel();
            _builder.Emit(OpCode.LoadLocal, finallyControl.TypeRegister);
            _builder.EmitPushNumber(id);
            _builder.Emit(OpCode.StrictEqual);
            _builder.EmitJump(OpCode.JumpIfFalse, next);
            EmitJumpToControl(target!, isContinue);
            _builder.MarkLabel(next);
        }

        _builder.MarkLabel(endLabel);
    }

    private void EmitCatch(CatchClause handler)
    {
        var catchScope = _analysis.Scopes[handler];
        EnterScope(catchScope);
        if (handler.Param is not null)
        {
            EmitBindingInitialization(handler.Param, isDeclaration: true);
        }
        else
        {
            _builder.Emit(OpCode.Pop);
        }

        EmitStatement(handler.Body, labels: null);
        ExitScope(catchScope);
    }

    private void EmitSwitch(SwitchStatement switchStatement, HashSet<string>? labels)
    {
        ResetCompletion();
        EmitExpression(switchStatement.Discriminant);
        var discriminant = _function.AllocateRegister();
        _builder.Emit(OpCode.StoreLocal, discriminant);
        var scope = _analysis.Scopes[switchStatement];
        EnterScope(scope);
        foreach (var switchCase in switchStatement.Cases)
        {
            EmitBlockFunctionDeclarations(switchCase.Consequent, scope);
        }

        var breakLabel = _builder.DefineLabel();
        var caseLabels = new Label[switchStatement.Cases.Count];
        var defaultIndex = -1;
        for (var i = 0; i < switchStatement.Cases.Count; i++)
        {
            caseLabels[i] = _builder.DefineLabel();
            var test = switchStatement.Cases[i].Test;
            if (test is null)
            {
                defaultIndex = i;
                continue;
            }

            _builder.Emit(OpCode.LoadLocal, discriminant);
            EmitExpression(test);
            _builder.Emit(OpCode.StrictEqual);
            _builder.EmitJump(OpCode.JumpIfTrue, caseLabels[i]);
        }

        var exitLabel = _builder.DefineLabel();
        _builder.EmitJump(OpCode.Jump, defaultIndex >= 0 ? caseLabels[defaultIndex] : breakLabel);

        // break pops the switch scope: its target is the scope outside the switch
        var control = new ControlScope { Kind = ControlKind.Switch, Parent = _control, BreakLabel = exitLabel, BreakScope = scope.Parent! };
        if (labels is not null)
        {
            control.Labels.UnionWith(labels);
        }

        _control = control;
        for (var i = 0; i < switchStatement.Cases.Count; i++)
        {
            _builder.MarkLabel(caseLabels[i]);
            EmitStatements(switchStatement.Cases[i].Consequent);
        }

        _control = control.Parent;
        _builder.MarkLabel(breakLabel);
        ExitScope(scope);
        _builder.MarkLabel(exitLabel);
    }

    private void EmitWith(WithStatement withStatement)
    {
        ResetCompletion();
        EmitExpression(withStatement.Object);
        _builder.Emit(OpCode.ToObject);
        var scope = _analysis.Scopes[withStatement];
        _builder.Emit(OpCode.PushWith);
        _scope = scope;
        EmitStatement(withStatement.Body, labels: null);
        ExitScope(scope);
    }

    private void EmitExportDefault(ExportDefaultDeclaration exportDefault)
    {
        switch (exportDefault.Declaration)
        {
            case FunctionDeclaration:
                return;
            case ClassDeclaration classDeclaration:
                EmitClass(classDeclaration.Class, classDeclaration.Class.Id?.Name ?? "default");
                if (classDeclaration.Class.Id is not null)
                {
                    EmitInitializeIdentifier(classDeclaration.Class.Id);
                }
                else
                {
                    EmitInitializeVariable(_scope.Function.Scope.Find("*default*") ?? _scope.Find("*default*")!);
                }

                return;
            default:
                EmitNamedEvaluation(exportDefault.Declaration, "default");
                EmitInitializeVariable(FindVariable("*default*"));
                return;
        }
    }

    private Variable FindVariable(string name)
    {
        for (var scope = _scope; scope is not null; scope = scope.Parent)
        {
            if (scope.Find(name) is { } variable)
                return variable;
        }

        throw new InvalidOperationException("Unknown binding " + name);
    }
}
