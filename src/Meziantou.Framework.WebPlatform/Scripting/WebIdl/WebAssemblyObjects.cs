using System.Runtime.CompilerServices;
using Meziantou.Framework.WebPlatform.WebAssembly;

namespace Meziantou.Framework.WebPlatform.Scripting;

// The platform objects of the interfaces of the WebAssembly namespace (https://webassembly.github.io/spec/js-api/). A
// memory, a table, a global, a tag and an exception have a single object per agent (the object caches of
// https://webassembly.github.io/spec/js-api/#webassembly-js-object-caches): the agent maps them to their platform object,
// whose wrapper is the JavaScript object.

/// <summary>A <c>WebAssembly.Module</c> object.</summary>
internal sealed class WebAssemblyModuleObject(WasmModule module, IReadOnlyList<string> builtinSets, string? importedStringModule)
{
    /// <summary>[[Module]] and [[Bytes]]</summary>
    public WasmModule Module { get; } = module;

    /// <summary>[[BuiltinSets]]</summary>
    public IReadOnlyList<string> BuiltinSets { get; } = builtinSets;

    /// <summary>[[ImportedStringModule]]</summary>
    public string? ImportedStringModule { get; } = importedStringModule;
}

/// <summary>A <c>WebAssembly.Instance</c> object.</summary>
internal sealed class WebAssemblyInstanceObject(WasmInstance instance, object exports)
{
    /// <summary>[[Instance]]</summary>
    public WasmInstance Instance { get; } = instance;

    /// <summary>[[Exports]]: the frozen exports object (a JavaScript value).</summary>
    public object Exports { get; } = exports;
}

/// <summary>
/// A <c>WebAssembly.Memory</c> object. The bytes of a shared memory are a shared data block of the script engine, the
/// memory of its <c>SharedArrayBuffer</c> objects.
/// </summary>
internal sealed class WebAssemblyMemoryObject(WasmMemory memory)
{
    /// <summary>[[Memory]]</summary>
    public WasmMemory Memory { get; } = memory;

    /// <summary>The growable shared data block of a shared memory (see <c>IJsAdapter.GetSharedDataBlock</c>), or null.</summary>
    public object? SharedDataBlock => Memory.SharedBytes?.DataBlock;

    public bool IsShared => Memory.SharedBytes is not null;

    public bool Is64 => Memory.Type.Is64;

    /// <summary>The maximum size of the memory in pages, or null when it has none.</summary>
    public ulong? Maximum => Memory.Type.Maximum;

    /// <summary>[[BufferObject]]: the <c>ArrayBuffer</c> (or the <c>SharedArrayBuffer</c>) of the memory (a JavaScript value).</summary>
    public object? BufferObject { get; set; }

    /// <summary>Whether <see cref="BufferObject"/> is a resizable <c>ArrayBuffer</c> (or a growable <c>SharedArrayBuffer</c>), created by <c>toResizableBuffer()</c>.</summary>
    public bool IsBufferResizable { get; set; }

    /// <summary>
    /// The byte length of the fixed-length <c>SharedArrayBuffer</c> of a shared memory: the buffer is replaced when the
    /// memory grows, possibly by another <c>WebAssembly.Memory</c> object of its data block (of another agent).
    /// </summary>
    public int SharedBufferByteLength { get; set; }

    /// <summary>The realm in which the buffers are created (the realm of the object).</summary>
    public object? Realm { get; set; }
}

/// <summary>A <c>WebAssembly.Table</c> object.</summary>
internal sealed class WebAssemblyTableObject(WasmTable table)
{
    /// <summary>[[Table]]</summary>
    public WasmTable Table { get; } = table;
}

/// <summary>A <c>WebAssembly.Global</c> object.</summary>
internal sealed class WebAssemblyGlobalObject(WasmGlobal global)
{
    /// <summary>[[Global]]</summary>
    public WasmGlobal Global { get; } = global;
}

/// <summary>A <c>WebAssembly.Tag</c> object.</summary>
internal sealed class WebAssemblyTagObject(WasmTag tag)
{
    /// <summary>[[Address]]</summary>
    public WasmTag Tag { get; } = tag;
}

/// <summary>A <c>WebAssembly.Exception</c> object.</summary>
internal sealed class WebAssemblyExceptionObject(WasmExceptionReference exception)
{
    /// <summary>[[Address]]</summary>
    public WasmExceptionReference Exception { get; } = exception;

    /// <summary>[[Type]]</summary>
    public WasmTag Tag { get; } = exception.Tag;

    /// <summary>[[Stack]]: a string, or null for undefined.</summary>
    public string? Stack { get; set; }
}

/// <summary>
/// The serialized form of a shared <c>WebAssembly.Memory</c> (https://webassembly.github.io/threads/web-api/#serialization):
/// the shared memory, which can only be deserialized in the realms of the agent cluster.
/// </summary>
internal sealed class SerializedWasmSharedMemory(WasmMemory memory, object? agentCluster)
{
    public WasmMemory Memory { get; } = memory;

