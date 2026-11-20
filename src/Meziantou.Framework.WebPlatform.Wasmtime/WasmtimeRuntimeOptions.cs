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
    /// that would be larger cannot grow. The bytes of a shared memory are allocated for its maximum size, limited by this
    /// value: a shared memory cannot grow beyond.
    /// </summary>
    public long MaxMemorySize { get; init; } = Array.MaxLength;

    /// <summary>
    /// Gets the folder where the compiled code of the modules is saved, to be loaded instead of compiled again when the same
    /// module is compiled later, by this runtime or by another one (another test, another run of the tests). The default is
    /// <see langword="null"/>: the modules are compiled each time. Compiling a large module, such as the runtime of .NET in
    /// a Blazor WebAssembly application, takes seconds, while loading it from the folder takes milliseconds.
    /// </summary>
    /// <remarks>
    /// The files are native code that the runtime loads and runs without validating it: use a folder that only the current
    /// user can write to. A file that does not match the version or the configuration of Wasmtime is replaced. The files
    /// are never deleted: they are about ten times as large as the modules.
    /// </remarks>
    public string? ModuleCacheDirectory { get; init; }
}
