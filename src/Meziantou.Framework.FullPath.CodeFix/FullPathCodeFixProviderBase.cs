using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Formatting;

namespace Meziantou.Framework.Analyzers.FullPath;

/// <summary>
/// Replaces the expression a diagnostic was reported on with the expression returned by
/// <see cref="TryGetReplacementExpression"/>.
/// </summary>
public abstract class FullPathCodeFixProviderBase : CodeFixProvider
{
    protected abstract string Title { get; }

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

        var analyzerContext = new FullPathContext(semanticModel.Compilation);
        if (!analyzerContext.IsValid)
            return;

        foreach (var diagnostic in context.Diagnostics)
        {
            var expression = root.FindNode(diagnostic.Location.SourceSpan, getInnermostNodeForTie: true).FirstAncestorOrSelf<ExpressionSyntax>();
            if (expression is null)
                continue;

            if (!TryGetReplacementExpression(semanticModel, expression, analyzerContext, context.CancellationToken, out _))
                continue;

            context.RegisterCodeFix(
                CodeAction.Create(
                    title: Title,
                    createChangedDocument: cancellationToken => ApplyFixAsync(context.Document, expression, cancellationToken),
                    equivalenceKey: GetType().FullName),
                diagnostic);
        }
    }

    private async Task<Document> ApplyFixAsync(Document document, ExpressionSyntax expressionSyntax, CancellationToken cancellationToken)
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        if (root is null)
            return document;

        var semanticModel = await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
        if (semanticModel is null)
            return document;

        var analyzerContext = new FullPathContext(semanticModel.Compilation);
        if (!analyzerContext.IsValid)
            return document;

        if (!TryGetReplacementExpression(semanticModel, expressionSyntax, analyzerContext, cancellationToken, out var replacementExpression))
            return document;

        replacementExpression = ParenthesizeForContext(replacementExpression, expressionSyntax)
            .WithTriviaFrom(expressionSyntax)
            .WithAdditionalAnnotations(Formatter.Annotation);
        var newRoot = root.ReplaceNode(expressionSyntax, replacementExpression);
        return document.WithSyntaxRoot(newRoot);
    }

    private protected abstract bool TryGetReplacementExpression(
        SemanticModel semanticModel,
        ExpressionSyntax expressionSyntax,
        FullPathContext analyzerContext,
        CancellationToken cancellationToken,
        out ExpressionSyntax replacementExpression);

    /// <summary>Builds <c>expression.IsEmpty</c> or <c>!expression.IsEmpty</c>.</summary>
    private protected static ExpressionSyntax CreateIsEmptyExpression(ExpressionSyntax fullPathExpression, bool negate)
    {
        ExpressionSyntax result = SyntaxFactory.MemberAccessExpression(
            SyntaxKind.SimpleMemberAccessExpression,
            fullPathExpression.WithoutTrivia().Parenthesize(),
            SyntaxFactory.IdentifierName("IsEmpty"));

        if (negate)
        {
            result = SyntaxFactory.PrefixUnaryExpression(SyntaxKind.LogicalNotExpression, result);
        }

        return result;
    }

    /// <summary>
    /// Parenthesizes the replacement when the syntax it replaces sits in a position that binds tighter than it,
    /// so that <c>!path.IsEmpty</c> substituted into <c>Path.IsPathRooted(p).ToString()</c> does not become
    /// <c>!p.IsEmpty.ToString()</c>.
    /// </summary>
    private static ExpressionSyntax ParenthesizeForContext(ExpressionSyntax replacement, ExpressionSyntax replaced)
    {
        var needsParentheses = replaced.Parent switch
        {
            MemberAccessExpressionSyntax memberAccess => memberAccess.Expression == replaced,
            ElementAccessExpressionSyntax elementAccess => elementAccess.Expression == replaced,
            ConditionalAccessExpressionSyntax conditionalAccess => conditionalAccess.Expression == replaced,
            PostfixUnaryExpressionSyntax => true,
            _ => false,
        };

        return needsParentheses ? replacement.Parenthesize() : replacement;
    }
}
