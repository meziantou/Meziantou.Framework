using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Formatting;
using Microsoft.CodeAnalysis.Operations;

namespace Meziantou.Framework.Analyzers.Assertions;

[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(CountAssertionCodeFixProvider))]
public sealed class CountAssertionCodeFixProvider : CodeFixProvider
{
    public override ImmutableArray<string> FixableDiagnosticIds => [RuleIdentifiers.UseEmptyAssertionDiagnosticId, RuleIdentifiers.UseHasCountAssertionDiagnosticId];

    public override FixAllProvider GetFixAllProvider()
    {
        return WellKnownFixAllProviders.BatchFixer;
    }

    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        if (root is null)
            return;

        var semanticModel = await context.Document.GetSemanticModelAsync(context.CancellationToken).ConfigureAwait(false);
        if (semanticModel is null)
            return;

        var assertType = semanticModel.Compilation.GetTypeByMetadataName(AssertionsAnalyzerHelpers.AssertMetadataName);
        if (assertType is null || !CountAssertionAnalyzerCommon.TryCreateSymbols(semanticModel.Compilation, out var symbols))
            return;

        foreach (var diagnostic in context.Diagnostics)
        {
            var node = root.FindNode(diagnostic.Location.SourceSpan, getInnermostNodeForTie: true);
            if (!TryFindFixableInvocation(semanticModel, node, assertType, symbols, context.CancellationToken, out var invocationExpression, out var match))
                continue;

            context.RegisterCodeFix(
                CodeAction.Create(
                    title: match.UseEmptyAssertion ? "Use Assert.Empty" : "Use Assert.HasCount",
                    createChangedDocument: cancellationToken => ApplyFixAsync(context.Document, invocationExpression, assertType, symbols, cancellationToken),
                    equivalenceKey: GetType().FullName),
                diagnostic);
        }
    }

    private static async Task<Document> ApplyFixAsync(Document document, InvocationExpressionSyntax invocationExpression, INamedTypeSymbol assertType, CountAssertionAnalyzerCommon.Symbols symbols, CancellationToken cancellationToken)
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        if (root is null)
            return document;

        var semanticModel = await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
        if (semanticModel is null)
            return document;

        if (!TryGetAssertionMatch(semanticModel, invocationExpression, assertType, symbols, cancellationToken, out var match))
            return document;

        if (!TryGetCollectionExpression(match.CollectionOperation, out var collectionExpression))
            return document;

        var newInvocationExpression = invocationExpression
            .WithExpression(ReplaceMethodName(invocationExpression.Expression, match.UseEmptyAssertion ? "Empty" : "HasCount"))
            .WithArgumentList(CreateArgumentList(match, collectionExpression))
            .WithAdditionalAnnotations(Formatter.Annotation);
        var newRoot = root.ReplaceNode(invocationExpression, newInvocationExpression);
        return document.WithSyntaxRoot(newRoot);
    }

    private static bool TryGetAssertionMatch(
        SemanticModel semanticModel,
        InvocationExpressionSyntax invocationExpression,
        INamedTypeSymbol assertType,
        CountAssertionAnalyzerCommon.Symbols symbols,
        CancellationToken cancellationToken,
        out CountAssertionAnalyzerCommon.AssertionMatch match)
    {
        if (semanticModel.GetOperation(invocationExpression, cancellationToken) is IInvocationOperation invocationOperation &&
            CountAssertionAnalyzerCommon.TryGetAssertionMatch(invocationOperation, assertType, symbols, out match))
        {
            return true;
        }

        match = default;
        return false;
    }

    private static bool TryFindFixableInvocation(
        SemanticModel semanticModel,
        SyntaxNode diagnosticNode,
        INamedTypeSymbol assertType,
        CountAssertionAnalyzerCommon.Symbols symbols,
        CancellationToken cancellationToken,
        out InvocationExpressionSyntax invocationExpression,
        out CountAssertionAnalyzerCommon.AssertionMatch match)
    {
        foreach (var candidate in diagnosticNode.AncestorsAndSelf().OfType<InvocationExpressionSyntax>())
        {
            if (TryGetAssertionMatch(semanticModel, candidate, assertType, symbols, cancellationToken, out match))
            {
                invocationExpression = candidate;
                return true;
            }
        }

        invocationExpression = null!;
        match = default;
        return false;
    }

    private static bool TryGetCollectionExpression(IOperation operation, out ExpressionSyntax collectionExpression)
    {
        var syntax = operation.Syntax;
        if (syntax is ArgumentSyntax argumentSyntax)
        {
            collectionExpression = argumentSyntax.Expression;
            return true;
        }

        if (syntax is ExpressionSyntax expressionSyntax)
        {
            collectionExpression = expressionSyntax;
            return true;
        }

        collectionExpression = null!;
        return false;
    }

    private static ExpressionSyntax ReplaceMethodName(ExpressionSyntax expression, string methodName)
    {
        if (expression is MemberAccessExpressionSyntax memberAccessExpression)
        {
            return memberAccessExpression.WithName(SyntaxFactory.IdentifierName(CreateIdentifier(memberAccessExpression.Name.Identifier, methodName)));
        }

        if (expression is IdentifierNameSyntax identifierName)
            return identifierName.WithIdentifier(CreateIdentifier(identifierName.Identifier, methodName));

        return expression;
    }

    private static SyntaxToken CreateIdentifier(SyntaxToken identifier, string text)
    {
        return SyntaxFactory.Identifier(identifier.LeadingTrivia, text, identifier.TrailingTrivia);
    }

    private static ArgumentListSyntax CreateArgumentList(CountAssertionAnalyzerCommon.AssertionMatch match, ExpressionSyntax collectionExpression)
    {
        if (match.UseEmptyAssertion)
        {
            var actualArgument = (ArgumentSyntax)match.ActualArgument.Syntax;
            return SyntaxFactory.ArgumentList([SyntaxFactory.Argument(collectionExpression.WithoutTrivia()).WithTriviaFrom(actualArgument)]);
        }

        var expectedArgument = (ArgumentSyntax)match.ExpectedArgument.Syntax;
        var actualCountArgument = (ArgumentSyntax)match.ActualArgument.Syntax;
        return SyntaxFactory.ArgumentList(
        [
            SyntaxFactory.Argument(expectedArgument.Expression.WithoutTrivia()).WithTriviaFrom(expectedArgument),
            SyntaxFactory.Argument(collectionExpression.WithoutTrivia()).WithTriviaFrom(actualCountArgument),
        ]);
    }
}
