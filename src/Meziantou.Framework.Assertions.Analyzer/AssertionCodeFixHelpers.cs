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

    /// <summary>
    /// Finds the <c>Assert.True</c>/<c>Assert.False</c> invocation that encloses <paramref name="diagnosticNode"/>.
    /// The condition is not necessarily an invocation, so ancestors are matched semantically rather than by depth.
    /// </summary>
    internal static bool TryFindEnclosingAssertTrueFalseInvocation(
        SemanticModel semanticModel,
        SyntaxNode diagnosticNode,
        INamedTypeSymbol assertType,
        CancellationToken cancellationToken,
        out InvocationExpressionSyntax assertInvocation)
    {
        foreach (var candidate in diagnosticNode.AncestorsAndSelf().OfType<InvocationExpressionSyntax>())
        {
            if (TryGetInvocationOperation(semanticModel, candidate, cancellationToken, out var operation) &&
                operation.TargetMethod is { IsStatic: true, Name: "True" or "False" } targetMethod &&
                SymbolEqualityComparer.Default.Equals(targetMethod.ContainingType, assertType))
            {
                assertInvocation = candidate;
                return true;
            }
        }

        assertInvocation = null!;
        return false;
    }

    /// <summary>
    /// Builds the replacement for an <c>Assert.True</c>/<c>Assert.False</c> call rewritten into a dedicated assertion.
    /// </summary>
    internal static bool TryCreateConditionRewriteFix(
        SemanticModel semanticModel,
        InvocationExpressionSyntax assertInvocation,
        ConditionRewriteAnalyzerCommon.ConditionRewriteMatch match,
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

        if (match.IgnoreCaseValue == true)
        {
            arguments.Add(SyntaxFactory.Argument(
                SyntaxFactory.NameColon(SyntaxFactory.IdentifierName("ignoreCase")),
                refKindKeyword: default,
                SyntaxFactory.LiteralExpression(SyntaxKind.TrueLiteralExpression)));
        }

        if (TryGetMessageArgument(assertInvocation, out var messageArgument))
        {
            arguments.Add(SyntaxFactory.Argument(
                SyntaxFactory.NameColon(SyntaxFactory.IdentifierName("message")),
                refKindKeyword: default,
                messageArgument.Expression.WithoutTrivia()));
        }

        var expression = match.TypeArgument is null
            ? ReplaceMethodName(assertInvocation.Expression, match.AssertionMethodName)
            : ReplaceMethodNameWithTypeArgument(
                assertInvocation.Expression,
                match.AssertionMethodName,
                SyntaxFactory.ParseTypeName(match.TypeArgument.ToMinimalDisplayString(semanticModel, assertInvocation.SpanStart)));

        fixedInvocation = assertInvocation
            .WithExpression(expression)
            .WithArgumentList(SyntaxFactory.ArgumentList(SyntaxFactory.SeparatedList(arguments)));
        return true;
    }

    /// <summary>
    /// Awaits <paramref name="expression"/>, turning the enclosing method into <c>async Task</c> when needed.
    /// </summary>
    internal static bool TryCreateAwaitFix(SyntaxNode root, SemanticModel semanticModel, ExpressionSyntax expression, out SyntaxNode newRoot)
    {
        var awaitExpression = SyntaxFactory
            .AwaitExpression(expression.WithoutLeadingTrivia())
            .WithLeadingTrivia(expression.GetLeadingTrivia());

        var enclosingMethod = expression.FirstAncestorOrSelf<MethodDeclarationSyntax>();
        if (enclosingMethod is null)
        {
            newRoot = null!;
            return false;
        }

        if (enclosingMethod.Modifiers.Any(SyntaxKind.AsyncKeyword))
        {
            newRoot = root.ReplaceNode(expression, awaitExpression);
            return true;
        }

        // Only a void-returning method can be turned into an async method without changing its contract
        if (enclosingMethod.ReturnType is not PredefinedTypeSyntax { Keyword.RawKind: (int)SyntaxKind.VoidKeyword })
        {
            newRoot = null!;
            return false;
        }

        var taskType = semanticModel.Compilation.GetTypeByMetadataName("System.Threading.Tasks.Task");
        if (taskType is null)
        {
            newRoot = null!;
            return false;
        }

        var taskTypeSyntax = SyntaxFactory
            .ParseTypeName(taskType.ToMinimalDisplayString(semanticModel, enclosingMethod.ReturnType.SpanStart))
            .WithTriviaFrom(enclosingMethod.ReturnType);

        var newMethod = enclosingMethod
            .ReplaceNode(expression, awaitExpression)
            .WithReturnType(taskTypeSyntax)
            .AddModifiers(SyntaxFactory.Token(SyntaxKind.AsyncKeyword).WithTrailingTrivia(SyntaxFactory.Space));

        newRoot = root.ReplaceNode(enclosingMethod, newMethod);
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
