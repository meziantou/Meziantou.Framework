namespace Meziantou.Framework.Assertions.Architecture;

/// <summary>Specifies the shape of a <see cref="TypeReference"/>.</summary>
public enum TypeReferenceKind
{
    /// <summary>A class, a struct, an interface, an enum or a delegate, generic or not (<c>Order</c>, <c>List&lt;Order&gt;</c>).</summary>
    Named,

    /// <summary>An array (<c>Order[]</c>, <c>int[,]</c>).</summary>
    Array,

    /// <summary>An unmanaged pointer (<c>int*</c>).</summary>
    UnmanagedPointer,

    /// <summary>A managed reference (<c>ref int</c>).</summary>
    ByReference,

    /// <summary>A generic parameter of a type or a method (<c>T</c>).</summary>
    GenericParameter,

    /// <summary>A function pointer (<c>delegate*&lt;int, void&gt;</c>).</summary>
    FunctionPointer,
}
