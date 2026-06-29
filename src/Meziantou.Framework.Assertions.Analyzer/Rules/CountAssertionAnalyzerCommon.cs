using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace Meziantou.Framework.Analyzers.Assertions;

internal static class CountAssertionAnalyzerCommon
{
    private const string EmptyAssertionMethodName = "Empty";
    private const string HasCountAssertionMethodName = "HasCount";
    private const string DoesNotHaveCountAssertionMethodName = "DoesNotHaveCount";
    private const string HasCountLessThanAssertionMethodName = "HasCountLessThan";
    private const string HasCountLessThanOrEqualAssertionMethodName = "HasCountLessThanOrEqual";
    private const string HasCountGreaterThanAssertionMethodName = "HasCountGreaterThan";
    private const string HasCountGreaterThanOrEqualAssertionMethodName = "HasCountGreaterThanOrEqual";

    internal static bool TryCreateSymbols(Compilation compilation, out Symbols symbols)
    {
        var arrayType = compilation.GetSpecialType(SpecialType.System_Array);
        if (arrayType is null)
        {
            symbols = default;
            return false;
        }

        var collectionType = compilation.GetTypeByMetadataName("System.Collections.ICollection");
        var genericCollectionType = compilation.GetTypeByMetadataName("System.Collections.Generic.ICollection`1");
        var enumerableType = compilation.GetTypeByMetadataName("System.Linq.Enumerable");
        if (collectionType is null || genericCollectionType is null || enumerableType is null)
        {
            symbols = default;
            return false;
        }

        var nonGenericICollectionCountProperty = collectionType.GetMembers("Count").OfType<IPropertySymbol>().FirstOrDefault();
        var genericICollectionCountPropertyDefinition = genericCollectionType.GetMembers("Count").OfType<IPropertySymbol>().FirstOrDefault();
        var enumerableCountMethodDefinitions = enumerableType.GetMembers("Count")
            .OfType<IMethodSymbol>()
            .Where(m => m is { IsStatic: true, IsExtensionMethod: true, Parameters.Length: 1 })
            .Select(m => m.OriginalDefinition)
            .ToImmutableArray();
        if (nonGenericICollectionCountProperty is null || genericICollectionCountPropertyDefinition is null || enumerableCountMethodDefinitions.IsDefaultOrEmpty)
        {
            symbols = default;
            return false;
        }

        symbols = new Symbols(arrayType, nonGenericICollectionCountProperty, genericICollectionCountPropertyDefinition, enumerableCountMethodDefinitions);
        return true;
    }

    internal static bool TryGetAssertionMatch(IInvocationOperation invocationOperation, INamedTypeSymbol assertType, Symbols symbols, out AssertionMatch match)
    {
        if (invocationOperation.TargetMethod is not { IsStatic: true } targetMethod ||
            !SymbolEqualityComparer.Default.Equals(targetMethod.ContainingType, assertType))
        {
            match = default;
            return false;
        }

        switch (targetMethod.Name)
        {
            case "Equal":
                return TryGetAssertEqualAssertionMatch(invocationOperation, symbols, out match);

            case "True":
                return TryGetAssertBooleanAssertionMatch(invocationOperation, symbols, conditionExpectedToBeFalse: false, out match);

            case "False":
                return TryGetAssertBooleanAssertionMatch(invocationOperation, symbols, conditionExpectedToBeFalse: true, out match);
        }

        match = default;
        return false;
    }

    private static bool TryGetAssertEqualAssertionMatch(IInvocationOperation invocationOperation, Symbols symbols, out AssertionMatch match)
    {
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

        if (!TryGetCollectionOperation(actualArgument.Value, symbols, out var collectionOperation, out var countOperation))
        {
            match = default;
            return false;
        }

        if (!TryGetAssertionMethodForEquality(expectedArgument.Value, out var expectedOperation, out var assertionMethodName))
        {
            match = default;
            return false;
        }

        match = new AssertionMatch(expectedOperation, collectionOperation, countOperation, assertionMethodName);
        return true;
    }

    private static bool TryGetAssertBooleanAssertionMatch(IInvocationOperation invocationOperation, Symbols symbols, bool conditionExpectedToBeFalse, out AssertionMatch match)
    {
        var conditionArgument = invocationOperation.Arguments.FirstOrDefault(argument => argument.Parameter?.Name == "condition");
        if (conditionArgument is null)
        {
            match = default;
            return false;
        }

        if (!TryGetCollectionComparisonCondition(conditionArgument.Value, symbols, out var expectedOperation, out var collectionOperation, out var countOperation, out var comparisonOperator, out var collectionOperationOnLeftSide))
        {
            match = default;
            return false;
        }

        if (!TryGetAssertionMethodForComparison(expectedOperation, comparisonOperator, collectionOperationOnLeftSide, conditionExpectedToBeFalse, out var unwrappedExpectedOperation, out var assertionMethodName))
        {
            match = default;
            return false;
        }

        match = new AssertionMatch(unwrappedExpectedOperation, collectionOperation, countOperation, assertionMethodName);
        return true;
    }

