using System.Runtime.CompilerServices;
using Meziantou.Framework.JavaScriptEngine.Compiler;

namespace Meziantou.Framework.JavaScriptEngine.Interpreter;

/// <summary>
/// The bytecode interpreter. <see cref="Run"/> executes a frame until it returns or is suspended (yield, await); the
/// calls of ordinary JavaScript functions push frames in the same loop (no .NET recursion), the other calls (built-ins,
/// generators, async functions, proxies...) go through their [[Call]] internal method.
/// </summary>
internal static partial class JsInterpreter
{
    /// <summary>Runs a frame until it returns (the return value) or is suspended (the yielded or awaited value, with <see cref="Frame.State"/> set).</summary>
    public static JsValue Run(Frame entry)
    {
        var engine = entry.Realm.Engine;
        var frame = entry;
        var previousRealm = engine.CurrentRealm;
        engine.CurrentRealm = frame.Realm;
        engine.PushFrame(frame);
        try
        {
            if (entry.PendingThrow)
            {
                // A frame resumed by a rejected await: the exception is thrown at the await
                entry.PendingThrow = false;
                if (!HandleException(ref frame, entry, engine, entry.ResumeValue, isFaultingFrame: false))
                    throw new JsException(entry.ResumeValue);
            }

            while (true)
            {
                try
                {
                    return Execute(ref frame, entry, engine);
                }
                catch (JsException ex) when (HandleException(ref frame, entry, engine, ex.Value, isFaultingFrame: true))
                {
                    // The exception is handled by a frame of the run (an exception filter: an exception that leaves the
                    // run is not caught and rethrown, which would nest the exception dispatch in each .NET level of a deep
                    // recursion through host functions and overflow the stack). The finally blocks of the native calls
                    // ran after the filter: the current realm is the one of the frame of the handler.
                    engine.CurrentRealm = frame.Realm;
                }
            }
        }
        finally
        {
            // The frames of this run that are still on the stack (an exception propagates) are discarded
            while (!ReferenceEquals(frame, entry))
            {
                engine.PopFrame();
                engine.CallDepth--;
                frame = frame.Parent!;
            }

            engine.PopFrame();
            engine.CurrentRealm = previousRealm;
        }
    }

    /// <summary>Finds the handler of an exception in the frames of the run; false when the exception leaves the entry frame.</summary>
    private static bool HandleException(ref Frame frame, Frame entry, JsEngine engine, JsValue exception, bool isFaultingFrame)
    {
        while (true)
        {
            var pc = isFaultingFrame ? frame.Pc : frame.Pc - 1;
            foreach (var handler in frame.Code.Handlers)
            {
                if (pc >= handler.Start && pc < handler.End)
                {
                    var stackBase = frame.Code.RegisterCount;
                    frame.Sp = stackBase + handler.StackDepth;
                    frame.Values[frame.Sp++] = exception;
                    frame.Pc = handler.Target;
                    if (handler.EnvironmentRegister >= 0)
                    {
                        frame.Environment = (Environment)frame.Values[handler.EnvironmentRegister].Cell!;
                    }

                    engine.CurrentRealm = frame.Realm;
                    return true;
                }
            }

            if (ReferenceEquals(frame, entry))
            {
                frame.State = FrameState.Completed;
                return false;
            }

            frame.State = FrameState.Completed;
            engine.PopFrame();
            engine.CallDepth--;
            frame = frame.Parent!;
            isFaultingFrame = false;
        }
    }

    /// <summary>Gets the source position of the instruction before the position of a frame (the running instruction, or the call of a caller frame).</summary>
    internal static JsSourceLocation GetLocation(Frame frame) => GetLocation(frame.Code, frame.Pc);

