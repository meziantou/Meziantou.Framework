namespace Meziantou.Framework.WasmEngine;

/// <summary>The kind of a value type (https://webassembly.github.io/spec/core/syntax/types.html#value-types).</summary>
public enum WasmValueKind : byte
{
    /// <summary>A 32-bit integer.</summary>
    I32,

    /// <summary>A 64-bit integer.</summary>
    I64,

    /// <summary>A 32-bit floating-point number.</summary>
    F32,

    /// <summary>A 64-bit floating-point number.</summary>
    F64,

    /// <summary>A 128-bit vector.</summary>
    V128,

    /// <summary>A reference.</summary>
    Ref,
}

/// <summary>The kind of a heap type (https://webassembly.github.io/spec/core/syntax/types.html#heap-types).</summary>
public enum WasmHeapKind : byte
{
    /// <summary><c>extern</c>: the references to host values.</summary>
    Extern,

    /// <summary><c>noextern</c>: the bottom type of the extern hierarchy.</summary>
    NoExtern,

    /// <summary><c>func</c>: the references to functions.</summary>
    Func,

    /// <summary>A function type defined by a module.</summary>
    ConcreteFunc,

    /// <summary><c>nofunc</c>: the bottom type of the func hierarchy.</summary>
    NoFunc,

    /// <summary><c>any</c>: the top type of the any hierarchy.</summary>
    Any,

    /// <summary><c>none</c>: the bottom type of the any hierarchy.</summary>
    None,

    /// <summary><c>eq</c>: the references that can be compared.</summary>
    Eq,

    /// <summary><c>i31</c>: the unboxed scalars.</summary>
    I31,

    /// <summary><c>array</c>: the arrays.</summary>
    Array,

    /// <summary>An array type defined by a module.</summary>
    ConcreteArray,

    /// <summary><c>struct</c>: the structs.</summary>
    Struct,

    /// <summary>A struct type defined by a module.</summary>
    ConcreteStruct,

    /// <summary><c>exn</c>: the exceptions.</summary>
    Exn,

    /// <summary>An exception type defined by a module.</summary>
    ConcreteExn,

    /// <summary><c>noexn</c>: the bottom type of the exn hierarchy.</summary>
    NoExn,
}

