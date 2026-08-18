using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;
using Meziantou.Framework.Roslyn;

namespace Meziantou.Framework.Analyzers.Assertions;

/// <summary>
/// Detects count assertions that are equivalent to <c>Assert.Empty</c> or <c>Assert.NotEmpty</c>,
/// such as <c>Assert.HasCount(0, actual)</c> or <c>Assert.HasCountGreaterThan(0, actual)</c>.
/// </summary>
internal static class EmptinessAssertionAnalyzerCommon
{
    private const string EmptyAssertionMethodName = "Empty";
    private const string NotEmptyAssertionMethodName = "NotEmpty";

    internal static bool TryGetAssertionMatch(IInvocationOperation invocationOperation, INamedTypeSymbol assertType, out EmptinessMatch match)
    {
        if (invocationOperation.TargetMethod is not { IsStatic: true } targetMethod ||
            !SymbolEqualityComparer.Default.Equals(targetMethod.ContainingType, assertType))
        {
            match = default;
            return false;
        }

        var expectedCountArgument = invocationOperation.Arguments.FirstOrDefault(argument => argument.Parameter?.Name == "expectedCount");
        if (expectedCountArgument is null)
        {
            match = default;
            return false;
        }

        var expectedCountOperation = expectedCountArgument.Value.UnwrapImplicitConversions();
        if (expectedCountOperation.ConstantValue is not { HasValue: true, Value: int expectedCount })
        {
            match = default;
            return false;
        }

        var assertionMethodName = (targetMethod.Name, expectedCount) switch
        {
            ("HasCount", 0) => EmptyAssertionMethodName,
            ("HasCountLessThan", 1) => EmptyAssertionMethodName,
            ("HasCountLessThanOrEqual", 0) => EmptyAssertionMethodName,
            ("DoesNotHaveCount", 0) => NotEmptyAssertionMethodName,
            ("HasCountGreaterThan", 0) => NotEmptyAssertionMethodName,
            ("HasCountGreaterThanOrEqual", 1) => NotEmptyAssertionMethodName,
            _ => null,
        };

        if (assertionMethodName is null)
        {
            match = default;
            return false;
        }

        match = new EmptinessMatch(expectedCountArgument, assertionMethodName);
        return true;
    }

    internal readonly record struct EmptinessMatch(IArgumentOperation ExpectedCountArgument, string AssertionMethodName);
}
