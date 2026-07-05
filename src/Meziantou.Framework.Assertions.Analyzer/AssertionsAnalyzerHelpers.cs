using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace Meziantou.Framework.Analyzers.Assertions;

internal static class AssertionsAnalyzerHelpers
{
    public const string AssertMetadataName = "Meziantou.Framework.Assertions.Assert";

    public static bool IsAssertReferenceEqualsInvocation(IInvocationOperation invocationOperation, INamedTypeSymbol assertType)
    {
        return invocationOperation.TargetMethod is { IsStatic: true, Name: "ReferenceEquals" } targetMethod &&
            SymbolEqualityComparer.Default.Equals(targetMethod.ContainingType, assertType);
    }

    public static bool IsAssertIsTypeInvocation(IInvocationOperation invocationOperation, INamedTypeSymbol assertType)
    {
        return invocationOperation.TargetMethod is { IsStatic: true, Name: "IsType" } targetMethod &&
            SymbolEqualityComparer.Default.Equals(targetMethod.ContainingType, assertType);
    }

    public static IOperation UnwrapImplicitConversion(IOperation operation)
    {
        while (operation is IConversionOperation { IsImplicit: true } conversionOperation)
        {
            operation = conversionOperation.Operand;
        }

        return operation;
    }

    public static bool IsValueType(ITypeSymbol? type)
    {
        return type?.IsValueType == true;
    }

    public static bool IsNonNullableValueType(ITypeSymbol? type)
    {
        return type is { IsValueType: true } &&
               !IsNullableValueType(type) &&
               !IsCSharpUnionType(type);
    }

    private static bool IsNullableValueType(ITypeSymbol type)
    {
        return type.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T;
    }

    private static bool IsCSharpUnionType(ITypeSymbol type)
    {
        foreach (var attribute in type.GetAttributes())
        {
            if (attribute.AttributeClass?.ToDisplayString() == "System.Runtime.CompilerServices.UnionAttribute")
                return true;
        }

        return false;
    }
}
