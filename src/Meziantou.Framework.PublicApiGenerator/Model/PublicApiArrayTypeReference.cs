namespace Meziantou.Framework.PublicApiGenerator;

/// <summary>A reference to an array type.</summary>
public sealed class PublicApiArrayTypeReference : PublicApiTypeReference
{
    internal PublicApiArrayTypeReference(PublicApiTypeReference elementType, int rank, bool isSZArray, PublicApiNullableAnnotation nullableAnnotation)
        : base(nullableAnnotation)
    {
        ElementType = elementType;
        Rank = rank;
        IsSZArray = isSZArray;
    }

    public override PublicApiTypeReferenceKind Kind => PublicApiTypeReferenceKind.ArrayType;

    public PublicApiTypeReference ElementType { get; }

    public int Rank { get; }

    /// <summary>Gets a value indicating whether the array is a single-dimensional array with a zero lower bound (e.g. <c>int[]</c>).</summary>
    public bool IsSZArray { get; }
}
