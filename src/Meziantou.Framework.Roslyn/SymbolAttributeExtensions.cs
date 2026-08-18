#nullable enable
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using Microsoft.CodeAnalysis;

namespace Meziantou.Framework.Roslyn;

internal static class SymbolAttributeExtensions
{
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
                if (attribute.AttributeClass.IsOrInheritFrom(attributeType))
                    yield return attribute;
            }
            else
            {
                if (attributeType.IsEqualTo(attribute.AttributeClass))
                    yield return attribute;
            }
        }
    }

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
                if (attribute.AttributeClass.IsOrInheritFrom(attributeType))
                    return attribute;
            }
            else
            {
                if (attributeType.IsEqualTo(attribute.AttributeClass))
                    return attribute;
            }
        }

        return null;
    }

    public static bool HasAttribute(this ISymbol symbol, [NotNullWhen(true)] ITypeSymbol? attributeType, bool inherits = true)
    {
        return GetFirstAttribute(symbol, attributeType, inherits) is not null;
    }
}