    public object? AgentCluster { get; } = agentCluster;
}

/// <summary>The serialized form of a <c>WebAssembly.Module</c> (https://webassembly.github.io/spec/web-api/#serialization).</summary>
internal sealed class SerializedWasmModule(WebAssemblyModuleObject module, object? agentCluster)
{
    public WebAssemblyModuleObject Module { get; } = module;

    /// <summary>[[AgentCluster]]: the module can only be deserialized in the realms of this agent cluster.</summary>
    public object? AgentCluster { get; } = agentCluster;
}

/// <summary>The WebAssembly state of an agent: its store and its object caches.</summary>
internal sealed class WebAssemblyAgentState<TValue>(WasmStore store)
    where TValue : class
{
    /// <summary>The store of the agent (https://webassembly.github.io/spec/js-api/#associated-store).</summary>
    public WasmStore Store { get; } = store;

    /// <summary>The Memory, Table, Global and Tag object caches: the platform object of a memory, a table, a global or a tag.</summary>
    public ConditionalWeakTable<WasmExtern, object> ExternObjects { get; } = [];

    /// <summary>The Exception object cache.</summary>
    public ConditionalWeakTable<WasmExceptionReference, WebAssemblyExceptionObject> ExceptionObjects { get; } = [];

    /// <summary>
    /// The Exported Function cache. The values are the <typeparamref name="TValue"/> functions, stored as objects: a value of a
    /// <see cref="ConditionalWeakTable{TKey, TValue}"/> must have a public parameterless constructor for the trimming analysis.
    /// </summary>
    public ConditionalWeakTable<WasmFunction, object> ExportedFunctions { get; } = [];

    /// <summary>The [[FunctionAddress]] of the Exported Functions.</summary>
    public ConditionalWeakTable<TValue, WasmFunction> FunctionAddresses { get; } = [];

    /// <summary>The name of the Exported Functions (the index of the function in its module), when it is known.</summary>
    public ConditionalWeakTable<WasmFunction, string> FunctionNames { get; } = [];

    /// <summary>The Exported GC Object cache (the <typeparamref name="TValue"/> objects, see <see cref="ExportedFunctions"/>).</summary>
    public ConditionalWeakTable<WasmGcObject, object> GcObjects { get; } = [];

    /// <summary>The [[ObjectAddress]] of the Exported GC Objects.</summary>
    public ConditionalWeakTable<TValue, WasmGcObject> GcObjectAddresses { get; } = [];

    /// <summary>The host value cache (for the objects): the same host reference for the same object.</summary>
    public ConditionalWeakTable<TValue, WasmHostReference> HostReferences { get; } = [];
}

/// <summary>The services of the store of an agent: the interruptions of the script engine, and the bytes of the shared memories.</summary>
internal sealed class WebAssemblyHost(IWebIdlHost host, Func<ulong, ulong, WasmSharedMemoryBytes> createSharedMemoryBytes) : IWasmHost
{
    public void CheckInterrupt() => host.CheckInterrupt();

    public WasmSharedMemoryBytes CreateSharedMemoryBytes(ulong byteLength, ulong capacity) => createSharedMemoryBytes(byteLength, capacity);
}

/// <summary>
/// The waiters of the data block of a shared memory: <c>Atomics.wait</c>, <c>Atomics.waitAsync</c> and <c>Atomics.notify</c>
/// use the atomic instructions of the memory, so that the scripts and the WebAssembly code notify each other. A blocking
/// wait waits on the thread of the agent; an asynchronous wait waits on a thread of its own.
/// </summary>
/// <remarks>
/// The memory is referenced weakly: the native memory keeps the data block alive, which references its waiters, so a
/// strong reference would keep the memory alive forever. The <c>WebAssembly.Memory</c> objects keep the memory alive; a
/// script that only keeps a buffer of the memory cannot wait for the WebAssembly code anymore once they are collected
/// (a wait then times out immediately).
/// </remarks>
internal sealed class WasmSharedMemoryWaiters(WasmMemory target) : ISharedDataBlockWaiters
{
    /// <summary>How long the caller of <see cref="WaitAsync"/> stays a waiter after the thread of the wait is about to wait.</summary>
    private static readonly TimeSpan HandOffDuration = TimeSpan.FromMilliseconds(1);

    /// <summary>The maximum duration of the hand-off of a wait, or of the cancellation of a blocking wait.</summary>
    private static readonly TimeSpan MaxHandOffDuration = TimeSpan.FromSeconds(1);

    private readonly WeakReference<WasmMemory> _memory = new(target);

    public int Notify(int byteIndex, int count) => _memory.TryGetTarget(out var memory) ? (int)Math.Min(memory.AtomicNotify((ulong)byteIndex, (uint)count), int.MaxValue) : 0;

