using Microsoft.CodeAnalysis;

namespace Meziantou.Framework.Roslyn;

internal static class TypeSymbolExtensions
{
    public static bool IsAssignableTo(this ITypeSymbol typeSymbol, ITypeSymbol baseType)
    {
        if (SymbolEqualityComparer.Default.Equals(typeSymbol, baseType))
            return true;

        if (typeSymbol is not INamedTypeSymbol namedType)
            return false;

        for (var current = namedType.BaseType; current is not null; current = current.BaseType)
        {
            if (SymbolEqualityComparer.Default.Equals(current, baseType))
                return true;
        }

        foreach (var implementedInterface in namedType.AllInterfaces)
        {
            if (SymbolEqualityComparer.Default.Equals(implementedInterface, baseType))
                return true;
        }

        return false;
    }
}
