using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace Meziantou.Framework.Analyzers.Assertions;

internal static class AssertionMethodSelectionAnalyzerCommon
{
    private const string NullAssertionMethodName = "Null";
    private const string NotNullAssertionMethodName = "NotNull";
    private const string EqualAssertionMethodName = "Equal";
    private const string NotEqualAssertionMethodName = "NotEqual";

    internal static bool TryGetNullCheckMatch(IInvocationOperation invocationOperation, INamedTypeSymbol assertType, out NullCheckMatch match)
    {
        if (!IsAssertInvocation(invocationOperation, assertType, "True"))
        {
            match = default;
            return false;
        }

        var conditionArgument = invocationOperation.Arguments.FirstOrDefault(argument => argument.Parameter?.Name == "condition");
        if (conditionArgument is null)
        {
            match = default;
            return false;
        }

        var conditionOperation = AssertionsAnalyzerHelpers.UnwrapImplicitConversion(conditionArgument.Value);
        if (conditionOperation is not IBinaryOperation binaryOperation ||
            binaryOperation.OperatorKind is not (BinaryOperatorKind.Equals or BinaryOperatorKind.NotEquals))
        {
            match = default;
            return false;
        }

        if (TryGetNullComparedValue(binaryOperation.LeftOperand, binaryOperation.RightOperand, out var actualOperation) ||
            TryGetNullComparedValue(binaryOperation.RightOperand, binaryOperation.LeftOperand, out actualOperation))
        {
            match = new NullCheckMatch(actualOperation, binaryOperation.OperatorKind == BinaryOperatorKind.Equals ? NullAssertionMethodName : NotNullAssertionMethodName);
            return true;
        }

        match = default;
        return false;
    }

    internal static bool TryGetNullNotNullValueTypeMatch(IInvocationOperation invocationOperation, INamedTypeSymbol assertType, out NullNotNullValueTypeMatch match)
    {
        if (!IsAssertInvocation(invocationOperation, assertType, NullAssertionMethodName, NotNullAssertionMethodName))
        {
            match = default;
            return false;
        }

        var actualArgument = invocationOperation.Arguments.FirstOrDefault(argument => argument.Parameter?.Name == "actual");
        if (actualArgument is null ||
            !AssertionsAnalyzerHelpers.IsNonNullableValueType(AssertionsAnalyzerHelpers.UnwrapImplicitConversion(actualArgument.Value).Type))
        {
            match = default;
            return false;
        }

        var actualOperation = AssertionsAnalyzerHelpers.UnwrapImplicitConversion(actualArgument.Value);
        var valueType = actualOperation.Type;
        if (valueType is null)
        {
            match = default;
            return false;
        }

        match = new NullNotNullValueTypeMatch(
            actualOperation,
            valueType,
            invocationOperation.TargetMethod.Name == NullAssertionMethodName ? EqualAssertionMethodName : NotEqualAssertionMethodName);
        return true;
    }

    internal static bool TryGetSameNotSameValueTypeMatch(IInvocationOperation invocationOperation, INamedTypeSymbol assertType, out SameNotSameValueTypeMatch match)
    {
        if (!IsAssertInvocation(invocationOperation, assertType, "Same", "NotSame"))
        {
            match = default;
            return false;
        }

        IArgumentOperation? expectedArgument = null;
        IArgumentOperation? actualArgument = null;
        foreach (var argument in invocationOperation.Arguments)
        {
            switch (argument.Parameter?.Name)
            {
                case "expected":
                    expectedArgument = argument;
                    break;
                case "actual":
                    actualArgument = argument;
                    break;
            }
        }

        if (expectedArgument is null || actualArgument is null)
        {
            match = default;
            return false;
        }

        var expectedOperation = AssertionsAnalyzerHelpers.UnwrapImplicitConversion(expectedArgument.Value);
        var actualOperation = AssertionsAnalyzerHelpers.UnwrapImplicitConversion(actualArgument.Value);
        if (!AssertionsAnalyzerHelpers.IsValueType(expectedOperation.Type) ||
            !AssertionsAnalyzerHelpers.IsValueType(actualOperation.Type))
        {
            match = default;
            return false;
        }

        match = new SameNotSameValueTypeMatch(
            expectedOperation,
            actualOperation,
            invocationOperation.TargetMethod.Name == "Same" ? EqualAssertionMethodName : NotEqualAssertionMethodName);
        return true;
    }

    private static bool IsAssertInvocation(IInvocationOperation invocationOperation, INamedTypeSymbol assertType, params string[] methodNames)
    {
        return invocationOperation.TargetMethod is { IsStatic: true } targetMethod &&
            SymbolEqualityComparer.Default.Equals(targetMethod.ContainingType, assertType) &&
            methodNames.Contains(targetMethod.Name);
    }

    private static bool TryGetNullComparedValue(IOperation leftOperation, IOperation rightOperation, out IOperation actualOperation)
    {
        leftOperation = AssertionsAnalyzerHelpers.UnwrapImplicitConversion(leftOperation);
        if (leftOperation.ConstantValue is { HasValue: true, Value: null })
        {
            actualOperation = AssertionsAnalyzerHelpers.UnwrapImplicitConversion(rightOperation);
            return true;
        }

        actualOperation = null!;
        return false;
    }

    internal readonly record struct NullCheckMatch(IOperation ActualOperation, string AssertionMethodName);

    internal readonly record struct NullNotNullValueTypeMatch(IOperation ActualOperation, ITypeSymbol ValueType, string AssertionMethodName);

    internal readonly record struct SameNotSameValueTypeMatch(IOperation ExpectedOperation, IOperation ActualOperation, string AssertionMethodName);
}
