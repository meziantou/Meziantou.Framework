using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.CompilerServices;
using Meziantou.Framework.JavaScriptEngine.Compiler;
using DynamicMethod = System.Reflection.Emit.DynamicMethod;
using EmitOpCodes = System.Reflection.Emit.OpCodes;
using ILGenerator = System.Reflection.Emit.ILGenerator;
using ILLabel = System.Reflection.Emit.Label;
using LocalBuilder = System.Reflection.Emit.LocalBuilder;

namespace Meziantou.Framework.JavaScriptEngine.Interpreter;

/// <summary>
/// The compiled code of a function (<see cref="JitCompiler"/>): it runs the bytecode of the frame from <see cref="Frame.Pc"/>
/// until an instruction it leaves to the interpreter, and sets <see cref="Frame.Pc"/> (that instruction) and
/// <see cref="Frame.Sp"/>.
/// </summary>
internal delegate void JitCode(Frame frame, JsEngine engine);

/// <summary>
/// Compiles the bytecode of the hot functions to .NET code (a <see cref="DynamicMethod"/>), which the .NET JIT compiles
/// to machine code: one block of IL per instruction, without the dispatch of the interpreter, the operands as constants,
/// and the slots of the operand stack at known offsets (the depth of the stack at each instruction is known).
/// </summary>
/// <remarks>
/// <para>
/// The compiled code is an accelerator of the interpreter, not a separate execution model: the state stays in the frame
/// (registers, operand stack, environment, position), so the interpreter can take over at any instruction. The compiled
/// code leaves to the interpreter the instructions that change the running frame (the calls that push a frame, return,
/// throw, yield, await) and the paths the interpreter handles without a .NET exception (the TypeError of a property of
/// null...). The interpreter enters the compiled code again when it enters a frame (a call, a return to the caller, a
/// handler, a resumed generator) or takes a backward jump, at the positions that are entry points of the compiled code.
/// The calls of JavaScript functions still push their frames in the loop of the interpreter: the compiled code never
/// calls the compiled code of another function (no .NET recursion).
/// </para>
/// <para>
/// The compiled code checks the depth of the stack when it is entered and after the instructions it runs in the
/// interpreter (<see cref="JsInterpreter.JitExecuteCold"/>): when it is not the expected one, it returns and the
/// interpreter runs the instruction.
/// </para>
/// </remarks>
internal sealed class JitCompiler
{
    // The size of the functions that are compiled: the .NET JIT does not optimize the very large methods (MinOpts), and
    // the code of a large function is less likely to be hot
    private const int MaxCodeLength = 8_000;
    private const int MaxILSize = 60_000;

    private static readonly int ValueSize = Unsafe.SizeOf<JsValue>();
    private static readonly ConcurrentDictionary<string, MethodInfo> Helpers = new(StringComparer.Ordinal);

    private static readonly MethodInfo FrameGetValues = typeof(Frame).GetProperty(nameof(Frame.Values))!.GetMethod!;
    private static readonly MethodInfo FrameGetArgumentCount = typeof(Frame).GetProperty(nameof(Frame.ArgumentCount))!.GetMethod!;
    private static readonly MethodInfo FrameGetCode = typeof(Frame).GetProperty(nameof(Frame.Code))!.GetMethod!;
    private static readonly MethodInfo FrameGetPc = typeof(Frame).GetProperty(nameof(Frame.Pc))!.GetMethod!;
    private static readonly MethodInfo FrameSetPc = typeof(Frame).GetProperty(nameof(Frame.Pc))!.SetMethod!;
    private static readonly MethodInfo FrameGetSp = typeof(Frame).GetProperty(nameof(Frame.Sp))!.GetMethod!;
    private static readonly MethodInfo FrameSetSp = typeof(Frame).GetProperty(nameof(Frame.Sp))!.SetMethod!;
    private static readonly MethodInfo TemplateGetConstants = typeof(FunctionTemplate).GetProperty(nameof(FunctionTemplate.Constants))!.GetMethod!;
    private static readonly MethodInfo CountInterruptPoint = typeof(JsEngine).GetMethod(nameof(JsEngine.CountInterruptPoint), BindingFlags.Instance | BindingFlags.NonPublic)!;
    private static readonly MethodInfo Store = typeof(JsValue).GetMethod(nameof(JsValue.Store), BindingFlags.Static | BindingFlags.NonPublic)!;
    private static readonly MethodInfo StoreNumber = typeof(JsValue).GetMethod(nameof(JsValue.StoreNumber), BindingFlags.Static | BindingFlags.NonPublic)!;
    private static readonly MethodInfo GetNull = typeof(JsValue).GetProperty(nameof(JsValue.Null))!.GetMethod!;
    private static readonly MethodInfo GetTrue = typeof(JsValue).GetProperty(nameof(JsValue.True))!.GetMethod!;
    private static readonly MethodInfo GetFalse = typeof(JsValue).GetProperty(nameof(JsValue.False))!.GetMethod!;
    private static readonly MethodInfo GetHole = typeof(JsValue).GetProperty(nameof(JsValue.Hole), BindingFlags.Static | BindingFlags.NonPublic)!.GetMethod!;

