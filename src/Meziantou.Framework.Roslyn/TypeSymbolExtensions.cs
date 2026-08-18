#if !MEZIANTOU_FRAMEWORK_ROSLYN_ENABLE_WARNINGS
#pragma warning disable
#endif
#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using Microsoft.CodeAnalysis;

namespace Meziantou.Framework.Roslyn;

// http://source.roslyn.io/#Microsoft.CodeAnalysis.Workspaces/Shared/Extensions/ITypeSymbolExtensions.cs,190b4ed0932458fd,references
internal static class TypeSymbolExtensions
{
    public static IList<INamedTypeSymbol> GetAllInterfacesIncludingThis(this ITypeSymbol type)
    {
        var allInterfaces = type.AllInterfaces;
        if (type is INamedTypeSymbol namedType && namedType.TypeKind == TypeKind.Interface && !allInterfaces.Contains(namedType))
        {
            var result = new List<INamedTypeSymbol>(allInterfaces.Length + 1);
            result.AddRange(allInterfaces);
            result.Add(namedType);
            return result;
        }

        return allInterfaces;
    }

    public static IEnumerable<ISymbol> GetAllMembers(this ITypeSymbol? symbol)
    {
        while (symbol is not null)
        {
            foreach (var member in symbol.GetMembers())
                yield return member;

            symbol = symbol.BaseType;
        }
    }

    public static IEnumerable<ISymbol> GetAllMembers(this ITypeSymbol? symbol, string name)
    {
        while (symbol is not null)
        {
            foreach (var member in symbol.GetMembers(name))
                yield return member;

            symbol = symbol.BaseType;
        }
    }

    public static bool InheritsFrom(this ITypeSymbol classSymbol, [NotNullWhen(true)] ITypeSymbol? baseClassType)
    {
        return InheritsFrom(classSymbol, baseClassType, visitedTypeParameters: null);
    }

    private static bool InheritsFrom(this ITypeSymbol classSymbol, [NotNullWhen(true)] ITypeSymbol? baseClassType, HashSet<ITypeParameterSymbol>? visitedTypeParameters)
    {
        if (baseClassType is null)
            return false;

        if (classSymbol is ITypeParameterSymbol typeParameter)
        {
            return AnyConstraintTypeMatches(typeParameter, visitedTypeParameters, (constraintType, visitedTypeParameters) =>
            {
                return !SymbolEquals(constraintType, baseClassType) && constraintType.InheritsFrom(baseClassType, visitedTypeParameters);
            });
        }

        var baseType = classSymbol.BaseType;
        while (baseType is not null)
        {
            if (SymbolEquals(baseClassType, baseType))
                return true;

            baseType = baseType.BaseType;
        }

        return false;
    }

    public static bool Implements(this ITypeSymbol classSymbol, [NotNullWhen(true)] ITypeSymbol? interfaceType)
    {
        return Implements(classSymbol, interfaceType, visitedTypeParameters: null);
    }

    private static bool Implements(this ITypeSymbol classSymbol, [NotNullWhen(true)] ITypeSymbol? interfaceType, HashSet<ITypeParameterSymbol>? visitedTypeParameters)
    {
        if (interfaceType is null)
            return false;

        if (classSymbol is ITypeParameterSymbol typeParameter)
        {
            return AnyConstraintTypeMatches(typeParameter, visitedTypeParameters, (constraintType, visitedTypeParameters) =>
            {
                return SymbolEquals(constraintType, interfaceType) || constraintType.Implements(interfaceType, visitedTypeParameters);
            });
        }

        foreach (var @interface in classSymbol.AllInterfaces)
        {
            if (SymbolEquals(@interface, interfaceType))
                return true;
        }

        return false;
    }

    public static bool ImplementsGenericInterface(this ITypeSymbol classSymbol, [NotNullWhen(true)] ITypeSymbol? interfaceType)
    {
        return ImplementsGenericInterface(classSymbol, interfaceType, visitedTypeParameters: null);
    }

