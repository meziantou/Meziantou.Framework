using System.Runtime.CompilerServices;
using Meziantou.Framework.JavaScriptEngine.Compiler;

namespace Meziantou.Framework.JavaScriptEngine.Interpreter;

/// <summary>
/// The operations of the code compiled by <see cref="JitCompiler"/>: the instructions of the dispatch loop of
/// <see cref="Execute"/>, with the same semantics, on the slots of the frame given by reference (the compiled code knows
/// the depth of the stack at each instruction). They are small so that the .NET JIT inlines them in the compiled code.
/// The helpers that return false did nothing: the compiled code leaves the instruction to the interpreter (a call that
/// pushes a frame, an error the interpreter throws without a .NET exception...).
/// </summary>
internal static partial class JsInterpreter
{
    /// <summary>Gets whether the code of a frame entered by a call (or a backward jump) runs compiled: it counts the calls and compiles the hot functions.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool EnterJitCode(FunctionTemplate code, JsEngine engine)
    {
        if (!engine.IsCompilationEnabled)
            return false;

        if (code.JitCode is not null)
            return true;

        if (code.IsJitCompilationAttempted)
            return false;

        var count = code.HotCount + 1;
        code.HotCount = count;
        if (count < engine.CompilationThreshold)
            return false;

        return CompileJitCode(code);
    }

    /// <summary>Gets whether the code of a frame resumed by the interpreter (a return, a handler) runs compiled.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool HasJitCode(FunctionTemplate code, JsEngine engine) => code.JitCode is not null && engine.IsCompilationEnabled;

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static bool CompileJitCode(FunctionTemplate code) => code.CompileJitCode() is not null;

    /// <summary>Runs the compiled code of a frame from its position, until an instruction it leaves to the interpreter (frame.Pc and frame.Sp are set).</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void RunJitCode(Frame frame, JsEngine engine) => frame.Code.JitCode!(frame, engine);

    // ---- Constants and stack -----------------------------------------------------------------------------------------

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static void JitSwap(ref JsValue a, ref JsValue b) => (a, b) = (b, a);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static void JitRot3(ref JsValue a, ref JsValue b, ref JsValue c)
    {
        // a b c -> c a b
        var top = c;
        c = b;
        b = a;
        a = top;
    }

