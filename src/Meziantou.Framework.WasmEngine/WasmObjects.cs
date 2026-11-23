using System.Runtime.CompilerServices;
using Meziantou.Framework.WebPlatform.WebAssembly;

namespace Meziantou.Framework.WasmEngine;

/// <summary>
/// A compiled module (see <see cref="WasmRuntime.Compile(byte[])"/>). It is immutable: it can be instantiated by the stores of its
/// runtime, from any thread.
/// </summary>
public abstract class WasmModule
{
    private WasmModuleSections? _sections;

    private protected WasmModule(byte[] bytes)
    {
        SourceBytes = bytes;
    }

    /// <summary>Gets the runtime that compiled the module.</summary>
    public abstract WasmRuntime Runtime { get; }

    /// <summary>Gets the binary of the module.</summary>
    public ReadOnlyMemory<byte> Bytes => SourceBytes;

    /// <summary>The binary of the module (not copied).</summary>
    internal byte[] SourceBytes { get; }

    /// <summary>Gets the imports of the module, in the order of the import section (https://webassembly.github.io/spec/core/appendix/embedding.html#mathrm-module-imports-xref-syntax-modules-syntax-module-mathit-module).</summary>
    public abstract IReadOnlyList<WasmImport> Imports { get; }

    /// <summary>Gets the exports of the module, in the order of the export section.</summary>
    public abstract IReadOnlyList<WasmExport> Exports { get; }

    /// <summary>Gets whether the code of the functions was loaded from a code cache (see <see cref="WasmRuntime.Compile(byte[], ReadOnlySpan{byte})"/>).</summary>
    public abstract bool IsLoadedFromCodeCache { get; }

    /// <summary>
    /// Creates the code cache of the module: the validated and compiled code of its functions. Pass it to
    /// <see cref="WasmRuntime.Compile(byte[], ReadOnlySpan{byte})"/> with the same binary, for instance in a later run of
    /// the application, to skip the validation and the compilation of the code. The cache is only valid for the build
    /// of the engine that created it, and only for the platforms of the same kind (with or without dynamic code).
    /// </summary>
    /// <returns>The bytes of the cache.</returns>
    /// <example>
    /// <code xmlns:ref="urn:meziantou:file-referencer" ref:source="../../tests/Meziantou.Framework.WasmEngine.Tests/WasmRuntimeTests.cs" ref:region="WasmEngine_CodeCache" ref:dedent="true">
    /// var bytes = WasmRuntime.WatToWasm("""
    ///     (module
    ///       (func (export "add") (param i32 i32) (result i32)
    ///         (i32.add (local.get 0) (local.get 1))))
    ///     """);
    ///
    /// // The first run compiles the module from its binary, and saves its code cache (e.g. in a file)
    /// byte[] codeCache;
    /// using (var runtime = new WasmRuntime())
    /// {
    ///     var module = runtime.Compile(bytes);
    ///     Assert.False(module.IsLoadedFromCodeCache);
    ///     codeCache = module.CreateCodeCache();
    /// }
    ///
    /// // A later run gives the cache with the same binary: the code is not validated and compiled again. A cache that
    /// // does not match the binary or the build of the engine is ignored: the module is compiled from its binary.
    /// using (var runtime = new WasmRuntime())
    /// {
    ///     var module = runtime.Compile(bytes, codeCache);
    ///     Assert.True(module.IsLoadedFromCodeCache);
    ///
    ///     using var store = runtime.CreateStore();
    ///     var add = store.Instantiate(module).GetFunction("add")!;
    ///     Assert.Equal(42, add.Call(WasmValue.FromI32(40), WasmValue.FromI32(2))[0].I32);
    /// }
    /// </code>
    /// </example>
    public abstract byte[] CreateCodeCache();

    /// <summary>The sections of the module that the native module does not expose (the custom sections, the limits of the tables, the function indices of the exports).</summary>
    internal WasmModuleSections Sections => _sections ??= WasmModuleSections.Read(SourceBytes);

