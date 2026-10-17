using System.Runtime.InteropServices;
using Meziantou.Framework.WebPlatform.Wasmtime.Interop;
using Meziantou.Framework.WebPlatform.WebAssembly;

namespace Meziantou.Framework.WebPlatform.Wasmtime;

/// <summary>An owned type of the C API (<c>wasm_functype_t</c>, <c>wasm_globaltype_t</c>, <c>wasm_tagtype_t</c>), deleted when the handle is released.</summary>
internal sealed class NativeTypeHandle : SafeHandle
{
    private readonly WasmExternKind _kind;

    public NativeTypeHandle(nint handle, WasmExternKind kind)
        : base(0, ownsHandle: true)
    {
        _kind = kind;
        SetHandle(handle);
    }

    public override bool IsInvalid => handle == 0;

    protected override bool ReleaseHandle()
    {
        switch (_kind)
        {
            case WasmExternKind.Function:
                NativeMethods.wasm_functype_delete(handle);
                break;
            case WasmExternKind.Global:
                NativeMethods.wasm_globaltype_delete(handle);
                break;
            case WasmExternKind.Table:
                NativeMethods.wasm_tabletype_delete(handle);
                break;
            case WasmExternKind.Tag:
                NativeMethods.wasm_tagtype_delete(handle);
                break;
        }

        return true;
    }
}

/// <summary>Converts the types of the C API to the types of the runtime abstraction, and back.</summary>
internal static unsafe class WasmtimeTypes
{
    /// <summary>Reads a value type (<c>wasm_valtype_t</c>, borrowed).</summary>
    public static WasmValueType ReadValueType(nint valueType)
    {
        WasmtimeValType type;
        NativeMethods.wasmtime_valtype_new(valueType, &type);
        try
        {
            return type.Kind switch
            {
                WasmtimeValTypeKind.I32 => WasmValueType.I32,
                WasmtimeValTypeKind.I64 => WasmValueType.I64,
                WasmtimeValTypeKind.F32 => WasmValueType.F32,
                WasmtimeValTypeKind.F64 => WasmValueType.F64,
                WasmtimeValTypeKind.V128 => WasmValueType.V128,
                _ => new WasmValueType(WasmValueKind.Ref, (WasmHeapKind)type.HeapType.Kind, type.Nullable != 0),
            };
        }
        finally
        {
            NativeMethods.wasmtime_valtype_delete(&type);
        }
    }

    /// <summary>Creates a value type (<c>wasm_valtype_t</c>, owned) of an abstract heap type.</summary>
    public static nint CreateValueType(nint engine, WasmValueType type)
    {
        if (type.Heap is WasmHeapKind.ConcreteFunc or WasmHeapKind.ConcreteArray or WasmHeapKind.ConcreteStruct or WasmHeapKind.ConcreteExn)
            throw new NotSupportedException("A concrete heap type cannot be created");

        var nativeType = new WasmtimeValType
        {
            Kind = type.Kind switch
            {
                WasmValueKind.I32 => WasmtimeValTypeKind.I32,
                WasmValueKind.I64 => WasmtimeValTypeKind.I64,
                WasmValueKind.F32 => WasmtimeValTypeKind.F32,
                WasmValueKind.F64 => WasmtimeValTypeKind.F64,
                WasmValueKind.V128 => WasmtimeValTypeKind.V128,
                _ => WasmtimeValTypeKind.Ref,
            },
            Nullable = type.Nullable ? (byte)1 : (byte)0,
            HeapType = new WasmtimeHeapType { Kind = (byte)type.Heap },
        };

        return NativeMethods.wasmtime_valtype_to_wasm(engine, &nativeType);
    }

    private static WasmValueType[] ReadValueTypes(WasmPointerVec* types)
    {
        var result = new WasmValueType[(int)types->Size];
        for (var i = 0; i < result.Length; i++)
        {
            result[i] = ReadValueType(types->Data[i]);
        }

        return result;
    }

    /// <summary>Reads a function type (borrowed). The type keeps a copy of the native type, which keeps the concrete heap types.</summary>
    public static WasmFunctionType ReadFunctionType(nint functionType)
    {
        return new WasmFunctionType(ReadValueTypes(NativeMethods.wasm_functype_params(functionType)), ReadValueTypes(NativeMethods.wasm_functype_results(functionType)))
        {
            Native = new NativeTypeHandle(NativeMethods.wasm_functype_copy(functionType), WasmExternKind.Function),
        };
    }

