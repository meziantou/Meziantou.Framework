using System.Diagnostics;

namespace Meziantou.Framework.JavaScriptEngine.Compiler;

/// <summary>Builds the bytecode of a function: the instructions, the constants, the labels, the try regions and the positions.</summary>
internal sealed class BytecodeBuilder
{
    private readonly List<int> _code = [];
    private readonly List<JsValue> _constants = [];
    private readonly Dictionary<double, int> _numberConstants = [];
    private readonly Dictionary<string, int> _stringConstants = new(StringComparer.Ordinal);
    private readonly List<PropertyKey> _keys = [];
    private readonly Dictionary<PropertyKey, int> _keyIndices = [];
    private readonly List<object> _objects = [];
    private readonly List<ExceptionHandler> _handlers = [];
    private readonly List<int> _positions = [];
    private readonly List<int> _labelPositions = [];
    private readonly List<int> _callSites = [];
    private readonly List<int> _labelDepths = [];
    private readonly List<(int Position, int Label)> _fixups = [];
    private int _lastPositionOffset = -1;

    // The starts of the last three instructions (the last one first) and the position of the last marked label, for the peephole optimizations
    private int _instruction1 = -1;
    private int _instruction2 = -1;
    private int _instruction3 = -1;
    private int _lastLabelPosition = -1;

    public int StackDepth { get; set; }

    public int MaxStack { get; private set; }

    /// <summary>Gets whether the code contains an await (the top-level await of a module).</summary>
    public bool ContainsAwait { get; private set; }

    /// <summary>Gets the position of the next instruction.</summary>
    public int Position => _code.Count;

    /// <summary>Gets whether the last instruction never falls through (a jump, a throw or a return).</summary>
    public bool IsUnreachable { get; private set; }

    // ------------------------------------------------------------------------------------------------------------------
    // Constants
    // ------------------------------------------------------------------------------------------------------------------

    public int AddConstant(JsValue value)
    {
        if (value.IsNumber)
        {
            var number = value.AsNumber();
            if (!double.IsNaN(number) && !(number == 0 && double.IsNegative(number)))
            {
                if (_numberConstants.TryGetValue(number, out var existing))
                    return existing;

                _numberConstants[number] = _constants.Count;
            }
        }
        else if (value.IsString)
        {
            var text = value.AsString();
            if (_stringConstants.TryGetValue(text, out var existing))
                return existing;

            _stringConstants[text] = _constants.Count;
        }

        _constants.Add(value);
        return _constants.Count - 1;
    }

    public int AddKey(PropertyKey key)
    {
        if (_keyIndices.TryGetValue(key, out var index))
            return index;

        index = _keys.Count;
        _keys.Add(key);
        _keyIndices[key] = index;
        return index;
    }

    public int AddObject(object value)
    {
        _objects.Add(value);
        return _objects.Count - 1;
    }

    // ------------------------------------------------------------------------------------------------------------------
    // Instructions
    // ------------------------------------------------------------------------------------------------------------------

    public void Emit(OpCode op)
    {
        Debug.Assert(GetOperandCount(op) == 0, $"{op} has operands");
        if (op == OpCode.Pop && TryRemoveDiscardedValue())
            return;

        EmitCore(op, 0);
    }

    /// <summary>
    /// The value of an assignment or an update whose result is discarded (an expression statement, the update of a for
    /// statement) is not duplicated: <c>Dup; Store; Pop</c> is <c>Store</c>, and <c>Dup; Increment; Store; Pop</c> (a postfix
    /// update) is <c>Increment; Store</c>. No jump targets the removed instructions.
    /// </summary>
    private bool TryRemoveDiscardedValue()
    {
        if (_instruction1 < 0 || _instruction2 < 0 || _lastLabelPosition > _instruction2 || !IsSimpleStore((OpCode)_code[_instruction1]))
            return false;

        int dupPosition;
        if ((OpCode)_code[_instruction2] == OpCode.Dup)
        {
            dupPosition = _instruction2;
            _instruction2 = _instruction3;
        }
        else if ((OpCode)_code[_instruction2] is OpCode.Increment or OpCode.Decrement && _instruction3 >= 0 && (OpCode)_code[_instruction3] == OpCode.Dup && _lastLabelPosition <= _instruction3)
        {
            dupPosition = _instruction3;
        }
        else
        {
            return false;
        }

        _code.RemoveAt(dupPosition);
        _instruction1--;
        if (_instruction2 > dupPosition)
        {
            _instruction2--;
        }

        _instruction3 = -1;
        for (var i = _positions.Count - 2; i >= 0 && _positions[i] > dupPosition; i -= 2)
        {
            _positions[i]--;
        }

        StackDepth--;
        return true;
    }

