using System.Collections.Immutable;

namespace Meziantou.Framework.PublicApiGenerator;

/// <summary>
/// A reference to a class, struct, interface, enum or delegate, optionally constructed with type arguments.
/// </summary>
/// <remarks>
/// A nested type references its containing type through <see cref="ContainingType"/>. Each level only holds its own type arguments,
/// so <c>Outer&lt;string&gt;.Inner&lt;int&gt;</c> has <c>int</c> as type argument and a containing type constructed with <c>string</c>.
/// Primitive types, such as <c>int</c> or <c>string</c>, are represented by their <c>System</c> type.
/// </remarks>
public sealed class PublicApiNamedTypeReference : PublicApiTypeReference
{
    private string? _documentationId;
    private string? _fullName;

    internal PublicApiNamedTypeReference(
        string @namespace,
        string metadataName,
        PublicApiNamedTypeReference? containingType,
        ImmutableArray<PublicApiTypeReference> typeArguments,
        string? assemblyName,
        bool isValueType,
        PublicApiNullableAnnotation nullableAnnotation,
        ImmutableArray<string?> tupleElementNames = default,
        bool isPrimitive = false,
        bool isFromSerializedName = false)
        : base(nullableAnnotation)
    {
        Namespace = containingType is null ? @namespace : string.Empty;
        MetadataName = metadataName;
        Name = MetadataNameHelper.RemoveGenericArity(metadataName);
        ContainingType = containingType;
        TypeArguments = typeArguments.IsDefault ? [] : typeArguments;
        AssemblyName = assemblyName;
        IsValueType = isValueType;
        TupleElementNames = tupleElementNames.IsDefault ? [] : tupleElementNames;
        IsPrimitive = isPrimitive;
        IsFromSerializedName = isFromSerializedName;
    }

    public override PublicApiTypeReferenceKind Kind => PublicApiTypeReferenceKind.NamedType;

    /// <summary>Gets the namespace of the type. It is empty for nested types and for types of the global namespace.</summary>
    public string Namespace { get; }

    /// <summary>Gets the name of the type, without the generic arity suffix (e.g. <c>List</c>).</summary>
    public string Name { get; }

    /// <summary>Gets the metadata name of the type, including the generic arity suffix (e.g. <c>List`1</c>).</summary>
    public string MetadataName { get; }

    /// <summary>Gets the containing type when the referenced type is nested.</summary>
    public PublicApiNamedTypeReference? ContainingType { get; }

    /// <summary>Gets the type arguments declared at this nesting level. It is empty for non-generic types.</summary>
    public ImmutableArray<PublicApiTypeReference> TypeArguments { get; }

    /// <summary>
    /// Gets the simple name of the assembly that declares the type, as referenced by the inspected assembly.
    /// It is <see langword="null"/> when the assembly cannot be determined from metadata, for instance for a type name serialized in an attribute without an assembly name.
    /// </summary>
    public string? AssemblyName { get; }

    /// <summary>Gets a value indicating whether the signature encodes the type as a value type.</summary>
    public bool IsValueType { get; }

    /// <summary>Gets the element names of a tuple type. It is empty when the type is not a tuple or when its elements are not named. Unnamed elements are <see langword="null"/>.</summary>
    /// <remarks>For tuples of more than 7 elements, the names of the remaining elements are stored on the nested <c>TRest</c> type argument.</remarks>
    public ImmutableArray<string?> TupleElementNames { get; }

    /// <summary>Gets the metadata full name of the referenced type definition, using <c>+</c> to separate nested types (e.g. <c>System.Collections.Generic.Dictionary`2+Enumerator</c>).</summary>
    public string FullName => _fullName ??= ContainingType is null
        ? (Namespace.Length == 0 ? MetadataName : Namespace + "." + MetadataName)
        : ContainingType.FullName + "+" + MetadataName;

    /// <summary>Gets the XML documentation ID of the referenced type definition (e.g. <c>T:System.Collections.Generic.List`1</c>).</summary>
    public string DocumentationId => _documentationId ??= "T:" + DocumentationIdBuilder.GetTypeDefinitionName(this);

    /// <summary>Gets a value indicating whether the type is a constructed <see cref="Nullable{T}"/>.</summary>
    public bool IsNullableValueType => ContainingType is null && TypeArguments.Length == 1 && Namespace is "System" && MetadataName is "Nullable`1";

    /// <summary>Gets a value indicating whether the type is a constructed <see cref="ValueTuple"/>.</summary>
    public bool IsTupleType => ContainingType is null && !TypeArguments.IsEmpty && Namespace is "System" && MetadataName.StartsWith("ValueTuple`", StringComparison.Ordinal);

    // The signature encodes the type with a primitive type code (e.g. ELEMENT_TYPE_I4), not as a type reference
    internal bool IsPrimitive { get; }

    // The type comes from a type name serialized in a custom attribute blob
    internal bool IsFromSerializedName { get; }

    internal bool IsSystemType(string metadataName) => ContainingType is null && Namespace is "System" && string.Equals(MetadataName, metadataName, StringComparison.Ordinal);

    internal IEnumerable<PublicApiTypeReference> GetAllTypeArguments()
    {
        if (ContainingType is not null)
        {
            foreach (var typeArgument in ContainingType.GetAllTypeArguments())
            {
                yield return typeArgument;
            }
        }

        foreach (var typeArgument in TypeArguments)
        {
            yield return typeArgument;
        }
    }
}
