using Meziantou.Framework.JavaScriptEngine.Compiler;

namespace Meziantou.Framework.JavaScriptEngine.Interpreter;

/// <summary>The execution context of a function call (or a script, eval code): its registers, its operand stack and its position.</summary>
internal sealed class Frame
{
    public Frame(FunctionTemplate code, JsRealm realm, Environment environment, JsValue[] arguments)
    {
        Code = code;
        Realm = realm;
        Environment = environment;
        Arguments = arguments;
        Values = new JsValue[code.RegisterCount + code.MaxStack];
        Sp = code.RegisterCount;
    }

    public FunctionTemplate Code { get; }

    public JsRealm Realm { get; }

    /// <summary>Gets or sets the function being called (null for script and eval code).</summary>
    public JsClosure? Function { get; set; }

    /// <summary>Gets or sets the this binding (a hole in a derived constructor before super()).</summary>
    public JsValue This { get; set; }

    public JsObject? NewTarget { get; set; }

    /// <summary>Gets or sets the current lexical environment.</summary>
    public Environment Environment { get; set; }

    /// <summary>Gets the registers, then the operand stack.</summary>
    public JsValue[] Values { get; }

    /// <summary>Gets or sets the position of the top of the operand stack in <see cref="Values"/>.</summary>
    public int Sp { get; set; }

    /// <summary>Gets or sets the position of the current instruction (while running) or of the next one (suspended, or a caller).</summary>
    public int Pc { get; set; }

    public JsValue[] Arguments { get; }

    /// <summary>Gets or sets the caller frame in the same run of the interpreter.</summary>
    public Frame? Parent { get; set; }

    public ScriptRecord? Script { get; set; }

    /// <summary>Gets or sets whether the frame is a [[Construct]] (the result is this when the function does not return an object).</summary>
    public bool IsConstruct { get; set; }

    public FrameState State { get; set; }

    /// <summary>Gets or sets the generator or the async function that owns the frame.</summary>
    public object? Owner { get; set; }

    /// <summary>Gets or sets the value given to a resumed frame, and how it is resumed (0 next, 1 throw, 2 return).</summary>
    public JsValue ResumeValue { get; set; }

    public int ResumeKind { get; set; }

    /// <summary>Gets or sets whether the frame resumes by throwing <see cref="ResumeValue"/> at its suspension point (await).</summary>
    public bool PendingThrow { get; set; }
}
