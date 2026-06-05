using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Formatting;
using Microsoft.CodeAnalysis.Operations;

namespace Meziantou.Framework.Analyzers.FullPath;

[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(FullPathCodeFixProvider))]
public sealed class FullPathCodeFixProvider : CodeFixProvider
{
    public override ImmutableArray<string> FixableDiagnosticIds => [FullPathAnalyzerCommon.PathGetFullPathDiagnosticId, FullPathAnalyzerCommon.DivisionWithFullPathRightDiagnosticId];

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

        var fullPathType = semanticModel.Compilation.GetTypeByMetadataName(FullPathAnalyzerCommon.FullPathMetadataName);
        if (fullPathType is null)
            return;

        var pathType = semanticModel.Compilation.GetTypeByMetadataName(FullPathAnalyzerCommon.PathMetadataName);
        if (pathType is null)
            return;

        foreach (var diagnostic in context.Diagnostics)
        {
            var expressionSyntax = root.FindNode(diagnostic.Location.SourceSpan, getInnermostNodeForTie: true).FirstAncestorOrSelf<ExpressionSyntax>();
            if (expressionSyntax is null || semanticModel.GetOperation(expressionSyntax, context.CancellationToken) is not IOperation operation)
                continue;

            if (!TryCreateReplacementExpression(operation, pathType, fullPathType, out _))
                continue;

            var title = diagnostic.Id switch
            {
                FullPathAnalyzerCommon.PathGetFullPathDiagnosticId => "Use FullPath value",
                FullPathAnalyzerCommon.DivisionWithFullPathRightDiagnosticId => "Use right FullPath value",
                _ => "Apply FullPath simplification",
            };

            context.RegisterCodeFix(
                CodeAction.Create(
                    title,
                    cancellationToken => ApplyFixAsync(context.Document, expressionSyntax, pathType, fullPathType, cancellationToken),
                    equivalenceKey: nameof(FullPathCodeFixProvider)),
                diagnostic);
        }
    }

    private static async Task<Document> ApplyFixAsync(Document document, ExpressionSyntax expressionSyntax, INamedTypeSymbol pathType, INamedTypeSymbol fullPathType, CancellationToken cancellationToken)
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        if (root is null)
            return document;

        var semanticModel = await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
        if (semanticModel is null || semanticModel.GetOperation(expressionSyntax, cancellationToken) is not IOperation operation)
            return document;

        if (!TryCreateReplacementExpression(operation, pathType, fullPathType, out var replacementExpression))
            return document;

        replacementExpression = replacementExpression.WithTriviaFrom(expressionSyntax).WithAdditionalAnnotations(Formatter.Annotation);
        var newRoot = root.ReplaceNode(expressionSyntax, replacementExpression);
        return document.WithSyntaxRoot(newRoot);
    }

    private static bool TryCreateReplacementExpression(IOperation operation, INamedTypeSymbol pathType, INamedTypeSymbol fullPathType, out ExpressionSyntax replacementExpression)
    {
        if (operation is IInvocationOperation invocationOperation &&
            FullPathAnalyzerCommon.TryGetPathGetFullPathInvocationMatch(invocationOperation, pathType, fullPathType, out var fullPathOperation) &&
            fullPathOperation.Syntax is ExpressionSyntax fullPathExpression)
        {
            replacementExpression = fullPathExpression;
            return true;
        }

        if (operation is IBinaryOperation binaryOperation &&
            FullPathAnalyzerCommon.TryGetFullPathDivisionMatch(binaryOperation, fullPathType, out var rightFullPathOperation) &&
            rightFullPathOperation.Syntax is ExpressionSyntax rightFullPathExpression)
        {
            replacementExpression = rightFullPathExpression;
            return true;
        }

        return TryReturnNoReplacement(out replacementExpression);
    }

    private static bool TryReturnNoReplacement(out ExpressionSyntax replacementExpression)
    {
        replacementExpression = null!;
        return false;
    }
}