    /// <summary>Returns the contents of the custom sections of a name (https://webassembly.github.io/spec/core/binary/modules.html#custom-section), in the order of the module.</summary>
    /// <param name="name">The name of the sections.</param>
    public IReadOnlyList<ReadOnlyMemory<byte>> GetCustomSections(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        var result = new List<ReadOnlyMemory<byte>>();
        foreach (var (sectionName, offset, length) in Sections.CustomSections)
        {
            if (sectionName == name)
            {
                result.Add(Bytes.Slice(offset, length));
            }
        }

        return result;
    }

    /// <summary>
    /// Returns whether a parameter of a function type of the imports or of the exports of the module is
    /// <c>(ref null (array (mut i16)))</c>: a nullable reference to a final mutable i16 array type without supertype in a
    /// recursion group of its own (e.g. the arrays of char codes of the JS String Builtins). A concrete heap type is not
    /// described by <see cref="WasmValueType"/>: the runtime compares the types.
    /// </summary>
    /// <param name="functionType">A function type of an import or of an export of the module.</param>
    /// <param name="parameterIndex">The index of the parameter.</param>
    public virtual bool IsMutableI16ArrayParameter(WasmFunctionType functionType, int parameterIndex) => false;
}

/// <summary>An instance of a module (a module instance).</summary>
public abstract class WasmInstance
{
    private protected WasmInstance()
    {
    }

    /// <summary>Gets the exports of the instance, in the order of the exports of the module.</summary>
    public abstract IReadOnlyList<(string Name, WasmExtern Value)> Exports { get; }

    /// <summary>Returns the export of a name, or null.</summary>
    /// <param name="name">The name of the export.</param>
    public WasmExtern? GetExport(string name)
    {
        foreach (var (exportName, value) in Exports)
        {
            if (exportName == name)
                return value;
        }

        return null;
    }

    /// <summary>Returns the exported function of a name, or null when there is no such export or it is not a function.</summary>
    public WasmFunction? GetFunction(string name) => GetExport(name) as WasmFunction;

    /// <summary>Returns the exported memory of a name, or null when there is no such export or it is not a memory.</summary>
    public WasmMemory? GetMemory(string name) => GetExport(name) as WasmMemory;

    /// <summary>Returns the exported table of a name, or null when there is no such export or it is not a table.</summary>
    public WasmTable? GetTable(string name) => GetExport(name) as WasmTable;

    /// <summary>Returns the exported global of a name, or null when there is no such export or it is not a global.</summary>
    public WasmGlobal? GetGlobal(string name) => GetExport(name) as WasmGlobal;

    /// <summary>Returns the exported tag of a name, or null when there is no such export or it is not a tag.</summary>
    public WasmTag? GetTag(string name) => GetExport(name) as WasmTag;
}

/// <summary>An external value (https://webassembly.github.io/spec/core/exec/runtime.html#external-values): the store returns the same object for the same address.</summary>
public abstract class WasmExtern
{
    private protected WasmExtern()
    {
    }

    /// <summary>Gets the kind of the value.</summary>
    public abstract WasmExternKind Kind { get; }
}

/// <summary>A function (a function address).</summary>
public abstract class WasmFunction : WasmExtern
{
    private protected WasmFunction()
    {
    }

    /// <inheritdoc/>
    public override WasmExternKind Kind => WasmExternKind.Function;

    /// <summary>Gets the type of the function.</summary>
    public abstract WasmFunctionType Type { get; }

    /// <summary>Gets the state given to <see cref="WasmStore.CreateFunction"/> for a host function, or null.</summary>
    public object? HostState { get; private protected init; }

    /// <summary>Invokes the function (func_invoke).</summary>
    /// <param name="arguments">The arguments: one per parameter of the type.</param>
    /// <param name="results">Receives the results: one per result of the type.</param>
    /// <exception cref="WasmTrapException">The execution trapped.</exception>
    /// <exception cref="WasmThrowException">The function threw an exception.</exception>
    /// <exception cref="WasmTypeMismatchException">An argument does not match the type of its parameter.</exception>
    public abstract void Invoke(ReadOnlySpan<WasmValue> arguments, Span<WasmValue> results);

