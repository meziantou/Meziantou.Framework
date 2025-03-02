using System.Collections.Immutable;
using System.Reflection;

namespace Meziantou.Framework.Diagnostics.ContextSnapshot;

public sealed class AssemblySnapshot
{
    public string? Name { get; }
    public string? Location { get; }
    public bool ReflectionOnly { get; }
    public Version? Version { get; }
    public AssemblyNameFlags Flags { get; }
    public ImmutableArray<byte>? PublicKeyToken { get; }
    public CultureInfoSnapshot? CultureInfo { get; }
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
