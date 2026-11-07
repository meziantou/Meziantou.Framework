namespace Meziantou.Framework.WebPlatform.WebAssembly;

/// <summary>
/// A WebAssembly runtime: it compiles and runs the WebAssembly modules of the scripts, through the <c>WebAssembly</c>
/// namespace of the JavaScript realms (https://webassembly.github.io/spec/js-api/). Set it in
/// <see cref="Dom.BrowsingContextOptions.WebAssemblyRuntime"/>. The <c>Meziantou.Framework.WebPlatform.Wasmtime</c>
/// package provides a runtime based on Wasmtime.
/// </summary>
/// <remarks>
/// A runtime can be shared by several browsing contexts and script engines: the compiled modules are shared, and each
/// agent (the windows of a script engine, a worker) has its own store of instances.
/// </remarks>
public abstract class WebAssemblyRuntime
{
    private protected WebAssemblyRuntime()
    {
    }

    /// <summary>Validates a module (https://webassembly.github.io/spec/core/appendix/embedding.html#mathrm-module-validate-xref-syntax-modules-syntax-module-mathit-module). It can be called from any thread.</summary>
    internal abstract bool Validate(ReadOnlySpan<byte> bytes);

    /// <summary>Compiles a module. It can be called from any thread.</summary>
    /// <exception cref="WasmCompileException">The module is not valid.</exception>
    internal abstract WasmModule Compile(byte[] bytes);

    /// <summary>Creates the store of an agent (https://webassembly.github.io/spec/js-api/#associated-store). The store is used by a single thread.</summary>
    internal abstract WasmStore CreateStore(IWasmHost host);
}

/// <summary>The services the store of an agent needs from the script engine.</summary>
internal interface IWasmHost
{
    /// <summary>
    /// Called periodically while WebAssembly code runs: throws when the running script must be interrupted (e.g. the
    /// timeout of the script engine). The exception is thrown by the call into WebAssembly.
    /// </summary>
    void CheckInterrupt();
}
