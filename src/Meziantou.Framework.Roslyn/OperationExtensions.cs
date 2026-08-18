#nullable enable
using System.Collections.Generic;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace Meziantou.Framework.Roslyn;

internal static class OperationExtensions
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