    /// <summary>Invokes the function (func_invoke), and returns its results.</summary>
    /// <param name="arguments">The arguments: one per parameter of the type.</param>
    /// <exception cref="WasmTrapException">The execution trapped.</exception>
    /// <exception cref="WasmThrowException">The function threw an exception.</exception>
    /// <exception cref="WasmTypeMismatchException">An argument does not match the type of its parameter.</exception>
    public WasmValue[] Call(params ReadOnlySpan<WasmValue> arguments)
    {
        var results = new WasmValue[Type.Results.Length];
        Invoke(arguments, results);
        return results;
    }
}

/// <summary>
/// A memory (a memory address). The bytes of a memory that is not shared are a .NET array (<see cref="Data"/>), replaced
/// when the memory grows. The bytes of a shared memory are a <see cref="WasmSharedMemoryBlock"/>, which never moves: a
/// shared memory can be used by the stores of every thread, and its methods can be called from any thread.
/// </summary>
public abstract class WasmMemory : WasmExtern
{
    private protected WasmMemory()
    {
    }

    /// <inheritdoc/>
    public override WasmExternKind Kind => WasmExternKind.Memory;

    /// <summary>Gets the type of the memory.</summary>
    public abstract WasmMemoryType Type { get; }

    /// <summary>
    /// Gets the bytes of a memory that is not shared (the length is the size of the memory in bytes). The array is replaced
    /// when the memory grows (see <see cref="DataChanged"/>): do not keep it.
    /// </summary>
    /// <exception cref="NotSupportedException">The memory is shared.</exception>
#pragma warning disable CA1819 // Properties should not return arrays: the array is the memory, shared with the host
    public abstract byte[] Data { get; }
#pragma warning restore CA1819

    /// <summary>Gets the bytes of a shared memory, or null for a memory that is not shared.</summary>
    public virtual WasmSharedMemoryBlock? SharedBlock => null;

    /// <summary>Returns the bytes of the memory. The span is invalid after the memory grows, unless the memory is shared.</summary>
    public virtual Span<byte> GetSpan() => Data;

    /// <summary>
    /// Notifies the waiters of an address of a shared memory (<c>memory.atomic.notify</c>), from any thread, and returns the
    /// number of waiters notified.
    /// </summary>
    /// <param name="address">The address: a multiple of 4.</param>
    /// <param name="count">The maximum number of waiters to notify.</param>
    /// <exception cref="WasmTrapException">The address is out of bounds or not aligned.</exception>
    /// <exception cref="NotSupportedException">The memory is not shared.</exception>
    public virtual uint AtomicNotify(ulong address, uint count) => throw new NotSupportedException("The memory is not shared");

    /// <summary>
    /// Blocks the calling thread until a waiter of an address of a shared memory is notified or the timeout elapses
    /// (<c>memory.atomic.wait32</c> and <c>memory.atomic.wait64</c>), when the address contains <paramref name="expected"/>.
    /// It can be called from any thread.
    /// </summary>
    /// <param name="address">The address: a multiple of <paramref name="size"/>.</param>
    /// <param name="expected">The expected value (the low 32 bits for a 32-bit wait).</param>
    /// <param name="size">The size of the value in bytes: 4 or 8.</param>
    /// <param name="timeout">The maximum duration of the wait, or null to wait until notified.</param>
    /// <param name="signal">Set right before the thread joins the waiters of the address, or null.</param>
    /// <exception cref="WasmTrapException">The address is out of bounds or not aligned.</exception>
    /// <exception cref="NotSupportedException">The memory is not shared.</exception>
    public virtual WasmWaitResult AtomicWait(ulong address, long expected, int size, TimeSpan? timeout, WasmWaitSignal? signal = null) => throw new NotSupportedException("The memory is not shared");