    // ---- Registers and environments ------------------------------------------------------------------------------------

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static void JitLoadChecked(Frame frame, ref JsValue destination, JsValue value, int name)
    {
        if (value.IsHole)
        {
            ThrowTdz(frame, name);
        }

        JsValue.Store(ref destination, value);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static void JitStoreChecked(Frame frame, ref JsValue slot, JsValue value, int name)
    {
        if (slot.IsHole)
        {
            ThrowTdz(frame, name);
        }

        JsValue.Store(ref slot, value);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static void JitIncrementLocal(ref JsValue local, int delta)
    {
        if (local.IsNumber)
        {
            JsValue.StoreNumber(ref local, local.NumberValue + delta);
        }
        else
        {
            local = Operators.Increment(JsOperations.ToNumeric(local), delta);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static void JitLoadContext(Frame frame, ref JsValue destination, int depth, int slot)
    {
        JsValue.Store(ref destination, GetEnvironment(frame.Environment, depth).Slots[slot]);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static void JitLoadContextChecked(Frame frame, ref JsValue destination, int depth, int slot, int name)
    {
        var value = GetEnvironment(frame.Environment, depth).Slots[slot];
        if (value.IsHole)
        {
            ThrowTdz(frame, name);
        }

        JsValue.Store(ref destination, value);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static void JitStoreContext(Frame frame, JsValue value, int depth, int slot)
    {
        JsValue.Store(ref GetEnvironment(frame.Environment, depth).Slots[slot], value);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static void JitStoreContextChecked(Frame frame, JsValue value, int depth, int slot, int name)
    {
        var slots = GetEnvironment(frame.Environment, depth).Slots;
        if (slots[slot].IsHole)
        {
            ThrowTdz(frame, name);
        }

        JsValue.Store(ref slots[slot], value);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static void JitLoadGlobal(Frame frame, ref JsValue destination, int name, int cache) => destination = LoadGlobal(frame, name, cache);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static void JitStoreGlobal(Frame frame, JsValue value, int name, int strict, int cache) => StoreGlobal(frame, name, cache, value, strict != 0);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static void JitPushScope(Frame frame, int scope)
    {
        frame.Environment = new DeclarativeEnvironment((ScopeInfo)frame.Code.Objects[scope], frame.Environment);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static void JitPopScope(Frame frame) => frame.Environment = frame.Environment.Outer!;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static void JitCopyScope(Frame frame)
    {
        var environment = (DeclarativeEnvironment)frame.Environment;
        if (environment.IsCaptured)
        {
            frame.Environment = environment.Copy();
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static void JitLoadArgument(Frame frame, JsValue[] values, ref JsValue destination, int index)
    {
        // The arguments are stored after the registers
        JsValue.Store(ref destination, (uint)index < (uint)frame.ArgumentCount ? values[frame.Code.RegisterCount + index] : default);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static void JitLoadArgumentsLength(Frame frame, ref JsValue destination) => JsValue.StoreNumber(ref destination, frame.ArgumentCount);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static void JitLoadArgumentsElement(Frame frame, JsValue[] values, ref JsValue slot, int mapped)
    {
        var key = slot;
        if (key.IsNumber && key.NumberValue is var number && (uint)number < (uint)frame.ArgumentCount && (uint)number == number)
        {
            slot = values[frame.Code.RegisterCount + (int)(uint)number];
        }
        else
        {
            JsValue.Store(ref slot, GetArgumentsProperty(frame, key, mapped: mapped != 0));
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static void JitLoadThis(Frame frame, ref JsValue destination)
    {
        var thisValue = frame.This;
        if (thisValue.IsHole)
        {
            ThrowThisNotInitialized();
        }

        destination = thisValue;
    }

    // ---- Operators -----------------------------------------------------------------------------------------------------

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static void JitAdd(ref JsValue left, JsValue right)
    {
        var leftValue = left;
        if (leftValue.IsNumber && right.IsNumber)
        {
            JsValue.StoreNumber(ref left, leftValue.NumberValue + right.NumberValue);
        }
        else
        {
            left = Operators.Add(leftValue, right);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static void JitSubtract(ref JsValue left, JsValue right)
    {
        var leftValue = left;
        if (leftValue.IsNumber && right.IsNumber)
        {
            JsValue.StoreNumber(ref left, leftValue.NumberValue - right.NumberValue);
        }
        else
        {
            left = Operators.Binary(OpCode.Subtract, leftValue, right);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static void JitMultiply(ref JsValue left, JsValue right)
    {
        var leftValue = left;
        if (leftValue.IsNumber && right.IsNumber)
        {
            JsValue.StoreNumber(ref left, leftValue.NumberValue * right.NumberValue);
        }
        else
        {
            left = Operators.Binary(OpCode.Multiply, leftValue, right);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static void JitDivide(ref JsValue left, JsValue right)
    {
        var leftValue = left;
        if (leftValue.IsNumber && right.IsNumber)
        {
            JsValue.StoreNumber(ref left, leftValue.NumberValue / right.NumberValue);
        }
        else
        {
            left = Operators.Binary(OpCode.Divide, leftValue, right);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static void JitModulo(ref JsValue left, JsValue right)
    {
        var leftValue = left;
        if (leftValue.IsNumber && right.IsNumber)
        {
            JsValue.StoreNumber(ref left, Operators.Modulo(leftValue.NumberValue, right.NumberValue));
        }
        else
        {
            left = Operators.Binary(OpCode.Modulo, leftValue, right);
        }
    }

    /// <summary>The shifts and the bitwise operators (the operator is a constant of the compiled code: the switch is removed when the helper is inlined).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static void JitBitwise(ref JsValue left, JsValue right, int op)
    {
        var leftValue = left;
        if (leftValue.IsNumber && right.IsNumber)
        {
            var a = ToInt32Fast(leftValue.NumberValue);
            var b = ToInt32Fast(right.NumberValue);
            double result = (OpCode)op switch
            {
                OpCode.LeftShift => a << (b & 0x1F),
                OpCode.RightShift => a >> (b & 0x1F),
                OpCode.UnsignedRightShift => (uint)a >> (b & 0x1F),
                OpCode.BitwiseAnd => a & b,
                OpCode.BitwiseOr => a | b,
                _ => a ^ b,
            };
            JsValue.StoreNumber(ref left, result);
        }
        else
        {
            left = Operators.Binary((OpCode)op, leftValue, right);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int ToInt32Fast(double value) => value is >= int.MinValue and <= int.MaxValue ? (int)value : JsOperations.DoubleToInt32(value);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static void JitBitwiseNot(ref JsValue slot)
    {
        var value = slot;
        slot = value.IsNumber ? new JsValue(~ToInt32Fast(value.NumberValue)) : Operators.BitwiseNot(value);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static void JitToNumber(ref JsValue slot)
    {
        var value = slot;
        if (!value.IsNumber)
        {
            slot = new JsValue(JsOperations.ToNumber(value));
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static void JitEqual(ref JsValue left, JsValue right, int negate)
    {
        var leftValue = left;
        var result = leftValue.IsNumber && right.IsNumber ? leftValue.NumberValue == right.NumberValue : JsOperations.IsLooselyEqual(leftValue, right);
        left = result != (negate != 0) ? JsValue.True : JsValue.False;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static void JitStrictEqual(ref JsValue left, JsValue right, int negate)
    {
        var leftValue = left;
        var result = leftValue.IsNumber ? right.IsNumber && leftValue.NumberValue == right.NumberValue : JsOperations.IsStrictlyEqual(leftValue, right);
        left = result != (negate != 0) ? JsValue.True : JsValue.False;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static void JitLessThan(ref JsValue left, JsValue right)
    {
        var leftValue = left;
        left = (leftValue.IsNumber && right.IsNumber ? leftValue.NumberValue < right.NumberValue : Operators.LessThan(leftValue, right, leftFirst: true) is true) ? JsValue.True : JsValue.False;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static void JitGreaterThan(ref JsValue left, JsValue right)
    {
        var leftValue = left;
        left = (leftValue.IsNumber && right.IsNumber ? leftValue.NumberValue > right.NumberValue : Operators.LessThan(right, leftValue, leftFirst: false) is true) ? JsValue.True : JsValue.False;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static void JitLessThanOrEqual(ref JsValue left, JsValue right)
    {
        var leftValue = left;
        left = (leftValue.IsNumber && right.IsNumber ? leftValue.NumberValue <= right.NumberValue : Operators.LessThan(right, leftValue, leftFirst: false) is false) ? JsValue.True : JsValue.False;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static void JitGreaterThanOrEqual(ref JsValue left, JsValue right)
    {
        var leftValue = left;
        left = (leftValue.IsNumber && right.IsNumber ? leftValue.NumberValue >= right.NumberValue : Operators.LessThan(leftValue, right, leftFirst: true) is false) ? JsValue.True : JsValue.False;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static void JitNegate(ref JsValue slot)
    {
        var value = slot;
        slot = value.IsNumber ? new JsValue(-value.NumberValue) : Operators.Negate(value);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static void JitToNumeric(ref JsValue slot)
    {
        if (!slot.IsNumber)
        {
            slot = JsOperations.ToNumeric(slot);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static void JitLogicalNot(ref JsValue slot) => slot = ToBoolean(slot) ? JsValue.False : JsValue.True;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static void JitTypeOf(ref JsValue slot) => slot = JsValue.FromJsString(Operators.TypeOf(slot));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static void JitIncrement(ref JsValue slot, int delta)
    {
        var value = slot;
        slot = value.IsNumber ? new JsValue(value.NumberValue + delta) : Operators.Increment(value, delta);
    }

    // ---- Control flow --------------------------------------------------------------------------------------------------

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static bool JitToBoolean(JsValue value) => ToBoolean(value);

    /// <summary>The condition of a comparison followed by a conditional jump (<see cref="OpCode.JumpIfLessThan"/>...): whether the jump is taken.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static bool JitCompareJump(JsValue left, JsValue right, int op)
    {
        var opCode = (OpCode)op;
        if (opCode is OpCode.JumpIfStrictEqual or OpCode.JumpIfStrictNotEqual)
        {
            var equal = left.IsNumber ? right.IsNumber && left.NumberValue == right.NumberValue : JsOperations.IsStrictlyEqual(left, right);
            return equal == (opCode == OpCode.JumpIfStrictEqual);
        }

        if (left.IsNumber && right.IsNumber)
        {
            var l = left.NumberValue;
            var r = right.NumberValue;
            return opCode switch
            {
                OpCode.JumpIfLessThan => l < r,
                OpCode.JumpIfNotLessThan => !(l < r),
                OpCode.JumpIfLessThanOrEqual => l <= r,
                OpCode.JumpIfNotLessThanOrEqual => !(l <= r),
                OpCode.JumpIfGreaterThan => l > r,
                OpCode.JumpIfNotGreaterThan => !(l > r),
                OpCode.JumpIfGreaterThanOrEqual => l >= r,
                _ => !(l >= r),
            };
        }

        return CompareForJump(opCode, left, right);
    }

    // ---- Objects and properties ----------------------------------------------------------------------------------------

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static void JitNewObject(Frame frame, ref JsValue destination) => destination = new JsObject(frame.Realm.Intrinsics.ObjectPrototype);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static void JitNewArray(Frame frame, ref JsValue destination) => destination = new JsArray(frame.Realm.Intrinsics.ArrayPrototype);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static void JitArrayAppend(JsValue array, JsValue value) => ArrayAppend(array, value);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static void JitNewArgumentList(ref JsValue destination) => destination = new JsValue(new ArgumentList());

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static void JitArgumentListAppend(JsValue list, JsValue value) => ((ArgumentList)list.Cell!).Add(value);

    /// <summary>object -> value; false when the object is null or undefined (the interpreter throws the TypeError).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static bool JitGetProperty(Frame frame, ref JsValue slot, int name, int cache)
    {
        var target = slot;
        if (target.IsNullOrUndefined)
            return false;

        JsValue.Store(ref slot, GetProperty(frame, name, cache, target));
        return true;
    }

    /// <summary>object -> function object; false when the object is null or undefined.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static bool JitGetMethod(Frame frame, ref JsValue slot, ref JsValue next, int name, int cache)
    {
        var target = slot;
        if (target.IsNullOrUndefined)
            return false;

        slot = GetProperty(frame, name, cache, target);
        next = target;
        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static void JitSetProperty(Frame frame, ref JsValue target, JsValue value, int name, int cache)
    {
        SetProperty(frame, name, cache, target, value);
        target = value;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static void JitGetElement(Frame frame, ref JsValue target, JsValue key) => JsValue.Store(ref target, GetElement(frame.Realm, target, key));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static void JitSetElement(Frame frame, ref JsValue target, JsValue key, JsValue value)
    {
        SetElement(frame, target, key, value);
        target = value;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static void JitDefineField(Frame frame, JsValue target, JsValue value, int name, int cache) => DefineField(frame, name, cache, target, value);

    // ---- Calls ---------------------------------------------------------------------------------------------------------

    /// <summary>
    /// (argc) function thisValue arg1..N -> result, for a call that does not push a frame; false when the interpreter does
    /// the call (a closure whose frame it pushes, or a value that is not callable: the interpreter throws the TypeError).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static bool JitCall(Frame frame, JsValue[] values, int functionIndex, int argumentCount)
    {
        var function = values[functionIndex];
        if (function.Cell is JsClosure closure ? CanRunInline(closure) : !function.IsCallable)
            return false;

        frame.Sp = functionIndex;
        values[functionIndex] = CallValue(frame, function, values[functionIndex + 1], values.AsSpan(functionIndex + 2, argumentCount));
        return true;
    }

    /// <summary>function thisValue argumentList -> result, for a call that does not push a frame; false when the interpreter does the call.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static bool JitCallSpread(Frame frame, JsValue[] values, int functionIndex)
    {
        var function = values[functionIndex];
        if (function.Cell is JsClosure closure && CanRunInline(closure))
            return false;

        var arguments = GetSpreadArguments(values[functionIndex + 2]).Values;
        frame.Sp = functionIndex;
        values[functionIndex] = CallValue(frame, function, values[functionIndex + 1], arguments);
        return true;
    }

    /// <summary>(argc) constructor arg1..N -> object (or constructor argumentList -> object), for a construct that does not push a frame; false when the interpreter does it.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static bool JitNew(Frame frame, JsValue[] values, int constructorIndex, int argumentCount, int spread)
    {
        if (values[constructorIndex].Cell is JsClosure closure && CanConstructInline(closure))
            return false;

        frame.Sp = constructorIndex;
        var arguments = spread != 0 ? GetSpreadArguments(values[constructorIndex + 1]).Values : values.AsSpan(constructorIndex + 1, argumentCount);
        var constructor = GetConstructor(frame, values[constructorIndex]);
        values[constructorIndex] = constructor.Construct(arguments, constructor);
        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static void JitCreateClosure(Frame frame, ref JsValue destination, int template) => destination = CreateClosure(frame, (FunctionTemplate)frame.Code.Objects[template]);

    /// <summary>An instruction of <see cref="ExecuteCold"/>: frame.Pc is the position of the instruction; returns the new stack pointer and sets the position of the next instruction.</summary>
    internal static int JitExecuteCold(Frame frame, int op, int sp) => ExecuteCold(frame, (OpCode)op, sp);
}