    private readonly FunctionTemplate _template;
    private readonly int[] _code;
    private readonly int[] _depths;
    private readonly bool[] _isEntryPoint;
    private ILGenerator _il = null!;
    private ILLabel[] _labels = null!;
    private LocalBuilder _values = null!;
    private LocalBuilder _stackBase = null!;
    private LocalBuilder _registers = null!;
    private LocalBuilder _stack = null!;
    private LocalBuilder _constants = null!;
    private LocalBuilder _temp = null!;

    private JitCompiler(FunctionTemplate template)
    {
        _template = template;
        _code = template.Code;
        _depths = new int[_code.Length];
        _isEntryPoint = new bool[_code.Length];
    }

    /// <summary>Gets whether the runtime can compile code (false with Native AOT).</summary>
    public static bool IsSupported => RuntimeFeature.IsDynamicCodeSupported;

    /// <summary>Compiles the bytecode of a function (compiled by <see cref="FunctionTemplate.EnsureCompiled"/>); null when it cannot be compiled.</summary>
    public static JitCode? Compile(FunctionTemplate template)
    {
        if (!RuntimeFeature.IsDynamicCodeSupported || template.Code.Length > MaxCodeLength)
            return null;

        var compiler = new JitCompiler(template);
        if (!compiler.Analyze())
            return null;

        return compiler.Emit();
    }

    // ------------------------------------------------------------------------------------------------------------------
    // Analysis
    // ------------------------------------------------------------------------------------------------------------------

    /// <summary>Computes the depth of the stack at each reachable instruction, and the entry points; false when the code is not supported.</summary>
    private bool Analyze()
    {
        var code = _code;
        var isStart = new bool[code.Length];
        for (var pc = 0; pc < code.Length;)
        {
            var op = (OpCode)code[pc];
            if ((uint)op > (uint)OpCode.DebugStatement)
                return false;

            // The code compiled for a debugger is interpreted: the debugger observes each statement
            if (op is OpCode.DebugStatement or OpCode.Debugger)
                return false;

            var next = pc + 1 + BytecodeBuilder.GetOperandCount(op);
            if (next > code.Length)
                return false;

            // The registers are accessed without bounds checks
            if (op is OpCode.LoadLocal or OpCode.StoreLocal or OpCode.LoadLocalChecked or OpCode.StoreLocalChecked or OpCode.IncrementLocal or OpCode.DecrementLocal
                && (uint)code[pc + 1] >= (uint)_template.RegisterCount)
            {
                return false;
            }

            isStart[pc] = true;
            pc = next;
        }

        Array.Fill(_depths, -1);
        var pending = new Stack<(int Pc, int Depth)>();
        pending.Push((0, 0));
        _isEntryPoint[0] = true;
        foreach (var handler in _template.Handlers)
        {
            if ((uint)handler.Target >= (uint)code.Length)
                return false;

            pending.Push((handler.Target, handler.StackDepth + 1));
            _isEntryPoint[handler.Target] = true;
        }

        while (pending.TryPop(out var item))
        {
            var (pc, depth) = item;
            if ((uint)pc >= (uint)code.Length || !isStart[pc])
                return false;

            if (_depths[pc] >= 0)
            {
                if (_depths[pc] != depth)
                    return false;

                continue;
            }

            _depths[pc] = depth;
            var op = (OpCode)code[pc];
            var operandCount = BytecodeBuilder.GetOperandCount(op);
            var after = depth + BytecodeBuilder.GetStackEffect(op, operandCount > 0 ? code[pc + 1] : 0);
            if (after < 0 || after > _template.MaxStack || depth > _template.MaxStack)
                return false;

            var next = pc + 1 + operandCount;
            if (IsJump(op))
            {
                var target = code[pc + 1];
                pending.Push((target, GetDepthAtTarget(op, after)));
                if (target <= pc)
                {
                    // A loop: the interpreter enters the compiled code at its start (on-stack replacement)
                    if ((uint)target >= (uint)code.Length)
                        return false;

                    _isEntryPoint[target] = true;
                }
            }

            if (op is OpCode.Jump or OpCode.Throw or OpCode.Return or OpCode.ThrowError)
                continue;

            if (next >= code.Length)
                return false;

            // The interpreter resumes the frame after a call that pushed a frame, a yield or an await
            if (op is OpCode.Call or OpCode.TailCall or OpCode.TailCallEval or OpCode.CallSpread or OpCode.TailCallSpread or OpCode.TailCallEvalSpread
                or OpCode.New or OpCode.NewSpread or OpCode.GeneratorStart or OpCode.Yield or OpCode.Await)
            {
                _isEntryPoint[next] = true;
            }

            pending.Push((next, after));
        }

        return true;
    }

    private static bool IsJump(OpCode op)
    {
        return op is OpCode.Jump or OpCode.JumpIfTrue or OpCode.JumpIfFalse or OpCode.JumpIfTrueKeep or OpCode.JumpIfFalseKeep
            or OpCode.JumpIfNullishKeep or OpCode.JumpIfNotNullishKeep or OpCode.JumpIfNotUndefinedKeep or OpCode.JumpIfUndefinedPop
            or OpCode.IteratorNext or OpCode.IteratorStepFinish or OpCode.ForInNext or OpCode.AsyncDisposeNext
            || BytecodeBuilder.IsCompareJump(op);
    }

