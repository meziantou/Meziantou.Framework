namespace Meziantou.Framework.PublicApiGenerator;

/// <summary>
/// The CodeView debug directory entry of a module, which identifies the PDB that matches the module.
/// </summary>
/// <param name="PdbGuid">The GUID of the PDB. For a portable PDB, the PDB ID is made of this GUID and <paramref name="Stamp"/>.</param>
/// <param name="Age">The age of the PDB.</param>
/// <param name="Stamp">The time stamp of the debug directory entry.</param>
/// <param name="Path">The path of the PDB, as recorded by the compiler. It is machine-specific and must not be used as an identity.</param>
/// <param name="IsPortable">A value indicating whether the PDB is a portable PDB.</param>
public sealed record PublicApiPdbReference(Guid PdbGuid, int Age, uint Stamp, string Path, bool IsPortable);
