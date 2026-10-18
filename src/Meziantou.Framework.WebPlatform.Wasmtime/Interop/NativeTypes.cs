using System.Runtime.InteropServices;

namespace Meziantou.Framework.WebPlatform.Wasmtime.Interop;

// The structures of the C API of Wasmtime (crates/c-api/include), for 64-bit platforms. The private fields of the handles
// identify the objects of a store: they are compared field by field, as the padding bytes are not initialized.

/// <summary><c>wasm_byte_vec_t</c>, <c>wasm_name_t</c>, <c>wasm_message_t</c></summary>
[StructLayout(LayoutKind.Sequential)]
internal unsafe struct WasmByteVec
{
    public nuint Size;
    public byte* Data;

    public readonly ReadOnlySpan<byte> AsSpan() => Data is null ? default : new ReadOnlySpan<byte>(Data, checked((int)Size));
}

/// <summary>A vector of pointers (<c>wasm_valtype_vec_t</c>, <c>wasm_importtype_vec_t</c>...).</summary>
[StructLayout(LayoutKind.Sequential)]
internal unsafe struct WasmPointerVec
{
    public nuint Size;
    public nint* Data;
}

/// <summary><c>wasm_limits_t</c>: the maximum is <see cref="uint.MaxValue"/> when there is none.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct WasmLimits
{
    public uint Min;
    public uint Max;
}

/// <summary><c>wasmtime_func_t</c></summary>
[StructLayout(LayoutKind.Sequential)]
internal struct WasmtimeFunc
{
    public ulong StoreId;
    public nint Private;

    public readonly bool IsNull => StoreId == 0;
}

/// <summary><c>wasmtime_table_t</c>, <c>wasmtime_memory_t</c> and <c>wasmtime_tag_t</c>: <c>{ { u64, u32 }, u32 }</c>.</summary>
[StructLayout(LayoutKind.Explicit, Size = 24)]
internal struct WasmtimeIndexedExtern
{
    [FieldOffset(0)]
    public ulong StoreId;

    [FieldOffset(8)]
    public uint Private1;

    [FieldOffset(16)]
    public uint Private2;
}

/// <summary><c>wasmtime_global_t</c></summary>
[StructLayout(LayoutKind.Explicit, Size = 24)]
internal struct WasmtimeGlobalHandle
{
    [FieldOffset(0)]
    public ulong StoreId;

    [FieldOffset(8)]
    public uint Private1;

    [FieldOffset(12)]
    public uint Private2;

    [FieldOffset(16)]
    public uint Private3;
}

/// <summary><c>wasmtime_instance_t</c></summary>
[StructLayout(LayoutKind.Sequential)]
internal struct WasmtimeInstance
{
    public ulong StoreId;
    public nuint Private;
}

/// <summary><c>wasmtime_extern_kind_t</c></summary>
internal enum WasmtimeExternKind : byte
{
    Func = 0,
    Global = 1,
    Table = 2,
    Memory = 3,
    SharedMemory = 4,
    Tag = 5,
}

/// <summary><c>wasmtime_extern_t</c></summary>
[StructLayout(LayoutKind.Explicit, Size = 32)]
internal struct WasmtimeExtern
{
    [FieldOffset(0)]
    public WasmtimeExternKind Kind;

    [FieldOffset(8)]
    public WasmtimeFunc Func;

    [FieldOffset(8)]
    public WasmtimeGlobalHandle Global;

    [FieldOffset(8)]
    public WasmtimeIndexedExtern Table;

    [FieldOffset(8)]
    public WasmtimeIndexedExtern Memory;

    [FieldOffset(8)]
    public WasmtimeIndexedExtern Tag;
}

/// <summary><c>wasmtime_anyref_t</c>, <c>wasmtime_externref_t</c>, <c>wasmtime_exnref_t</c>: a rooted reference to a GC object (the null reference has a store id of 0).</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct WasmtimeRef
{
    public ulong StoreId;
    public uint Private1;
    public uint Private2;
    public nint Private3;

    public readonly bool IsNull => StoreId == 0;
}

/// <summary><c>wasmtime_valkind_t</c></summary>
internal enum WasmtimeValKind : byte
{
    I32 = 0,
    I64 = 1,
    F32 = 2,
    F64 = 3,
    V128 = 4,
    FuncRef = 5,
    ExternRef = 6,
    AnyRef = 7,
    ExnRef = 8,
}

/// <summary><c>wasmtime_val_t</c></summary>
[StructLayout(LayoutKind.Explicit, Size = 32)]
internal struct WasmtimeVal
{
    [FieldOffset(0)]
    public WasmtimeValKind Kind;

    [FieldOffset(8)]
    public int I32;

    [FieldOffset(8)]
    public long I64;

    [FieldOffset(8)]
    public int F32Bits;

    [FieldOffset(8)]
    public long F64Bits;

    [FieldOffset(8)]
    public long V128Low;

    [FieldOffset(16)]
    public long V128High;

    [FieldOffset(8)]
    public WasmtimeFunc FuncRef;

    [FieldOffset(8)]
    public WasmtimeRef Ref;
}

/// <summary><c>wasmtime_valtype_kind_t</c></summary>
internal enum WasmtimeValTypeKind : byte
{
    I32 = 0,
    I64 = 1,
    F32 = 2,
    F64 = 3,
    V128 = 4,
    Ref = 5,
}

/// <summary><c>wasmtime_heaptype_t</c></summary>
[StructLayout(LayoutKind.Explicit, Size = 16)]
internal struct WasmtimeHeapType
{
    [FieldOffset(0)]
    public byte Kind;

    [FieldOffset(8)]
    public nint Concrete;
}

/// <summary><c>wasmtime_valtype_t</c>: <c>{ kind, { nullable, heaptype } }</c>.</summary>
[StructLayout(LayoutKind.Explicit, Size = 32)]
internal struct WasmtimeValType
{
    [FieldOffset(0)]
    public WasmtimeValTypeKind Kind;

    [FieldOffset(8)]
    public byte Nullable;

    [FieldOffset(16)]
    public WasmtimeHeapType HeapType;
}

/// <summary><c>wasmtime_linear_memory_t</c></summary>
[StructLayout(LayoutKind.Sequential)]
internal unsafe struct WasmtimeLinearMemory
{
    public nint Env;
    public delegate* unmanaged<nint, nuint*, nuint*, byte*> GetMemory;
    public delegate* unmanaged<nint, nuint, nint> GrowMemory;
    public delegate* unmanaged<nint, void> Finalizer;
}

/// <summary><c>wasmtime_memory_creator_t</c></summary>
[StructLayout(LayoutKind.Sequential)]
internal unsafe struct WasmtimeMemoryCreator
{
    public nint Env;
    public delegate* unmanaged<nint, nint, nuint, nuint, nuint, nuint, WasmtimeLinearMemory*, nint> NewMemory;
    public delegate* unmanaged<nint, void> Finalizer;
}

/// <summary>The kinds of the external types of <c>wasm.h</c> (<c>wasm_externkind_t</c>).</summary>
internal enum WasmExternTypeKind : byte
{
    Func = 0,
    Global = 1,
    Table = 2,
    Memory = 3,
    Tag = 4,
}
