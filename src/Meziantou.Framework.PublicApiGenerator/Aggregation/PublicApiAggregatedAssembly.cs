using System.Collections.Immutable;

namespace Meziantou.Framework.PublicApiGenerator;

/// <summary>The public API of an assembly built for several target frameworks. See <see cref="PublicApi.Aggregate(IReadOnlyList{PublicApiAssembly})"/>.</summary>
public sealed class PublicApiAggregatedAssembly
{
    private readonly Dictionary<string, PublicApiAggregatedSymbol> _symbolsByDocumentationId;

    internal PublicApiAggregatedAssembly(string name, ImmutableArray<PublicApiAssembly> assemblies, ImmutableArray<PublicApiAggregatedSymbol> types)
    {
        Name = name;
        Assemblies = assemblies;
        TargetFrameworks = [.. assemblies.Select(static assembly => assembly.TargetFramework!)];
        Types = types;

        _symbolsByDocumentationId = new Dictionary<string, PublicApiAggregatedSymbol>(StringComparer.Ordinal);
        foreach (var type in types)
        {
            Link(type, declaringType: null);
        }
    }

    /// <summary>Gets the simple name of the assembly.</summary>
    public string Name { get; }

    /// <summary>Gets the target frameworks, in the order of <see cref="Assemblies"/>. They are the values of <see cref="PublicApiAssembly.TargetFramework"/>, unchanged.</summary>
    public ImmutableArray<string> TargetFrameworks { get; }

    /// <summary>Gets the assembly of each target framework.</summary>
    public ImmutableArray<PublicApiAssembly> Assemblies { get; }

    /// <summary>Gets the top-level types of all the target frameworks, sorted by namespace and metadata name.</summary>
    public ImmutableArray<PublicApiAggregatedSymbol> Types { get; }

    /// <summary>Gets the assembly of a target framework, or <see langword="null"/> when the target framework is not part of the aggregation.</summary>
    public PublicApiAssembly? GetAssembly(string targetFramework)
    {
        ArgumentNullException.ThrowIfNull(targetFramework);
        return Assemblies.FirstOrDefault(assembly => string.Equals(assembly.TargetFramework, targetFramework, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Finds an aggregated type or member using its XML documentation ID.</summary>
    public PublicApiAggregatedSymbol? FindSymbolByDocumentationId(string documentationId)
    {
        ArgumentNullException.ThrowIfNull(documentationId);
        return _symbolsByDocumentationId.GetValueOrDefault(documentationId);
    }

    public override string ToString() => Name;

    private void Link(PublicApiAggregatedSymbol symbol, PublicApiAggregatedSymbol? declaringType)
    {
        symbol.Assembly = this;
        symbol.DeclaringType = declaringType;
        _symbolsByDocumentationId.TryAdd(symbol.DocumentationId, symbol);
        foreach (var member in symbol.Members)
        {
            Link(member, symbol);
        }

        foreach (var nestedType in symbol.NestedTypes)
        {
            Link(nestedType, symbol);
        }
    }
}