    private static JsSourceLocation GetLocation(FunctionTemplate code, int pc)
    {
        var offset = code.GetSourceOffset(Math.Max(0, pc - 1));
        var (line, column) = code.Source.GetLineAndColumn(Math.Max(0, offset));
        return new JsSourceLocation(code.Source.Url, line, column);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ThrowTdz(PropertyKey name) => ThrowHelper.ThrowReferenceError($"Cannot access '{name}' before initialization");

    private static DeclarativeEnvironment GetEnvironment(Environment environment, int depth)
    {
        for (var i = 0; i < depth; i++)
        {
            environment = environment.Outer!;
        }

        return (DeclarativeEnvironment)environment;
    }

    private static JsValue Execute(ref Frame frame, Frame entry, JsEngine engine)
    {
        var template = frame.Code;
        var code = template.Code;
        var values = frame.Values;
        var constants = template.Constants;
        var keys = template.Keys;
        var objects = template.Objects;
        var pc = frame.Pc;
        var sp = frame.Sp;
        var strict = template.IsStrict;

        while (true)
        {
            frame.Pc = pc;
            var op = (OpCode)code[pc++];
            switch (op)
            {
                // ---- Constants ------------------------------------------------------------------------------------
                case OpCode.PushUndefined:
                    values[sp++] = default;
                    break;
                case OpCode.PushNull:
                    values[sp++] = JsValue.Null;
                    break;
                case OpCode.PushTrue:
                    values[sp++] = JsValue.True;
                    break;
                case OpCode.PushFalse:
                    values[sp++] = JsValue.False;
                    break;
                case OpCode.PushInt:
                    values[sp++] = new JsValue(code[pc++]);
                    break;
                case OpCode.PushConst:
                    values[sp++] = constants[code[pc++]];
                    break;
                case OpCode.PushHole:
                    values[sp++] = JsValue.Hole;
                    break;

                // ---- Stack ----------------------------------------------------------------------------------------
                case OpCode.Pop:
                    sp--;
                    break;
                case OpCode.Dup:
                    values[sp] = values[sp - 1];
                    sp++;
                    break;
                case OpCode.Dup2:
                    values[sp] = values[sp - 2];
                    values[sp + 1] = values[sp - 1];
                    sp += 2;
                    break;
                case OpCode.Swap:
                    (values[sp - 1], values[sp - 2]) = (values[sp - 2], values[sp - 1]);
                    break;
                case OpCode.Rot3:
                    {
                        // a b c -> c a b
                        var c = values[sp - 1];
                        values[sp - 1] = values[sp - 2];
                        values[sp - 2] = values[sp - 3];
                        values[sp - 3] = c;
                        break;
                    }

                case OpCode.Rot3Reverse:
                    {
                        // a b c -> b c a
                        var a = values[sp - 3];
                        values[sp - 3] = values[sp - 2];
                        values[sp - 2] = values[sp - 1];
                        values[sp - 1] = a;
                        break;
                    }

                case OpCode.Rot4:
                    {
                        // a b c d -> d a b c
                        var d = values[sp - 1];
                        values[sp - 1] = values[sp - 2];
                        values[sp - 2] = values[sp - 3];
                        values[sp - 3] = values[sp - 4];
                        values[sp - 4] = d;
                        break;
                    }

                // ---- Registers and environments -------------------------------------------------------------------
                case OpCode.LoadLocal:
                    values[sp++] = values[code[pc++]];
                    break;
                case OpCode.LoadLocalChecked:
                    {
                        var value = values[code[pc++]];
                        var name = code[pc++];
                        if (value.IsHole)
                        {
                            ThrowTdz(keys[name]);
                        }

                        values[sp++] = value;
                        break;
                    }

                case OpCode.StoreLocal:
                    values[code[pc++]] = values[--sp];
                    break;
                case OpCode.StoreLocalChecked:
                    {
                        var register = code[pc++];
                        var name = code[pc++];
                        if (values[register].IsHole)
                        {
                            ThrowTdz(keys[name]);
                        }

                        values[register] = values[--sp];
                        break;
                    }

                case OpCode.LoadContext:
                    {
                        var depth = code[pc++];
                        var slot = code[pc++];
                        values[sp++] = GetEnvironment(frame.Environment, depth).Slots[slot];
                        break;
                    }

                case OpCode.LoadContextChecked:
                    {
                        var depth = code[pc++];
                        var slot = code[pc++];
                        var name = code[pc++];
                        var value = GetEnvironment(frame.Environment, depth).Slots[slot];
                        if (value.IsHole)
                        {
                            ThrowTdz(keys[name]);
                        }

                        values[sp++] = value;
                        break;
                    }

                case OpCode.LoadImport:
                    {
                        var depth = code[pc++];
                        var slot = code[pc++];
                        var name = code[pc++];
                        values[sp++] = ImportBindingCell.Read(GetEnvironment(frame.Environment, depth).Slots[slot], keys[name]);
                        break;
                    }

                case OpCode.StoreContext:
                    {
                        var depth = code[pc++];
                        var slot = code[pc++];
                        GetEnvironment(frame.Environment, depth).Slots[slot] = values[--sp];
                        break;
                    }

                case OpCode.StoreContextChecked:
                    {
                        var depth = code[pc++];
                        var slot = code[pc++];
                        var name = code[pc++];
                        var slots = GetEnvironment(frame.Environment, depth).Slots;
                        if (slots[slot].IsHole)
                        {
                            ThrowTdz(keys[name]);
                        }

                        slots[slot] = values[--sp];
                        break;
                    }

                case OpCode.ThrowConstAssignLocal:
                    {
                        var register = code[pc++];
                        var name = code[pc++];
                        if (register >= 0 && values[register].IsHole)
                        {
                            ThrowTdz(keys[name]);
                        }

                        ThrowHelper.ThrowTypeError("Assignment to constant variable.");
                        break;
                    }

                case OpCode.ThrowConstAssignContext:
                    {
                        var depth = code[pc++];
                        var slot = code[pc++];
                        var name = code[pc++];
                        if (slot >= 0 && GetEnvironment(frame.Environment, depth).Slots[slot].IsHole)
                        {
                            ThrowTdz(keys[name]);
                        }

                        ThrowHelper.ThrowTypeError("Assignment to constant variable.");
                        break;
                    }

                case OpCode.LoadGlobal:
                    values[sp++] = LoadGlobal(frame.Realm, keys[code[pc++]], throwIfMissing: true);
                    break;
                case OpCode.LoadGlobalOrUndefined:
                    values[sp++] = LoadGlobal(frame.Realm, keys[code[pc++]], throwIfMissing: false);
                    break;
                case OpCode.StoreGlobal:
                    {
                        var name = keys[code[pc++]];
                        var isStrict = code[pc++] != 0;
                        StoreGlobal(frame.Realm, name, values[--sp], isStrict);
                        break;
                    }

                case OpCode.LoadName:
                    values[sp++] = LoadName(frame, keys[code[pc++]], throwIfMissing: true, out _);
                    break;
                case OpCode.LoadNameOrUndefined:
                    values[sp++] = LoadName(frame, keys[code[pc++]], throwIfMissing: false, out _);
                    break;
                case OpCode.LoadNameForCall:
                    {
                        values[sp++] = LoadName(frame, keys[code[pc++]], throwIfMissing: true, out var thisValue);
                        values[sp++] = thisValue;
                        break;
                    }

                case OpCode.StoreName:
                    {
                        var name = keys[code[pc++]];
                        var isStrict = code[pc++] != 0;
                        StoreName(frame, name, values[--sp], isStrict);
                        break;
                    }

                case OpCode.DeleteName:
                    values[sp++] = DeleteName(frame, keys[code[pc++]]);
                    break;
                case OpCode.InitializeName:
                    InitializeName(frame, keys[code[pc++]], values[--sp]);
                    break;
                case OpCode.PushScope:
                    frame.Environment = new DeclarativeEnvironment((ScopeInfo)objects[code[pc++]], frame.Environment);
                    break;
                case OpCode.PopScope:
                    frame.Environment = frame.Environment.Outer!;
                    break;
                case OpCode.PushWith:
                    frame.Environment = new ObjectEnvironment(values[--sp].AsObject(), frame.Environment);
                    break;
                case OpCode.CopyScope:
                    frame.Environment = ((DeclarativeEnvironment)frame.Environment).Copy();
                    break;
                case OpCode.SaveEnvironment:
                    values[code[pc++]] = new JsValue(frame.Environment);
                    break;
                case OpCode.RestoreEnvironment:
                    frame.Environment = (Environment)values[code[pc++]].Cell!;
                    break;
                case OpCode.LoadArgument:
                    {
                        var index = code[pc++];
                        values[sp++] = index < frame.Arguments.Length ? frame.Arguments[index] : default;
                        break;
                    }

                case OpCode.LoadRestArguments:
                    {
                        var index = code[pc++];
                        var arguments = frame.Arguments;
                        values[sp++] = JsArray.Create(frame.Realm, index < arguments.Length ? arguments.AsSpan(index) : []);
                        break;
                    }

                case OpCode.LoadAllArguments:
                    values[sp++] = JsArray.Create(frame.Realm, frame.Arguments);
                    break;
                case OpCode.CreateArguments:
                    values[sp++] = CreateArgumentsObject(frame, mapped: code[pc++] != 0);
                    break;
                case OpCode.LoadThis:
                    {
                        var thisValue = frame.This;
                        if (thisValue.IsHole)
                        {
                            ThrowHelper.ThrowReferenceError("Must call super constructor in derived class before accessing 'this' or returning from derived constructor");
                        }

                        values[sp++] = thisValue;
                        break;
                    }

                case OpCode.LoadFunction:
                    values[sp++] = frame.Function is { } currentFunction ? new JsValue(currentFunction) : default;
                    break;
                case OpCode.LoadNewTarget:
                    values[sp++] = frame.NewTarget is { } currentNewTarget ? new JsValue(currentNewTarget) : default;
                    break;
                case OpCode.LoadHomeObject:
                    values[sp++] = frame.Function?.HomeObject is { } currentHome ? new JsValue(currentHome) : default;
                    break;
                case OpCode.LoadGlobalThis:
                    values[sp++] = frame.Realm.GlobalThis;
                    break;
                case OpCode.LoadImportMeta:
                    values[sp++] = GetImportMeta(frame);
                    break;
                case OpCode.BindThis:
                    if (!frame.This.IsHole)
                    {
                        ThrowHelper.ThrowReferenceError("Super constructor may only be called once");
                    }

                    frame.This = values[sp - 1];
                    break;
                case OpCode.CheckThisUninitialized:
                    if (!values[sp - 1].IsHole)
                    {
                        ThrowHelper.ThrowReferenceError("Super constructor may only be called once");
                    }

                    break;

                // ---- Operators ------------------------------------------------------------------------------------
                case OpCode.Add:
                    {
                        var left = values[sp - 2];
                        var right = values[sp - 1];
                        sp--;
                        values[sp - 1] = left.IsNumber && right.IsNumber ? new JsValue(left.NumberValue + right.NumberValue) : Operators.Add(left, right);
                        break;
                    }

                case OpCode.Subtract:
                case OpCode.Multiply:
                case OpCode.Divide:
                case OpCode.Modulo:
                case OpCode.Exponentiate:
                case OpCode.LeftShift:
                case OpCode.RightShift:
                case OpCode.UnsignedRightShift:
                case OpCode.BitwiseAnd:
                case OpCode.BitwiseOr:
                case OpCode.BitwiseXor:
                    {
                        var left = values[sp - 2];
                        var right = values[sp - 1];
                        sp--;
                        values[sp - 1] = Operators.Binary(op, left, right);
                        break;
                    }

                case OpCode.Equal:
                    sp--;
                    values[sp - 1] = JsValue.FromBoolean(JsOperations.IsLooselyEqual(values[sp - 1], values[sp]));
                    break;
                case OpCode.NotEqual:
                    sp--;
                    values[sp - 1] = JsValue.FromBoolean(!JsOperations.IsLooselyEqual(values[sp - 1], values[sp]));
                    break;
                case OpCode.StrictEqual:
                    sp--;
                    values[sp - 1] = JsValue.FromBoolean(JsOperations.IsStrictlyEqual(values[sp - 1], values[sp]));
                    break;
                case OpCode.StrictNotEqual:
                    sp--;
                    values[sp - 1] = JsValue.FromBoolean(!JsOperations.IsStrictlyEqual(values[sp - 1], values[sp]));
                    break;
                case OpCode.LessThan:
                    sp--;
                    values[sp - 1] = Operators.LessThan(values[sp - 1], values[sp], leftFirst: true) is true ? JsValue.True : JsValue.False;
                    break;
                case OpCode.GreaterThan:
                    sp--;
                    values[sp - 1] = Operators.LessThan(values[sp], values[sp - 1], leftFirst: false) is true ? JsValue.True : JsValue.False;
                    break;
                case OpCode.LessThanOrEqual:
                    sp--;
                    values[sp - 1] = Operators.LessThan(values[sp], values[sp - 1], leftFirst: false) is false ? JsValue.True : JsValue.False;
                    break;
                case OpCode.GreaterThanOrEqual:
                    sp--;
                    values[sp - 1] = Operators.LessThan(values[sp - 1], values[sp], leftFirst: true) is false ? JsValue.True : JsValue.False;
                    break;
                case OpCode.InstanceOf:
                    sp--;
                    values[sp - 1] = JsValue.FromBoolean(ObjectOperations.InstanceOf(values[sp - 1], values[sp]));
                    break;
                case OpCode.In:
                    {
                        sp--;
                        var target = values[sp];
                        if (target.Cell is not JsObject targetObject)
                        {
                            ThrowHelper.ThrowTypeError("Cannot use 'in' operator to search for a key in " + JsOperations.DescribeForError(target));
                            break;
                        }

                        values[sp - 1] = JsValue.FromBoolean(targetObject.HasProperty(JsOperations.ToPropertyKey(values[sp - 1])));
                        break;
                    }

                case OpCode.PrivateIn:
                    {
                        var depth = code[pc++];
                        var slot = code[pc++];
                        var privateName = GetPrivateName(frame, values, ref sp, depth, slot);
                        var target = values[sp - 1];
                        if (target.Cell is not JsObject targetObject)
                        {
                            ThrowHelper.ThrowTypeError("Cannot use 'in' operator to search for a private field in a non-object");
                            break;
                        }

                        values[sp - 1] = JsValue.FromBoolean(targetObject.PrivateElements?.Contains(privateName) == true);
                        break;
                    }

                case OpCode.Negate:
                    {
                        var value = values[sp - 1];
                        values[sp - 1] = value.IsNumber ? new JsValue(-value.NumberValue) : Operators.Negate(value);
                        break;
                    }

                case OpCode.ToNumber:
                    values[sp - 1] = new JsValue(JsOperations.ToNumber(values[sp - 1]));
                    break;
                case OpCode.ToNumeric:
                    values[sp - 1] = JsOperations.ToNumeric(values[sp - 1]);
                    break;
                case OpCode.BitwiseNot:
                    values[sp - 1] = Operators.BitwiseNot(values[sp - 1]);
                    break;
                case OpCode.LogicalNot:
                    values[sp - 1] = JsValue.FromBoolean(!JsOperations.ToBoolean(values[sp - 1]));
                    break;
                case OpCode.TypeOf:
                    values[sp - 1] = JsValue.FromJsString(Operators.TypeOf(values[sp - 1]));
                    break;
                case OpCode.Increment:
                    {
                        var value = values[sp - 1];
                        values[sp - 1] = value.IsNumber ? new JsValue(value.NumberValue + 1) : Operators.Increment(value, 1);
                        break;
                    }

                case OpCode.Decrement:
                    {
                        var value = values[sp - 1];
                        values[sp - 1] = value.IsNumber ? new JsValue(value.NumberValue - 1) : Operators.Increment(value, -1);
                        break;
                    }

                case OpCode.ToStringValue:
                    values[sp - 1] = JsValue.FromJsString(JsOperations.ToJsString(values[sp - 1]));
                    break;
                case OpCode.ToPropertyKey:
                    {
                        var value = values[sp - 1];
                        if (!value.IsString && !value.IsSymbol && !value.IsNumber)
                        {
                            values[sp - 1] = JsOperations.ToPropertyKey(value).ToJsValue();
                        }

                        break;
                    }

                case OpCode.ToObject:
                    values[sp - 1] = JsOperations.ToObject(values[sp - 1]);
                    break;
                case OpCode.RequireObjectCoercible:
                    if (values[sp - 1].IsNullOrUndefined)
                    {
                        ThrowHelper.ThrowTypeError($"Cannot destructure '{values[sp - 1]}' as it is {values[sp - 1]}.");
                    }

                    break;

                case OpCode.RequireObjectCoercibleBase:
                    if (values[sp - 2].IsNullOrUndefined)
                    {
                        ThrowHelper.ThrowTypeError($"Cannot read properties of {values[sp - 2]}");
                    }

                    break;
                case OpCode.ResolveName:
                    values[sp++] = ResolveName(frame, keys[code[pc++]]);
                    break;
                case OpCode.LoadResolved:
                    values[sp - 1] = LoadResolved(frame, values[sp - 1], keys[code[pc++]]);
                    break;
                case OpCode.StoreResolved:
                    {
                        var name = keys[code[pc++]];
                        var isStrict = code[pc++] != 0;
                        var value = values[--sp];
                        StoreResolved(frame, values[sp - 1], name, value, isStrict);
                        values[sp - 1] = value;
                        break;
                    }

                // ---- Control flow ---------------------------------------------------------------------------------
                case OpCode.Jump:
                    {
                        var target = code[pc];
                        if (target < pc)
                        {
                            engine.CountInterruptPoint();
                        }

                        pc = target;
                        break;
                    }

                case OpCode.JumpIfTrue:
                    {
                        var target = code[pc++];
                        if (JsOperations.ToBoolean(values[--sp]))
                        {
                            pc = target;
                        }

                        break;
                    }

                case OpCode.JumpIfFalse:
                    {
                        var target = code[pc++];
                        if (!JsOperations.ToBoolean(values[--sp]))
                        {
                            pc = target;
                        }

                        break;
                    }

                case OpCode.JumpIfTrueKeep:
                    {
                        var target = code[pc++];
                        if (JsOperations.ToBoolean(values[sp - 1]))
                        {
                            pc = target;
                        }

                        break;
                    }

                case OpCode.JumpIfFalseKeep:
                    {
                        var target = code[pc++];
                        if (!JsOperations.ToBoolean(values[sp - 1]))
                        {
                            pc = target;
                        }

                        break;
                    }

                case OpCode.JumpIfNullishKeep:
                    {
                        var target = code[pc++];
                        if (values[sp - 1].IsNullOrUndefined)
                        {
                            pc = target;
                        }

                        break;
                    }

                case OpCode.JumpIfNotNullishKeep:
                    {
                        var target = code[pc++];
                        if (!values[sp - 1].IsNullOrUndefined)
                        {
                            pc = target;
                        }

                        break;
                    }

                case OpCode.JumpIfNotUndefinedKeep:
                    {
                        var target = code[pc++];
                        if (!values[sp - 1].IsUndefined)
                        {
                            pc = target;
                        }

                        break;
                    }

                case OpCode.JumpIfUndefinedPop:
                    {
                        var target = code[pc++];
                        if (values[sp - 1].IsUndefined)
                        {
                            sp--;
                            pc = target;
                        }

                        break;
                    }

                case OpCode.Throw:
                    {
                        var exception = values[--sp];
                        frame.Sp = sp;
                        var location = GetLocation(frame.Code, pc);
                        if (!HandleException(ref frame, entry, engine, exception, isFaultingFrame: true))
                            throw new JsException(exception, location);

                        template = frame.Code;
                        code = template.Code;
                        values = frame.Values;
                        constants = template.Constants;
                        keys = template.Keys;
                        objects = template.Objects;
                        pc = frame.Pc;
                        sp = frame.Sp;
                        strict = template.IsStrict;
                        break;
                    }

                case OpCode.ThrowError:
                    {
                        var type = (ErrorType)code[pc++];
                        var message = constants[code[pc++]].AsString();
                        ThrowHelper.ThrowError(type, message);
                        break;
                    }

                case OpCode.Return:
                    {
                        var result = values[--sp];
                        if (frame.IsConstruct)
                        {
                            result = GetConstructResult(frame, result);
                        }

                        frame.State = FrameState.Completed;
                        if (ReferenceEquals(frame, entry))
                            return result;

                        engine.PopFrame();
                        engine.CallDepth--;
                        frame = frame.Parent!;
                        engine.CurrentRealm = frame.Realm;
                        template = frame.Code;
                        code = template.Code;
                        values = frame.Values;
                        constants = template.Constants;
                        keys = template.Keys;
                        objects = template.Objects;
                        pc = frame.Pc;
                        sp = frame.Sp;
                        strict = template.IsStrict;
                        values[sp++] = result;
                        break;
                    }

                case OpCode.Debugger:
                    break;

                // ---- Objects and properties -----------------------------------------------------------------------
                case OpCode.NewObject:
                    values[sp++] = new JsObject(frame.Realm.Intrinsics.ObjectPrototype);
                    break;
                case OpCode.NewArray:
                    values[sp++] = new JsArray(frame.Realm.Intrinsics.ArrayPrototype);
                    break;
                case OpCode.ArrayAppend:
                    {
                        var value = values[--sp];
                        var array = (JsArray)values[sp - 1].Cell!;
                        if (value.IsHole)
                        {
                            array.DefineOwnProperty(new PropertyKey("length"), PropertyDescriptor.ValueOnly(array.Length + 1));
                        }
                        else
                        {
                            array.Push(value);
                        }

                        break;
                    }

                case OpCode.ArrayAppendSpread:
                    {
                        var iterable = values[--sp];
                        frame.Sp = sp;
                        AppendSpread((JsArray)values[sp - 1].Cell!, iterable);
                        break;
                    }

                case OpCode.GetProperty:
                    {
                        var key = keys[code[pc++]];
                        var target = values[sp - 1];
                        values[sp - 1] = target.Cell is JsObject targetObject ? targetObject.Get(key, target) : GetPropertyOfPrimitive(frame.Realm, target, key);
                        break;
                    }

                case OpCode.SetProperty:
                    {
                        var key = keys[code[pc++]];
                        var value = values[--sp];
                        var target = values[sp - 1];
                        SetPropertyOfValue(target, key, value, strict);
                        values[sp - 1] = value;
                        break;
                    }

                case OpCode.GetElement:
                    {
                        var keyValue = values[--sp];
                        var target = values[sp - 1];
                        values[sp - 1] = GetElement(frame.Realm, target, keyValue);
                        break;
                    }

                case OpCode.SetElement:
                    {
                        var value = values[--sp];
                        var keyValue = values[--sp];
                        var target = values[sp - 1];
                        if (target.IsNullOrUndefined)
                        {
                            ThrowHelper.ThrowTypeError($"Cannot set properties of {target} (setting '{JsOperations.ToPropertyKey(keyValue)}')");
                        }

                        var key = keyValue.IsNumber ? PropertyKey.FromDouble(keyValue.NumberValue) : JsOperations.ToPropertyKey(keyValue);
                        SetPropertyOfValue(target, key, value, strict);
                        values[sp - 1] = value;
                        break;
                    }

                case OpCode.DeleteProperty:
                    {
                        var key = keys[code[pc++]];
                        var isStrict = code[pc++] != 0;
                        values[sp - 1] = DeleteProperty(values[sp - 1], key, isStrict);
                        break;
                    }

                case OpCode.DeleteElement:
                    {
                        var isStrict = code[pc++] != 0;
                        var keyValue = values[--sp];
                        var target = values[sp - 1];
                        var targetObject = JsOperations.ToObject(target);
                        values[sp - 1] = DeleteProperty(targetObject, JsOperations.ToPropertyKey(keyValue), isStrict);
                        break;
                    }

                case OpCode.DefineField:
                    {
                        var key = keys[code[pc++]];
                        var value = values[--sp];
                        ((JsObject)values[sp - 1].Cell!).CreateDataPropertyOrThrow(key, value);
                        break;
                    }

                case OpCode.DefineFieldComputed:
                    {
                        var value = values[--sp];
                        var key = JsOperations.ToPropertyKey(values[--sp]);
                        ((JsObject)values[sp - 1].Cell!).CreateDataPropertyOrThrow(key, value);
                        break;
                    }

                case OpCode.DefineMethod:
                    {
                        var kind = code[pc++];
                        var enumerable = code[pc++] != 0;
                        var function = (JsClosure)values[--sp].Cell!;
                        var key = JsOperations.ToPropertyKey(values[--sp]);
                        var target = (JsObject)values[sp - 1].Cell!;
                        DefineMethod(target, key, function, kind, enumerable);
                        break;
                    }

                case OpCode.SetPrototypeLiteral:
                    {
                        var value = values[--sp];
                        var target = (JsObject)values[sp - 1].Cell!;
                        if (value.Cell is JsObject prototype)
                        {
                            target.SetPrototypeOf(prototype);
                        }
                        else if (value.IsNull)
                        {
                            target.SetPrototypeOf(null);
                        }

                        break;
                    }

                case OpCode.CopyDataProperties:
                    {
                        var source = values[--sp];
                        PropertyOperations.CopyDataProperties((JsObject)values[sp - 1].Cell!, source, []);
                        break;
                    }

                case OpCode.CopyDataPropertiesExcluding:
                    {
                        var count = code[pc++];
                        var excluded = new PropertyKey[count];
                        for (var i = count - 1; i >= 0; i--)
                        {
                            excluded[i] = JsOperations.ToPropertyKey(values[--sp]);
                        }

                        var source = values[sp - 1];
                        var result = new JsObject(frame.Realm.Intrinsics.ObjectPrototype);
                        PropertyOperations.CopyDataProperties(result, source, excluded);
                        values[sp - 1] = result;
                        break;
                    }

                case OpCode.GetSuperProperty:
                    {
                        var key = keys[code[pc++]];
                        var home = values[--sp];
                        var thisValue = values[sp - 1];
                        values[sp - 1] = GetSuperBase(home).Get(key, thisValue);
                        break;
                    }

                case OpCode.GetSuperElement:
                    {
                        var home = values[--sp];
                        var key = JsOperations.ToPropertyKey(values[--sp]);
                        var thisValue = values[sp - 1];
                        values[sp - 1] = GetSuperBase(home).Get(key, thisValue);
                        break;
                    }

                case OpCode.SetSuperProperty:
                    {
                        var key = keys[code[pc++]];
                        var home = values[--sp];
                        var value = values[--sp];
                        var thisValue = values[sp - 1];
                        SetSuper(home, key, value, thisValue);
                        values[sp - 1] = value;
                        break;
                    }

                case OpCode.SetSuperElement:
                    {
                        var home = values[--sp];
                        var value = values[--sp];
                        var key = JsOperations.ToPropertyKey(values[--sp]);
                        var thisValue = values[sp - 1];
                        SetSuper(home, key, value, thisValue);
                        values[sp - 1] = value;
                        break;
                    }

                case OpCode.GetPrivate:
                    {
                        var depth = code[pc++];
                        var slot = code[pc++];
                        var privateName = GetPrivateName(frame, values, ref sp, depth, slot);
                        values[sp - 1] = PrivateGet(values[sp - 1], privateName);
                        break;
                    }

                case OpCode.SetPrivate:
                    {
                        var depth = code[pc++];
                        var slot = code[pc++];
                        var privateName = GetPrivateName(frame, values, ref sp, depth, slot);
                        var value = values[--sp];
                        PrivateSet(values[sp - 1], privateName, value);
                        values[sp - 1] = value;
                        break;
                    }

                // ---- Calls ----------------------------------------------------------------------------------------
                case OpCode.Call:
                case OpCode.CallSpread:
                    {
                        JsValue function;
                        JsValue thisValue;
                        JsValue[]? spreadArguments = null;
                        int argumentsStart;
                        int argumentCount;
                        if (op == OpCode.Call)
                        {
                            argumentCount = code[pc++];
                            var functionIndex = sp - argumentCount - 2;
                            function = values[functionIndex];
                            thisValue = values[functionIndex + 1];
                            argumentsStart = functionIndex + 2;
                            sp = functionIndex;
                        }
                        else
                        {
                            spreadArguments = GetSpreadArguments(values[--sp]);
                            thisValue = values[--sp];
                            function = values[--sp];
                            argumentsStart = 0;
                            argumentCount = spreadArguments.Length;
                        }

                        var arguments = spreadArguments is null ? values.AsSpan(argumentsStart, argumentCount) : spreadArguments;
                        if (function.Cell is JsClosure closure && CanRunInline(closure))
                        {
                            frame.Pc = pc;
                            frame.Sp = sp;
                            var callee = CreateCallFrame(closure, thisValue, arguments, newTarget: null);
                            frame = PushInlineFrame(frame, callee, engine);
                            template = frame.Code;
                            code = template.Code;
                            values = frame.Values;
                            constants = template.Constants;
                            keys = template.Keys;
                            objects = template.Objects;
                            pc = frame.Pc;
                            sp = frame.Sp;
                            strict = template.IsStrict;
                            break;
                        }

                        frame.Sp = sp;
                        if (function.Cell is not JsObject { IsCallable: true } callable)
                        {
                            ThrowNotCallable(frame, function);
                            break;
                        }

                        var result = callable.Call(thisValue, arguments);
                        values[sp++] = result;
                        break;
                    }

                case OpCode.CallEval:
                case OpCode.CallEvalSpread:
                    {
                        JsValue function;
                        JsValue thisValue;
                        JsValue[] arguments;
                        EvalContext context;
                        if (op == OpCode.CallEval)
                        {
                            var argumentCount = code[pc++];
                            context = (EvalContext)objects[code[pc++]];
                            var functionIndex = sp - argumentCount - 2;
                            function = values[functionIndex];
                            thisValue = values[functionIndex + 1];
                            arguments = values.AsSpan(functionIndex + 2, argumentCount).ToArray();
                            sp = functionIndex;
                        }
                        else
                        {
                            context = (EvalContext)objects[code[pc++]];
                            arguments = GetSpreadArguments(values[--sp]);
                            thisValue = values[--sp];
                            function = values[--sp];
                        }

                        frame.Sp = sp;
                        values[sp++] = CallEval(frame, function, thisValue, arguments, context);
                        break;
                    }

                case OpCode.New:
                case OpCode.NewSpread:
                    {
                        JsValue constructor;
                        JsValue[]? spreadArguments = null;
                        int argumentsStart;
                        int argumentCount;
                        if (op == OpCode.New)
                        {
                            argumentCount = code[pc++];
                            var constructorIndex = sp - argumentCount - 1;
                            constructor = values[constructorIndex];
                            argumentsStart = constructorIndex + 1;
                            sp = constructorIndex;
                        }
                        else
                        {
                            spreadArguments = GetSpreadArguments(values[--sp]);
                            constructor = values[--sp];
                            argumentsStart = 0;
                            argumentCount = spreadArguments.Length;
                        }

                        var arguments = spreadArguments is null ? values.AsSpan(argumentsStart, argumentCount) : spreadArguments;
                        frame.Sp = sp;
                        if (constructor.Cell is not JsObject { IsConstructor: true } constructorObject)
                        {
                            ThrowHelper.ThrowTypeError($"{DescribeCallee(frame)} is not a constructor");
                            break;
                        }

                        if (constructorObject is JsClosure closure && CanConstructInline(closure))
                        {
                            frame.Pc = pc;
                            var callee = CreateConstructFrame(closure, arguments, closure);
                            frame = PushInlineFrame(frame, callee, engine);
                            template = frame.Code;
                            code = template.Code;
                            values = frame.Values;
                            constants = template.Constants;
                            keys = template.Keys;
                            objects = template.Objects;
                            pc = frame.Pc;
                            sp = frame.Sp;
                            strict = template.IsStrict;
                            break;
                        }

                        values[sp++] = constructorObject.Construct(arguments, constructorObject);
                        break;
                    }

                case OpCode.SuperCall:
                case OpCode.SuperCallSpread:
                    {
                        JsValue[] arguments;
                        if (op == OpCode.SuperCall)
                        {
                            var argumentCount = code[pc++];
                            arguments = values.AsSpan(sp - argumentCount, argumentCount).ToArray();
                            sp -= argumentCount;
                        }
                        else
                        {
                            arguments = GetSpreadArguments(values[--sp]);
                        }

                        var newTarget = values[--sp];
                        var activeFunction = values[--sp];
                        frame.Sp = sp;
                        values[sp++] = SuperCall(activeFunction, newTarget, arguments);
                        break;
                    }

                case OpCode.ImportCall:
                    {
                        var options = values[--sp];
                        var specifier = values[--sp];
                        frame.Sp = sp;
                        values[sp++] = ImportCall(frame, specifier, options);
                        break;
                    }

                // ---- Functions and classes ------------------------------------------------------------------------
                case OpCode.CreateClosure:
                    values[sp++] = CreateClosure(frame, (FunctionTemplate)objects[code[pc++]]);
                    break;
                case OpCode.CreateClass:
                    {
                        var classTemplate = (ClassTemplate)objects[code[pc++]];
                        var superclass = values[sp - 1];
                        var (constructor, prototype) = CreateClass(frame, classTemplate, superclass);
                        values[sp - 1] = constructor;
                        values[sp++] = prototype;
                        break;
                    }

                case OpCode.InitializeInstanceElements:
                    {
                        var constructor = (JsClosure)values[--sp].Cell!;
                        InitializeInstanceElements((JsObject)values[sp - 1].Cell!, constructor);
                        break;
                    }

                case OpCode.CreatePrivateName:
                    values[sp++] = new JsValue(new PrivateNameCell(new PrivateName(constants[code[pc++]].AsString()[1..])));
                    break;
                case OpCode.DefinePrivateMethod:
                    {
                        var kind = code[pc++];
                        var isStatic = code[pc++] != 0;
                        var privateName = ((PrivateNameCell)values[--sp].Cell!).Name;
                        var function = (JsClosure)values[--sp].Cell!;
                        var home = (JsObject)values[--sp].Cell!;
                        var constructor = (JsClosure)values[--sp].Cell!;
                        DefinePrivateMethod(constructor, home, function, privateName, kind, isStatic);
                        break;
                    }

                case OpCode.SetFieldInitializer:
                    {
                        var initializer = (JsClosure)values[--sp].Cell!;
                        var constructor = (JsClosure)values[--sp].Cell!;
                        initializer.HomeObject = constructor.Get(new PropertyKey("prototype")).TryGetObject();
                        constructor.FieldInitializer = initializer;
                        break;
                    }

                case OpCode.CallClassStaticInitializer:
                    {
                        var initializer = (JsClosure)values[--sp].Cell!;
                        var constructor = (JsClosure)values[--sp].Cell!;
                        initializer.HomeObject = constructor;
                        frame.Sp = sp;
                        initializer.Call(constructor, []);
                        break;
                    }

                case OpCode.SetHomeObject:
                    {
                        var home = values[--sp];
                        ((JsClosure)values[sp - 1].Cell!).HomeObject = home.TryGetObject();
                        break;
                    }

                case OpCode.DefineClassField:
                    {
                        var key = keys[code[pc++]];
                        var value = values[--sp];
                        var target = (JsObject)values[--sp].Cell!;
                        target.CreateDataPropertyOrThrow(key, value);
                        break;
                    }

                case OpCode.DefineClassFieldComputed:
                    {
                        var value = values[--sp];
                        var key = JsOperations.ToPropertyKey(values[--sp]);
                        var target = (JsObject)values[--sp].Cell!;
                        target.CreateDataPropertyOrThrow(key, value);
                        break;
                    }

                case OpCode.DefinePrivateField:
                    {
                        var value = values[--sp];
                        var privateName = ((PrivateNameCell)values[--sp].Cell!).Name;
                        var target = (JsObject)values[--sp].Cell!;
                        PrivateFieldAdd(target, privateName, value);
                        break;
                    }

                case OpCode.SetFunctionName:
                    {
                        var name = values[--sp];
                        SetFunctionNameAtRuntime((JsObject)values[sp - 1].Cell!, name, prefix: 0);
                        break;
                    }

                case OpCode.SetFunctionNameComputed:
                    {
                        var prefix = code[pc++];
                        var name = values[--sp];
                        SetFunctionNameAtRuntime((JsObject)values[sp - 1].Cell!, name, prefix);
                        break;
                    }

                case OpCode.GetTemplateObject:
                    values[sp++] = GetTemplateObject(frame.Realm, (TemplateSite)objects[code[pc++]]);
                    break;
                case OpCode.CreateRegExp:
                    {
                        var pattern = constants[code[pc++]].AsString();
                        var flags = constants[code[pc++]].AsString();
                        values[sp++] = CreateRegExp(frame.Realm, pattern, flags);
                        break;
                    }

                // ---- Iteration ------------------------------------------------------------------------------------
                case OpCode.GetIterator:
                    values[sp - 1] = new JsValue(new IteratorRecordCell(IteratorOperations.GetIterator(values[sp - 1])));
                    break;
                case OpCode.GetAsyncIterator:
                    values[sp - 1] = new JsValue(new IteratorRecordCell(GetAsyncIterator(frame.Realm, values[sp - 1])));
                    break;
                case OpCode.IteratorNext:
                    {
                        var target = code[pc++];
                        var record = ((IteratorRecordCell)values[sp - 1].Cell!).Record;
                        frame.Sp = sp;
                        if (IteratorOperations.IteratorStepValue(record, out var value))
                        {
                            values[sp - 1] = value;
                        }
                        else
                        {
                            sp--;
                            pc = target;
                        }

                        break;
                    }

                case OpCode.IteratorNextOrUndefined:
                    {
                        var record = ((IteratorRecordCell)values[sp - 1].Cell!).Record;
                        if (record.Done)
                        {
                            values[sp - 1] = default;
                            break;
                        }

                        values[sp - 1] = IteratorOperations.IteratorStepValue(record, out var value) ? value : default;
                        break;
                    }

                case OpCode.IteratorRest:
                    {
                        var record = ((IteratorRecordCell)values[sp - 1].Cell!).Record;
                        var array = new JsArray(frame.Realm.Intrinsics.ArrayPrototype);
                        while (!record.Done && IteratorOperations.IteratorStepValue(record, out var value))
                        {
                            array.Push(value);
                        }

                        values[sp - 1] = array;
                        break;
                    }

                case OpCode.IteratorCallNext:
                    {
                        var record = ((IteratorRecordCell)values[sp - 1].Cell!).Record;
                        values[sp - 1] = JsOperations.Call(record.NextMethod, record.Iterator, []);
                        break;
                    }

                case OpCode.IteratorStepFinish:
                    {
                        var target = code[pc++];
                        var result = values[--sp];
                        var record = ((IteratorRecordCell)values[sp - 1].Cell!).Record;
                        if (result.Cell is not JsObject resultObject)
                        {
                            record.Done = true;
                            ThrowHelper.ThrowTypeError("Iterator result is not an object");
                            break;
                        }

                        if (IteratorOperations.IteratorComplete(resultObject))
                        {
                            record.Done = true;
                            sp--;
                            pc = target;
                            break;
                        }

                        values[sp - 1] = IteratorOperations.IteratorValue(resultObject);
                        break;
                    }

                case OpCode.IteratorClose:
                    {
                        var record = ((IteratorRecordCell)values[--sp].Cell!).Record;
                        frame.Sp = sp;
                        if (!record.Done)
                        {
                            record.Done = true;
                            IteratorOperations.IteratorClose(record);
                        }

                        break;
                    }

                case OpCode.IteratorCloseOnThrow:
                    {
                        var record = ((IteratorRecordCell)values[--sp].Cell!).Record;
                        frame.Sp = sp;
                        if (!record.Done)
                        {
                            record.Done = true;
                            IteratorOperations.IteratorCloseOnThrow(record);
                        }

                        break;
                    }

                case OpCode.IteratorCallReturn:
                    {
                        var record = ((IteratorRecordCell)values[sp - 1].Cell!).Record;
                        if (record.Done)
                        {
                            values[sp - 1] = default;
                            break;
                        }

                        record.Done = true;
                        var returnMethod = JsOperations.GetMethod(record.Iterator, new PropertyKey("return"));
                        values[sp - 1] = returnMethod is null ? default : returnMethod.Call(record.Iterator, []);
                        break;
                    }

                case OpCode.ThrowIfNotObject:
                    if (!values[sp - 1].IsObject)
                    {
                        ThrowHelper.ThrowTypeError("Iterator result " + JsOperations.DescribeForError(values[sp - 1]) + " is not an object");
                    }

                    break;
                case OpCode.IteratorIsDone:
                    values[sp - 1] = JsValue.FromBoolean(((IteratorRecordCell)values[sp - 1].Cell!).Record.Done);
                    break;
                case OpCode.IteratorDelegateStep:
                    {
                        var isAsync = code[pc++] != 0;
                        var kind = (int)values[--sp].NumberValue;
                        var received = values[--sp];
                        var record = ((IteratorRecordCell)values[sp - 1].Cell!).Record;
                        frame.Sp = sp;
                        values[sp - 1] = DelegateStep(record, received, kind, isAsync);
                        break;
                    }

                case OpCode.IteratorComplete:
                    values[sp - 1] = JsValue.FromBoolean(IteratorOperations.IteratorComplete(values[sp - 1].AsObject()));
                    break;
                case OpCode.IteratorValue:
                    values[sp - 1] = IteratorOperations.IteratorValue(values[sp - 1].AsObject());
                    break;
                case OpCode.ForInPrepare:
                    values[sp - 1] = new JsValue(new ForInEnumerator(JsOperations.ToObject(values[sp - 1])));
                    break;
                case OpCode.ForInNext:
                    {
                        var target = code[pc++];
                        var enumerator = (ForInEnumerator)values[sp - 1].Cell!;
                        if (enumerator.MoveNext(out var key))
                        {
                            values[sp - 1] = key;
                        }
                        else
                        {
                            sp--;
                            pc = target;
                        }

                        break;
                    }

                // ---- Generators and async functions ---------------------------------------------------------------
                case OpCode.GeneratorStart:
                    frame.Pc = pc;
                    frame.Sp = sp;
                    frame.State = FrameState.SuspendedYield;
                    return default;
                case OpCode.Yield:
                    {
                        var value = values[--sp];
                        frame.Pc = pc;
                        frame.Sp = sp;
                        frame.State = FrameState.SuspendedYield;
                        return value;
                    }

                case OpCode.Await:
                    {
                        var value = values[--sp];
                        frame.Pc = pc;
                        frame.Sp = sp;
                        frame.State = FrameState.SuspendedAwait;
                        return value;
                    }

                case OpCode.CreateIterResult:
                    values[sp - 1] = IteratorOperations.CreateIteratorResultObject(frame.Realm, values[sp - 1], done: false);
                    break;
                case OpCode.CreateIterResultDone:
                    values[sp - 1] = IteratorOperations.CreateIteratorResultObject(frame.Realm, values[sp - 1], done: code[pc++] != 0);
                    break;

                // ---- Declarations ---------------------------------------------------------------------------------
                case OpCode.GlobalDeclarationInstantiation:
                    GlobalDeclarationInstantiation(frame.Realm, (GlobalDeclarations)objects[code[pc++]]);
                    break;
                case OpCode.EvalDeclarationInstantiation:
                    EvalDeclarationInstantiation(frame, (EvalDeclarations)objects[code[pc++]]);
                    break;
                case OpCode.InitializeGlobalLexical:
                    InitializeGlobalLexical(frame.Realm, keys[code[pc++]], values[--sp]);
                    break;
                case OpCode.InitializeGlobalFunction:
                    InitializeGlobalFunction(frame.Realm, keys[code[pc++]], values[--sp]);
                    break;
                case OpCode.SetVarBinding:
                    SetVarBinding(frame, keys[code[pc++]], values[--sp]);
                    break;
                case OpCode.SetGlobalVar:
                    frame.Realm.GlobalObject.Set(keys[code[pc++]], values[--sp], throwOnFailure: false);
                    break;
                case OpCode.AddDisposableResource:
                    {
                        var hint = code[pc++] != 0 ? DisposeHint.AsyncDispose : DisposeHint.SyncDispose;
                        var value = values[--sp];
                        var capability = (DisposeCapabilityCell)values[--sp].Cell!;
                        DisposableOperations.AddDisposableResource(capability.Capability, value, hint);
                        break;
                    }

                case OpCode.NewDisposeCapability:
                    values[sp++] = new JsValue(new DisposeCapabilityCell(DisposableOperations.NewDisposeCapability()));
                    break;
                case OpCode.DisposeResources:
                    {
                        var value = values[--sp];
                        var type = (int)values[--sp].NumberValue;
                        var capability = ((DisposeCapabilityCell)values[--sp].Cell!).Capability;
                        frame.Sp = sp;
                        if (DisposableOperations.DisposeResources(capability, type == 1 ? value : (JsValue?)null) is { } thrown)
                            throw new JsException(thrown);

                        break;
                    }

                case OpCode.AsyncDisposeStart:
                    {
                        var value = values[--sp];
                        var type = (int)values[--sp].NumberValue;
                        var capability = ((DisposeCapabilityCell)values[sp - 1].Cell!).Capability;
                        values[sp - 1] = new JsValue(new AsyncDisposeOperationCell(new AsyncDisposeResourcesOperation(frame.Realm, capability, type == 1 ? value : (JsValue?)null)));
                        break;
                    }

                case OpCode.AsyncDisposeNext:
                    {
                        var target = code[pc++];
                        var operation = ((AsyncDisposeOperationCell)values[sp - 1].Cell!).Operation;
                        frame.Sp = sp;
                        if (operation.MoveNext(out var value))
                        {
                            values[sp - 1] = value;
                        }
                        else
                        {
                            sp--;
                            pc = target;
                        }

                        break;
                    }

                case OpCode.AsyncDisposeSetResult:
                    {
                        var thrown = values[--sp];
                        var operation = ((AsyncDisposeOperationCell)values[--sp].Cell!).Operation;
                        operation.SetAwaitResult(thrown.IsHole ? (JsValue?)null : thrown);
                        break;
                    }

                case OpCode.AsyncDisposeFinish:
                    {
                        var operation = ((AsyncDisposeOperationCell)values[--sp].Cell!).Operation;
                        if (operation.Completion is { } thrown)
                            throw new JsException(thrown);

                        break;
                    }

                default:
                    throw new InvalidOperationException("Unknown opcode " + op);
            }
        }
    }
}
