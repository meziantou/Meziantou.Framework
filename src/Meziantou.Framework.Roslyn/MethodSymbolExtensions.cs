#if !MEZIANTOU_FRAMEWORK_ROSLYN_ENABLE_WARNINGS
#pragma warning disable
#endif
#nullable enable
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Meziantou.Framework.Roslyn;

internal static partial class MethodSymbolExtensions
{
    public static bool IsPrimaryConstructor(this IMethodSymbol? methodSymbol, CancellationToken cancellationToken, bool includeRecordDeclarations = false)
    {
        if (methodSymbol is not { MethodKind: MethodKind.Constructor })
            return false;

        foreach (var syntaxReference in methodSymbol.DeclaringSyntaxReferences)
        {
            var syntax = syntaxReference.GetSyntax(cancellationToken);
            if (syntax is ClassDeclarationSyntax or StructDeclarationSyntax || (includeRecordDeclarations && syntax is RecordDeclarationSyntax))
                return true;
        }

        return false;
    }

    public static bool IsInterfaceImplementation(this IMethodSymbol symbol)
    {
        if (symbol.ExplicitInterfaceImplementations.Length > 0)
            return true;

        return IsInterfaceImplementation((ISymbol)symbol);
    }

    public static bool IsInterfaceImplementation(this IPropertySymbol symbol)
    {
        if (symbol.ExplicitInterfaceImplementations.Length > 0)
            return true;

        return IsInterfaceImplementation((ISymbol)symbol);
    }

    public static bool IsInterfaceImplementation(this IEventSymbol symbol)
    {
        if (symbol.ExplicitInterfaceImplementations.Length > 0)
            return true;

        return IsInterfaceImplementation((ISymbol)symbol);
    }

    private static bool IsInterfaceImplementation(this ISymbol symbol)
    {
        return GetImplementedInterfaceMember(symbol) is not null;
    }

    public static IPropertySymbol? GetImplementedInterfaceMember(this IPropertySymbol symbol)
    {
        if (symbol.ExplicitInterfaceImplementations.Length > 0)
            return symbol.ExplicitInterfaceImplementations[0];

        return (IPropertySymbol?)GetImplementedInterfaceMember((ISymbol)symbol);
    }

    public static IEventSymbol? GetImplementedInterfaceMember(this IEventSymbol symbol)
    {
        if (symbol.ExplicitInterfaceImplementations.Length > 0)
            return symbol.ExplicitInterfaceImplementations[0];

        return (IEventSymbol?)GetImplementedInterfaceMember((ISymbol)symbol);
    }

    public static IMethodSymbol? GetImplementedInterfaceMember(this IMethodSymbol symbol)
    {
        if (symbol.ExplicitInterfaceImplementations.Length > 0)
            return symbol.ExplicitInterfaceImplementations[0];

        return (IMethodSymbol?)GetImplementedInterfaceMember((ISymbol)symbol);
    }

    private static ISymbol? GetImplementedInterfaceMember(this ISymbol symbol)
    {
        if (symbol.ContainingType is null)
            return null;

        return symbol.ContainingType.AllInterfaces
            .SelectMany(@interface => @interface.GetMembers())
            .FirstOrDefault(interfaceMember => SymbolEqualityComparer.Default.Equals(symbol, symbol.ContainingType.FindImplementationForInterfaceMember(interfaceMember)));
    }

    public static bool IsOrOverrides(this IMethodSymbol? symbol, IMethodSymbol? baseMethod)
    {
        if (symbol is null || baseMethod is null)
            return false;

        if (SymbolEquals(symbol, baseMethod))
            return true;

        while (symbol is not null)
        {
            if (SymbolEquals(symbol, baseMethod))
                return true;

            symbol = symbol.OverriddenMethod;
        }

        return false;
    }

    public static bool Overrides(this IMethodSymbol? symbol, ISymbol? baseSymbol)
    {
        if (baseSymbol is null)
            return false;

        var currentMethod = symbol?.OverriddenMethod;
        while (currentMethod is not null)
        {
            if (SymbolEqualityComparer.Default.Equals(baseSymbol, currentMethod))
                return true;

            currentMethod = currentMethod.OverriddenMethod;
        }

        return false;
    }

    public static AttributeData? GetReturnTypeAttribute(this IMethodSymbol method, ITypeSymbol? attributeType, bool inherits = true)
    {
        if (attributeType is null)
            return null;

        if (attributeType.IsSealed)
            inherits = false;

        foreach (var attribute in method.GetReturnTypeAttributes())
        {
            if (attribute.AttributeClass is null)
                continue;

            if (inherits)
            {
                if (attribute.AttributeClass.IsOrInheritsFrom(attributeType))
                    return attribute;
            }
            else
            {
                if (SymbolEquals(attributeType, attribute.AttributeClass))
                    return attribute;
            }
        }

        return null;
    }

    public static bool HasReturnTypeAttribute(this IMethodSymbol method, [NotNullWhen(true)] ITypeSymbol? attributeType, bool inherits = true)
    {
        return GetReturnTypeAttribute(method, attributeType, inherits) is not null;
    }

    private static bool SymbolEquals(ISymbol? symbol, ISymbol? expectedSymbol)
    {
        return symbol is not null && expectedSymbol is not null && SymbolEqualityComparer.Default.Equals(symbol, expectedSymbol);
    }
}