    private static bool TryGetCollectionComparisonCondition(
        IOperation conditionOperation,
        Symbols symbols,
        out IOperation expectedOperation,
        out IOperation collectionOperation,
        out IOperation countOperation,
        out BinaryOperatorKind comparisonOperator,
        out bool collectionOperationOnLeftSide)
    {
        conditionOperation = AssertionsAnalyzerHelpers.UnwrapImplicitConversion(conditionOperation);
        if (conditionOperation is not IBinaryOperation binaryOperation ||
            binaryOperation.OperatorKind is not (BinaryOperatorKind.Equals or
                                                BinaryOperatorKind.NotEquals or
                                                BinaryOperatorKind.LessThan or
                                                BinaryOperatorKind.LessThanOrEqual or
                                                BinaryOperatorKind.GreaterThan or
                                                BinaryOperatorKind.GreaterThanOrEqual))
        {
            expectedOperation = null!;
            collectionOperation = null!;
            countOperation = null!;
            comparisonOperator = default;
            collectionOperationOnLeftSide = default;
            return false;
        }

        if (TryGetCollectionOperation(binaryOperation.LeftOperand, symbols, out collectionOperation, out countOperation))
        {
            expectedOperation = binaryOperation.RightOperand;
            comparisonOperator = binaryOperation.OperatorKind;
            collectionOperationOnLeftSide = true;
            return true;
        }

        if (TryGetCollectionOperation(binaryOperation.RightOperand, symbols, out collectionOperation, out countOperation))
        {
            expectedOperation = binaryOperation.LeftOperand;
            comparisonOperator = binaryOperation.OperatorKind;
            collectionOperationOnLeftSide = false;
            return true;
        }

        expectedOperation = null!;
        collectionOperation = null!;
        countOperation = null!;
        comparisonOperator = default;
        collectionOperationOnLeftSide = default;
        return false;
    }

    private static bool TryGetAssertionMethodForEquality(IOperation operation, out IOperation expectedOperation, out string assertionMethodName)
    {
        expectedOperation = AssertionsAnalyzerHelpers.UnwrapImplicitConversion(operation);
        if (NumericHelpers.IsZero(expectedOperation.ConstantValue))
        {
            assertionMethodName = EmptyAssertionMethodName;
            return true;
        }

        if (expectedOperation.Type?.SpecialType == SpecialType.System_Int32)
        {
            assertionMethodName = HasCountAssertionMethodName;
            return true;
        }

        expectedOperation = null!;
        assertionMethodName = null!;
        return false;
    }

    private static bool TryGetAssertionMethodForComparison(IOperation expectedOperation, BinaryOperatorKind comparisonOperator, bool collectionOperationOnLeftSide, bool conditionExpectedToBeFalse, out IOperation unwrappedExpectedOperation, out string assertionMethodName)
    {
        if (conditionExpectedToBeFalse)
            comparisonOperator = NegateComparisonOperator(comparisonOperator);

        if (comparisonOperator == BinaryOperatorKind.Equals)
            return TryGetAssertionMethodForEquality(expectedOperation, out unwrappedExpectedOperation, out assertionMethodName);

        if (!TryGetIntExpectedOperation(expectedOperation, out unwrappedExpectedOperation))
        {
            assertionMethodName = null!;
            return false;
        }

        assertionMethodName = GetAssertionMethodName(comparisonOperator, collectionOperationOnLeftSide);
        return true;
    }

    private static bool TryGetIntExpectedOperation(IOperation operation, out IOperation expectedOperation)
    {
        expectedOperation = AssertionsAnalyzerHelpers.UnwrapImplicitConversion(operation);
        if (expectedOperation.Type?.SpecialType != SpecialType.System_Int32)
        {
            expectedOperation = null!;
            return false;
        }

        return true;
    }

    private static string GetAssertionMethodName(BinaryOperatorKind comparisonOperator, bool collectionOperationOnLeftSide)
    {
        if (comparisonOperator == BinaryOperatorKind.NotEquals)
            return DoesNotHaveCountAssertionMethodName;

        return (comparisonOperator, collectionOperationOnLeftSide) switch
        {
            (BinaryOperatorKind.LessThan, true) => HasCountLessThanAssertionMethodName,
            (BinaryOperatorKind.LessThanOrEqual, true) => HasCountLessThanOrEqualAssertionMethodName,
            (BinaryOperatorKind.GreaterThan, true) => HasCountGreaterThanAssertionMethodName,
            (BinaryOperatorKind.GreaterThanOrEqual, true) => HasCountGreaterThanOrEqualAssertionMethodName,
            (BinaryOperatorKind.LessThan, false) => HasCountGreaterThanAssertionMethodName,
            (BinaryOperatorKind.LessThanOrEqual, false) => HasCountGreaterThanOrEqualAssertionMethodName,
            (BinaryOperatorKind.GreaterThan, false) => HasCountLessThanAssertionMethodName,
            (BinaryOperatorKind.GreaterThanOrEqual, false) => HasCountLessThanOrEqualAssertionMethodName,
            _ => throw new ArgumentOutOfRangeException(nameof(comparisonOperator)),
        };
    }

