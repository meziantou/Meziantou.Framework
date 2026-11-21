namespace Meziantou.Framework.WasmEngine;

/// <summary>
/// Validates the code of a function or of a constant expression
/// (https://webassembly.github.io/spec/core/appendix/algorithm.html), and compiles it to the instructions of the
/// interpreter: the blocks disappear, the branches have their target, the number of values they keep and the height of
/// the operand stack at their label.
/// </summary>
internal sealed partial class FunctionCompiler
{
    private readonly ModuleInfo _module;
    private readonly ValType[] _locals;
    private readonly bool _isConstant;
    private readonly int _globalLimit;
    private readonly List<ValType> _stack = [];
    private readonly List<Control> _controls = [];
    private readonly List<Instr> _code = [];
    private readonly List<BranchTarget> _branchTables = [];
    private readonly List<HandlerInfo> _handlers = [];
    private readonly bool[] _localInitialized;
    private readonly List<int> _initializedLocals = [];
    private int _maxHeight;

    // The locals that hold the exceptions of the handlers of the legacy try instructions, after the locals of the function:
    // one per nesting level of the handlers
    private int _exceptionLocalDepth;
    private int _exceptionLocalCount;

    // The height of the operand stack before each instruction, and the number of operands it pops (the compiler to
    // .NET code maps the operands to locals)
    private readonly List<int> _heights = [];
    private readonly List<int> _pops = [];
    private int _instructionHeight;
    private int _minimumHeight;

    private static bool RecordHeights => System.Runtime.CompilerServices.RuntimeFeature.IsDynamicCodeSupported;

    private FunctionCompiler(ModuleInfo module, ValType[] locals, int parameterCount, bool isConstant, int globalLimit)
    {
        _module = module;
        _locals = locals;
        _isConstant = isConstant;
        _globalLimit = globalLimit;
        _localInitialized = new bool[locals.Length];
        for (var i = 0; i < locals.Length; i++)
        {
            _localInitialized[i] = i < parameterCount || locals[i].IsDefaultable;
        }
    }

    private enum ControlKind : byte
    {
        Function,
        Block,
        Loop,
        If,
        Else,
        TryTable,

        /// <summary>The body of a legacy <c>try</c>.</summary>
        Try,

        /// <summary>A <c>catch</c> handler of a legacy <c>try</c>.</summary>
        Catch,

        /// <summary>The <c>catch_all</c> handler of a legacy <c>try</c> (the last one).</summary>
        CatchAll,
    }

    private enum FixupKind : byte
    {
        Instruction,
        TableEntry,
        Catch,
    }

    private readonly record struct Fixup(FixupKind Kind, int Index, HandlerInfo? Handler = null);

    private sealed class Control
    {
        public ControlKind Kind { get; set; }

        public ValType[] Parameters { get; init; } = [];

        public ValType[] Results { get; init; } = [];

        public int Height { get; init; }

        public bool Unreachable { get; set; }

        /// <summary>The target of the branches to a loop.</summary>
        public int StartPc { get; init; }

        /// <summary>The branches to the end of the block, patched at its end.</summary>
        public List<Fixup> Fixups { get; } = [];

        /// <summary>The conditional jump of an if, patched at its else or at its end, or -1.</summary>
        public int ElseFixup { get; set; } = -1;

        public int InitHeight { get; init; }

        public HandlerInfo? Handler { get; init; }

        /// <summary>The local that holds the exception of the handlers of a legacy <c>try</c> (for <c>rethrow</c>), or -1.</summary>
        public int ExceptionLocal { get; set; } = -1;

        /// <summary>The handlers of the legacy <c>try</c> ... <c>delegate</c> that target this label, whose exceptions are thrown again in its code.</summary>
        public List<HandlerInfo>? Delegates { get; set; }

        public ValType[] LabelTypes => Kind == ControlKind.Loop ? Parameters : Results;
    }

    public static CompiledCode CompileFunction(ModuleInfo module, ref WasmReader reader, DefinedType type, ValType[] locals)
    {
        var compiler = new FunctionCompiler(module, locals, type.Parameters.Length, isConstant: false, module.Globals.Count);
        compiler.PushControl(ControlKind.Function, [], type.Results);
        compiler.Compile(ref reader);
        return compiler.CreateCode(type.Parameters.Length, type.Results);
    }

    /// <summary>Compiles a constant expression (https://webassembly.github.io/spec/core/valid/instructions.html#constant-expressions) whose type is <paramref name="type"/>.</summary>
    /// <param name="module">The module of the expression.</param>
    /// <param name="reader">The reader of the expression, at its first instruction.</param>
    /// <param name="type">The type of the value of the expression.</param>
    /// <param name="globalLimit">The number of globals the expression can read (the previous globals).</param>
    public static CompiledCode CompileConstant(ModuleInfo module, ref WasmReader reader, ValType type, int globalLimit)
    {
        var compiler = new FunctionCompiler(module, [], 0, isConstant: true, globalLimit);
        compiler.PushControl(ControlKind.Function, [], [type]);
        compiler.Compile(ref reader);
        return compiler.CreateCode(0, [type]);
    }

    private CompiledCode CreateCode(int parameterCount, ValType[] results) => new()
    {
        Code = [.. _code],
        BranchTables = [.. _branchTables],
        Handlers = [.. _handlers],
        Locals = _exceptionLocalCount is 0 ? _locals : [.. _locals, .. Enumerable.Repeat(ValType.ExnRef, _exceptionLocalCount)],
        MaxStackHeight = _maxHeight,
        ParameterCount = parameterCount,
        Results = results,
        Heights = [.. _heights],
        Pops = [.. _pops],
    };

    private static WasmDecodeException Error(string message) => new(message);