    public bool Wait(int byteIndex, long expectedValue, int size, TimeSpan? timeout, CancellationToken cancellationToken)
    {
        if (!_memory.TryGetTarget(out var memory) || cancellationToken.IsCancellationRequested)
            return false;

        // A canceled wait is woken by notifying the waiters of the location until it stops (the other waiters of the
        // location wake up spuriously)
        var wait = new CancelableWait(memory, byteIndex);
        WasmWaitResult result;
        using (cancellationToken.UnsafeRegister(static state => ((CancelableWait)state!).Cancel(), wait))
        {
            try
            {
                result = memory.AtomicWait((ulong)byteIndex, expectedValue, size, timeout);
            }
            finally
            {
                wait.Stop();
            }
        }

        // A value that changed since the engine checked it is a notification that happened before the wait
        return !wait.IsCanceled && result != WasmWaitResult.TimedOut;
    }

    public void WaitAsync(int byteIndex, long expectedValue, int size, TimeSpan? timeout, Action<bool> completed)
    {
        if (!_memory.TryGetTarget(out var memory))
        {
            completed(false);
            return;
        }

        var wait = new AsyncWait(memory, byteIndex, expectedValue, size, timeout, completed);
        wait.Start();

        // The waiter must be a waiter of the memory when the method returns, but the thread of the wait starts waiting later:
        // the calling thread waits until the thread of the wait is about to wait, so that a notification that comes before
        // notifies the caller instead. A notified caller completes the wait, and the thread of the wait gives the
        // notification it gets afterwards to the next waiter.
        var start = System.Diagnostics.Stopwatch.GetTimestamp();
        while (true)
        {
            var handedOff = wait.IsAboutToWait;
            WasmWaitResult result;
            try
            {
                result = memory.AtomicWait((ulong)byteIndex, expectedValue, size, HandOffDuration);
            }
            catch
            {
                return;
            }

            if (result != WasmWaitResult.TimedOut)
            {
                wait.CompleteByCaller();
                return;
            }

            if (handedOff || System.Diagnostics.Stopwatch.GetElapsedTime(start) > MaxHandOffDuration)
                return;
        }
    }

    /// <summary>A blocking wait, woken when it is canceled.</summary>
    private sealed class CancelableWait(WasmMemory memory, int byteIndex)
    {
        private int _canceled;
        private int _stopped;

        public bool IsCanceled => Volatile.Read(ref _canceled) != 0;

        public void Stop() => Volatile.Write(ref _stopped, 1);

        public void Cancel()
        {
            Volatile.Write(ref _canceled, 1);
            ThreadPool.UnsafeQueueUserWorkItem(static state =>
            {
                var start = System.Diagnostics.Stopwatch.GetTimestamp();
                while (Volatile.Read(ref state._stopped) == 0 && System.Diagnostics.Stopwatch.GetElapsedTime(start) < MaxHandOffDuration)
                {
                    try
                    {
                        state.Wake();
                    }
                    catch
                    {
                        return;
                    }

                    Thread.Sleep(1);
                }
            }, this, preferLocal: false);
        }

        private void Wake() => memory.AtomicNotify((ulong)byteIndex, uint.MaxValue);
    }

    /// <summary>An asynchronous wait: a thread of its own waits.</summary>
    private sealed class AsyncWait(WasmMemory memory, int byteIndex, long expectedValue, int size, TimeSpan? timeout, Action<bool> completed)
    {
        private readonly Thread _thread = new(static state => ((AsyncWait)state!).Run())
        {
            IsBackground = true,
            Name = "WebAssembly atomic wait",
        };

        private int _aboutToWait;
        private int _state; // 0: waiting, 1: completed

        public bool IsAboutToWait => Volatile.Read(ref _aboutToWait) != 0;

        public void Start() => _thread.UnsafeStart(this);

        /// <summary>The caller was notified while the thread was not waiting yet: the wait completes.</summary>
        public void CompleteByCaller()
        {
            if (Interlocked.Exchange(ref _state, 1) == 0)
            {
                completed(true);
            }
        }

        private void Run()
        {
            var result = WasmWaitResult.TimedOut;
            try
            {
                result = memory.AtomicWait((ulong)byteIndex, expectedValue, size, timeout, () => Volatile.Write(ref _aboutToWait, 1));
            }
            catch
            {
                // The memory cannot be used anymore (e.g. the runtime is disposed): the wait times out
            }

            if (Interlocked.Exchange(ref _state, 1) == 0)
            {
                // A value that changed since the engine checked it is a notification that happened before the wait
                completed(result != WasmWaitResult.TimedOut);
                return;
            }

            // The caller completed the wait: a notification of this thread was meant for another waiter
            if (result == WasmWaitResult.Ok)
            {
                try
                {
                    memory.AtomicNotify((ulong)byteIndex, 1);
                }
                catch
                {
                    // The memory cannot be used anymore
                }
            }
        }
    }
}
