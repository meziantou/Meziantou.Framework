using System.Runtime.InteropServices;

namespace Meziantou.Framework.WasmEngine;

/// <summary>
/// An instruction of the validated code that the interpreter runs. The branches have their target, the number of values
/// they keep and the height of the operand stack at the target (relative to the base of the operands of the frame).
/// </summary>
[StructLayout(LayoutKind.Auto)]
internal struct Instr
{
    public ushort Op;

    public int I0;

    public int I1;

    public int I2;

    public long L0;

    public long L1;

    public object? O;

    public override readonly string ToString() => OpNames.GetName(Op) + " " + I0 + " " + I1 + " " + I2 + " " + L0 + " " + L1;
}

/// <summary>The target of an entry of a br_table.</summary>
[StructLayout(LayoutKind.Auto)]
internal struct BranchTarget
{
    public int Pc;

    public int Arity;

    public int Height;
}

internal enum CatchKind : byte
{
    Catch,
    CatchRef,
    CatchAll,
    CatchAllRef,
}

/// <summary>A catch clause of a try_table: the values are given to the label.</summary>
[StructLayout(LayoutKind.Auto)]
internal struct CatchClause
{
    public CatchKind Kind;

    public int Tag;

    public int Pc;

    public int Arity;

    public int Height;
}

/// <summary>The instructions of a try_table, and its catch clauses (the handlers are sorted by start).</summary>
internal sealed class HandlerInfo
{
    public int Start { get; set; }

    public int End { get; set; }

    public CatchClause[] Catches { get; set; } = [];
}

/// <summary>The compiled code of a function or of a constant expression.</summary>
internal sealed class CompiledCode
{
    private Instr[]? _interpreted;

    public Instr[] Code { get; set; } = [];

    /// <summary>
    /// The instructions that the interpreter runs: <see cref="Code"/> with fused operations (see <see cref="InstructionFuser"/>),
    /// at the same indexes. They are created when the interpreter runs the function the first time, so that the functions
    /// that are not interpreted do not have them.
    /// </summary>
    public Instr[] Interpreted => _interpreted ?? CreateInterpreted();

    public BranchTarget[] BranchTables { get; set; } = [];

    public HandlerInfo[] Handlers { get; set; } = [];

    /// <summary>The types of the locals (the parameters first).</summary>
    public ValType[] Locals { get; set; } = [];

    /// <summary>The maximum height of the operand stack.</summary>
    public int MaxStackHeight { get; set; }

    public int ParameterCount { get; set; }

    public ValType[] Results { get; set; } = [];

    /// <summary>The number of slots of a frame: the locals and the operands.</summary>
    public int FrameSize => Locals.Length + MaxStackHeight;

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private Instr[] CreateInterpreted()
    {
        // Two threads can create them at the same time: the instructions are the same
        var interpreted = InstructionFuser.Fuse(Code);
        Volatile.Write(ref _interpreted, interpreted);
        return interpreted;
    }

    /// <summary>The height of the operand stack before each instruction (empty when dynamic code is not supported).</summary>
    public int[] Heights { get; set; } = [];

    /// <summary>The number of operands each instruction pops (empty when dynamic code is not supported).</summary>
    public int[] Pops { get; set; } = [];

    /// <summary>The number of calls, used to decide when the function is compiled to .NET code.</summary>
    public int CallCount;

    /// <summary>The .NET code of the function (a <see cref="JitFunction"/>), or null.</summary>
    public JitFunction? Compiled;

    /// <summary>
    /// The typed entry of the .NET code (a <see cref="JitTyped0"/>, <see cref="JitTyped1"/>, ... delegate), once the code ran:
    /// the compiled functions of the module call it directly, with their arguments. Null when the function is not compiled,
    /// or when its type has references, vectors, more than one result or too many parameters.
    /// </summary>
    public Delegate? Typed;

    /// <summary>The typed entry, until the .NET code of the function ran (the JIT of .NET compiled it without errors).</summary>
    public Delegate? PendingTyped;

    /// <summary>Whether the compilation to .NET code failed or is not possible: the function is interpreted.</summary>
    public bool CompilationFailed;

    /// <summary>0 before the compilation to .NET code, 1 once it started (a thread compiles it, the others interpret it).</summary>
    public int CompilationStarted;

    /// <summary>The number of backward branches of the interpreted calls: a call whose loops run many times continues in .NET code (OSR, see <see cref="Machine"/>).</summary>
    public int BackEdgeCount;

    /// <summary>The OSR method of the function (see <see cref="IlCompiler.CompileOsr"/>), or null.</summary>
    public JitOsrFunction? Osr;

    /// <summary>The instructions of the loops of <see cref="Osr"/>, in the order of their indexes.</summary>
    public int[] OsrLoops = [];

    /// <summary>0 before the compilation of the OSR method, 1 once it started or failed.</summary>
    public int OsrStarted;

    /// <summary>A name for the diagnostics (the index of the function).</summary>
    public string Name { get; set; } = "";
}
