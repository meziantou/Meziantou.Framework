using Meziantou.Framework.WebPlatform.WebAssembly;

namespace Meziantou.Framework.WebPlatform.Scripting;

// The type reflection of WebAssembly (https://webassembly.github.io/js-types/js-api/): the type() methods of the Memory,
// Table, Global, Tag and Function objects, and the WebAssembly.Function constructor.
public sealed partial class WebIdlRealm<TAdapter, TValue>
{
    /// <summary>FromValueType(type) (https://webassembly.github.io/js-types/js-api/#abstract-opdef-fromvaluetype): <c>funcref</c> for the function references.</summary>
    internal static string FromWasmValueType(WasmValueType type) => type is { Kind: WasmValueKind.Ref, Heap: WasmHeapKind.Func, Nullable: true } ? "funcref" : type.ToString();

    /// <summary>https://webassembly.github.io/js-types/js-api/#dom-memory-type: the members of the MemoryType dictionary, in lexicographical order.</summary>
    internal TValue GetWasmMemoryType(WasmMemory memory)
    {
        var type = memory.Type;
        var result = CreateObject();
        if (type.Is64)
        {
            SetMember(result, "address", Adapter.FromString("i64"));
        }

        if (type.Maximum is { } maximum)
        {
            SetMember(result, "maximum", FromWasmAddress(maximum, type.Is64));
        }

        // The minimum is the current size: the memory instructions and grow() change it
        SetMember(result, "minimum", FromWasmAddress(memory.Size, type.Is64));
        SetMember(result, "shared", Adapter.FromBoolean(type.Shared));
        return result;
    }

    /// <summary>https://webassembly.github.io/js-types/js-api/#dom-table-type: the members of the TableType dictionary, in lexicographical order.</summary>
    internal TValue GetWasmTableType(WasmTable table)
    {
        var type = table.Type;
        var result = CreateObject();
        if (type.Is64)
        {
            SetMember(result, "address", Adapter.FromString("i64"));
        }

        SetMember(result, "element", Adapter.FromString(FromWasmValueType(type.ElementType)));
        if (type.Maximum is { } maximum)
        {
            SetMember(result, "maximum", FromWasmAddress(maximum, type.Is64));
        }

        SetMember(result, "minimum", FromWasmAddress(table.Size, type.Is64));
        return result;
    }

    /// <summary>https://webassembly.github.io/js-types/js-api/#dom-global-type: the members of the GlobalType dictionary.</summary>
    internal TValue GetWasmGlobalType(WasmGlobal global)
    {
        var type = global.Type;
        var result = CreateObject();
        SetMember(result, "mutable", Adapter.FromBoolean(type.Mutable));
        SetMember(result, "value", Adapter.FromString(FromWasmValueType(type.ValueType)));
        return result;
    }

    /// <summary>The type() of a Tag object: the members of the TagType dictionary.</summary>
    internal TValue GetWasmTagType(WasmTag tag)
    {
        var result = CreateObject();
        SetMember(result, "parameters", ToWasmValueTypeArray(tag.Type.Parameters));
        return result;
    }

    /// <summary>https://webassembly.github.io/js-types/js-api/#dom-function-type: the members of the FunctionType dictionary.</summary>
    internal TValue GetWasmFunctionType(TValue function)
    {
        if (!WebAssemblyState.FunctionAddresses.TryGetValue(function, out var wasmFunction))
            throw CreateTypeError("The value is not a WebAssembly function");

        var type = wasmFunction.Type;
        var result = CreateObject();
        SetMember(result, "parameters", ToWasmValueTypeArray(type.Parameters));
        SetMember(result, "results", ToWasmValueTypeArray(type.Results));
        return result;
    }

    private TValue ToWasmValueTypeArray(WasmValueType[] types) => CreateArray([.. types.Select(type => Adapter.FromString(FromWasmValueType(type)))]);

    /// <summary>
    /// https://webassembly.github.io/js-types/js-api/#construct-a-new-webassembly-function: a host function of the type that
    /// calls <paramref name="callable"/>, and its Exported Function. The Exported Function is the wrapper of the returned
    /// function, which the interface constructor returns.
    /// </summary>
    internal WasmFunction CreateWasmFunction(WasmFunctionType type, TValue callable)
    {
        var function = CreateWasmHostFunction(callable, type, GetWasmIncumbentGlobal());
        var exportedFunction = GetWasmExportedFunction(function);
        Agent.Wrappers.AddOrUpdate(function, exportedFunction);
        return function;
    }

    /// <summary>
    /// The prototype of the Exported Functions: <c>WebAssembly.Function.prototype</c>
    /// (https://webassembly.github.io/js-types/js-api/#exported-functions).
    /// </summary>
    private TValue GetWasmFunctionPrototype() => EnsureInterfaceObject(WebAssemblyBindings<TAdapter, TValue>.FunctionInterface).Prototype;
}
