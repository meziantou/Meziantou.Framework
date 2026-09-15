#if !MEZIANTOU_FRAMEWORK_ROSLYN_ENABLE_WARNINGS
#pragma warning disable
#endif
#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using Microsoft.CodeAnalysis;

namespace Meziantou.Framework.Roslyn;

// http://source.roslyn.io/#Microsoft.CodeAnalysis.Workspaces/Shared/Extensions/ITypeSymbolExtensions.cs,190b4ed0932458fd,references
#if !MEZIANTOU_FRAMEWORK_ROSLYN_DISABLE_EMBEDDEDATTRIBUTE
[Microsoft.CodeAnalysis.Embedded]
#endif
internal static partial class TypeSymbolExtensions
{
    public static ImmutableArray<INamedTypeSymbol> GetAllInterfacesIncludingSelf(this ITypeSymbol type)
    {
        var allInterfaces = type.AllInterfaces;
        if (type is not INamedTypeSymbol { TypeKind: TypeKind.Interface } namedType)
            return allInterfaces;

        foreach (var @interface in allInterfaces)
        {
            if (SymbolEqualityComparer.Default.Equals(@interface, namedType))
                return allInterfaces;
        }

        return allInterfaces.Add(namedType);
    }

    /// <summary>
    /// Enumerates the members of <paramref name="symbol"/> and of its base types.
    /// </summary>
    /// <param name="symbol">The type whose members are enumerated.</param>
    /// <param name="includeInterfaceMembers">
    /// <see langword="true"/> to also enumerate the members of all the interfaces implemented by <paramref name="symbol"/>; otherwise, <see langword="false"/>.
    /// This parameter is ignored when <paramref name="symbol"/> is an interface as the members of its base interfaces are always enumerated.
    /// </param>
    public static IEnumerable<ISymbol> GetAllMembers(this ITypeSymbol? symbol, bool includeInterfaceMembers = false)
    {
        var type = symbol;
        while (type is not null)
        {
            foreach (var member in type.GetMembers())
                yield return member;

            type = type.BaseType;
        }

        if (symbol is not null && (includeInterfaceMembers || symbol.TypeKind is TypeKind.Interface))
        {
            foreach (var @interface in symbol.AllInterfaces)
            {
                foreach (var member in @interface.GetMembers())
                    yield return member;
            }
        }
    }

    /// <summary>
    /// Enumerates the members named <paramref name="name"/> of <paramref name="symbol"/> and of its base types.
    /// </summary>
    /// <param name="symbol">The type whose members are enumerated.</param>
    /// <param name="name">The name of the members to enumerate.</param>
    /// <param name="includeInterfaceMembers">
    /// <see langword="true"/> to also enumerate the members of all the interfaces implemented by <paramref name="symbol"/>; otherwise, <see langword="false"/>.
    /// This parameter is ignored when <paramref name="symbol"/> is an interface as the members of its base interfaces are always enumerated.
    /// </param>
    public static IEnumerable<ISymbol> GetAllMembers(this ITypeSymbol? symbol, string name, bool includeInterfaceMembers = false)
    {
        var type = symbol;
        while (type is not null)
        {
            foreach (var member in type.GetMembers(name))
                yield return member;

            type = type.BaseType;
        }

        if (symbol is not null && (includeInterfaceMembers || symbol.TypeKind is TypeKind.Interface))
        {
            foreach (var @interface in symbol.AllInterfaces)
            {
                foreach (var member in @interface.GetMembers(name))
                    yield return member;
            }
        }
    }

    /// <summary>
    /// Determines whether <paramref name="classSymbol"/> derives from <paramref name="baseClassType"/>. A type is not considered to derive from itself.
    /// </summary>
    /// <remarks>
    /// When <paramref name="classSymbol"/> is a type parameter, its constraint types are inspected and a constraint that is <paramref name="baseClassType"/> is a match,
    /// so <c>T</c> in <c>where T : Base</c> derives from <c>Base</c>. <see cref="Implements(ITypeSymbol, ITypeSymbol)"/> and
    /// <see cref="ImplementsGenericInterface(ITypeSymbol, ITypeSymbol)"/> behave the same way.
    /// </remarks>
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
                return SymbolEquals(constraintType, baseClassType) || constraintType.InheritsFrom(baseClassType, visitedTypeParameters);
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

