using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;
using Meziantou.Framework.Roslyn;

namespace Meziantou.Framework.Analyzers.Assertions;

/// <summary>
/// Detects <c>Assert.True(false)</c> and <c>Assert.False(true)</c>, which are unconditional failures.
/// </summary>
internal static class UseFailAssertionAnalyzerCommon
{
    internal static bool TryGetAssertionMatch(IInvocationOperation invocationOperation, INamedTypeSymbol assertType, out FailMatch match)
    {
        if (invocationOperation.TargetMethod is not { IsStatic: true, Name: "True" or "False" } targetMethod ||
            !SymbolEqualityComparer.Default.Equals(targetMethod.ContainingType, assertType))
        {
            match = default;
            return false;
        }

        var conditionArgument = invocationOperation.Arguments.FirstOrDefault(argument => argument.Parameter?.Name == "condition");
        if (conditionArgument is null ||
            conditionArgument.Value.UnwrapImplicitConversions().ConstantValue is not { HasValue: true, Value: bool conditionValue })
        {
            match = default;
            return false;
        }

        if (targetMethod.Name == "True" == conditionValue)
        {
            match = default;
            return false;
        }

        // An omitted optional argument is still present in Arguments, and its syntax is the whole invocation
        var messageArgument = invocationOperation.Arguments.FirstOrDefault(argument =>
            argument is { Parameter.Name: "message", ArgumentKind: ArgumentKind.Explicit });
        match = new FailMatch(messageArgument);
        return true;
    }

    internal readonly record struct FailMatch(IArgumentOperation? MessageArgument);
}
