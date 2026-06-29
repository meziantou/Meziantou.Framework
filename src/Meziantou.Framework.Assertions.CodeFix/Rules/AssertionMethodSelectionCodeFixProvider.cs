using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Formatting;
using Microsoft.CodeAnalysis.Operations;

namespace Meziantou.Framework.Analyzers.Assertions;

[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(AssertionMethodSelectionCodeFixProvider))]
public sealed class AssertionMethodSelectionCodeFixProvider : CodeFixProvider
{
    public override ImmutableArray<string> FixableDiagnosticIds =>
    [
        RuleIdentifiers.UseNullAssertionDiagnosticId,
        RuleIdentifiers.UseNotNullAssertionDiagnosticId,
        RuleIdentifiers.NullWithValueTypeDiagnosticId,
        RuleIdentifiers.NotNullWithValueTypeDiagnosticId,
        RuleIdentifiers.SameWithValueTypeDiagnosticId,
        RuleIdentifiers.NotSameWithValueTypeDiagnosticId,
        RuleIdentifiers.UseIsAssignableToDiagnosticId,
        RuleIdentifiers.UseIsNotAssignableToDiagnosticId,
    ];

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

            if (!TryGetCodeFixTitle(semanticModel, invocationExpression, assertType, context.CancellationToken, out var title))
                continue;

