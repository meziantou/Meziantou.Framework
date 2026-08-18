using System;
using System.Diagnostics.CodeAnalysis;
using Microsoft.CodeAnalysis;

namespace Meziantou.Framework.Roslyn;

internal static class SymbolExtensions
{
    public static bool CanChangeDeclaredType(this ISymbol symbol)
    {
        if (symbol.IsOverride || symbol.IsVirtual || symbol.IsAbstract)
            return false;

        var containingType = symbol.ContainingType;
        if (containingType is null)
            return true;

        foreach (var interfaceType in containingType.AllInterfaces)
        {
            foreach (var interfaceMember in interfaceType.GetMembers())
            {
                if (SymbolEqualityComparer.Default.Equals(containingType.FindImplementationForInterfaceMember(interfaceMember), symbol))
                    return false;
            }
        }

        return true;
    }

    public static Location? GetFirstSourceLocation(this ISymbol symbol)
    {
        foreach (var candidateLocation in symbol.Locations)
        {
            if (candidateLocation.IsInSource)
                return candidateLocation;
        }

        return null;
    }

    public static bool HasAttribute(this ISymbol symbol, string metadataName)
    {
        foreach (var attribute in symbol.GetAttributes())
        {
            if (attribute.AttributeClass is null)
                continue;

            var attributeName = attribute.AttributeClass.ToDisplayString();
            if (string.Equals(attributeName, metadataName, StringComparison.Ordinal) ||
                string.Equals(attributeName, "global::" + metadataName, StringComparison.Ordinal))
                return true;
        }

        return false;
    }

    public static bool IsVisibleOutsideOfAssembly([NotNullWhen(true)] this ISymbol? symbol)
    {
        if (symbol is null)
            return false;

        if (symbol.DeclaredAccessibility is not Accessibility.Public and not Accessibility.Protected and not Accessibility.ProtectedOrInternal)
            return false;

        if (symbol.ContainingType is null)
            return true;

        return symbol.ContainingType.IsVisibleOutsideOfAssembly();
    }
}
