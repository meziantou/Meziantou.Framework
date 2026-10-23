using Meziantou.Framework.JavaScriptEngine.Compiler;

namespace Meziantou.Framework.JavaScriptEngine.Interpreter;

/// <summary>
/// The execution context of a function call (or a script, eval code): its registers, its arguments, its operand stack and
/// its position. <see cref="Values"/> holds the registers, then a copy of the arguments, then the operand stack.
/// </summary>
/// <remarks>
/// The frames of the ordinary calls come from the <see cref="FramePool"/> of the engine and go back to it when the call
/// completes (<see cref="IsPooled"/>): nothing may keep a reference to them after that. The frames of the generators,
/// the async functions, the scripts, the modules and eval code are not pooled (a generator keeps its frame).
/// </remarks>
internal sealed class Frame
{
    public Frame(FunctionTemplate code, JsRealm realm, Environment environment, ReadOnlySpan<JsValue> arguments)
    {
        Values = new JsValue[code.RegisterCount + arguments.Length + code.MaxStack];
        Initialize(code, realm, environment, arguments);
    }

    /// <summary>Prepares the frame for a call (a new frame, or a frame of the pool whose values are cleared).</summary>
    internal void Initialize(FunctionTemplate code, JsRealm realm, Environment environment, ReadOnlySpan<JsValue> arguments)
    {
        Code = code;
        IsStrict = code.IsStrict;
        Realm = realm;
        Environment = environment;
        ArgumentCount = arguments.Length;
        arguments.CopyTo(Values.AsSpan(code.RegisterCount));
        Sp = code.RegisterCount + arguments.Length;
    }

    /// <summary>Clears the frame before it goes back to the pool (no reference is kept to the values of the call).</summary>
    internal void Clear()
    {
        Values.AsSpan(0, Code.RegisterCount + ArgumentCount + Code.MaxStack).Clear();
        Code = null!;
        Realm = null!;
        Environment = null!;
        Function = null;
        This = default;
        NewTarget = null;
        Pc = 0;
        Parent = null;
        Script = null;
        IsConstruct = false;
        State = FrameState.Running;
    }

    /// <summary>Gets the length of <see cref="Values"/> needed by a call.</summary>
    internal static int GetValueCount(FunctionTemplate code, int argumentCount) => code.RegisterCount + argumentCount + code.MaxStack;

    public FunctionTemplate Code { get; private set; } = null!;

    /// <summary>
    /// Gets or sets whether the running code is strict: the code of the template, or a class definition (its heritage and
    /// computed keys) in sloppy code, between <see cref="OpCode.EnterStrictCode"/> and <see cref="OpCode.ExitStrictCode"/>.
    /// </summary>
    public bool IsStrict { get; set; }

    public JsRealm Realm { get; private set; } = null!;

    /// <summary>Gets or sets the function being called (null for script and eval code).</summary>
    public JsClosure? Function { get; set; }

    /// <summary>Gets or sets the this binding (a hole in a derived constructor before super()).</summary>
    public JsValue This { get; set; }

    public JsObject? NewTarget { get; set; }

    /// <summary>Gets or sets the current lexical environment.</summary>
    public Environment Environment { get; set; } = null!;

    /// <summary>Gets the registers, the arguments, then the operand stack.</summary>
    public JsValue[] Values { get; internal set; }

    /// <summary>Gets or sets the position of the top of the operand stack in <see cref="Values"/>.</summary>
    public int Sp { get; set; }

    /// <summary>Gets or sets the position of the current instruction (while running) or of the next one (suspended, or a caller).</summary>
    public int Pc { get; set; }

    /// <summary>Gets the number of arguments (stored after the registers).</summary>
    public int ArgumentCount { get; private set; }

    /// <summary>Gets the arguments of the call.</summary>
    public ReadOnlySpan<JsValue> Arguments => Values.AsSpan(Code.RegisterCount, ArgumentCount);

    /// <summary>Gets the position of the bottom of the operand stack in <see cref="Values"/>.</summary>
    public int StackBase => Code.RegisterCount + ArgumentCount;

    /// <summary>Gets or sets the caller frame in the same run of the interpreter (the next free frame in the pool).</summary>
    public Frame? Parent { get; set; }

    public IScriptOrModule? Script { get; set; }

    /// <summary>Gets or sets whether the frame is a [[Construct]] (the result is this when the function does not return an object).</summary>
    public bool IsConstruct { get; set; }

    /// <summary>Gets whether the frame comes from the <see cref="FramePool"/> (it is returned to the pool when the call completes).</summary>
    public bool IsPooled { get; init; }

    public FrameState State { get; set; }

    /// <summary>Gets or sets the generator or the async function that owns the frame.</summary>
    public object? Owner { get; set; }

    /// <summary>Gets or sets the value given to a resumed frame, and how it is resumed (0 next, 1 throw, 2 return).</summary>
    public JsValue ResumeValue { get; set; }

    public int ResumeKind { get; set; }

    /// <summary>Gets or sets whether the frame resumes by throwing <see cref="ResumeValue"/> at its suspension point (await).</summary>
    public bool PendingThrow { get; set; }
}

/// <summary>
/// The free frames of an engine. The frames of the ordinary calls are reused: a call allocates neither its frame nor its
/// values (registers, arguments, stack). A frame keeps its values array, which grows when a call needs more.
/// </summary>
internal sealed class FramePool
{
    // The frames beyond this number are left to the GC (a deep recursion does not keep its frames forever)
    private const int MaxFreeFrames = 128;

    private Frame? _free;
    private int _freeCount;

    public Frame Rent(FunctionTemplate code, JsRealm realm, Environment environment, ReadOnlySpan<JsValue> arguments)
    {
        var frame = _free;
        if (frame is null)
            return new Frame(code, realm, environment, arguments) { IsPooled = true };

        _free = frame.Parent;
        _freeCount--;
        frame.Parent = null;
        var length = Frame.GetValueCount(code, arguments.Length);
        if (frame.Values.Length < length)
        {
            frame.Values = new JsValue[Math.Max(length, frame.Values.Length * 2)];
        }

        frame.Initialize(code, realm, environment, arguments);
        return frame;
    }

    /// <summary>Returns a frame whose call completed (no-op for a frame that is not pooled).</summary>
    public void Return(Frame frame)
    {
        if (!frame.IsPooled)
            return;

        frame.Clear();
        if (_freeCount >= MaxFreeFrames)
            return;

        frame.Parent = _free;
        _free = frame;
        _freeCount++;
    }
}
