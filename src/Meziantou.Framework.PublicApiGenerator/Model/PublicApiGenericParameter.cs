using System.Collections.Immutable;

namespace Meziantou.Framework.PublicApiGenerator;

/// <summary>A generic parameter of a type or of a method, with its constraints.</summary>
public sealed class PublicApiGenericParameter
{
    internal PublicApiGenericParameter(
        string name,
        int ordinal,
        bool isMethodTypeParameter,
        PublicApiVariance variance,
        bool hasReferenceTypeConstraint,
        bool hasValueTypeConstraint,
        bool hasUnmanagedTypeConstraint,
        bool hasConstructorConstraint,
        bool allowsRefLikeType,
        ImmutableArray<PublicApiTypeReference> constraintTypes,
        PublicApiNullableAnnotation nullableAnnotation,
        ImmutableArray<PublicApiAttribute> attributes)
    {
        Name = name;
        Ordinal = ordinal;
        IsMethodTypeParameter = isMethodTypeParameter;
        Variance = variance;
        HasReferenceTypeConstraint = hasReferenceTypeConstraint;
        HasValueTypeConstraint = hasValueTypeConstraint;
        HasUnmanagedTypeConstraint = hasUnmanagedTypeConstraint;
        HasConstructorConstraint = hasConstructorConstraint;
        AllowsRefLikeType = allowsRefLikeType;
        ConstraintTypes = constraintTypes;
        NullableAnnotation = nullableAnnotation;
        Attributes = attributes;
    }

    public string Name { get; }

    /// <summary>Gets the position of the generic parameter. For a nested type, the position includes the generic parameters of its containing types, as in metadata.</summary>
    public int Ordinal { get; }

    public bool IsMethodTypeParameter { get; }

    public PublicApiVariance Variance { get; }

    /// <summary>Gets a value indicating whether the parameter has the <c>class</c> constraint.</summary>
    public bool HasReferenceTypeConstraint { get; }

    /// <summary>Gets a value indicating whether the parameter has the <c>struct</c> constraint. It is also set for the <c>unmanaged</c> constraint.</summary>
    public bool HasValueTypeConstraint { get; }

    public bool HasUnmanagedTypeConstraint { get; }

    /// <summary>Gets a value indicating whether the parameter has the <c>new()</c> constraint. It is not set when the <c>struct</c> constraint implies it.</summary>
    public bool HasConstructorConstraint { get; }

    /// <summary>Gets a value indicating whether the parameter has the <c>allows ref struct</c> anti-constraint.</summary>
    public bool AllowsRefLikeType { get; }

    /// <summary>Gets the base type and interface constraints. The <c>System.ValueType</c> constraint that encodes <c>struct</c> is not included.</summary>
    public ImmutableArray<PublicApiTypeReference> ConstraintTypes { get; }

    /// <summary>
    /// Gets the nullable annotation of the generic parameter. With the <c>class</c> constraint, <see cref="PublicApiNullableAnnotation.Annotated"/> means <c>class?</c>.
    /// Without the <c>class</c> and <c>struct</c> constraints, <see cref="PublicApiNullableAnnotation.NotAnnotated"/> means <c>notnull</c>.
    /// </summary>
    public PublicApiNullableAnnotation NullableAnnotation { get; }

    public ImmutableArray<PublicApiAttribute> Attributes { get; }

    public override string ToString() => Name;
}
