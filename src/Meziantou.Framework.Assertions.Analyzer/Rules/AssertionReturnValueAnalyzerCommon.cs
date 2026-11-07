using System.Collections.Immutable;
using Meziantou.Framework.Roslyn;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace Meziantou.Framework.Analyzers.Assertions;

/// <summary>
/// Detects assertions whose return value is discarded while a later statement re-derives the same value, such as
/// <c>Assert.Single(collection);</c> followed by <c>collection[0]</c>, or <c>Assert.IsType&lt;T&gt;(value);</c>
/// followed by <c>(T)value</c>.
/// </summary>
/// <remarks>
/// Only values that cannot change without a visible write are considered: locals, parameters, fields, properties and
/// constant indexers. The search stops at the first statement that assigns one of them, passes one of them by
/// reference, or calls an instance method on the asserted value, as the value may no longer be the same.
/// </remarks>
internal static class AssertionReturnValueAnalyzerCommon
{
    internal static bool TryGetMatch(IInvocationOperation invocationOperation, INamedTypeSymbol assertType, [NotNullWhen(true)] out AssertionReturnValueMatch? match)
    {
        match = null;
        if (invocationOperation.TargetMethod is not { IsStatic: true, ReturnsVoid: false } targetMethod ||
            !SymbolEqualityComparer.Default.Equals(targetMethod.ContainingType, assertType))
        {
            return false;
        }

        // The return value must be discarded, and the statement must be in a block so a local can replace it
        if (invocationOperation.Parent is not IExpressionStatementOperation { Parent: IBlockOperation block } statement)
            return false;

        if (!TryGetAssertionKind(targetMethod, out var kind))
            return false;

        if (!TryGetValueOperations(invocationOperation, kind, out var valueOperation, out var keyOperation))
            return false;

        var trackedSymbols = ImmutableHashSet.CreateBuilder<ISymbol>(SymbolEqualityComparer.Default);
        if (!IsTrackableValue(valueOperation, trackedSymbols, allowConstant: false))
            return false;

        if (keyOperation is not null && !IsTrackableValue(keyOperation, trackedSymbols, allowConstant: true))
            return false;

        var context = new AnalysisContext(kind, targetMethod, valueOperation, keyOperation, trackedSymbols.ToImmutable());
        var rederivations = ImmutableArray.CreateBuilder<IOperation>();
        var statementIndex = block.Operations.IndexOf(statement);
        for (var i = statementIndex + 1; i < block.Operations.Length; i++)
        {
            var nextStatement = block.Operations[i];

            // Once the value may have changed, later matches are no longer re-derivations
            if (MayChangeValue(nextStatement, context))
                break;

            CollectRederivations(nextStatement, context, rederivations);
        }

        if (rederivations.Count == 0)
            return false;

        match = new AssertionReturnValueMatch(statement, kind, rederivations.ToImmutable());
        return true;
    }

    private static bool TryGetAssertionKind(IMethodSymbol targetMethod, out AssertionKind kind)
    {
        var originalReturnType = targetMethod.OriginalDefinition.ReturnType;

        // Asynchronous overloads return a task, not the value itself
        var returnsValue = originalReturnType is ITypeParameterSymbol || originalReturnType.SpecialType is SpecialType.System_Char or SpecialType.System_Object;

        kind = targetMethod.Name switch
        {
            "Single" when returnsValue && !HasParameter(targetMethod, "predicate") => AssertionKind.Single,
            "IsType" or "IsAssignableTo" when targetMethod.TypeArguments.Length == 1 => AssertionKind.Type,
            "NotNull" when targetMethod.TypeArguments.Length == 1 => AssertionKind.NotNull,
            "Contains" when returnsValue && HasParameter(targetMethod, "expected") => AssertionKind.Contains,
            _ => AssertionKind.None,
        };

        return kind is not AssertionKind.None;
    }

    private static bool HasParameter(IMethodSymbol method, string name)
    {
        foreach (var parameter in method.Parameters)
        {
            if (parameter.Name == name)
                return true;
        }

        return false;
    }

    private static bool TryGetValueOperations(IInvocationOperation invocationOperation, AssertionKind kind, [NotNullWhen(true)] out IOperation? valueOperation, out IOperation? keyOperation)
    {
        valueOperation = null;
        keyOperation = null;
        foreach (var argument in invocationOperation.Arguments)
        {
            switch (argument.Parameter?.Name)
            {
                case "actual":
                    valueOperation = argument.Value.UnwrapImplicitConversions();
                    break;

                case "expected" when kind is AssertionKind.Contains:
                    keyOperation = argument.Value.UnwrapImplicitConversions();
                    break;

                // A custom comparer may not match the comparer used by the indexer of the dictionary
                case "comparer" when kind is AssertionKind.Contains && !argument.IsImplicit:
                    return false;
            }
        }

        if (kind is AssertionKind.Contains && keyOperation is null)
            return false;

        return valueOperation is not null;
    }

