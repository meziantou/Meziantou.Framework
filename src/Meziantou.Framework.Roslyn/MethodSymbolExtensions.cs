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

#if !MEZIANTOU_FRAMEWORK_ROSLYN_DISABLE_EMBEDDEDATTRIBUTE
[Microsoft.CodeAnalysis.Embedded]
#endif
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

    /// <summary>
    /// Gets the first return type attribute of <paramref name="method"/> that matches <paramref name="attributeType"/>.
    /// Only the attributes applied to the return type of <paramref name="method"/> are considered. Unlike <c>Attribute.GetCustomAttributes(inherit: true)</c>,
    /// the methods overridden by <paramref name="method"/> are never inspected.
    /// </summary>
    /// <param name="method">The method whose return type attributes are inspected.</param>
    /// <param name="attributeType">The type of the attribute to look for. When <see langword="null"/>, no attribute is returned.</param>
    /// <param name="inherits">
    /// When <see langword="true"/>, an attribute also matches when its class derives from <paramref name="attributeType"/>.
    /// When <see langword="false"/>, the attribute class must be exactly <paramref name="attributeType"/>.
    /// This parameter is ignored when <paramref name="attributeType"/> is sealed, as no other class can derive from it.
    /// Note this is unrelated to the <c>inherit</c> parameter of <c>Attribute.GetCustomAttributes</c>: it never makes the
    /// methods overridden by <paramref name="method"/> be inspected.
    /// </param>
    /// <returns>The first matching attribute, or <see langword="null"/> when the return type of <paramref name="method"/> has no matching attribute.</returns>
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

    /// <summary>
    /// Indicates whether the return type of <paramref name="method"/> has an attribute matching <paramref name="attributeType"/>.
    /// Only the attributes applied to the return type of <paramref name="method"/> are considered. Unlike <c>Attribute.GetCustomAttributes(inherit: true)</c>,
    /// the methods overridden by <paramref name="method"/> are never inspected.
    /// </summary>
    /// <param name="method">The method whose return type attributes are inspected.</param>
    /// <param name="attributeType">The type of the attribute to look for. When <see langword="null"/>, the method returns <see langword="false"/>.</param>
    /// <param name="inherits">
    /// When <see langword="true"/>, an attribute also matches when its class derives from <paramref name="attributeType"/>.
    /// When <see langword="false"/>, the attribute class must be exactly <paramref name="attributeType"/>.
    /// This parameter is ignored when <paramref name="attributeType"/> is sealed, as no other class can derive from it.
    /// Note this is unrelated to the <c>inherit</c> parameter of <c>Attribute.GetCustomAttributes</c>: it never makes the
    /// methods overridden by <paramref name="method"/> be inspected.
    /// </param>
    /// <returns><see langword="true"/> when the return type of <paramref name="method"/> has a matching attribute; otherwise <see langword="false"/>.</returns>
    public static bool HasReturnTypeAttribute(this IMethodSymbol method, [NotNullWhen(true)] ITypeSymbol? attributeType, bool inherits = true)
    {
        return GetReturnTypeAttribute(method, attributeType, inherits) is not null;
    }

    private static bool SymbolEquals(ISymbol? symbol, ISymbol? expectedSymbol)
    {
        return symbol is not null && expectedSymbol is not null && SymbolEqualityComparer.Default.Equals(symbol, expectedSymbol);
    }
}
