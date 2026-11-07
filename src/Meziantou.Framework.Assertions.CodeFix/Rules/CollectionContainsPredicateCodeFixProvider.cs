using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Formatting;

namespace Meziantou.Framework.Analyzers.Assertions;

[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(CollectionContainsPredicateCodeFixProvider))]
public sealed class CollectionContainsPredicateCodeFixProvider : CodeFixProvider
{
    public override ImmutableArray<string> FixableDiagnosticIds =>
        [RuleIdentifiers.UseContainsWithExpectedValueDiagnosticId, RuleIdentifiers.UseDoesNotContainWithExpectedValueDiagnosticId];

    public override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        if (root is null)
            return;

        var semanticModel = await context.Document.GetSemanticModelAsync(context.CancellationToken).ConfigureAwait(false);
        if (semanticModel is null)
            return;

        var assertType = semanticModel.Compilation.GetTypeByMetadataName(AssertionsAnalyzerHelpers.AssertMetadataName);
        if (assertType is null)
            return;

        foreach (var diagnostic in context.Diagnostics)
        {
            var invocationExpression = root.FindNode(diagnostic.Location.SourceSpan, getInnermostNodeForTie: true)
                .FirstAncestorOrSelf<InvocationExpressionSyntax>();
            if (invocationExpression is null)
                continue;

            if (!AssertionCodeFixHelpers.TryGetInvocationOperation(semanticModel, invocationExpression, context.CancellationToken, out var invocationOperation))
                continue;

            if (!CollectionContainsPredicateAnalyzerCommon.TryGetAssertionMatch(invocationOperation, assertType, out var match))
                continue;

            context.RegisterCodeFix(
                CodeAction.Create(
                    title: "Use Assert." + match.AssertionMethodName + " with the expected value",
                    createChangedDocument: ct => ApplyFixAsync(context.Document, invocationExpression, assertType, ct),
                    equivalenceKey: GetType().FullName),
                diagnostic);
        }
    }

    private static async Task<Document> ApplyFixAsync(Document document, InvocationExpressionSyntax invocationExpression, INamedTypeSymbol assertType, CancellationToken cancellationToken)
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        if (root is null)
            return document;

        var semanticModel = await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
        if (semanticModel is null)
            return document;

        if (!AssertionCodeFixHelpers.TryGetInvocationOperation(semanticModel, invocationExpression, cancellationToken, out var invocationOperation))
            return document;

        if (!CollectionContainsPredicateAnalyzerCommon.TryGetAssertionMatch(invocationOperation, assertType, out var match))
            return document;

        if (!AssertionCodeFixHelpers.TryGetExpressionSyntax(match.ExpectedOperation, out var expectedExpression) ||
            !AssertionCodeFixHelpers.TryGetExpressionSyntax(match.ActualOperation, out var actualExpression))
        {
            return document;
        }

        var arguments = new List<ArgumentSyntax>(3)
        {
            SyntaxFactory.Argument(expectedExpression.WithoutTrivia()),
            SyntaxFactory.Argument(actualExpression.WithoutTrivia()),
        };

        // The overload takes an optional comparer, so the message cannot stay positional
        if (match.MessageArgument is not null &&
            AssertionCodeFixHelpers.TryGetExpressionSyntax(match.MessageArgument.Value, out var messageExpression))
        {
            arguments.Add(SyntaxFactory.Argument(
                SyntaxFactory.NameColon(SyntaxFactory.IdentifierName("message")),
                refKindKeyword: default,
                messageExpression.WithoutTrivia()));
        }

        var newInvocationExpression = invocationExpression
            .WithArgumentList(SyntaxFactory.ArgumentList(SyntaxFactory.SeparatedList(arguments)))
            .WithAdditionalAnnotations(Formatter.Annotation);

        var newRoot = root.ReplaceNode(invocationExpression, newInvocationExpression);
        return document.WithSyntaxRoot(newRoot);
    }
}
