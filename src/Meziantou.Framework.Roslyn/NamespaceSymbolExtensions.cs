#if !MEZIANTOU_FRAMEWORK_ROSLYN_ENABLE_WARNINGS
#pragma warning disable
#endif
#nullable enable
using System;
using Microsoft.CodeAnalysis;

namespace Meziantou.Framework.Roslyn;

#if !MEZIANTOU_FRAMEWORK_ROSLYN_DISABLE_EMBEDDEDATTRIBUTE
[Microsoft.CodeAnalysis.Embedded]
#endif
internal static partial class NamespaceSymbolExtensions
{
    public static bool MatchesNamespace(this INamespaceSymbol namespaceSymbol, ReadOnlySpan<string> namespaceParts)
    {
        for (var i = namespaceParts.Length - 1; i >= 0; i--)
        {
            if (namespaceSymbol is null || namespaceSymbol.IsGlobalNamespace)
                return false;

            if (!string.Equals(namespaceParts[i], namespaceSymbol.Name, System.StringComparison.Ordinal))
                return false;

            namespaceSymbol = namespaceSymbol.ContainingNamespace;
        }

        return namespaceSymbol is null || namespaceSymbol.IsGlobalNamespace;
    }
}
