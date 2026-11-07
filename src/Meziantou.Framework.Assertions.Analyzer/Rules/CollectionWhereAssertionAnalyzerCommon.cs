using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;
using Meziantou.Framework.Roslyn;

namespace Meziantou.Framework.Analyzers.Assertions;

/// <summary>
/// Detects assertions applied to a filtered sequence, such as <c>Assert.NotEmpty(collection.Where(predicate))</c>,
/// which can be expressed with the predicate overload of the assertion.
/// </summary>
internal static class CollectionWhereAssertionAnalyzerCommon
{
    private const string ContainsAssertionMethodName = "Contains";
    private const string DoesNotContainAssertionMethodName = "DoesNotContain";
    private const string SingleAssertionMethodName = "Single";

    internal static bool TryCreateSymbols(Compilation compilation, [NotNullWhen(true)] out Symbols? symbols)
    {
        var enumerableType = compilation.GetTypeByMetadataName("System.Linq.Enumerable");
        if (enumerableType is null)
        {
            symbols = null;
            return false;
        }

        // Only the Func<TSource, bool> overload can be moved to the assertion; the indexed one has no equivalent
        var enumerableWhereMethods = enumerableType.GetMembers("Where")
            .OfType<IMethodSymbol>()
            .Where(method => method is { IsStatic: true, IsExtensionMethod: true, Parameters.Length: 2 } &&
                             method.Parameters[1].Type is INamedTypeSymbol { TypeArguments.Length: 2 })
            .Select(method => method.OriginalDefinition)
            .ToImmutableArray();

        if (enumerableWhereMethods.IsDefaultOrEmpty)
        {
            symbols = null;
            return false;
        }

        symbols = new Symbols(enumerableWhereMethods);
        return true;
    }

    internal static bool TryGetAssertionMatch(
        IInvocationOperation invocationOperation,
        INamedTypeSymbol assertType,
        Symbols symbols,
        out CollectionWhereMatch match)
    {
        if (invocationOperation.TargetMethod is not { IsStatic: true } targetMethod ||
            !SymbolEqualityComparer.Default.Equals(targetMethod.ContainingType, assertType))
        {
            match = default;
            return false;
        }

        var assertionMethodName = targetMethod.Name switch
        {
            "NotEmpty" => ContainsAssertionMethodName,
            "Empty" => DoesNotContainAssertionMethodName,
            "Single" => SingleAssertionMethodName,
            _ => null,
        };

        if (assertionMethodName is null)
        {
            match = default;
            return false;
        }

        var actualArgument = invocationOperation.Arguments.FirstOrDefault(argument => argument.Parameter?.Name == "actual");

        // The overload that already takes a predicate is the expected result of the fix
        if (actualArgument?.Parameter is not { } actualParameter ||
            actualParameter.Type.OriginalDefinition.SpecialType != SpecialType.System_Collections_Generic_IEnumerable_T ||
            targetMethod.Parameters.Any(parameter => parameter.Name == "predicate"))
        {
            match = default;
            return false;
        }

        if (actualArgument.Value.UnwrapImplicitConversions() is not IInvocationOperation whereInvocation ||
            !TryGetWhereArguments(whereInvocation, symbols, out var sourceOperation, out var predicateOperation))
        {
            match = default;
            return false;
        }

        match = new CollectionWhereMatch(actualArgument, sourceOperation, predicateOperation, assertionMethodName);
        return true;
    }

    private static bool TryGetWhereArguments(
        IInvocationOperation whereInvocation,
        Symbols symbols,
        [NotNullWhen(true)] out IOperation? sourceOperation,
        [NotNullWhen(true)] out IOperation? predicateOperation)
    {
        if (whereInvocation.TargetMethod.Name != "Where")
        {
            sourceOperation = null;
            predicateOperation = null;
            return false;
        }

        var originalDefinition = (whereInvocation.TargetMethod.ReducedFrom ?? whereInvocation.TargetMethod).OriginalDefinition;
        if (!symbols.EnumerableWhereMethods.Contains(originalDefinition, SymbolEqualityComparer.Default))
        {
            sourceOperation = null;
            predicateOperation = null;
            return false;
        }

        if (whereInvocation.Instance is not null)
        {
            sourceOperation = whereInvocation.Instance.UnwrapImplicitConversions();
            predicateOperation = whereInvocation.Arguments[0].Value.UnwrapImplicitConversions();
            return true;
        }

        var sourceArgument = whereInvocation.Arguments.FirstOrDefault(argument => argument.Parameter?.Name == "source");
        var predicateArgument = whereInvocation.Arguments.FirstOrDefault(argument => argument.Parameter?.Name == "predicate");
        if (sourceArgument is null || predicateArgument is null)
        {
            sourceOperation = null;
            predicateOperation = null;
            return false;
        }

        sourceOperation = sourceArgument.Value.UnwrapImplicitConversions();
        predicateOperation = predicateArgument.Value.UnwrapImplicitConversions();
        return true;
    }

    internal readonly record struct Symbols(ImmutableArray<IMethodSymbol> EnumerableWhereMethods);

    internal readonly record struct CollectionWhereMatch(
        IArgumentOperation ActualArgument,
        IOperation SourceOperation,
        IOperation PredicateOperation,
        string AssertionMethodName);
}
