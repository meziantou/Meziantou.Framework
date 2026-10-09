using System.Collections.Immutable;

namespace Meziantou.Framework.PublicApiGenerator;

/// <summary>A declaration shared by the target frameworks listed in <see cref="TargetFrameworks"/>.</summary>
public sealed class PublicApiSymbolVariant
{
    internal PublicApiSymbolVariant(ImmutableArray<string> targetFrameworks, ImmutableArray<PublicApiSymbol> symbols)
    {
        TargetFrameworks = targetFrameworks;
        Symbols = symbols;
    }

    public ImmutableArray<string> TargetFrameworks { get; }

    /// <summary>Gets the symbol of each target framework of <see cref="TargetFrameworks"/>, in the same order. Each symbol keeps its own metadata provenance.</summary>
    public ImmutableArray<PublicApiSymbol> Symbols { get; }

    /// <summary>Gets the symbol of the first target framework, which can be used to format the declaration of the variant.</summary>
    public PublicApiSymbol Symbol => Symbols[0];

    public override string ToString() => string.Join(", ", TargetFrameworks);
}