    /// <summary>The depth of the stack at the target of a jump (see BytecodeBuilder.EmitJump): the iteration jumps pop the value they would produce.</summary>
    private static int GetDepthAtTarget(OpCode op, int depthAfter)
    {
        return op is OpCode.IteratorNext or OpCode.ForInNext or OpCode.IteratorStepFinish or OpCode.JumpIfUndefinedPop or OpCode.AsyncDisposeNext ? depthAfter - 1 : depthAfter;
    }

    // ------------------------------------------------------------------------------------------------------------------
    // Code generation
    // ------------------------------------------------------------------------------------------------------------------

    [RequiresDynamicCode("The compiled code is a DynamicMethod")]
    private JitCode? Emit()
    {
        var method = new DynamicMethod("js:" + _template.Name, returnType: null, [typeof(Frame), typeof(JsEngine)], typeof(JitCompiler).Module, skipVisibility: true);
        _il = method.GetILGenerator(_code.Length * 16);
        _labels = new ILLabel[_code.Length];
        for (var pc = 0; pc < _code.Length; pc++)
        {
            if (_depths[pc] >= 0)
            {
                _labels[pc] = _il.DefineLabel();
            }
        }

        EmitPrologue();
        for (var pc = 0; pc < _code.Length; pc++)
        {
            if (_depths[pc] < 0)
                continue;

            _il.MarkLabel(_labels[pc]);
            EmitInstruction(pc, (OpCode)_code[pc], _depths[pc]);
        }

        if (_il.ILOffset > MaxILSize)
            return null;

        return method.CreateDelegate<JitCode>();
    }

    private void EmitPrologue()
    {
        var il = _il;
        _values = il.DeclareLocal(typeof(JsValue[]));
        _stackBase = il.DeclareLocal(typeof(int));
        _registers = il.DeclareLocal(typeof(JsValue).MakeByRefType());
        _stack = il.DeclareLocal(typeof(JsValue).MakeByRefType());
        _constants = il.DeclareLocal(typeof(JsValue[]));
        _temp = il.DeclareLocal(typeof(int));

        // values = frame.Values; stackBase = registerCount + frame.ArgumentCount (the operand stack is after the arguments)
        il.Emit(EmitOpCodes.Ldarg_0);
        il.Emit(EmitOpCodes.Call, FrameGetValues);
        il.Emit(EmitOpCodes.Stloc, _values);
        il.Emit(EmitOpCodes.Ldc_I4, _template.RegisterCount);
        il.Emit(EmitOpCodes.Ldarg_0);
        il.Emit(EmitOpCodes.Call, FrameGetArgumentCount);
        il.Emit(EmitOpCodes.Add);
        il.Emit(EmitOpCodes.Stloc, _stackBase);

        // registers = ref values[0]; stack = ref values[stackBase]: the slots are at constant offsets of these references
        // (the values are never empty: a function has at least the slot of its return value)
        il.Emit(EmitOpCodes.Ldloc, _values);
        il.Emit(EmitOpCodes.Ldc_I4_0);
        il.Emit(EmitOpCodes.Ldelema, typeof(JsValue));
        il.Emit(EmitOpCodes.Stloc, _registers);
        il.Emit(EmitOpCodes.Ldloc, _registers);
        il.Emit(EmitOpCodes.Ldloc, _stackBase);
        il.Emit(EmitOpCodes.Ldc_I4, ValueSize);
        il.Emit(EmitOpCodes.Mul);
        il.Emit(EmitOpCodes.Add);
        il.Emit(EmitOpCodes.Stloc, _stack);

        il.Emit(EmitOpCodes.Ldarg_0);
        il.Emit(EmitOpCodes.Call, FrameGetCode);
        il.Emit(EmitOpCodes.Call, TemplateGetConstants);
        il.Emit(EmitOpCodes.Stloc, _constants);

        // Dispatch on the position of the frame: an entry point whose stack has the expected depth, or back to the interpreter
        var entryPoints = new List<int>();
        for (var pc = 0; pc < _code.Length; pc++)
        {
            if (_isEntryPoint[pc] && _depths[pc] >= 0)
            {
                entryPoints.Add(pc);
            }
        }

        il.Emit(EmitOpCodes.Ldarg_0);
        il.Emit(EmitOpCodes.Call, FrameGetPc);
        il.Emit(EmitOpCodes.Stloc, _temp);
        var exit = il.DefineLabel();
        var entryLabels = new ILLabel[entryPoints.Count];
        for (var i = 0; i < entryLabels.Length; i++)
        {
            entryLabels[i] = il.DefineLabel();
        }

        EmitDispatch(entryPoints, entryLabels, 0, entryPoints.Count - 1, exit);
        for (var i = 0; i < entryLabels.Length; i++)
        {
            var pc = entryPoints[i];
            il.MarkLabel(entryLabels[i]);
            il.Emit(EmitOpCodes.Ldarg_0);
            il.Emit(EmitOpCodes.Call, FrameGetSp);
            EmitAbsoluteIndex(_depths[pc]);
            il.Emit(EmitOpCodes.Beq, _labels[pc]);
            il.Emit(EmitOpCodes.Ret);
        }

        il.MarkLabel(exit);
        il.Emit(EmitOpCodes.Ret);
    }

