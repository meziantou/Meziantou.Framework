using System.Collections.Immutable;

namespace Meziantou.Framework.PublicApiGenerator;

internal static class PublicApiAggregator
{
    public static PublicApiAggregatedAssembly Aggregate(IReadOnlyList<PublicApiAssembly> assemblies)
    {
        ArgumentNullException.ThrowIfNull(assemblies);
        if (assemblies.Count == 0)
            throw new ArgumentException("At least one assembly must be provided.", nameof(assemblies));

        var targetFrameworks = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var assembly in assemblies)
        {
            if (assembly is null)
                throw new ArgumentException("Assemblies cannot contain null values.", nameof(assemblies));

            // Unrelated assemblies are never merged: types with the same name could be different types
            if (!string.Equals(assembly.Name, assemblies[0].Name, StringComparison.Ordinal))
                throw new ArgumentException($"All the assemblies must have the same name. '{assembly.Name}' does not match '{assemblies[0].Name}'.", nameof(assemblies));

            if (string.IsNullOrWhiteSpace(assembly.TargetFramework))
                throw new ArgumentException($"The target framework of the assembly '{assembly.FullName}' is unknown. Set PublicApiReadOptions.TargetFramework when reading it.", nameof(assemblies));

            if (!targetFrameworks.Add(assembly.TargetFramework))
                throw new ArgumentException($"Multiple assemblies have the target framework '{assembly.TargetFramework}'.", nameof(assemblies));
        }

        var inputs = assemblies.ToImmutableArray();
        var types = AggregateSymbols(inputs, inputs.Select(static assembly => (IEnumerable<PublicApiSymbol>)assembly.Types).ToArray())
            .OrderBy(static type => ((PublicApiType)type.Symbols[0]).Namespace, StringComparer.Ordinal)
            .ThenBy(static type => ((PublicApiType)type.Symbols[0]).FullName, StringComparer.Ordinal)
            .ToImmutableArray();
        return new PublicApiAggregatedAssembly(inputs[0].Name, inputs, types);
    }

    // symbolsByAssembly[i] contains the symbols of inputs[i]. Symbols are matched using their documentation ID.
    private static List<PublicApiAggregatedSymbol> AggregateSymbols(ImmutableArray<PublicApiAssembly> inputs, IEnumerable<PublicApiSymbol>[] symbolsByAssembly)
    {
        var groups = new Dictionary<string, (PublicApiSymbol?[] Symbols, int Order)>(StringComparer.Ordinal);
        for (var assemblyIndex = 0; assemblyIndex < inputs.Length; assemblyIndex++)
        {
            var occurrences = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var symbol in symbolsByAssembly[assemblyIndex])
            {
                // Documentation IDs are unique for assemblies compiled from C#. Otherwise, symbols are matched by occurrence.
                var occurrence = occurrences.TryGetValue(symbol.DocumentationId, out var count) ? count : 0;
                occurrences[symbol.DocumentationId] = occurrence + 1;
                var key = occurrence == 0 ? symbol.DocumentationId : symbol.DocumentationId + "#" + occurrence.ToString(CultureInfo.InvariantCulture);

                if (!groups.TryGetValue(key, out var group))
                {
                    group = (new PublicApiSymbol?[inputs.Length], groups.Count);
                    groups.Add(key, group);
                }

                group.Symbols[assemblyIndex] = symbol;
            }
        }

        var result = new List<PublicApiAggregatedSymbol>(groups.Count);
        foreach (var group in groups.Values.OrderBy(static group => group.Order))
        {
            result.Add(CreateAggregatedSymbol(inputs, group.Symbols));
        }

        return result;
    }

    private static PublicApiAggregatedSymbol CreateAggregatedSymbol(ImmutableArray<PublicApiAssembly> inputs, PublicApiSymbol?[] symbolsByAssembly)
    {
        var targetFrameworks = ImmutableArray.CreateBuilder<string>();
        var symbols = ImmutableArray.CreateBuilder<PublicApiSymbol>();
        for (var i = 0; i < symbolsByAssembly.Length; i++)
        {
            if (symbolsByAssembly[i] is { } symbol)
            {
                targetFrameworks.Add(inputs[i].TargetFramework!);
                symbols.Add(symbol);
            }
        }

        // Symbols are grouped by declaration: two symbols belong to the same variant when their declarations do not differ
        var variants = new List<(List<string> TargetFrameworks, List<PublicApiSymbol> Symbols)>();
        var differences = PublicApiSymbolDifferences.None;
        for (var i = 0; i < symbols.Count; i++)
        {
            var symbol = symbols[i];
            var found = false;
            foreach (var variant in variants)
            {
                var variantDifferences = PublicApiDeclarationComparer.Compare(variant.Symbols[0], symbol);
                if (variantDifferences == PublicApiSymbolDifferences.None)
                {
                    variant.TargetFrameworks.Add(targetFrameworks[i]);
                    variant.Symbols.Add(symbol);
                    found = true;
                    break;
                }
            }

            if (!found)
            {
                if (variants.Count > 0)
                {
                    differences |= PublicApiDeclarationComparer.Compare(variants[0].Symbols[0], symbol);
                }

                variants.Add(([targetFrameworks[i]], [symbol]));
            }
        }

        var members = ImmutableArray<PublicApiAggregatedSymbol>.Empty;
        var nestedTypes = ImmutableArray<PublicApiAggregatedSymbol>.Empty;
        if (symbols[0] is PublicApiType)
        {
            members = [.. AggregateSymbols(inputs, symbolsByAssembly.Select(static symbol => symbol is PublicApiType type ? type.Members.Cast<PublicApiSymbol>() : []).ToArray())];
            nestedTypes = [.. AggregateSymbols(inputs, symbolsByAssembly.Select(static symbol => symbol is PublicApiType type ? type.NestedTypes.Cast<PublicApiSymbol>() : []).ToArray())
                .OrderBy(static type => type.Symbols[0].MetadataName, StringComparer.Ordinal)];
        }

        return new PublicApiAggregatedSymbol(
            symbols[0].Kind,
            symbols[0].DocumentationId,
            symbols[0].Name,
            targetFrameworks.ToImmutable(),
            symbols.ToImmutable(),
            [.. variants.Select(static variant => new PublicApiSymbolVariant([.. variant.TargetFrameworks], [.. variant.Symbols]))],
            differences,
            members,
            nestedTypes);
    }
}
