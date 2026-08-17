using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Formatting;
using Microsoft.CodeAnalysis.Text;

namespace Meziantou.Framework.Analyzers.Assertions;

[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(AwaitAssertionCodeFixProvider))]
public sealed class AwaitAssertionCodeFixProvider : CodeFixProvider
{
    public override ImmutableArray<string> FixableDiagnosticIds => [RuleIdentifiers.AwaitAssertionDiagnosticId];

    public override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        if (root is null)
            return;

        foreach (var diagnostic in context.Diagnostics)
        {
            if (root.FindNode(diagnostic.Location.SourceSpan, getInnermostNodeForTie: true) is not ExpressionSyntax expression)
                continue;

            if (!AssertionCodeFixHelpers.CanCreateAwaitFix(expression))
                continue;

            context.RegisterCodeFix(
                CodeAction.Create(
                    title: "Await the assertion",
                    createChangedDocument: ct => ApplyFixAsync(context.Document, diagnostic.Location.SourceSpan, ct),
                    equivalenceKey: GetType().FullName),
                diagnostic);
        }
    }

    private static async Task<Document> ApplyFixAsync(Document document, TextSpan span, CancellationToken cancellationToken)
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        if (root is null)
            return document;

        var semanticModel = await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
        if (semanticModel is null)
            return document;

        if (root.FindNode(span, getInnermostNodeForTie: true) is not ExpressionSyntax expression)
            return document;

        if (!AssertionCodeFixHelpers.TryCreateAwaitFix(root, semanticModel, expression, out var newRoot))
            return document;

        return document.WithSyntaxRoot(newRoot.WithAdditionalAnnotations(Formatter.Annotation));
    }
}