    // ------------------------------------------------------------------------------------------------------------
    // The operand and control stacks
    // ------------------------------------------------------------------------------------------------------------

    private int Height => _stack.Count;

    private Control CurrentControl => _controls[^1];

    private void Push(ValType type)
    {
        _stack.Add(type);
        if (_stack.Count > _maxHeight)
        {
            _maxHeight = _stack.Count;
        }
    }

    private void Push(ValType[] types)
    {
        foreach (var type in types)
        {
            Push(type);
        }
    }

    private ValType Pop()
    {
        var control = CurrentControl;
        if (_stack.Count == control.Height)
        {
            if (control.Unreachable)
                return ValType.Bottom;

            throw Error("type mismatch: operand stack underflow");
        }

        var result = _stack[^1];
        _stack.RemoveAt(_stack.Count - 1);
        if (_stack.Count < _minimumHeight)
        {
            _minimumHeight = _stack.Count;
        }

        return result;
    }

    private ValType Pop(ValType expected)
    {
        var actual = Pop();
        if (!TypeRelations.IsSubtype(actual, expected))
            throw Error("type mismatch: expected " + expected + ", got " + actual);

        return actual;
    }

    private void Pop(ValType[] expected)
    {
        for (var i = expected.Length - 1; i >= 0; i--)
        {
            Pop(expected[i]);
        }
    }

    /// <summary>Pops a reference: bottom in unreachable code.</summary>
    private ValType PopRef()
    {
        var actual = Pop();
        if (actual.Code != ValueCode.Bottom && !actual.IsRef)
            throw Error("type mismatch: expected a reference, got " + actual);

        return actual;
    }

    private Control PushControl(ControlKind kind, ValType[] parameters, ValType[] results, HandlerInfo? handler = null)
    {
        var control = new Control
        {
            Kind = kind,
            Parameters = parameters,
            Results = results,
            Height = _stack.Count,
            StartPc = _code.Count,
            InitHeight = _initializedLocals.Count,
            Handler = handler,
        };
        _controls.Add(control);
        Push(parameters);
        return control;
    }

    private Control PopControl()
    {
        var control = CurrentControl;
        Pop(control.Results);
        if (_stack.Count != control.Height)
            throw Error("type mismatch: values remaining on the stack at the end of the block");

        _controls.RemoveAt(_controls.Count - 1);
        ResetLocals(control.InitHeight);
        return control;
    }

    private void SetUnreachable()
    {
        var control = CurrentControl;
        _stack.RemoveRange(control.Height, _stack.Count - control.Height);
        control.Unreachable = true;
    }

    private void ResetLocals(int initHeight)
    {
        for (var i = _initializedLocals.Count - 1; i >= initHeight; i--)
        {
            _localInitialized[_initializedLocals[i]] = false;
            _initializedLocals.RemoveAt(i);
        }
    }

    private Control GetLabel(uint depth)
    {
        if (depth >= (uint)_controls.Count)
            throw Error("unknown label " + depth);

        return _controls[_controls.Count - 1 - (int)depth];
    }

    // ------------------------------------------------------------------------------------------------------------
    // Emission
    // ------------------------------------------------------------------------------------------------------------

    private int Emit(ushort op, int i0 = 0, int i1 = 0, int i2 = 0, long l0 = 0, long l1 = 0, object? o = null)
    {
        _code.Add(new Instr { Op = op, I0 = i0, I1 = i1, I2 = i2, L0 = l0, L1 = l1, O = o });
        if (RecordHeights)
        {
            _heights.Add(_instructionHeight);
            _pops.Add(_instructionHeight - _minimumHeight);
        }
        return _code.Count - 1;
    }

    /// <summary>Records the branch of an instruction to a label: a loop has its target, a block is patched at its end.</summary>
    private void SetBranchTarget(int instruction, Control label)
    {
        if (label.Kind == ControlKind.Loop)
        {
            var instr = _code[instruction];
            instr.I0 = label.StartPc;
            _code[instruction] = instr;
        }
        else
        {
            label.Fixups.Add(new Fixup(FixupKind.Instruction, instruction));
        }
    }

    private void Patch(Control control, int target)
    {
        foreach (var fixup in control.Fixups)
        {
            switch (fixup.Kind)
            {
                case FixupKind.Instruction:
                    var instr = _code[fixup.Index];
                    instr.I0 = target;
                    _code[fixup.Index] = instr;
                    break;
                case FixupKind.TableEntry:
                    var entry = _branchTables[fixup.Index];
                    entry.Pc = target;
                    _branchTables[fixup.Index] = entry;
                    break;
                case FixupKind.Catch:
                    fixup.Handler!.Catches[fixup.Index].Pc = target;
                    break;
            }
        }
    }

    /// <summary>Emits a branch to a label, whose values are on the top of the stack.</summary>
    private void EmitBranch(Control label, int heightBefore)
    {
        var arity = label.LabelTypes.Length;
        var needsCopy = heightBefore - arity != label.Height;
        var instruction = Emit(needsCopy ? Op.BrCopy : Op.Jump, i1: arity, i2: label.Height);
        SetBranchTarget(instruction, label);
    }

    // ------------------------------------------------------------------------------------------------------------
    // Immediates
    // ------------------------------------------------------------------------------------------------------------

    private static int ReadIndex(ref WasmReader reader) => (int)Math.Min(reader.ReadU32(), int.MaxValue);

    private DefinedType GetType(int index)
    {
        if ((uint)index >= (uint)_module.Types.Count)
            throw Error("unknown type " + index);

        return _module.Types[index];
    }

    private DefinedType ReadStructType(ref WasmReader reader)
    {
        var type = GetType(ReadIndex(ref reader));
        if (!type.IsStruct)
            throw Error("type mismatch: expected a struct type");

        return type;
    }