    /// <summary>
    /// Determines whether <paramref name="classSymbol"/> implements <paramref name="interfaceType"/>. An interface is not considered to implement itself.
    /// </summary>
    /// <remarks>
    /// When <paramref name="classSymbol"/> is a type parameter, its constraint types are inspected and a constraint that is <paramref name="interfaceType"/> is a match,
    /// so <c>T</c> in <c>where T : ISample</c> implements <c>ISample</c>.
    /// </remarks>
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

    /// <summary>
    /// Determines whether <paramref name="classSymbol"/> implements a construction of the generic interface <paramref name="interfaceType"/>, whatever its type arguments are.
    /// An interface is not considered to implement itself.
    /// </summary>
    /// <remarks>
    /// When <paramref name="classSymbol"/> is a type parameter, its constraint types are inspected and a constraint that is a construction of
    /// <paramref name="interfaceType"/> is a match, so <c>T</c> in <c>where T : ISample&lt;int&gt;</c> implements <c>ISample&lt;&gt;</c>.
    /// </remarks>
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

    /// <summary>
    /// Determines whether <paramref name="symbol"/> is <paramref name="interfaceType"/> or implements it.
    /// </summary>
    public static bool IsOrImplements(this ITypeSymbol symbol, [NotNullWhen(true)] ITypeSymbol? interfaceType)
    {
        if (interfaceType is null)
            return false;

        return SymbolEquals(symbol, interfaceType) || symbol.Implements(interfaceType);
    }

    /// <summary>
    /// Determines whether <paramref name="symbol"/> is <paramref name="expectedType"/> or derives from it.
    /// </summary>
    public static bool IsOrInheritsFrom(this ITypeSymbol symbol, [NotNullWhen(true)] ITypeSymbol? expectedType)
    {
        return IsOrInheritsFrom(symbol, expectedType, visitedTypeParameters: null);
    }

    private static bool IsOrInheritsFrom(this ITypeSymbol symbol, [NotNullWhen(true)] ITypeSymbol? expectedType, HashSet<ITypeParameterSymbol>? visitedTypeParameters)
    {
        if (expectedType is null)
            return false;

        if (SymbolEquals(symbol, expectedType))
            return true;

        if (symbol is ITypeParameterSymbol typeParameter)
        {
            return AnyConstraintTypeMatches(typeParameter, visitedTypeParameters, (constraintType, visitedTypeParameters) =>
            {
                return constraintType.IsOrInheritsFrom(expectedType, visitedTypeParameters);
            });
        }

        return !expectedType.IsSealed && symbol.InheritsFrom(expectedType, visitedTypeParameters);
    }

    /// <summary>
    /// Determines whether a value of type <paramref name="type"/> can be assigned to a variable of type <paramref name="targetType"/> while keeping its identity,
    /// that is through an identity, an implicit reference or a boxing conversion.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This follows the C# conversion rules: a type is assignable to itself, to its base types, to the interfaces it implements, and to <see cref="object"/>.
    /// The variance of generic interfaces and delegates, array covariance, and the constraints of type parameters are taken into account, so
    /// <c>string[]</c> is assignable to <c>IEnumerable&lt;object&gt;</c> and <c>T</c> in <c>where T : IDisposable</c> is assignable to <c>IDisposable</c>.
    /// <c>object</c> and <c>dynamic</c> are interchangeable, and so are tuples differing only by their element names.
    /// </para>
    /// <para>
    /// Conversions that produce a different value are not considered: numeric, nullable, user-defined, tuple, pointer or span conversions.
    /// For instance, <c>int</c> is not assignable to <c>long</c> nor to <c>int?</c>, and <c>IEnumerable&lt;int&gt;</c> is not assignable to <c>IEnumerable&lt;object&gt;</c>.
    /// </para>
    /// </remarks>
    public static bool IsAssignableTo(this ITypeSymbol type, [NotNullWhen(true)] ITypeSymbol? targetType)
    {
        if (targetType is null)
            return false;

        return IsAssignableTo(type, targetType, visitedTypeParameters: null, depth: 0);
    }

