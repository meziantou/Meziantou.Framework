using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Reflection.Emit;

namespace Meziantou.Framework.WasmEngine;

/// <summary>
/// Compiles the code of a function to .NET code (a <see cref="DynamicMethod"/>), which the JIT of .NET compiles to native
/// code. The locals and the operands of the function are locals of the method: each one has the bits of its number
/// (<see cref="Slot.A"/>), and, when the function uses them, the high bits of its vector (<see cref="Slot.B"/>) and its
/// reference (<see cref="Slot.R"/>). The calls and the instructions that the method does not implement use the stack of
/// the machine, as the interpreter does.
/// </summary>
[RequiresDynamicCode("The functions are compiled with DynamicMethod")]
internal sealed partial class IlCompiler
{
    /// <summary>The maximum number of locals of a method: a larger function is interpreted.</summary>
    private const int MaxMethodLocals = 2048;

    /// <summary>The maximum number of locals of a method whose frame is small (see <see cref="Machine._jitStackBound"/>).</summary>
    private const int MaxSmallFrameLocals = 256;

    /// <summary>The methods of the typed entries (<see cref="CompiledCode.Typed"/>), by number of parameters.</summary>
    private static readonly MethodInfo[] TypedInvokes =
    [
        typeof(JitTyped0).GetMethod(nameof(JitTyped0.Invoke))!,
        typeof(JitTyped1).GetMethod(nameof(JitTyped1.Invoke))!,
        typeof(JitTyped2).GetMethod(nameof(JitTyped2.Invoke))!,
        typeof(JitTyped3).GetMethod(nameof(JitTyped3.Invoke))!,
        typeof(JitTyped4).GetMethod(nameof(JitTyped4.Invoke))!,
        typeof(JitTyped5).GetMethod(nameof(JitTyped5.Invoke))!,
        typeof(JitTyped6).GetMethod(nameof(JitTyped6.Invoke))!,
        typeof(JitTyped7).GetMethod(nameof(JitTyped7.Invoke))!,
        typeof(JitTyped8).GetMethod(nameof(JitTyped8.Invoke))!,
    ];

    private static readonly Dictionary<ushort, MethodInfo> UnaryOperations = [];
    private static readonly Dictionary<ushort, MethodInfo> BinaryOperations = [];
    private static readonly Dictionary<ushort, MethodInfo> Loads = [];
    private static readonly Dictionary<ushort, MethodInfo> Stores = [];
    private static readonly Dictionary<string, MethodInfo> JitOpsMethods = new(StringComparer.Ordinal);

    /// <summary>
    /// The loads and the stores that the compiled code does without <see cref="JitOps"/>, on a memory that is not shared
    /// and has 32-bit addresses: the size of the access, the instruction that reads or writes it, and the conversion of
    /// the value (to the bits of a slot for a load, from them for a store).
    /// </summary>
    private static readonly Dictionary<ushort, (int Size, OpCode Access, OpCode Convert)> InlineAccesses = new()
    {
        [Op.I32Load] = (4, OpCodes.Ldind_I4, OpCodes.Conv_I8),
        [Op.I64Load] = (8, OpCodes.Ldind_I8, OpCodes.Nop),
        [Op.F32Load] = (4, OpCodes.Ldind_I4, OpCodes.Conv_I8),
        [Op.F64Load] = (8, OpCodes.Ldind_I8, OpCodes.Nop),
        [Op.I32Load8S] = (1, OpCodes.Ldind_I1, OpCodes.Conv_I8),
        [Op.I32Load8U] = (1, OpCodes.Ldind_U1, OpCodes.Conv_U8),
        [Op.I32Load16S] = (2, OpCodes.Ldind_I2, OpCodes.Conv_I8),
        [Op.I32Load16U] = (2, OpCodes.Ldind_U2, OpCodes.Conv_U8),
        [Op.I64Load8S] = (1, OpCodes.Ldind_I1, OpCodes.Conv_I8),
        [Op.I64Load8U] = (1, OpCodes.Ldind_U1, OpCodes.Conv_U8),
        [Op.I64Load16S] = (2, OpCodes.Ldind_I2, OpCodes.Conv_I8),
        [Op.I64Load16U] = (2, OpCodes.Ldind_U2, OpCodes.Conv_U8),
        [Op.I64Load32S] = (4, OpCodes.Ldind_I4, OpCodes.Conv_I8),
        [Op.I64Load32U] = (4, OpCodes.Ldind_U4, OpCodes.Conv_U8),
        [Op.I32Store] = (4, OpCodes.Stind_I4, OpCodes.Conv_I4),
        [Op.I64Store] = (8, OpCodes.Stind_I8, OpCodes.Nop),
        [Op.F32Store] = (4, OpCodes.Stind_I4, OpCodes.Conv_I4),
        [Op.F64Store] = (8, OpCodes.Stind_I8, OpCodes.Nop),
        [Op.I32Store8] = (1, OpCodes.Stind_I1, OpCodes.Conv_I4),
        [Op.I32Store16] = (2, OpCodes.Stind_I2, OpCodes.Conv_I4),
        [Op.I64Store8] = (1, OpCodes.Stind_I1, OpCodes.Conv_I4),
        [Op.I64Store16] = (2, OpCodes.Stind_I2, OpCodes.Conv_I4),
        [Op.I64Store32] = (4, OpCodes.Stind_I4, OpCodes.Conv_I4),
    };

    private static readonly FieldInfo SlotA = typeof(Slot).GetField(nameof(Slot.A))!;
    private static readonly FieldInfo SlotB = typeof(Slot).GetField(nameof(Slot.B))!;
    private static readonly FieldInfo SlotR = typeof(Slot).GetField(nameof(Slot.R))!;
    private static readonly FieldInfo GlobalValue = typeof(ManagedGlobal).GetField(nameof(ManagedGlobal.Value))!;
    private static readonly FieldInfo ClosureCode = typeof(JitClosure).GetField(nameof(JitClosure.Code))!;
    private static readonly FieldInfo ClosureInstrs = typeof(JitClosure).GetField(nameof(JitClosure.Instrs))!;
    private static readonly FieldInfo ClosureCallees = typeof(JitClosure).GetField(nameof(JitClosure.Callees))!;
    private static readonly FieldInfo ClosureTypes = typeof(JitClosure).GetField(nameof(JitClosure.Types))!;
    private static readonly FieldInfo CodeTyped = typeof(CompiledCode).GetField(nameof(CompiledCode.Typed))!;
    private static readonly FieldInfo FunctionTyped = typeof(ManagedFunction).GetField(nameof(ManagedFunction.Typed))!;
    private static readonly FieldInfo FunctionTypedInstance = typeof(ManagedFunction).GetField(nameof(ManagedFunction.TypedInstance))!;
    private static readonly MethodInfo FunctionDefinedType = typeof(ManagedFunction).GetProperty(nameof(ManagedFunction.DefinedType))!.GetMethod!;
    private static readonly MethodInfo TableSize = typeof(ManagedTable).GetProperty(nameof(ManagedTable.Size))!.GetMethod!;
    private static readonly MethodInfo TableElements = typeof(ManagedTable).GetProperty(nameof(ManagedTable.Elements))!.GetMethod!;
    private static readonly MethodInfo CodeFunctionInstance = typeof(CodeFunction).GetProperty(nameof(CodeFunction.Instance))!.GetMethod!;
    private static readonly MethodInfo InstanceFunctions = typeof(ManagedInstance).GetProperty(nameof(ManagedInstance.Functions))!.GetMethod!;
    private static readonly MethodInfo InstanceMemories = typeof(ManagedInstance).GetProperty(nameof(ManagedInstance.Memories))!.GetMethod!;
    private static readonly MethodInfo InstanceGlobals = typeof(ManagedInstance).GetProperty(nameof(ManagedInstance.Globals))!.GetMethod!;
    private static readonly MethodInfo InstanceTables = typeof(ManagedInstance).GetProperty(nameof(ManagedInstance.Tables))!.GetMethod!;
    private static readonly MethodInfo MachineStack = typeof(Machine).GetProperty(nameof(Machine.Stack))!.GetMethod!;
    private static readonly MethodInfo MachineJitStack = GetMethod(typeof(Machine), nameof(Machine.JitStack));
    private static readonly MethodInfo MachineJitCall = GetMethod(typeof(Machine), nameof(Machine.JitCall));
    private static readonly MethodInfo MachineJitExecute = GetMethod(typeof(Machine), nameof(Machine.JitExecute));
    private static readonly MethodInfo MachineCheckInterruptSlow = GetMethod(typeof(Machine), nameof(Machine.CheckInterruptSlow));
    private static readonly FieldInfo MachineInterruptRequested = typeof(Machine).GetField(nameof(Machine._interruptRequested), BindingFlags.NonPublic | BindingFlags.Instance)!;
    private static readonly MethodInfo MemoryBase = typeof(ManagedMemory).GetProperty(nameof(ManagedMemory.Base))!.GetMethod!;
    private static readonly MethodInfo MemoryUnsharedByteLength = typeof(ManagedMemory).GetProperty(nameof(ManagedMemory.UnsharedByteLength))!.GetMethod!;
    private static readonly MethodInfo MachineJitCheckStack = GetMethod(typeof(Machine), nameof(Machine.JitCheckStack));
    private static readonly MethodInfo MachineJitStackOverflow = GetMethod(typeof(Machine), nameof(Machine.JitStackOverflow));
    private static readonly FieldInfo MachineJitDepthLimit = typeof(Machine).GetField(nameof(Machine._jitDepthLimit), BindingFlags.NonPublic | BindingFlags.Instance)!;
    private static readonly FieldInfo MachineJitStackBound = typeof(Machine).GetField(nameof(Machine._jitStackBound), BindingFlags.NonPublic | BindingFlags.Instance)!;

