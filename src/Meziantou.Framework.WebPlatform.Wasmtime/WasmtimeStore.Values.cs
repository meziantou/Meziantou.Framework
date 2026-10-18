using System.Runtime.InteropServices;
using Meziantou.Framework.WebPlatform.Wasmtime.Interop;
using Meziantou.Framework.WebPlatform.WebAssembly;

namespace Meziantou.Framework.WebPlatform.Wasmtime;

// The conversions of the values between the runtime abstraction and the C API. The references to GC objects of the C API
// are rooted: a converted value is owned by the caller, which unroots it when it is not given to Wasmtime.
internal sealed unsafe partial class WasmtimeStore
{
    /// <summary>Converts a value to a native value of a type (owned).</summary>
    private WasmtimeVal ToNative(WasmValue value, WasmValueType type)
    {
        var result = default(WasmtimeVal);
        switch (type.Kind)
        {
            case WasmValueKind.I32:
                result.Kind = WasmtimeValKind.I32;
                result.I32 = value.I32;
                return result;

            case WasmValueKind.I64:
                result.Kind = WasmtimeValKind.I64;
                result.I64 = value.I64;
                return result;

            case WasmValueKind.F32:
                result.Kind = WasmtimeValKind.F32;
                result.F32Bits = value.F32Bits;
                return result;

            case WasmValueKind.F64:
                result.Kind = WasmtimeValKind.F64;
                result.F64Bits = value.F64Bits;
                return result;

            case WasmValueKind.V128:
                result.Kind = WasmtimeValKind.V128;
                result.V128Low = value.V128Low;
                result.V128High = value.V128High;
                return result;
        }

        var reference = value.Reference;
        if (type.IsFunctionReference)
        {
            result.Kind = WasmtimeValKind.FuncRef;
            result.FuncRef = reference switch
            {
                null => default,
                WasmtimeFunction function when function.Store == this => function.Handle,
                _ => throw new WasmTypeMismatchException("The value is not a function"),
            };
            return result;
        }

        if (type.IsExternReference)
        {
            result.Kind = WasmtimeValKind.ExternRef;
            result.Ref = reference switch
            {
                null => default,
                WasmHostReference host => CreateExternRef(host),

                // ref.extern of a reference of the any hierarchy
                _ => ConvertAnyToExtern(reference),
            };
            return result;
        }

        if (type.IsAnyReference)
        {
            result.Kind = WasmtimeValKind.AnyRef;
            result.Ref = ToNativeAnyRef(reference);
            return result;
        }

        // exnref
        result.Kind = WasmtimeValKind.ExnRef;
        if (reference is WasmtimeExceptionReference exception && exception.Store == this)
        {
            var handle = exception.Handle;
            NativeMethods.wasmtime_exnref_clone(&handle, &result.Ref);
        }
        else if (reference is not null)
        {
            throw new WasmTypeMismatchException("The value is not an exception");
        }

        return result;
    }

    /// <summary>Converts a reference of the any hierarchy (or a host value: <c>ref.host</c>) to a native <c>anyref</c> (owned).</summary>
    private WasmtimeRef ToNativeAnyRef(object? reference)
    {
        WasmtimeRef result;
        switch (reference)
        {
            case null:
                return default;

            case WasmI31Reference i31:
                NativeMethods.wasmtime_anyref_from_i31(Context, (uint)i31.Value & 0x7FFF_FFFF, &result);
                return result;

            case WasmtimeGcObject gcObject when gcObject.Store == this:
                var handle = gcObject.Handle;
                NativeMethods.wasmtime_anyref_clone(&handle, &result);
                return result;

            case WasmHostReference host:
                // ref.host: the host value is internalized (any.convert_extern of an externref)
                var externRef = new WasmtimeVal { Kind = WasmtimeValKind.ExternRef, Ref = CreateExternRef(host) };
                try
                {
                    return CallConversion(GetAnyConvertExtern(), externRef, WasmtimeValKind.AnyRef);
                }
                finally
                {
                    Unroot(ref externRef);
                }

            default:
                throw new WasmTypeMismatchException("The value is not a reference of the any hierarchy");
        }
    }

    private WasmtimeRef ConvertAnyToExtern(object reference)
    {
        var anyRef = new WasmtimeVal { Kind = WasmtimeValKind.AnyRef, Ref = ToNativeAnyRef(reference) };
        try
        {
            return CallConversion(GetExternConvertAny(), anyRef, WasmtimeValKind.ExternRef);
        }
        finally
        {
            Unroot(ref anyRef);
        }
    }