    private static bool IsSimpleStore(OpCode op) => op is OpCode.StoreLocal or OpCode.StoreLocalChecked or OpCode.StoreContext or OpCode.StoreContextChecked;

    public void Emit(OpCode op, int operand)
    {
        Debug.Assert(GetOperandCount(op) == 1, $"{op} does not have 1 operand");
        EmitCore(op, operand);
        _code.Add(operand);
    }

    public void Emit(OpCode op, int operand1, int operand2)
    {
        Debug.Assert(GetOperandCount(op) == 2, $"{op} does not have 2 operands");
        EmitCore(op, operand1);
        _code.Add(operand1);
        _code.Add(operand2);
    }

    public void Emit(OpCode op, int operand1, int operand2, int operand3)
    {
        Debug.Assert(GetOperandCount(op) == 3, $"{op} does not have 3 operands");
        EmitCore(op, operand1);
        _code.Add(operand1);
        _code.Add(operand2);
        _code.Add(operand3);
    }

    private void EmitCore(OpCode op, int firstOperand)
    {
        _instruction3 = _instruction2;
        _instruction2 = _instruction1;
        _instruction1 = _code.Count;
        _code.Add((int)op);
        StackDepth += GetStackEffect(op, firstOperand);
        Debug.Assert(StackDepth >= 0, $"Negative stack depth after {op}");
        if (StackDepth > MaxStack)
        {
            MaxStack = StackDepth;
        }

        IsUnreachable = op is OpCode.Jump or OpCode.Throw or OpCode.Return or OpCode.ThrowError;
        if (op == OpCode.Await)
        {
            ContainsAwait = true;
        }
    }

    public void EmitPushNumber(double value)
    {
        if (value == Math.Floor(value) && value is >= int.MinValue and <= int.MaxValue && !(value == 0 && double.IsNegative(value)))
        {
            Emit(OpCode.PushInt, (int)value);
        }
        else
        {
            Emit(OpCode.PushConst, AddConstant(value));
        }
    }

    public void EmitPushString(string value) => Emit(OpCode.PushConst, AddConstant(value));

    // ------------------------------------------------------------------------------------------------------------------
    // Labels
    // ------------------------------------------------------------------------------------------------------------------

    public Label DefineLabel()
    {
        _labelPositions.Add(-1);
        _labelDepths.Add(-1);
        return new Label(_labelPositions.Count - 1);
    }

    /// <summary>Marks the position of a label. The stack depth is the one recorded by the jumps to the label (when the code before is unreachable).</summary>
    public void MarkLabel(Label label)
    {
        _labelPositions[label.Id] = _code.Count;
        _lastLabelPosition = _code.Count;
        var depth = _labelDepths[label.Id];
        if (IsUnreachable && depth >= 0)
        {
            StackDepth = depth;
        }
        else if (depth < 0)
        {
            _labelDepths[label.Id] = StackDepth;
        }

        IsUnreachable = false;
    }

    /// <summary>Emits a jump to a label (the operand is patched when the label is marked).</summary>
    public void EmitJump(OpCode op, Label label)
    {
        Debug.Assert(GetOperandCount(op) == 1, $"{op} is not a jump");
        EmitCore(op, 0);
        _fixups.Add((_code.Count, label.Id));
        _code.Add(-1);

        // The depth at the target: after the jump for the conditional jumps; for the iteration jumps, the depth without the produced value
        var depthAtTarget = op switch
        {
            OpCode.IteratorNext or OpCode.ForInNext => StackDepth - 1,
            OpCode.IteratorStepFinish or OpCode.JumpIfUndefinedPop or OpCode.AsyncDisposeNext => StackDepth - 1,
            _ => StackDepth,
        };
        RecordLabelDepth(label, depthAtTarget);
    }

    private void RecordLabelDepth(Label label, int depth)
    {
        if (_labelDepths[label.Id] < 0)
        {
            _labelDepths[label.Id] = depth;
        }
    }

    /// <summary>Sets the stack depth expected at a label that is only reached by an exception handler.</summary>
    public void SetLabelDepth(Label label, int depth) => _labelDepths[label.Id] = depth;

