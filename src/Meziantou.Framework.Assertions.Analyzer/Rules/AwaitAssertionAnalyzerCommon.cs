using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace Meziantou.Framework.Analyzers.Assertions;

/// <summary>
/// Detects assertions whose result is a <see cref="System.Threading.Tasks.Task"/> that is never awaited, which
/// makes the assertion pass unconditionally.
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

        // The original definition tells an assertion that completes asynchronously apart from one that merely
        // returns a value which happens to be a task: Assert.Single<T> returns T, so Assert.Single(taskArray)
        // returns a Task without the assertion itself being asynchronous.
        if (!IsTaskLike(targetMethod.OriginalDefinition.ReturnType, symbols))
            return false;

        // Anything else (await, return, assignment, argument) consumes the task
        return invocationOperation.Parent is IExpressionStatementOperation;
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
