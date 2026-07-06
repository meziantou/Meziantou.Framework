using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Formatting;

namespace Meziantou.Framework.Analyzers.Assertions;

[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(RegexMatchesCodeFixProvider))]
public sealed class RegexMatchesCodeFixProvider : CodeFixProvider
{
    public override ImmutableArray<string> FixableDiagnosticIds =>
        [RuleIdentifiers.UseMatchesDiagnosticId, RuleIdentifiers.UseDoesNotMatchDiagnosticId];

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
        if (assertType is null || !TrueFalseConditionMethodSelectionAnalyzerCommon.TryCreateSymbols(semanticModel.Compilation, out var symbols))
            return;

        foreach (var diagnostic in context.Diagnostics)
        {
            var innerNode = root.FindNode(diagnostic.Location.SourceSpan, getInnermostNodeForTie: true);
            var outerInvocation = AssertionCodeFixHelpers.TryFindOuterAssertInvocation(innerNode);
            if (outerInvocation is null)
                continue;

            if (!AssertionCodeFixHelpers.TryGetInvocationOperation(semanticModel, outerInvocation, context.CancellationToken, out var outerOp))
                continue;

            if (!TrueFalseConditionMethodSelectionAnalyzerCommon.TryGetAssertInnerInvocation(outerOp, assertType, out var innerInvocation, out var conditionExpectedToBeFalse))
                continue;

            if (!TrueFalseConditionMethodSelectionAnalyzerCommon.TryGetRegexIsMatchMatch(innerInvocation, symbols.Value, conditionExpectedToBeFalse, out var match))
                continue;

            context.RegisterCodeFix(CodeAction.Create(title: "Use Assert." + match.AssertionMethodName, createChangedDocument: ct => ApplyFixAsync(context.Document, outerInvocation, match, ct), equivalenceKey: GetType().FullName), diagnostic);
        }
    }

    private static async Task<Document> ApplyFixAsync(Document document, InvocationExpressionSyntax outerInvocation, TrueFalseConditionMethodSelectionAnalyzerCommon.TrueFalseConditionMatch match, CancellationToken cancellationToken)
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        if (root is null)
            return document;

        if (!AssertionCodeFixHelpers.TryCreateTrueFalseConditionFix(outerInvocation, match, out var fixedInvocation))
            return document;

        var newRoot = root.ReplaceNode(outerInvocation, fixedInvocation.WithAdditionalAnnotations(Formatter.Annotation));
        return document.WithSyntaxRoot(newRoot);
    }
}
