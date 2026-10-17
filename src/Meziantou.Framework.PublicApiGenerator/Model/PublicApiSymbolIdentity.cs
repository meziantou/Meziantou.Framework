namespace Meziantou.Framework.PublicApiGenerator;

/// <summary>
/// Identifies a symbol across assemblies. The documentation ID alone is not unique: unrelated assemblies,
/// or the builds of the same assembly for several target frameworks, can declare symbols with the same documentation ID.
/// </summary>
/// <param name="Scope">The scope of the symbol. See <see cref="PublicApiAssembly.Scope"/>.</param>
/// <param name="DocumentationId">The XML documentation ID of the symbol.</param>
public sealed record PublicApiSymbolIdentity(string Scope, string DocumentationId)
{
    public override string ToString() => Scope + "|" + DocumentationId;
}