    private DefinedType ReadArrayType(ref WasmReader reader)
    {
        var type = GetType(ReadIndex(ref reader));
        if (!type.IsArray)
            throw Error("type mismatch: expected an array type");

        return type;
    }

    private DefinedType ReadFunctionTypeIndex(ref WasmReader reader)
    {
        var type = GetType(ReadIndex(ref reader));
        if (!type.IsFunc)
            throw Error("type mismatch: expected a function type");

        return type;
    }

    private ValType ReadValType(ref WasmReader reader) => ModuleDecoder.Resolve(_module, ModuleDecoder.ReadRawValType(ref reader));

    private (WasmHeapKind Heap, DefinedType? Defined) ReadHeapType(ref WasmReader reader)
    {
        var (heap, index) = ModuleDecoder.ReadHeapType(ref reader);
        if (index < 0)
            return (heap, null);

        var type = GetType(index);
        return (new ValType(nullable: true, type).Heap, type);
    }

    private static ValType MakeRef(bool nullable, WasmHeapKind heap, DefinedType? defined) => defined is null ? new ValType(nullable, heap) : new ValType(nullable, defined);

    private (ValType[] Parameters, ValType[] Results) ReadBlockType(ref WasmReader reader)
    {
        var b = reader.PeekByte();
        if (b == 0x40)
        {
            reader.ReadByte();
            return ([], []);
        }

        if (b is 0x7F or 0x7E or 0x7D or 0x7C or 0x7B or 0x63 or 0x64 || ModuleDecoder.GetAbstractHeapType(b) is not null)
            return ([], [ReadValType(ref reader)]);

        var index = reader.ReadS33();
        if (index < 0)
            throw Error("malformed block type");

        var type = GetType((int)Math.Min(index, int.MaxValue));
        if (!type.IsFunc)
            throw Error("type mismatch: block type is not a function type");

        return (type.Parameters, type.Results);
    }

    private ValType GetLocal(int index)
    {
        if ((uint)index >= (uint)_locals.Length)
            throw Error("unknown local " + index);

        return _locals[index];
    }

    private GlobalInfo GetGlobal(int index)
    {
        if ((uint)index >= (uint)_module.Globals.Count || (_isConstant && index >= _globalLimit))
            throw Error("unknown global " + index);

        return _module.Globals[index];
    }

    private TableInfo GetTable(int index)
    {
        if ((uint)index >= (uint)_module.Tables.Count)
            throw Error("unknown table " + index);

        return _module.Tables[index];
    }

    private MemoryInfo GetMemory(int index)
    {
        if ((uint)index >= (uint)_module.Memories.Count)
            throw Error("unknown memory " + index);

        return _module.Memories[index];
    }

    private DefinedType GetTag(int index)
    {
        if ((uint)index >= (uint)_module.Tags.Count)
            throw Error("unknown tag " + index);

        return _module.Tags[index];
    }

    private ElementSegment GetElement(int index)
    {
        if ((uint)index >= (uint)_module.Elements.Count)
            throw Error("unknown elem segment " + index);

        return _module.Elements[index];
    }

    private void CheckData(int index)
    {
        if (_module.DataCount is not { } count)
            throw Error("data count section required");

        if ((uint)index >= (uint)count)
            throw Error("unknown data segment " + index);
    }

    /// <summary>Reads a memarg (https://webassembly.github.io/spec/core/binary/instructions.html#memory-instructions): the alignment, the memory and the offset.</summary>
    private (int Memory, MemoryInfo Info, ulong Offset) ReadMemArg(ref WasmReader reader, int naturalAlignment, bool exact = false)
    {
        var align = reader.ReadU32();
        var memoryIndex = 0;
        if ((align & 0x40) != 0)
        {
            align &= ~0x40u;
            memoryIndex = ReadIndex(ref reader);
        }

        var offset = reader.ReadU64();
        var memory = GetMemory(memoryIndex);
        if (align >= 32 || (exact ? align != naturalAlignment : align > naturalAlignment))
            throw Error(exact ? "alignment must be equal to natural" : "alignment must not be larger than natural");

        if (!memory.Is64 && offset > uint.MaxValue)
            throw Error("offset out of range");

        return (memoryIndex, memory, offset);
    }

    // ------------------------------------------------------------------------------------------------------------
    // The instructions
    // ------------------------------------------------------------------------------------------------------------

    private void Compile(ref WasmReader reader)
    {
        while (true)
        {
            if (reader.AtEnd)
                throw Error(_isConstant ? "unexpected end of section or function" : "unexpected end of section or function: END opcode expected");

            var opcode = reader.ReadByte();
            _instructionHeight = Height;
            _minimumHeight = Height;
            if (_isConstant && !IsConstantInstruction(opcode, ref reader))
                throw Error("constant expression required");

            if (opcode == 0x0B && _controls.Count == 1)
            {
                CompileEnd();
                return;
            }

            CompileInstruction(opcode, ref reader);
        }
    }

    private static bool IsConstantInstruction(byte opcode, ref WasmReader reader)
    {
        switch (opcode)
        {
            case 0x0B or 0x41 or 0x42 or 0x43 or 0x44 or 0x23 or 0xD0 or 0xD2:
            case 0x6A or 0x6B or 0x6C or 0x7C or 0x7D or 0x7E:
                return true;
            case 0xFD:
                var position = reader.Position;
                var simd = reader.ReadU32();
                reader.Position = position;
                return simd == 0x0C;
            case 0xFB:
                position = reader.Position;
                var gc = reader.ReadU32();
                reader.Position = position;
                return gc is 0 or 1 or 6 or 7 or 8 or 26 or 27 or 28;
            default:
                return false;
        }
    }

