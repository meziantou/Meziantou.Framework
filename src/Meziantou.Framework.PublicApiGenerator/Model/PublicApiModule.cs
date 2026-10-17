using System.Collections.Immutable;

namespace Meziantou.Framework.PublicApiGenerator;

/// <summary>The manifest module of an assembly.</summary>
public sealed class PublicApiModule
{
    internal PublicApiModule(string name, Guid moduleVersionId, ImmutableArray<PublicApiAttribute> attributes, ImmutableArray<PublicApiPdbReference> pdbReferences, bool hasEmbeddedPdb)
    {
        Name = name;
        ModuleVersionId = moduleVersionId;
        Attributes = attributes;
        PdbReferences = pdbReferences;
        HasEmbeddedPdb = hasEmbeddedPdb;
    }

    public string Name { get; }

    /// <summary>Gets the module version ID (MVID). It changes on every build that changes the module, and is identical for deterministic builds of the same inputs.</summary>
    public Guid ModuleVersionId { get; }

    public ImmutableArray<PublicApiAttribute> Attributes { get; }

    /// <summary>Gets the CodeView entries of the debug directory, which identify the matching PDB files.</summary>
    public ImmutableArray<PublicApiPdbReference> PdbReferences { get; }

    /// <summary>Gets a value indicating whether a portable PDB is embedded in the module.</summary>
    public bool HasEmbeddedPdb { get; }

    public override string ToString() => Name;
}
