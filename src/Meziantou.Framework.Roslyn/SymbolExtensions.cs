using System.Diagnostics.CodeAnalysis;
using Microsoft.CodeAnalysis;

namespace Meziantou.Framework.Roslyn;

internal static class SymbolExtensions
{
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
