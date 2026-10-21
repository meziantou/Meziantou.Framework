using Meziantou.Framework.JavaScriptEngine.Ast;

namespace Meziantou.Framework.JavaScriptEngine.Compiler;

internal sealed partial class CodeGenerator
{
    /// <summary>BindingInitialization: binds the value on the stack (consumed) to an identifier or a pattern of a declaration.</summary>
    private void EmitBindingInitialization(Node target, bool isDeclaration)
    {
        switch (target)
        {
            case Identifier identifier:
                if (isDeclaration)
                {
                    EmitInitializeIdentifier(identifier);
                }
                else
                {
                    EmitStoreIdentifier(identifier);
                }

                return;
            case ObjectPattern objectPattern:
                EmitObjectPattern(objectPattern, isDeclaration, isAssignment: false);
                return;
            case ArrayPattern arrayPattern:
                EmitArrayPattern(arrayPattern, isDeclaration, isAssignment: false);
                return;
            case AssignmentPattern assignment:
                EmitDefaultValue(assignment);
                EmitBindingInitialization(assignment.Left, isDeclaration);
                return;
            default:
                throw new InvalidOperationException("Invalid binding target: " + target.Type);
        }
    }

    /// <summary>DestructuringAssignmentEvaluation: assigns the value on the stack (consumed) to a pattern of an assignment expression.</summary>
    private void EmitDestructuringAssignment(Node pattern)
    {
        switch (pattern)
        {
            case ObjectPattern objectPattern:
                EmitObjectPattern(objectPattern, isDeclaration: false, isAssignment: true);
                return;
            case ArrayPattern arrayPattern:
                EmitArrayPattern(arrayPattern, isDeclaration: false, isAssignment: true);
                return;
            default:
                throw new InvalidOperationException("Invalid destructuring target: " + pattern.Type);
        }
    }

    /// <summary>Assigns the value on the stack (consumed) to the left side of a for-in/of statement (an assignment target or a pattern).</summary>
    private void EmitAssignmentTarget(Node target)
    {
        if (target is ObjectPattern or ArrayPattern)
        {
            EmitDestructuringAssignment(target);
            return;
        }

        // The reference is evaluated after the value (the value is moved above the base)
        var kind = EmitReferenceBaseAbove(target);
        EmitPutReference(target, kind);
        _builder.Emit(OpCode.Pop);
    }

    /// <summary>Evaluates the base of a reference while the value is on the stack: value -> base value.</summary>
    private TargetKind EmitReferenceBaseAbove(Node target)
    {
        var value = _function.AllocateRegister();
        _builder.Emit(OpCode.StoreLocal, value);
        var kind = EmitReferenceBase(target);
        _builder.Emit(OpCode.LoadLocal, value);
        return kind;
    }

    /// <summary>value -> value or default: evaluates the default when the value is undefined.</summary>
    private void EmitDefaultValue(AssignmentPattern assignment)
    {
        var hasValue = _builder.DefineLabel();
        _builder.EmitJump(OpCode.JumpIfNotUndefinedKeep, hasValue);
        _builder.Emit(OpCode.Pop);
        EmitNamedEvaluation(assignment.Right, assignment.Left);
        _builder.MarkLabel(hasValue);
    }

    /// <summary>Binds or assigns an element of a pattern (a pattern, an identifier, a member expression, with a default); emitValue pushes the value of the element.</summary>
    private void EmitPatternElement(Node element, bool isDeclaration, bool isAssignment, Action emitValue)
    {
        var target = element;
        AssignmentPattern? withDefault = null;
        if (element is AssignmentPattern assignment)
        {
            withDefault = assignment;
            target = assignment.Left;
        }

        // In an assignment, a reference target is evaluated before the value
        if (isAssignment && target is not ObjectPattern and not ArrayPattern && (target is not Identifier identifierTarget || _analysis.References[identifierTarget].IsDynamic))
        {
            var kind = EmitReferenceBase(target);
            emitValue();
            if (withDefault is not null)
            {
                EmitDefaultValue(withDefault);
            }

            EmitPutReference(target, kind);
            _builder.Emit(OpCode.Pop);
            return;
        }

        emitValue();
        if (withDefault is not null)
        {
            EmitDefaultValue(withDefault);
        }

        if (isAssignment)
        {
            if (target is Identifier identifier)
            {
                EmitStoreIdentifier(identifier);
            }
            else
            {
                EmitDestructuringAssignment(target);
            }
        }
        else
        {
            EmitBindingInitialization(target, isDeclaration);
        }
    }