    private void CompileEnd()
    {
        var control = PopControl();
        if (control.Kind == ControlKind.If && !CheckIfWithoutElse(control))
            throw Error("type mismatch: if without else must have the same parameter and result types");

        EmitDelegates(control, jump: true);
        var target = _code.Count;
        if (control.ElseFixup >= 0)
        {
            var instr = _code[control.ElseFixup];
            instr.I0 = target;
            _code[control.ElseFixup] = instr;
        }

        if (control.Kind == ControlKind.Function)
        {
            Patch(control, target);

            // The end can follow code that the validator knows unreachable (after a branch to the function): the results
            // are on the stack of the function
            _instructionHeight = control.Height + control.Results.Length;
            Emit(Op.End, i1: control.Results.Length);
            return;
        }

        Patch(control, target);
        switch (control.Kind)
        {
            case ControlKind.Try:
                // A legacy try without handler is a block
                _handlers.Remove(control.Handler!);
                break;

            case ControlKind.Catch or ControlKind.CatchAll:
                // The handlers of a legacy try end at its first handler
                _exceptionLocalDepth--;
                break;

            default:
                if (control.Handler is { } handler)
                {
                    handler.End = target;
                }

                break;
        }

        Push(control.Results);
    }

    // ------------------------------------------------------------------------------------------------------------
    // The legacy exception handling (https://github.com/WebAssembly/exception-handling/blob/main/proposals/exception-handling/legacy/Exceptions.md)
    // ------------------------------------------------------------------------------------------------------------

    // A legacy try is a handler whose range is its body, with a clause per catch: the clause gives the exception to the
    // handler code (catch_ref, catch_all_ref), which stores it in a local for the rethrow instructions. A try ... delegate
    // is a handler with a catch_all_ref clause whose target is a throw_ref in the code of the label of the delegate: the
    // handlers that enclose this code receive the exception, as if the code of the label threw it.

    private void CompileTry(ref WasmReader reader)
    {
        var (parameters, results) = ReadBlockType(ref reader);
        Pop(parameters);
        var handler = new HandlerInfo { Start = _code.Count };
        _handlers.Add(handler);
        PushControl(ControlKind.Try, parameters, results, handler);
    }

    private void CompileCatch(ref WasmReader reader, bool all)
    {
        var control = CurrentControl;
        if (control.Kind is not (ControlKind.Try or ControlKind.Catch))
            throw Error(all ? "catch_all without try" : "catch without try");

        var tag = all ? -1 : ReadIndex(ref reader);
        var values = all ? [] : GetTag(tag).Parameters;

        // The end of the previous part of the try, as an else
        Pop(control.Results);
        if (_stack.Count != control.Height)
            throw Error("type mismatch: values remaining on the stack at the end of the block");

        var jump = Emit(Op.Jump);
        control.Fixups.Add(new Fixup(FixupKind.Instruction, jump));
        EmitDelegates(control, jump: false);
        var handler = control.Handler!;
        if (control.Kind == ControlKind.Try)
        {
            handler.End = _code.Count;
            control.ExceptionLocal = _locals.Length + _exceptionLocalDepth;
            _exceptionLocalDepth++;
            _exceptionLocalCount = Math.Max(_exceptionLocalCount, _exceptionLocalDepth);
        }

        handler.Catches = [.. handler.Catches, new CatchClause { Kind = all ? CatchKind.CatchAllRef : CatchKind.CatchRef, Tag = tag, Pc = _code.Count, Arity = values.Length + 1, Height = control.Height }];

        // The code of the handler starts with the values of the tag and the exception, which it stores
        _instructionHeight = control.Height + values.Length + 1;
        _minimumHeight = _instructionHeight - 1;
        _maxHeight = Math.Max(_maxHeight, _instructionHeight);
        Emit(Op.LocalSet, i0: control.ExceptionLocal);

        control.Kind = all ? ControlKind.CatchAll : ControlKind.Catch;
        control.Unreachable = false;
        ResetLocals(control.InitHeight);
        Push(values);
    }

    private void CompileDelegate(ref WasmReader reader)
    {
        var control = CurrentControl;
        if (control.Kind != ControlKind.Try)
            throw Error("delegate without try");

        var depth = reader.ReadU32();
        PopControl();
        EmitDelegates(control, jump: true);
        var target = _code.Count;
        Patch(control, target);
        var handler = control.Handler!;
        handler.End = target;
        handler.Catches = [new CatchClause { Kind = CatchKind.CatchAllRef, Tag = -1, Arity = 1 }];

        // The label is one of the blocks that enclose the try
        var label = GetLabel(depth);
        (label.Delegates ??= []).Add(handler);
        Push(control.Results);
    }

    private void CompileRethrow(ref WasmReader reader)
    {
        var label = GetLabel(reader.ReadU32());
        if (label.Kind is not (ControlKind.Catch or ControlKind.CatchAll))
            throw Error("invalid rethrow label");

        Emit(Op.LocalGet, i0: label.ExceptionLocal);
        Push(ValType.ExnRef);
        _instructionHeight = Height;
        _minimumHeight = Height;
        Pop();
        Emit(Op.ThrowRef);
        SetUnreachable();
    }

