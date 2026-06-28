using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace Meziantou.Framework.Analyzers.Assertions;

internal static class CountAssertionAnalyzerCommon
{
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
        if (invocationOperation.TargetMethod is not { IsStatic: true, Name: "Equal" } targetMethod ||
            !SymbolEqualityComparer.Default.Equals(targetMethod.ContainingType, assertType))
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

        if (!TryGetCollectionOperation(actualArgument.Value, symbols, out var collectionOperation, out var countOperation))
        {
            match = default;
            return false;
        }

        var expectedValue = AssertionsAnalyzerHelpers.UnwrapImplicitConversion(expectedArgument.Value);
        var useEmptyAssertion = NumericHelpers.IsZero(expectedValue.ConstantValue);
        if (!useEmptyAssertion && expectedValue.Type?.SpecialType != SpecialType.System_Int32)
        {
            match = default;
            return false;
        }

        match = new AssertionMatch(expectedArgument, actualArgument, collectionOperation, countOperation, useEmptyAssertion);
        return true;
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
        IArgumentOperation ExpectedArgument,
        IArgumentOperation ActualArgument,
        IOperation CollectionOperation,
        IOperation CountOperation,
        bool UseEmptyAssertion);

    internal readonly record struct Symbols(
        INamedTypeSymbol ArrayType,
        IPropertySymbol NonGenericICollectionCountProperty,
        IPropertySymbol GenericICollectionCountPropertyDefinition,
        ImmutableArray<IMethodSymbol> EnumerableCountMethodDefinitions);
}
