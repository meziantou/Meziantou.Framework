#if !MEZIANTOU_FRAMEWORK_ROSLYN_ENABLE_WARNINGS
#pragma warning disable
#endif
#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace Meziantou.Framework.Roslyn;

#if !MEZIANTOU_FRAMEWORK_ROSLYN_DISABLE_EMBEDDEDATTRIBUTE
[Microsoft.CodeAnalysis.Embedded]
#endif
internal static partial class OperationExtensions
{
    public static IEnumerable<IOperation> Ancestors(this IOperation operation)
    {
        var parent = operation.Parent;
        while (parent is not null)
        {
            yield return parent;
            parent = parent.Parent;
        }
    }

    public static bool IsInNameofOperation(this IOperation operation)
    {
        var parent = operation.Parent;
        while (parent is not null)
        {
            if (parent.Kind == OperationKind.NameOf)
                return true;

            parent = parent.Parent;
        }

        return false;
    }

    public static IOperation UnwrapImplicitConversions(this IOperation operation, bool recursive = true)
    {
        if (operation is IConversionOperation conversionOperation && conversionOperation.IsImplicit)
        {
            if (recursive)
                return UnwrapImplicitConversions(conversionOperation.Operand, recursive);

            return conversionOperation.Operand;
        }

        return operation;
    }

    public static IOperation UnwrapConversions(this IOperation operation, bool recursive = true)
    {
        if (operation is IConversionOperation conversionOperation)
        {
            if (recursive)
                return UnwrapConversions(conversionOperation.Operand, recursive);

            return conversionOperation.Operand;
        }

        return operation;
    }

    public static IOperation? UnwrapLabels(this IOperation operation, bool recursive = true)
    {
        if (operation is ILabeledOperation label)
        {
            if (label.Operation is null)
                return null;

            return recursive ? UnwrapLabels(label.Operation, recursive) : label.Operation;
        }

        return operation;
    }

    /// <summary>
    /// Returns the arguments sorted by the ordinal of their parameter. <see cref="IInvocationOperation.Arguments"/> keeps the source order, so named
    /// arguments such as <c>string.Equals(comparisonType: StringComparison.Ordinal, a: x, b: y)</c> are not in the order of the parameters.
    /// The arguments are returned unchanged when they are already in order, or when one of them has no parameter.
    /// </summary>
    public static ImmutableArray<IArgumentOperation> InParameterOrder(this ImmutableArray<IArgumentOperation> arguments)
    {
        if (arguments.IsDefault)
            return arguments;

        var isOrdered = true;
        for (var i = 0; i < arguments.Length; i++)
        {
            var parameter = arguments[i].Parameter;
            if (parameter is null)
                return arguments;

            if (parameter.Ordinal != i)
            {
                isOrdered = false;
            }
        }

        if (isOrdered)
            return arguments;

        var result = new IArgumentOperation[arguments.Length];
        arguments.CopyTo(result);
        Array.Sort(result, (x, y) => x.Parameter!.Ordinal.CompareTo(y.Parameter!.Ordinal));

        return ImmutableArray.Create(result);
    }

    public static IMethodSymbol? GetContainingMethod(this IOperation operation, CancellationToken cancellationToken)
    {
        if (operation.SemanticModel is null)
            return null;

        foreach (var syntax in operation.Syntax.AncestorsAndSelf())
        {
            if (syntax is MethodDeclarationSyntax method)
                return operation.SemanticModel.GetDeclaredSymbol(method, cancellationToken) as IMethodSymbol;
        }

        return null;
    }
}
