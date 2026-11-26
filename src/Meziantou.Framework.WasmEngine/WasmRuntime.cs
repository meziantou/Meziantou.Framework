using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace Meziantou.Framework.WasmEngine;

/// <summary>
/// A WebAssembly runtime written in .NET: it decodes and validates the modules (<see cref="Compile(byte[])"/>), and creates
/// the stores where they are instantiated (<see cref="CreateStore"/>). The code is interpreted, and the functions that run
/// often are compiled to .NET code when the platform supports dynamic code.
/// </summary>
/// <remarks>
/// <para>
/// The WebAssembly 3.0 features are enabled (the reference types, the typed function references, the GC types, the
/// exceptions, the tail calls, memory64, the multiple memories, the SIMD and relaxed SIMD instructions, the wide
/// arithmetic), and the threads (the shared memories and the atomic instructions).
/// </para>
/// <para>
/// The bytes of a memory are a .NET array (<see cref="WasmMemory.Data"/>), replaced when the memory grows. The bytes of a
/// shared memory are allocated once for the maximum size of the memory (<see cref="WasmStoreOptions.SharedMemoryAllocator"/>):
/// the stores of every thread can import it.
/// </para>
/// <para>
/// The runtime and the modules can be used from any thread; a store is used by one thread at a time. The running code
/// calls <see cref="WasmStoreOptions.CheckInterrupt"/> periodically, so that the host can interrupt it.
/// </para>
/// </remarks>
public sealed class WasmRuntime : IDisposable
{
    private static readonly WasmStoreOptions DefaultStoreOptions = new();

    private readonly WasmEngineTelemetry _telemetry;
    private int _disposed;

    /// <summary>Creates a runtime.</summary>
    /// <param name="options">The options, or null for the default options.</param>
    /// <exception cref="ArgumentOutOfRangeException">The interval of the interruption checks or the maximum sizes are not positive.</exception>
    public WasmRuntime(WasmRuntimeOptions? options = null)
    {
        Options = options ?? new WasmRuntimeOptions();
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(Options.InterruptionCheckInterval, TimeSpan.Zero, nameof(options));
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(Options.MaxStackSize, 0, nameof(options));
        ArgumentOutOfRangeException.ThrowIfNegative(Options.MaxMemorySize, nameof(options));
        _telemetry = WasmEngineTelemetry.Get(Options.MeterFactory);

        Interruptions = new InterruptionTimer(Options.InterruptionCheckInterval);
    }

    /// <summary>Gets the options of the runtime.</summary>
    public WasmRuntimeOptions Options { get; }

    internal WasmEngineTelemetry Telemetry => _telemetry;

    internal TypeRegistry Registry { get; } = new();