    /// <summary>Creates a function type (owned): the native type of the function type, or a type of abstract heap types.</summary>
    public static nint CreateFunctionType(nint engine, WasmFunctionType type)
    {
        if (type.Native is NativeTypeHandle native)
            return NativeMethods.wasm_functype_copy(native.DangerousGetHandle());

        WasmPointerVec parameters;
        WasmPointerVec results;
        CreateValueTypes(engine, type.Parameters, &parameters);
        CreateValueTypes(engine, type.Results, &results);
        return NativeMethods.wasm_functype_new(&parameters, &results);
    }

    private static void CreateValueTypes(nint engine, WasmValueType[] types, WasmPointerVec* result)
    {
        if (types.Length == 0)
        {
            NativeMethods.wasm_valtype_vec_new_empty(result);
            return;
        }

        var data = stackalloc nint[types.Length];
        for (var i = 0; i < types.Length; i++)
        {
            data[i] = CreateValueType(engine, types[i]);
        }

        NativeMethods.wasm_valtype_vec_new(result, (nuint)types.Length, data);
    }

    /// <summary>Reads a global type (borrowed).</summary>
    public static WasmGlobalType ReadGlobalType(nint globalType)
    {
        return new WasmGlobalType(ReadValueType(NativeMethods.wasm_globaltype_content(globalType)), NativeMethods.wasm_globaltype_mutability(globalType) != 0)
        {
            Native = new NativeTypeHandle(NativeMethods.wasm_globaltype_copy(globalType), WasmExternKind.Global),
        };
    }

    /// <summary>Creates a global type (owned).</summary>
    public static nint CreateGlobalType(nint engine, WasmGlobalType type)
    {
        if (type.Native is NativeTypeHandle native)
            return NativeMethods.wasm_globaltype_copy(native.DangerousGetHandle());

        return NativeMethods.wasm_globaltype_new(CreateValueType(engine, type.ValueType), type.Mutable ? (byte)1 : (byte)0);
    }

    /// <summary>Reads a memory type (borrowed).</summary>
    public static WasmMemoryType ReadMemoryType(nint memoryType)
    {
        ulong maximum;
        var hasMaximum = NativeMethods.wasmtime_memorytype_maximum(memoryType, &maximum);
        return new WasmMemoryType(NativeMethods.wasmtime_memorytype_minimum(memoryType), hasMaximum ? maximum : null, NativeMethods.wasmtime_memorytype_is64(memoryType), NativeMethods.wasmtime_memorytype_isshared(memoryType));
    }

    /// <summary>
    /// Reads a table type (borrowed). <c>wasm_tabletype_limits</c> aborts the process when the limits of a 64-bit table
    /// do not fit in 32 bits: the limits come from the binary of the module.
    /// </summary>
    public static WasmTableType ReadTableType(nint tableType, WasmTableLimits? limits)
    {
        var elementType = ReadValueType(NativeMethods.wasm_tabletype_element(tableType));
        return new WasmTableType(elementType, limits?.Minimum ?? 0, limits?.Maximum, limits?.Is64 ?? false)
        {
            Native = new NativeTypeHandle(NativeMethods.wasm_tabletype_copy(tableType), WasmExternKind.Table),
        };
    }

    /// <summary>Reads a tag type (borrowed).</summary>
    public static WasmTagType ReadTagType(nint tagType)
    {
        var functionType = ReadFunctionType(NativeMethods.wasm_tagtype_functype(tagType));
        return new WasmTagType(functionType.Parameters) { Native = functionType.Native };
    }

    /// <summary>Creates a tag type (owned).</summary>
    public static nint CreateTagType(nint engine, WasmTagType type)
    {
        var functionType = type.Native is NativeTypeHandle native
            ? NativeMethods.wasm_functype_copy(native.DangerousGetHandle())
            : CreateFunctionType(engine, new WasmFunctionType(type.Parameters, []));
        return NativeMethods.wasm_tagtype_new(functionType);
    }

    /// <summary>Reads an external type (borrowed).</summary>
    public static WasmExternType ReadExternType(nint externType, WasmTableLimits? tableLimits)
    {
        return NativeMethods.wasm_externtype_kind(externType) switch
        {
            WasmExternTypeKind.Func => ReadFunctionType(NativeMethods.wasm_externtype_as_functype_const(externType)),
            WasmExternTypeKind.Global => ReadGlobalType(NativeMethods.wasm_externtype_as_globaltype_const(externType)),
            WasmExternTypeKind.Table => ReadTableType(NativeMethods.wasm_externtype_as_tabletype_const(externType), tableLimits),
            WasmExternTypeKind.Memory => ReadMemoryType(NativeMethods.wasm_externtype_as_memorytype_const(externType)),
            _ => ReadTagType(NativeMethods.wasm_externtype_as_tagtype_const(externType)),
        };
    }
}
