using System.Collections.Immutable;
using System.Reflection;

namespace Meziantou.Framework.Diagnostics.ContextSnapshot;

/// <summary>Represents a snapshot of an assembly at a specific point in time.</summary>
public sealed class AssemblySnapshot
{
    /// <summary>Gets the name of the assembly.</summary>
    public string? Name { get; }
    /// <summary>Gets the location of the assembly file on disk.</summary>
    public string? Location { get; }
    /// <summary>Gets a value indicating whether the assembly is loaded for reflection-only.</summary>
    public bool ReflectionOnly { get; }
    /// <summary>Gets the version of the assembly.</summary>
    public Version? Version { get; }
    /// <summary>Gets the flags that describe the attributes of the assembly.</summary>
    public AssemblyNameFlags Flags { get; }
    /// <summary>Gets the public key token of the assembly.</summary>
    public ImmutableArray<byte>? PublicKeyToken { get; }
    /// <summary>Gets the culture information of the assembly.</summary>
    public CultureInfoSnapshot? CultureInfo { get; }
    /// <summary>Gets the collection of modules in the assembly.</summary>
    public ImmutableArray<ModuleSnapshot> Modules { get; }

    internal AssemblySnapshot(Assembly assembly)
    {
        var name = assembly.GetName();
        Name = name.Name;
        PublicKeyToken = name.GetPublicKeyToken()?.ToImmutableArray();
        Location = assembly.IsDynamic ? null : assembly.Location;
        ReflectionOnly = assembly.ReflectionOnly;
        Version = name.Version;
        Flags = name.Flags;
        CultureInfo = CultureInfoSnapshot.Get(name.CultureInfo);
        Modules = assembly.Modules.Select(module => new ModuleSnapshot(module)).ToImmutableArray();
    }
}