    /// <summary>A binary search of the position of the frame (in <see cref="_temp"/>) in the sorted entry points.</summary>
    private void EmitDispatch(List<int> entryPoints, ILLabel[] entryLabels, int low, int high, ILLabel exit)
    {
        var il = _il;
        if (high - low < 4)
        {
            for (var i = low; i <= high; i++)
            {
                il.Emit(EmitOpCodes.Ldloc, _temp);
                il.Emit(EmitOpCodes.Ldc_I4, entryPoints[i]);
                il.Emit(EmitOpCodes.Beq, entryLabels[i]);
            }

            il.Emit(EmitOpCodes.Br, exit);
            return;
        }

        var middle = (low + high) / 2;
        var lower = il.DefineLabel();
        il.Emit(EmitOpCodes.Ldloc, _temp);
        il.Emit(EmitOpCodes.Ldc_I4, entryPoints[middle]);
        il.Emit(EmitOpCodes.Blt, lower);
        EmitDispatch(entryPoints, entryLabels, middle, high, exit);
        il.MarkLabel(lower);
        EmitDispatch(entryPoints, entryLabels, low, middle - 1, exit);
    }

    private void EmitInstruction(int pc, OpCode op, int d)
    {
        var code = _code;
        var operand1 = pc + 1 < code.Length ? code[pc + 1] : 0;
        var operand2 = pc + 2 < code.Length ? code[pc + 2] : 0;
        var operand3 = pc + 3 < code.Length ? code[pc + 3] : 0;
        if (!IsPure(op, pc, operand1))
        {
            // The position of the running instruction: the handlers, the locations of the errors, the stack traces
            EmitSetPc(pc);
        }

        switch (op)
        {
            // ---- Constants and stack ------------------------------------------------------------------------------
            case OpCode.PushUndefined:
                EmitSlot(d);
                _il.Emit(EmitOpCodes.Initobj, typeof(JsValue));
                break;
            case OpCode.PushNull:
                EmitStoreSlot(d, GetNull);
                break;
            case OpCode.PushTrue:
                EmitStoreSlot(d, GetTrue);
                break;
            case OpCode.PushFalse:
                EmitStoreSlot(d, GetFalse);
                break;
            case OpCode.PushHole:
                EmitStoreSlot(d, GetHole);
                break;
            case OpCode.PushInt:
                EmitSlot(d);
                _il.Emit(EmitOpCodes.Ldc_R8, (double)operand1);
                _il.Emit(EmitOpCodes.Call, StoreNumber);
                break;
            case OpCode.PushConst:
                EmitSlot(d);
                _il.Emit(EmitOpCodes.Ldloc, _constants);
                _il.Emit(EmitOpCodes.Ldc_I4, operand1);
                _il.Emit(EmitOpCodes.Ldelem, typeof(JsValue));
                _il.Emit(EmitOpCodes.Call, Store);
                break;
            case OpCode.Pop:
                break;
            case OpCode.Dup:
                EmitSlot(d);
                EmitSlotValue(d - 1);
                _il.Emit(EmitOpCodes.Call, Store);
                break;
            case OpCode.Dup2:
                EmitSlot(d);
                EmitSlotValue(d - 2);
                _il.Emit(EmitOpCodes.Call, Store);
                EmitSlot(d + 1);
                EmitSlotValue(d - 1);
                _il.Emit(EmitOpCodes.Call, Store);
                break;
            case OpCode.Swap:
                EmitSlot(d - 1);
                EmitSlot(d - 2);
                EmitCall(nameof(JsInterpreter.JitSwap));
                break;
            case OpCode.Rot3:
                EmitSlot(d - 3);
                EmitSlot(d - 2);
                EmitSlot(d - 1);
                EmitCall(nameof(JsInterpreter.JitRot3));
                break;

            // ---- Registers and environments -----------------------------------------------------------------------
            case OpCode.LoadLocal:
                EmitSlot(d);
                EmitRegisterValue(operand1);
                _il.Emit(EmitOpCodes.Call, Store);
                break;
            case OpCode.LoadLocalChecked:
                _il.Emit(EmitOpCodes.Ldarg_0);
                EmitSlot(d);
                EmitRegisterValue(operand1);
                EmitInt(operand2);
                EmitCall(nameof(JsInterpreter.JitLoadChecked));
                break;
            case OpCode.StoreLocal:
                EmitRegister(operand1);
                EmitSlotValue(d - 1);
                _il.Emit(EmitOpCodes.Call, Store);
                break;
            case OpCode.StoreLocalChecked:
                _il.Emit(EmitOpCodes.Ldarg_0);
                EmitRegister(operand1);
                EmitSlotValue(d - 1);
                EmitInt(operand2);
                EmitCall(nameof(JsInterpreter.JitStoreChecked));
                break;
            case OpCode.IncrementLocal:
            case OpCode.DecrementLocal:
                EmitRegister(operand1);
                EmitInt(op == OpCode.IncrementLocal ? 1 : -1);
                EmitCall(nameof(JsInterpreter.JitIncrementLocal));
                break;
            case OpCode.LoadContext:
                _il.Emit(EmitOpCodes.Ldarg_0);
                EmitSlot(d);
                EmitInt(operand1);
                EmitInt(operand2);
                EmitCall(nameof(JsInterpreter.JitLoadContext));
                break;
            case OpCode.LoadContextChecked:
                _il.Emit(EmitOpCodes.Ldarg_0);
                EmitSlot(d);
                EmitInt(operand1);
                EmitInt(operand2);
                EmitInt(operand3);
                EmitCall(nameof(JsInterpreter.JitLoadContextChecked));
                break;
            case OpCode.StoreContext:
                _il.Emit(EmitOpCodes.Ldarg_0);
                EmitSlotValue(d - 1);
                EmitInt(operand1);
                EmitInt(operand2);
                EmitCall(nameof(JsInterpreter.JitStoreContext));
                break;
            case OpCode.StoreContextChecked:
                _il.Emit(EmitOpCodes.Ldarg_0);
                EmitSlotValue(d - 1);
                EmitInt(operand1);
                EmitInt(operand2);
                EmitInt(operand3);
                EmitCall(nameof(JsInterpreter.JitStoreContextChecked));
                break;
            case OpCode.LoadGlobal:
                _il.Emit(EmitOpCodes.Ldarg_0);
                EmitSlot(d);
                EmitInt(operand1);
                EmitInt(operand2);
                EmitCall(nameof(JsInterpreter.JitLoadGlobal));
                break;
            case OpCode.StoreGlobal:
                _il.Emit(EmitOpCodes.Ldarg_0);
                EmitSlotValue(d - 1);
                EmitInt(operand1);
                EmitInt(operand2);
                EmitInt(operand3);
                EmitCall(nameof(JsInterpreter.JitStoreGlobal));
                break;
            case OpCode.PushScope:
                _il.Emit(EmitOpCodes.Ldarg_0);
                EmitInt(operand1);
                EmitCall(nameof(JsInterpreter.JitPushScope));
                break;
            case OpCode.PopScope:
                _il.Emit(EmitOpCodes.Ldarg_0);
                EmitCall(nameof(JsInterpreter.JitPopScope));
                break;
            case OpCode.CopyScope:
                _il.Emit(EmitOpCodes.Ldarg_0);
                EmitCall(nameof(JsInterpreter.JitCopyScope));
                break;
            case OpCode.LoadArgument:
                _il.Emit(EmitOpCodes.Ldarg_0);
                _il.Emit(EmitOpCodes.Ldloc, _values);
                EmitSlot(d);
                EmitInt(operand1);
                EmitCall(nameof(JsInterpreter.JitLoadArgument));
                break;
            case OpCode.LoadArgumentsLength:
                _il.Emit(EmitOpCodes.Ldarg_0);
                EmitSlot(d);
                EmitCall(nameof(JsInterpreter.JitLoadArgumentsLength));
                break;
            case OpCode.LoadArgumentsElement:
                _il.Emit(EmitOpCodes.Ldarg_0);
                _il.Emit(EmitOpCodes.Ldloc, _values);
                EmitSlot(d - 1);
                EmitInt(operand1);
                EmitCall(nameof(JsInterpreter.JitLoadArgumentsElement));
                break;
            case OpCode.LoadThis:
                _il.Emit(EmitOpCodes.Ldarg_0);
                EmitSlot(d);
                EmitCall(nameof(JsInterpreter.JitLoadThis));
                break;

            // ---- Operators ------------------------------------------------------------------------------------------
            case OpCode.Add:
                EmitBinary(d, nameof(JsInterpreter.JitAdd));
                break;
            case OpCode.Subtract:
                EmitBinary(d, nameof(JsInterpreter.JitSubtract));
                break;
            case OpCode.Multiply:
                EmitBinary(d, nameof(JsInterpreter.JitMultiply));
                break;
            case OpCode.Divide:
                EmitBinary(d, nameof(JsInterpreter.JitDivide));
                break;
            case OpCode.Modulo:
                EmitBinary(d, nameof(JsInterpreter.JitModulo));
                break;
            case OpCode.LeftShift:
            case OpCode.RightShift:
            case OpCode.UnsignedRightShift:
            case OpCode.BitwiseAnd:
            case OpCode.BitwiseOr:
            case OpCode.BitwiseXor:
                EmitSlot(d - 2);
                EmitSlotValue(d - 1);
                EmitInt((int)op);
                EmitCall(nameof(JsInterpreter.JitBitwise));
                break;
            case OpCode.BitwiseNot:
                EmitSlot(d - 1);
                EmitCall(nameof(JsInterpreter.JitBitwiseNot));
                break;
            case OpCode.ToNumber:
                EmitSlot(d - 1);
                EmitCall(nameof(JsInterpreter.JitToNumber));
                break;
            case OpCode.Equal:
            case OpCode.NotEqual:
                EmitSlot(d - 2);
                EmitSlotValue(d - 1);
                EmitInt(op == OpCode.NotEqual ? 1 : 0);
                EmitCall(nameof(JsInterpreter.JitEqual));
                break;
            case OpCode.StrictEqual:
            case OpCode.StrictNotEqual:
                EmitSlot(d - 2);
                EmitSlotValue(d - 1);
                EmitInt(op == OpCode.StrictNotEqual ? 1 : 0);
                EmitCall(nameof(JsInterpreter.JitStrictEqual));
                break;
            case OpCode.LessThan:
                EmitBinary(d, nameof(JsInterpreter.JitLessThan));
                break;
            case OpCode.GreaterThan:
                EmitBinary(d, nameof(JsInterpreter.JitGreaterThan));
                break;
            case OpCode.LessThanOrEqual:
                EmitBinary(d, nameof(JsInterpreter.JitLessThanOrEqual));
                break;
            case OpCode.GreaterThanOrEqual:
                EmitBinary(d, nameof(JsInterpreter.JitGreaterThanOrEqual));
                break;
            case OpCode.Negate:
                EmitSlot(d - 1);
                EmitCall(nameof(JsInterpreter.JitNegate));
                break;
            case OpCode.ToNumeric:
                EmitSlot(d - 1);
                EmitCall(nameof(JsInterpreter.JitToNumeric));
                break;
            case OpCode.LogicalNot:
                EmitSlot(d - 1);
                EmitCall(nameof(JsInterpreter.JitLogicalNot));
                break;
            case OpCode.TypeOf:
                EmitSlot(d - 1);
                EmitCall(nameof(JsInterpreter.JitTypeOf));
                break;
            case OpCode.Increment:
            case OpCode.Decrement:
                EmitSlot(d - 1);
                EmitInt(op == OpCode.Increment ? 1 : -1);
                EmitCall(nameof(JsInterpreter.JitIncrement));
                break;

            // ---- Control flow ---------------------------------------------------------------------------------------
            case OpCode.Jump:
                EmitJump(pc, operand1);
                break;
            case OpCode.JumpIfTrue:
            case OpCode.JumpIfFalse:
                EmitSlotValue(d - 1);
                EmitCall(nameof(JsInterpreter.JitToBoolean));
                EmitConditionalJump(pc, operand1, jumpIfTrue: op == OpCode.JumpIfTrue);
                break;
            case OpCode.JumpIfTrueKeep:
            case OpCode.JumpIfFalseKeep:
                // As in the interpreter, not a loop: no check of the limits
                EmitSlotValue(d - 1);
                EmitCall(nameof(JsInterpreter.JitToBoolean));
                _il.Emit(op == OpCode.JumpIfTrueKeep ? EmitOpCodes.Brtrue : EmitOpCodes.Brfalse, _labels[operand1]);
                break;
            case >= OpCode.JumpIfLessThan and <= OpCode.JumpIfStrictNotEqual:
                EmitSlotValue(d - 2);
                EmitSlotValue(d - 1);
                EmitInt((int)op);
                EmitCall(nameof(JsInterpreter.JitCompareJump));
                EmitConditionalJump(pc, operand1, jumpIfTrue: true);
                break;

            // ---- Objects and properties -------------------------------------------------------------------------------
            case OpCode.NewObject:
                _il.Emit(EmitOpCodes.Ldarg_0);
                EmitSlot(d);
                EmitCall(nameof(JsInterpreter.JitNewObject));
                break;
            case OpCode.NewArray:
                _il.Emit(EmitOpCodes.Ldarg_0);
                EmitSlot(d);
                EmitCall(nameof(JsInterpreter.JitNewArray));
                break;
            case OpCode.ArrayAppend:
                EmitSlotValue(d - 2);
                EmitSlotValue(d - 1);
                EmitCall(nameof(JsInterpreter.JitArrayAppend));
                break;
            case OpCode.NewArgumentList:
                EmitSlot(d);
                EmitCall(nameof(JsInterpreter.JitNewArgumentList));
                break;
            case OpCode.ArgumentListAppend:
                EmitSlotValue(d - 2);
                EmitSlotValue(d - 1);
                EmitCall(nameof(JsInterpreter.JitArgumentListAppend));
                break;
            case OpCode.GetProperty:
                _il.Emit(EmitOpCodes.Ldarg_0);
                EmitSlot(d - 1);
                EmitInt(operand1);
                EmitInt(operand2);
                EmitCall(nameof(JsInterpreter.JitGetProperty));
                EmitExitIfFalse(pc, d);
                break;
            case OpCode.GetMethod:
                _il.Emit(EmitOpCodes.Ldarg_0);
                EmitSlot(d - 1);
                EmitSlot(d);
                EmitInt(operand1);
                EmitInt(operand2);
                EmitCall(nameof(JsInterpreter.JitGetMethod));
                EmitExitIfFalse(pc, d);
                break;
            case OpCode.SetProperty:
                _il.Emit(EmitOpCodes.Ldarg_0);
                EmitSlot(d - 2);
                EmitSlotValue(d - 1);
                EmitInt(operand1);
                EmitInt(operand2);
                EmitCall(nameof(JsInterpreter.JitSetProperty));
                break;
            case OpCode.GetElement:
                _il.Emit(EmitOpCodes.Ldarg_0);
                EmitSlot(d - 2);
                EmitSlotValue(d - 1);
                EmitCall(nameof(JsInterpreter.JitGetElement));
                break;
            case OpCode.SetElement:
                _il.Emit(EmitOpCodes.Ldarg_0);
                EmitSlot(d - 3);
                EmitSlotValue(d - 2);
                EmitSlotValue(d - 1);
                EmitCall(nameof(JsInterpreter.JitSetElement));
                break;
            case OpCode.DefineField:
                _il.Emit(EmitOpCodes.Ldarg_0);
                EmitSlotValue(d - 2);
                EmitSlotValue(d - 1);
                EmitInt(operand1);
                EmitInt(operand2);
                EmitCall(nameof(JsInterpreter.JitDefineField));
                break;

            // ---- Calls --------------------------------------------------------------------------------------------------
            case OpCode.Call:
            case OpCode.TailCall:
                _il.Emit(EmitOpCodes.Ldarg_0);
                _il.Emit(EmitOpCodes.Ldloc, _values);
                EmitAbsoluteIndex(d - operand1 - 2);
                EmitInt(operand1);
                EmitCall(nameof(JsInterpreter.JitCall));
                EmitExitIfFalse(pc, d);
                break;
            case OpCode.CallSpread:
            case OpCode.TailCallSpread:
                _il.Emit(EmitOpCodes.Ldarg_0);
                _il.Emit(EmitOpCodes.Ldloc, _values);
                EmitAbsoluteIndex(d - 3);
                EmitCall(nameof(JsInterpreter.JitCallSpread));
                EmitExitIfFalse(pc, d);
                break;
            case OpCode.New:
            case OpCode.NewSpread:
                _il.Emit(EmitOpCodes.Ldarg_0);
                _il.Emit(EmitOpCodes.Ldloc, _values);
                EmitAbsoluteIndex(op == OpCode.New ? d - operand1 - 1 : d - 2);
                EmitInt(op == OpCode.New ? operand1 : 0);
                EmitInt(op == OpCode.New ? 0 : 1);
                EmitCall(nameof(JsInterpreter.JitNew));
                EmitExitIfFalse(pc, d);
                break;
            case OpCode.CreateClosure:
                _il.Emit(EmitOpCodes.Ldarg_0);
                EmitSlot(d);
                EmitInt(operand1);
                EmitCall(nameof(JsInterpreter.JitCreateClosure));
                break;

            // ---- The instructions of the interpreter: they change the running frame, or suspend it ---------------------
            case OpCode.Throw:
            case OpCode.Return:
            case OpCode.TailCallEval:
            case OpCode.TailCallEvalSpread:
            case OpCode.GeneratorStart:
            case OpCode.Yield:
            case OpCode.Await:
                EmitExit(pc, d);
                break;

            default:
                EmitCold(pc, op, d, operand1);
                break;
        }
    }

