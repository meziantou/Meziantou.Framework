using System.Collections.Immutable;
using System.Runtime.Loader;

namespace Meziantou.Framework.Diagnostics.ContextSnapshot;

public sealed class AssemblyLoadContextSnapshot
{
    public string? Name { get; }
    public bool IsCollectible { get; }
    public ImmutableArray<AssemblySnapshot> Assemblies { get; }

    private AssemblyLoadContextSnapshot(AssemblyLoadContext assemblyLoadContext)
    {
        Name = assemblyLoadContext.Name;
        IsCollectible = assemblyLoadContext.IsCollectible;
        Assemblies = assemblyLoadContext.Assemblies.Select(asm => new AssemblySnapshot(asm)).ToImmutableArray();
    }

    internal static ImmutableArray<AssemblyLoadContextSnapshot> Get()
    {
        var result = ImmutableArray.CreateBuilder<AssemblyLoadContextSnapshot>();
        foreach (var context in AssemblyLoadContext.All)
        {
            result.Add(new(context));
        }

        return result.ToImmutable();
    }
}