    /// <summary>Gets the size of the memory in pages.</summary>
    public abstract ulong Size { get; }

    /// <summary>Grows the memory (mem_grow) and returns its previous size in pages.</summary>
    /// <param name="delta">The number of pages to add.</param>
    /// <exception cref="WasmAllocationException">The memory cannot grow.</exception>
    public abstract ulong Grow(ulong delta);

    /// <summary>
    /// Grows the memory to <paramref name="newSize"/> pages, and returns its previous size in pages: the memory does not
    /// change when its size is <paramref name="newSize"/> or more. The threads that share a memory can grow it concurrently:
    /// a shared memory grows to the new size, never beyond it.
    /// </summary>
    /// <param name="newSize">The size in pages.</param>
    /// <exception cref="WasmAllocationException">The memory cannot grow.</exception>
    public virtual ulong GrowTo(ulong newSize)
    {
        var size = Size;
        if (newSize > size)
        {
            Grow(newSize - size);
        }

        return size;
    }

    /// <summary>
    /// Occurs after the bytes of a memory that is not shared are replaced: the memory grew, by <see cref="Grow"/> or by a
    /// <c>memory.grow</c> instruction. It is raised on the thread of the store.
    /// </summary>
    public event EventHandler? DataChanged;

    private protected void OnDataChanged() => DataChanged?.Invoke(this, EventArgs.Empty);
}

/// <summary>The result of <see cref="WasmMemory.AtomicWait"/>: the values of <c>memory.atomic.wait32</c>.</summary>
public enum WasmWaitResult
{
    /// <summary>The waiter was notified.</summary>
    Ok = 0,

    /// <summary>The address does not contain the expected value.</summary>
    NotEqual = 1,

    /// <summary>The timeout elapsed.</summary>
    TimedOut = 2,
}

/// <summary>
/// Tells another thread that a thread is about to join the waiters of an address (see <see cref="WasmMemory.AtomicWait"/>):
/// the native code of the wait sets it right before it waits, so that a notification sent once it is set reaches the wait,
/// unless the thread stops for longer than the time the other thread waits after it sees it.
/// </summary>
public sealed class WasmWaitSignal
{
    private byte[]? _flag;

    /// <summary>Gets a value indicating whether the thread of the wait is about to wait (or is waiting, or waited).</summary>
    public bool IsSet => Volatile.Read(ref _flag) is { } flag && Volatile.Read(ref flag[0]) != 0;

    /// <summary>Sets the bytes whose first byte the native code of the wait sets (a memory of the helper of the wait).</summary>
    internal void Attach(byte[] flag) => Volatile.Write(ref _flag, flag);
}

/// <summary>
/// The bytes of a shared memory, allocated once for the capacity of the memory, whose address never changes: the memory
/// grows within the capacity. The default blocks are pinned .NET arrays; set <see cref="WasmStoreOptions.SharedMemoryAllocator"/>
/// to provide the bytes (e.g. the data block of a <c>SharedArrayBuffer</c> of a script engine). The members can be called
/// from any thread.
/// </summary>
public abstract class WasmSharedMemoryBlock
{
    /// <summary>Initializes a block.</summary>
    /// <param name="address">The address of the first byte, which must not change while the block is alive.</param>
    /// <param name="capacity">The number of bytes allocated at <paramref name="address"/>.</param>
    protected WasmSharedMemoryBlock(nint address, ulong capacity)
    {
        Address = address;
        Capacity = capacity;
    }

    /// <summary>Gets the address of the first byte, which never changes.</summary>
    public nint Address { get; }

    /// <summary>Gets the number of bytes the memory can grow to.</summary>
    public ulong Capacity { get; }

    /// <summary>Gets the current size of the memory in bytes.</summary>
    public abstract ulong ByteLength { get; }

    /// <summary>
    /// Grows the bytes to <paramref name="newByteLength"/> (the new bytes must be zeroed), and returns false when it is less
    /// than the current length or greater than the capacity.
    /// </summary>
    public abstract bool TryGrow(ulong newByteLength);

