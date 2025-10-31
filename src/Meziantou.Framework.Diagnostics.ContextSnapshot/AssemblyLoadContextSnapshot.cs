using System.Collections.Immutable;
using System.Runtime.Loader;

namespace Meziantou.Framework.Diagnostics.ContextSnapshot;

/// <summary>Represents a snapshot of an assembly load context at a specific point in time.</summary>
public sealed class AssemblyLoadContextSnapshot
{
    /// <summary>Gets the name of the assembly load context.</summary>
    public string? Name { get; }
    /// <summary>Gets a value indicating whether the assembly load context is collectible.</summary>
    public bool IsCollectible { get; }
    /// <summary>Gets the collection of assemblies loaded in this context.</summary>
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
