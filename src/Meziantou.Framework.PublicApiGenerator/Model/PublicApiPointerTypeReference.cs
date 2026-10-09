namespace Meziantou.Framework.PublicApiGenerator;

/// <summary>A reference to an unmanaged pointer type.</summary>
public sealed class PublicApiPointerTypeReference : PublicApiTypeReference
{
    internal PublicApiPointerTypeReference(PublicApiTypeReference elementType)
        : base(PublicApiNullableAnnotation.NotAnnotated)
    {
        ElementType = elementType;
    }

    public override PublicApiTypeReferenceKind Kind => PublicApiTypeReferenceKind.PointerType;

    public PublicApiTypeReference ElementType { get; }
}
