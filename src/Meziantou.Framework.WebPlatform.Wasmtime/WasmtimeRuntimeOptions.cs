namespace Meziantou.Framework.WebPlatform.Wasmtime;

/// <summary>Options of a <see cref="WasmtimeRuntime"/>.</summary>
public sealed class WasmtimeRuntimeOptions
{
    /// <summary>
    /// Gets the maximum size of the stack of the WebAssembly code, in bytes. The default is 512 KiB. A deeper recursion
    /// throws the exception of a stack overflow of JavaScript (a <c>RangeError</c>).
    /// </summary>
    public int MaxStackSize { get; init; } = 512 * 1024;

    /// <summary>
    /// Gets the interval at which the running WebAssembly code checks whether it must be interrupted (e.g. the timeout of the
    /// script engine). The default is 10 milliseconds.
    /// </summary>
    public TimeSpan InterruptionCheckInterval { get; init; } = TimeSpan.FromMilliseconds(10);

    /// <summary>
    /// Gets the maximum size of a memory, in bytes. The default and the upper limit is the maximum length of a .NET array
    /// (the memories are .NET arrays, so that the <c>ArrayBuffer</c> objects of JavaScript share their bytes): a memory
    /// that would be larger cannot grow.
    /// </summary>
    public long MaxMemorySize { get; init; } = Array.MaxLength;
}
