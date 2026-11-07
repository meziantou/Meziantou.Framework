namespace Meziantou.Framework.WebPlatform.WebAssembly;

/// <summary>The kind of a value type (https://webassembly.github.io/spec/core/syntax/types.html#value-types).</summary>
internal enum WasmValueKind : byte
{
    I32,
    I64,
    F32,
    F64,
    V128,
    Ref,
}

/// <summary>The kind of a heap type (https://webassembly.github.io/spec/core/syntax/types.html#heap-types).</summary>
internal enum WasmHeapKind : byte
{
    Extern,
    NoExtern,
    Func,
    ConcreteFunc,
    NoFunc,
    Any,
    None,
    Eq,
    I31,
    Array,
    ConcreteArray,
    Struct,
    ConcreteStruct,
    Exn,
    ConcreteExn,
    NoExn,
}

/// <summary>A value type: a number type, the vector type, or a reference type with its heap type.</summary>
internal readonly record struct WasmValueType(WasmValueKind Kind, WasmHeapKind Heap = default, bool Nullable = true)
{
    public static WasmValueType I32 => new(WasmValueKind.I32);

    public static WasmValueType I64 => new(WasmValueKind.I64);

    public static WasmValueType F32 => new(WasmValueKind.F32);

    public static WasmValueType F64 => new(WasmValueKind.F64);

    public static WasmValueType V128 => new(WasmValueKind.V128);

    /// <summary><c>funcref</c>: <c>(ref null func)</c></summary>
    public static WasmValueType FuncRef => new(WasmValueKind.Ref, WasmHeapKind.Func);

    /// <summary><c>externref</c>: <c>(ref null extern)</c></summary>
    public static WasmValueType ExternRef => new(WasmValueKind.Ref, WasmHeapKind.Extern);

    /// <summary><c>anyref</c>: <c>(ref null any)</c></summary>
    public static WasmValueType AnyRef => new(WasmValueKind.Ref, WasmHeapKind.Any);

    public bool IsReference => Kind == WasmValueKind.Ref;

    /// <summary>Whether the type matches <c>v128</c> or <c>exnref</c>: the values that cannot be converted to JavaScript.</summary>
    public bool IsV128OrExnRef => Kind == WasmValueKind.V128 || (Kind == WasmValueKind.Ref && Heap is WasmHeapKind.Exn or WasmHeapKind.ConcreteExn or WasmHeapKind.NoExn);

    /// <summary>Whether the type matches <c>(ref null extern)</c>.</summary>
    public bool IsExternReference => Kind == WasmValueKind.Ref && Heap is WasmHeapKind.Extern or WasmHeapKind.NoExtern;

    /// <summary>Whether the type matches <c>(ref null func)</c>.</summary>
    public bool IsFunctionReference => Kind == WasmValueKind.Ref && Heap is WasmHeapKind.Func or WasmHeapKind.ConcreteFunc or WasmHeapKind.NoFunc;

    /// <summary>Whether the type matches <c>(ref null any)</c>.</summary>
    public bool IsAnyReference => Kind == WasmValueKind.Ref && Heap is WasmHeapKind.Any or WasmHeapKind.None or WasmHeapKind.Eq or WasmHeapKind.I31 or WasmHeapKind.Array or WasmHeapKind.ConcreteArray or WasmHeapKind.Struct or WasmHeapKind.ConcreteStruct;

    /// <summary>The string of the type in the type reflection of the JavaScript API (e.g. <c>i32</c>, <c>anyfunc</c>).</summary>
    public override string ToString() => Kind switch
    {
        WasmValueKind.I32 => "i32",
        WasmValueKind.I64 => "i64",
        WasmValueKind.F32 => "f32",
        WasmValueKind.F64 => "f64",
        WasmValueKind.V128 => "v128",
        _ => (Heap, Nullable) switch
        {
            (WasmHeapKind.Func, true) => "anyfunc",
            (WasmHeapKind.Extern, true) => "externref",
            (WasmHeapKind.Any, true) => "anyref",
            (WasmHeapKind.Eq, true) => "eqref",
            (WasmHeapKind.I31, true) => "i31ref",
            (WasmHeapKind.Struct, true) => "structref",
            (WasmHeapKind.Array, true) => "arrayref",
            (WasmHeapKind.Exn, true) => "exnref",
            _ => "(ref " + (Nullable ? "null " : "") + Heap.ToString().ToLowerInvariant() + ")",
        },
    };
}

