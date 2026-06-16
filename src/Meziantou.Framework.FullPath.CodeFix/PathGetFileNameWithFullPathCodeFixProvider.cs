using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Formatting;
using Microsoft.CodeAnalysis.Operations;

namespace Meziantou.Framework.Analyzers.FullPath;

[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(PathGetFileNameWithFullPathCodeFixProvider))]
public sealed class PathGetFileNameWithFullPathCodeFixProvider : CodeFixProvider
{
    public override ImmutableArray<string> FixableDiagnosticIds => [FullPathAnalyzerCommon.PathGetFileNameWithFullPathDiagnosticId];

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

        var pathType = semanticModel.Compilation.GetTypeByMetadataName("System.IO.Path");
        var fullPathType = semanticModel.Compilation.GetTypeByMetadataName("Meziantou.Framework.FullPath");
        if (pathType is null || fullPathType is null)
            return;

        foreach (var diagnostic in context.Diagnostics)
        {
            var expression = root.FindNode(diagnostic.Location.SourceSpan, getInnermostNodeForTie: true).FirstAncestorOrSelf<ExpressionSyntax>();
            if (expression is null)
                continue;

            if (!TryGetReplacementExpression(semanticModel, expression, pathType, fullPathType, context.CancellationToken, out _))
                continue;

            context.RegisterCodeFix(
                CodeAction.Create(
                    title: "Use FullPath.Name",
                    createChangedDocument: cancellationToken => ApplyFixAsync(context.Document, expression, pathType, fullPathType, cancellationToken),
                    equivalenceKey: GetType().FullName),
                diagnostic);
        }
    }

    private static async Task<Document> ApplyFixAsync(Document document, ExpressionSyntax expressionSyntax, INamedTypeSymbol pathType, INamedTypeSymbol fullPathType, CancellationToken cancellationToken)
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        if (root is null)
            return document;

        var semanticModel = await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
        if (semanticModel is null)
            return document;

        if (!TryGetReplacementExpression(semanticModel, expressionSyntax, pathType, fullPathType, cancellationToken, out var replacementExpression))
            return document;

        replacementExpression = replacementExpression.WithTriviaFrom(expressionSyntax).WithAdditionalAnnotations(Formatter.Annotation);
        var newRoot = root.ReplaceNode(expressionSyntax, replacementExpression);
        return document.WithSyntaxRoot(newRoot);
    }

    private static bool TryGetReplacementExpression(
        SemanticModel semanticModel,
        ExpressionSyntax expressionSyntax,
        INamedTypeSymbol pathType,
        INamedTypeSymbol fullPathType,
        CancellationToken cancellationToken,
        out ExpressionSyntax replacementExpression)
    {
        if (semanticModel.GetOperation(expressionSyntax, cancellationToken) is IInvocationOperation invocationOperation &&
            invocationOperation.TargetMethod is { IsStatic: true, Name: "GetFileName" } targetMethod &&
            SymbolEqualityComparer.Default.Equals(targetMethod.ContainingType, pathType) &&
            invocationOperation.Arguments.Length == 1)
        {
            var fullPathOperation = UnwrapImplicitConversion(invocationOperation.Arguments[0].Value);
            if (SymbolEqualityComparer.Default.Equals(fullPathOperation.Type, fullPathType) &&
                fullPathOperation.Syntax is ExpressionSyntax fullPathExpression)
            {
                replacementExpression = SyntaxFactory.MemberAccessExpression(
                    SyntaxKind.SimpleMemberAccessExpression,
                    fullPathExpression.WithoutTrivia(),
                    SyntaxFactory.IdentifierName("Name"));
                return true;
            }
        }

        replacementExpression = null!;
        return false;
    }

    private static IOperation UnwrapImplicitConversion(IOperation operation)
    {
        while (operation is IConversionOperation conversionOperation && conversionOperation.IsImplicit)
        {
            operation = conversionOperation.Operand;
        }

        return operation;
    }
}
