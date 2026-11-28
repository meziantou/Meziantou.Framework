using Meziantou.Framework.WasmEngine.Wasi.Internal;

namespace Meziantou.Framework.WasmEngine.Wasi;

/// <summary>
/// The environment of a WASI Preview 1 program (<c>wasi_snapshot_preview1</c>): its arguments, environment variables,
/// standard streams, clocks, random numbers, and the file system of its preopened directories. Define its imports
/// (<see cref="DefineImports"/>), instantiate the module, and run it (<see cref="Run"/>).
/// </summary>
/// <remarks>
/// <para>
/// A context is the state of one program, such as its open files: use one context per instance, from one thread at a
/// time (the thread of the store). Dispose it to close the files the program left open.
/// </para>
/// <para>
/// The functions of WASI read and write the memory that the module exports as <c>memory</c>. They do not support the
/// sockets (they return <see cref="WasiErrno.NotSocket"/>) and the signals (<see cref="WasiErrno.NotImplemented"/>).
/// </para>
/// </remarks>
/// <example>
/// <code xmlns:ref="urn:meziantou:file-referencer" ref:source="../../tests/Meziantou.Framework.WasmEngine.Wasi.Tests/WasiContextTests.cs" ref:region="Wasi_GettingStarted" ref:dedent="true">
/// using var runtime = new WasmRuntime();
///
/// // A command that writes "Hello, WASI!" to its standard output (file descriptor 1) with fd_write
/// var module = runtime.Compile(WasmRuntime.WatToWasm("""
///     (module
///       (import "wasi_snapshot_preview1" "fd_write" (func $fd_write (param i32 i32 i32 i32) (result i32)))
///       (memory (export "memory") 1)
///       (data (i32.const 8) "Hello, WASI!\n")
///       (func (export "_start")
///         ;; An iovec at 0: the address and the length of the text
///         (i32.store (i32.const 0) (i32.const 8))
///         (i32.store (i32.const 4) (i32.const 13))
///         (drop (call $fd_write (i32.const 1) (i32.const 0) (i32.const 1) (i32.const 32)))))
///     """));
///
/// using var output = new MemoryStream();
/// using var wasi = new WasiContext(new WasiOptions
/// {
///     Arguments = ["hello.wasm"],
///     StandardOutput = output,
/// });
///
/// using var store = runtime.CreateStore();
/// var instance = store.Instantiate(module, wasi.DefineImports(store));
/// var exitCode = wasi.Run(instance);
///
/// Assert.Equal(0, exitCode);
/// Assert.Equal("Hello, WASI!\n", Encoding.UTF8.GetString(output.ToArray()));
/// </code>
/// </example>
public sealed class WasiContext : IDisposable
{
    private readonly WasiImplementation _implementation;
    private readonly WasiFunctions _functions;
    private bool _disposed;

    /// <summary>Creates the environment of a program.</summary>
    /// <param name="options">The options, or null for a program without arguments, environment variables, input, output and files.</param>
    /// <exception cref="ArgumentException">An argument or an environment variable contains a null character, or the name of an environment variable is empty or contains <c>=</c>.</exception>
    public WasiContext(WasiOptions? options = null)
    {
        _implementation = new WasiImplementation(options ?? new WasiOptions());
        _functions = new WasiFunctions(_implementation);
    }

    /// <summary>Defines the functions of WASI Preview 1, the imports of the module <c>wasi_snapshot_preview1</c>.</summary>
    /// <param name="store">The store that creates the host functions, where the module is instantiated.</param>
    /// <param name="imports">The imports where the functions are defined, or null to create them.</param>
    /// <returns>The imports.</returns>
    /// <exception cref="ObjectDisposedException">The context is disposed.</exception>
    public WasmImports DefineImports(WasmStore store, WasmImports? imports = null)
    {
        ArgumentNullException.ThrowIfNull(store);
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _functions.DefineWasmImports(store, imports);
    }

    /// <summary>
    /// Runs a command: calls the export <c>_start</c> of the instance, and returns the exit code of the program, 0 when
    /// <c>_start</c> returns, or the code of <c>proc_exit</c>.
    /// </summary>
    /// <param name="instance">An instance of the module, instantiated with the imports of <see cref="DefineImports"/>.</param>
    /// <returns>The exit code of the program.</returns>
    /// <exception cref="InvalidOperationException">The instance does not export a function <c>_start</c>.</exception>
    /// <exception cref="WasmTrapException">The program trapped.</exception>
    /// <exception cref="ObjectDisposedException">The context is disposed.</exception>
    public int Run(WasmInstance instance)
    {
        ArgumentNullException.ThrowIfNull(instance);
        ObjectDisposedException.ThrowIf(_disposed, this);
        var start = instance.GetFunction("_start") ?? throw new InvalidOperationException("The instance does not export a function '_start': it is not a WASI command");
        try
        {
            start.Call();
            return 0;
        }
        catch (WasiExitException ex)
        {
            return ex.ExitCode;
        }
    }

    /// <summary>Initializes a reactor (a library): calls the export <c>_initialize</c> of the instance, when it has one. Then call the exports of the instance.</summary>
    /// <param name="instance">An instance of the module, instantiated with the imports of <see cref="DefineImports"/>.</param>
    /// <exception cref="WasiExitException">The program called <c>proc_exit</c>.</exception>
    /// <exception cref="WasmTrapException">The program trapped.</exception>
    /// <exception cref="ObjectDisposedException">The context is disposed.</exception>
    public void Initialize(WasmInstance instance)
    {
        ArgumentNullException.ThrowIfNull(instance);
        ObjectDisposedException.ThrowIf(_disposed, this);
        instance.GetFunction("_initialize")?.Call();
    }

    /// <summary>Closes the files that the program left open. The streams of <see cref="WasiOptions"/> are not closed.</summary>
    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _implementation.Dispose();
    }

    internal WasiImplementation Implementation => _implementation;
}
