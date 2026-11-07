namespace Meziantou.Framework.Assertions.Architecture;

/// <summary>Options to load an <see cref="ArchitectureModel"/>.</summary>
public sealed class ArchitectureLoadOptions
{
    /// <summary>Gets the paths of the assemblies to load.</summary>
    public IList<string> AssemblyPaths { get; } = [];

    /// <summary>Gets the content of the assemblies to load from memory, such as assemblies compiled at run time. An embedded PDB is read from the image.</summary>
    public IList<ReadOnlyMemory<byte>> AssemblyImages { get; } = [];

    /// <summary>
    /// Gets the folders where the assemblies referenced by the architecture are searched, to resolve base types and interfaces that are defined outside the architecture.
    /// The folders of the loaded assemblies and the assemblies of the running framework are always searched.
    /// </summary>
    public IList<string> ReferenceSearchDirectories { get; } = [];

    /// <summary>Gets or sets a value indicating whether the portable PDBs are read to give the source location of the dependencies. The default value is <see langword="true"/>.</summary>
    public bool ReadSourceLocations { get; set; } = true;
}