    public int GetLabelPosition(Label label) => _labelPositions[label.Id];

    // ------------------------------------------------------------------------------------------------------------------
    // Try regions and positions
    // ------------------------------------------------------------------------------------------------------------------

    public void AddHandler(int start, int end, Label target, int stackDepth, int environmentRegister)
    {
        _handlers.Add(new ExceptionHandler(start, end, target.Id, stackDepth, environmentRegister));
    }

    /// <summary>Records the source offset of the next instruction (for the errors and the stack traces).</summary>
    public void SetPosition(int sourceOffset)
    {
        if (sourceOffset == _lastPositionOffset)
            return;

        _lastPositionOffset = sourceOffset;
        if (_positions.Count >= 2 && _positions[^2] == _code.Count)
        {
            _positions[^1] = sourceOffset;
        }
        else
        {
            _positions.Add(_code.Count);
            _positions.Add(sourceOffset);
        }
    }

    /// <summary>Records the source range of the callee of the next instruction (a call or a new), for the error messages.</summary>
    public void AddCallSite(int calleeStart, int calleeEnd)
    {
        _callSites.Add(_code.Count);
        _callSites.Add(calleeStart);
        _callSites.Add(calleeEnd);
    }

    /// <summary>Gets the triples (pc, callee start, callee end) of the calls, sorted by pc.</summary>
    public int[] GetCallSites() => [.. _callSites];

    // ------------------------------------------------------------------------------------------------------------------
    // Result
    // ------------------------------------------------------------------------------------------------------------------

    public (int[] Code, JsValue[] Constants, PropertyKey[] Keys, object[] Objects, ExceptionHandler[] Handlers, int[] Positions) Build()
    {
        var code = _code.ToArray();
        foreach (var (position, label) in _fixups)
        {
            var target = _labelPositions[label];
            if (target < 0)
                throw new InvalidOperationException("A label is not marked");

            code[position] = target;
        }

        var handlers = new ExceptionHandler[_handlers.Count];
        for (var i = 0; i < handlers.Length; i++)
        {
            var handler = _handlers[i];
            handlers[i] = handler with { Target = _labelPositions[handler.Target] };
        }

        // The innermost regions first (they are added when the try statement ends, the inner ones first)
        return (code, [.. _constants], [.. _keys], [.. _objects], handlers, [.. _positions]);
    }

    // ------------------------------------------------------------------------------------------------------------------
    // Instruction table
    // ------------------------------------------------------------------------------------------------------------------

    public static int GetOperandCount(OpCode op)
    {
        return op switch
        {
            OpCode.PushInt or OpCode.PushConst
                or OpCode.LoadLocal or OpCode.StoreLocal
                or OpCode.LoadGlobal or OpCode.LoadGlobalOrUndefined
                or OpCode.LoadName or OpCode.LoadNameOrUndefined or OpCode.LoadNameForCall or OpCode.DeleteName or OpCode.InitializeName
                or OpCode.PushScope
                or OpCode.LoadArgument or OpCode.LoadRestArguments or OpCode.CreateArguments
                or OpCode.Jump or OpCode.JumpIfTrue or OpCode.JumpIfFalse or OpCode.JumpIfTrueKeep or OpCode.JumpIfFalseKeep
                or OpCode.JumpIfNullishKeep or OpCode.JumpIfNotNullishKeep or OpCode.JumpIfNotUndefinedKeep
                or OpCode.GetProperty or OpCode.SetProperty or OpCode.DefineField
                or OpCode.DeleteElement
                or OpCode.CopyDataPropertiesExcluding
                or OpCode.GetSuperProperty or OpCode.SetSuperProperty
                or OpCode.Call or OpCode.New or OpCode.SuperCall
                or OpCode.CreateClosure or OpCode.CreateClass or OpCode.CreatePrivateName
                or OpCode.DefineClassField or OpCode.SetFunctionNameComputed
                or OpCode.GetTemplateObject
                or OpCode.IteratorNext or OpCode.ForInNext or OpCode.IteratorDelegateStep or OpCode.CreateIterResultDone or OpCode.IteratorStepFinish
                or OpCode.GlobalDeclarationInstantiation or OpCode.EvalDeclarationInstantiation
                or OpCode.InitializeGlobalLexical or OpCode.InitializeGlobalFunction or OpCode.SetGlobalVar
                or OpCode.CallEvalSpread
                or OpCode.SaveEnvironment or OpCode.RestoreEnvironment
                or OpCode.JumpIfUndefinedPop or OpCode.AddDisposableResource
                or OpCode.ResolveName or OpCode.LoadResolved or OpCode.SetVarBinding or OpCode.AsyncDisposeNext
                => 1,
            OpCode.LoadLocalChecked or OpCode.StoreLocalChecked or OpCode.ThrowConstAssignLocal
                or OpCode.LoadContext or OpCode.StoreContext
                or OpCode.StoreGlobal or OpCode.StoreName
                or OpCode.DeleteProperty
                or OpCode.DefineMethod
                or OpCode.GetPrivate or OpCode.SetPrivate or OpCode.PrivateIn
                or OpCode.CallEval
                or OpCode.DefinePrivateMethod
                or OpCode.ThrowError
                or OpCode.CreateRegExp
                or OpCode.StoreResolved
                => 2,
            OpCode.LoadContextChecked or OpCode.StoreContextChecked or OpCode.ThrowConstAssignContext or OpCode.LoadImport
                => 3,
            _ => 0,
        };
    }