    /// <summary>
    /// Emits the <c>throw_ref</c> that throws again, in the code of a label, the exceptions of the <c>delegate</c>
    /// instructions that target it, and sets the target of their clauses.
    /// </summary>
    /// <param name="label">The label, whose code ends.</param>
    /// <param name="jump">Whether the code before can reach the <c>throw_ref</c>, and jumps over it.</param>
    private void EmitDelegates(Control label, bool jump)
    {
        if (label.Delegates is not { Count: > 0 } delegates)
            return;

        var instructionHeight = _instructionHeight;
        var minimumHeight = _minimumHeight;
        var skip = -1;
        if (jump)
        {
            // The values of the label are on the stack
            _instructionHeight = label.Height + label.Results.Length;
            _minimumHeight = label.Height;
            skip = Emit(Op.Jump);
        }

        var pc = _code.Count;
        _instructionHeight = label.Height + 1;
        _minimumHeight = label.Height;
        _maxHeight = Math.Max(_maxHeight, _instructionHeight);
        Emit(Op.ThrowRef);
        foreach (var handler in delegates)
        {
            handler.Catches[0].Pc = pc;
            handler.Catches[0].Height = label.Height;
        }

        delegates.Clear();
        if (skip >= 0)
        {
            var instr = _code[skip];
            instr.I0 = _code.Count;
            _code[skip] = instr;
        }

        _instructionHeight = instructionHeight;
        _minimumHeight = minimumHeight;
    }

    private static bool CheckIfWithoutElse(Control control)
    {
        if (control.Parameters.Length != control.Results.Length)
            return false;

        for (var i = 0; i < control.Parameters.Length; i++)
        {
            if (!TypeRelations.IsSubtype(control.Parameters[i], control.Results[i]))
                return false;
        }

        return true;
    }

