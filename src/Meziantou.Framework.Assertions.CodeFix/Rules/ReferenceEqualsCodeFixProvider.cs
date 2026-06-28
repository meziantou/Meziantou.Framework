using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Formatting;
using Microsoft.CodeAnalysis.Operations;

namespace Meziantou.Framework.Analyzers.Assertions;

[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(ReferenceEqualsCodeFixProvider))]
public sealed class ReferenceEqualsCodeFixProvider : CodeFixProvider
{
    public override ImmutableArray<string> FixableDiagnosticIds => [RuleIdentifiers.ReferenceEqualsDiagnosticId];

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

            if (!CanFix(semanticModel, invocationExpression, assertType, context.CancellationToken))
                continue;

            context.RegisterCodeFix(
                CodeAction.Create(
                    title: "Use Assert.Same",
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

        if (!CanFix(semanticModel, invocationExpression, assertType, cancellationToken))
            return document;

        var newInvocationExpression = invocationExpression
            .WithExpression(ReplaceReferenceEqualsName(invocationExpression.Expression))
            .WithArgumentList(RenameArguments(invocationExpression.ArgumentList))
            .WithAdditionalAnnotations(Formatter.Annotation);
        var newRoot = root.ReplaceNode(invocationExpression, newInvocationExpression);

        return document.WithSyntaxRoot(newRoot);
    }

    private static bool CanFix(SemanticModel semanticModel, InvocationExpressionSyntax invocationExpression, INamedTypeSymbol assertType, CancellationToken cancellationToken)
    {
        if (invocationExpression.Parent is not ExpressionStatementSyntax)
            return false;

        if (semanticModel.GetOperation(invocationExpression, cancellationToken) is not IInvocationOperation invocationOperation ||
            !AssertionsAnalyzerHelpers.IsAssertReferenceEqualsInvocation(invocationOperation, assertType))
        {
            return false;
        }

        return invocationExpression.Expression switch
        {
            MemberAccessExpressionSyntax { Name.Identifier.ValueText: "ReferenceEquals" } => true,
            IdentifierNameSyntax { Identifier.ValueText: "ReferenceEquals" } => true,
            _ => false,
        };
    }

    private static ExpressionSyntax ReplaceReferenceEqualsName(ExpressionSyntax expression)
    {
        if (expression is MemberAccessExpressionSyntax memberAccessExpression)
        {
            return memberAccessExpression.WithName(SyntaxFactory.IdentifierName(CreateIdentifier(memberAccessExpression.Name.Identifier, "Same")));
        }

        if (expression is IdentifierNameSyntax identifierName)
            return identifierName.WithIdentifier(CreateIdentifier(identifierName.Identifier, "Same"));

        return expression;
    }

    private static SyntaxToken CreateIdentifier(SyntaxToken identifier, string text)
    {
        return SyntaxFactory.Identifier(identifier.LeadingTrivia, text, identifier.TrailingTrivia);
    }

    private static ArgumentListSyntax RenameArguments(ArgumentListSyntax argumentList)
    {
        return argumentList.WithArguments(SyntaxFactory.SeparatedList(argumentList.Arguments.Select(RenameArgument), argumentList.Arguments.GetSeparators()));
    }

    private static ArgumentSyntax RenameArgument(ArgumentSyntax argument)
    {
        return argument.NameColon?.Name.Identifier.ValueText switch
        {
            "a" => argument.WithNameColon(SyntaxFactory.NameColon(SyntaxFactory.IdentifierName("expected")).WithTriviaFrom(argument.NameColon)),
            "b" => argument.WithNameColon(SyntaxFactory.NameColon(SyntaxFactory.IdentifierName("actual")).WithTriviaFrom(argument.NameColon)),
            _ => argument,
        };
    }
}