    private static bool ImplementsGenericInterface(this ITypeSymbol classSymbol, [NotNullWhen(true)] ITypeSymbol? interfaceType, HashSet<ITypeParameterSymbol>? visitedTypeParameters)
    {
        if (interfaceType is null)
            return false;

        if (classSymbol is ITypeParameterSymbol typeParameter)
        {
            return AnyConstraintTypeMatches(typeParameter, visitedTypeParameters, (constraintType, visitedTypeParameters) =>
            {
                return SymbolEquals(constraintType.OriginalDefinition, interfaceType.OriginalDefinition) || constraintType.ImplementsGenericInterface(interfaceType, visitedTypeParameters);
            });
        }

        foreach (var iface in classSymbol.AllInterfaces)
        {
            if (SymbolEquals(iface.OriginalDefinition, interfaceType.OriginalDefinition))
                return true;
        }

        return false;
    }

    public static bool IsOrImplements(this ITypeSymbol symbol, [NotNullWhen(true)] ITypeSymbol? interfaceType)
    {
        if (interfaceType is null)
            return false;

        return SymbolEquals(symbol, interfaceType) || symbol.Implements(interfaceType);
    }

    public static bool IsOrInheritFrom(this ITypeSymbol symbol, [NotNullWhen(true)] ITypeSymbol? expectedType)
    {
        return IsOrInheritFrom(symbol, expectedType, visitedTypeParameters: null);
    }

    private static bool IsOrInheritFrom(this ITypeSymbol symbol, [NotNullWhen(true)] ITypeSymbol? expectedType, HashSet<ITypeParameterSymbol>? visitedTypeParameters)
    {
        if (expectedType is null)
            return false;

        if (SymbolEquals(symbol, expectedType))
            return true;

        if (symbol is ITypeParameterSymbol typeParameter)
        {
            return AnyConstraintTypeMatches(typeParameter, visitedTypeParameters, (constraintType, visitedTypeParameters) =>
            {
                return constraintType.IsOrInheritFrom(expectedType, visitedTypeParameters);
            });
        }

        return !expectedType.IsSealed && symbol.InheritsFrom(expectedType, visitedTypeParameters);
    }

    private static bool AnyConstraintTypeMatches(ITypeParameterSymbol typeParameter, HashSet<ITypeParameterSymbol>? visitedTypeParameters, Func<ITypeSymbol, HashSet<ITypeParameterSymbol>, bool> predicate)
    {
        visitedTypeParameters ??= [];
        if (!visitedTypeParameters.Add(typeParameter))
            return false;

        foreach (var constraintType in typeParameter.ConstraintTypes)
        {
            if (predicate(constraintType, visitedTypeParameters))
                return true;
        }

        return false;
    }

    public static bool IsEqualToAny([NotNullWhen(true)] this ITypeSymbol? symbol, params ReadOnlySpan<ITypeSymbol?> expectedTypes)
    {
        if (symbol is null || expectedTypes.IsEmpty)
            return false;

        foreach (var expectedType in expectedTypes)
        {
            if (SymbolEquals(symbol, expectedType))
                return true;
        }

        return false;
    }

    public static bool IsEqualToAny([NotNullWhen(true)] this ITypeSymbol? symbol, [NotNullWhen(true)] ITypeSymbol? expectedType1)
    {
        if (symbol is null)
            return false;

        if (SymbolEquals(symbol, expectedType1))
            return true;

        return false;
    }

    public static bool IsEqualToAny([NotNullWhen(true)] this ITypeSymbol? symbol, ITypeSymbol? expectedType1, ITypeSymbol? expectedType2)
    {
        if (symbol is null)
            return false;

        if (SymbolEquals(symbol, expectedType1))
            return true;

        if (SymbolEquals(symbol, expectedType2))
            return true;

        return false;
    }

    public static bool IsEqualToAny([NotNullWhen(true)] this ITypeSymbol? symbol, ITypeSymbol? expectedType1, ITypeSymbol? expectedType2, ITypeSymbol? expectedType3)
    {
        if (symbol is null)
            return false;

        if (SymbolEquals(symbol, expectedType1))
            return true;

        if (SymbolEquals(symbol, expectedType2))
            return true;

        if (SymbolEquals(symbol, expectedType3))
            return true;

        return false;
    }

    public static bool IsObject([NotNullWhen(true)] this ITypeSymbol? symbol)
    {
        if (symbol is null)
            return false;

        return symbol.SpecialType == SpecialType.System_Object;
    }

    public static bool IsString([NotNullWhen(true)] this ITypeSymbol? symbol)
    {
        if (symbol is null)
            return false;

        return symbol.SpecialType == SpecialType.System_String;
    }

