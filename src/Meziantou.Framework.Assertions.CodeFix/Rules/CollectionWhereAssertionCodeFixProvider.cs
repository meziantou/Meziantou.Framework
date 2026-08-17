using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Formatting;

namespace Meziantou.Framework.Analyzers.Assertions;

[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(CollectionWhereAssertionCodeFixProvider))]
public sealed class CollectionWhereAssertionCodeFixProvider : CodeFixProvider
{
    public override ImmutableArray<string> FixableDiagnosticIds =>
        [RuleIdentifiers.UseContainsForWhereDiagnosticId, RuleIdentifiers.UseDoesNotContainForWhereDiagnosticId, RuleIdentifiers.UseSingleWithPredicateDiagnosticId];

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
        if (assertType is null || !CollectionWhereAssertionAnalyzerCommon.TryCreateSymbols(semanticModel.Compilation, out var symbols))
            return;

        foreach (var diagnostic in context.Diagnostics)
        {
            var invocationExpression = root.FindNode(diagnostic.Location.SourceSpan, getInnermostNodeForTie: true)
                .FirstAncestorOrSelf<InvocationExpressionSyntax>();
            if (invocationExpression is null)
                continue;

            if (!AssertionCodeFixHelpers.TryGetInvocationOperation(semanticModel, invocationExpression, context.CancellationToken, out var invocationOperation))
                continue;

            if (!CollectionWhereAssertionAnalyzerCommon.TryGetAssertionMatch(invocationOperation, assertType, symbols.Value, out var match))
                continue;

            context.RegisterCodeFix(
                CodeAction.Create(
                    title: "Use Assert." + match.AssertionMethodName + " with a predicate",
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

        if (!CollectionWhereAssertionAnalyzerCommon.TryCreateSymbols(semanticModel.Compilation, out var symbols))
            return document;

        if (!AssertionCodeFixHelpers.TryGetInvocationOperation(semanticModel, invocationExpression, cancellationToken, out var invocationOperation))
            return document;

        if (!CollectionWhereAssertionAnalyzerCommon.TryGetAssertionMatch(invocationOperation, assertType, symbols.Value, out var match))
            return document;

        if (!AssertionCodeFixHelpers.TryGetExpressionSyntax(match.SourceOperation, out var sourceExpression) ||
            !AssertionCodeFixHelpers.TryGetExpressionSyntax(match.PredicateOperation, out var predicateExpression))
        {
            return document;
        }

        var arguments = new List<ArgumentSyntax>(invocationExpression.ArgumentList.Arguments.Count + 1);
        foreach (var argument in invocationExpression.ArgumentList.Arguments)
        {
            if (argument != match.ActualArgument.Syntax)
            {
                arguments.Add(argument);
                continue;
            }

            // The filtered sequence is replaced by the source, and the filter becomes the predicate of the assertion
            arguments.Add(argument.WithExpression(sourceExpression.WithoutTrivia()));

            var predicateArgument = SyntaxFactory.Argument(predicateExpression.WithoutTrivia());
            if (argument.NameColon is not null)
            {
                predicateArgument = predicateArgument.WithNameColon(SyntaxFactory.NameColon("predicate"));
            }

            arguments.Add(predicateArgument);
        }

        var newInvocationExpression = invocationExpression
            .WithExpression(AssertionCodeFixHelpers.ReplaceMethodName(invocationExpression.Expression, match.AssertionMethodName))
            .WithArgumentList(SyntaxFactory.ArgumentList(SyntaxFactory.SeparatedList(arguments)))
            .WithAdditionalAnnotations(Formatter.Annotation);

        var newRoot = root.ReplaceNode(invocationExpression, newInvocationExpression);
        return document.WithSyntaxRoot(newRoot);
    }
}
