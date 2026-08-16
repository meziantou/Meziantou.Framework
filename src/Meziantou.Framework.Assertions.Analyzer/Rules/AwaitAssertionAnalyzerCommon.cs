using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace Meziantou.Framework.Analyzers.Assertions;

/// <summary>
/// Detects assertions whose result is a <see cref="System.Threading.Tasks.Task"/> that is never awaited, and
/// assertions comparing an un-awaited task with a plain value. Both make the assertion pass unconditionally.
/// </summary>
internal static class AwaitAssertionAnalyzerCommon
{
    internal static bool TryCreateSymbols(Compilation compilation, [NotNullWhen(true)] out Symbols? symbols)
    {
        var taskType = compilation.GetTypeByMetadataName("System.Threading.Tasks.Task");
        var genericTaskType = compilation.GetTypeByMetadataName("System.Threading.Tasks.Task`1");
        var valueTaskType = compilation.GetTypeByMetadataName("System.Threading.Tasks.ValueTask");
        var genericValueTaskType = compilation.GetTypeByMetadataName("System.Threading.Tasks.ValueTask`1");
        if (taskType is null || genericTaskType is null)
        {
            symbols = null;
            return false;
        }

        symbols = new Symbols(taskType, genericTaskType, valueTaskType, genericValueTaskType);
        return true;
    }

    /// <summary>
    /// Matches an assertion whose returned task is discarded, such as <c>Assert.ThrowsAsync&lt;T&gt;(...);</c>.
    /// </summary>
    internal static bool IsDiscardedTaskAssertion(IInvocationOperation invocationOperation, INamedTypeSymbol assertType, Symbols symbols)
    {
        if (invocationOperation.TargetMethod is not { IsStatic: true } targetMethod ||
            !SymbolEqualityComparer.Default.Equals(targetMethod.ContainingType, assertType))
        {
            return false;
        }

        if (!IsTaskLike(targetMethod.ReturnType, symbols))
            return false;

        // Anything else (await, return, assignment, argument) consumes the task
        return invocationOperation.Parent is IExpressionStatementOperation;
    }

    /// <summary>
    /// Matches a comparison assertion where exactly one of the compared values is an un-awaited task.
    /// </summary>
    internal static bool TryGetTaskArgumentMatch(IInvocationOperation invocationOperation, INamedTypeSymbol assertType, Symbols symbols, out IArgumentOperation taskArgument)
    {
        if (invocationOperation.TargetMethod is not { IsStatic: true } targetMethod ||
            !SymbolEqualityComparer.Default.Equals(targetMethod.ContainingType, assertType) ||
            targetMethod.Name is not ("Equal" or "NotEqual" or "Same" or "NotSame" or "Equivalent" or "NotEquivalent"))
        {
            taskArgument = null!;
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
            taskArgument = null!;
            return false;
        }

        var expectedIsTask = IsTaskLike(AssertionsAnalyzerHelpers.UnwrapImplicitConversion(expectedArgument.Value).Type, symbols);
        var actualIsTask = IsTaskLike(AssertionsAnalyzerHelpers.UnwrapImplicitConversion(actualArgument.Value).Type, symbols);

        // Comparing two tasks may be intentional; comparing a task with a plain value never succeeds
        if (expectedIsTask == actualIsTask)
        {
            taskArgument = null!;
            return false;
        }

        taskArgument = expectedIsTask ? expectedArgument : actualArgument;
        return true;
    }

    private static bool IsTaskLike(ITypeSymbol? type, Symbols symbols)
    {
        if (type is null)
            return false;

        var originalDefinition = type.OriginalDefinition;
        return SymbolEqualityComparer.Default.Equals(originalDefinition, symbols.TaskType) ||
               SymbolEqualityComparer.Default.Equals(originalDefinition, symbols.GenericTaskType) ||
               (symbols.ValueTaskType is not null && SymbolEqualityComparer.Default.Equals(originalDefinition, symbols.ValueTaskType)) ||
               (symbols.GenericValueTaskType is not null && SymbolEqualityComparer.Default.Equals(originalDefinition, symbols.GenericValueTaskType));
    }

    internal readonly record struct Symbols(
        INamedTypeSymbol TaskType,
        INamedTypeSymbol GenericTaskType,
        INamedTypeSymbol? ValueTaskType,
        INamedTypeSymbol? GenericValueTaskType);
}