    /// <summary>Returns the bytes of the memory: the span is valid while the block is alive.</summary>
    public unsafe Span<byte> GetSpan() => new((void*)Address, checked((int)ByteLength));
}

/// <summary>A <see cref="WasmSharedMemoryBlock"/> whose bytes are a pinned .NET array.</summary>
internal sealed class PinnedArraySharedMemoryBlock : WasmSharedMemoryBlock
{
    private readonly byte[] _data;
    private ulong _byteLength;

    private PinnedArraySharedMemoryBlock(byte[] data, ulong byteLength)
        : base(GetAddress(data), (ulong)data.LongLength)
    {
        _data = data;
        _byteLength = byteLength;
    }

    public static PinnedArraySharedMemoryBlock Create(ulong byteLength, ulong capacity)
    {
        if (capacity > (ulong)Array.MaxLength)
            throw new WasmAllocationException("The shared memory is too large");

        return new PinnedArraySharedMemoryBlock(GC.AllocateArray<byte>((int)capacity, pinned: true), byteLength);
    }

    private static unsafe nint GetAddress(byte[] data) => (nint)Unsafe.AsPointer(ref System.Runtime.InteropServices.MemoryMarshal.GetArrayDataReference(data));

    public override ulong ByteLength => Volatile.Read(ref _byteLength);

    public override bool TryGrow(ulong newByteLength)
    {
        while (true)
        {
            var current = Volatile.Read(ref _byteLength);
            if (newByteLength < current || newByteLength > Capacity)
                return false;

            if (Interlocked.CompareExchange(ref _byteLength, newByteLength, current) == current)
            {
                GC.KeepAlive(_data);
                return true;
            }
        }
    }
}

/// <summary>Allocates the bytes of a shared memory (see <see cref="WasmStoreOptions.SharedMemoryAllocator"/>).</summary>
/// <param name="byteLength">The initial size of the memory in bytes.</param>
/// <param name="capacity">The maximum size of the memory in bytes: the bytes are allocated for it.</param>
/// <returns>The bytes of the memory. Throw a <see cref="WasmAllocationException"/> when they cannot be allocated.</returns>
public delegate WasmSharedMemoryBlock WasmSharedMemoryAllocator(ulong byteLength, ulong capacity);

/// <summary>A table (a table address).</summary>
public abstract class WasmTable : WasmExtern
{
    private protected WasmTable()
    {
    }

    /// <inheritdoc/>
    public override WasmExternKind Kind => WasmExternKind.Table;

    /// <summary>Gets the type of the table.</summary>
    public abstract WasmTableType Type { get; }

    /// <summary>Gets the number of elements.</summary>
    public abstract ulong Size { get; }

    /// <summary>Reads an element (table_read).</summary>
    /// <exception cref="WasmAllocationException">The index is out of bounds.</exception>
    public abstract WasmValue Get(ulong index);

    /// <summary>Writes an element (table_write).</summary>
    /// <exception cref="WasmAllocationException">The index is out of bounds.</exception>
    /// <exception cref="WasmTypeMismatchException">The value does not match the type of the elements.</exception>
    public abstract void Set(ulong index, WasmValue value);

    /// <summary>Grows the table (table_grow) and returns its previous size.</summary>
    /// <param name="delta">The number of elements to add.</param>
    /// <param name="value">The value of the new elements.</param>
    /// <exception cref="WasmAllocationException">The table cannot grow.</exception>
    public abstract ulong Grow(ulong delta, WasmValue value);
}

/// <summary>A global (a global address).</summary>
public abstract class WasmGlobal : WasmExtern
{
    private protected WasmGlobal()
    {
    }

    /// <inheritdoc/>
    public override WasmExternKind Kind => WasmExternKind.Global;

    /// <summary>Gets the type of the global.</summary>
    public abstract WasmGlobalType Type { get; }

    /// <summary>Reads the value (global_read).</summary>
    public abstract WasmValue Get();

