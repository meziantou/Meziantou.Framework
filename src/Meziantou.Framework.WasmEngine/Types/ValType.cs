using System.Runtime.InteropServices;

namespace Meziantou.Framework.WasmEngine;

/// <summary>The code of a value type or of a storage type (the packed types are only storage types).</summary>
internal enum ValueCode : byte
{
    I32,
    I64,
    F32,
    F64,
    V128,
    Ref,
    I8,
    I16,

    /// <summary>The bottom type of the validator (the operands of unreachable code).</summary>
    Bottom,
}

/// <summary>
/// A value type, or a storage type of a field. A reference type has an abstract heap type, or a concrete heap type
/// (<see cref="Defined"/>, a canonical type of the runtime: two types are equal when their objects are the same).
/// </summary>
[StructLayout(LayoutKind.Auto)]
internal readonly struct ValType : IEquatable<ValType>
{
    public ValType(ValueCode code)
    {
        Code = code;
        Nullable = false;
        Heap = default;
        Defined = null;
    }

    public ValType(bool nullable, WasmHeapKind heap)
    {
        Code = ValueCode.Ref;
        Nullable = nullable;
        Heap = heap;
        Defined = null;
    }

    public ValType(bool nullable, DefinedType defined)
    {
        Code = ValueCode.Ref;
        Nullable = nullable;
        Heap = defined.Kind switch
        {
            CompositeKind.Func => WasmHeapKind.ConcreteFunc,
            CompositeKind.Struct => WasmHeapKind.ConcreteStruct,
            _ => WasmHeapKind.ConcreteArray,
        };
        Defined = defined;
    }

    public ValueCode Code { get; }

    public bool Nullable { get; }

    public WasmHeapKind Heap { get; }

    public DefinedType? Defined { get; }

    public static ValType I32 => new(ValueCode.I32);

    public static ValType I64 => new(ValueCode.I64);

    public static ValType F32 => new(ValueCode.F32);

    public static ValType F64 => new(ValueCode.F64);

    public static ValType V128 => new(ValueCode.V128);

    public static ValType Bottom => new(ValueCode.Bottom);

    public static ValType FuncRef => new(nullable: true, WasmHeapKind.Func);

    public static ValType ExternRef => new(nullable: true, WasmHeapKind.Extern);

    public static ValType AnyRef => new(nullable: true, WasmHeapKind.Any);

    public static ValType ExnRef => new(nullable: true, WasmHeapKind.Exn);

    public bool IsRef => Code == ValueCode.Ref;

    public bool IsNumeric => Code is ValueCode.I32 or ValueCode.I64 or ValueCode.F32 or ValueCode.F64;

    public bool IsPacked => Code is ValueCode.I8 or ValueCode.I16;

    /// <summary>Whether the type has a default value (the numbers, the vectors and the nullable references).</summary>
    public bool IsDefaultable => Code != ValueCode.Ref || Nullable;

    /// <summary>The type of the values of a storage type (the packed types are i32).</summary>
    public ValType Unpacked => IsPacked ? I32 : this;

    /// <summary>The size of a storage type in bytes (the size of an array element).</summary>
    public int StorageSize => Code switch
    {
        ValueCode.I8 => 1,
        ValueCode.I16 => 2,
        ValueCode.I32 or ValueCode.F32 => 4,
        ValueCode.I64 or ValueCode.F64 => 8,
        ValueCode.V128 => 16,
        _ => 8,
    };

    public ValType AsNonNullable() => Code != ValueCode.Ref ? this : Defined is { } defined ? new ValType(nullable: false, defined) : new ValType(nullable: false, Heap);

    public ValType AsNullable() => Code != ValueCode.Ref ? this : Defined is { } defined ? new ValType(nullable: true, defined) : new ValType(nullable: true, Heap);

    public bool Equals(ValType other) => Code == other.Code && Nullable == other.Nullable && Heap == other.Heap && ReferenceEquals(Defined, other.Defined);

    public override bool Equals([System.Diagnostics.CodeAnalysis.NotNullWhen(true)] object? obj) => obj is ValType other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(Code, Nullable, Heap, Defined);

    public static bool operator ==(ValType left, ValType right) => left.Equals(right);

    public static bool operator !=(ValType left, ValType right) => !left.Equals(right);

    /// <summary>The public description of the type (a concrete heap type is only described by its kind).</summary>
    public WasmValueType ToPublic() => Code switch
    {
        ValueCode.I32 or ValueCode.I8 or ValueCode.I16 => WasmValueType.I32,
        ValueCode.I64 => WasmValueType.I64,
        ValueCode.F32 => WasmValueType.F32,
        ValueCode.F64 => WasmValueType.F64,
        ValueCode.V128 => WasmValueType.V128,
        _ => new WasmValueType(WasmValueKind.Ref, Heap, Nullable),
    };

    /// <summary>The type of a public description: a concrete heap type cannot be described.</summary>
    /// <exception cref="NotSupportedException">The type has a concrete heap type.</exception>
    public static ValType FromPublic(WasmValueType type)
    {
        return type.Kind switch
        {
            WasmValueKind.I32 => I32,
            WasmValueKind.I64 => I64,
            WasmValueKind.F32 => F32,
            WasmValueKind.F64 => F64,
            WasmValueKind.V128 => V128,
            _ => type.Heap is WasmHeapKind.ConcreteFunc or WasmHeapKind.ConcreteArray or WasmHeapKind.ConcreteStruct or WasmHeapKind.ConcreteExn
                ? throw new NotSupportedException("A concrete heap type cannot be created from its description")
                : new ValType(type.Nullable, type.Heap),
        };
    }

    public override string ToString() => Code switch
    {
        ValueCode.I32 => "i32",
        ValueCode.I64 => "i64",
        ValueCode.F32 => "f32",
        ValueCode.F64 => "f64",
        ValueCode.V128 => "v128",
        ValueCode.I8 => "i8",
        ValueCode.I16 => "i16",
        ValueCode.Bottom => "bot",
        _ => "(ref " + (Nullable ? "null " : "") + (Defined is { } defined ? "$" + defined.Id.ToString(System.Globalization.CultureInfo.InvariantCulture) : Heap.ToString().ToLowerInvariant()) + ")",
    };
}