            context.RegisterCodeFix(
                CodeAction.Create(
                    title: title,
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

        if (!TryCreateFixedInvocation(semanticModel, invocationExpression, assertType, cancellationToken, out var fixedInvocation))
            return document;

        var newRoot = root.ReplaceNode(invocationExpression, fixedInvocation.WithAdditionalAnnotations(Formatter.Annotation));
        return document.WithSyntaxRoot(newRoot);
    }

    private static bool TryGetCodeFixTitle(SemanticModel semanticModel, InvocationExpressionSyntax invocationExpression, INamedTypeSymbol assertType, CancellationToken cancellationToken, out string title)
    {
        if (!TryGetInvocationOperation(semanticModel, invocationExpression, cancellationToken, out var invocationOperation))
        {
            title = null!;
            return false;
        }

        if (AssertionMethodSelectionAnalyzerCommon.TryGetNullCheckMatch(invocationOperation, assertType, out var nullCheckMatch) &&
            invocationExpression.ArgumentList.Arguments.Count == 1)
        {
            title = "Use Assert." + nullCheckMatch.AssertionMethodName;
            return true;
        }

        if (AssertionMethodSelectionAnalyzerCommon.TryGetNullNotNullValueTypeMatch(invocationOperation, assertType, out var nullNotNullValueTypeMatch) &&
            invocationExpression.ArgumentList.Arguments.Count == 1)
        {
            title = "Use Assert." + nullNotNullValueTypeMatch.AssertionMethodName;
            return true;
        }

        if (AssertionMethodSelectionAnalyzerCommon.TryGetSameNotSameValueTypeMatch(invocationOperation, assertType, out var sameNotSameValueTypeMatch) &&
            invocationExpression.ArgumentList.Arguments.Count == 2)
        {
            title = "Use Assert." + sameNotSameValueTypeMatch.AssertionMethodName;
            return true;
        }

        if (AssertionMethodSelectionAnalyzerCommon.TryGetAssignableTypeCheckMatch(invocationOperation, assertType, out var assignableTypeCheckMatch) &&
            invocationExpression.ArgumentList.Arguments.Count == 1)
        {
            title = "Use Assert." + assignableTypeCheckMatch.AssertionMethodName;
            return true;
        }

        title = null!;
        return false;
    }

    private static bool TryCreateFixedInvocation(SemanticModel semanticModel, InvocationExpressionSyntax invocationExpression, INamedTypeSymbol assertType, CancellationToken cancellationToken, out InvocationExpressionSyntax fixedInvocation)
    {
        if (!TryGetInvocationOperation(semanticModel, invocationExpression, cancellationToken, out var invocationOperation))
        {
            fixedInvocation = null!;
            return false;
        }

        if (AssertionMethodSelectionAnalyzerCommon.TryGetNullCheckMatch(invocationOperation, assertType, out var nullCheckMatch) &&
            invocationExpression.ArgumentList.Arguments.Count == 1 &&
            TryGetExpressionSyntax(nullCheckMatch.ActualOperation, out var actualExpression))
        {
            fixedInvocation = invocationExpression
                .WithExpression(ReplaceMethodName(invocationExpression.Expression, nullCheckMatch.AssertionMethodName))
                .WithArgumentList(SyntaxFactory.ArgumentList([SyntaxFactory.Argument(actualExpression.WithoutTrivia())]));
            return true;
        }

        if (AssertionMethodSelectionAnalyzerCommon.TryGetNullNotNullValueTypeMatch(invocationOperation, assertType, out var nullNotNullValueTypeMatch) &&
            invocationExpression.ArgumentList.Arguments.Count == 1 &&
            TryGetExpressionSyntax(nullNotNullValueTypeMatch.ActualOperation, out var actualValueExpression))
        {
            var typeName = nullNotNullValueTypeMatch.ValueType.ToMinimalDisplayString(semanticModel, invocationExpression.SpanStart);
            var defaultExpression = SyntaxFactory.DefaultExpression(SyntaxFactory.ParseTypeName(typeName));
            fixedInvocation = invocationExpression
                .WithExpression(ReplaceMethodName(invocationExpression.Expression, nullNotNullValueTypeMatch.AssertionMethodName))
                .WithArgumentList(
                    SyntaxFactory.ArgumentList(
                    [
                        SyntaxFactory.Argument(defaultExpression),
                        SyntaxFactory.Argument(actualValueExpression.WithoutTrivia()),
                    ]));
            return true;
        }

        if (AssertionMethodSelectionAnalyzerCommon.TryGetSameNotSameValueTypeMatch(invocationOperation, assertType, out var sameNotSameValueTypeMatch) &&
            invocationExpression.ArgumentList.Arguments.Count == 2)
        {
            fixedInvocation = invocationExpression.WithExpression(ReplaceMethodName(invocationExpression.Expression, sameNotSameValueTypeMatch.AssertionMethodName));
            return true;
        }

        if (AssertionMethodSelectionAnalyzerCommon.TryGetAssignableTypeCheckMatch(invocationOperation, assertType, out var assignableTypeCheckMatch) &&
            invocationExpression.ArgumentList.Arguments.Count == 1 &&
            TryGetExpressionSyntax(assignableTypeCheckMatch.ActualOperation, out var actualAssignableExpression))
        {
            fixedInvocation = invocationExpression
                .WithExpression(ReplaceMethodNameWithTypeArgument(invocationExpression.Expression, assignableTypeCheckMatch.AssertionMethodName, assignableTypeCheckMatch.TypeSyntax))
                .WithArgumentList(SyntaxFactory.ArgumentList([SyntaxFactory.Argument(actualAssignableExpression.WithoutTrivia())]));
            return true;
        }

        fixedInvocation = null!;
        return false;
    }

    private static bool TryGetInvocationOperation(SemanticModel semanticModel, InvocationExpressionSyntax invocationExpression, CancellationToken cancellationToken, out IInvocationOperation invocationOperation)
    {
        if (semanticModel.GetOperation(invocationExpression, cancellationToken) is IInvocationOperation operation)
        {
            invocationOperation = operation;
            return true;
        }

        invocationOperation = null!;
        return false;
    }

    private static bool TryGetExpressionSyntax(IOperation operation, out ExpressionSyntax expression)
    {
        operation = AssertionsAnalyzerHelpers.UnwrapImplicitConversion(operation);
        if (operation.Syntax is ExpressionSyntax expressionSyntax)
        {
            expression = expressionSyntax;
            return true;
        }

        if (operation.Syntax is ArgumentSyntax argumentSyntax)
        {
            expression = argumentSyntax.Expression;
            return true;
        }

        expression = null!;
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

    private static ExpressionSyntax ReplaceMethodNameWithTypeArgument(ExpressionSyntax expression, string methodName, TypeSyntax typeArgument)
    {
        if (expression is MemberAccessExpressionSyntax memberAccessExpression)
        {
            return memberAccessExpression.WithName(CreateGenericName(memberAccessExpression.Name.Identifier, methodName, typeArgument));
        }

        if (expression is IdentifierNameSyntax identifierName)
            return CreateGenericName(identifierName.Identifier, methodName, typeArgument);

        return expression;
    }

    private static SyntaxToken CreateIdentifier(SyntaxToken identifier, string text)
    {
        return SyntaxFactory.Identifier(identifier.LeadingTrivia, text, identifier.TrailingTrivia);
    }

    private static GenericNameSyntax CreateGenericName(SyntaxToken identifier, string methodName, TypeSyntax typeArgument)
    {
        return SyntaxFactory.GenericName(
            CreateIdentifier(identifier, methodName),
            SyntaxFactory.TypeArgumentList([typeArgument.WithoutTrivia()]));
    }
}
