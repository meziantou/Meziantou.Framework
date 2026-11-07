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
/// A <c>WebAssembly.Memory</c> object. A shared memory is only a <c>SharedArrayBuffer</c> (the runtimes do not support the
/// threads): it cannot be imported by a module.
/// </summary>
internal sealed class WebAssemblyMemoryObject
{
    private readonly bool _is64;

    public WebAssemblyMemoryObject(WasmMemory memory)
    {
        Memory = memory;
    }

    public WebAssemblyMemoryObject(object sharedBuffer, bool is64)
    {
        BufferObject = sharedBuffer;
        IsShared = true;
        _is64 = is64;
    }

    /// <summary>[[Memory]], or null for a shared memory.</summary>
    public WasmMemory? Memory { get; }

    public bool IsShared { get; }

    public bool Is64 => Memory?.Type.Is64 ?? _is64;

    /// <summary>[[BufferObject]]: the <c>ArrayBuffer</c> (or the <c>SharedArrayBuffer</c>) of the memory (a JavaScript value).</summary>
    public object? BufferObject { get; set; }

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

/// <summary>The services of the store of an agent: the interruptions of the script engine.</summary>
internal sealed class WebAssemblyHost(IWebIdlHost host) : IWasmHost
{
    public void CheckInterrupt() => host.CheckInterrupt();
}
