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

    public static bool HasAttribute(this ISymbol symbol, [NotNullWhen(true)] ITypeSymbol? attributeType, bool inherits = true)
    {
        return GetFirstAttribute(symbol, attributeType, inherits) is not null;
    }

    private static bool SymbolEquals(ISymbol? symbol, ISymbol? expectedSymbol)
    {
        return symbol is not null && expectedSymbol is not null && SymbolEqualityComparer.Default.Equals(symbol, expectedSymbol);
    }
}