    private static BinaryOperatorKind NegateComparisonOperator(BinaryOperatorKind comparisonOperator)
    {
        return comparisonOperator switch
        {
            BinaryOperatorKind.Equals => BinaryOperatorKind.NotEquals,
            BinaryOperatorKind.NotEquals => BinaryOperatorKind.Equals,
            BinaryOperatorKind.LessThan => BinaryOperatorKind.GreaterThanOrEqual,
            BinaryOperatorKind.LessThanOrEqual => BinaryOperatorKind.GreaterThan,
            BinaryOperatorKind.GreaterThan => BinaryOperatorKind.LessThanOrEqual,
            BinaryOperatorKind.GreaterThanOrEqual => BinaryOperatorKind.LessThan,
            _ => throw new ArgumentOutOfRangeException(nameof(comparisonOperator)),
        };
    }

    private static bool TryGetCollectionOperation(IOperation operation, Symbols symbols, out IOperation collectionOperation, out IOperation countOperation)
    {
        operation = AssertionsAnalyzerHelpers.UnwrapImplicitConversion(operation);

        switch (operation)
        {
            case IPropertyReferenceOperation { Instance: { } instance } propertyReferenceOperation
                when IsArrayLengthProperty(propertyReferenceOperation.Property, symbols) ||
                     IsCollectionCountProperty(propertyReferenceOperation.Property, symbols):
                collectionOperation = instance;
                countOperation = propertyReferenceOperation;
                return true;

            case IInvocationOperation invocationOperation when IsEnumerableCountInvocation(invocationOperation, symbols):
                countOperation = invocationOperation;
                if (invocationOperation.Instance is not null)
                {
                    collectionOperation = invocationOperation.Instance;
                    return true;
                }

                if (invocationOperation.Arguments.FirstOrDefault(a => a.Parameter?.Name == "source") is { } sourceArgument)
                {
                    collectionOperation = sourceArgument.Value;
                    return true;
                }

                break;
        }

        collectionOperation = null!;
        countOperation = null!;
        return false;
    }

    private static bool IsArrayLengthProperty(IPropertySymbol property, Symbols symbols)
    {
        return property is { Name: "Length" } &&
               SymbolEqualityComparer.Default.Equals(property.ContainingType, symbols.ArrayType);
    }

    private static bool IsCollectionCountProperty(IPropertySymbol property, Symbols symbols)
    {
        if (property.Name != "Count")
            return false;

        if (SymbolEqualityComparer.Default.Equals(property, symbols.NonGenericICollectionCountProperty) ||
            SymbolEqualityComparer.Default.Equals(property.OriginalDefinition, symbols.GenericICollectionCountPropertyDefinition))
        {
            return true;
        }

        if (property.ContainingType.FindImplementationForInterfaceMember(symbols.NonGenericICollectionCountProperty) is { } nonGenericImplementation &&
            SymbolEqualityComparer.Default.Equals(nonGenericImplementation, property))
        {
            return true;
        }

        foreach (var interfaceType in property.ContainingType.AllInterfaces)
        {
            if (!SymbolEqualityComparer.Default.Equals(interfaceType.OriginalDefinition, symbols.GenericICollectionCountPropertyDefinition.ContainingType))
                continue;

            var interfaceCountProperty = interfaceType.GetMembers("Count").OfType<IPropertySymbol>().FirstOrDefault();
            if (interfaceCountProperty is null)
                continue;

            if (property.ContainingType.FindImplementationForInterfaceMember(interfaceCountProperty) is { } implementation &&
                SymbolEqualityComparer.Default.Equals(implementation, property))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsEnumerableCountInvocation(IInvocationOperation invocationOperation, Symbols symbols)
    {
        var targetMethod = invocationOperation.TargetMethod.ReducedFrom ?? invocationOperation.TargetMethod;
        if (targetMethod.Name != "Count")
            return false;

        return symbols.EnumerableCountMethodDefinitions.Any(method => SymbolEqualityComparer.Default.Equals(targetMethod.OriginalDefinition, method));
    }

    internal readonly record struct AssertionMatch(
        IOperation ExpectedOperation,
        IOperation CollectionOperation,
        IOperation CountOperation,
        string AssertionMethodName)
    {
        internal bool UseEmptyAssertion => AssertionMethodName == EmptyAssertionMethodName;
    }

    internal readonly record struct Symbols(
        INamedTypeSymbol ArrayType,
        IPropertySymbol NonGenericICollectionCountProperty,
        IPropertySymbol GenericICollectionCountPropertyDefinition,
        ImmutableArray<IMethodSymbol> EnumerableCountMethodDefinitions);
}
