namespace Meziantou.Framework.PublicApiGenerator;

/// <summary>A reference to a generic parameter of a type or of a method.</summary>
public sealed class PublicApiTypeParameterReference : PublicApiTypeReference
{
    internal PublicApiTypeParameterReference(string name, int ordinal, bool isMethodTypeParameter, PublicApiNullableAnnotation nullableAnnotation)
        : base(nullableAnnotation)
    {
        Name = name;
        Ordinal = ordinal;
        IsMethodTypeParameter = isMethodTypeParameter;
    }

    public override PublicApiTypeReferenceKind Kind => PublicApiTypeReferenceKind.TypeParameter;

    public string Name { get; }

    /// <summary>Gets the position of the generic parameter. For a nested type, the position includes the generic parameters of its containing types, as in metadata.</summary>
    public int Ordinal { get; }

    /// <summary>Gets a value indicating whether the generic parameter is declared by a method rather than by a type.</summary>
    public bool IsMethodTypeParameter { get; }
}
