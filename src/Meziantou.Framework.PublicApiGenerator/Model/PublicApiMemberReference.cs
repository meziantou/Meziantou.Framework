namespace Meziantou.Framework.PublicApiGenerator;

/// <summary>A reference to a member of another type, such as the interface member implemented by an explicit interface implementation.</summary>
public sealed class PublicApiMemberReference
{
    internal PublicApiMemberReference(PublicApiSymbolKind kind, PublicApiTypeReference containingType, string name, string documentationId)
    {
        Kind = kind;
        ContainingType = containingType;
        Name = name;
        DocumentationId = documentationId;
    }

    public PublicApiSymbolKind Kind { get; }

    /// <summary>Gets the type that declares the member, constructed with the type arguments used by the implementation (e.g. <c>IEquatable&lt;string&gt;</c>).</summary>
    public PublicApiTypeReference ContainingType { get; }

    public string Name { get; }

    /// <summary>Gets the XML documentation ID of the member, as declared by its generic type definition (e.g. <c>M:System.IEquatable`1.Equals(`0)</c>).</summary>
    public string DocumentationId { get; }

    public override string ToString() => DocumentationId;
}