/// <summary>The kind of an external type (https://webassembly.github.io/spec/core/syntax/types.html#external-types).</summary>
internal enum WasmExternKind : byte
{
    Function,
    Table,
    Memory,
    Global,
    Tag,
}

/// <summary>An external type: the type of an import or of an export.</summary>
internal abstract class WasmExternType
{
    public abstract WasmExternKind Kind { get; }

    /// <summary>The string value of the extern type (https://webassembly.github.io/spec/js-api/#string-value-of-the-extern-type).</summary>
    public string KindName => Kind switch
    {
        WasmExternKind.Function => "function",
        WasmExternKind.Table => "table",
        WasmExternKind.Memory => "memory",
        WasmExternKind.Global => "global",
        _ => "tag",
    };
}

/// <summary>A function type. A type read from a module may reference the types of the module (<see cref="Native"/>).</summary>
internal sealed class WasmFunctionType(WasmValueType[] parameters, WasmValueType[] results) : WasmExternType
{
    public override WasmExternKind Kind => WasmExternKind.Function;

    public WasmValueType[] Parameters { get; } = parameters;

    public WasmValueType[] Results { get; } = results;

    /// <summary>The type of the runtime, which keeps the concrete heap types of the parameters and of the results, or null.</summary>
    public object? Native { get; init; }

    /// <summary>Whether a parameter or a result matches <c>v128</c> or <c>exnref</c> (the function cannot be called from JavaScript).</summary>
    public bool HasV128OrExnRef => Parameters.Any(static type => type.IsV128OrExnRef) || Results.Any(static type => type.IsV128OrExnRef);
}

/// <summary>A tag type: the types of the parameters of the exceptions.</summary>
internal sealed class WasmTagType(WasmValueType[] parameters) : WasmExternType
{
    public override WasmExternKind Kind => WasmExternKind.Tag;

    public WasmValueType[] Parameters { get; } = parameters;

    /// <summary>The type of the runtime, or null.</summary>
    public object? Native { get; init; }
}

/// <summary>A memory type (https://webassembly.github.io/spec/core/syntax/types.html#memory-types): the limits are in pages.</summary>
internal sealed class WasmMemoryType(ulong minimum, ulong? maximum, bool is64, bool shared) : WasmExternType
{
    public override WasmExternKind Kind => WasmExternKind.Memory;

    public ulong Minimum { get; } = minimum;

    public ulong? Maximum { get; } = maximum;

    public bool Is64 { get; } = is64;

    public bool Shared { get; } = shared;
}

/// <summary>A table type (https://webassembly.github.io/spec/core/syntax/types.html#table-types).</summary>
internal sealed class WasmTableType(WasmValueType elementType, ulong minimum, ulong? maximum, bool is64) : WasmExternType
{
    public override WasmExternKind Kind => WasmExternKind.Table;

    public WasmValueType ElementType { get; } = elementType;

    public ulong Minimum { get; } = minimum;

    public ulong? Maximum { get; } = maximum;

    public bool Is64 { get; } = is64;

    /// <summary>The type of the runtime, which keeps the concrete heap type of the elements, or null.</summary>
    public object? Native { get; init; }
}

/// <summary>A global type (https://webassembly.github.io/spec/core/syntax/types.html#global-types).</summary>
internal sealed class WasmGlobalType(WasmValueType valueType, bool mutable) : WasmExternType
{
    public override WasmExternKind Kind => WasmExternKind.Global;

    public WasmValueType ValueType { get; } = valueType;

    public bool Mutable { get; } = mutable;

    /// <summary>The type of the runtime, which keeps the concrete heap type of the value, or null.</summary>
    public object? Native { get; init; }
}

/// <summary>An import of a module.</summary>
internal sealed record WasmImport(string Module, string Name, WasmExternType Type);

/// <summary>An export of a module.</summary>
internal sealed record WasmExport(string Name, WasmExternType Type);
