namespace Meziantou.Framework.WasmEngine;

#pragma warning disable CA1032 // The exceptions are created by the engine: they have no parameterless constructor

/// <summary>The base class of the exceptions of the WebAssembly engine.</summary>
public abstract class WasmException : Exception
{
    private protected WasmException(string message)
        : base(message)
    {
    }

    private protected WasmException(string message, Exception? innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>A module is not valid (a <c>WebAssembly.CompileError</c> in JavaScript).</summary>
public sealed class WasmCompileException : WasmException
{
    /// <summary>Creates an exception.</summary>
    public WasmCompileException(string message)
        : base(message)
    {
    }

    /// <summary>Creates an exception.</summary>
    public WasmCompileException(string message, Exception? innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>The imports do not match the module (a <c>WebAssembly.LinkError</c> in JavaScript).</summary>
public sealed class WasmLinkException : WasmException
{
    /// <summary>Creates an exception.</summary>
    public WasmLinkException(string message)
        : base(message)
    {
    }

    /// <summary>Creates an exception.</summary>
    public WasmLinkException(string message, Exception? innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>The kind of a trap.</summary>
public enum WasmTrapKind
{
    /// <summary>Any trap but a stack overflow (e.g. <c>unreachable</c>, an out-of-bounds access, a division by zero).</summary>
    Other,

    /// <summary>The call stack is exhausted (see <see cref="WasmRuntimeOptions.MaxStackSize"/>).</summary>
    StackOverflow,
}

/// <summary>The execution of WebAssembly trapped (a <c>WebAssembly.RuntimeError</c> in JavaScript).</summary>
public sealed class WasmTrapException : WasmException
{
    /// <summary>Creates an exception.</summary>
    public WasmTrapException(string message)
        : this(message, WasmTrapKind.Other)
    {
    }

    /// <summary>Creates an exception.</summary>
    public WasmTrapException(string message, WasmTrapKind kind)
        : base(message)
    {
        TrapKind = kind;
    }

    /// <summary>Creates an exception.</summary>
    public WasmTrapException(string message, Exception? innerException)
        : base(message, innerException)
    {
    }

    /// <summary>Gets the kind of the trap.</summary>
    public WasmTrapKind TrapKind { get; }

    /// <summary>Whether the trap is counted by the metrics (a trap goes through the nested calls that it ends).</summary>
    internal bool IsRecorded { get; set; }
}

/// <summary>
/// WebAssembly threw an exception (<c>throw</c>, <c>throw_ref</c>), or a host function throws an exception in WebAssembly:
/// a <see cref="WasmHostCallback"/> that throws this exception throws <see cref="Exception"/> in WebAssembly, where it can
/// be caught.
/// </summary>
public sealed class WasmThrowException : WasmException
{
    /// <summary>Creates an exception.</summary>
    /// <param name="exception">The WebAssembly exception (see <see cref="WasmStore.CreateException"/>).</param>
    public WasmThrowException(WasmExceptionReference exception)
        : base("A WebAssembly exception was thrown")
    {
        ArgumentNullException.ThrowIfNull(exception);
        Exception = exception;
    }

    /// <summary>Gets the WebAssembly exception.</summary>
    public WasmExceptionReference Exception { get; }
}

/// <summary>A memory, a table or a value cannot be allocated or grow, or an index is out of bounds (a <c>RangeError</c> in JavaScript).</summary>
public sealed class WasmAllocationException : WasmException
{
    /// <summary>Creates an exception.</summary>
    public WasmAllocationException(string message)
        : base(message)
    {
    }

    /// <summary>Creates an exception.</summary>
    public WasmAllocationException(string message, Exception? innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>A value does not match the type of its destination (a <c>TypeError</c> in JavaScript).</summary>
public sealed class WasmTypeMismatchException : WasmException
{
    /// <summary>Creates an exception.</summary>
    public WasmTypeMismatchException(string message)
        : base(message)
    {
    }

    /// <summary>Creates an exception.</summary>
    public WasmTypeMismatchException(string message, Exception? innerException)
        : base(message, innerException)
    {
    }
}
#pragma warning restore CA1032