    private static bool IsTrackableValue(IOperation operation, ImmutableHashSet<ISymbol>.Builder trackedSymbols, bool allowConstant)
    {
        operation = operation.UnwrapImplicitConversions();
        switch (operation)
        {
            case ILocalReferenceOperation localReference:
                trackedSymbols.Add(localReference.Local);
                return true;

            case IParameterReferenceOperation parameterReference:
                trackedSymbols.Add(parameterReference.Parameter);
                return true;

            case IInstanceReferenceOperation:
                return true;

            case IFieldReferenceOperation fieldReference:
                trackedSymbols.Add(fieldReference.Field);
                return fieldReference.Instance is null || IsTrackableValue(fieldReference.Instance, trackedSymbols, allowConstant: false);

            case IPropertyReferenceOperation propertyReference:
                trackedSymbols.Add(propertyReference.Property);
                if (propertyReference.Instance is not null && !IsTrackableValue(propertyReference.Instance, trackedSymbols, allowConstant: false))
                    return false;

                foreach (var argument in propertyReference.Arguments)
                {
                    if (!IsTrackableValue(argument.Value, trackedSymbols, allowConstant: true))
                        return false;
                }

                return true;

            case IArrayElementReferenceOperation arrayElementReference:
                if (!IsTrackableValue(arrayElementReference.ArrayReference, trackedSymbols, allowConstant: false))
                    return false;

                foreach (var index in arrayElementReference.Indices)
                {
                    if (!IsTrackableValue(index, trackedSymbols, allowConstant: true))
                        return false;
                }

                return true;

            default:
                return allowConstant && operation.ConstantValue.HasValue;
        }
    }

    private static bool AreEquivalent(IOperation? left, IOperation? right)
    {
        if (left is null || right is null)
            return left is null && right is null;

        left = left.UnwrapImplicitConversions();
        right = right.UnwrapImplicitConversions();
        return (left, right) switch
        {
            (ILocalReferenceOperation l, ILocalReferenceOperation r) => SymbolEqualityComparer.Default.Equals(l.Local, r.Local),
            (IParameterReferenceOperation l, IParameterReferenceOperation r) => SymbolEqualityComparer.Default.Equals(l.Parameter, r.Parameter),
            (IInstanceReferenceOperation l, IInstanceReferenceOperation r) => l.ReferenceKind == r.ReferenceKind,
            (IFieldReferenceOperation l, IFieldReferenceOperation r) => SymbolEqualityComparer.Default.Equals(l.Field, r.Field) && AreEquivalent(l.Instance, r.Instance),
            (IPropertyReferenceOperation l, IPropertyReferenceOperation r) =>
                SymbolEqualityComparer.Default.Equals(l.Property, r.Property) &&
                AreEquivalent(l.Instance, r.Instance) &&
                AreEquivalent(l.Arguments.Select(argument => argument.Value), r.Arguments.Select(argument => argument.Value)),
            (IArrayElementReferenceOperation l, IArrayElementReferenceOperation r) =>
                AreEquivalent(l.ArrayReference, r.ArrayReference) &&
                AreEquivalent(l.Indices, r.Indices),
            _ => left.ConstantValue.HasValue && right.ConstantValue.HasValue &&
                 object.Equals(left.ConstantValue.Value, right.ConstantValue.Value) &&
                 SymbolEqualityComparer.Default.Equals(left.Type, right.Type),
        };
    }

    private static bool AreEquivalent(IEnumerable<IOperation> left, IEnumerable<IOperation> right)
    {
        using var leftEnumerator = left.GetEnumerator();
        using var rightEnumerator = right.GetEnumerator();
        while (true)
        {
            var hasLeft = leftEnumerator.MoveNext();
            var hasRight = rightEnumerator.MoveNext();
            if (!hasLeft || !hasRight)
                return hasLeft == hasRight;

            if (!AreEquivalent(leftEnumerator.Current, rightEnumerator.Current))
                return false;
        }
    }

    private static bool MayChangeValue(IOperation statement, AnalysisContext context)
    {
        // Writes inside lambdas and local functions are included, as they may run before the re-derivation
        foreach (var operation in DescendantsAndSelf(statement, includeFunctions: true))
        {
            var isWrite = operation switch
            {
                IAssignmentOperation assignment => IsWriteToTrackedValue(assignment.Target, context),
                IIncrementOrDecrementOperation incrementOrDecrement => IsWriteToTrackedValue(incrementOrDecrement.Target, context),
                IArgumentOperation { Parameter.RefKind: RefKind.Ref or RefKind.Out } argument => IsWriteToTrackedValue(argument.Value, context),
                IInvocationOperation { Instance: not null } invocation => IsMutatingInvocationOnValue(invocation, context),
                _ => false,
            };

            if (isWrite)
                return true;
        }

        return false;
    }

