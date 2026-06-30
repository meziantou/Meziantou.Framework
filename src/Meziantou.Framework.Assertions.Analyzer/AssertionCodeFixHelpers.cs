using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace Meziantou.Framework.Analyzers.Assertions;

internal static class AssertionCodeFixHelpers
{
    internal static bool TryGetExpressionSyntax(IOperation operation, out ExpressionSyntax expression)
    {
        operation = AssertionsAnalyzerHelpers.UnwrapImplicitConversion(operation);
        if (operation.Syntax is ExpressionSyntax expressionSyntax)
        {
            expression = expressionSyntax;
            return true;
        }

        if (operation.Syntax is ArgumentSyntax argumentSyntax)
        {
            expression = argumentSyntax.Expression;
            return true;
        }

        expression = null!;
        return false;
    }

    internal static bool TryGetInvocationOperation(SemanticModel semanticModel, InvocationExpressionSyntax invocationExpression, CancellationToken cancellationToken, out IInvocationOperation invocationOperation)
    {
        if (semanticModel.GetOperation(invocationExpression, cancellationToken) is IInvocationOperation operation)
        {
            invocationOperation = operation;
            return true;
        }

        invocationOperation = null!;
        return false;
    }

    internal static ExpressionSyntax ReplaceMethodName(ExpressionSyntax expression, string methodName)
    {
        if (expression is MemberAccessExpressionSyntax memberAccessExpression)
            return memberAccessExpression.WithName(SyntaxFactory.IdentifierName(CreateIdentifier(memberAccessExpression.Name.Identifier, methodName)));

        if (expression is IdentifierNameSyntax identifierName)
            return identifierName.WithIdentifier(CreateIdentifier(identifierName.Identifier, methodName));

        return expression;
    }

    internal static ExpressionSyntax ReplaceMethodNameWithTypeArgument(ExpressionSyntax expression, string methodName, TypeSyntax typeArgument)
    {
        if (expression is MemberAccessExpressionSyntax memberAccessExpression)
            return memberAccessExpression.WithName(CreateGenericName(memberAccessExpression.Name.Identifier, methodName, typeArgument));

        if (expression is IdentifierNameSyntax identifierName)
            return CreateGenericName(identifierName.Identifier, methodName, typeArgument);

        return expression;
    }

    internal static SyntaxToken CreateIdentifier(SyntaxToken identifier, string text)
        => SyntaxFactory.Identifier(identifier.LeadingTrivia, text, identifier.TrailingTrivia);

    internal static GenericNameSyntax CreateGenericName(SyntaxToken identifier, string methodName, TypeSyntax typeArgument)
        => SyntaxFactory.GenericName(
            CreateIdentifier(identifier, methodName),
            SyntaxFactory.TypeArgumentList([typeArgument.WithoutTrivia()]));

    /// <summary>
    /// Finds the outer Assert.True/False invocation that wraps the node at <paramref name="diagnosticNode"/>.
    /// The diagnostic is reported on the inner invocation, so we skip one level.
    /// </summary>
    internal static InvocationExpressionSyntax? TryFindOuterAssertInvocation(SyntaxNode diagnosticNode)
        => diagnosticNode
            .AncestorsAndSelf()
            .OfType<InvocationExpressionSyntax>()
            .Skip(1)
            .FirstOrDefault();

    /// <summary>
    /// Builds a fixed invocation for TrueFalse condition fixes.
    /// </summary>
    internal static bool TryCreateTrueFalseConditionFix(
        InvocationExpressionSyntax outerInvocation,
        TrueFalseConditionMethodSelectionAnalyzerCommon.TrueFalseConditionMatch match,
        out InvocationExpressionSyntax fixedInvocation)
    {
        var arguments = new List<ArgumentSyntax>(match.Arguments.Length + 2);
        foreach (var argument in match.Arguments)
        {
            if (!TryGetExpressionSyntax(argument, out var argumentExpression))
            {
                fixedInvocation = null!;
                return false;
            }

            arguments.Add(SyntaxFactory.Argument(argumentExpression.WithoutTrivia()));
        }

        if (match.HasIgnoreCase)
        {
            var ignoreCaseExpression = match.IgnoreCaseValue == true
                ? SyntaxFactory.LiteralExpression(SyntaxKind.TrueLiteralExpression)
                : (ExpressionSyntax)SyntaxFactory.LiteralExpression(SyntaxKind.FalseLiteralExpression);

            arguments.Add(SyntaxFactory.Argument(ignoreCaseExpression));
        }

        if (TryGetMessageArgument(outerInvocation, out var messageArgument))
        {
            arguments.Add(SyntaxFactory.Argument(
                SyntaxFactory.NameColon(SyntaxFactory.IdentifierName("message")),
                refKindKeyword: default,
                messageArgument.Expression.WithoutTrivia()));
        }

        var argumentList = SyntaxFactory.ArgumentList(SyntaxFactory.SeparatedList(arguments));
        fixedInvocation = outerInvocation
            .WithExpression(ReplaceMethodName(outerInvocation.Expression, match.AssertionMethodName))
            .WithArgumentList(argumentList);
        return true;
    }

    private static bool TryGetMessageArgument(InvocationExpressionSyntax invocation, out ArgumentSyntax messageArgument)
    {
        var positionalArgumentIndex = 0;
        foreach (var argument in invocation.ArgumentList.Arguments)
        {
            if (argument.NameColon is { Name.Identifier.ValueText: "message" })
            {
                messageArgument = argument;
                return true;
            }

            if (argument.NameColon is not null)
                continue;

            if (positionalArgumentIndex == 1)
            {
                messageArgument = argument;
                return true;
            }

            positionalArgumentIndex++;
        }

        messageArgument = null!;
        return false;
    }
}