    private void CompileInstruction(byte opcode, ref WasmReader reader)
    {
        switch (opcode)
        {
            case 0x00: // unreachable
                Emit(Op.Unreachable);
                SetUnreachable();
                break;

            case 0x01: // nop
                break;

            case 0x02: // block
            {
                var (parameters, results) = ReadBlockType(ref reader);
                Pop(parameters);
                PushControl(ControlKind.Block, parameters, results);
                break;
            }

            case 0x03: // loop
            {
                var (parameters, results) = ReadBlockType(ref reader);
                Pop(parameters);
                PushControl(ControlKind.Loop, parameters, results);
                break;
            }

            case 0x04: // if
            {
                var (parameters, results) = ReadBlockType(ref reader);
                Pop(ValType.I32);
                Pop(parameters);
                var jump = Emit(Op.JumpIfZero);
                var control = PushControl(ControlKind.If, parameters, results);
                control.ElseFixup = jump;
                break;
            }

            case 0x05: // else
            {
                var control = CurrentControl;
                if (control.Kind != ControlKind.If)
                    throw Error("else without if");

                Pop(control.Results);
                if (_stack.Count != control.Height)
                    throw Error("type mismatch: values remaining on the stack at the end of the block");

                var jump = Emit(Op.Jump);
                control.Fixups.Add(new Fixup(FixupKind.Instruction, jump));
                EmitDelegates(control, jump: false);
                var instr = _code[control.ElseFixup];
                instr.I0 = _code.Count;
                _code[control.ElseFixup] = instr;
                control.ElseFixup = -1;
                control.Kind = ControlKind.Else;
                control.Unreachable = false;
                ResetLocals(control.InitHeight);
                Push(control.Parameters);
                break;
            }

            case 0x06: // try (legacy)
                CompileTry(ref reader);
                break;

            case 0x07: // catch (legacy)
                CompileCatch(ref reader, all: false);
                break;

            case 0x19: // catch_all (legacy)
                CompileCatch(ref reader, all: true);
                break;

            case 0x18: // delegate (legacy)
                CompileDelegate(ref reader);
                break;

            case 0x09: // rethrow (legacy)
                CompileRethrow(ref reader);
                break;

            case 0x08: // throw
            {
                var tagIndex = ReadIndex(ref reader);
                var tag = GetTag(tagIndex);
                Pop(tag.Parameters);
                Emit(Op.Throw, i0: tagIndex);
                SetUnreachable();
                break;
            }

            case 0x0A: // throw_ref
                Pop(ValType.ExnRef);
                Emit(Op.ThrowRef);
                SetUnreachable();
                break;

            case 0x0B: // end
                CompileEnd();
                break;

            case 0x0C: // br
            {
                var label = GetLabel(reader.ReadU32());
                var heightBefore = Height;
                Pop(label.LabelTypes);
                EmitBranch(label, heightBefore);
                SetUnreachable();
                break;
            }

            case 0x0D: // br_if
            {
                var label = GetLabel(reader.ReadU32());
                Pop(ValType.I32);
                var heightBefore = Height;
                var types = label.LabelTypes;
                Pop(types);
                Push(types);
                var needsCopy = heightBefore - types.Length != label.Height;
                var instruction = Emit(Op.BrIf, i1: types.Length, i2: label.Height, l0: needsCopy ? 1 : 0);
                SetBranchTarget(instruction, label);
                break;
            }

            case 0x0E: // br_table
            {
                var count = reader.ReadCount();
                var depths = new uint[count + 1];
                for (var i = 0; i <= count; i++)
                {
                    depths[i] = reader.ReadU32();
                }

                Pop(ValType.I32);
                var heightBefore = Height;
                var defaultLabel = GetLabel(depths[count]);
                var arity = defaultLabel.LabelTypes.Length;
                var tableStart = _branchTables.Count;
                for (var i = 0; i <= count; i++)
                {
                    var label = GetLabel(depths[i]);
                    var types = label.LabelTypes;
                    if (types.Length != arity)
                        throw Error("type mismatch: br_table targets have different arities");

                    // Each label is checked against the operands (popped and pushed back)
                    var popped = new ValType[types.Length];
                    for (var j = types.Length - 1; j >= 0; j--)
                    {
                        popped[j] = Pop(types[j]);
                    }

                    foreach (var value in popped)
                    {
                        Push(value);
                    }

                    _branchTables.Add(new BranchTarget { Pc = label.StartPc, Arity = arity, Height = label.Height });
                    if (label.Kind != ControlKind.Loop)
                    {
                        label.Fixups.Add(new Fixup(FixupKind.TableEntry, _branchTables.Count - 1));
                    }
                }

                Pop(defaultLabel.LabelTypes);
                Emit(Op.BrTable, i0: tableStart, i1: count + 1, i2: heightBefore);
                SetUnreachable();
                break;
            }

            case 0x0F: // return
                Pop(_controls[0].Results);
                Emit(Op.Return, i1: _controls[0].Results.Length);
                SetUnreachable();
                break;

            case 0x10: // call
            {
                var index = ReadIndex(ref reader);
                var type = GetFunction(index);
                Pop(type.Parameters);
                Push(type.Results);
                Emit(Op.Call, i0: index);
                break;
            }

            case 0x11: // call_indirect
            {
                var type = ReadFunctionTypeIndex(ref reader);
                var tableIndex = ReadIndex(ref reader);
                var table = GetTable(tableIndex);
                if (!TypeRelations.IsSubtype(table.ElementType, ValType.FuncRef))
                    throw Error("type mismatch: call_indirect requires a table of functions");

                Pop(table.AddressType);
                Pop(type.Parameters);
                Push(type.Results);
                Emit(Op.CallIndirect, i0: tableIndex, o: type);
                break;
            }

            case 0x12: // return_call
            {
                var index = ReadIndex(ref reader);
                var type = GetFunction(index);
                CheckTailCallResults(type);
                Pop(type.Parameters);
                Emit(Op.ReturnCall, i0: index);
                SetUnreachable();
                break;
            }

            case 0x13: // return_call_indirect
            {
                var type = ReadFunctionTypeIndex(ref reader);
                var tableIndex = ReadIndex(ref reader);
                var table = GetTable(tableIndex);
                if (!TypeRelations.IsSubtype(table.ElementType, ValType.FuncRef))
                    throw Error("type mismatch: return_call_indirect requires a table of functions");

                CheckTailCallResults(type);
                Pop(table.AddressType);
                Pop(type.Parameters);
                Emit(Op.ReturnCallIndirect, i0: tableIndex, o: type);
                SetUnreachable();
                break;
            }

            case 0x14: // call_ref
            {
                var type = ReadFunctionTypeIndex(ref reader);
                Pop(new ValType(nullable: true, type));
                Pop(type.Parameters);
                Push(type.Results);
                Emit(Op.CallRef, o: type);
                break;
            }

            case 0x15: // return_call_ref
            {
                var type = ReadFunctionTypeIndex(ref reader);
                CheckTailCallResults(type);
                Pop(new ValType(nullable: true, type));
                Pop(type.Parameters);
                Emit(Op.ReturnCallRef, o: type);
                SetUnreachable();
                break;
            }

            case 0x1A: // drop
                Pop();
                Emit(Op.Drop);
                break;

            case 0x1B: // select
            {
                Pop(ValType.I32);
                var t1 = Pop();
                var t2 = Pop();
                if (t1.IsRef || t2.IsRef)
                    throw Error("type mismatch: select without type requires numeric or vector operands");

                if (t1.Code != ValueCode.Bottom && t2.Code != ValueCode.Bottom && t1.Code != t2.Code)
                    throw Error("type mismatch: select operands have different types");

                Push(t1.Code == ValueCode.Bottom ? t2 : t1);
                Emit(Op.Select);
                break;
            }

            case 0x1C: // select t*
            {
                var count = reader.ReadU32();
                if (count != 1)
                    throw Error("invalid result arity");

                var type = ReadValType(ref reader);
                Pop(ValType.I32);
                Pop(type);
                Pop(type);
                Push(type);
                Emit(Op.Select);
                break;
            }

            case 0x1F: // try_table
                CompileTryTable(ref reader);
                break;

            case 0x20: // local.get
            {
                var index = ReadIndex(ref reader);
                var type = GetLocal(index);
                if (!_localInitialized[index])
                    throw Error("uninitialized local " + index);

                Push(type);
                Emit(Op.LocalGet, i0: index);
                break;
            }

            case 0x21: // local.set
            {
                var index = ReadIndex(ref reader);
                Pop(GetLocal(index));
                InitializeLocal(index);
                Emit(Op.LocalSet, i0: index);
                break;
            }

            case 0x22: // local.tee
            {
                var index = ReadIndex(ref reader);
                var type = GetLocal(index);
                Pop(type);
                InitializeLocal(index);
                Push(type);
                Emit(Op.LocalTee, i0: index);
                break;
            }

            case 0x23: // global.get
            {
                var index = ReadIndex(ref reader);
                var global = GetGlobal(index);
                if (_isConstant && global.Mutable)
                    throw Error("constant expression required: global is mutable");

                Push(global.Type);
                Emit(Op.GlobalGet, i0: index);
                break;
            }

            case 0x24: // global.set
            {
                var index = ReadIndex(ref reader);
                var global = GetGlobal(index);
                if (!global.Mutable)
                    throw Error("global is immutable");

                Pop(global.Type);
                Emit(Op.GlobalSet, i0: index);
                break;
            }

            case 0x25: // table.get
            {
                var index = ReadIndex(ref reader);
                var table = GetTable(index);
                Pop(table.AddressType);
                Push(table.ElementType);
                Emit(Op.TableGet, i0: index);
                break;
            }

            case 0x26: // table.set
            {
                var index = ReadIndex(ref reader);
                var table = GetTable(index);
                Pop(table.ElementType);
                Pop(table.AddressType);
                Emit(Op.TableSet, i0: index);
                break;
            }

            case >= 0x28 and <= 0x35: // loads
            {
                var (size, result) = opcode switch
                {
                    0x28 => (2, ValType.I32),
                    0x29 => (3, ValType.I64),
                    0x2A => (2, ValType.F32),
                    0x2B => (3, ValType.F64),
                    0x2C or 0x2D => (0, ValType.I32),
                    0x2E or 0x2F => (1, ValType.I32),
                    0x30 or 0x31 => (0, ValType.I64),
                    0x32 or 0x33 => (1, ValType.I64),
                    _ => (2, ValType.I64),
                };
                var (memory, info, offset) = ReadMemArg(ref reader, size);
                Pop(info.AddressType);
                Push(result);
                Emit(opcode, i0: memory, l0: (long)offset);
                break;
            }

            case >= 0x36 and <= 0x3E: // stores
            {
                var (size, value) = opcode switch
                {
                    0x36 => (2, ValType.I32),
                    0x37 => (3, ValType.I64),
                    0x38 => (2, ValType.F32),
                    0x39 => (3, ValType.F64),
                    0x3A => (0, ValType.I32),
                    0x3B => (1, ValType.I32),
                    0x3C => (0, ValType.I64),
                    0x3D => (1, ValType.I64),
                    _ => (2, ValType.I64),
                };
                var (memory, info, offset) = ReadMemArg(ref reader, size);
                Pop(value);
                Pop(info.AddressType);
                Emit(opcode, i0: memory, l0: (long)offset);
                break;
            }

            case 0x3F: // memory.size
            {
                var index = ReadIndex(ref reader);
                var memory = GetMemory(index);
                Push(memory.AddressType);
                Emit(Op.MemorySize, i0: index);
                break;
            }

            case 0x40: // memory.grow
            {
                var index = ReadIndex(ref reader);
                var memory = GetMemory(index);
                Pop(memory.AddressType);
                Push(memory.AddressType);
                Emit(Op.MemoryGrow, i0: index);
                break;
            }

            case 0x41:
                Push(ValType.I32);
                Emit(Op.I32Const, l0: reader.ReadS32());
                break;

            case 0x42:
                Push(ValType.I64);
                Emit(Op.I64Const, l0: reader.ReadS64());
                break;

            case 0x43:
                Push(ValType.F32);
                Emit(Op.F32Const, l0: reader.ReadF32Bits());
                break;

            case 0x44:
                Push(ValType.F64);
                Emit(Op.F64Const, l0: (long)reader.ReadF64Bits());
                break;

            case >= 0x45 and <= 0xC4:
                CompileNumeric(opcode);
                break;

            case 0xD0: // ref.null
            {
                var (heap, defined) = ReadHeapType(ref reader);
                Push(MakeRef(nullable: true, heap, defined));
                Emit(Op.RefNull);
                break;
            }

            case 0xD1: // ref.is_null
                PopRef();
                Push(ValType.I32);
                Emit(Op.RefIsNull);
                break;

            case 0xD2: // ref.func
            {
                var index = ReadIndex(ref reader);
                var type = GetFunction(index);
                if (_isConstant)
                {
                    _module.DeclaredFunctionReferences.Add(index);
                }
                else if (!_module.DeclaredFunctionReferences.Contains(index))
                {
                    throw Error("undeclared function reference");
                }

                Push(new ValType(nullable: false, type));
                Emit(Op.RefFunc, i0: index);
                break;
            }

            case 0xD3: // ref.eq
                Pop(new ValType(nullable: true, WasmHeapKind.Eq));
                Pop(new ValType(nullable: true, WasmHeapKind.Eq));
                Push(ValType.I32);
                Emit(Op.RefEq);
                break;

            case 0xD4: // ref.as_non_null
            {
                var type = PopRef();
                Push(type.Code == ValueCode.Bottom ? type : type.AsNonNullable());
                Emit(Op.RefAsNonNull);
                break;
            }

            case 0xD5: // br_on_null
            {
                var label = GetLabel(reader.ReadU32());
                var type = PopRef();
                var heightBefore = Height;
                var types = label.LabelTypes;
                Pop(types);
                Push(types);
                Push(type.Code == ValueCode.Bottom ? type : type.AsNonNullable());
                var needsCopy = heightBefore - types.Length != label.Height;
                var instruction = Emit(Op.BrOnNull, i1: types.Length, i2: label.Height, l0: needsCopy ? 1 : 0);
                SetBranchTarget(instruction, label);
                break;
            }

            case 0xD6: // br_on_non_null
            {
                var label = GetLabel(reader.ReadU32());
                var types = label.LabelTypes;
                if (types.Length == 0 || !types[^1].IsRef)
                    throw Error("type mismatch: br_on_non_null requires a label whose last type is a reference");

                var type = PopRef();
                if (type.Code != ValueCode.Bottom && !TypeRelations.IsSubtype(type.AsNonNullable(), types[^1]))
                    throw Error("type mismatch: br_on_non_null operand does not match the label");

                var heightBefore = Height + 1;
                var prefix = types[..^1];
                Pop(prefix);
                Push(prefix);
                var needsCopy = heightBefore - types.Length != label.Height;
                var instruction = Emit(Op.BrOnNonNull, i1: types.Length, i2: label.Height, l0: needsCopy ? 1 : 0);
                SetBranchTarget(instruction, label);
                break;
            }

            case 0xFB:
                CompileGc(ref reader);
                break;

            case 0xFC:
                CompileMisc(ref reader);
                break;

            case 0xFD:
                CompileSimd(ref reader);
                break;

            case 0xFE:
                CompileAtomic(ref reader);
                break;

            default:
                throw Error("illegal opcode " + opcode.ToString("x2", System.Globalization.CultureInfo.InvariantCulture));
        }
    }

