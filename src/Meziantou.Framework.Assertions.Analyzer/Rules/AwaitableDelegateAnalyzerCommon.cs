using Meziantou.Framework.Roslyn;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace Meziantou.Framework.Analyzers.Assertions;

/// <summary>
/// Detects a delegate passed to an assertion overload that runs it synchronously (<see cref="System.Action"/> or
/// <c>Func&lt;object?&gt;</c>) while the delegate produces an awaitable, such as
/// <c>Assert.DoesNotThrow(() =&gt; GetValueTaskOfInt())</c>. The assertion never awaits the result, so it does not
/// observe the exceptions or the events that happen after the first await and silently passes.
/// </summary>
internal static class AwaitableDelegateAnalyzerCommon
{
    private const string GetAwaiterMethodName = "GetAwaiter";

    internal static bool TryCreateSymbols(Compilation compilation, [NotNullWhen(true)] out Symbols? symbols)
    {
        var actionType = compilation.GetTypeByMetadataName("System.Action");
        var funcType = compilation.GetTypeByMetadataName("System.Func`1");
        if (actionType is null || funcType is null)
        {
            symbols = null;
            return false;
        }

        symbols = new Symbols(actionType, funcType);
        return true;
    }

    internal static bool TryGetMatch(IArgumentOperation argumentOperation, INamedTypeSymbol assertType, Symbols symbols, out AwaitableDelegateMatch match)
    {
        match = default;
        if (argumentOperation.Parent is not IInvocationOperation { TargetMethod: { IsStatic: true } targetMethod } ||
            !SymbolEqualityComparer.Default.Equals(targetMethod.ContainingType, assertType))
        {
            return false;
        }

        if (argumentOperation.Parameter is null || !IsNonAwaitingDelegateType(argumentOperation.Parameter.Type, symbols))
            return false;

        if (argumentOperation.Value is not IDelegateCreationOperation delegateCreation || delegateCreation.SemanticModel is null)
            return false;

        var semanticModel = delegateCreation.SemanticModel;
        var position = delegateCreation.Syntax.SpanStart;
        switch (delegateCreation.Target)
        {
            case IAnonymousFunctionOperation anonymousFunction:
                if (anonymousFunction.Symbol.IsAsync)
                {
                    // An async lambda converted to Action is an async void lambda
                    if (!anonymousFunction.Symbol.ReturnsVoid)
                        return false;

                    match = new AwaitableDelegateMatch(delegateCreation, AwaitableType: null);
                    return true;
                }

                foreach (var resultOperation in GetResultOperations(anonymousFunction))
                {
                    if (IsDiscardAssignment(resultOperation))
                        continue;

                    var resultType = resultOperation.UnwrapImplicitConversions().Type;
                    if (IsAwaitable(resultType, semanticModel, position))
                    {
                        match = new AwaitableDelegateMatch(delegateCreation, resultType);
                        return true;
                    }
                }

                return false;

            case IMethodReferenceOperation methodReference when IsAwaitable(methodReference.Method.ReturnType, semanticModel, position):
                match = new AwaitableDelegateMatch(delegateCreation, methodReference.Method.ReturnType);
                return true;

            default:
                return false;
        }
    }

    internal static bool IsNonAwaitingDelegateType(ITypeSymbol? type, Symbols symbols)
    {
        if (SymbolEqualityComparer.Default.Equals(type, symbols.ActionType))
            return true;

        return type is INamedTypeSymbol { TypeArguments: [{ SpecialType: SpecialType.System_Object }] } namedType &&
               SymbolEqualityComparer.Default.Equals(namedType.OriginalDefinition, symbols.FuncType);
    }

    // The values the delegate produces: the body of an expression-bodied lambda, or the returned values of a block body.
    // A statement of a block body discards its value explicitly, so it is not a result.
    private static IEnumerable<IOperation> GetResultOperations(IAnonymousFunctionOperation anonymousFunction)
    {
        var operations = new Stack<IOperation>();
        operations.Push(anonymousFunction.Body);
        var isExpressionBodied = anonymousFunction.Body.IsImplicit;
        while (operations.Count > 0)
        {
            var operation = operations.Pop();
            switch (operation)
            {
                // Nested functions have their own results
                case IAnonymousFunctionOperation or ILocalFunctionOperation:
                    continue;

                case IReturnOperation { ReturnedValue: { } returnedValue }:
                    yield return returnedValue;
                    continue;

                case IExpressionStatementOperation expressionStatement when isExpressionBodied && expressionStatement.Parent == anonymousFunction.Body:
                    yield return expressionStatement.Operation;
                    continue;
            }

            foreach (var child in operation.ChildOperations)
            {
                operations.Push(child);
            }
        }
    }

    private static bool IsDiscardAssignment(IOperation operation)
    {
        return operation.UnwrapImplicitConversions() is ISimpleAssignmentOperation { Target: IDiscardOperation };
    }

    // An expression can be awaited when an accessible GetAwaiter method without parameters, including an extension method,
    // exists for its type. Task, ValueTask, ConfiguredTaskAwaitable and custom awaitables all follow this pattern.
    private static bool IsAwaitable(ITypeSymbol? type, SemanticModel semanticModel, int position)
    {
        if (type is null or IErrorTypeSymbol || type.TypeKind is TypeKind.Dynamic || type.SpecialType is SpecialType.System_Void)
            return false;

        foreach (var symbol in semanticModel.LookupSymbols(position, type, GetAwaiterMethodName, includeReducedExtensionMethods: true))
        {
            if (symbol is IMethodSymbol { Parameters.Length: 0, ReturnsVoid: false } method &&
                (!method.IsStatic || method.ReducedFrom is not null))
            {
                return true;
            }
        }

        return false;
    }

    internal readonly record struct Symbols(INamedTypeSymbol ActionType, INamedTypeSymbol FuncType);

    /// <param name="DelegateCreation">The lambda or method group converted to the non-awaiting delegate.</param>
    /// <param name="AwaitableType">The awaitable type produced by the delegate, or <see langword="null"/> for an async void lambda.</param>
    internal readonly record struct AwaitableDelegateMatch(IDelegateCreationOperation DelegateCreation, ITypeSymbol? AwaitableType);
}
