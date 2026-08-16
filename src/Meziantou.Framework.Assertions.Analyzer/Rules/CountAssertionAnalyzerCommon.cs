using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace Meziantou.Framework.Analyzers.Assertions;

internal static class CountAssertionAnalyzerCommon
{
    private const string EmptyAssertionMethodName = "Empty";
    private const string NotEmptyAssertionMethodName = "NotEmpty";
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

        // Types that expose the item count through a 'Length' property instead of 'Count'
        var lengthTypeDefinitions = new[]
        {
            compilation.GetTypeByMetadataName("System.Collections.Immutable.ImmutableArray`1"),
            compilation.GetTypeByMetadataName("System.ReadOnlySpan`1"),
            compilation.GetTypeByMetadataName("System.Span`1"),
        }.Where(type => type is not null).Select(type => type!).ToImmutableArray();

        symbols = new Symbols(arrayType, nonGenericICollectionCountProperty, genericICollectionCountPropertyDefinition, enumerableCountMethodDefinitions, lengthTypeDefinitions);
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
                return TryGetAssertEqualAssertionMatch(invocationOperation, symbols, negate: false, out match);

            case "NotEqual":
                return TryGetAssertEqualAssertionMatch(invocationOperation, symbols, negate: true, out match);

            case "True":
                return TryGetAssertBooleanAssertionMatch(invocationOperation, symbols, conditionExpectedToBeFalse: false, out match);

            case "False":
                return TryGetAssertBooleanAssertionMatch(invocationOperation, symbols, conditionExpectedToBeFalse: true, out match);
        }

        match = default;
        return false;
    }

    private static bool TryGetAssertEqualAssertionMatch(IInvocationOperation invocationOperation, Symbols symbols, bool negate, out AssertionMatch match)
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

        if (!TryGetAssertionMethodForEquality(expectedArgument.Value, negate, out var expectedOperation, out var assertionMethodName))
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

    private static bool TryGetAssertionMethodForEquality(IOperation operation, bool negate, out IOperation expectedOperation, out string assertionMethodName)
    {
        expectedOperation = AssertionsAnalyzerHelpers.UnwrapImplicitConversion(operation);
        if (NumericHelpers.IsZero(expectedOperation.ConstantValue))
        {
            assertionMethodName = negate ? NotEmptyAssertionMethodName : EmptyAssertionMethodName;
            return true;
        }

        if (expectedOperation.Type?.SpecialType == SpecialType.System_Int32)
        {
            assertionMethodName = negate ? DoesNotHaveCountAssertionMethodName : HasCountAssertionMethodName;
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

        if (comparisonOperator is BinaryOperatorKind.Equals or BinaryOperatorKind.NotEquals)
            return TryGetAssertionMethodForEquality(expectedOperation, comparisonOperator == BinaryOperatorKind.NotEquals, out unwrappedExpectedOperation, out assertionMethodName);

        if (!TryGetIntExpectedOperation(expectedOperation, out unwrappedExpectedOperation))
        {
            assertionMethodName = null!;
            return false;
        }

        assertionMethodName = GetAssertionMethodName(comparisonOperator, collectionOperationOnLeftSide);
        assertionMethodName = NormalizeToEmptinessAssertion(assertionMethodName, unwrappedExpectedOperation);
        return true;
    }

    /// <summary>
    /// Rewrites count comparisons that are equivalent to an emptiness check, so that <c>list.Count &gt; 0</c>
    /// maps to <c>Assert.NotEmpty</c> rather than <c>Assert.HasCountGreaterThan(0, ...)</c>.
    /// </summary>
    private static string NormalizeToEmptinessAssertion(string assertionMethodName, IOperation expectedOperation)
    {
        if (expectedOperation.ConstantValue is not { HasValue: true, Value: int expectedCount })
            return assertionMethodName;

        return (assertionMethodName, expectedCount) switch
        {
            (HasCountGreaterThanAssertionMethodName, 0) => NotEmptyAssertionMethodName,
            (HasCountGreaterThanOrEqualAssertionMethodName, 1) => NotEmptyAssertionMethodName,
            (HasCountLessThanAssertionMethodName, 1) => EmptyAssertionMethodName,
            (HasCountLessThanOrEqualAssertionMethodName, 0) => EmptyAssertionMethodName,
            _ => assertionMethodName,
        };
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
                when IsLengthProperty(propertyReferenceOperation.Property, symbols) ||
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

    private static bool IsLengthProperty(IPropertySymbol property, Symbols symbols)
    {
        if (property.Name != "Length")
            return false;

        var containingType = property.ContainingType;
        if (containingType.SpecialType == SpecialType.System_String ||
            SymbolEqualityComparer.Default.Equals(containingType, symbols.ArrayType))
        {
            return true;
        }

        foreach (var lengthTypeDefinition in symbols.LengthTypeDefinitions)
        {
            if (SymbolEqualityComparer.Default.Equals(containingType.OriginalDefinition, lengthTypeDefinition))
                return true;
        }

        return false;
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
        /// <summary>
        /// <c>Assert.Empty</c> and <c>Assert.NotEmpty</c> take only the collection, so the expected count is dropped.
        /// </summary>
        internal bool UseEmptyAssertion => AssertionMethodName is EmptyAssertionMethodName or NotEmptyAssertionMethodName;
    }

    internal readonly record struct Symbols(
        INamedTypeSymbol ArrayType,
        IPropertySymbol NonGenericICollectionCountProperty,
        IPropertySymbol GenericICollectionCountPropertyDefinition,
        ImmutableArray<IMethodSymbol> EnumerableCountMethodDefinitions,
        ImmutableArray<INamedTypeSymbol> LengthTypeDefinitions);
}
