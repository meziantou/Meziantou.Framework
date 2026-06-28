using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace Meziantou.Framework.Analyzers.Assertions;

#if MEZIANTOU_FRAMEWORK_ASSERTIONS_CODEFIX
internal static class AssertionArgumentOrderAnalyzer
#else
public sealed partial class AssertionArgumentOrderAnalyzer
#endif
{
    internal static bool TryGetAssertionMatch(IInvocationOperation invocationOperation, INamedTypeSymbol assertType, out AssertionMatch match)
    {
        if (invocationOperation.TargetMethod is not { IsStatic: true } targetMethod ||
            !SymbolEqualityComparer.Default.Equals(targetMethod.ContainingType, assertType) ||
            targetMethod.Name is not ("Equal" or "NotEqual"))
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

        match = new AssertionMatch(expectedArgument, actualArgument);
        return true;
    }

    internal static bool IsConstantOrCollectionContainingConstant(IOperation operation)
    {
        operation = AssertionsAnalyzerHelpers.UnwrapImplicitConversion(operation);

        if (operation.ConstantValue.HasValue)
            return true;

        if (operation is ICollectionExpressionOperation collectionExpression)
            return collectionExpression.Elements.Any(IsConstantOrCollectionContainingConstant);

        if (operation is IArrayCreationOperation { Initializer: { } initializer })
            return initializer.ElementValues.Any(IsConstantOrCollectionContainingConstant);

        if (operation is IArrayInitializerOperation arrayInitializer)
            return arrayInitializer.ElementValues.Any(IsConstantOrCollectionContainingConstant);

        return false;
    }

    internal readonly record struct AssertionMatch(IArgumentOperation ExpectedArgument, IArgumentOperation ActualArgument);
}