    /// <summary>
    /// Determines whether a value of type <paramref name="sourceType"/> can be assigned to a variable of type <paramref name="type"/> while keeping its identity.
    /// </summary>
    /// <remarks>
    /// This is the reverse of <see cref="IsAssignableTo(ITypeSymbol, ITypeSymbol)"/>, which documents the conversions taken into account.
    /// </remarks>
    public static bool IsAssignableFrom(this ITypeSymbol type, [NotNullWhen(true)] ITypeSymbol? sourceType)
    {
        if (sourceType is null)
            return false;

        return IsAssignableTo(sourceType, type, visitedTypeParameters: null, depth: 0);
    }

    // Variance lets a contravariant type argument grow at each step, so a pathological hierarchy could recurse forever
    private const int MaxAssignabilityDepth = 32;

    private static bool IsAssignableTo(ITypeSymbol type, ITypeSymbol targetType, HashSet<ITypeParameterSymbol>? visitedTypeParameters, int depth)
    {
        if (IsIdentityConvertible(type, targetType))
            return true;

        if (depth > MaxAssignabilityDepth || type.TypeKind is TypeKind.Error || targetType.TypeKind is TypeKind.Error)
            return false;

        switch (type)
        {
            case ITypeParameterSymbol typeParameter:
                // A type parameter that allows ref structs cannot be boxed, not even to the type parameters it depends on
                if (AllowsRefLikeType(typeParameter))
                    return false;

                if (IsObjectOrDynamic(targetType))
                    return true;

                if ((typeParameter.HasValueTypeConstraint || typeParameter.HasUnmanagedTypeConstraint) && targetType.SpecialType is SpecialType.System_ValueType)
                    return true;

                return AnyConstraintTypeMatches(typeParameter, visitedTypeParameters, (constraintType, visitedTypeParameters) =>
                {
                    return IsAssignableTo(constraintType, targetType, visitedTypeParameters, depth);
                });

            case IArrayTypeSymbol arrayType:
                if (IsObjectOrDynamic(targetType))
                    return true;

                if (targetType is IArrayTypeSymbol targetArrayType)
                {
                    return arrayType.Rank == targetArrayType.Rank
                        && arrayType.IsSZArray == targetArrayType.IsSZArray
                        && HasImplicitReferenceConversion(arrayType.ElementType, targetArrayType.ElementType, depth + 1);
                }

                if (IsAssignableToBaseTypeOrInterface(arrayType, targetType, depth))
                    return true;

                // T[] implements IList<T> and its base interfaces, which are invariant, but the array is still covariant
                if (arrayType.IsSZArray && targetType is INamedTypeSymbol { TypeKind: TypeKind.Interface, TypeArguments.Length: 1 } targetInterface)
                {
                    foreach (var @interface in arrayType.AllInterfaces)
                    {
                        if (@interface.TypeArguments.Length == 1
                            && SymbolEquals(@interface.OriginalDefinition, targetInterface.OriginalDefinition)
                            && IsIdentityConvertible(@interface.TypeArguments[0], arrayType.ElementType))
                        {
                            return HasImplicitReferenceConversion(arrayType.ElementType, targetInterface.TypeArguments[0], depth + 1);
                        }
                    }
                }

                return false;

            case INamedTypeSymbol namedType:
                if (namedType.IsRefLikeType)
                    return false;

                // A nullable value type boxes to the boxed form of its underlying type, or to null
                if (namedType.OriginalDefinition.SpecialType is SpecialType.System_Nullable_T)
                    return targetType.IsReferenceType && targetType.TypeKind is not TypeKind.TypeParameter && IsAssignableTo(namedType.TypeArguments[0], targetType, visitedTypeParameters, depth);

                if (IsObjectOrDynamic(targetType))
                    return true;

                if (namedType.TypeKind is TypeKind.Interface or TypeKind.Delegate && IsVarianceConvertible(namedType, targetType, depth))
                    return true;

                return IsAssignableToBaseTypeOrInterface(namedType, targetType, depth);

            default:
                return false;
        }
    }

    private static bool IsAssignableToBaseTypeOrInterface(ITypeSymbol type, ITypeSymbol targetType, int depth)
    {
        if (targetType.TypeKind is TypeKind.Interface)
        {
            foreach (var @interface in type.AllInterfaces)
            {
                if (IsVarianceConvertible(@interface, targetType, depth))
                    return true;
            }

            return false;
        }

        var baseType = type.BaseType;
        while (baseType is not null)
        {
            if (IsIdentityConvertible(baseType, targetType))
                return true;

            baseType = baseType.BaseType;
        }

        return false;
    }