    private readonly CompiledCode _code;
    private readonly Instr[] _instrs;
    private readonly int[] _heights;
    private readonly int[] _pops;
    private readonly ModuleInfo _module;
    private readonly bool[] _reachable;
    private readonly bool[] _isTarget;
    private readonly Label[] _labels;
    private readonly Dictionary<int, LocalBuilder> _memories = [];
    private readonly Dictionary<int, (LocalBuilder Base, LocalBuilder Length)> _inlineMemories = [];
    private readonly Dictionary<int, (LocalBuilder Function, LocalBuilder Typed, LocalBuilder Instance)> _imports = [];
    private readonly Dictionary<int, LocalBuilder> _globals = [];
    private readonly Dictionary<int, LocalBuilder> _tables = [];
    private readonly List<(CatchClause Clause, int Index)> _clauses = [];
    private readonly List<CompiledCode> _callees = [];
    private readonly List<DefinedType> _types = [];

    // Whether the method takes the parameters as arguments and returns the result (see CompiledCode.Typed)
    private readonly bool _typed;

    // Whether the method starts at a loop of a running interpreted call (see CompileOsr)
    private readonly bool _osr;
    private DynamicMethod _method = null!;
    private ILGenerator _il = null!;
    private bool _hasV128;
    private bool _hasRefs;
    private int _methodLocals;

    // The calls and the loops of the function (see AnalyzeControl)
    private bool _makesCalls;
    private bool _hasLoops;
    private LocalBuilder[] _localA = [];
    private LocalBuilder?[] _localB = [];
    private LocalBuilder?[] _localR = [];
    private LocalBuilder[] _stackA = [];
    private LocalBuilder[]? _stackB;
    private LocalBuilder[]? _stackR;
    private LocalBuilder _scratch = null!;
    private LocalBuilder _slots = null!;
    private LocalBuilder _callee = null!;
    private LocalBuilder _codeCallee = null!;
    private LocalBuilder _address = null!;
    private LocalBuilder _effectiveAddress = null!;
    private LocalBuilder? _pc;
    private LocalBuilder? _entry;
    private LocalBuilder _probe = null!;
    private LocalBuilder? _result;
    private Label _returnLabel;
    private bool _inTry;

    static IlCompiler()
    {
        var operations = new Dictionary<string, ushort>(StringComparer.Ordinal);
        foreach (var field in typeof(Op).GetFields(BindingFlags.Public | BindingFlags.Static))
        {
            if (field.IsLiteral && field.FieldType == typeof(ushort))
            {
                operations[field.Name] = (ushort)field.GetRawConstantValue()!;
            }
        }

        foreach (var method in typeof(JitOps).GetMethods(BindingFlags.Public | BindingFlags.Static))
        {
            JitOpsMethods.TryAdd(method.Name, method);
            if (!operations.TryGetValue(method.Name, out var op))
                continue;

            var parameters = method.GetParameters();
            if (method.ReturnType == typeof(long) && parameters.Length == 1 && parameters[0].ParameterType == typeof(long))
            {
                UnaryOperations[op] = method;
            }
            else if (method.ReturnType == typeof(long) && parameters.Length == 2 && parameters[0].ParameterType == typeof(long) && parameters[1].ParameterType == typeof(long))
            {
                BinaryOperations[op] = method;
            }
            else if (method.ReturnType == typeof(long) && parameters.Length == 3 && parameters[0].ParameterType == typeof(ManagedMemory))
            {
                Loads[op] = method;
            }
            else if (method.ReturnType == typeof(void) && parameters.Length == 4 && parameters[0].ParameterType == typeof(ManagedMemory))
            {
                Stores[op] = method;
            }
        }
    }

    private IlCompiler(CompiledCode code, ModuleInfo module, bool osr = false)
    {
        _osr = osr;
        _code = code;
        _instrs = code.Code;
        _heights = code.Heights;
        _pops = code.Pops;
        _module = module;
        _reachable = new bool[_instrs.Length];
        _isTarget = new bool[_instrs.Length];
        _labels = new Label[_instrs.Length];
        _typed = !osr && IsTyped(code.Locals.AsSpan(0, code.ParameterCount), code.Results);
    }

    /// <summary>Whether the functions of a type have a typed entry (<see cref="CompiledCode.Typed"/>): numbers only, and one result at most.</summary>
    private static bool IsTyped(ReadOnlySpan<ValType> parameters, ReadOnlySpan<ValType> results)
    {
        if (parameters.Length >= TypedInvokes.Length || results.Length > 1)
            return false;

        foreach (var parameter in parameters)
        {
            if (!parameter.IsNumeric)
                return false;
        }

        return results.Length == 0 || results[0].IsNumeric;
    }

