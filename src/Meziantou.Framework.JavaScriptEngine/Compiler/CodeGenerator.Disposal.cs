namespace Meziantou.Framework.JavaScriptEngine.Compiler;

internal sealed partial class CodeGenerator
{
    // The register of the DisposeCapability of each scope that has using declarations
    private readonly Dictionary<Scope, int> _disposeCapabilities = [];

    private static bool HasUsingDeclarations(Scope scope, out bool isAsync)
    {
        var hasUsing = false;
        isAsync = false;
        foreach (var variable in scope.OrderedVariables)
        {
            if (variable.IsUsing)
            {
                hasUsing = true;
                isAsync |= variable.IsAwaitUsing;
            }
        }

        return hasUsing;
    }

    /// <summary>
    /// Emits the body of a scope that has using declarations: the resources are disposed when the scope completes (normally,
    /// by an exception, a return or a break), with the completion of the scope (DisposeResources).
    /// </summary>
    private void EmitWithDisposal(Scope scope, Action emitBody)
    {
        if (!HasUsingDeclarations(scope, out var isAsync))
        {
            emitBody();
            return;
        }

        var capability = _function.AllocateRegister();
        _builder.Emit(OpCode.NewDisposeCapability);
        _builder.Emit(OpCode.StoreLocal, capability);
        _disposeCapabilities[scope] = capability;

        var environment = _function.AllocateRegister();
        _builder.Emit(OpCode.SaveEnvironment, environment);
        var depth = _builder.StackDepth;
        var finallyLabel = _builder.DefineLabel();
        var finallyHandler = _builder.DefineLabel();
        var control = new ControlScope
        {
            Kind = ControlKind.Finally,
            Parent = _control,
            BreakScope = _scope,
            TypeRegister = _function.AllocateRegister(),
            ValueRegister = _function.AllocateRegister(),
            FinallyLabel = finallyLabel,
        };

        var savedScope = _scope;
        _control = control;
        var regionStart = _builder.Position;
        emitBody();
        var regionEnd = _builder.Position;
        _control = control.Parent;
        if (!_builder.IsUnreachable)
        {
            _builder.EmitPushNumber(CompletionNormal);
            _builder.Emit(OpCode.StoreLocal, control.TypeRegister);
            _builder.EmitJump(OpCode.Jump, finallyLabel);
        }

        _builder.SetLabelDepth(finallyHandler, depth + 1);
        _builder.MarkLabel(finallyHandler);
        _builder.AddHandler(regionStart, regionEnd, finallyHandler, depth, environment);
        _builder.Emit(OpCode.StoreLocal, control.ValueRegister);
        _builder.EmitPushNumber(CompletionThrow);
        _builder.Emit(OpCode.StoreLocal, control.TypeRegister);

        // Dispose the resources with the completion of the scope
        _builder.MarkLabel(finallyLabel);
        _builder.Emit(OpCode.RestoreEnvironment, environment);
        _scope = savedScope;
        _builder.Emit(OpCode.LoadLocal, capability);
        _builder.Emit(OpCode.LoadLocal, control.TypeRegister);
        _builder.Emit(OpCode.LoadLocal, control.ValueRegister);
        if (isAsync)
        {
            EmitAsyncDisposeResources();
        }
        else
        {
            _builder.Emit(OpCode.DisposeResources);
        }

        // Dispatch the completion (a throw completion was rethrown by the disposal)
        var notReturn = _builder.DefineLabel();
        _builder.Emit(OpCode.LoadLocal, control.TypeRegister);
        _builder.EmitPushNumber(CompletionReturn);
        _builder.Emit(OpCode.StrictEqual);
        _builder.EmitJump(OpCode.JumpIfFalse, notReturn);
        _builder.Emit(OpCode.LoadLocal, control.ValueRegister);
        EmitReturn();
        _builder.MarkLabel(notReturn);
        foreach (var (target, isContinue, id) in control.Jumps)
        {
            var next = _builder.DefineLabel();
            _builder.Emit(OpCode.LoadLocal, control.TypeRegister);
            _builder.EmitPushNumber(id);
            _builder.Emit(OpCode.StrictEqual);
            _builder.EmitJump(OpCode.JumpIfFalse, next);
            EmitJumpToControl(target!, isContinue);
            _builder.MarkLabel(next);
        }

        _disposeCapabilities.Remove(scope);
    }

    /// <summary>capability type value -> : the disposal of the resources of an await using scope (each async disposal is awaited).</summary>
    private void EmitAsyncDisposeResources()
    {
        var operation = _function.AllocateRegister();
        _builder.Emit(OpCode.AsyncDisposeStart);
        _builder.Emit(OpCode.StoreLocal, operation);
        var loop = _builder.DefineLabel();
        var done = _builder.DefineLabel();
        _builder.MarkLabel(loop);
        _builder.Emit(OpCode.LoadLocal, operation);
        _builder.EmitJump(OpCode.AsyncDisposeNext, done);

        // Await the value; the result (or the exception) is given back to the operation
        var environment = _function.AllocateRegister();
        _builder.Emit(OpCode.SaveEnvironment, environment);
        var depth = _builder.StackDepth - 1;
        var regionStart = _builder.Position;
        _builder.Emit(OpCode.Await);
        _builder.Emit(OpCode.Pop);
        var regionEnd = _builder.Position;
        _builder.Emit(OpCode.LoadLocal, operation);
        _builder.Emit(OpCode.PushHole);
        _builder.Emit(OpCode.AsyncDisposeSetResult);
        _builder.EmitJump(OpCode.Jump, loop);
        var handler = _builder.DefineLabel();
        _builder.SetLabelDepth(handler, depth + 1);
        _builder.MarkLabel(handler);
        _builder.AddHandler(regionStart, regionEnd, handler, depth, environment);
        _builder.Emit(OpCode.LoadLocal, operation);
        _builder.Emit(OpCode.Swap);
        _builder.Emit(OpCode.AsyncDisposeSetResult);
        _builder.EmitJump(OpCode.Jump, loop);
        _builder.MarkLabel(done);
        _builder.Emit(OpCode.LoadLocal, operation);
        _builder.Emit(OpCode.AsyncDisposeFinish);
    }

    /// <summary>AddDisposableResource in the nearest scope with using declarations: value -> value.</summary>
    private void EmitAddDisposableResourceCore(bool isAsync)
    {
        for (var scope = _scope; scope is not null; scope = scope.Parent)
        {
            if (_disposeCapabilities.TryGetValue(scope, out var capability))
            {
                _builder.Emit(OpCode.Dup);
                _builder.Emit(OpCode.LoadLocal, capability);
                _builder.Emit(OpCode.Swap);
                _builder.Emit(OpCode.AddDisposableResource, isAsync ? 1 : 0);
                return;
            }
        }

        throw new InvalidOperationException("A using declaration outside of a scope with a dispose capability");
    }
}
