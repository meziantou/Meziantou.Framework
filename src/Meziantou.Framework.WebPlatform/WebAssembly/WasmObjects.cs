namespace Meziantou.Framework.WebPlatform.WebAssembly;

/// <summary>A compiled module. It is immutable: it can be shared by the agents (e.g. posted to a worker).</summary>
internal abstract class WasmModule
{
    private WasmModuleSections? _sections;

    protected WasmModule(byte[] bytes)
    {
        Bytes = bytes;
    }

    /// <summary>The runtime that compiled the module.</summary>
    public abstract WebAssemblyRuntime Runtime { get; }

    /// <summary>The source bytes of the module ([[Bytes]]).</summary>
    public byte[] Bytes { get; }

    /// <summary>The imports of the module, in the order of the import section (https://webassembly.github.io/spec/core/appendix/embedding.html#mathrm-module-imports-xref-syntax-modules-syntax-module-mathit-module).</summary>
    public abstract IReadOnlyList<WasmImport> Imports { get; }

    /// <summary>The exports of the module, in the order of the export section.</summary>
    public abstract IReadOnlyList<WasmExport> Exports { get; }

    /// <summary>The sections of the module that the runtime does not expose (the custom sections, the function indices of the exports).</summary>
    internal WasmModuleSections Sections => _sections ??= WasmModuleSections.Read(Bytes);
}

/// <summary>An instance of a module (a module instance).</summary>
internal abstract class WasmInstance
{
    /// <summary>The exports of the instance, in the order of the exports of the module.</summary>
    public abstract IReadOnlyList<(string Name, WasmExtern Value)> Exports { get; }
}

/// <summary>An external value (https://webassembly.github.io/spec/core/exec/runtime.html#external-values): the store returns the same object for the same address.</summary>
internal abstract class WasmExtern
{
    public abstract WasmExternKind Kind { get; }
}

/// <summary>A function (a function address).</summary>
internal abstract class WasmFunction : WasmExtern
{
    public override WasmExternKind Kind => WasmExternKind.Function;

    public abstract WasmFunctionType Type { get; }

    /// <summary>
    /// The JavaScript function of a host function created from a JavaScript function (https://webassembly.github.io/spec/js-api/#create-a-host-function),
    /// or null.
    /// </summary>
    public object? HostFunction { get; init; }

    /// <summary>Invokes the function (func_invoke).</summary>
    /// <exception cref="WasmTrapException">The execution trapped.</exception>
    /// <exception cref="WasmThrowException">The function threw an exception.</exception>
    /// <exception cref="WasmTypeMismatchException">An argument does not match the type of its parameter.</exception>
    public abstract void Invoke(ReadOnlySpan<WasmValue> arguments, Span<WasmValue> results);
}

/// <summary>A memory (a memory address). The bytes of a memory that is not shared are a .NET array, replaced when the memory grows.</summary>
internal abstract class WasmMemory : WasmExtern
{
    public override WasmExternKind Kind => WasmExternKind.Memory;

    public abstract WasmMemoryType Type { get; }

    /// <summary>The bytes of the memory (the length is the size of the memory).</summary>
    public abstract byte[] Data { get; }

    /// <summary>The size of the memory in pages.</summary>
    public abstract ulong Size { get; }

    /// <summary>Grows the memory (mem_grow) and returns its previous size in pages.</summary>
    /// <exception cref="WasmAllocationException">The memory cannot grow.</exception>
    public abstract ulong Grow(ulong delta);

    /// <summary>
    /// Called after the bytes of the memory are replaced (the memory grew, by <see cref="Grow"/> or by a <c>memory.grow</c>
    /// instruction): the buffer of the memory must be refreshed (https://webassembly.github.io/spec/js-api/#refresh-the-memory-buffer).
    /// </summary>
    public Action<WasmMemory>? DataChanged { get; set; }

    protected void OnDataChanged() => DataChanged?.Invoke(this);
}

/// <summary>A table (a table address).</summary>
internal abstract class WasmTable : WasmExtern
{
    public override WasmExternKind Kind => WasmExternKind.Table;