    private static MethodInfo GetMethod([DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicMethods | DynamicallyAccessedMemberTypes.NonPublicMethods)] Type type, string name) => type.GetMethod(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static)!;

    /// <summary>A method of <see cref="JitOps"/> (resolved once, not at each instruction).</summary>
    private static MethodInfo GetJitOp(string name) => JitOpsMethods[name];

    /// <summary>Compiles a function, or returns null when its code cannot be compiled (it is interpreted).</summary>
    public static JitFunction? Compile(CodeFunction function)
    {
        var code = function.Code;
        if (code.Heights.Length != code.Code.Length || code.Pops.Length != code.Code.Length || code.Code.Length == 0)
            return null;

        return new IlCompiler(code, function.Instance.Module.Info).CompileMethod() as JitFunction;
    }

    /// <summary>Compiles the method: a <see cref="JitFunction"/>, or a <see cref="JitOsrFunction"/> for an OSR compiler, or null.</summary>
    private Delegate? CompileMethod()
    {
        if (!Analyze())
            return null;

        // A typed method takes the parameters after the arguments of a JitFunction, and returns its result (or 0)
        // The method of an OSR entry takes the index of its loop (see CompileOsr)
        // The parameters of a typed method are before the frame pointer and the depth, so that the first ones are registers
        Type[] parameterTypes = _typed
            ? [typeof(JitClosure), typeof(Machine), typeof(ManagedInstance), .. Enumerable.Repeat(typeof(long), _code.ParameterCount), typeof(int), typeof(int)]
            : [typeof(JitClosure), typeof(Machine), typeof(ManagedInstance), typeof(int), typeof(int)];
        if (_osr)
        {
            parameterTypes = [.. parameterTypes, typeof(int)];
        }

        _method = new DynamicMethod("wasm$" + _code.Name + (_osr ? "$osr" : ""), _typed ? typeof(long) : typeof(void), parameterTypes, typeof(IlCompiler).Module, skipVisibility: true);
        _il = _method.GetILGenerator();
        DeclareLocals();
        EmitPrologue();

        if (_code.Handlers.Length > 0)
        {
            // The handlers of the exceptions: the code is in a try block; the catch block gives the values of the
            // clause to its label, and enters the try block again at the label (the switch of the start of the block)
            _entry = _il.DeclareLocal(typeof(int));
            if (_osr)
            {
                // The entries of the switch: the start, the clauses, then the loops
                _il.Emit(OpCodes.Ldarg_S, (byte)5);
                EmitInt(1 + _clauses.Count);
                _il.Emit(OpCodes.Add);
                _il.Emit(OpCodes.Stloc, _entry);
            }

            var top = _il.DefineLabel();
            _il.MarkLabel(top);
            EmitReloadMemories();
            _returnLabel = _il.BeginExceptionBlock();
            _inTry = true;
            _il.Emit(OpCodes.Ldloc, _entry);
            var clauseLabels = new Label[_clauses.Count];
            for (var i = 0; i < _clauses.Count; i++)
            {
                clauseLabels[i] = _labels[_clauses[i].Clause.Pc];
            }

            var start = _il.DefineLabel();
            var loopLabels = DefineLoopEntries();
            _il.Emit(OpCodes.Switch, [start, .. clauseLabels, .. loopLabels]);
            _il.Emit(OpCodes.Br, start);
            EmitLoopEntries(loopLabels);
            _il.MarkLabel(start);
            if (!EmitBody())
                return null;

            _il.BeginCatchBlock(typeof(WasmThrowException));
            EmitCatch(top);
            _il.EndExceptionBlock();
            EmitReturn(_result);
        }
        else
        {
            if (_osr)
            {
                var loopLabels = DefineLoopEntries();
                _il.Emit(OpCodes.Ldarg_S, (byte)5);
                _il.Emit(OpCodes.Switch, loopLabels);
                EmitLoopEntries(loopLabels);
            }

            if (!EmitBody())
                return null;
        }

        var closure = new JitClosure { Code = _code, Instrs = _instrs, Callees = [.. _callees], Types = [.. _types] };
        if (_osr)
            return _method.CreateDelegate(typeof(JitOsrFunction), closure);

        if (!_typed)
            return (JitFunction)_method.CreateDelegate(typeof(JitFunction), closure);

        // The machine calls the entry, which reads the parameters from the stack and writes the result to it. The typed
        // method is published once the code ran (see Machine.CallCompiled): the JIT of .NET compiled it without errors
        var invoke = TypedInvokes[_code.ParameterCount];
        _code.PendingTyped = _method.CreateDelegate(invoke.DeclaringType!, closure);
        return CreateEntry(closure);
    }

    /// <summary>Creates the method that calls the typed method with the parameters of the stack, and writes its result to the stack.</summary>
    private JitFunction CreateEntry(JitClosure closure)
    {
        var entry = new DynamicMethod("wasm$" + _code.Name + "$entry", typeof(void), [typeof(JitClosure), typeof(Machine), typeof(ManagedInstance), typeof(int), typeof(int)], typeof(IlCompiler).Module, skipVisibility: true);
        var il = entry.GetILGenerator();
        var slots = il.DeclareLocal(typeof(Slot[]));
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, MachineStack);
        il.Emit(OpCodes.Stloc, slots);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldarg_2);
        for (var i = 0; i < _code.ParameterCount; i++)
        {
            il.Emit(OpCodes.Ldloc, slots);
            il.Emit(OpCodes.Ldarg_3);
            il.Emit(OpCodes.Ldc_I4, i);
            il.Emit(OpCodes.Add);
            il.Emit(OpCodes.Ldelema, typeof(Slot));
            il.Emit(OpCodes.Ldfld, SlotA);
        }

        il.Emit(OpCodes.Ldarg_3);
        il.Emit(OpCodes.Ldarg_S, (byte)4);
        il.Emit(OpCodes.Call, _method);
        if (_code.Results.Length == 0)
        {
            il.Emit(OpCodes.Pop);
        }
        else
        {
            // The stack can be larger than when the method started
            var result = il.DeclareLocal(typeof(long));
            il.Emit(OpCodes.Stloc, result);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Ldarg_3);
            il.Emit(OpCodes.Ldc_I4_1);
            il.Emit(OpCodes.Add);
            il.Emit(OpCodes.Call, MachineJitStack);
            il.Emit(OpCodes.Ldarg_3);
            il.Emit(OpCodes.Ldelema, typeof(Slot));
            il.Emit(OpCodes.Ldloc, result);
            il.Emit(OpCodes.Stfld, SlotA);
        }

        il.Emit(OpCodes.Ret);
        return (JitFunction)entry.CreateDelegate(typeof(JitFunction), closure);
    }

    /// <summary>Returns: a typed method returns <paramref name="result"/>, or 0 when it is null.</summary>
    private void EmitReturn(LocalBuilder? result)
    {
        if (_typed)
        {
            if (result is null)
            {
                _il.Emit(OpCodes.Ldc_I8, 0L);
            }
            else
            {
                _il.Emit(OpCodes.Ldloc, result);
            }
        }

        _il.Emit(OpCodes.Ret);
    }

    // ------------------------------------------------------------------------------------------------------------
    // Analysis
    // ------------------------------------------------------------------------------------------------------------

    /// <summary>Finds the reachable instructions, the targets of the branches, and the kinds of values the function uses.</summary>
    private bool Analyze()
    {
        var count = _instrs.Length;
        var pending = new Stack<int>();
        void AddTarget(int target)
        {
            if ((uint)target >= (uint)count)
                throw new InvalidOperationException("The target of a branch is out of the code");

            _isTarget[target] = true;
            if (!_reachable[target])
            {
                _reachable[target] = true;
                pending.Push(target);
            }
        }

        void AddNext(int pc)
        {
            if (pc < count && !_reachable[pc])
            {
                _reachable[pc] = true;
                pending.Push(pc);
            }
        }

        var clauseIndex = 0;
        foreach (var handler in _code.Handlers)
        {
            foreach (var clause in handler.Catches)
            {
                _clauses.Add((clause, clauseIndex++));
                AddTarget(clause.Pc);
            }
        }

        _reachable[0] = true;
        pending.Push(0);
        while (pending.Count > 0)
        {
            var pc = pending.Pop();
            ref readonly var ins = ref _instrs[pc];
            switch (ins.Op)
            {
                case Op.ReturnCall or Op.ReturnCallIndirect or Op.ReturnCallRef:
                    // The tail calls do not use the stack of .NET in the interpreter only
                    return false;

                case Op.Unreachable or Op.Return or Op.End or Op.Throw or Op.ThrowRef:
                    break;

                case Op.Jump or Op.BrCopy:
                    AddTarget(ins.I0);
                    break;

                case Op.JumpIfZero or Op.BrIf or Op.BrOnNull or Op.BrOnNonNull or Op.BrOnCast or Op.BrOnCastFail:
                    AddTarget(ins.I0);
                    AddNext(pc + 1);
                    break;

                case Op.BrTable:
                    for (var i = 0; i < ins.I1; i++)
                    {
                        AddTarget(_code.BranchTables[ins.I0 + i].Pc);
                    }

                    break;

                default:
                    AddNext(pc + 1);
                    break;
            }
        }

        foreach (var local in _code.Locals)
        {
            Use(local);
        }

        foreach (var result in _code.Results)
        {
            Use(result);
        }

        if (_code.Handlers.Length > 0)
        {
            _hasRefs = true;
        }

        for (var pc = 0; pc < count; pc++)
        {
            if (!_reachable[pc])
                continue;

            ref readonly var ins = ref _instrs[pc];
            if (_heights[pc] < 0 || _pops[pc] < 0 || _pops[pc] > _heights[pc])
                return false;

            switch (ins.Op)
            {
                case Op.Call:
                    UseFunctionType(_module.Functions[ins.I0]);
                    break;

                case Op.CallIndirect or Op.CallRef:
                    _hasRefs = true;
                    if (ins.O is DefinedType type)
                    {
                        UseFunctionType(type);
                    }

                    break;

                case Op.GlobalGet or Op.GlobalSet:
                    Use(_module.Globals[ins.I0].Type);
                    break;

                case Op.Throw:
                    _hasRefs = true;
                    UseFunctionType(_module.Tags[ins.I0]);
                    break;

                case Op.ThrowRef or Op.RefNull or Op.RefIsNull or Op.RefFunc or Op.RefEq or Op.RefAsNonNull or Op.BrOnNull or Op.BrOnNonNull or Op.TableGet or Op.TableSet or Op.TableGrow or Op.TableFill:
                    _hasRefs = true;
                    break;

                default:
                    if (ins.Op is >= Op.GcPrefix and < Op.MiscPrefix)
                    {
                        _hasRefs = true;
                    }
                    else if (Op.IsSimd(ins.Op))
                    {
                        _hasV128 = true;
                    }

                    break;
            }
        }

        var parts = 1 + (_hasV128 ? 1 : 0) + (_hasRefs ? 1 : 0);
        _methodLocals = (_code.Locals.Length + _code.MaxStackHeight) * parts;
        AnalyzeControl();
        return _methodLocals <= MaxMethodLocals;
    }

    /// <summary>
    /// Finds whether the function makes calls, and whether it has loops (the backward branches). A function without calls,
    /// loops and handlers is a leaf: its frame is the last one, and it ends after a bounded number of instructions. Its
    /// callers checked the interruption and the stack, so its small frame does not check them again.
    /// </summary>
    private void AnalyzeControl()
    {
        for (var pc = 0; pc < _instrs.Length; pc++)
        {
            if (!_reachable[pc])
                continue;

            ref readonly var ins = ref _instrs[pc];
            switch (ins.Op)
            {
                case Op.Call or Op.CallIndirect or Op.CallRef or Op.ReturnCall or Op.ReturnCallIndirect or Op.ReturnCallRef:
                    _makesCalls = true;
                    break;

                case Op.Jump or Op.BrCopy or Op.JumpIfZero or Op.BrIf or Op.BrOnNull or Op.BrOnNonNull or Op.BrOnCast or Op.BrOnCastFail:
                    AddLoop(ins.I0, pc);
                    break;

                case Op.BrTable:
                    for (var i = 0; i < ins.I1; i++)
                    {
                        AddLoop(_code.BranchTables[ins.I0 + i].Pc, pc);
                    }

                    break;
            }
        }
    }

    private void Use(ValType type)
    {
        if (type.Code == ValueCode.V128)
        {
            _hasV128 = true;
        }
        else if (type.IsRef)
        {
            _hasRefs = true;
        }
    }

    private void UseFunctionType(DefinedType type)
    {
        foreach (var parameter in type.Parameters)
        {
            Use(parameter);
        }

        foreach (var result in type.Results)
        {
            Use(result);
        }
    }

    // ------------------------------------------------------------------------------------------------------------
    // Locals
    // ------------------------------------------------------------------------------------------------------------

    private void DeclareLocals()
    {
        var locals = _code.Locals;
        _localA = new LocalBuilder[locals.Length];
        _localB = new LocalBuilder?[locals.Length];
        _localR = new LocalBuilder?[locals.Length];
        for (var i = 0; i < locals.Length; i++)
        {
            _localA[i] = _il.DeclareLocal(typeof(long));
            if (locals[i].Code == ValueCode.V128)
            {
                _localB[i] = _il.DeclareLocal(typeof(long));
            }
            else if (locals[i].IsRef)
            {
                _localR[i] = _il.DeclareLocal(typeof(object));
            }
        }

        var height = _code.MaxStackHeight;
        _stackA = new LocalBuilder[height];
        _stackB = _hasV128 ? new LocalBuilder[height] : null;
        _stackR = _hasRefs ? new LocalBuilder[height] : null;
        for (var h = 0; h < height; h++)
        {
            _stackA[h] = _il.DeclareLocal(typeof(long));
            if (_stackB is not null)
            {
                _stackB[h] = _il.DeclareLocal(typeof(long));
            }

            if (_stackR is not null)
            {
                _stackR[h] = _il.DeclareLocal(typeof(object));
            }
        }

        _scratch = _il.DeclareLocal(typeof(int));
        _probe = _il.DeclareLocal(typeof(int));
        _slots = _il.DeclareLocal(typeof(Slot[]));
        _callee = _il.DeclareLocal(typeof(ManagedFunction));
        _codeCallee = _il.DeclareLocal(typeof(CodeFunction));
        _address = _il.DeclareLocal(typeof(ulong));
        _effectiveAddress = _il.DeclareLocal(typeof(ulong));
        if (_code.Handlers.Length > 0)
        {
            _pc = _il.DeclareLocal(typeof(int));
            if (_typed && _code.Results.Length == 1)
            {
                _result = _il.DeclareLocal(typeof(long));
            }
        }

        for (var pc = 0; pc < _instrs.Length; pc++)
        {
            if (_reachable[pc])
            {
                _labels[pc] = _il.DefineLabel();
            }
        }

        DeclareFloatLocals();
    }

    private LocalBuilder GetMemory(int index) => GetCached(_memories, index, typeof(ManagedMemory));

    private LocalBuilder GetGlobal(int index) => GetCached(_globals, index, typeof(ManagedGlobal));

    private LocalBuilder GetTable(int index) => GetCached(_tables, index, typeof(ManagedTable));

    private LocalBuilder GetCached(Dictionary<int, LocalBuilder> cache, int index, Type type)
    {
        if (!cache.TryGetValue(index, out var local))
        {
            local = _il.DeclareLocal(type);
            cache.Add(index, local);
        }

        return local;
    }

    private void EmitPrologue()
    {
        RegisterInstanceObjects();
        LoadInstanceObjects(_memories, InstanceMemories);
        LoadInstanceObjects(_globals, InstanceGlobals);
        LoadInstanceObjects(_tables, InstanceTables);
        foreach (var (index, import) in _imports)
        {
            _il.Emit(OpCodes.Ldarg_2);
            _il.Emit(OpCodes.Call, InstanceFunctions);
            EmitInt(index);
            _il.Emit(OpCodes.Ldelem_Ref);
            _il.Emit(OpCodes.Stloc, import.Function);
            EmitReadImportEntry(import);
        }

        EmitReloadMemories();

        EmitEntryChecks();

        // The parameters: the arguments of a typed method, or the slots at the frame pointer
        if (_typed)
        {
            for (var i = 0; i < _code.ParameterCount; i++)
            {
                EmitLoadArgument(3 + i);
                _il.Emit(OpCodes.Stloc, _localA[i]);
            }
        }
        else
        {
            // An OSR entry reads all the locals of the interpreted frame
            _il.Emit(OpCodes.Ldarg_1);
            _il.Emit(OpCodes.Call, MachineStack);
            _il.Emit(OpCodes.Stloc, _slots);
            for (var i = 0; i < (_osr ? _code.Locals.Length : _code.ParameterCount); i++)
            {
                EmitSlotAddress(_slots, baseLocal: null, i);
                EmitLoadSlot(_localA[i], _localB[i], _localR[i]);
            }
        }

        EmitFloatParameters(_osr ? _code.Locals.Length : _code.ParameterCount);

        // The scratch slots of an OSR entry are after the interpreted frame (its operands are read by the loop entries)
        EmitLoadArgument(FpArgument);
        EmitInt(_osr ? _code.FrameSize : _code.ParameterCount);
        _il.Emit(OpCodes.Add);
        _il.Emit(OpCodes.Stloc, _scratch);
    }

    /// <summary>Checks the depth of the frame, the stack of .NET and the interruption (a leaf function does not, see <see cref="AnalyzeControl"/>).</summary>
    private void EmitEntryChecks()
    {
        // A leaf function (see AnalyzeControl)
        var small = _methodLocals <= MaxSmallFrameLocals;
        if (small && !_makesCalls && !_hasLoops && _code.Handlers.Length == 0)
            return;

        // The depth of the frame (an argument): the deeper frames trap
        var depthChecked = _il.DefineLabel();
        EmitLoadArgument(DepthArgument);
        _il.Emit(OpCodes.Ldarg_1);
        _il.Emit(OpCodes.Ldfld, MachineJitDepthLimit);
        _il.Emit(OpCodes.Ble, depthChecked);
        _il.Emit(OpCodes.Call, MachineJitStackOverflow);
        _il.Emit(OpCodes.Throw);
        _il.MarkLabel(depthChecked);

        // The stack of .NET: a small frame compares its address to the bound of the machine, the other frames check it.
        // The address of a local slows the loops down: a function with loops checks the stack (it runs longer than the check)
        var stackChecked = _il.DefineLabel();
        if (small && !_hasLoops)
        {
            _il.Emit(OpCodes.Ldloca, _probe);
            _il.Emit(OpCodes.Conv_U);
            _il.Emit(OpCodes.Ldarg_1);
            _il.Emit(OpCodes.Ldfld, MachineJitStackBound);
            _il.Emit(OpCodes.Bge_Un, stackChecked);
        }

        _il.Emit(OpCodes.Ldarg_1);
        _il.Emit(OpCodes.Call, MachineJitCheckStack);
        _il.MarkLabel(stackChecked);

        EmitCheckInterrupt();
    }

    /// <summary>Declares the locals of the objects of the instance that the code uses: they are loaded once (the instructions refer to them by index).</summary>
    private void RegisterInstanceObjects()
    {
        for (var pc = 0; pc < _instrs.Length; pc++)
        {
            if (!_reachable[pc])
                continue;

            ref readonly var ins = ref _instrs[pc];
            if (Loads.ContainsKey(ins.Op) || Stores.ContainsKey(ins.Op) || ins.Op is Op.MemorySize or Op.MemoryGrow)
            {
                GetMemory(ins.I0);
                if (InlineAccesses.ContainsKey(ins.Op) && _module.Memories[ins.I0] is { Is64: false, Shared: false } && !_inlineMemories.ContainsKey(ins.I0))
                {
                    _inlineMemories.Add(ins.I0, (_il.DeclareLocal(typeof(nint)), _il.DeclareLocal(typeof(ulong))));
                }
            }
            else if (ins.Op is Op.GlobalGet or Op.GlobalSet)
            {
                GetGlobal(ins.I0);
            }
            else if (ins.Op is Op.TableGet or Op.TableSet or Op.CallIndirect)
            {
                GetTable(ins.I0);
            }
            else if (ins.Op == Op.Call && ins.I0 < _module.ImportedFunctionCount && _module.Functions[ins.I0] is { } importType && IsTyped(importType.Parameters, importType.Results) && !_imports.ContainsKey(ins.I0))
            {
                // An imported function is the same for the instance: the code reads it and its typed entry once
                _imports.Add(ins.I0, (_il.DeclareLocal(typeof(ManagedFunction)), _il.DeclareLocal(TypedInvokes[importType.Parameters.Length].DeclaringType!), _il.DeclareLocal(typeof(ManagedInstance))));
            }
        }
    }

    private void LoadInstanceObjects(Dictionary<int, LocalBuilder> locals, MethodInfo getter)
    {
        foreach (var (index, local) in locals)
        {
            _il.Emit(OpCodes.Ldarg_2);
            _il.Emit(OpCodes.Call, getter);
            EmitInt(index);
            _il.Emit(OpCodes.Ldelem_Ref);
            _il.Emit(OpCodes.Stloc, local);
        }
    }

    // ------------------------------------------------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------------------------------------------------

    private void EmitInt(int value) => _il.Emit(OpCodes.Ldc_I4, value);

    // The arguments of the method: a typed method has its parameters after the instance (see CompileMethod)
    private int FpArgument => _typed ? 3 + _code.ParameterCount : 3;

    private int DepthArgument => FpArgument + 1;

    private void EmitLoadArgument(int index)
    {
        // The code of an inlined function runs in the method of its caller: it does not read the arguments
        if (_isInline)
            throw new InvalidOperationException("The code of an inlined function cannot read the arguments of the method");

        _il.Emit(OpCodes.Ldarg_S, (byte)index);
    }

    private void EmitLoadInstr(int pc)
    {
        _il.Emit(OpCodes.Ldarg_0);
        _il.Emit(OpCodes.Ldfld, ClosureInstrs);
        EmitInt(pc);
        _il.Emit(OpCodes.Ldelema, typeof(Instr));
    }

    /// <summary>Pushes the address of the slot <c>base + offset</c> of an array of slots: the base is a local, or the frame pointer when it is null.</summary>
    private void EmitSlotAddress(LocalBuilder slots, LocalBuilder? baseLocal, int offset)
    {
        _il.Emit(OpCodes.Ldloc, slots);
        if (baseLocal is null)
        {
            EmitLoadArgument(FpArgument);
        }
        else
        {
            _il.Emit(OpCodes.Ldloc, baseLocal);
        }

        if (offset != 0)
        {
            EmitInt(offset);
            _il.Emit(OpCodes.Add);
        }

        _il.Emit(OpCodes.Ldelema, typeof(Slot));
    }

    /// <summary>Stores the parts of the slot whose address is on the stack into locals.</summary>
    private void EmitLoadSlot(LocalBuilder a, LocalBuilder? b, LocalBuilder? r)
    {
        if (b is not null)
        {
            _il.Emit(OpCodes.Dup);
            _il.Emit(OpCodes.Ldfld, SlotB);
            _il.Emit(OpCodes.Stloc, b);
        }

        if (r is not null)
        {
            _il.Emit(OpCodes.Dup);
            _il.Emit(OpCodes.Ldfld, SlotR);
            _il.Emit(OpCodes.Stloc, r);
        }

        _il.Emit(OpCodes.Ldfld, SlotA);
        _il.Emit(OpCodes.Stloc, a);
    }

    /// <summary>Stores locals into the parts of the slot whose address is on the stack.</summary>
    private void EmitStoreSlot(LocalBuilder a, LocalBuilder? b, LocalBuilder? r)
    {
        if (b is not null)
        {
            _il.Emit(OpCodes.Dup);
            _il.Emit(OpCodes.Ldloc, b);
            _il.Emit(OpCodes.Stfld, SlotB);
        }

        if (r is not null)
        {
            _il.Emit(OpCodes.Dup);
            _il.Emit(OpCodes.Ldloc, r);
            _il.Emit(OpCodes.Stfld, SlotR);
        }

        _il.Emit(OpCodes.Ldloc, a);
        _il.Emit(OpCodes.Stfld, SlotA);
    }

    private LocalBuilder? StackB(int height) => _stackB?[height];

    private LocalBuilder? StackR(int height) => _stackR?[height];

    private void Copy(LocalBuilder? source, LocalBuilder? destination)
    {
        if (source is not null && destination is not null && source != destination)
        {
            _il.Emit(OpCodes.Ldloc, source);
            _il.Emit(OpCodes.Stloc, destination);
        }
    }

    /// <summary>Copies the operand at <paramref name="source"/> to <paramref name="destination"/>.</summary>
    private void CopyOperand(int source, int destination)
    {
        if (source == destination)
            return;

        Copy(_stackA[source], _stackA[destination]);
        Copy(StackB(source), StackB(destination));
        Copy(StackR(source), StackR(destination));
    }

    /// <summary>Copies the <paramref name="count"/> operands at <paramref name="source"/> to <paramref name="destination"/> (the values of a branch).</summary>
    private void CopyOperands(int source, int destination, int count)
    {
        for (var i = 0; i < count; i++)
        {
            CopyOperand(source + i, destination + i);
        }
    }

    /// <summary>Writes the operands to the stack of the machine, at the scratch slots.</summary>
    private void Spill(int height, int count, int size)
    {
        _il.Emit(OpCodes.Ldarg_1);
        _il.Emit(OpCodes.Ldloc, _scratch);
        EmitInt(Math.Max(size, 1));
        _il.Emit(OpCodes.Add);
        _il.Emit(OpCodes.Call, MachineJitStack);
        _il.Emit(OpCodes.Stloc, _slots);
        for (var i = 0; i < count; i++)
        {
            EmitSlotAddress(_slots, _scratch, i);
            EmitStoreSlot(_stackA[height + i], StackB(height + i), StackR(height + i));
        }
    }

    /// <summary>Reads the operands from the scratch slots of the stack of the machine.</summary>
    private void Unspill(int height, int count)
    {
        if (count == 0)
            return;

        _il.Emit(OpCodes.Ldarg_1);
        _il.Emit(OpCodes.Call, MachineStack);
        _il.Emit(OpCodes.Stloc, _slots);
        for (var i = 0; i < count; i++)
        {
            EmitSlotAddress(_slots, _scratch, i);
            EmitLoadSlot(_stackA[height + i], StackB(height + i), StackR(height + i));
        }
    }

    /// <summary>
    /// Reads again the address and the size of the memories whose accesses are inline (see <see cref="InlineAccesses"/>):
    /// at the start of the function, and after the code that can grow them (the calls, the instructions of the machine,
    /// <c>memory.grow</c>, the interruption action, the handlers of the exceptions). Returns true.
    /// </summary>
    private bool EmitReloadMemories()
    {
        foreach (var (index, (baseLocal, length)) in _inlineMemories)
        {
            var memory = GetMemory(index);
            _il.Emit(OpCodes.Ldloc, memory);
            _il.Emit(OpCodes.Call, MemoryBase);
            _il.Emit(OpCodes.Stloc, baseLocal);
            _il.Emit(OpCodes.Ldloc, memory);
            _il.Emit(OpCodes.Call, MemoryUnsharedByteLength);
            _il.Emit(OpCodes.Stloc, length);
        }

        return true;
    }

    /// <summary>
    /// A load or a store of a memory that is not shared and has 32-bit addresses: the effective address is the address
    /// (32 bits) plus the offset (32 bits), so the end of the access cannot overflow 64 bits.
    /// </summary>
    private void EmitInlineAccess(in Instr ins, int h, (LocalBuilder Base, LocalBuilder Length) memory, (int Size, OpCode Access, OpCode Convert) access)
    {
        var isLoad = Loads.ContainsKey(ins.Op);
        EmitInlineAddress(in ins, isLoad ? _stackA[h - 1] : _stackA[h - 2], memory, access.Size);
        if (isLoad)
        {
            if (access.Size > 1)
            {
                _il.Emit(OpCodes.Unaligned, (byte)1);
            }

            _il.Emit(access.Access);
            if (access.Convert != OpCodes.Nop)
            {
                _il.Emit(access.Convert);
            }

            _il.Emit(OpCodes.Stloc, _stackA[h - 1]);
        }
        else
        {
            _il.Emit(OpCodes.Ldloc, _stackA[h - 1]);
            if (access.Convert != OpCodes.Nop)
            {
                _il.Emit(access.Convert);
            }

            if (access.Size > 1)
            {
                _il.Emit(OpCodes.Unaligned, (byte)1);
            }

            _il.Emit(access.Access);
        }
    }

    /// <summary>Checks the bounds of an access of <paramref name="size"/> bytes at the address of a slot, and pushes the address of its first byte.</summary>
    private void EmitInlineAddress(in Instr ins, LocalBuilder address, (LocalBuilder Base, LocalBuilder Length) memory, int size)
    {
        _il.Emit(OpCodes.Ldloc, address);
        _il.Emit(OpCodes.Conv_U4);
        _il.Emit(OpCodes.Conv_U8);
        if (ins.L0 != 0)
        {
            _il.Emit(OpCodes.Ldc_I8, ins.L0);
            _il.Emit(OpCodes.Add);
        }

        _il.Emit(OpCodes.Stloc, _effectiveAddress);
        var inBounds = _il.DefineLabel();
        _il.Emit(OpCodes.Ldloc, _effectiveAddress);
        _il.Emit(OpCodes.Ldc_I8, (long)size);
        _il.Emit(OpCodes.Add);
        _il.Emit(OpCodes.Ldloc, memory.Length);
        _il.Emit(OpCodes.Ble_Un, inBounds);
        _il.Emit(OpCodes.Call, GetJitOp(nameof(JitOps.OutOfBoundsMemoryTrap)));
        _il.Emit(OpCodes.Throw);
        _il.MarkLabel(inBounds);

        _il.Emit(OpCodes.Ldloc, memory.Base);
        _il.Emit(OpCodes.Ldloc, _effectiveAddress);
        _il.Emit(OpCodes.Conv_U);
        _il.Emit(OpCodes.Add);
    }

    /// <summary>Calls the interruption action when the timer asked for it (see <see cref="Machine.CheckInterrupt"/>): the action is host code, which can grow the memories.</summary>
    private void EmitCheckInterrupt()
    {
        var skip = _il.DefineLabel();
        _il.Emit(OpCodes.Ldarg_1);
        _il.Emit(OpCodes.Volatile);
        _il.Emit(OpCodes.Ldfld, MachineInterruptRequested);
        _il.Emit(OpCodes.Brfalse, skip);
        _il.Emit(OpCodes.Ldarg_1);
        _il.Emit(OpCodes.Call, MachineCheckInterruptSlow);
        EmitReloadMemories();
        _il.MarkLabel(skip);
    }

    /// <summary>Jumps to an instruction, after copying the values of the branch.</summary>
    private void EmitBranch(int pc, int target, int source, int destination, int arity)
    {
        CopyOperands(source, destination, arity);
        if (target <= pc)
        {
            EmitCheckInterrupt();
        }

        _il.Emit(OpCodes.Br, _labels[target]);
    }

    private void SetPc(int pc)
    {
        if (_pc is not null)
        {
            EmitInt(pc);
            _il.Emit(OpCodes.Stloc, _pc);
        }
    }

    // ------------------------------------------------------------------------------------------------------------
    // The instructions
    // ------------------------------------------------------------------------------------------------------------

    private bool EmitBody()
    {
        for (var pc = 0; pc < _instrs.Length; pc++)
        {
            if (!_reachable[pc])
                continue;

            if (_isTarget[pc])
            {
                // The branches to the instruction give it the bits of the operands
                MaterializeAll(_heights[pc]);
                SetBits(_heights[pc], _kinds.Length);
            }

            _il.MarkLabel(_labels[pc]);
            if (!EmitInstruction(pc))
                return false;
        }

        return true;
    }