    /// <summary>Whether an instruction cannot throw, call code or observe the position of the frame (the position is not set before it).</summary>
    private static bool IsPure(OpCode op, int pc, int operand1)
    {
        return op is OpCode.PushUndefined or OpCode.PushNull or OpCode.PushTrue or OpCode.PushFalse or OpCode.PushInt or OpCode.PushConst or OpCode.PushHole
            or OpCode.Pop or OpCode.Dup or OpCode.Dup2 or OpCode.Swap or OpCode.Rot3
            or OpCode.LoadLocal or OpCode.StoreLocal or OpCode.LoadContext or OpCode.StoreContext
            or OpCode.LoadArgument or OpCode.LoadArgumentsLength or OpCode.PopScope
            or OpCode.JumpIfTrue or OpCode.JumpIfFalse or OpCode.JumpIfTrueKeep or OpCode.JumpIfFalseKeep
            || (op == OpCode.Jump && operand1 > pc);
    }

    /// <summary>An instruction of <see cref="JsInterpreter.JitExecuteCold"/>: the compiled code continues at the next instruction (or the target of a jump) when the position and the depth of the stack are the expected ones.</summary>
    private void EmitCold(int pc, OpCode op, int d, int operand1)
    {
        var il = _il;
        il.Emit(EmitOpCodes.Ldarg_0);
        EmitInt((int)op);
        EmitAbsoluteIndex(d);
        EmitCall(nameof(JsInterpreter.JitExecuteCold));
        il.Emit(EmitOpCodes.Stloc, _temp);

        var operandCount = BytecodeBuilder.GetOperandCount(op);
        var after = d + BytecodeBuilder.GetStackEffect(op, operandCount > 0 ? operand1 : 0);
        var next = pc + 1 + operandCount;
        if (op is not OpCode.ThrowError && next < _code.Length && _depths[next] == after)
        {
            EmitContinueIf(next, after);
        }

        if (IsJump(op) && (uint)operand1 < (uint)_code.Length && _depths[operand1] == GetDepthAtTarget(op, after))
        {
            EmitContinueIf(operand1, _depths[operand1]);
        }

        // Not the expected state: the interpreter continues from the position set by the instruction
        il.Emit(EmitOpCodes.Ldarg_0);
        il.Emit(EmitOpCodes.Ldloc, _temp);
        il.Emit(EmitOpCodes.Call, FrameSetSp);
        il.Emit(EmitOpCodes.Ret);
    }