/// <summary>A value type: a number type, the vector type, or a reference type with its heap type.</summary>
/// <param name="Kind">The kind of the type.</param>
/// <param name="Heap">The heap type of a reference type.</param>
/// <param name="Nullable">Whether a reference type contains the null reference.</param>
public readonly record struct WasmValueType(WasmValueKind Kind, WasmHeapKind Heap = default, bool Nullable = true)
{
    /// <summary>Gets the <c>i32</c> type.</summary>
    public static WasmValueType I32 => new(WasmValueKind.I32);

    /// <summary>Gets the <c>i64</c> type.</summary>
    public static WasmValueType I64 => new(WasmValueKind.I64);

    /// <summary>Gets the <c>f32</c> type.</summary>
    public static WasmValueType F32 => new(WasmValueKind.F32);

    /// <summary>Gets the <c>f64</c> type.</summary>
    public static WasmValueType F64 => new(WasmValueKind.F64);

    /// <summary>Gets the <c>v128</c> type.</summary>
    public static WasmValueType V128 => new(WasmValueKind.V128);

    /// <summary>Gets the <c>funcref</c> type: <c>(ref null func)</c>.</summary>
    public static WasmValueType FuncRef => new(WasmValueKind.Ref, WasmHeapKind.Func);

    /// <summary>Gets the <c>externref</c> type: <c>(ref null extern)</c>.</summary>
    public static WasmValueType ExternRef => new(WasmValueKind.Ref, WasmHeapKind.Extern);

    /// <summary>Gets the <c>anyref</c> type: <c>(ref null any)</c>.</summary>
    public static WasmValueType AnyRef => new(WasmValueKind.Ref, WasmHeapKind.Any);

    /// <summary>Gets the <c>exnref</c> type: <c>(ref null exn)</c>.</summary>
    public static WasmValueType ExnRef => new(WasmValueKind.Ref, WasmHeapKind.Exn);

    /// <summary>Gets a value indicating whether the type is a reference type.</summary>
    public bool IsReference => Kind == WasmValueKind.Ref;

    /// <summary>Gets a value indicating whether the type matches <c>(ref null extern)</c>.</summary>
    public bool IsExternReference => Kind == WasmValueKind.Ref && Heap is WasmHeapKind.Extern or WasmHeapKind.NoExtern;

    /// <summary>Gets a value indicating whether the type matches <c>(ref null func)</c>.</summary>
    public bool IsFunctionReference => Kind == WasmValueKind.Ref && Heap is WasmHeapKind.Func or WasmHeapKind.ConcreteFunc or WasmHeapKind.NoFunc;

    /// <summary>Gets a value indicating whether the type matches <c>(ref null any)</c>.</summary>
    public bool IsAnyReference => Kind == WasmValueKind.Ref && Heap is WasmHeapKind.Any or WasmHeapKind.None or WasmHeapKind.Eq or WasmHeapKind.I31 or WasmHeapKind.Array or WasmHeapKind.ConcreteArray or WasmHeapKind.Struct or WasmHeapKind.ConcreteStruct;

    /// <summary>Gets a value indicating whether the type matches <c>(ref null exn)</c>.</summary>
    public bool IsExceptionReference => Kind == WasmValueKind.Ref && Heap is WasmHeapKind.Exn or WasmHeapKind.ConcreteExn or WasmHeapKind.NoExn;

    /// <summary>Returns the type in the text format (e.g. <c>i32</c>, <c>funcref</c>, <c>(ref null any)</c>).</summary>
    public override string ToString() => Kind switch
    {
        WasmValueKind.I32 => "i32",
        WasmValueKind.I64 => "i64",
        WasmValueKind.F32 => "f32",
        WasmValueKind.F64 => "f64",
        WasmValueKind.V128 => "v128",
        _ => (Heap, Nullable) switch
        {
            (WasmHeapKind.Func, true) => "funcref",
            (WasmHeapKind.Extern, true) => "externref",
            (WasmHeapKind.Any, true) => "anyref",
            (WasmHeapKind.Eq, true) => "eqref",
            (WasmHeapKind.I31, true) => "i31ref",
            (WasmHeapKind.Struct, true) => "structref",
            (WasmHeapKind.Array, true) => "arrayref",
            (WasmHeapKind.Exn, true) => "exnref",
            _ => "(ref " + (Nullable ? "null " : "") + GetHeapTypeName(Heap) + ")",
        },
    };

    private static string GetHeapTypeName(WasmHeapKind heap) => heap switch
    {
        WasmHeapKind.Extern => "extern",
        WasmHeapKind.NoExtern => "noextern",
        WasmHeapKind.Func => "func",
        WasmHeapKind.NoFunc => "nofunc",
        WasmHeapKind.Any => "any",
        WasmHeapKind.None => "none",
        WasmHeapKind.Eq => "eq",
        WasmHeapKind.I31 => "i31",
        WasmHeapKind.Array => "array",
        WasmHeapKind.Struct => "struct",
        WasmHeapKind.Exn => "exn",
        WasmHeapKind.NoExn => "noexn",
        _ => "$concrete",
    };
}

/// <summary>The kind of an external type (https://webassembly.github.io/spec/core/syntax/types.html#external-types).</summary>
public enum WasmExternKind : byte
{
    /// <summary>A function.</summary>
    Function,

    /// <summary>A table.</summary>
    Table,

    /// <summary>A memory.</summary>
    Memory,

    /// <summary>A global.</summary>
    Global,

    /// <summary>A tag.</summary>
    Tag,
}

/// <summary>An external type: the type of an import or of an export.</summary>
public abstract class WasmExternType
{
    private protected WasmExternType()
    {
    }

    /// <summary>Gets the kind of the type.</summary>
    public abstract WasmExternKind Kind { get; }
}

/// <summary>A function type. A type read from a module keeps the concrete heap types of its parameters and results.</summary>
public sealed class WasmFunctionType : WasmExternType
{
    /// <summary>Creates a function type of abstract heap types.</summary>
    /// <param name="parameters">The types of the parameters.</param>
    /// <param name="results">The types of the results.</param>
    public WasmFunctionType(WasmValueType[] parameters, WasmValueType[] results)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        ArgumentNullException.ThrowIfNull(results);
        Parameters = parameters;
        Results = results;
    }

    /// <inheritdoc/>
    public override WasmExternKind Kind => WasmExternKind.Function;

    /// <summary>Gets the types of the parameters.</summary>
#pragma warning disable CA1819 // Properties should not return arrays
    public WasmValueType[] Parameters { get; }

    /// <summary>Gets the types of the results.</summary>
    public WasmValueType[] Results { get; }
#pragma warning restore CA1819

    /// <summary>The native type, which keeps the concrete heap types of the parameters and of the results, or null.</summary>
    internal object? Native { get; init; }
}