#pragma warning disable MA0051 // Method is too long: one case per instruction
    private bool EmitInstruction(int pc)
    {
        ref readonly var ins = ref _instrs[pc];
        var h = _heights[pc];
        var op = ins.Op;
        if (TryEmitFloat(in ins, h))
            return true;

        // The other instructions read and write the bits of their operands
        var pops = _pops[pc];
        if (ReadsAllOperands(op))
        {
            MaterializeAll(h);
        }
        else
        {
            MaterializeRange(h - pops, h);
        }

        SetBits(h - pops, pc + 1 < _instrs.Length ? Math.Max(_heights[pc + 1], h) : h);
        if (UnaryOperations.TryGetValue(op, out var unary))
        {
            _il.Emit(OpCodes.Ldloc, _stackA[h - 1]);
            _il.Emit(OpCodes.Call, unary);
            _il.Emit(OpCodes.Stloc, _stackA[h - 1]);
            return true;
        }

        if (BinaryOperations.TryGetValue(op, out var binary))
        {
            _il.Emit(OpCodes.Ldloc, _stackA[h - 2]);
            _il.Emit(OpCodes.Ldloc, _stackA[h - 1]);
            _il.Emit(OpCodes.Call, binary);
            _il.Emit(OpCodes.Stloc, _stackA[h - 2]);
            return true;
        }

        if (_inlineMemories.TryGetValue(ins.I0, out var inlineMemory) && InlineAccesses.TryGetValue(op, out var access))
        {
            EmitInlineAccess(in ins, h, inlineMemory, access);
            return true;
        }

        if (Loads.TryGetValue(op, out var load))
        {
            _il.Emit(OpCodes.Ldloc, GetMemory(ins.I0));
            _il.Emit(OpCodes.Ldloc, _stackA[h - 1]);
            _il.Emit(OpCodes.Ldc_I8, ins.L0);
            _il.Emit(OpCodes.Call, load);
            _il.Emit(OpCodes.Stloc, _stackA[h - 1]);
            return true;
        }

        if (Stores.TryGetValue(op, out var store))
        {
            _il.Emit(OpCodes.Ldloc, GetMemory(ins.I0));
            _il.Emit(OpCodes.Ldloc, _stackA[h - 2]);
            _il.Emit(OpCodes.Ldc_I8, ins.L0);
            _il.Emit(OpCodes.Ldloc, _stackA[h - 1]);
            _il.Emit(OpCodes.Call, store);
            return true;
        }

        switch (op)
        {
            case Op.Nop or Op.Drop or Op.AnyConvertExtern or Op.ExternConvertAny:
                return true;

            case Op.Unreachable:
                _il.Emit(OpCodes.Call, GetJitOp(nameof(JitOps.UnreachableTrap)));
                _il.Emit(OpCodes.Throw);
                return true;

            case Op.I32Const or Op.I64Const or Op.F32Const or Op.F64Const:
                _il.Emit(OpCodes.Ldc_I8, ins.L0);
                _il.Emit(OpCodes.Stloc, _stackA[h]);
                return true;

            // Control
            case Op.Jump:
                EmitBranch(pc, ins.I0, 0, 0, 0);
                return true;

            case Op.BrCopy:
                EmitBranch(pc, ins.I0, h - ins.I1, ins.I2, ins.I1);
                return true;

            case Op.JumpIfZero:
            {
                _il.Emit(OpCodes.Ldloc, _stackA[h - 1]);
                _il.Emit(OpCodes.Conv_I4);
                if (ins.I0 > pc)
                {
                    _il.Emit(OpCodes.Brfalse, _labels[ins.I0]);
                }
                else
                {
                    var skip = _il.DefineLabel();
                    _il.Emit(OpCodes.Brtrue, skip);
                    EmitBranch(pc, ins.I0, 0, 0, 0);
                    _il.MarkLabel(skip);
                }

                return true;
            }

            case Op.BrIf:
            {
                _il.Emit(OpCodes.Ldloc, _stackA[h - 1]);
                _il.Emit(OpCodes.Conv_I4);
                if (ins.L0 == 0 && ins.I0 > pc)
                {
                    _il.Emit(OpCodes.Brtrue, _labels[ins.I0]);
                }
                else
                {
                    var skip = _il.DefineLabel();
                    _il.Emit(OpCodes.Brfalse, skip);
                    EmitBranch(pc, ins.I0, h - 1 - ins.I1, ins.I2, ins.L0 != 0 ? ins.I1 : 0);
                    _il.MarkLabel(skip);
                }

                return true;
            }

            case Op.BrTable:
            {
                var count = ins.I1;
                var stubs = new Label[count];
                for (var i = 0; i < count; i++)
                {
                    stubs[i] = _il.DefineLabel();
                }

                _il.Emit(OpCodes.Ldloc, _stackA[h - 1]);
                _il.Emit(OpCodes.Conv_I4);
                _il.Emit(OpCodes.Switch, stubs[..^1]);
                _il.Emit(OpCodes.Br, stubs[^1]);
                for (var i = 0; i < count; i++)
                {
                    var target = _code.BranchTables[ins.I0 + i];
                    _il.MarkLabel(stubs[i]);
                    EmitBranch(pc, target.Pc, h - 1 - target.Arity, target.Height, target.Arity);
                }

                return true;
            }

            case Op.Return or Op.End when _typed:
            {
                if (_isInline)
                {
                    EmitInlineReturn(in ins, h);
                    return true;
                }

                var result = ins.I1 == 0 ? null : _stackA[h - 1];
                if (!_inTry)
                {
                    EmitReturn(result);
                    return true;
                }

                if (result is not null)
                {
                    _il.Emit(OpCodes.Ldloc, result);
                    _il.Emit(OpCodes.Stloc, _result!);
                }

                _il.Emit(OpCodes.Leave, _returnLabel);
                return true;
            }

            case Op.Return or Op.End:
            {
                var arity = ins.I1;
                _il.Emit(OpCodes.Ldarg_1);
                EmitLoadArgument(FpArgument);
                EmitInt(Math.Max(arity, 1));
                _il.Emit(OpCodes.Add);
                _il.Emit(OpCodes.Call, MachineJitStack);
                _il.Emit(OpCodes.Stloc, _slots);
                for (var i = 0; i < arity; i++)
                {
                    EmitSlotAddress(_slots, baseLocal: null, i);
                    EmitStoreSlot(_stackA[h - arity + i], StackB(h - arity + i), StackR(h - arity + i));
                }

                if (_inTry)
                {
                    _il.Emit(OpCodes.Leave, _returnLabel);
                }
                else
                {
                    EmitReturn(result: null);
                }

                return true;
            }

            case Op.Call or Op.CallIndirect or Op.CallRef:
                return EmitCall(pc, in ins, h) && (TakeCallInlined() || EmitReloadMemories());

            case Op.Throw:
            {
                var count = _pops[pc];
                SetPc(pc);
                Spill(h - count, count, count);
                _il.Emit(OpCodes.Ldarg_2);
                EmitInt(ins.I0);
                _il.Emit(OpCodes.Ldloc, _slots);
                _il.Emit(OpCodes.Ldloc, _scratch);
                _il.Emit(OpCodes.Call, GetJitOp(nameof(JitOps.Throw)));
                _il.Emit(OpCodes.Throw);
                return true;
            }

            case Op.ThrowRef:
                SetPc(pc);
                _il.Emit(OpCodes.Ldloc, StackR(h - 1)!);
                _il.Emit(OpCodes.Call, GetJitOp(nameof(JitOps.ThrowReference)));
                _il.Emit(OpCodes.Throw);
                return true;

            case Op.Select:
            {
                var skip = _il.DefineLabel();
                _il.Emit(OpCodes.Ldloc, _stackA[h - 1]);
                _il.Emit(OpCodes.Conv_I4);
                _il.Emit(OpCodes.Brtrue, skip);
                CopyOperand(h - 2, h - 3);
                _il.MarkLabel(skip);
                return true;
            }

            // Variables
            case Op.LocalGet:
                Copy(_localA[ins.I0], _stackA[h]);
                Copy(_localB[ins.I0], StackB(h));
                Copy(_localR[ins.I0], StackR(h));
                return true;

            case Op.LocalSet:
            case Op.LocalTee:
                Copy(_stackA[h - 1], _localA[ins.I0]);
                Copy(StackB(h - 1), _localB[ins.I0]);
                Copy(StackR(h - 1), _localR[ins.I0]);
                return true;

            case Op.GlobalGet:
            {
                var type = _module.Globals[ins.I0].Type;
                _il.Emit(OpCodes.Ldloc, GetGlobal(ins.I0));
                _il.Emit(OpCodes.Ldflda, GlobalValue);
                EmitLoadSlot(_stackA[h], type.Code == ValueCode.V128 ? StackB(h) : null, type.IsRef ? StackR(h) : null);
                return true;
            }

            case Op.GlobalSet:
            {
                var type = _module.Globals[ins.I0].Type;
                _il.Emit(OpCodes.Ldloc, GetGlobal(ins.I0));
                _il.Emit(OpCodes.Ldflda, GlobalValue);
                EmitStoreSlot(_stackA[h - 1], type.Code == ValueCode.V128 ? StackB(h - 1) : null, type.IsRef ? StackR(h - 1) : null);
                return true;
            }

            case Op.TableGet:
                _il.Emit(OpCodes.Ldloc, GetTable(ins.I0));
                _il.Emit(OpCodes.Ldloc, _stackA[h - 1]);
                _il.Emit(OpCodes.Call, GetJitOp(nameof(JitOps.TableGet)));
                _il.Emit(OpCodes.Stloc, StackR(h - 1)!);
                return true;

            case Op.TableSet:
                _il.Emit(OpCodes.Ldloc, GetTable(ins.I0));
                _il.Emit(OpCodes.Ldloc, _stackA[h - 2]);
                _il.Emit(OpCodes.Ldloc, StackR(h - 1)!);
                _il.Emit(OpCodes.Call, GetJitOp(nameof(JitOps.TableSet)));
                return true;

            case Op.MemorySize:
                _il.Emit(OpCodes.Ldloc, GetMemory(ins.I0));
                _il.Emit(OpCodes.Call, GetJitOp(nameof(JitOps.MemorySize)));
                _il.Emit(OpCodes.Stloc, _stackA[h]);
                return true;

            case Op.MemoryGrow:
                _il.Emit(OpCodes.Ldloc, GetMemory(ins.I0));
                _il.Emit(OpCodes.Ldloc, _stackA[h - 1]);
                _il.Emit(OpCodes.Call, GetJitOp(nameof(JitOps.MemoryGrow)));
                _il.Emit(OpCodes.Stloc, _stackA[h - 1]);
                return EmitReloadMemories();

            // References
            case Op.RefNull:
                _il.Emit(OpCodes.Ldnull);
                _il.Emit(OpCodes.Stloc, StackR(h)!);
                return true;

            case Op.RefFunc:
                _il.Emit(OpCodes.Ldarg_2);
                _il.Emit(OpCodes.Call, InstanceFunctions);
                EmitInt(ins.I0);
                _il.Emit(OpCodes.Ldelem_Ref);
                _il.Emit(OpCodes.Stloc, StackR(h)!);
                return true;

            case Op.RefIsNull:
                _il.Emit(OpCodes.Ldloc, StackR(h - 1)!);
                _il.Emit(OpCodes.Ldnull);
                _il.Emit(OpCodes.Ceq);
                _il.Emit(OpCodes.Conv_I8);
                _il.Emit(OpCodes.Stloc, _stackA[h - 1]);
                _il.Emit(OpCodes.Ldnull);
                _il.Emit(OpCodes.Stloc, StackR(h - 1)!);
                return true;

            case Op.RefEq:
                _il.Emit(OpCodes.Ldloc, StackR(h - 2)!);
                _il.Emit(OpCodes.Ldloc, StackR(h - 1)!);
                _il.Emit(OpCodes.Call, GetJitOp(nameof(JitOps.RefEq)));
                _il.Emit(OpCodes.Stloc, _stackA[h - 2]);
                _il.Emit(OpCodes.Ldnull);
                _il.Emit(OpCodes.Stloc, StackR(h - 2)!);
                return true;

            case Op.RefAsNonNull:
                _il.Emit(OpCodes.Ldloc, StackR(h - 1)!);
                _il.Emit(OpCodes.Call, GetJitOp(nameof(JitOps.CheckNonNull)));
                return true;

            case Op.BrOnNull:
            {
                var skip = _il.DefineLabel();
                _il.Emit(OpCodes.Ldloc, StackR(h - 1)!);
                _il.Emit(OpCodes.Brtrue, skip);
                EmitBranch(pc, ins.I0, h - 1 - ins.I1, ins.I2, ins.L0 != 0 ? ins.I1 : 0);
                _il.MarkLabel(skip);
                return true;
            }

            case Op.BrOnNonNull:
            {
                var skip = _il.DefineLabel();
                _il.Emit(OpCodes.Ldloc, StackR(h - 1)!);
                _il.Emit(OpCodes.Brfalse, skip);
                EmitBranch(pc, ins.I0, h - ins.I1, ins.I2, ins.L0 != 0 ? ins.I1 : 0);
                _il.MarkLabel(skip);
                return true;
            }

            case Op.BrOnCast or Op.BrOnCastFail:
            {
                var skip = _il.DefineLabel();
                _il.Emit(OpCodes.Ldloc, StackR(h - 1)!);
                EmitLoadInstr(pc);
                _il.Emit(OpCodes.Call, GetJitOp(nameof(JitOps.Matches)));
                _il.Emit(op == Op.BrOnCast ? OpCodes.Brfalse : OpCodes.Brtrue, skip);
                EmitBranch(pc, ins.I0, h - ins.I1, ins.I2, ins.L0 != 0 ? ins.I1 : 0);
                _il.MarkLabel(skip);
                return true;
            }

            case Op.LocalCopy:
                return false;

            default:
                return EmitFallback(pc, h) && EmitReloadMemories();
        }
    }