    private static bool HasImplicitReferenceConversion(ITypeSymbol type, ITypeSymbol targetType, int depth)
    {
        // Any conversion that keeps the identity of a reference type is a reference conversion, boxing only applies to the other types
        return type.IsReferenceType && IsAssignableTo(type, targetType, visitedTypeParameters: null, depth);
    }

    private static bool IsVarianceConvertible(INamedTypeSymbol type, ITypeSymbol targetType, int depth)
    {
        if (IsIdentityConvertible(type, targetType))
            return true;

        if (targetType is not INamedTypeSymbol namedTargetType
            || namedTargetType.TypeKind is not (TypeKind.Interface or TypeKind.Delegate)
            || !SymbolEquals(type.OriginalDefinition, namedTargetType.OriginalDefinition)
            || !AreIdentityConvertible(type.ContainingType, namedTargetType.ContainingType))
        {
            return false;
        }

        var typeParameters = type.OriginalDefinition.TypeParameters;
        for (var i = 0; i < typeParameters.Length; i++)
        {
            var typeArgument = type.TypeArguments[i];
            var targetTypeArgument = namedTargetType.TypeArguments[i];
            if (IsIdentityConvertible(typeArgument, targetTypeArgument))
                continue;

            var isConvertible = typeParameters[i].Variance switch
            {
                VarianceKind.Out => HasImplicitReferenceConversion(typeArgument, targetTypeArgument, depth + 1),
                VarianceKind.In => HasImplicitReferenceConversion(targetTypeArgument, typeArgument, depth + 1),
                _ => false,
            };

            if (!isConvertible)
                return false;
        }

        return true;
    }

    private static bool IsIdentityConvertible(ITypeSymbol type, ITypeSymbol targetType)
    {
        if (SymbolEquals(type, targetType))
            return true;

        if (IsObjectOrDynamic(type) && IsObjectOrDynamic(targetType))
            return true;

        switch (type, targetType)
        {
            case (IArrayTypeSymbol arrayType, IArrayTypeSymbol targetArrayType):
                return arrayType.Rank == targetArrayType.Rank
                    && arrayType.IsSZArray == targetArrayType.IsSZArray
                    && IsIdentityConvertible(arrayType.ElementType, targetArrayType.ElementType);

            case (IPointerTypeSymbol pointerType, IPointerTypeSymbol targetPointerType):
                return IsIdentityConvertible(pointerType.PointedAtType, targetPointerType.PointedAtType);

            // List<dynamic> is List<object>, and (int A, int B) is (int, int)
            case (INamedTypeSymbol { IsGenericType: true } namedType, INamedTypeSymbol { IsGenericType: true } namedTargetType):
                if (!SymbolEquals(namedType.OriginalDefinition, namedTargetType.OriginalDefinition) || namedType.TypeArguments.Length != namedTargetType.TypeArguments.Length)
                    return false;

                for (var i = 0; i < namedType.TypeArguments.Length; i++)
                {
                    if (!IsIdentityConvertible(namedType.TypeArguments[i], namedTargetType.TypeArguments[i]))
                        return false;
                }

                return AreIdentityConvertible(namedType.ContainingType, namedTargetType.ContainingType);

            default:
                return false;
        }
    }

    private static bool AreIdentityConvertible(ITypeSymbol? type, ITypeSymbol? targetType)
    {
        if (type is null || targetType is null)
            return type is null && targetType is null;

        return IsIdentityConvertible(type, targetType);
    }

    private static bool IsObjectOrDynamic(ITypeSymbol type)
    {
        return type.SpecialType is SpecialType.System_Object || type.TypeKind is TypeKind.Dynamic;
    }

    private static bool AllowsRefLikeType(ITypeParameterSymbol typeParameter)
    {
#if ROSLYN_4_12_OR_GREATER
        return typeParameter.AllowsRefLikeType;
#else
        return false;
#endif
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
        return symbol is not null && GetEnumUnderlyingType(symbol) is not null;
    }

    private static bool SymbolEquals(ISymbol? symbol, ISymbol? expectedSymbol)
    {
        return symbol is not null && expectedSymbol is not null && SymbolEqualityComparer.Default.Equals(symbol, expectedSymbol);
    }

    public static INamedTypeSymbol? GetEnumUnderlyingType(this ITypeSymbol? symbol)
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