    /// <summary>Continues at an instruction when the position of the frame is this instruction and the stack pointer (in <see cref="_temp"/>) has its depth.</summary>
    private void EmitContinueIf(int target, int depth)
    {
        var il = _il;
        var skip = il.DefineLabel();
        il.Emit(EmitOpCodes.Ldarg_0);
        il.Emit(EmitOpCodes.Call, FrameGetPc);
        EmitInt(target);
        il.Emit(EmitOpCodes.Bne_Un, skip);
        il.Emit(EmitOpCodes.Ldloc, _temp);
        EmitAbsoluteIndex(depth);
        il.Emit(EmitOpCodes.Beq, _labels[target]);
        il.MarkLabel(skip);
    }

    private void EmitBinary(int d, string helper)
    {
        EmitSlot(d - 2);
        EmitSlotValue(d - 1);
        EmitCall(helper);
    }

    private void EmitJump(int pc, int target)
    {
        if (target <= pc)
        {
            // A loop: the limits of the script are checked as in the interpreter
            EmitCountInterruptPoint();
        }

        _il.Emit(EmitOpCodes.Br, _labels[target]);
    }

    private void EmitConditionalJump(int pc, int target, bool jumpIfTrue)
    {
        if (target > pc)
        {
            _il.Emit(jumpIfTrue ? EmitOpCodes.Brtrue : EmitOpCodes.Brfalse, _labels[target]);
            return;
        }

        var skip = _il.DefineLabel();
        _il.Emit(jumpIfTrue ? EmitOpCodes.Brfalse : EmitOpCodes.Brtrue, skip);
        EmitSetPc(pc);
        EmitCountInterruptPoint();
        _il.Emit(EmitOpCodes.Br, _labels[target]);
        _il.MarkLabel(skip);
    }