#pragma warning restore MA0051

    private bool EmitCall(int pc, in Instr ins, int h)
    {
        if (pc + 1 >= _instrs.Length)
            return false;

        var pops = _pops[pc];
        var parameters = ins.Op == Op.Call ? pops : pops - 1;
        var bottom = h - pops;
        var results = _heights[pc + 1] - bottom;
        if (parameters < 0 || results < 0)
            return false;

        SetPc(pc);
        var done = _il.DefineLabel();
        var staticCallee = ins.Op == Op.Call && ins.I0 >= _module.ImportedFunctionCount;
        (LocalBuilder Function, LocalBuilder Typed, LocalBuilder Instance)? import = null;
        if (ins.Op == Op.Call && _imports.TryGetValue(ins.I0, out var cachedImport) && _module.Functions[ins.I0].Parameters.Length == parameters && _module.Functions[ins.I0].Results.Length == results)
        {
            // The typed entry of the import, read when the function started (or after the last call by the machine)
            import = cachedImport;
            var general = _il.DefineLabel();
            _il.Emit(OpCodes.Ldloc, cachedImport.Typed);
            _il.Emit(OpCodes.Brfalse, general);
            _il.Emit(OpCodes.Ldloc, cachedImport.Typed);
            _il.Emit(OpCodes.Ldarg_1);
            _il.Emit(OpCodes.Ldloc, cachedImport.Instance);
            EmitTypedArgumentsAfterInstance(bottom, parameters);
            _il.Emit(OpCodes.Callvirt, TypedInvokes[parameters]);
            EmitTypedResult(bottom, results);
            _il.Emit(OpCodes.Br, done);
            _il.MarkLabel(general);
        }

        if (staticCallee)
        {
            var callee = _module.Functions[ins.I0];
            if (IsTyped(callee.Parameters, callee.Results) && callee.Parameters.Length == parameters && callee.Results.Length == results)
            {
                var calleeCode = _module.Code[ins.I0 - _module.ImportedFunctionCount];
                if (ReferenceEquals(calleeCode, _code))
                {
                    // A recursive call: the method calls itself
                    _il.Emit(OpCodes.Ldarg_0);
                    EmitTypedArguments(bottom, parameters);
                    _il.Emit(OpCodes.Call, _method);
                    EmitTypedResult(bottom, results);
                    return true;
                }

                if (TryEmitInline(calleeCode, bottom, results))
                {
                    _callInlined = true;
                    return true;
                }

                // A function of the module: its typed entry, once it is compiled, or the machine
                var invoke = TypedInvokes[parameters];
                var notCompiled = _il.DefineLabel();
                _il.Emit(OpCodes.Ldarg_0);
                _il.Emit(OpCodes.Ldfld, ClosureCallees);
                EmitInt(_callees.Count);
                _il.Emit(OpCodes.Ldelem_Ref);
                _il.Emit(OpCodes.Ldfld, CodeTyped);
                _il.Emit(OpCodes.Dup);
                _il.Emit(OpCodes.Brfalse, notCompiled);
                _il.Emit(OpCodes.Castclass, invoke.DeclaringType!);
                _callees.Add(calleeCode);
                EmitTypedArguments(bottom, parameters);
                _il.Emit(OpCodes.Callvirt, invoke);
                EmitTypedResult(bottom, results);
                _il.Emit(OpCodes.Br, done);
                _il.MarkLabel(notCompiled);
                _il.Emit(OpCodes.Pop);
            }
        }

        switch (ins.Op)
        {
            case Op.Call:
                _il.Emit(OpCodes.Ldarg_2);
                _il.Emit(OpCodes.Call, InstanceFunctions);
                EmitInt(ins.I0);
                _il.Emit(OpCodes.Ldelem_Ref);
                break;

            case Op.CallIndirect:
            {
                var typeSlot = _types.Count;
                _types.Add((DefinedType)ins.O!);
                if (GetCallType(in ins) is { } indirectType && IsTyped(indirectType.Parameters, indirectType.Results) && indirectType.Parameters.Length == parameters && indirectType.Results.Length == results)
                {
                    EmitTypedIndirectCall(in ins, h, bottom, parameters, results, typeSlot, done);
                }

                _il.Emit(OpCodes.Ldloc, GetTable(ins.I0));
                EmitTableAddress(in ins, h);
                EmitLoadType(typeSlot);
                _il.Emit(OpCodes.Call, GetJitOp(nameof(JitOps.CallIndirectTarget)));
                break;
            }

            default:
                _il.Emit(OpCodes.Ldloc, StackR(h - 1)!);
                _il.Emit(OpCodes.Call, GetJitOp(nameof(JitOps.CallRefTarget)));
                break;
        }

        _il.Emit(OpCodes.Stloc, _callee);
        if (!staticCallee && import is null && GetCallType(in ins) is { } callType && IsTyped(callType.Parameters, callType.Results) && callType.Parameters.Length == parameters && callType.Results.Length == results)
        {
            EmitDynamicTypedCall(bottom, parameters, results, done);
        }

        Spill(bottom, parameters, Math.Max(parameters, results));
        _il.Emit(OpCodes.Ldarg_1);
        _il.Emit(OpCodes.Ldloc, _callee);
        _il.Emit(OpCodes.Ldloc, _scratch);
        _il.Emit(OpCodes.Ldloc, _scratch);
        EmitInt(parameters);
        _il.Emit(OpCodes.Add);
        EmitLoadArgument(DepthArgument);
        _il.Emit(OpCodes.Call, MachineJitCall);
        Unspill(bottom, results);
        if (import is { } calledImport)
        {
            // The machine called the import: its typed entry can be published now
            EmitReadImportEntry(calledImport);
        }

        _il.MarkLabel(done);
        return true;
    }

    /// <summary>Reads the typed entry of an imported function and its instance (see <see cref="ManagedFunction.Typed"/>): null when the function is not compiled yet, or is a host function.</summary>
    private void EmitReadImportEntry((LocalBuilder Function, LocalBuilder Typed, LocalBuilder Instance) import)
    {
        _il.Emit(OpCodes.Ldloc, import.Function);
        _il.Emit(OpCodes.Ldfld, FunctionTyped);
        _il.Emit(OpCodes.Isinst, import.Typed.LocalType);
        _il.Emit(OpCodes.Stloc, import.Typed);
        _il.Emit(OpCodes.Ldloc, import.Function);
        _il.Emit(OpCodes.Ldfld, FunctionTypedInstance);
        _il.Emit(OpCodes.Stloc, import.Instance);
    }

    /// <summary>Pushes the index of an indirect call in its table (an unsigned 64-bit number): the operand of a 32-bit table is the low bits of its slot.</summary>
    private void EmitTableAddress(in Instr ins, int h)
    {
        _il.Emit(OpCodes.Ldloc, _stackA[h - 1]);
        if (!_module.Tables[ins.I0].Is64)
        {
            _il.Emit(OpCodes.Conv_U4);
            _il.Emit(OpCodes.Conv_U8);
        }
    }

    /// <summary>Pushes the type of an indirect call (see <see cref="JitClosure.Types"/>).</summary>
    private void EmitLoadType(int typeSlot)
    {
        _il.Emit(OpCodes.Ldarg_0);
        _il.Emit(OpCodes.Ldfld, ClosureTypes);
        EmitInt(typeSlot);
        _il.Emit(OpCodes.Ldelem_Ref);
    }

    /// <summary>
    /// The indirect call of a compiled function of the expected type, whose typed entry is known (see
    /// <see cref="ManagedFunction.Typed"/>): the code reads the table, compares the type and calls the entry, then jumps to
    /// <paramref name="done"/>. The other cases (an index out of the table, a null element, a host function, a subtype, a
    /// function that is not compiled) continue with the general path, which reports the traps.
    /// </summary>
    private void EmitTypedIndirectCall(in Instr ins, int h, int bottom, int parameters, int results, int typeSlot, Label done)
    {
        var invoke = TypedInvokes[parameters];
        var general = _il.DefineLabel();
        var generalPop = _il.DefineLabel();
        var table = GetTable(ins.I0);
        EmitTableAddress(in ins, h);
        _il.Emit(OpCodes.Stloc, _address);
        _il.Emit(OpCodes.Ldloc, _address);
        _il.Emit(OpCodes.Ldloc, table);
        _il.Emit(OpCodes.Call, TableSize);
        _il.Emit(OpCodes.Bge_Un, general);
        _il.Emit(OpCodes.Ldloc, table);
        _il.Emit(OpCodes.Call, TableElements);
        _il.Emit(OpCodes.Ldloc, _address);
        _il.Emit(OpCodes.Conv_I4);
        _il.Emit(OpCodes.Ldelem_Ref);
        _il.Emit(OpCodes.Isinst, typeof(CodeFunction));
        _il.Emit(OpCodes.Stloc, _codeCallee);
        _il.Emit(OpCodes.Ldloc, _codeCallee);
        _il.Emit(OpCodes.Brfalse, general);
        _il.Emit(OpCodes.Ldloc, _codeCallee);
        _il.Emit(OpCodes.Call, FunctionDefinedType);
        EmitLoadType(typeSlot);
        _il.Emit(OpCodes.Bne_Un, general);
        _il.Emit(OpCodes.Ldloc, _codeCallee);
        _il.Emit(OpCodes.Ldfld, FunctionTyped);
        _il.Emit(OpCodes.Isinst, invoke.DeclaringType!);
        _il.Emit(OpCodes.Dup);
        _il.Emit(OpCodes.Brfalse, generalPop);
        _il.Emit(OpCodes.Ldarg_1);
        _il.Emit(OpCodes.Ldloc, _codeCallee);
        _il.Emit(OpCodes.Call, CodeFunctionInstance);
        EmitTypedArgumentsAfterInstance(bottom, parameters);
        _il.Emit(OpCodes.Callvirt, invoke);
        EmitTypedResult(bottom, results);
        _il.Emit(OpCodes.Br, done);
        _il.MarkLabel(generalPop);
        _il.Emit(OpCodes.Pop);
        _il.MarkLabel(general);
    }

    /// <summary>The type of the function that a call expects: the type of the import, or the type of the indirect call.</summary>
    private DefinedType? GetCallType(in Instr ins) => ins.Op == Op.Call ? _module.Functions[ins.I0] : ins.O as DefinedType;

    /// <summary>
    /// Calls the typed entry of the callee (in <see cref="_callee"/>) when it is a compiled function of a module, and jumps
    /// to <paramref name="done"/>; otherwise, continues with the call by the machine. The callee has the type of the call
    /// (the link and the indirect calls check it): its parameters and its result are the same numbers.
    /// </summary>
    private void EmitDynamicTypedCall(int bottom, int parameters, int results, Label done)
    {
        var invoke = TypedInvokes[parameters];
        var notTypedPop = _il.DefineLabel();

        // A host function has no typed entry
        _il.Emit(OpCodes.Ldloc, _callee);
        _il.Emit(OpCodes.Ldfld, FunctionTyped);
        _il.Emit(OpCodes.Isinst, invoke.DeclaringType!);
        _il.Emit(OpCodes.Dup);
        _il.Emit(OpCodes.Brfalse, notTypedPop);

        // The callee can be a function of another instance
        _il.Emit(OpCodes.Ldarg_1);
        _il.Emit(OpCodes.Ldloc, _callee);
        _il.Emit(OpCodes.Ldfld, FunctionTypedInstance);
        EmitTypedArgumentsAfterInstance(bottom, parameters);
        _il.Emit(OpCodes.Callvirt, invoke);
        EmitTypedResult(bottom, results);
        _il.Emit(OpCodes.Br, done);
        _il.MarkLabel(notTypedPop);
        _il.Emit(OpCodes.Pop);
    }

    /// <summary>Pushes the arguments of a typed method (after the closure or the delegate): the machine, the instance, the parameters, the frame pointer (the scratch slots) and the depth.</summary>
    private void EmitTypedArguments(int bottom, int parameters)
    {
        // The callee is a function of the same module: it has the same instance
        _il.Emit(OpCodes.Ldarg_1);
        _il.Emit(OpCodes.Ldarg_2);
        EmitTypedArgumentsAfterInstance(bottom, parameters);
    }

    /// <summary>Pushes the parameters of a typed method, the frame pointer (the scratch slots) and the depth of the callee.</summary>
    private void EmitTypedArgumentsAfterInstance(int bottom, int parameters)
    {
        for (var i = 0; i < parameters; i++)
        {
            _il.Emit(OpCodes.Ldloc, _stackA[bottom + i]);
        }

        _il.Emit(OpCodes.Ldloc, _scratch);
        EmitLoadArgument(DepthArgument);
        _il.Emit(OpCodes.Ldc_I4_1);
        _il.Emit(OpCodes.Add);
    }

    private void EmitTypedResult(int bottom, int results)
    {
        if (results == 0)
        {
            _il.Emit(OpCodes.Pop);
        }
        else
        {
            _il.Emit(OpCodes.Stloc, _stackA[bottom]);
        }
    }

    /// <summary>Runs an instruction with the interpreter (<see cref="Machine.JitExecute"/>): its operands are written to the stack of the machine, and its results are read from it.</summary>
    private bool EmitFallback(int pc, int h)
    {
        if (pc + 1 >= _instrs.Length)
            return false;

        var pops = _pops[pc];
        var bottom = h - pops;
        var results = _heights[pc + 1] - bottom;
        if (results is < 0 or > 16)
            return false;

        // The machine reads the operands of the stack only, but the instruction is not known: all the operands are bits
        MaterializeAll(h);
        Spill(bottom, pops, Math.Max(pops, results));
        _il.Emit(OpCodes.Ldarg_1);
        EmitLoadInstr(pc);
        _il.Emit(OpCodes.Ldarg_2);
        _il.Emit(OpCodes.Ldloc, _scratch);
        EmitInt(pops);
        _il.Emit(OpCodes.Add);
        _il.Emit(OpCodes.Call, MachineJitExecute);
        Unspill(bottom, results);
        return true;
    }

    /// <summary>The catch block: finds the clause of the exception, gives its values to the label of the clause, and enters the try block again.</summary>
    private void EmitCatch(Label top)
    {
        var exception = _il.DeclareLocal(typeof(WasmThrowException));
        var index = _il.DeclareLocal(typeof(int));
        var payload = _il.DeclareLocal(typeof(Slot[]));
        _il.Emit(OpCodes.Stloc, exception);

        _il.Emit(OpCodes.Ldarg_0);
        _il.Emit(OpCodes.Ldfld, ClosureCode);
        _il.Emit(OpCodes.Ldarg_2);
        _il.Emit(OpCodes.Ldloc, _pc!);
        _il.Emit(OpCodes.Ldloc, exception);
        _il.Emit(OpCodes.Call, GetJitOp(nameof(JitOps.FindClause)));
        _il.Emit(OpCodes.Stloc, index);

        var unpack = new Label[_clauses.Count];
        for (var i = 0; i < unpack.Length; i++)
        {
            unpack[i] = _il.DefineLabel();
        }

        _il.Emit(OpCodes.Ldloc, index);
        _il.Emit(OpCodes.Switch, unpack);
        _il.Emit(OpCodes.Rethrow);
        for (var i = 0; i < _clauses.Count; i++)
        {
            var clause = _clauses[i].Clause;
            _il.MarkLabel(unpack[i]);
            var payloadCount = clause.Kind switch
            {
                CatchKind.Catch => clause.Arity,
                CatchKind.CatchRef => clause.Arity - 1,
                _ => 0,
            };

            if (payloadCount > 0)
            {
                _il.Emit(OpCodes.Ldloc, exception);
                _il.Emit(OpCodes.Call, GetJitOp(nameof(JitOps.Payload)));
                _il.Emit(OpCodes.Stloc, payload);
                for (var k = 0; k < payloadCount; k++)
                {
                    _il.Emit(OpCodes.Ldloc, payload);
                    EmitInt(k);
                    _il.Emit(OpCodes.Ldelema, typeof(Slot));
                    EmitLoadSlot(_stackA[clause.Height + k], StackB(clause.Height + k), StackR(clause.Height + k));
                }
            }

            if (clause.Kind is CatchKind.CatchRef or CatchKind.CatchAllRef)
            {
                _il.Emit(OpCodes.Ldloc, exception);
                _il.Emit(OpCodes.Call, GetJitOp(nameof(JitOps.ExceptionObject)));
                _il.Emit(OpCodes.Stloc, StackR(clause.Height + payloadCount)!);
            }

            EmitInt(i + 1);
            _il.Emit(OpCodes.Stloc, _entry!);
            _il.Emit(OpCodes.Leave, top);
        }
    }
}