    /// <summary>Writes the value (global_write).</summary>
    /// <exception cref="WasmTypeMismatchException">The global is immutable, or the value does not match its type.</exception>
    public abstract void Set(WasmValue value);
}

/// <summary>A tag (a tag address): the identity of the exceptions.</summary>
public abstract class WasmTag : WasmExtern
{
    private protected WasmTag()
    {
    }

    /// <inheritdoc/>
    public override WasmExternKind Kind => WasmExternKind.Tag;

    /// <summary>Gets the type of the tag.</summary>
    public abstract WasmTagType Type { get; }
}

/// <summary>
/// The steps of a host function: the arguments are converted to the results, or an exception is thrown. A
/// <see cref="WasmThrowException"/> throws its exception in WebAssembly; any other exception aborts the execution and is
/// thrown by the call into WebAssembly.
/// </summary>
/// <param name="arguments">The arguments: one per parameter of the type of the function.</param>
/// <param name="results">Receives the results: one per result of the type of the function.</param>
public delegate void WasmHostCallback(ReadOnlySpan<WasmValue> arguments, Span<WasmValue> results);

/// <summary>The imports of an instantiation, by module and name (see <see cref="WasmStore.Instantiate(WasmModule, WasmImports)"/>).</summary>
public sealed class WasmImports
{
    private readonly Dictionary<(string Module, string Name), WasmExtern> _values = [];

    /// <summary>Defines an import, or replaces its value.</summary>
    /// <param name="module">The name of the module of the import.</param>
    /// <param name="name">The name of the import.</param>
    /// <param name="value">The value of the import.</param>
    /// <returns>The imports.</returns>
    public WasmImports Define(string module, string name, WasmExtern value)
    {
        ArgumentNullException.ThrowIfNull(module);
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(value);
        _values[(module, name)] = value;
        return this;
    }

    /// <summary>Defines the exports of an instance as the imports of a module.</summary>
    /// <param name="module">The name of the module of the imports.</param>
    /// <param name="instance">The instance whose exports are defined.</param>
    /// <returns>The imports.</returns>
    public WasmImports Define(string module, WasmInstance instance)
    {
        ArgumentNullException.ThrowIfNull(instance);
        foreach (var (name, value) in instance.Exports)
        {
            Define(module, name, value);
        }

        return this;
    }

    /// <summary>Returns the value of an import, or null.</summary>
    public WasmExtern? Get(string module, string name) => _values.TryGetValue((module, name), out var value) ? value : null;
}

/// <summary>
/// A store (https://webassembly.github.io/spec/core/exec/runtime.html#store): the instances, functions, memories, tables,
/// globals, tags and exceptions created in it. A store is not thread-safe: use it from one thread at a time. Dispose it to
/// release its native resources; a store that is not disposed is released when it is collected.
/// </summary>
public abstract class WasmStore : IDisposable
{
    private protected WasmStore()
    {
    }

    /// <summary>Gets the runtime of the store.</summary>
    public abstract WasmRuntime Runtime { get; }

    /// <summary>Instantiates a module with the imports, in the order of <see cref="WasmModule.Imports"/> (module_instantiate).</summary>
    /// <param name="module">A module of the runtime of the store.</param>
    /// <param name="imports">The values of the imports, in the order of <see cref="WasmModule.Imports"/>.</param>
    /// <exception cref="WasmLinkException">An import does not match the type of the import of the module.</exception>
    /// <exception cref="WasmTrapException">The start function trapped.</exception>
    /// <exception cref="WasmThrowException">The start function threw an exception.</exception>
    public abstract WasmInstance Instantiate(WasmModule module, IReadOnlyList<WasmExtern> imports);