    /// <summary>The change of the stack depth by an instruction (on the fall-through path).</summary>
    public static int GetStackEffect(OpCode op, int firstOperand)
    {
        return op switch
        {
            OpCode.PushUndefined or OpCode.PushNull or OpCode.PushTrue or OpCode.PushFalse or OpCode.PushInt or OpCode.PushConst or OpCode.PushHole => 1,
            OpCode.Pop => -1,
            OpCode.Dup => 1,
            OpCode.Dup2 => 2,
            OpCode.Dup3 => 3,
            OpCode.Swap or OpCode.Rot3 or OpCode.Rot3Reverse or OpCode.Rot4 or OpCode.Rot5 => 0,
            OpCode.LoadLocal or OpCode.LoadLocalChecked or OpCode.LoadContext or OpCode.LoadContextChecked or OpCode.LoadImport => 1,
            OpCode.StoreLocal or OpCode.StoreLocalChecked or OpCode.StoreContext or OpCode.StoreContextChecked => -1,
            OpCode.ThrowConstAssignLocal or OpCode.ThrowConstAssignContext => 0,
            OpCode.LoadGlobal or OpCode.LoadGlobalOrUndefined or OpCode.LoadName or OpCode.LoadNameOrUndefined => 1,
            OpCode.LoadNameForCall => 2,
            OpCode.StoreGlobal or OpCode.StoreName or OpCode.InitializeName => -1,
            OpCode.DeleteName => 1,
            OpCode.PushScope or OpCode.PopScope or OpCode.CopyScope => 0,
            OpCode.PushWith => -1,
            OpCode.LoadArgument or OpCode.LoadRestArguments or OpCode.CreateArguments => 1,
            OpCode.LoadThis or OpCode.LoadFunction or OpCode.LoadNewTarget or OpCode.LoadHomeObject or OpCode.LoadImportMeta or OpCode.LoadGlobalThis => 1,
            OpCode.Add or OpCode.Subtract or OpCode.Multiply or OpCode.Divide or OpCode.Modulo or OpCode.Exponentiate
                or OpCode.LeftShift or OpCode.RightShift or OpCode.UnsignedRightShift
                or OpCode.BitwiseAnd or OpCode.BitwiseOr or OpCode.BitwiseXor
                or OpCode.Equal or OpCode.NotEqual or OpCode.StrictEqual or OpCode.StrictNotEqual
                or OpCode.LessThan or OpCode.LessThanOrEqual or OpCode.GreaterThan or OpCode.GreaterThanOrEqual
                or OpCode.InstanceOf or OpCode.In => -1,
            OpCode.PrivateIn => 0,
            OpCode.Negate or OpCode.ToNumber or OpCode.ToNumeric or OpCode.BitwiseNot or OpCode.LogicalNot or OpCode.TypeOf
                or OpCode.Increment or OpCode.Decrement or OpCode.ToStringValue or OpCode.ToPropertyKey or OpCode.ToObject or OpCode.RequireObjectCoercible => 0,
            OpCode.Jump => 0,
            OpCode.JumpIfTrue or OpCode.JumpIfFalse => -1,
            OpCode.JumpIfTrueKeep or OpCode.JumpIfFalseKeep or OpCode.JumpIfNullishKeep or OpCode.JumpIfNotNullishKeep or OpCode.JumpIfNotUndefinedKeep => 0,
            OpCode.Throw or OpCode.Return => -1,
            OpCode.ThrowError or OpCode.Debugger => 0,
            OpCode.NewObject or OpCode.NewArray => 1,
            OpCode.ArrayAppend or OpCode.ArrayAppendSpread => -1,
            OpCode.GetProperty => 0,
            OpCode.SetProperty => -1,
            OpCode.GetElement => -1,
            OpCode.SetElement => -2,
            OpCode.DeleteProperty => 0,
            OpCode.DeleteElement => -1,
            OpCode.DefineField => -1,
            OpCode.DefineFieldComputed => -2,
            OpCode.DefineMethod => -2,
            OpCode.SetPrototypeLiteral or OpCode.CopyDataProperties => -1,
            OpCode.CopyDataPropertiesExcluding => -firstOperand,
            OpCode.LoadSuperBase => 0,
            OpCode.GetSuperProperty => -1,
            OpCode.GetSuperElement => -2,
            OpCode.SetSuperProperty => -2,
            OpCode.SetSuperElement => -3,
            OpCode.GetPrivate => 0,
            OpCode.SetPrivate => -1,
            OpCode.Call or OpCode.CallEval => -(firstOperand + 1),
            OpCode.CallSpread or OpCode.CallEvalSpread => -2,
            OpCode.New => -firstOperand,
            OpCode.NewSpread => -1,
            OpCode.SuperCall => 1 - firstOperand,
            OpCode.SuperCallSpread => 0,
            OpCode.LoadAllArguments => 1,
            OpCode.ImportCall => -1,
            OpCode.CreateClosure => 1,
            OpCode.CreateClass => 1,
            OpCode.InitializeInstanceElements => -1,
            OpCode.CreatePrivateName => 1,
            OpCode.DefinePrivateMethod => -4,
            OpCode.CallClassStaticInitializer => -2,
            OpCode.SetHomeObject => -1,
            OpCode.IteratorCallReturn or OpCode.ThrowIfNotObject => 0,
            OpCode.JumpIfUndefinedPop => 0,
            OpCode.AddDisposableResource => -2,
            OpCode.NewDisposeCapability => 1,
            OpCode.DisposeResources => -3,
            OpCode.AsyncDisposeStart => -2,
            OpCode.AsyncDisposeNext => 0,
            OpCode.AsyncDisposeSetResult => -2,
            OpCode.AsyncDisposeFinish => -1,
            OpCode.RequireObjectCoercibleBase => 0,
            OpCode.ResolveName => 1,
            OpCode.LoadResolved => 0,
            OpCode.StoreResolved => -1,
            OpCode.SetFieldInitializer => -2,
            OpCode.DefineClassField => -2,
            OpCode.DefineClassFieldComputed or OpCode.DefinePrivateField => -3,
            OpCode.SetFunctionName or OpCode.SetFunctionNameComputed => -1,
            OpCode.GetTemplateObject or OpCode.CreateRegExp => 1,
            OpCode.GetIterator or OpCode.GetAsyncIterator => 0,
            OpCode.IteratorNext or OpCode.IteratorNextOrUndefined => 0,
            OpCode.IteratorRest => 0,
            OpCode.IteratorClose or OpCode.IteratorCloseOnThrow => -1,
            OpCode.IteratorIsDone => 0,
            OpCode.IteratorDelegateStep => -1,
            OpCode.IteratorCallNext => 0,
            OpCode.IteratorStepFinish => -1,
            OpCode.IteratorComplete or OpCode.IteratorValue => 0,
            OpCode.ForInPrepare => 0,
            OpCode.ForInNext => 0,
            OpCode.GeneratorStart => 0,
            OpCode.Yield => 1,
            OpCode.Await or OpCode.CreateIterResult or OpCode.CreateIterResultDone => 0,
            OpCode.GlobalDeclarationInstantiation or OpCode.EvalDeclarationInstantiation => 0,
            OpCode.InitializeGlobalLexical or OpCode.InitializeGlobalFunction or OpCode.SetGlobalVar or OpCode.SetVarBinding => -1,
            OpCode.SaveEnvironment or OpCode.RestoreEnvironment => 0,
            OpCode.BindThis or OpCode.CheckThisUninitialized => 0,
            _ => throw new ArgumentOutOfRangeException(nameof(op), op, "Unknown opcode"),
        };
    }
}