    /// <summary>The timer that asks the running code to call <see cref="WasmStoreOptions.CheckInterrupt"/>.</summary>
    internal InterruptionTimer Interruptions { get; }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);

    /// <summary>Validates a module (https://webassembly.github.io/spec/core/appendix/embedding.html#mathrm-module-validate-xref-syntax-modules-syntax-module-mathit-module). It can be called from any thread.</summary>
    /// <param name="bytes">The binary of the module.</param>
    /// <returns>Whether the module is valid.</returns>
    /// <exception cref="ObjectDisposedException">The runtime is disposed.</exception>
    public bool Validate(ReadOnlySpan<byte> bytes)
    {
        ThrowIfDisposed();
        try
        {
            Decode(bytes.ToArray());
            return true;
        }
        catch (WasmDecodeException)
        {
            return false;
        }
    }

    private ModuleInfo Decode(byte[] bytes) => ModuleDecoder.Decode(bytes, Registry);

    /// <summary>Compiles a module. It can be called from any thread.</summary>
    /// <param name="bytes">The binary of the module. The module keeps the array: do not change it.</param>
    /// <returns>The module, which can be instantiated by the stores of the runtime.</returns>
    /// <exception cref="WasmCompileException">The module is not valid.</exception>
    /// <exception cref="ObjectDisposedException">The runtime is disposed.</exception>
    public WasmModule Compile(byte[] bytes) => Compile(bytes, codeCache: default);

    /// <summary>
    /// Compiles a module, with the code of its functions from a code cache (<see cref="WasmModule.CreateCodeCache"/>)
    /// when the cache was created for this binary by this build of the engine: the validation and the compilation of
    /// the code are skipped. Otherwise, the module is compiled from its binary (<see cref="WasmModule.IsLoadedFromCodeCache"/>
    /// is false). It can be called from any thread.
    /// </summary>
    /// <remarks>
    /// A code cache is trusted like a compiled assembly: its code is not validated again. Store it where only the
    /// application can write. The cache detects the changes of the binary, of the engine and the corruptions, but not
    /// a cache written on purpose to match the binary.
    /// </remarks>
    /// <param name="bytes">The binary of the module. The module keeps the array: do not change it.</param>
    /// <param name="codeCache">The code cache of the module, or an empty span.</param>
    /// <returns>The module, which can be instantiated by the stores of the runtime.</returns>
    /// <exception cref="WasmCompileException">The module is not valid.</exception>
    /// <exception cref="ObjectDisposedException">The runtime is disposed.</exception>
    public WasmModule Compile(byte[] bytes, ReadOnlySpan<byte> codeCache)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        ThrowIfDisposed();
        using var activity = WasmEngineTelemetry.StartCompile(bytes.Length);
        var start = Stopwatch.GetTimestamp();
        try
        {
            var module = TryLoadFromCodeCache(bytes, codeCache);
            if (module is null)
            {
                ModuleInfo info;
                try
                {
                    info = Decode(bytes);
                }
                catch (WasmDecodeException ex)
                {
                    throw new WasmCompileException(ex.Message, ex);
                }

                module = new ManagedModule(this, info, bytes);
            }

            _telemetry.RecordCompile(Stopwatch.GetElapsedTime(start), bytes.Length, exception: null);
            return module;
        }
        catch (WasmCompileException ex)
        {
            WasmEngineTelemetry.SetError(activity, ex);
            _telemetry.RecordCompile(Stopwatch.GetElapsedTime(start), bytes.Length, ex);
            throw;
        }
    }

    private ManagedModule? TryLoadFromCodeCache(byte[] bytes, ReadOnlySpan<byte> codeCache)
    {
        if (codeCache.IsEmpty || !CodeCache.IsValid(bytes, codeCache))
            return null;

        try
        {
            var info = ModuleDecoder.DecodeWithoutCode(bytes, Registry);
            CodeCache.ReadCode(codeCache, info);
            return new ManagedModule(this, info, bytes) { LoadedFromCodeCache = true };
        }
        catch (Exception ex) when (ex is WasmDecodeException or CodeCacheFormatException)
        {
            // A cache of this build for this binary is always readable: the module is compiled from its binary anyway
            return null;
        }
    }

    /// <summary>Compiles a module. It can be called from any thread.</summary>
    /// <param name="bytes">The binary of the module, which is copied.</param>
    /// <returns>The module, which can be instantiated by the stores of the runtime.</returns>
    /// <exception cref="WasmCompileException">The module is not valid.</exception>
    /// <exception cref="ObjectDisposedException">The runtime is disposed.</exception>
    public WasmModule Compile(ReadOnlySpan<byte> bytes) => Compile(bytes.ToArray());

    /// <summary>Creates a store, where the modules of the runtime are instantiated. A store is used by one thread at a time.</summary>
    /// <param name="options">The options of the store, or null for the default options.</param>
    /// <returns>The store. Dispose it when it is no longer used.</returns>
    /// <exception cref="ObjectDisposedException">The runtime is disposed.</exception>
    public WasmStore CreateStore(WasmStoreOptions? options = null)
    {
        ThrowIfDisposed();
        return new ManagedStore(this, options ?? DefaultStoreOptions);
    }

    /// <summary>Converts the text format of a module to its binary format (https://webassembly.github.io/spec/core/text/index.html).</summary>
    /// <param name="text">The text of the module.</param>
    /// <returns>The binary of the module. It is not validated: a module in the binary form, <c>(module binary "...")</c>, gives its bytes as they are.</returns>
    /// <exception cref="FormatException">The text is not valid.</exception>
    public static byte[] WatToWasm(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        try
        {
            return Text.WatParser.ParseModule(text);
        }
        catch (Text.WatException ex)
        {
            throw new FormatException(ex.Message, ex);
        }
    }

    /// <summary>Releases the timer of the interruptions. The stores and the modules must not be used afterwards.</summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        Interruptions.Dispose();
    }
}