    private void EmitCountInterruptPoint()
    {
        _il.Emit(EmitOpCodes.Ldarg_1);
        _il.Emit(EmitOpCodes.Call, CountInterruptPoint);
    }

    /// <summary>Leaves the instruction to the interpreter when the helper returned false.</summary>
    private void EmitExitIfFalse(int pc, int d)
    {
        var next = _il.DefineLabel();
        _il.Emit(EmitOpCodes.Brtrue, next);
        EmitExit(pc, d);
        _il.MarkLabel(next);
    }

    /// <summary>Leaves the instruction to the interpreter: its position, and the stack pointer before it.</summary>
    private void EmitExit(int pc, int d)
    {
        EmitSetPc(pc);
        _il.Emit(EmitOpCodes.Ldarg_0);
        EmitAbsoluteIndex(d);
        _il.Emit(EmitOpCodes.Call, FrameSetSp);
        _il.Emit(EmitOpCodes.Ret);
    }

    private void EmitSetPc(int pc)
    {
        _il.Emit(EmitOpCodes.Ldarg_0);
        EmitInt(pc);
        _il.Emit(EmitOpCodes.Call, FrameSetPc);
    }

    private void EmitInt(int value) => _il.Emit(EmitOpCodes.Ldc_I4, value);

    /// <summary>The index in the values of the frame of a slot of the operand stack.</summary>
    private void EmitAbsoluteIndex(int depth)
    {
        _il.Emit(EmitOpCodes.Ldloc, _stackBase);
        if (depth != 0)
        {
            EmitInt(depth);
            _il.Emit(EmitOpCodes.Add);
        }
    }

