using System.Diagnostics.CodeAnalysis;

namespace Meziantou.Framework.PublicApiGenerator;

/// <summary>
/// A reference to a type, as it appears in a signature, a base type list, a constraint or an attribute argument.
/// </summary>
/// <remarks>
/// Type references support structural equality, which includes nullable annotations and tuple element names,
/// but not <see cref="PublicApiNamedTypeReference.AssemblyName"/> as types are commonly forwarded between assemblies.
/// </remarks>
public abstract class PublicApiTypeReference : IEquatable<PublicApiTypeReference>
{
    private protected PublicApiTypeReference(PublicApiNullableAnnotation nullableAnnotation)
    {
        NullableAnnotation = nullableAnnotation;
    }

    public abstract PublicApiTypeReferenceKind Kind { get; }

    /// <summary>Gets the nullable annotation of this type. Nested types (type arguments, element types) carry their own annotation.</summary>
    public PublicApiNullableAnnotation NullableAnnotation { get; }

    public bool Equals([NotNullWhen(true)] PublicApiTypeReference? other) => PublicApiTypeReferenceComparer.Default.Equals(this, other);

    public override bool Equals([NotNullWhen(true)] object? obj) => Equals(obj as PublicApiTypeReference);

    public override int GetHashCode() => PublicApiTypeReferenceComparer.Default.GetHashCode(this);

    /// <summary>Returns the C# representation of the type.</summary>
    public override string ToString() => PublicApiFormatter.Format(this).Text;
}
