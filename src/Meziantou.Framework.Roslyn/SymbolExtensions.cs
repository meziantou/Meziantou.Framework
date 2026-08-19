#if !MEZIANTOU_FRAMEWORK_ROSLYN_ENABLE_WARNINGS
#pragma warning disable
#endif
#nullable enable
using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using Microsoft.CodeAnalysis;

namespace Meziantou.Framework.Roslyn;

#if !MEZIANTOU_FRAMEWORK_ROSLYN_DISABLE_EMBEDDEDATTRIBUTE
[Microsoft.CodeAnalysis.Embedded]
#endif
internal static partial class SymbolExtensions
{
    public static bool IsVisibleOutsideOfAssembly([NotNullWhen(true)] this ISymbol? symbol)
    {
        if (symbol is null)
            return false;

        if (symbol.DeclaredAccessibility is not Accessibility.Public and not Accessibility.Protected and not Accessibility.ProtectedOrInternal)
        {
            return false;
        }

        if (symbol.ContainingType is null)
            return true;

        return IsVisibleOutsideOfAssembly(symbol.ContainingType);
    }

    public static bool IsOverrideOrInterfaceImplementation(this ISymbol? symbol)
    {
        if (symbol is IMethodSymbol methodSymbol)
            return methodSymbol.IsOverride || IsInterfaceImplementation(methodSymbol);

        if (symbol is IPropertySymbol propertySymbol)
            return propertySymbol.IsOverride || IsInterfaceImplementation(propertySymbol);

        if (symbol is IEventSymbol eventSymbol)
            return eventSymbol.IsOverride || IsInterfaceImplementation(eventSymbol);

        return false;
    }

    private static bool IsInterfaceImplementation(IMethodSymbol symbol)
    {
        if (symbol.ExplicitInterfaceImplementations.Length > 0)
            return true;

        return IsInterfaceImplementation((ISymbol)symbol);
    }

    private static bool IsInterfaceImplementation(IPropertySymbol symbol)
    {
        if (symbol.ExplicitInterfaceImplementations.Length > 0)
            return true;

        return IsInterfaceImplementation((ISymbol)symbol);
    }

    private static bool IsInterfaceImplementation(IEventSymbol symbol)
    {
        if (symbol.ExplicitInterfaceImplementations.Length > 0)
            return true;

        return IsInterfaceImplementation((ISymbol)symbol);
    }

    private static bool IsInterfaceImplementation(ISymbol symbol)
    {
        if (symbol.ContainingType is null)
            return false;

        foreach (var interfaceType in symbol.ContainingType.AllInterfaces)
        {
            foreach (var interfaceMember in interfaceType.GetMembers())
            {
                if (SymbolEqualityComparer.Default.Equals(symbol, symbol.ContainingType.FindImplementationForInterfaceMember(interfaceMember)))
                    return true;
            }
        }

        return false;
    }

    public static ITypeSymbol? GetSymbolType(this ISymbol symbol)
    {
        return symbol switch
        {
            IParameterSymbol parameter => parameter.Type,
            IFieldSymbol field => field.Type,
            IPropertySymbol { GetMethod: not null } property => property.Type,
            ILocalSymbol local => local.Type,
            IMethodSymbol method => method.ReturnType,
            INamedTypeSymbol namedType => namedType,
            ITypeParameterSymbol typeParameter => typeParameter,
            _ => null,
        };
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
}