/// <summary>A field of a struct type or the element of an array type.</summary>
internal readonly record struct FieldType(ValType Type, bool Mutable);

internal enum CompositeKind : byte
{
    Func,
    Struct,
    Array,
}

/// <summary>A canonical defined type of a runtime (a function, struct or array type of a recursion group, with its supertype).</summary>
internal sealed class DefinedType
{
    private WasmFunctionType? _publicFunctionType;

    public DefinedType(int id, CompositeKind kind)
    {
        Id = id;
        Kind = kind;
    }

    /// <summary>A unique number in the runtime.</summary>
    public int Id { get; }

    public CompositeKind Kind { get; }

    public bool Final { get; set; } = true;

    public DefinedType? Super { get; set; }

    /// <summary>The number of supertypes.</summary>
    public int Depth { get; set; }

    public ValType[] Parameters { get; set; } = [];

    public ValType[] Results { get; set; } = [];

    /// <summary>The fields of a struct type, or the element of an array type.</summary>
    public FieldType[] Fields { get; set; } = [];

    public bool IsFunc => Kind == CompositeKind.Func;

    public bool IsStruct => Kind == CompositeKind.Struct;

    public bool IsArray => Kind == CompositeKind.Array;

    public FieldType Element => Fields[0];

    public bool IsSubtypeOf(DefinedType other)
    {
        if (ReferenceEquals(this, other))
            return true;

        // The supertypes form a chain: the type is a subtype when the other type is at its depth in the chain
        if (other.Depth >= Depth)
            return false;

        var type = this;
        while (type.Depth > other.Depth)
        {
            type = type.Super!;
        }

        return ReferenceEquals(type, other);
    }

    /// <summary>The public function type of a function type (keeps this type as its native type).</summary>
    public WasmFunctionType PublicFunctionType => _publicFunctionType ??= new WasmFunctionType(Array.ConvertAll(Parameters, type => type.ToPublic()), Array.ConvertAll(Results, type => type.ToPublic())) { Native = this };

    public override string ToString() => Kind + "#" + Id.ToString(System.Globalization.CultureInfo.InvariantCulture);
}
