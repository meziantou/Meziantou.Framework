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
    /// Matches an assertion whose returned task is discarded, such as <c>Assert.ThrowsAsync&lt;T&gt;(...);</c> or
    /// <c>Assert.ThrowsAsync&lt;T&gt;(...).ConfigureAwait(false);</c>.
    /// </summary>
    /// <param name="discardedOperation">
    /// The expression whose value is discarded: the assertion itself, or the outermost <c>ConfigureAwait</c> call
    /// applied to it. Awaiting this expression fixes the issue.
    /// </param>
    internal static bool IsDiscardedTaskAssertion(IInvocationOperation invocationOperation, INamedTypeSymbol assertType, Symbols symbols, [NotNullWhen(true)] out IOperation? discardedOperation)
    {
        discardedOperation = null;
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

        // ConfigureAwait only wraps the task in an awaitable, so discarding its result still discards the task
        IOperation operation = invocationOperation;
        while (operation.Parent is IInvocationOperation parentInvocation &&
               parentInvocation.Instance == operation &&
               IsTaskConfigureAwait(parentInvocation.TargetMethod, symbols))
        {
            operation = parentInvocation;
        }

        // Anything else (await, return, assignment, argument) consumes the task
        if (operation.Parent is not IExpressionStatementOperation)
            return false;

        discardedOperation = operation;
        return true;
    }

    private static bool IsTaskConfigureAwait(IMethodSymbol method, Symbols symbols)
        => method is { IsStatic: false, Name: "ConfigureAwait" } &&
           IsTaskLike(method.ContainingType, symbols);

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