    private static bool IsWriteToTrackedValue(IOperation target, AnalysisContext context)
    {
        target = target.UnwrapImplicitConversions();
        switch (target)
        {
            case ILocalReferenceOperation localReference:
                return context.TrackedSymbols.Contains(localReference.Local);

            case IParameterReferenceOperation parameterReference:
                return context.TrackedSymbols.Contains(parameterReference.Parameter);

            case IFieldReferenceOperation fieldReference:
                return context.TrackedSymbols.Contains(fieldReference.Field);

            // Assigning an item of the asserted value, such as collection[0] = item, changes the re-derived value
            case IPropertyReferenceOperation propertyReference:
                return context.TrackedSymbols.Contains(propertyReference.Property) ||
                       (propertyReference.Property.IsIndexer && AreEquivalent(propertyReference.Instance, context.ValueOperation));

            case IArrayElementReferenceOperation arrayElementReference:
                return AreEquivalent(arrayElementReference.ArrayReference, context.ValueOperation);

            case ITupleOperation tuple:
                return tuple.Elements.Any(element => IsWriteToTrackedValue(element, context));

            case IDeclarationExpressionOperation declaration:
                return IsWriteToTrackedValue(declaration.Expression, context);

            default:
                return false;
        }
    }

    private static bool IsMutatingInvocationOnValue(IInvocationOperation invocation, AnalysisContext context)
    {
        if (!AreEquivalent(invocation.Instance, context.ValueOperation))
            return false;

        // Nullable<T>.GetValueOrDefault() is one of the re-derivations of Assert.NotNull
        if (IsRederivation(invocation, context))
            return false;

        return invocation.TargetMethod.Name is not (nameof(object.ToString) or nameof(object.GetHashCode) or nameof(object.Equals) or nameof(object.GetType)) &&
               !invocation.TargetMethod.IsReadOnly;
    }

    private static void CollectRederivations(IOperation statement, AnalysisContext context, ImmutableArray<IOperation>.Builder rederivations)
    {
        // Lambdas and local functions run at some other time, so they are not re-derivations
        foreach (var operation in DescendantsAndSelf(statement, includeFunctions: false))
        {
            if (IsRederivation(operation, context) && IsReadOfValue(operation))
            {
                // A re-derivation that contains another one, such as ((T)value).Value, is replaced as a whole
                if (!rederivations.Any(existing => existing.Syntax.Span.Contains(operation.Syntax.Span)))
                {
                    rederivations.Add(operation);
                }
            }
        }
    }

    private static bool IsRederivation(IOperation operation, AnalysisContext context)
    {
        if (operation.IsInNameofOperation())
            return false;

        var isRederivation = context.Kind switch
        {
            AssertionKind.Single => IsSingleItemRederivation(operation, context.ValueOperation),
            AssertionKind.Type => IsTypeRederivation(operation, context.ValueOperation),
            AssertionKind.NotNull => IsNotNullRederivation(operation, context.ValueOperation),
            AssertionKind.Contains => IsContainsRederivation(operation, context.ValueOperation, context.KeyOperation!),
            _ => false,
        };

        // The re-derived value must have the type returned by the assertion, so the local can replace it
        return isRederivation && SymbolEqualityComparer.Default.Equals(operation.Type, context.TargetMethod.ReturnType);
    }

    private static bool IsSingleItemRederivation(IOperation operation, IOperation valueOperation)
    {
        switch (operation)
        {
            case IInvocationOperation invocation when IsLinqMethod(invocation.TargetMethod):
                var method = invocation.TargetMethod.ReducedFrom ?? invocation.TargetMethod;
                var source = invocation.Instance ?? (invocation.Arguments.Length > 0 ? invocation.Arguments[0].Value : null);
                var otherArguments = invocation.Instance is null ? invocation.Arguments.Skip(1) : invocation.Arguments;
                if (!AreEquivalent(source, valueOperation))
                    return false;

                return method.Name switch
                {
                    "First" or "FirstOrDefault" or "Single" or "SingleOrDefault" or "Last" or "LastOrDefault" => method.Parameters.Length == 1,
                    "ElementAt" or "ElementAtOrDefault" => method.Parameters.Length == 2 && otherArguments.All(argument => IsZero(argument.Value)),
                    _ => false,
                };

            case IPropertyReferenceOperation { Property.IsIndexer: true, Arguments: [var argument] } propertyReference:
                return IsZero(argument.Value) && AreEquivalent(propertyReference.Instance, valueOperation);

            case IArrayElementReferenceOperation { Indices: [var index] } arrayElementReference:
                return IsZero(index) && AreEquivalent(arrayElementReference.ArrayReference, valueOperation);

            default:
                return false;
        }

        static bool IsLinqMethod(IMethodSymbol method)
        {
            method = method.ReducedFrom ?? method;
            return method.IsExtensionMethod && method.ContainingNamespace is { Name: "Linq", ContainingNamespace: { Name: "System", ContainingNamespace.IsGlobalNamespace: true } };
        }

        static bool IsZero(IOperation operation)
        {
            return operation.UnwrapImplicitConversions().ConstantValue is { HasValue: true, Value: 0 };
        }
    }

