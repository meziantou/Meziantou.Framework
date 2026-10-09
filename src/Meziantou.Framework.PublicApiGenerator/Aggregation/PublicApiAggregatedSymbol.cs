using System.Collections.Immutable;

namespace Meziantou.Framework.PublicApiGenerator;

/// <summary>A type or a member of an <see cref="PublicApiAggregatedAssembly"/>, identified by its documentation ID.</summary>
public sealed class PublicApiAggregatedSymbol
{
    internal PublicApiAggregatedSymbol(
        PublicApiSymbolKind kind,
        string documentationId,
        string name,
        ImmutableArray<string> targetFrameworks,
        ImmutableArray<PublicApiSymbol> symbols,
        ImmutableArray<PublicApiSymbolVariant> variants,
        PublicApiSymbolDifferences differences,
        ImmutableArray<PublicApiAggregatedSymbol> members,
        ImmutableArray<PublicApiAggregatedSymbol> nestedTypes)
    {
        Kind = kind;
        DocumentationId = documentationId;
        Name = name;
        TargetFrameworks = targetFrameworks;
        Symbols = symbols;
        Variants = variants;
        Differences = differences;
        Members = members;
        NestedTypes = nestedTypes;
    }

    public PublicApiSymbolKind Kind { get; }

    public string DocumentationId { get; }

    public string Name { get; }

    /// <summary>Gets the aggregated type that declares the symbol, or <see langword="null"/> for top-level types.</summary>
    public PublicApiAggregatedSymbol? DeclaringType { get; internal set; }

    public PublicApiAggregatedAssembly Assembly { get; internal set; } = null!;

    /// <summary>Gets the target frameworks that declare the symbol.</summary>
    public ImmutableArray<string> TargetFrameworks { get; }

    /// <summary>Gets the symbol of each target framework of <see cref="TargetFrameworks"/>, in the same order.</summary>
    public ImmutableArray<PublicApiSymbol> Symbols { get; }

    public bool IsAvailableInAllTargetFrameworks => TargetFrameworks.Length == Assembly.TargetFrameworks.Length;

    /// <summary>Gets the distinct declarations of the symbol. There is a single variant when all the target frameworks share the same declaration.</summary>
    public ImmutableArray<PublicApiSymbolVariant> Variants { get; }

    /// <summary>Gets the parts of the declaration that differ between the variants.</summary>
    public PublicApiSymbolDifferences Differences { get; }

    /// <summary>Gets the members of a type, from all the target frameworks.</summary>
    public ImmutableArray<PublicApiAggregatedSymbol> Members { get; }

    /// <summary>Gets the nested types of a type, from all the target frameworks.</summary>
    public ImmutableArray<PublicApiAggregatedSymbol> NestedTypes { get; }

    /// <summary>Gets the symbol declared for a target framework, or <see langword="null"/> when the target framework does not declare it.</summary>
    public PublicApiSymbol? GetSymbol(string targetFramework)
    {
        ArgumentNullException.ThrowIfNull(targetFramework);
        for (var i = 0; i < TargetFrameworks.Length; i++)
        {
            if (string.Equals(TargetFrameworks[i], targetFramework, StringComparison.OrdinalIgnoreCase))
                return Symbols[i];
        }

        return null;
    }

    public override string ToString() => DocumentationId;
}
