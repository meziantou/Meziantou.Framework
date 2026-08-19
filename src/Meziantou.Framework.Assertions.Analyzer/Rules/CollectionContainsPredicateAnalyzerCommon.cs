using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;
using Meziantou.Framework.Roslyn;

namespace Meziantou.Framework.Analyzers.Assertions;

/// <summary>
/// Detects <c>Assert.Contains(collection, item =&gt; item == expected)</c>, where the predicate only compares the item
/// with a value, so the assertion can report the expected value instead of an opaque predicate.
/// </summary>
internal static class CollectionContainsPredicateAnalyzerCommon
{
    internal static bool TryGetAssertionMatch(
        IInvocationOperation invocationOperation,
        INamedTypeSymbol assertType,
        out ContainsPredicateMatch match)
    {
        if (invocationOperation.TargetMethod is not { IsStatic: true, Name: "Contains" or "DoesNotContain" } targetMethod ||
            !SymbolEqualityComparer.Default.Equals(targetMethod.ContainingType, assertType))
        {
            match = default;
            return false;
        }

        var actualArgument = invocationOperation.Arguments.FirstOrDefault(argument => argument.Parameter?.Name == "actual");
        var predicateArgument = invocationOperation.Arguments.FirstOrDefault(argument => argument.Parameter?.Name == "predicate");
        if (actualArgument is null || predicateArgument is null)
        {
            match = default;
            return false;
        }

        var actualOperation = actualArgument.Value.UnwrapImplicitConversions();

        // Assert.Contains(expected, actual) has dedicated string overloads with a different meaning
        if (actualOperation.Type?.SpecialType == SpecialType.System_String)
        {
            match = default;
            return false;
        }

        if (!TryGetComparedValue(predicateArgument.Value, out var expectedOperation))
        {
            match = default;
            return false;
        }

        var messageArgument = invocationOperation.Arguments.FirstOrDefault(argument => argument is { ArgumentKind: ArgumentKind.Explicit, Parameter.Name: "message" });
        match = new ContainsPredicateMatch(actualOperation, expectedOperation, messageArgument, targetMethod.Name);
        return true;
    }

    private static bool TryGetComparedValue(IOperation predicateOperation, [NotNullWhen(true)] out IOperation? expectedOperation)
    {
        expectedOperation = null;

        var operation = predicateOperation.UnwrapImplicitConversions();
        if (operation is IDelegateCreationOperation delegateCreationOperation)
        {
            operation = delegateCreationOperation.Target;
        }

        if (operation is not IAnonymousFunctionOperation { Symbol.Parameters: [var lambdaParameter] } anonymousFunctionOperation ||
            anonymousFunctionOperation.Body is not { Operations: [IReturnOperation { ReturnedValue: { } returnedValue }] })
        {
            return false;
        }

        if (returnedValue.UnwrapImplicitConversions() is not IBinaryOperation { OperatorKind: BinaryOperatorKind.Equals, OperatorMethod: null } binaryOperation)
            return false;

        // The default equality comparer used by the assertion must behave like the == operator
        if (!HasDefaultEqualitySemantics(lambdaParameter.Type))
            return false;

        if (IsParameterReference(binaryOperation.LeftOperand, lambdaParameter))
        {
            expectedOperation = binaryOperation.RightOperand;
        }
        else if (IsParameterReference(binaryOperation.RightOperand, lambdaParameter))
        {
            expectedOperation = binaryOperation.LeftOperand;
        }
        else
        {
            return false;
        }

        // The value must be usable outside of the lambda
        if (!SymbolEqualityComparer.Default.Equals(expectedOperation.Type, lambdaParameter.Type) ||
            ReferencesParameter(expectedOperation, lambdaParameter))
        {
            expectedOperation = null;
            return false;
        }

        return true;
    }

    private static bool IsParameterReference(IOperation operation, IParameterSymbol parameter)
    {
        return operation.UnwrapImplicitConversions() is IParameterReferenceOperation parameterReferenceOperation &&
            SymbolEqualityComparer.Default.Equals(parameterReferenceOperation.Parameter, parameter) &&
            SymbolEqualityComparer.Default.Equals(operation.Type, parameter.Type);
    }

    private static bool ReferencesParameter(IOperation operation, IParameterSymbol parameter)
    {
        foreach (var descendant in operation.DescendantsAndSelf())
        {
            if (descendant is IParameterReferenceOperation parameterReferenceOperation &&
                SymbolEqualityComparer.Default.Equals(parameterReferenceOperation.Parameter, parameter))
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasDefaultEqualitySemantics(ITypeSymbol type)
    {
        if (type.TypeKind == TypeKind.Enum)
            return true;

        // Floating-point types are excluded as == and Equals disagree on NaN
        return type.SpecialType is SpecialType.System_String or
            SpecialType.System_Boolean or
            SpecialType.System_Char or
            SpecialType.System_SByte or
            SpecialType.System_Byte or
            SpecialType.System_Int16 or
            SpecialType.System_UInt16 or
            SpecialType.System_Int32 or
            SpecialType.System_UInt32 or
            SpecialType.System_Int64 or
            SpecialType.System_UInt64 or
            SpecialType.System_IntPtr or
            SpecialType.System_UIntPtr or
            SpecialType.System_Decimal;
    }

    internal readonly record struct ContainsPredicateMatch(
        IOperation ActualOperation,
        IOperation ExpectedOperation,
        IArgumentOperation? MessageArgument,
        string AssertionMethodName);
}