/// <summary>A tag type: the types of the parameters of the exceptions.</summary>
public sealed class WasmTagType : WasmExternType
{
    /// <summary>Creates a tag type of abstract heap types.</summary>
    /// <param name="parameters">The types of the values of the exceptions.</param>
    public WasmTagType(WasmValueType[] parameters)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        Parameters = parameters;
    }

    /// <inheritdoc/>
    public override WasmExternKind Kind => WasmExternKind.Tag;

    /// <summary>Gets the types of the values of the exceptions.</summary>
#pragma warning disable CA1819 // Properties should not return arrays
    public WasmValueType[] Parameters { get; }
#pragma warning restore CA1819

    /// <summary>The native type, or null.</summary>
    internal object? Native { get; init; }
}

/// <summary>A memory type (https://webassembly.github.io/spec/core/syntax/types.html#memory-types): the limits are in pages of 64 KiB.</summary>
/// <param name="minimum">The initial size in pages.</param>
/// <param name="maximum">The maximum size in pages, or null.</param>
/// <param name="is64">Whether the addresses are 64-bit (memory64).</param>
/// <param name="shared">Whether the memory is shared by the threads.</param>
public sealed class WasmMemoryType(ulong minimum, ulong? maximum, bool is64 = false, bool shared = false) : WasmExternType
{
    /// <summary>The size of a page in bytes.</summary>
    public const ulong PageSize = 65536;

    /// <inheritdoc/>
    public override WasmExternKind Kind => WasmExternKind.Memory;

    /// <summary>Gets the initial size in pages.</summary>
    public ulong Minimum { get; } = minimum;

    /// <summary>Gets the maximum size in pages, or null.</summary>
    public ulong? Maximum { get; } = maximum;

    /// <summary>Gets a value indicating whether the addresses are 64-bit (memory64).</summary>
    public bool Is64 { get; } = is64;

    /// <summary>Gets a value indicating whether the memory is shared by the threads.</summary>
    public bool Shared { get; } = shared;
}

/// <summary>A table type (https://webassembly.github.io/spec/core/syntax/types.html#table-types).</summary>
/// <param name="elementType">The type of the elements (a reference type).</param>
/// <param name="minimum">The initial number of elements.</param>
/// <param name="maximum">The maximum number of elements, or null.</param>
/// <param name="is64">Whether the indices are 64-bit.</param>
public sealed class WasmTableType(WasmValueType elementType, ulong minimum, ulong? maximum, bool is64 = false) : WasmExternType
{
    /// <inheritdoc/>
    public override WasmExternKind Kind => WasmExternKind.Table;

    /// <summary>Gets the type of the elements.</summary>
    public WasmValueType ElementType { get; } = elementType;

    /// <summary>Gets the initial number of elements.</summary>
    public ulong Minimum { get; } = minimum;

    /// <summary>Gets the maximum number of elements, or null.</summary>
    public ulong? Maximum { get; } = maximum;

    /// <summary>Gets a value indicating whether the indices are 64-bit.</summary>
    public bool Is64 { get; } = is64;

    /// <summary>The native type, which keeps the concrete heap type of the elements, or null.</summary>
    internal object? Native { get; init; }
}

/// <summary>A global type (https://webassembly.github.io/spec/core/syntax/types.html#global-types).</summary>
/// <param name="valueType">The type of the value.</param>
/// <param name="mutable">Whether the value can be changed.</param>
public sealed class WasmGlobalType(WasmValueType valueType, bool mutable) : WasmExternType
{
    /// <inheritdoc/>
    public override WasmExternKind Kind => WasmExternKind.Global;

    /// <summary>Gets the type of the value.</summary>
    public WasmValueType ValueType { get; } = valueType;

    /// <summary>Gets a value indicating whether the value can be changed.</summary>
    public bool Mutable { get; } = mutable;

    /// <summary>The native type, which keeps the concrete heap type of the value, or null.</summary>
    internal object? Native { get; init; }
}

/// <summary>An import of a module.</summary>
/// <param name="Module">The name of the module of the import.</param>
/// <param name="Name">The name of the import.</param>
/// <param name="Type">The type of the import.</param>
public sealed record WasmImport(string Module, string Name, WasmExternType Type);

/// <summary>An export of a module.</summary>
/// <param name="Name">The name of the export.</param>
/// <param name="Type">The type of the export.</param>
public sealed record WasmExport(string Name, WasmExternType Type);
