#if !MEZIANTOU_FRAMEWORK_ROSLYN_ENABLE_WARNINGS
#pragma warning disable
#endif
#nullable enable
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using Microsoft.CodeAnalysis;

namespace Meziantou.Framework.Roslyn;

#if !MEZIANTOU_FRAMEWORK_ROSLYN_DISABLE_EMBEDDEDATTRIBUTE
[Microsoft.CodeAnalysis.Embedded]
#endif
internal static partial class SymbolAttributeExtensions
{
    /// <summary>
    /// Gets the attributes applied to <paramref name="symbol"/> that match <paramref name="attributeType"/>.
    /// Only the attributes applied to the symbol itself are considered. Unlike <c>Attribute.GetCustomAttributes(inherit: true)</c>,
    /// the base types of the symbol and the members it overrides are never inspected.
    /// </summary>
    /// <param name="symbol">The symbol whose attributes are inspected.</param>
    /// <param name="attributeType">The type of the attribute to look for. When <see langword="null"/>, no attribute is returned.</param>
    /// <param name="inherits">
    /// When <see langword="true"/>, an attribute also matches when its class derives from <paramref name="attributeType"/>.
    /// When <see langword="false"/>, the attribute class must be exactly <paramref name="attributeType"/>.
    /// This parameter is ignored when <paramref name="attributeType"/> is sealed, as no other class can derive from it.
    /// Note this is unrelated to the <c>inherit</c> parameter of <c>Attribute.GetCustomAttributes</c>: it never makes the
    /// base types of the symbol or the members it overrides be inspected.
    /// </param>
    /// <returns>The attributes applied to <paramref name="symbol"/> that match <paramref name="attributeType"/>.</returns>
    public static IEnumerable<AttributeData> GetAttributes(this ISymbol symbol, ITypeSymbol? attributeType, bool inherits = true)
    {
        if (attributeType is null)
            yield break;

        if (attributeType.IsSealed)
        {
            inherits = false;
        }

        foreach (var attribute in symbol.GetAttributes())
        {
            if (attribute.AttributeClass is null)
                continue;

            if (inherits)
            {
                if (attribute.AttributeClass.IsOrInheritsFrom(attributeType))
                    yield return attribute;
            }
            else
            {
                if (SymbolEquals(attributeType, attribute.AttributeClass))
                    yield return attribute;
            }
        }
    }

    /// <summary>
    /// Gets the first attribute applied to <paramref name="symbol"/> that matches <paramref name="attributeType"/>.
    /// Only the attributes applied to the symbol itself are considered. Unlike <c>Attribute.GetCustomAttributes(inherit: true)</c>,
    /// the base types of the symbol and the members it overrides are never inspected.
    /// </summary>
    /// <param name="symbol">The symbol whose attributes are inspected.</param>
    /// <param name="attributeType">The type of the attribute to look for. When <see langword="null"/>, no attribute is returned.</param>
    /// <param name="inherits">
    /// When <see langword="true"/>, an attribute also matches when its class derives from <paramref name="attributeType"/>.
    /// When <see langword="false"/>, the attribute class must be exactly <paramref name="attributeType"/>.
    /// This parameter is ignored when <paramref name="attributeType"/> is sealed, as no other class can derive from it.
    /// Note this is unrelated to the <c>inherit</c> parameter of <c>Attribute.GetCustomAttributes</c>: it never makes the
    /// base types of the symbol or the members it overrides be inspected.
    /// </param>
    /// <returns>The first matching attribute, or <see langword="null"/> when <paramref name="symbol"/> has no matching attribute.</returns>
    public static AttributeData? GetFirstAttribute(this ISymbol symbol, ITypeSymbol? attributeType, bool inherits = true)
    {
        if (attributeType is null)
            return null;

        if (attributeType.IsSealed)
            inherits = false;

        foreach (var attribute in symbol.GetAttributes())
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
    /// Indicates whether <paramref name="symbol"/> has an attribute matching <paramref name="attributeType"/>.
    /// Only the attributes applied to the symbol itself are considered. Unlike <c>Attribute.GetCustomAttributes(inherit: true)</c>,
    /// the base types of the symbol and the members it overrides are never inspected.
    /// </summary>
    /// <param name="symbol">The symbol whose attributes are inspected.</param>
    /// <param name="attributeType">The type of the attribute to look for. When <see langword="null"/>, the method returns <see langword="false"/>.</param>
    /// <param name="inherits">
    /// When <see langword="true"/>, an attribute also matches when its class derives from <paramref name="attributeType"/>.
    /// When <see langword="false"/>, the attribute class must be exactly <paramref name="attributeType"/>.
    /// This parameter is ignored when <paramref name="attributeType"/> is sealed, as no other class can derive from it.
    /// Note this is unrelated to the <c>inherit</c> parameter of <c>Attribute.GetCustomAttributes</c>: it never makes the
    /// base types of the symbol or the members it overrides be inspected.
    /// </param>
    /// <returns><see langword="true"/> when <paramref name="symbol"/> has a matching attribute; otherwise <see langword="false"/>.</returns>
    public static bool HasAttribute(this ISymbol symbol, [NotNullWhen(true)] ITypeSymbol? attributeType, bool inherits = true)
    {
        return GetFirstAttribute(symbol, attributeType, inherits) is not null;
    }

    private static bool SymbolEquals(ISymbol? symbol, ISymbol? expectedSymbol)
    {
        return symbol is not null && expectedSymbol is not null && SymbolEqualityComparer.Default.Equals(symbol, expectedSymbol);
    }
}