    private DefinedType GetFunction(int index)
    {
        if ((uint)index >= (uint)_module.Functions.Count)
            throw Error("unknown function " + index);

        return _module.Functions[index];
    }

    private void CheckTailCallResults(DefinedType callee)
    {
        var results = _controls[0].Results;
        if (callee.Results.Length != results.Length)
            throw Error("type mismatch: tail call results do not match the results of the function");

        for (var i = 0; i < results.Length; i++)
        {
            if (!TypeRelations.IsSubtype(callee.Results[i], results[i]))
                throw Error("type mismatch: tail call results do not match the results of the function");
        }
    }

    private void InitializeLocal(int index)
    {
        if (!_localInitialized[index])
        {
            _localInitialized[index] = true;
            _initializedLocals.Add(index);
        }
    }

    private void CompileTryTable(ref WasmReader reader)
    {
        var (parameters, results) = ReadBlockType(ref reader);
        var count = reader.ReadCount();
        var catches = new CatchClause[count];
        var labels = new Control[count];
        for (var i = 0; i < count; i++)
        {
            var kind = reader.ReadByte();
            if (kind > 3)
                throw Error("malformed catch clause");

            var tagIndex = kind is 0 or 1 ? ReadIndex(ref reader) : -1;
            var label = GetLabel(reader.ReadU32());
            ValType[] values = kind switch
            {
                0 => GetTag(tagIndex).Parameters,
                1 => [.. GetTag(tagIndex).Parameters, new ValType(nullable: false, WasmHeapKind.Exn)],
                2 => [],
                _ => [new ValType(nullable: false, WasmHeapKind.Exn)],
            };

            var labelTypes = label.LabelTypes;
            if (labelTypes.Length != values.Length)
                throw Error("type mismatch: catch clause does not match the label");

            for (var j = 0; j < values.Length; j++)
            {
                if (!TypeRelations.IsSubtype(values[j], labelTypes[j]))
                    throw Error("type mismatch: catch clause does not match the label");
            }

            catches[i] = new CatchClause { Kind = (CatchKind)kind, Tag = tagIndex, Pc = label.StartPc, Arity = values.Length, Height = label.Height };
            labels[i] = label;
        }

        Pop(parameters);
        var handler = new HandlerInfo { Start = _code.Count, Catches = catches };
        for (var i = 0; i < count; i++)
        {
            if (labels[i].Kind != ControlKind.Loop)
            {
                labels[i].Fixups.Add(new Fixup(FixupKind.Catch, i, handler));
            }
        }

        _handlers.Add(handler);
        PushControl(ControlKind.TryTable, parameters, results, handler);
    }