    /// <summary>Instantiates a module with the imports of their module and name (module_instantiate).</summary>
    /// <param name="module">A module of the runtime of the store.</param>
    /// <param name="imports">The values of the imports.</param>
    /// <exception cref="WasmLinkException">An import is missing, or does not match the type of the import of the module.</exception>
    /// <exception cref="WasmTrapException">The start function trapped.</exception>
    /// <exception cref="WasmThrowException">The start function threw an exception.</exception>
    public WasmInstance Instantiate(WasmModule module, WasmImports imports)
    {
        ArgumentNullException.ThrowIfNull(module);
        ArgumentNullException.ThrowIfNull(imports);
        var moduleImports = module.Imports;
        var values = new WasmExtern[moduleImports.Count];
        for (var i = 0; i < values.Length; i++)
        {
            var import = moduleImports[i];
            values[i] = imports.Get(import.Module, import.Name) ?? throw new WasmLinkException($"The import '{import.Module}'.'{import.Name}' is not defined");
        }

        return Instantiate(module, values);
    }

    /// <summary>Instantiates a module without imports (module_instantiate).</summary>
    /// <param name="module">A module of the runtime of the store.</param>
    /// <exception cref="WasmLinkException">The module has imports.</exception>
    /// <exception cref="WasmTrapException">The start function trapped.</exception>
    /// <exception cref="WasmThrowException">The start function threw an exception.</exception>
    public WasmInstance Instantiate(WasmModule module) => Instantiate(module, Array.Empty<WasmExtern>());

    /// <summary>Creates a host function (func_alloc).</summary>
    /// <param name="type">The type of the function.</param>
    /// <param name="callback">The steps of the function.</param>
    /// <param name="state">A value associated with the function (<see cref="WasmFunction.HostState"/>).</param>
    public abstract WasmFunction CreateFunction(WasmFunctionType type, WasmHostCallback callback, object? state = null);

    /// <summary>Creates a memory (mem_alloc).</summary>
    /// <exception cref="WasmAllocationException">The memory cannot be allocated.</exception>
    public abstract WasmMemory CreateMemory(WasmMemoryType type);

    /// <summary>Creates a table (table_alloc) whose elements are <paramref name="value"/>.</summary>
    /// <exception cref="WasmAllocationException">The table cannot be allocated.</exception>
    public abstract WasmTable CreateTable(WasmTableType type, WasmValue value);

    /// <summary>Creates a global (global_alloc).</summary>
    /// <exception cref="WasmTypeMismatchException">The value does not match the type.</exception>
    public abstract WasmGlobal CreateGlobal(WasmGlobalType type, WasmValue value);

    /// <summary>Creates a tag (tag_alloc).</summary>
    public abstract WasmTag CreateTag(WasmTagType type);

    /// <summary>Creates an exception (exn_alloc), which a host function can throw with a <see cref="WasmThrowException"/>.</summary>
    /// <exception cref="WasmTypeMismatchException">The payload does not match the type of the tag.</exception>
    public abstract WasmExceptionReference CreateException(WasmTag tag, ReadOnlySpan<WasmValue> payload);

    /// <summary>Returns the number of elements of an array (array.len).</summary>
    /// <exception cref="WasmTrapException">The object is not an array of the store.</exception>
    public abstract int GetArrayLength(WasmGcObject array);

    /// <summary>Reads an element of an array (array.get; a packed element is zero-extended).</summary>
    /// <exception cref="WasmTrapException">The index is out of bounds, or the object is not an array of the store.</exception>
    public abstract WasmValue GetArrayElement(WasmGcObject array, int index);

    /// <summary>Writes an element of an array (array.set) whose storage type is <paramref name="type"/> (<c>i32</c> for a packed element).</summary>
    /// <exception cref="WasmTrapException">The index is out of bounds, the array is immutable, or the object is not an array of the store.</exception>
    public abstract void SetArrayElement(WasmGcObject array, int index, WasmValue value, WasmValueType type);

    /// <summary>Releases the native store. The objects of the store must not be used afterwards.</summary>
    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    /// <summary>Releases the native store.</summary>
    /// <param name="disposing">Whether the method is called by <see cref="Dispose()"/>.</param>
    protected virtual void Dispose(bool disposing)
    {
    }
}
