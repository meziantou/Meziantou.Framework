namespace Meziantou.Framework.WasmEngine;

/// <summary>The dynamic types of the references (ref.test, ref.cast, the values given by the host).</summary>
internal static class RuntimeTypes
{
    public static bool Matches(object? value, ValType type) => value is null ? type.Nullable : HeapMatches(value, type.Heap, type.Defined);

    public static bool Matches(object? value, WasmHeapKind heap, DefinedType? defined, bool nullable) => value is null ? nullable : HeapMatches(value, heap, defined);

    public static bool HeapMatches(object value, WasmHeapKind heap, DefinedType? defined)
    {
        return value switch
        {
            ManagedFunction function => heap == WasmHeapKind.Func || (defined is { IsFunc: true } && function.DefinedType.IsSubtypeOf(defined)),
            GcStruct structure => heap is WasmHeapKind.Any or WasmHeapKind.Eq or WasmHeapKind.Struct or WasmHeapKind.Extern || (defined is { IsStruct: true } && structure.DefinedType.IsSubtypeOf(defined)),
            GcArray array => heap is WasmHeapKind.Any or WasmHeapKind.Eq or WasmHeapKind.Array or WasmHeapKind.Extern || (defined is { IsArray: true } && array.DefinedType.IsSubtypeOf(defined)),
            WasmI31Reference => heap is WasmHeapKind.Any or WasmHeapKind.Eq or WasmHeapKind.I31 or WasmHeapKind.Extern,
            WasmHostReference => heap is WasmHeapKind.Extern or WasmHeapKind.Any,
            ManagedException => heap == WasmHeapKind.Exn,
            _ => false,
        };
    }

    /// <summary>ref.eq: the i31 references are compared by value, the other references by identity.</summary>
    public static bool RefEquals(object? a, object? b)
    {
        if (a is WasmI31Reference x && b is WasmI31Reference y)
            return x.Value == y.Value;

        return object.ReferenceEquals(a, b);
    }
}