    private void CompileNumeric(byte opcode)
    {
        var (parameters, result) = GetNumericSignature(opcode);
        Pop(parameters);
        Push(result);
        Emit(opcode);
    }

    private static readonly ValType[] I32x1 = [ValType.I32];
    private static readonly ValType[] I32x2 = [ValType.I32, ValType.I32];
    private static readonly ValType[] I64x1 = [ValType.I64];
    private static readonly ValType[] I64x2 = [ValType.I64, ValType.I64];
    private static readonly ValType[] F32x1 = [ValType.F32];
    private static readonly ValType[] F32x2 = [ValType.F32, ValType.F32];
    private static readonly ValType[] F64x1 = [ValType.F64];
    private static readonly ValType[] F64x2 = [ValType.F64, ValType.F64];

    private static (ValType[] Parameters, ValType Result) GetNumericSignature(int opcode) => opcode switch
    {
        0x45 => (I32x1, ValType.I32),
        >= 0x46 and <= 0x4F => (I32x2, ValType.I32),
        0x50 => (I64x1, ValType.I32),
        >= 0x51 and <= 0x5A => (I64x2, ValType.I32),
        >= 0x5B and <= 0x60 => (F32x2, ValType.I32),
        >= 0x61 and <= 0x66 => (F64x2, ValType.I32),
        >= 0x67 and <= 0x69 => (I32x1, ValType.I32),
        >= 0x6A and <= 0x78 => (I32x2, ValType.I32),
        >= 0x79 and <= 0x7B => (I64x1, ValType.I64),
        >= 0x7C and <= 0x8A => (I64x2, ValType.I64),
        >= 0x8B and <= 0x91 => (F32x1, ValType.F32),
        >= 0x92 and <= 0x98 => (F32x2, ValType.F32),
        >= 0x99 and <= 0x9F => (F64x1, ValType.F64),
        >= 0xA0 and <= 0xA6 => (F64x2, ValType.F64),
        0xA7 => (I64x1, ValType.I32),
        0xA8 or 0xA9 => (F32x1, ValType.I32),
        0xAA or 0xAB => (F64x1, ValType.I32),
        0xAC or 0xAD => (I32x1, ValType.I64),
        0xAE or 0xAF => (F32x1, ValType.I64),
        0xB0 or 0xB1 => (F64x1, ValType.I64),
        0xB2 or 0xB3 => (I32x1, ValType.F32),
        0xB4 or 0xB5 => (I64x1, ValType.F32),
        0xB6 => (F64x1, ValType.F32),
        0xB7 or 0xB8 => (I32x1, ValType.F64),
        0xB9 or 0xBA => (I64x1, ValType.F64),
        0xBB => (F32x1, ValType.F64),
        0xBC => (F32x1, ValType.I32),
        0xBD => (F64x1, ValType.I64),
        0xBE => (I32x1, ValType.F32),
        0xBF => (I64x1, ValType.F64),
        0xC0 or 0xC1 => (I32x1, ValType.I32),
        _ => (I64x1, ValType.I64),
    };
}