    /// <summary>Converts a native value (borrowed) to a value: the references that are kept are cloned.</summary>
    private WasmValue FromNative(ref WasmtimeVal value)
    {
        switch (value.Kind)
        {
            case WasmtimeValKind.I32:
                return WasmValue.FromI32(value.I32);
            case WasmtimeValKind.I64:
                return WasmValue.FromI64(value.I64);
            case WasmtimeValKind.F32:
                return WasmValue.FromF32Bits(value.F32Bits);
            case WasmtimeValKind.F64:
                return WasmValue.FromF64Bits(value.F64Bits);
            case WasmtimeValKind.V128:
                return WasmValue.FromV128(value.V128Low, value.V128High);
            case WasmtimeValKind.FuncRef:
                return WasmValue.FromReference(value.FuncRef.IsNull ? null : GetFunction(value.FuncRef));
            case WasmtimeValKind.ExnRef:
                if (value.Ref.IsNull)
                    return WasmValue.Null;

                fixed (WasmtimeRef* reference = &value.Ref)
                {
                    return WasmValue.FromReference(GetException(reference, owned: false));
                }

            case WasmtimeValKind.ExternRef:
                if (value.Ref.IsNull)
                    return WasmValue.Null;

                fixed (WasmtimeRef* reference = &value.Ref)
                {
                    var data = NativeMethods.wasmtime_externref_data(Context, reference);
                    if (data != 0 && GCHandle.FromIntPtr(data).Target is HostValueSlot slot)
                        return WasmValue.FromReference(slot.Value);
                }

                // ref.extern of a reference of the any hierarchy (extern.convert_any): ToJSValue(ref.extern ref) is ToJSValue(ref)
                var anyRef = new WasmtimeVal { Kind = WasmtimeValKind.AnyRef, Ref = CallConversion(GetAnyConvertExtern(), value, WasmtimeValKind.AnyRef) };
                try
                {
                    return FromNative(ref anyRef);
                }
                finally
                {
                    Unroot(ref anyRef);
                }

            default: // AnyRef
                if (value.Ref.IsNull)
                    return WasmValue.Null;

                fixed (WasmtimeRef* reference = &value.Ref)
                {
                    if (NativeMethods.wasmtime_anyref_is_i31(Context, reference))
                    {
                        int i31;
                        NativeMethods.wasmtime_anyref_i31_get_s(Context, reference, &i31);
                        return WasmValue.FromReference(new WasmI31Reference(i31));
                    }

                    if (NativeMethods.wasmtime_anyref_is_struct(Context, reference))
                        return WasmValue.FromReference(GetGcObject(reference, isArray: false));

                    if (NativeMethods.wasmtime_anyref_is_array(Context, reference))
                        return WasmValue.FromReference(GetGcObject(reference, isArray: true));
                }

                // ref.host: an internalized host value (any.convert_extern)
                var externRef = new WasmtimeVal { Kind = WasmtimeValKind.ExternRef, Ref = CallConversion(GetExternConvertAny(), value, WasmtimeValKind.ExternRef) };
                try
                {
                    return FromNative(ref externRef);
                }
                finally
                {
                    Unroot(ref externRef);
                }
        }
    }

    /// <summary>Unroots the reference of an owned native value.</summary>
    private static void Unroot(ref WasmtimeVal value)
    {
        if (value.Kind is WasmtimeValKind.AnyRef or WasmtimeValKind.ExternRef or WasmtimeValKind.ExnRef && !value.Ref.IsNull)
        {
            fixed (WasmtimeVal* pointer = &value)
            {
                NativeMethods.wasmtime_val_unroot(pointer);
            }

            value.Ref = default;
        }
    }

    // ----------------------------------------------------------------------------------------------------------------
    // Host values
    // ----------------------------------------------------------------------------------------------------------------

    /// <summary>Creates an <c>externref</c> (owned) whose data is a weak handle of a slot of the store.</summary>
    private WasmtimeRef CreateExternRef(WasmHostReference value)
    {
        var slot = new HostValueSlot(this, value);
        _hostValues.Add(slot);
        var handle = GCHandle.Alloc(slot, GCHandleType.Weak);
        WasmtimeRef result;
        if (!NativeMethods.wasmtime_externref_new(Context, GCHandle.ToIntPtr(handle), &FinalizeHostValue, &result))
        {
            handle.Free();
            _hostValues.Remove(slot);
            throw new WasmAllocationException("The externref cannot be allocated");
        }

        return result;
    }

    /// <summary>Called when an <c>externref</c> is collected (or when the store is deleted).</summary>
    [UnmanagedCallersOnly]
    private static void FinalizeHostValue(nint data)
    {
        var handle = GCHandle.FromIntPtr(data);
        if (handle.Target is HostValueSlot slot)
        {
            slot.Store._hostValues.Remove(slot);
        }

        handle.Free();
    }

    /// <summary>A host value referenced by an <c>externref</c>.</summary>
    private sealed class HostValueSlot(WasmtimeStore store, WasmHostReference value)
    {
        public WasmtimeStore Store { get; } = store;

        public WasmHostReference Value { get; } = value;
    }

    // ----------------------------------------------------------------------------------------------------------------
    // Conversions between the extern and any hierarchies
    // ----------------------------------------------------------------------------------------------------------------

    // The C API cannot convert the references between the hierarchies: a module does it (any.convert_extern and extern.convert_any)
    private WasmtimeFunction GetAnyConvertExtern()
    {
        if (_anyConvertExtern is null)
        {
            InstantiateConversions();
        }

        return _anyConvertExtern!;
    }

    private WasmtimeFunction GetExternConvertAny()
    {
        if (_externConvertAny is null)
        {
            InstantiateConversions();
        }

        return _externConvertAny!;
    }

    private void InstantiateConversions()
    {
        var instance = Instantiate(_runtime.ConversionsModule, []);
        _anyConvertExtern = (WasmtimeFunction)instance.Exports[0].Value;
        _externConvertAny = (WasmtimeFunction)instance.Exports[1].Value;
    }

    /// <summary>Calls a conversion function with an argument (borrowed) and returns its result (owned).</summary>
    private WasmtimeRef CallConversion(WasmtimeFunction function, WasmtimeVal argument, WasmtimeValKind resultKind)
    {
        var handle = function.Handle;
        WasmtimeVal result;
        nint trap;
        var error = NativeMethods.wasmtime_func_call(Context, &handle, &argument, 1, &result, 1, &trap);
        if (error != 0 || trap != 0)
        {
            ThrowIfFailed(error, trap, static message => new WasmTypeMismatchException(message));
        }

        System.Diagnostics.Debug.Assert(result.Kind == resultKind, "The conversion returns a reference of the expected kind");
        return result.Ref;
    }
}