    private void EmitObjectPattern(ObjectPattern pattern, bool isDeclaration, bool isAssignment)
    {
        _builder.Emit(OpCode.RequireObjectCoercible);
        var source = _function.AllocateRegister();
        _builder.Emit(OpCode.StoreLocal, source);
        var hasRest = pattern.Properties.Count > 0 && pattern.Properties[^1] is RestElement;
        var excludedKeys = new List<(PropertyKey? Constant, int Register)>();
        foreach (var node in pattern.Properties)
        {
            if (node is RestElement rest)
            {
                EmitPatternElement(rest.Argument, isDeclaration, isAssignment, () =>
                {
                    _builder.Emit(OpCode.LoadLocal, source);
                    foreach (var (constant, register) in excludedKeys)
                    {
                        if (constant is { } key)
                        {
                            EmitPushKey(key);
                        }
                        else
                        {
                            _builder.Emit(OpCode.LoadLocal, register);
                        }
                    }

                    _builder.Emit(OpCode.CopyDataPropertiesExcluding, excludedKeys.Count);
                });
                continue;
            }

            var property = (Property)node;
            if (property.Computed)
            {
                EmitExpression(property.Key);
                _builder.Emit(OpCode.ToPropertyKey);
                var keyRegister = _function.AllocateRegister();
                _builder.Emit(OpCode.StoreLocal, keyRegister);
                if (hasRest)
                {
                    excludedKeys.Add((null, keyRegister));
                }

                EmitPatternElement(property.Value, isDeclaration, isAssignment, () =>
                {
                    _builder.Emit(OpCode.LoadLocal, source);
                    _builder.Emit(OpCode.LoadLocal, keyRegister);
                    _builder.Emit(OpCode.GetElement);
                });
            }
            else
            {
                var key = GetPropertyKey(property.Key);
                if (hasRest)
                {
                    excludedKeys.Add((key, -1));
                }

                EmitPatternElement(property.Value, isDeclaration, isAssignment, () =>
                {
                    _builder.Emit(OpCode.LoadLocal, source);
                    _builder.SetPosition(property.Start);
                    _builder.Emit(OpCode.GetProperty, _builder.AddKey(key));
                });
            }
        }
    }

    private void EmitArrayPattern(ArrayPattern pattern, bool isDeclaration, bool isAssignment)
    {
        _builder.SetPosition(pattern.Start);
        _builder.Emit(OpCode.GetIterator);
        var iterator = _function.AllocateRegister();
        _builder.Emit(OpCode.StoreLocal, iterator);
        var environment = _function.AllocateRegister();
        _builder.Emit(OpCode.SaveEnvironment, environment);
        var depth = _builder.StackDepth;
        var regionStart = _builder.Position;
        var control = new ControlScope { Kind = ControlKind.ForOf, Parent = _control, BreakScope = _scope, IteratorRegister = iterator };
        _control = control;
        foreach (var element in pattern.Elements)
        {
            switch (element)
            {
                case null:
                    _builder.Emit(OpCode.LoadLocal, iterator);
                    _builder.Emit(OpCode.IteratorNextOrUndefined);
                    _builder.Emit(OpCode.Pop);
                    break;
                case RestElement rest:
                    EmitPatternElement(rest.Argument, isDeclaration, isAssignment, () =>
                    {
                        _builder.Emit(OpCode.LoadLocal, iterator);
                        _builder.Emit(OpCode.IteratorRest);
                    });
                    break;
                default:
                    EmitPatternElement(element, isDeclaration, isAssignment, () =>
                    {
                        _builder.Emit(OpCode.LoadLocal, iterator);
                        _builder.Emit(OpCode.IteratorNextOrUndefined);
                    });
                    break;
            }
        }

        _control = control.Parent;
        var regionEnd = _builder.Position;
        var endLabel = _builder.DefineLabel();

        // Close the iterator when it is not done (a normal completion: the errors of return() are thrown)
        _builder.Emit(OpCode.LoadLocal, iterator);
        _builder.Emit(OpCode.IteratorClose);
        _builder.EmitJump(OpCode.Jump, endLabel);

        // An exception closes the iterator (the errors of return() are ignored) and is rethrown
        var handler = _builder.DefineLabel();
        _builder.SetLabelDepth(handler, depth + 1);
        _builder.MarkLabel(handler);
        _builder.AddHandler(regionStart, regionEnd, handler, depth, environment);
        _builder.Emit(OpCode.LoadLocal, iterator);
        _builder.Emit(OpCode.IteratorCloseOnThrow);
        _builder.Emit(OpCode.Throw);
        _builder.MarkLabel(endLabel);
    }

    // ------------------------------------------------------------------------------------------------------------------
    // Explicit resource management and async iterators (completed by the corresponding milestones)
    // ------------------------------------------------------------------------------------------------------------------

    /// <summary>AddDisposableResource for a using declaration: value -> value.</summary>
    private void EmitAddDisposableResource(bool isAsync) => EmitAddDisposableResourceCore(isAsync);

    /// <summary>AsyncIteratorClose for a normal completion: iteratorRecord ->.</summary>
    private void EmitAsyncIteratorClose()
    {
        // return() is called and awaited, its result must be an object
        var done = _builder.DefineLabel();
        _builder.Emit(OpCode.IteratorCallReturn);
        _builder.EmitJump(OpCode.JumpIfUndefinedPop, done);
        _builder.Emit(OpCode.Await);
        _builder.Emit(OpCode.ThrowIfNotObject);
        _builder.Emit(OpCode.Pop);
        _builder.MarkLabel(done);
    }

    /// <summary>AsyncIteratorClose for a throw completion: iteratorRecord -> (the errors are ignored; the caller rethrows).</summary>
    private void EmitAsyncIteratorCloseOnThrow()
    {
        var done = _builder.DefineLabel();
        var environment = _function.AllocateRegister();
        _builder.Emit(OpCode.SaveEnvironment, environment);
        var depth = _builder.StackDepth - 1;
        var regionStart = _builder.Position;
        _builder.Emit(OpCode.IteratorCallReturn);
        _builder.EmitJump(OpCode.JumpIfUndefinedPop, done);
        _builder.Emit(OpCode.Await);
        _builder.Emit(OpCode.Pop);
        var regionEnd = _builder.Position;
        _builder.EmitJump(OpCode.Jump, done);
        var handler = _builder.DefineLabel();
        _builder.SetLabelDepth(handler, depth + 1);
        _builder.MarkLabel(handler);
        _builder.AddHandler(regionStart, regionEnd, handler, depth, environment);
        _builder.Emit(OpCode.Pop);
        _builder.MarkLabel(done);
    }
}
