using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace Meziantou.Framework.FastEnumGenerator;

internal static class FastEnumAnalyzerCommon
{
    internal const string FastEnumAttributeMetadataName = "Meziantou.Framework.Annotations.FastEnumAttribute";

    internal static ImmutableHashSet<INamedTypeSymbol> GetFastEnumTypes(Compilation compilation, INamedTypeSymbol fastEnumAttribute)
    {
        var result = ImmutableHashSet.CreateBuilder<INamedTypeSymbol>(SymbolEqualityComparer.Default);
        foreach (var attribute in compilation.Assembly.GetAttributes())
        {
            if (!SymbolEqualityComparer.Default.Equals(attribute.AttributeClass, fastEnumAttribute))
                continue;

            if (attribute.ConstructorArguments.Length != 1 || attribute.ConstructorArguments[0].Value is not INamedTypeSymbol { TypeKind: TypeKind.Enum } enumType)
                continue;

            _ = result.Add(enumType);
        }

        return result.ToImmutable();
    }

    internal static bool TryGetFastEnumInvocationMatch(IInvocationOperation invocationOperation, INamedTypeSymbol enumType, ImmutableHashSet<INamedTypeSymbol> fastEnumTypes, out FastEnumInvocationMatch match)
    {
        if (TryGetEnumToStringInvocationMatch(invocationOperation, fastEnumTypes, out match))
            return true;

        if (!TryGetSystemEnumMethodKind(invocationOperation.TargetMethod, enumType, out var methodKind))
        {
            match = default;
            return false;
        }

        if (!TryGetTargetEnumType(invocationOperation, out var targetEnumType))
        {
            match = default;
            return false;
        }

        if (!fastEnumTypes.Contains(targetEnumType))
        {
            match = default;
            return false;
        }

        match = new FastEnumInvocationMatch(methodKind, targetEnumType);
        return true;
    }

    private static bool TryGetEnumToStringInvocationMatch(IInvocationOperation invocationOperation, ImmutableHashSet<INamedTypeSymbol> fastEnumTypes, out FastEnumInvocationMatch match)
    {
        if (invocationOperation is
            {
                TargetMethod.Name: nameof(object.ToString),
                TargetMethod.IsStatic: false,
                Arguments.Length: 0,
                Instance.Type: INamedTypeSymbol { TypeKind: TypeKind.Enum } enumType,
            } &&
            fastEnumTypes.Contains(enumType))
        {
            match = new FastEnumInvocationMatch(FastEnumMethodKind.ToString, enumType);
            return true;
        }

        match = default;
        return false;
    }

    private static bool TryGetSystemEnumMethodKind(IMethodSymbol method, INamedTypeSymbol enumType, out FastEnumMethodKind methodKind)
    {
        if (!method.IsStatic || !SymbolEqualityComparer.Default.Equals(method.ContainingType, enumType))
        {
            methodKind = default;
            return false;
        }

        methodKind = method.Name switch
        {
            nameof(Enum.Parse) => FastEnumMethodKind.Parse,
            nameof(Enum.TryParse) => FastEnumMethodKind.TryParse,
            nameof(Enum.GetNames) => FastEnumMethodKind.GetNames,
            nameof(Enum.GetValues) => FastEnumMethodKind.GetValues,
            nameof(Enum.GetName) => FastEnumMethodKind.GetName,
            nameof(Enum.IsDefined) => FastEnumMethodKind.IsDefined,
            _ => default,
        };

        return methodKind is FastEnumMethodKind.Parse or FastEnumMethodKind.TryParse or FastEnumMethodKind.GetNames or FastEnumMethodKind.GetValues or FastEnumMethodKind.GetName or FastEnumMethodKind.IsDefined;
    }

    private static bool TryGetTargetEnumType(IInvocationOperation invocationOperation, out INamedTypeSymbol targetEnumType)
    {
        if (invocationOperation.TargetMethod.IsGenericMethod &&
            invocationOperation.TargetMethod.TypeArguments.Length >= 1 &&
            invocationOperation.TargetMethod.TypeArguments[0] is INamedTypeSymbol { TypeKind: TypeKind.Enum } typeArgument)
        {
            targetEnumType = typeArgument;
            return true;
        }

        if (invocationOperation.Arguments.Length > 0 &&
            UnwrapConversion(invocationOperation.Arguments[0].Value) is ITypeOfOperation { TypeOperand: INamedTypeSymbol { TypeKind: TypeKind.Enum } typeOfOperation })
        {
            targetEnumType = typeOfOperation;
            return true;
        }

        targetEnumType = null!;
        return false;
    }

    internal static bool HasTypeOfFirstArgument(IInvocationOperation invocationOperation)
    {
        return invocationOperation.Arguments.Length > 0 && UnwrapConversion(invocationOperation.Arguments[0].Value) is ITypeOfOperation;
    }

    private static IOperation UnwrapConversion(IOperation operation)
    {
        while (operation is IConversionOperation conversionOperation)
        {
            operation = conversionOperation.Operand;
        }

        return operation;
    }
}