    public abstract WasmTableType Type { get; }

    public abstract ulong Size { get; }

    /// <summary>Reads an element (table_read).</summary>
    /// <exception cref="WasmAllocationException">The index is out of bounds.</exception>
    public abstract WasmValue Get(ulong index);

    /// <summary>Writes an element (table_write).</summary>
    /// <exception cref="WasmAllocationException">The index is out of bounds.</exception>
    /// <exception cref="WasmTypeMismatchException">The value does not match the type of the elements.</exception>
    public abstract void Set(ulong index, WasmValue value);

    /// <summary>Grows the table (table_grow) and returns its previous size.</summary>
    /// <exception cref="WasmAllocationException">The table cannot grow.</exception>
    public abstract ulong Grow(ulong delta, WasmValue value);
}

/// <summary>A global (a global address).</summary>
internal abstract class WasmGlobal : WasmExtern
{
    public override WasmExternKind Kind => WasmExternKind.Global;

    public abstract WasmGlobalType Type { get; }

    public abstract WasmValue Get();

    /// <exception cref="WasmTypeMismatchException">The value does not match the type of the global.</exception>
    public abstract void Set(WasmValue value);
}

/// <summary>A tag (a tag address).</summary>
internal abstract class WasmTag : WasmExtern
{
    public override WasmExternKind Kind => WasmExternKind.Tag;

    public abstract WasmTagType Type { get; }
}

/// <summary>The steps of a host function: the arguments are converted to the results, or an exception is thrown.</summary>
internal delegate void WasmHostCallback(ReadOnlySpan<WasmValue> arguments, Span<WasmValue> results);

/// <summary>
/// The store of an agent (https://webassembly.github.io/spec/js-api/#associated-store): the instances, functions, memories,
/// tables, globals, tags and exceptions created by the scripts of the agent. It is used by the thread of the agent.
/// </summary>
internal abstract class WasmStore : IDisposable
{
    /// <summary>Instantiates a module with the imports, in the order of <see cref="WasmModule.Imports"/> (module_instantiate).</summary>
    /// <exception cref="WasmLinkException">An import does not match the type of the import of the module.</exception>
    /// <exception cref="WasmTrapException">The start function trapped.</exception>
    /// <exception cref="WasmThrowException">The start function threw an exception.</exception>
    public abstract WasmInstance Instantiate(WasmModule module, IReadOnlyList<WasmExtern> imports);

    /// <summary>Creates a host function (func_alloc). An exception thrown by the callback is thrown by the call into WebAssembly, except a <see cref="WasmThrowException"/>, which throws its exception in WebAssembly.</summary>
    public abstract WasmFunction CreateHostFunction(WasmFunctionType type, object? hostFunction, WasmHostCallback callback);

    /// <summary>Creates a memory (mem_alloc).</summary>
    /// <exception cref="WasmAllocationException">The memory cannot be allocated.</exception>
    public abstract WasmMemory CreateMemory(WasmMemoryType type);

    /// <summary>Creates a table (table_alloc).</summary>
    /// <exception cref="WasmAllocationException">The table cannot be allocated.</exception>
    public abstract WasmTable CreateTable(WasmTableType type, WasmValue value);

    /// <summary>Creates a global (global_alloc).</summary>
    public abstract WasmGlobal CreateGlobal(WasmGlobalType type, WasmValue value);

    /// <summary>Creates a tag (tag_alloc).</summary>
    public abstract WasmTag CreateTag(WasmTagType type);

    /// <summary>Creates an exception (exn_alloc).</summary>
    public abstract WasmExceptionReference CreateException(WasmTag tag, ReadOnlySpan<WasmValue> payload);

    /// <summary>The JavaScript exception tag of the agent (https://webassembly.github.io/spec/js-api/#get-the-javascript-exception-tag), allocated on first use.</summary>
    public abstract WasmTag JavaScriptExceptionTag { get; }

    public abstract void Dispose();
}
