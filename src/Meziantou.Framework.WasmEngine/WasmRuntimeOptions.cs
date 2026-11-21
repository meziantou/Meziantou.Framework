using System.Diagnostics.Metrics;

namespace Meziantou.Framework.WasmEngine;

/// <summary>Options of a <see cref="WasmRuntime"/>.</summary>
public sealed class WasmRuntimeOptions
{
    /// <summary>
    /// Gets the maximum size of the stack of the WebAssembly code, in bytes. The default is 512 KiB. A deeper recursion
    /// traps with <see cref="WasmTrapKind.StackOverflow"/>.
    /// </summary>
    public int MaxStackSize { get; init; } = 512 * 1024;

    /// <summary>
    /// Gets the interval at which the running WebAssembly code calls <see cref="WasmStoreOptions.CheckInterrupt"/>. The
    /// default is 10 milliseconds. The timer of the runtime runs only while a store with a
    /// <see cref="WasmStoreOptions.CheckInterrupt"/> action exists (it is not disposed or collected).
    /// </summary>
    public TimeSpan InterruptionCheckInterval { get; init; } = TimeSpan.FromMilliseconds(10);

    /// <summary>
    /// Gets the maximum size of a memory, in bytes. The default and the upper limit is the maximum length of a .NET array
    /// (the bytes of the memories are .NET arrays): a memory that would be larger cannot grow. The bytes of a shared memory
    /// are allocated for its maximum size, limited by this value: a shared memory cannot grow beyond.
    /// </summary>
    public long MaxMemorySize { get; init; } = Array.MaxLength;

    /// <summary>
    /// Gets the number of calls after which a function is compiled to .NET code, when the platform supports dynamic code
    /// (see <see cref="System.Runtime.CompilerServices.RuntimeFeature.IsDynamicCodeSupported"/>); before, and on the
    /// platforms that do not support it (Native AOT), the function is interpreted. The default is 100; 0 compiles the
    /// functions on their first call, and a negative value disables the compilation. A running call whose loops ran 100
    /// times this number of iterations continues in .NET code (on-stack replacement).
    /// </summary>
    public int CompilationThreshold { get; init; } = 100;

    /// <summary>The number of loop iterations of an interpreted function after which a running call continues in .NET code (OSR), or null for 100 times <see cref="CompilationThreshold"/> (the tests set it).</summary>
    internal int? OsrThreshold { get; init; }

    /// <summary>
    /// Gets the factory of the <see cref="Meter"/> of the metrics of the runtime (see the <c>Meziantou.Framework.WasmEngine</c>
    /// meter), or <see langword="null"/> to use a meter shared by the runtimes.
    /// </summary>
    public IMeterFactory? MeterFactory { get; init; }
}

/// <summary>Options of a <see cref="WasmStore"/> (see <see cref="WasmRuntime.CreateStore"/>).</summary>
public sealed class WasmStoreOptions
{
    /// <summary>
    /// Gets the action called periodically while WebAssembly code of the store runs (see
    /// <see cref="WasmRuntimeOptions.InterruptionCheckInterval"/>): it throws to interrupt the code, and the exception is
    /// thrown by the call into WebAssembly (e.g. a timeout, or <see cref="CancellationToken.ThrowIfCancellationRequested"/>).
    /// </summary>
    public Action? CheckInterrupt { get; init; }

    /// <summary>
    /// Gets the allocator of the bytes of the shared memories created by the store, or <see langword="null"/> to allocate
    /// pinned .NET arrays.
    /// </summary>
    public WasmSharedMemoryAllocator? SharedMemoryAllocator { get; init; }

    /// <summary>
    /// Gets the maximum number of elements of a table: a table that would be larger cannot grow (<c>table.grow</c> returns
    /// -1). The default is 10,000,000, the limit of the JavaScript API of WebAssembly.
    /// </summary>
    public long MaxTableElements { get; init; } = 10_000_000;
}
