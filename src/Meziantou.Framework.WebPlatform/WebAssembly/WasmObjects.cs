using Meziantou.Framework.WebPlatform.Scripting;

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

    /// <summary>
    /// Whether a parameter of a function type of the imports of the module is <c>(ref null (array (mut i16)))</c>: a nullable
    /// reference to the array type of the char codes of the JS String Builtins
    /// (https://webassembly.github.io/spec/js-api/#js-string-builtin-functions), a final mutable i16 array type
    /// without supertype in a recursion group of its own. A concrete heap type is not described by <see cref="WasmValueType"/>:
    /// the runtime checks the type. The default implementation returns false.
    /// </summary>
    /// <param name="functionType">A function type of an import of the module.</param>
    /// <param name="parameterIndex">The index of the parameter.</param>
    public virtual bool IsCharCodeArrayParameter(WasmFunctionType functionType, int parameterIndex) => false;
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

/// <summary>
/// A memory (a memory address). The bytes of a memory that is not shared are a .NET array, replaced when the memory grows.
/// The bytes of a shared memory are a shared data block of the script engine (<see cref="SharedBytes"/>), which never
/// moves: a shared memory can be used by any agent (its methods can be called from any thread).
/// </summary>
internal abstract class WasmMemory : WasmExtern
{
    public override WasmExternKind Kind => WasmExternKind.Memory;

    public abstract WasmMemoryType Type { get; }

    /// <summary>The bytes of a memory that is not shared (the length is the size of the memory).</summary>
    /// <exception cref="NotSupportedException">The memory is shared.</exception>
    public abstract byte[] Data { get; }

    /// <summary>The bytes of a shared memory, or null for a memory that is not shared.</summary>
    public virtual WasmSharedMemoryBytes? SharedBytes => null;

    /// <summary>
    /// Notifies the waiters of an address of a shared memory (<c>memory.atomic.notify</c>), from any thread, and returns the
    /// number of waiters notified.
    /// </summary>
    /// <exception cref="WasmTrapException">The address is out of bounds or not aligned.</exception>
    /// <exception cref="NotSupportedException">The memory is not shared.</exception>
    public virtual uint AtomicNotify(ulong address, uint count) => throw new NotSupportedException("The memory is not shared");

    /// <summary>
    /// Blocks the calling thread until the waiter of an address of a shared memory is notified or the timeout elapses
    /// (<c>memory.atomic.wait32</c> and <c>memory.atomic.wait64</c>), when the address contains <paramref name="expected"/>.
    /// It can be called from any thread.
    /// </summary>
    /// <param name="address">The address: a multiple of <paramref name="size"/>.</param>
    /// <param name="expected">The expected value (the low 32 bits for a 32-bit wait).</param>
    /// <param name="size">The size of the value in bytes: 4 or 8.</param>
    /// <param name="timeout">The maximum duration of the wait, or null to wait until notified.</param>
    /// <param name="signal">Set by the native code of the wait right before it joins the waiters of the address, or null.</param>
    /// <exception cref="WasmTrapException">The address is out of bounds or not aligned.</exception>
    /// <exception cref="NotSupportedException">The memory is not shared.</exception>
    public virtual WasmWaitResult AtomicWait(ulong address, long expected, int size, TimeSpan? timeout, WasmWaitSignal? signal = null) => throw new NotSupportedException("The memory is not shared");

    /// <summary>The size of the memory in pages.</summary>
    public abstract ulong Size { get; }

    /// <summary>Grows the memory (mem_grow) and returns its previous size in pages.</summary>
    /// <exception cref="WasmAllocationException">The memory cannot grow.</exception>
    public abstract ulong Grow(ulong delta);

    /// <summary>
    /// Grows the memory to <paramref name="newSize"/> pages, and returns its previous size in pages: the memory does not
    /// change when its size is <paramref name="newSize"/> or more. The agents that share a memory can grow it concurrently:
    /// the memory of a shared memory grows to the new size, never beyond it.
    /// </summary>
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
    /// Called after the bytes of the memory are replaced (the memory grew, by <see cref="Grow"/> or by a <c>memory.grow</c>
    /// instruction): the buffer of the memory must be refreshed (https://webassembly.github.io/spec/js-api/#refresh-the-memory-buffer).
    /// </summary>
    public Action<WasmMemory>? DataChanged { get; set; }