    public static bool IsChar([NotNullWhen(true)] this ITypeSymbol? symbol)
    {
        if (symbol is null)
            return false;

        return symbol.SpecialType == SpecialType.System_Char;
    }

    public static bool IsInt32([NotNullWhen(true)] this ITypeSymbol? symbol)
    {
        if (symbol is null)
            return false;

        return symbol.SpecialType == SpecialType.System_Int32;
    }

    public static bool IsBoolean([NotNullWhen(true)] this ITypeSymbol? symbol)
    {
        if (symbol is null)
            return false;

        return symbol.SpecialType == SpecialType.System_Boolean;
    }

    public static bool IsDateTime([NotNullWhen(true)] this ITypeSymbol? symbol)
    {
        if (symbol is null)
            return false;

        return symbol.SpecialType == SpecialType.System_DateTime;
    }

    public static bool IsEnum([NotNullWhen(returnValue: true)] this ITypeSymbol? symbol)
    {
        return symbol is not null && GetEnumType(symbol) is not null;
    }

    private static bool SymbolEquals(ISymbol? symbol, ISymbol? expectedSymbol)
    {
        return symbol is not null && expectedSymbol is not null && SymbolEqualityComparer.Default.Equals(symbol, expectedSymbol);
    }

    public static INamedTypeSymbol? GetEnumType(this ITypeSymbol? symbol)
    {
        return (symbol as INamedTypeSymbol)?.EnumUnderlyingType;
    }

    public static bool IsNumberType([NotNullWhen(true)] this ITypeSymbol? symbol)
    {
        if (symbol is null)
            return false;

        switch (symbol.SpecialType)
        {
            case SpecialType.System_Int16:
            case SpecialType.System_Int32:
            case SpecialType.System_Int64:
            case SpecialType.System_UInt16:
            case SpecialType.System_UInt32:
            case SpecialType.System_UInt64:
            case SpecialType.System_Single:
            case SpecialType.System_Double:
            case SpecialType.System_Decimal:
            case SpecialType.System_Byte:
            case SpecialType.System_SByte:
                return true;

            default:
                return false;
        }
    }

    /// <summary>
    /// Determines whether the type is a blittable type.
    /// Blittable types: byte, sbyte, short, ushort, int, uint, long, ulong,
    /// float, double, IntPtr, UIntPtr, pointers, enums, and structs
    /// containing only blittable fields.
    /// </summary>
    public static bool IsBlittableType([NotNullWhen(true)] this ITypeSymbol? symbol)
    {
        if (symbol is null)
            return false;

        switch (symbol.SpecialType)
        {
            case SpecialType.System_Byte:
            case SpecialType.System_SByte:
            case SpecialType.System_Int16:
            case SpecialType.System_UInt16:
            case SpecialType.System_Int32:
            case SpecialType.System_UInt32:
            case SpecialType.System_Int64:
            case SpecialType.System_UInt64:
            case SpecialType.System_Single:
            case SpecialType.System_Double:
            case SpecialType.System_IntPtr:
            case SpecialType.System_UIntPtr:
                return true;

            case SpecialType.None:
                break;

            default:
                return false;
        }

        if (symbol.TypeKind is TypeKind.Pointer)
            return true;

        if (symbol is INamedTypeSymbol namedType)
        {
            if (namedType.EnumUnderlyingType is not null)
                return true;

            if (namedType.IsValueType)
            {
                foreach (var member in namedType.GetMembers())
                {
                    if (member is not IFieldSymbol field || field.IsConst || field.IsStatic)
                        continue;

                    if (!field.Type.IsBlittableType())
                        return false;
                }

                return true;
            }
        }

        return false;
    }

    [return: NotNullIfNotNull(nameof(typeSymbol))]
    public static ITypeSymbol? GetUnderlyingNullableTypeOrSelf(this ITypeSymbol? typeSymbol)
    {
        if (typeSymbol is INamedTypeSymbol namedTypeSymbol)
        {
            if (namedTypeSymbol.ConstructedFrom.SpecialType == SpecialType.System_Nullable_T && namedTypeSymbol.TypeArguments.Length == 1)
            {
                return namedTypeSymbol.TypeArguments[0];
            }
        }

        return typeSymbol;
    }
}
