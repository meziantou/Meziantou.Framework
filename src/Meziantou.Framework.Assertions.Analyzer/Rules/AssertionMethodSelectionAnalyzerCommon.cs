using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace Meziantou.Framework.Analyzers.Assertions;

internal static class AssertionMethodSelectionAnalyzerCommon
{
    private const string NullAssertionMethodName = "Null";
    private const string NotNullAssertionMethodName = "NotNull";
    private const string EqualAssertionMethodName = "Equal";
    private const string NotEqualAssertionMethodName = "NotEqual";
    private const string IsAssignableToAssertionMethodName = "IsAssignableTo";
    private const string IsNotAssignableToAssertionMethodName = "IsNotAssignableTo";

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
        if (TryGetNullCheckMatchFromBinaryOperation(conditionOperation, out match))
            return true;

        if (TryGetNullCheckMatchFromPattern(conditionOperation, out match))
            return true;

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

    internal static bool TryGetAssignableTypeCheckMatch(IInvocationOperation invocationOperation, INamedTypeSymbol assertType, out AssignableTypeCheckMatch match)
    {
        if (!TryGetAssertCondition(invocationOperation, assertType, out var conditionOperation, out var conditionExpectedToBeFalse))
        {
            match = default;
            return false;
        }

        if (conditionOperation is IIsTypeOperation isTypeOperation &&
            TryGetIsTypeOperationTypeSyntax(isTypeOperation, out var isTypeOperationTypeSyntax))
        {
            var assertionMethodName = conditionExpectedToBeFalse ? IsNotAssignableToAssertionMethodName : IsAssignableToAssertionMethodName;
            match = new AssignableTypeCheckMatch(AssertionsAnalyzerHelpers.UnwrapImplicitConversion(isTypeOperation.ValueOperand), isTypeOperationTypeSyntax, assertionMethodName);
            return true;
        }

        if (conditionOperation is not IIsPatternOperation isPatternOperation ||
            !TryGetTypePattern(isPatternOperation.Pattern, out var typeSyntax, out var isNegated))
        {
            match = default;
            return false;
        }

        var patternAssertionMethodName = conditionExpectedToBeFalse == isNegated ? IsAssignableToAssertionMethodName : IsNotAssignableToAssertionMethodName;
        match = new AssignableTypeCheckMatch(AssertionsAnalyzerHelpers.UnwrapImplicitConversion(isPatternOperation.Value), typeSyntax, patternAssertionMethodName);
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

    private static bool TryGetAssertCondition(IInvocationOperation invocationOperation, INamedTypeSymbol assertType, out IOperation conditionOperation, out bool conditionExpectedToBeFalse)
    {
        if (IsAssertInvocation(invocationOperation, assertType, "True"))
        {
            conditionExpectedToBeFalse = false;
        }
        else if (IsAssertInvocation(invocationOperation, assertType, "False"))
        {
            conditionExpectedToBeFalse = true;
        }
        else
        {
            conditionOperation = null!;
            conditionExpectedToBeFalse = false;
            return false;
        }

        var conditionArgument = invocationOperation.Arguments.FirstOrDefault(argument => argument.Parameter?.Name == "condition");
        if (conditionArgument is null)
        {
            conditionOperation = null!;
            return false;
        }

        conditionOperation = AssertionsAnalyzerHelpers.UnwrapImplicitConversion(conditionArgument.Value);
        return true;
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

    private static bool TryGetNullCheckMatchFromBinaryOperation(IOperation conditionOperation, out NullCheckMatch match)
    {
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

    private static bool TryGetNullCheckMatchFromPattern(IOperation conditionOperation, out NullCheckMatch match)
    {
        if (conditionOperation is not IIsPatternOperation isPatternOperation)
        {
            match = default;
            return false;
        }

        if (IsNullPattern(isPatternOperation.Pattern))
        {
            match = new NullCheckMatch(AssertionsAnalyzerHelpers.UnwrapImplicitConversion(isPatternOperation.Value), NullAssertionMethodName);
            return true;
        }

        if (IsNotNullPattern(isPatternOperation.Pattern))
        {
            match = new NullCheckMatch(AssertionsAnalyzerHelpers.UnwrapImplicitConversion(isPatternOperation.Value), NotNullAssertionMethodName);
            return true;
        }

        match = default;
        return false;
    }

    private static bool IsNullPattern(IPatternOperation pattern)
    {
        return pattern.Syntax is ConstantPatternSyntax { Expression.RawKind: (int)SyntaxKind.NullLiteralExpression };
    }

    private static bool IsNotNullPattern(IPatternOperation pattern)
    {
        return pattern.Syntax is UnaryPatternSyntax
        {
            OperatorToken.RawKind: (int)SyntaxKind.NotKeyword,
            Pattern: ConstantPatternSyntax { Expression.RawKind: (int)SyntaxKind.NullLiteralExpression },
        };
    }

    private static bool TryGetTypePattern(IPatternOperation pattern, out TypeSyntax typeSyntax, out bool isNegated)
    {
        if (pattern.Syntax is TypePatternSyntax { Type: var directTypeSyntax })
        {
            typeSyntax = directTypeSyntax;
            isNegated = false;
            return true;
        }

        if (pattern.Syntax is UnaryPatternSyntax
            {
                OperatorToken.RawKind: (int)SyntaxKind.NotKeyword,
                Pattern: TypePatternSyntax { Type: var negatedTypeSyntax },
            })
        {
            typeSyntax = negatedTypeSyntax;
            isNegated = true;
            return true;
        }

        typeSyntax = null!;
        isNegated = false;
        return false;
    }

    private static bool TryGetIsTypeOperationTypeSyntax(IIsTypeOperation isTypeOperation, out TypeSyntax typeSyntax)
    {
        if (isTypeOperation.Syntax is BinaryExpressionSyntax { Right: TypeSyntax rightTypeSyntax })
        {
            typeSyntax = rightTypeSyntax;
            return true;
        }

        if (isTypeOperation.TypeOperand is not null)
        {
            typeSyntax = SyntaxFactory.ParseTypeName(isTypeOperation.TypeOperand.ToDisplayString());
            return true;
        }

        typeSyntax = null!;
        return false;
    }

    internal readonly record struct NullCheckMatch(IOperation ActualOperation, string AssertionMethodName);

    internal readonly record struct NullNotNullValueTypeMatch(IOperation ActualOperation, ITypeSymbol ValueType, string AssertionMethodName);

    internal readonly record struct AssignableTypeCheckMatch(IOperation ActualOperation, TypeSyntax TypeSyntax, string AssertionMethodName);

    internal readonly record struct SameNotSameValueTypeMatch(IOperation ExpectedOperation, IOperation ActualOperation, string AssertionMethodName);
}