    protected void OnDataChanged() => DataChanged?.Invoke(this);
}

/// <summary>The result of <see cref="WasmMemory.AtomicWait"/>: the values of <c>memory.atomic.wait32</c>.</summary>
internal enum WasmWaitResult
{
    Ok = 0,
    NotEqual = 1,
    TimedOut = 2,
}

/// <summary>
/// The bytes of a shared memory: a growable shared data block of the script engine (the memory of the
/// <c>SharedArrayBuffer</c> objects of the memory), allocated once for its capacity and pinned, so that its address never
/// changes. The memory grows within the capacity. Created by <see cref="IWasmHost.CreateSharedMemoryBytes"/>. The memory
/// keeps them alive as long as it is alive: they do not reference the agent that created them.
/// </summary>
internal sealed class WasmSharedMemoryBytes(IGrowableSharedDataBlock block, ulong capacity)
{
    private int _waitersInstalled;

    /// <summary>The block of the script engine.</summary>
    public IGrowableSharedDataBlock Block { get; } = block;

    /// <summary>The shared data block of the script engine (see <c>IJsAdapter.GetSharedDataBlock</c>).</summary>
    public object DataBlock => Block.DataBlock;

    /// <summary>The address of the first byte, which never changes.</summary>
    public nint Address { get; } = block.Address;

    /// <summary>The number of bytes the memory can grow to.</summary>
    public ulong Capacity { get; } = capacity;

    /// <summary>The current size of the memory in bytes.</summary>
    public ulong ByteLength => (ulong)Block.ByteLength;

    /// <summary>Grows the bytes to <paramref name="newByteLength"/> (the new bytes are zeroed): false when it is less than the current length or greater than the capacity.</summary>
    public bool TryGrow(ulong newByteLength) => newByteLength <= Capacity && Block.TryGrow((int)newByteLength);

    /// <summary>Returns true the first time it is called: the waiters of the data block are installed once.</summary>
    public bool TryBeginInstallWaiters() => Interlocked.Exchange(ref _waitersInstalled, 1) == 0;
}

/// <summary>
/// Tells another thread that a thread is about to join the waiters of an address (see <see cref="WasmMemory.AtomicWait"/>):
/// the native code of the wait sets it right before it waits, so that a notification sent once it is set reaches the wait,
/// unless the thread stops for longer than the time the other thread waits after it sees it.
/// </summary>
internal sealed class WasmWaitSignal
{
    private byte[]? _flag;

    /// <summary>Whether the thread of the wait is about to wait (or is waiting, or waited).</summary>
    public bool IsSet => Volatile.Read(ref _flag) is { } flag && Volatile.Read(ref flag[0]) != 0;

    /// <summary>Sets the bytes whose first byte the native code of the wait sets (a memory of the helper of the wait).</summary>
    public void Attach(byte[] flag) => Volatile.Write(ref _flag, flag);
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

    /// <summary>The number of elements of an array (array.len).</summary>
    /// <exception cref="WasmTrapException">The object is not an array of the store.</exception>
    public abstract int GetArrayLength(WasmGcObject array);

    /// <summary>Reads an element of an array (array.get; a packed element is zero-extended).</summary>
    /// <exception cref="WasmTrapException">The index is out of bounds, or the object is not an array of the store.</exception>
    public abstract WasmValue GetArrayElement(WasmGcObject array, int index);

    /// <summary>Writes an element of an array (array.set) whose storage type is <paramref name="type"/> (<c>i32</c> for a packed element).</summary>
    /// <exception cref="WasmTrapException">The index is out of bounds, the array is immutable, or the object is not an array of the store.</exception>
    public abstract void SetArrayElement(WasmGcObject array, int index, WasmValue value, WasmValueType type);

    /// <summary>The JavaScript exception tag of the agent (https://webassembly.github.io/spec/js-api/#get-the-javascript-exception-tag), allocated on first use.</summary>
    public abstract WasmTag JavaScriptExceptionTag { get; }

    public abstract void Dispose();
}