/// <summary>The data of the .NET code of a function: the first argument of its method.</summary>
internal sealed class JitClosure
{
    public required CompiledCode Code;

    public required Instr[] Instrs;

    /// <summary>The code of the functions of the module that the function calls (their typed entries, see <see cref="CompiledCode.Typed"/>).</summary>
    public required CompiledCode[] Callees;

    /// <summary>The types of the indirect calls of the function.</summary>
    public required DefinedType[] Types;
}

// The typed entries of the functions whose parameters and result are numbers (see CompiledCode.Typed): the frame pointer is
// the first free slot of the stack of the machine, the depth is the number of frames of .NET code with this one (see
// Machine._jitDepthLimit), the parameters are the bits of the numbers, and the result is 0 when the function has no result
internal delegate long JitTyped0(Machine machine, ManagedInstance instance, int fp, int depth);

internal delegate long JitTyped1(Machine machine, ManagedInstance instance, long p0, int fp, int depth);

internal delegate long JitTyped2(Machine machine, ManagedInstance instance, long p0, long p1, int fp, int depth);

internal delegate long JitTyped3(Machine machine, ManagedInstance instance, long p0, long p1, long p2, int fp, int depth);

internal delegate long JitTyped4(Machine machine, ManagedInstance instance, long p0, long p1, long p2, long p3, int fp, int depth);

internal delegate long JitTyped5(Machine machine, ManagedInstance instance, long p0, long p1, long p2, long p3, long p4, int fp, int depth);

internal delegate long JitTyped6(Machine machine, ManagedInstance instance, long p0, long p1, long p2, long p3, long p4, long p5, int fp, int depth);

internal delegate long JitTyped7(Machine machine, ManagedInstance instance, long p0, long p1, long p2, long p3, long p4, long p5, long p6, int fp, int depth);

internal delegate long JitTyped8(Machine machine, ManagedInstance instance, long p0, long p1, long p2, long p3, long p4, long p5, long p6, long p7, int fp, int depth);
