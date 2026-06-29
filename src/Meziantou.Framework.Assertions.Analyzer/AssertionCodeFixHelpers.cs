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
        var argExpressions = new SyntaxNodeOrToken[match.Arguments.Length * 2 - 1];
        for (var i = 0; i < match.Arguments.Length; i++)
        {
            if (i > 0)
                argExpressions[i * 2 - 1] = SyntaxFactory.Token(SyntaxKind.CommaToken).WithTrailingTrivia(SyntaxFactory.Space);

            if (!TryGetExpressionSyntax(match.Arguments[i], out var argExpr))
            {
                fixedInvocation = null!;
                return false;
            }

            argExpressions[i * 2] = SyntaxFactory.Argument(argExpr.WithoutTrivia());
        }

        var argumentList = SyntaxFactory.ArgumentList(SyntaxFactory.SeparatedList<ArgumentSyntax>(argExpressions));

        if (match.HasIgnoreCase)
        {
            var ignoreCaseExpr = match.IgnoreCaseValue == true
                ? SyntaxFactory.LiteralExpression(SyntaxKind.TrueLiteralExpression)
                : (ExpressionSyntax)SyntaxFactory.LiteralExpression(SyntaxKind.FalseLiteralExpression);

            argumentList = argumentList.AddArguments(SyntaxFactory.Argument(ignoreCaseExpr));
        }

        fixedInvocation = outerInvocation
            .WithExpression(ReplaceMethodName(outerInvocation.Expression, match.AssertionMethodName))
            .WithArgumentList(argumentList);
        return true;
    }
}