    private static bool IsTypeRederivation(IOperation operation, IOperation valueOperation)
    {
        return operation is IConversionOperation { IsImplicit: false, Conversion: { IsUserDefined: false, IsNumeric: false } } conversion &&
               AreEquivalent(conversion.Operand, valueOperation);
    }

    private static bool IsNotNullRederivation(IOperation operation, IOperation valueOperation)
    {
        return operation switch
        {
            IPropertyReferenceOperation { Property: { Name: "Value", ContainingType.OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } } propertyReference =>
                AreEquivalent(propertyReference.Instance, valueOperation),
            IInvocationOperation { TargetMethod: { Name: "GetValueOrDefault", Parameters.Length: 0, ContainingType.OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } } invocation =>
                AreEquivalent(invocation.Instance, valueOperation),
            _ => IsTypeRederivation(operation, valueOperation),
        };
    }

    private static bool IsContainsRederivation(IOperation operation, IOperation valueOperation, IOperation keyOperation)
    {
        return operation is IPropertyReferenceOperation { Property.IsIndexer: true, Arguments: [var argument] } propertyReference &&
               AreEquivalent(propertyReference.Instance, valueOperation) &&
               AreEquivalent(argument.Value, keyOperation);
    }

    /// <summary>
    /// Returns whether replacing the operation with a local copy of its value preserves the behavior. An array element
    /// of a value type is a variable: assigning one of its members or passing it by reference modifies the element.
    /// </summary>
    private static bool IsReadOfValue(IOperation operation)
    {
        if (operation.Syntax.Parent is RefExpressionSyntax)
            return false;

        if (operation.Type is not { IsValueType: true })
            return true;

        var current = operation;
        while (true)
        {
            var parent = current.Parent;
            switch (parent)
            {
                case IConversionOperation { IsImplicit: true }:
                case IParenthesizedOperation:
                    current = parent;
                    continue;

                case IMemberReferenceOperation memberReference when memberReference.Instance == current:
                    if (memberReference.Type is not { IsValueType: true })
                        return !IsWriteTarget(memberReference);

                    current = parent;
                    continue;

                case IInvocationOperation invocation when invocation.Instance == current:
                    return invocation.TargetMethod.IsReadOnly || current.Type is { IsReadOnly: true };

                case IArgumentOperation { Parameter.RefKind: not RefKind.None }:
                    return false;

                default:
                    return !IsWriteTarget(current);
            }
        }

        static bool IsWriteTarget(IOperation operation)
        {
            return operation.Parent switch
            {
                IAssignmentOperation assignment => assignment.Target == operation,
                IIncrementOrDecrementOperation incrementOrDecrement => incrementOrDecrement.Target == operation,
                _ => false,
            };
        }
    }

    private static IEnumerable<IOperation> DescendantsAndSelf(IOperation operation, bool includeFunctions)
    {
        var stack = new Stack<IOperation>();
        stack.Push(operation);
        while (stack.Count > 0)
        {
            var current = stack.Pop();
            yield return current;

            if (!includeFunctions && current is IAnonymousFunctionOperation or ILocalFunctionOperation)
                continue;

            foreach (var child in current.ChildOperations.Reverse())
            {
                stack.Push(child);
            }
        }
    }

    internal enum AssertionKind
    {
        None,
        Single,
        Type,
        NotNull,
        Contains,
    }

    private sealed record AnalysisContext(
        AssertionKind Kind,
        IMethodSymbol TargetMethod,
        IOperation ValueOperation,
        IOperation? KeyOperation,
        ImmutableHashSet<ISymbol> TrackedSymbols);

    internal sealed record AssertionReturnValueMatch(
        IExpressionStatementOperation AssertionStatement,
        AssertionKind Kind,
        ImmutableArray<IOperation> Rederivations);
}
