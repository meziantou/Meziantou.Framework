using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace Meziantou.Framework.Analyzers.Assertions;

internal static class AssertionsAnalyzerHelpers
{
    internal const string AssertMetadataName = "Meziantou.Framework.Assertions.Assert";

    internal static bool IsAssertReferenceEqualsInvocation(IInvocationOperation invocationOperation, INamedTypeSymbol assertType)
    {
        return invocationOperation.TargetMethod is { IsStatic: true, Name: "ReferenceEquals" } targetMethod &&
            SymbolEqualityComparer.Default.Equals(targetMethod.ContainingType, assertType);
    }

    internal static bool IsAssertIsTypeInvocation(IInvocationOperation invocationOperation, INamedTypeSymbol assertType)
    {
        return invocationOperation.TargetMethod is { IsStatic: true, Name: "IsType" } targetMethod &&
            SymbolEqualityComparer.Default.Equals(targetMethod.ContainingType, assertType);
    }

    internal static IOperation UnwrapImplicitConversion(IOperation operation)
    {
        while (operation is IConversionOperation { IsImplicit: true } conversionOperation)
        {
            operation = conversionOperation.Operand;
        }

        return operation;
    }
}
