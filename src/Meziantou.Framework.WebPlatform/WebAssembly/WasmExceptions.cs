namespace Meziantou.Framework.WebPlatform.WebAssembly;

#pragma warning disable CA1032, CA1064 // The exceptions never leave the library: the bindings convert them to JavaScript errors

/// <summary>A module is not valid: a <c>WebAssembly.CompileError</c>.</summary>
internal sealed class WasmCompileException(string message) : Exception(message);

/// <summary>The imports do not match the module: a <c>WebAssembly.LinkError</c>.</summary>
internal sealed class WasmLinkException(string message) : Exception(message);

/// <summary>The kind of a trap.</summary>
internal enum WasmTrapKind
{
    Other,

    /// <summary>The call stack is exhausted: the same exception as a stack overflow in JavaScript.</summary>
    StackOverflow,
}

/// <summary>The execution of WebAssembly trapped: a <c>WebAssembly.RuntimeError</c>.</summary>
internal sealed class WasmTrapException(string message, WasmTrapKind kind = WasmTrapKind.Other) : Exception(message)
{
    public WasmTrapKind TrapKind { get; } = kind;
}

/// <summary>WebAssembly threw an exception (an exception address), or a host function throws one in WebAssembly.</summary>
internal sealed class WasmThrowException(WasmExceptionReference exception) : Exception("A WebAssembly exception was thrown")
{
    public WasmExceptionReference Exception { get; } = exception;
}

/// <summary>A memory or a table cannot be allocated or grow, or an index is out of bounds: a <c>RangeError</c>.</summary>
internal sealed class WasmAllocationException(string message) : Exception(message);

/// <summary>A value does not match the type of its destination: a <c>TypeError</c>.</summary>
internal sealed class WasmTypeMismatchException(string message) : Exception(message);
#pragma warning restore CA1032, CA1064