    /// <summary>A reference to a slot of the operand stack (the depth is checked by the analysis: it is below the size of the stack).</summary>
    private void EmitSlot(int depth) => EmitReference(_stack, depth);

    private void EmitSlotValue(int depth)
    {
        EmitSlot(depth);
        _il.Emit(EmitOpCodes.Ldobj, typeof(JsValue));
    }

    private void EmitStoreSlot(int depth, MethodInfo getValue)
    {
        EmitSlot(depth);
        _il.Emit(EmitOpCodes.Call, getValue);
        _il.Emit(EmitOpCodes.Call, Store);
    }

    /// <summary>A reference to a register (the register is checked by the analysis: it is below the number of registers).</summary>
    private void EmitRegister(int register) => EmitReference(_registers, register);

    private void EmitRegisterValue(int register)
    {
        EmitRegister(register);
        _il.Emit(EmitOpCodes.Ldobj, typeof(JsValue));
    }

    private void EmitReference(LocalBuilder baseReference, int index)
    {
        _il.Emit(EmitOpCodes.Ldloc, baseReference);
        if (index != 0)
        {
            EmitInt(index * ValueSize);
            _il.Emit(EmitOpCodes.Add);
        }
    }

    private void EmitCall(string helper)
    {
        var method = Helpers.GetOrAdd(helper, static name => typeof(JsInterpreter).GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic) ?? throw new InvalidOperationException("Unknown helper " + name));
        _il.Emit(EmitOpCodes.Call, method);
    }
}
