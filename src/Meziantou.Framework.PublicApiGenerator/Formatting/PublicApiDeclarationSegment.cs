namespace Meziantou.Framework.PublicApiGenerator;

/// <summary>A part of a formatted declaration, with the symbol or the type it refers to.</summary>
public sealed class PublicApiDeclarationSegment
{
    internal PublicApiDeclarationSegment(PublicApiDeclarationSegmentKind kind, string text, PublicApiTypeReference? typeReference = null, PublicApiSymbol? symbol = null)
    {
        Kind = kind;
        Text = text;
        TypeReference = typeReference;
        Symbol = symbol;
    }

    public PublicApiDeclarationSegmentKind Kind { get; }

    public string Text { get; }

    /// <summary>Gets the type referenced by a <see cref="PublicApiDeclarationSegmentKind.TypeName"/> or a <see cref="PublicApiDeclarationSegmentKind.TypeParameterName"/> segment.</summary>
    /// <remarks>For a nested type, each level is a separate segment that references the corresponding <see cref="PublicApiNamedTypeReference.ContainingType"/>.</remarks>
    public PublicApiTypeReference? TypeReference { get; }

    /// <summary>Gets the declared symbol for an <see cref="PublicApiDeclarationSegmentKind.Identifier"/> segment.</summary>
    public PublicApiSymbol? Symbol { get; }

    public override string ToString() => Text;
}
