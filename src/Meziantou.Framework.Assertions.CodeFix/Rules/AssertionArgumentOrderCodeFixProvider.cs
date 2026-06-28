using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Formatting;
using Microsoft.CodeAnalysis.Operations;

namespace Meziantou.Framework.Analyzers.Assertions;

[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(AssertionArgumentOrderCodeFixProvider))]
public sealed class AssertionArgumentOrderCodeFixProvider : CodeFixProvider
{
    public override ImmutableArray<string> FixableDiagnosticIds => [RuleIdentifiers.AssertionArgumentOrderDiagnosticId];

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
        if (assertType is null)
            return;

        foreach (var diagnostic in context.Diagnostics)
        {
            var invocationExpression = root.FindNode(diagnostic.Location.SourceSpan, getInnermostNodeForTie: true).FirstAncestorOrSelf<InvocationExpressionSyntax>();
            if (invocationExpression is null)
                continue;

            if (!TryGetArgumentsToSwap(semanticModel, invocationExpression, assertType, context.CancellationToken, out _, out _))
                continue;

            context.RegisterCodeFix(
                CodeAction.Create(
                    title: "Swap expected and actual arguments",
                    createChangedDocument: cancellationToken => ApplyFixAsync(context.Document, invocationExpression, assertType, cancellationToken),
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

        if (!TryGetArgumentsToSwap(semanticModel, invocationExpression, assertType, cancellationToken, out var expectedArgument, out var actualArgument))
            return document;

        var expectedExpression = expectedArgument.Expression;
        var actualExpression = actualArgument.Expression;
        var newExpectedArgument = expectedArgument.WithExpression(actualExpression.WithTriviaFrom(expectedExpression));
        var newActualArgument = actualArgument.WithExpression(expectedExpression.WithTriviaFrom(actualExpression));
        var newRoot = root.ReplaceNodes(
            [expectedArgument, actualArgument],
            (node, _) => node == expectedArgument ? newExpectedArgument.WithAdditionalAnnotations(Formatter.Annotation) : newActualArgument.WithAdditionalAnnotations(Formatter.Annotation));

        return document.WithSyntaxRoot(newRoot);
    }

    private static bool TryGetArgumentsToSwap(
        SemanticModel semanticModel,
        InvocationExpressionSyntax invocationExpression,
        INamedTypeSymbol assertType,
        CancellationToken cancellationToken,
        out ArgumentSyntax expectedArgument,
        out ArgumentSyntax actualArgument)
    {
        if (semanticModel.GetOperation(invocationExpression, cancellationToken) is IInvocationOperation invocationOperation &&
            AssertionArgumentOrderAnalyzer.TryGetAssertionMatch(invocationOperation, assertType, out var match) &&
            AssertionArgumentOrderAnalyzer.IsConstantOrCollectionContainingConstant(match.ActualArgument.Value) &&
            !AssertionArgumentOrderAnalyzer.IsConstantOrCollectionContainingConstant(match.ExpectedArgument.Value) &&
            match.ExpectedArgument.Syntax is ArgumentSyntax expectedArgumentSyntax &&
            match.ActualArgument.Syntax is ArgumentSyntax actualArgumentSyntax)
        {
            expectedArgument = expectedArgumentSyntax;
            actualArgument = actualArgumentSyntax;
            return true;
        }

        expectedArgument = null!;
        actualArgument = null!;
        return false;
    }
}
