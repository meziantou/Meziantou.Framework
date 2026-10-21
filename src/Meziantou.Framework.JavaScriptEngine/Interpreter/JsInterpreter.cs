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
                    var result = Execute(ref frame, entry, engine);
                    if (entry.IsConstruct && entry.State == FrameState.Completed)
                    {
                        // The checks of the result of [[Construct]] are done once the function has been left: no handler
                        // of the function can catch them, and the errors are created in the realm of the caller
                        result = GetConstructResult(entry, result, previousRealm ?? entry.Realm);
                    }

                    return result;
                }
                catch (JsException ex)
                {
                    if (!HandleException(ref frame, entry, engine, ex.Value, isFaultingFrame: true))
                        throw;
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

    /// <summary>
    /// The dispatch loop. Only the frequent and simple instructions are implemented here; the others are in separate
    /// methods (<see cref="ExecuteCold"/>). The loop must stay small: the JIT tracks a limited number of locals per method,
    /// and a big method spills <c>pc</c>/<c>sp</c> to the stack, zeroes a large frame in its prologue and stops inlining
    /// the small helpers. The slow paths are calls that take and return values (no <c>ref</c> to the locals of the loop,
    /// which would make them address-exposed).
    /// </summary>
    private static JsValue Execute(ref Frame current, Frame entry, JsEngine engine)
    {
        var frame = current;
        var code = frame.Code.Code;
        var values = frame.Values;
        var pc = frame.Pc;
        var sp = frame.Sp;

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
                    values[sp++] = frame.Code.Constants[code[pc++]];
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

                // ---- Registers and environments -------------------------------------------------------------------
                case OpCode.LoadLocal:
                    values[sp++] = values[code[pc++]];
                    break;
                case OpCode.LoadLocalChecked:
                    {
                        var value = values[code[pc]];
                        if (value.IsHole)
                        {
                            ThrowTdz(frame, code[pc + 1]);
                        }

                        values[sp++] = value;
                        pc += 2;
                        break;
                    }

                case OpCode.StoreLocal:
                    values[code[pc++]] = values[--sp];
                    break;
                case OpCode.StoreLocalChecked:
                    {
                        var register = code[pc];
                        if (values[register].IsHole)
                        {
                            ThrowTdz(frame, code[pc + 1]);
                        }

                        values[register] = values[--sp];
                        pc += 2;
                        break;
                    }

                case OpCode.LoadContext:
                    values[sp++] = GetEnvironment(frame.Environment, code[pc]).Slots[code[pc + 1]];
                    pc += 2;
                    break;
                case OpCode.LoadContextChecked:
                    {
                        var value = GetEnvironment(frame.Environment, code[pc]).Slots[code[pc + 1]];
                        if (value.IsHole)
                        {
                            ThrowTdz(frame, code[pc + 2]);
                        }

                        values[sp++] = value;
                        pc += 3;
                        break;
                    }

                case OpCode.StoreContext:
                    GetEnvironment(frame.Environment, code[pc]).Slots[code[pc + 1]] = values[--sp];
                    pc += 2;
                    break;
                case OpCode.StoreContextChecked:
                    {
                        var slots = GetEnvironment(frame.Environment, code[pc]).Slots;
                        var slot = code[pc + 1];
                        if (slots[slot].IsHole)
                        {
                            ThrowTdz(frame, code[pc + 2]);
                        }

                        slots[slot] = values[--sp];
                        pc += 3;
                        break;
                    }

                case OpCode.LoadGlobal:
                    values[sp++] = LoadGlobal(frame, code[pc++]);
                    break;
                case OpCode.PushScope:
                    frame.Environment = new DeclarativeEnvironment((ScopeInfo)frame.Code.Objects[code[pc++]], frame.Environment);
                    break;
                case OpCode.PopScope:
                    frame.Environment = frame.Environment.Outer!;
                    break;
                case OpCode.CopyScope:
                    frame.Environment = ((DeclarativeEnvironment)frame.Environment).Copy();
                    break;
                case OpCode.LoadArgument:
                    {
                        var index = code[pc++];
                        var arguments = frame.Arguments;
                        values[sp++] = (uint)index < (uint)arguments.Length ? arguments[index] : default;
                        break;
                    }

                case OpCode.LoadThis:
                    {
                        var thisValue = frame.This;
                        if (thisValue.IsHole)
                        {
                            ThrowThisNotInitialized();
                        }

                        values[sp++] = thisValue;
                        break;
                    }

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
                    {
                        var left = values[sp - 2];
                        var right = values[sp - 1];
                        sp--;
                        values[sp - 1] = left.IsNumber && right.IsNumber ? new JsValue(left.NumberValue - right.NumberValue) : Operators.Binary(op, left, right);
                        break;
                    }

                case OpCode.Multiply:
                    {
                        var left = values[sp - 2];
                        var right = values[sp - 1];
                        sp--;
                        values[sp - 1] = left.IsNumber && right.IsNumber ? new JsValue(left.NumberValue * right.NumberValue) : Operators.Binary(op, left, right);
                        break;
                    }

                case OpCode.Divide:
                    {
                        var left = values[sp - 2];
                        var right = values[sp - 1];
                        sp--;
                        values[sp - 1] = left.IsNumber && right.IsNumber ? new JsValue(left.NumberValue / right.NumberValue) : Operators.Binary(op, left, right);
                        break;
                    }

                case OpCode.Modulo:
                    {
                        var left = values[sp - 2];
                        var right = values[sp - 1];
                        sp--;
                        values[sp - 1] = left.IsNumber && right.IsNumber ? new JsValue(left.NumberValue % right.NumberValue) : Operators.Binary(op, left, right);
                        break;
                    }

                case OpCode.Equal:
                    {
                        var left = values[sp - 2];
                        var right = values[sp - 1];
                        sp--;
                        values[sp - 1] = (left.IsNumber && right.IsNumber ? left.NumberValue == right.NumberValue : JsOperations.IsLooselyEqual(left, right)) ? JsValue.True : JsValue.False;
                        break;
                    }

                case OpCode.NotEqual:
                    {
                        var left = values[sp - 2];
                        var right = values[sp - 1];
                        sp--;
                        values[sp - 1] = (left.IsNumber && right.IsNumber ? left.NumberValue == right.NumberValue : JsOperations.IsLooselyEqual(left, right)) ? JsValue.False : JsValue.True;
                        break;
                    }

                case OpCode.StrictEqual:
                    {
                        var left = values[sp - 2];
                        var right = values[sp - 1];
                        sp--;
                        values[sp - 1] = (left.IsNumber ? right.IsNumber && left.NumberValue == right.NumberValue : JsOperations.IsStrictlyEqual(left, right)) ? JsValue.True : JsValue.False;
                        break;
                    }

                case OpCode.StrictNotEqual:
                    {
                        var left = values[sp - 2];
                        var right = values[sp - 1];
                        sp--;
                        values[sp - 1] = (left.IsNumber ? right.IsNumber && left.NumberValue == right.NumberValue : JsOperations.IsStrictlyEqual(left, right)) ? JsValue.False : JsValue.True;
                        break;
                    }

                case OpCode.LessThan:
                    {
                        var left = values[sp - 2];
                        var right = values[sp - 1];
                        sp--;
                        values[sp - 1] = (left.IsNumber && right.IsNumber ? left.NumberValue < right.NumberValue : Operators.LessThan(left, right, leftFirst: true) is true) ? JsValue.True : JsValue.False;
                        break;
                    }

                case OpCode.GreaterThan:
                    {
                        var left = values[sp - 2];
                        var right = values[sp - 1];
                        sp--;
                        values[sp - 1] = (left.IsNumber && right.IsNumber ? left.NumberValue > right.NumberValue : Operators.LessThan(right, left, leftFirst: false) is true) ? JsValue.True : JsValue.False;
                        break;
                    }

                case OpCode.LessThanOrEqual:
                    {
                        var left = values[sp - 2];
                        var right = values[sp - 1];
                        sp--;
                        values[sp - 1] = (left.IsNumber && right.IsNumber ? left.NumberValue <= right.NumberValue : Operators.LessThan(right, left, leftFirst: false) is false) ? JsValue.True : JsValue.False;
                        break;
                    }

                case OpCode.GreaterThanOrEqual:
                    {
                        var left = values[sp - 2];
                        var right = values[sp - 1];
                        sp--;
                        values[sp - 1] = (left.IsNumber && right.IsNumber ? left.NumberValue >= right.NumberValue : Operators.LessThan(left, right, leftFirst: true) is false) ? JsValue.True : JsValue.False;
                        break;
                    }

                case OpCode.Negate:
                    {
                        var value = values[sp - 1];
                        values[sp - 1] = value.IsNumber ? new JsValue(-value.NumberValue) : Operators.Negate(value);
                        break;
                    }

                case OpCode.ToNumeric:
                    if (!values[sp - 1].IsNumber)
                    {
                        values[sp - 1] = JsOperations.ToNumeric(values[sp - 1]);
                    }

                    break;
                case OpCode.LogicalNot:
                    values[sp - 1] = ToBoolean(values[sp - 1]) ? JsValue.False : JsValue.True;
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

                // ---- Control flow ---------------------------------------------------------------------------------
                case OpCode.Jump:
                    {
                        var target = code[pc];
                        if (target < pc)
                        {
                            engine.CheckInterrupt();
                        }

                        pc = target;
                        break;
                    }

                case OpCode.JumpIfTrue:
                    if (ToBoolean(values[--sp]))
                    {
                        pc = code[pc];
                    }
                    else
                    {
                        pc++;
                    }

                    break;
                case OpCode.JumpIfFalse:
                    if (ToBoolean(values[--sp]))
                    {
                        pc++;
                    }
                    else
                    {
                        pc = code[pc];
                    }

                    break;
                case OpCode.JumpIfTrueKeep:
                    if (ToBoolean(values[sp - 1]))
                    {
                        pc = code[pc];
                    }
                    else
                    {
                        pc++;
                    }

                    break;
                case OpCode.JumpIfFalseKeep:
                    if (ToBoolean(values[sp - 1]))
                    {
                        pc++;
                    }
                    else
                    {
                        pc = code[pc];
                    }

                    break;
                case OpCode.Throw:
                    {
                        var exception = values[--sp];
                        frame.Sp = sp;
                        var location = GetLocation(frame.Code, pc);
                        if (!HandleException(ref current, entry, engine, exception, isFaultingFrame: true))
                            throw new JsException(exception, location);

                        frame = current;
                        code = frame.Code.Code;
                        values = frame.Values;
                        pc = frame.Pc;
                        sp = frame.Sp;
                        break;
                    }

                case OpCode.Return:
                    {
                        var result = values[--sp];
                        if (frame.IsConstruct && !ReferenceEquals(frame, entry))
                        {
                            result = GetConstructResult(frame, result, frame.Parent!.Realm);
                        }

                        frame.State = FrameState.Completed;
                        if (ReferenceEquals(frame, entry))
                            return result;

                        engine.PopFrame();
                        engine.CallDepth--;
                        frame = frame.Parent!;
                        current = frame;
                        engine.CurrentRealm = frame.Realm;
                        code = frame.Code.Code;
                        values = frame.Values;
                        pc = frame.Pc;
                        sp = frame.Sp;
                        values[sp++] = result;
                        break;
                    }

                // ---- Objects and properties -----------------------------------------------------------------------
                case OpCode.NewObject:
                    values[sp++] = new JsObject(frame.Realm.Intrinsics.ObjectPrototype);
                    break;
                case OpCode.NewArray:
                    values[sp++] = new JsArray(frame.Realm.Intrinsics.ArrayPrototype);
                    break;
                case OpCode.ArrayAppend:
                    sp--;
                    ArrayAppend(values[sp - 1], values[sp]);
                    break;
                case OpCode.GetProperty:
                    values[sp - 1] = GetProperty(frame, code[pc++], values[sp - 1]);
                    break;
                case OpCode.SetProperty:
                    sp--;
                    SetProperty(frame, code[pc++], values[sp - 1], values[sp]);
                    values[sp - 1] = values[sp];
                    break;
                case OpCode.GetElement:
                    sp--;
                    values[sp - 1] = GetElement(frame.Realm, values[sp - 1], values[sp]);
                    break;
                case OpCode.SetElement:
                    sp -= 2;
                    SetElement(frame, values[sp - 1], values[sp], values[sp + 1]);
                    values[sp - 1] = values[sp + 1];
                    break;
                case OpCode.DefineField:
                    sp--;
                    DefineField(frame, code[pc++], values[sp - 1], values[sp]);
                    break;

                // ---- Calls ----------------------------------------------------------------------------------------
                case OpCode.Call:
                    {
                        var argumentCount = code[pc++];
                        var functionIndex = sp - argumentCount - 2;
                        sp = functionIndex;
                        if (values[functionIndex].Cell is JsClosure closure && CanRunInline(closure))
                        {
                            frame.Pc = pc;
                            frame.Sp = sp;
                            frame = PushInlineFrame(frame, CreateCallFrame(closure, values[functionIndex + 1], values.AsSpan(functionIndex + 2, argumentCount), newTarget: null), engine);
                            current = frame;
                            code = frame.Code.Code;
                            values = frame.Values;
                            pc = frame.Pc;
                            sp = frame.Sp;
                            break;
                        }

                        frame.Sp = sp;
                        values[sp] = CallValue(frame, values[functionIndex], values[functionIndex + 1], values.AsSpan(functionIndex + 2, argumentCount));
                        sp++;
                        break;
                    }

                case OpCode.CallSpread:
                    {
                        var arguments = GetSpreadArguments(values[--sp]);
                        sp -= 2;
                        if (values[sp].Cell is JsClosure closure && CanRunInline(closure))
                        {
                            frame.Pc = pc;
                            frame.Sp = sp;
                            frame = PushInlineFrame(frame, CreateCallFrame(closure, values[sp + 1], arguments, newTarget: null), engine);
                            current = frame;
                            code = frame.Code.Code;
                            values = frame.Values;
                            pc = frame.Pc;
                            sp = frame.Sp;
                            break;
                        }

                        frame.Sp = sp;
                        values[sp] = CallValue(frame, values[sp], values[sp + 1], arguments);
                        sp++;
                        break;
                    }

                case OpCode.New:
                case OpCode.NewSpread:
                    {
                        int constructorIndex;
                        int argumentCount;
                        JsValue[]? spreadArguments = null;
                        if (op == OpCode.New)
                        {
                            argumentCount = code[pc++];
                            constructorIndex = sp - argumentCount - 1;
                        }
                        else
                        {
                            spreadArguments = GetSpreadArguments(values[--sp]);
                            constructorIndex = sp - 1;
                            argumentCount = spreadArguments.Length;
                        }

                        sp = constructorIndex;
                        frame.Sp = sp;
                        var arguments = spreadArguments ?? values.AsSpan(constructorIndex + 1, argumentCount);
                        var constructor = GetConstructor(frame, values[constructorIndex]);
                        if (constructor is JsClosure closure && CanConstructInline(closure))
                        {
                            frame.Pc = pc;
                            frame = PushInlineFrame(frame, CreateConstructFrame(closure, arguments, closure), engine);
                            current = frame;
                            code = frame.Code.Code;
                            values = frame.Values;
                            pc = frame.Pc;
                            sp = frame.Sp;
                            break;
                        }

                        values[sp] = constructor.Construct(arguments, constructor);
                        sp++;
                        break;
                    }

                // ---- Functions ------------------------------------------------------------------------------------
                case OpCode.CreateClosure:
                    values[sp++] = CreateClosure(frame, (FunctionTemplate)frame.Code.Objects[code[pc++]]);
                    break;

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

                default:
                    // frame.Pc is the position of the instruction: the method reads the operands and sets the position of the next instruction
                    sp = ExecuteCold(frame, op, sp);
                    pc = frame.Pc;
                    break;
            }
        }
    }

    /// <summary>ToBoolean with the fast paths of the booleans (the result of the comparisons) inlined in the loop.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool ToBoolean(JsValue value)
    {
        var cell = value.Cell;
        if (ReferenceEquals(cell, BooleanCell.True))
            return true;

        if (ReferenceEquals(cell, BooleanCell.False))
            return false;

        return JsOperations.ToBoolean(value);
    }

    /// <summary>The instructions that are not in the dispatch loop: frame.Pc is the position of the instruction, the result is the new stack pointer.</summary>
    private static int ExecuteCold(Frame frame, OpCode op, int sp)
    {
        return op switch
        {
            OpCode.Rot3Reverse => ExecuteRot3Reverse(frame, sp),
            OpCode.Rot4 => ExecuteRot4(frame, sp),
            OpCode.Dup3 => ExecuteDup3(frame, sp),
            OpCode.Rot5 => ExecuteRot5(frame, sp),
            OpCode.LoadImport => ExecuteLoadImport(frame, sp),
            OpCode.ThrowConstAssignLocal => ExecuteThrowConstAssignLocal(frame),
            OpCode.ThrowConstAssignContext => ExecuteThrowConstAssignContext(frame),
            OpCode.LoadGlobalOrUndefined => ExecuteLoadGlobalOrUndefined(frame, sp),
            OpCode.StoreGlobal => ExecuteStoreGlobal(frame, sp),
            OpCode.LoadName => ExecuteLoadName(frame, sp),
            OpCode.LoadNameOrUndefined => ExecuteLoadNameOrUndefined(frame, sp),
            OpCode.LoadNameForCall => ExecuteLoadNameForCall(frame, sp),
            OpCode.StoreName => ExecuteStoreName(frame, sp),
            OpCode.DeleteName => ExecuteDeleteName(frame, sp),
            OpCode.InitializeName => ExecuteInitializeName(frame, sp),
            OpCode.PushWith => ExecutePushWith(frame, sp),
            OpCode.SaveEnvironment => ExecuteSaveEnvironment(frame, sp),
            OpCode.RestoreEnvironment => ExecuteRestoreEnvironment(frame, sp),
            OpCode.LoadRestArguments => ExecuteLoadRestArguments(frame, sp),
            OpCode.LoadAllArguments => ExecuteLoadAllArguments(frame, sp),
            OpCode.CreateArguments => ExecuteCreateArguments(frame, sp),
            OpCode.LoadFunction => ExecuteLoadFunction(frame, sp),
            OpCode.LoadNewTarget => ExecuteLoadNewTarget(frame, sp),
            OpCode.LoadHomeObject => ExecuteLoadHomeObject(frame, sp),
            OpCode.LoadGlobalThis => ExecuteLoadGlobalThis(frame, sp),
            OpCode.LoadImportMeta => ExecuteLoadImportMeta(frame, sp),
            OpCode.BindThis => ExecuteBindThis(frame, sp),
            OpCode.CheckThisUninitialized => ExecuteCheckThisUninitialized(frame, sp),
            OpCode.Divide => ExecuteBinary(frame, sp, op),
            OpCode.Exponentiate => ExecuteBinary(frame, sp, op),
            OpCode.LeftShift => ExecuteBinary(frame, sp, op),
            OpCode.RightShift => ExecuteBinary(frame, sp, op),
            OpCode.UnsignedRightShift => ExecuteBinary(frame, sp, op),
            OpCode.BitwiseAnd => ExecuteBinary(frame, sp, op),
            OpCode.BitwiseOr => ExecuteBinary(frame, sp, op),
            OpCode.BitwiseXor => ExecuteBinary(frame, sp, op),
            OpCode.InstanceOf => ExecuteInstanceOf(frame, sp),
            OpCode.In => ExecuteIn(frame, sp),
            OpCode.PrivateIn => ExecutePrivateIn(frame, sp),
            OpCode.ToNumber => ExecuteToNumber(frame, sp),
            OpCode.BitwiseNot => ExecuteBitwiseNot(frame, sp),
            OpCode.ToStringValue => ExecuteToStringValue(frame, sp),
            OpCode.ToPropertyKey => ExecuteToPropertyKey(frame, sp),
            OpCode.ToObject => ExecuteToObject(frame, sp),
            OpCode.RequireObjectCoercible => ExecuteRequireObjectCoercible(frame, sp),
            OpCode.RequireObjectCoercibleBase => ExecuteRequireObjectCoercibleBase(frame, sp),
            OpCode.ResolveName => ExecuteResolveName(frame, sp),
            OpCode.LoadResolved => ExecuteLoadResolved(frame, sp),
            OpCode.StoreResolved => ExecuteStoreResolved(frame, sp),
            OpCode.JumpIfNullishKeep => ExecuteJumpIfNullishKeep(frame, sp),
            OpCode.JumpIfNotNullishKeep => ExecuteJumpIfNotNullishKeep(frame, sp),
            OpCode.JumpIfNotUndefinedKeep => ExecuteJumpIfNotUndefinedKeep(frame, sp),
            OpCode.JumpIfUndefinedPop => ExecuteJumpIfUndefinedPop(frame, sp),
            OpCode.ThrowError => ExecuteThrowError(frame),
            OpCode.Debugger => ExecuteDebugger(frame, sp),
            OpCode.ArrayAppendSpread => ExecuteArrayAppendSpread(frame, sp),
            OpCode.DeleteProperty => ExecuteDeleteProperty(frame, sp),
            OpCode.DeleteElement => ExecuteDeleteElement(frame, sp),
            OpCode.DefineFieldComputed => ExecuteDefineFieldComputed(frame, sp),
            OpCode.DefineMethod => ExecuteDefineMethod(frame, sp),
            OpCode.SetPrototypeLiteral => ExecuteSetPrototypeLiteral(frame, sp),
            OpCode.CopyDataProperties => ExecuteCopyDataProperties(frame, sp),
            OpCode.CopyDataPropertiesExcluding => ExecuteCopyDataPropertiesExcluding(frame, sp),
            OpCode.LoadSuperBase => ExecuteLoadSuperBase(frame, sp),
            OpCode.GetSuperProperty => ExecuteGetSuperProperty(frame, sp),
            OpCode.GetSuperElement => ExecuteGetSuperElement(frame, sp),
            OpCode.SetSuperProperty => ExecuteSetSuperProperty(frame, sp),
            OpCode.SetSuperElement => ExecuteSetSuperElement(frame, sp),
            OpCode.GetPrivate => ExecuteGetPrivate(frame, sp),
            OpCode.SetPrivate => ExecuteSetPrivate(frame, sp),
            OpCode.CallEval => ExecuteCallEval(frame, sp, op),
            OpCode.CallEvalSpread => ExecuteCallEval(frame, sp, op),
            OpCode.SuperCall => ExecuteSuperCall(frame, sp, op),
            OpCode.SuperCallSpread => ExecuteSuperCall(frame, sp, op),
            OpCode.ImportCall => ExecuteImportCall(frame, sp),
            OpCode.CreateClass => ExecuteCreateClass(frame, sp),
            OpCode.InitializeInstanceElements => ExecuteInitializeInstanceElements(frame, sp),
            OpCode.CreatePrivateName => ExecuteCreatePrivateName(frame, sp),
            OpCode.DefinePrivateMethod => ExecuteDefinePrivateMethod(frame, sp),
            OpCode.SetFieldInitializer => ExecuteSetFieldInitializer(frame, sp),
            OpCode.CallClassStaticInitializer => ExecuteCallClassStaticInitializer(frame, sp),
            OpCode.SetHomeObject => ExecuteSetHomeObject(frame, sp),
            OpCode.DefineClassField => ExecuteDefineClassField(frame, sp),
            OpCode.DefineClassFieldComputed => ExecuteDefineClassFieldComputed(frame, sp),
            OpCode.DefinePrivateField => ExecuteDefinePrivateField(frame, sp),
            OpCode.SetFunctionName => ExecuteSetFunctionName(frame, sp),
            OpCode.SetFunctionNameComputed => ExecuteSetFunctionNameComputed(frame, sp),
            OpCode.GetTemplateObject => ExecuteGetTemplateObject(frame, sp),
            OpCode.CreateRegExp => ExecuteCreateRegExp(frame, sp),
            OpCode.GetIterator => ExecuteGetIterator(frame, sp),
            OpCode.GetAsyncIterator => ExecuteGetAsyncIterator(frame, sp),
            OpCode.IteratorNext => ExecuteIteratorNext(frame, sp),
            OpCode.IteratorNextOrUndefined => ExecuteIteratorNextOrUndefined(frame, sp),
            OpCode.IteratorRest => ExecuteIteratorRest(frame, sp),
            OpCode.IteratorCallNext => ExecuteIteratorCallNext(frame, sp),
            OpCode.IteratorStepFinish => ExecuteIteratorStepFinish(frame, sp),
            OpCode.IteratorClose => ExecuteIteratorClose(frame, sp),
            OpCode.IteratorCloseOnThrow => ExecuteIteratorCloseOnThrow(frame, sp),
            OpCode.IteratorCallReturn => ExecuteIteratorCallReturn(frame, sp),
            OpCode.ThrowIfNotObject => ExecuteThrowIfNotObject(frame, sp),
            OpCode.IteratorIsDone => ExecuteIteratorIsDone(frame, sp),
            OpCode.IteratorDelegateStep => ExecuteIteratorDelegateStep(frame, sp),
            OpCode.IteratorComplete => ExecuteIteratorComplete(frame, sp),
            OpCode.IteratorValue => ExecuteIteratorValue(frame, sp),
            OpCode.ForInPrepare => ExecuteForInPrepare(frame, sp),
            OpCode.ForInNext => ExecuteForInNext(frame, sp),
            OpCode.CreateIterResult => ExecuteCreateIterResult(frame, sp),
            OpCode.CreateIterResultDone => ExecuteCreateIterResultDone(frame, sp),
            OpCode.GlobalDeclarationInstantiation => ExecuteGlobalDeclarationInstantiation(frame, sp),
            OpCode.EvalDeclarationInstantiation => ExecuteEvalDeclarationInstantiation(frame, sp),
            OpCode.InitializeGlobalLexical => ExecuteInitializeGlobalLexical(frame, sp),
            OpCode.InitializeGlobalFunction => ExecuteInitializeGlobalFunction(frame, sp),
            OpCode.SetVarBinding => ExecuteSetVarBinding(frame, sp),
            OpCode.SetGlobalVar => ExecuteSetGlobalVar(frame, sp),
            OpCode.AddDisposableResource => ExecuteAddDisposableResource(frame, sp),
            OpCode.NewDisposeCapability => ExecuteNewDisposeCapability(frame, sp),
            OpCode.DisposeResources => ExecuteDisposeResources(frame, sp),
            OpCode.AsyncDisposeStart => ExecuteAsyncDisposeStart(frame, sp),
            OpCode.AsyncDisposeNext => ExecuteAsyncDisposeNext(frame, sp),
            OpCode.AsyncDisposeSetResult => ExecuteAsyncDisposeSetResult(frame, sp),
            OpCode.AsyncDisposeFinish => ExecuteAsyncDisposeFinish(frame, sp),
            _ => throw new InvalidOperationException("Unknown opcode " + op),
        };
    }
}
