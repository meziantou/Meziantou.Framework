using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Formatting;
using Microsoft.CodeAnalysis.Text;

namespace Meziantou.Framework.Analyzers.Assertions;

[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(AwaitableDelegateCodeFixProvider))]
public sealed class AwaitableDelegateCodeFixProvider : CodeFixProvider
{
    public override ImmutableArray<string> FixableDiagnosticIds => [RuleIdentifiers.AwaitableDelegateDiagnosticId];

    public override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        if (root is null)
            return;

        var semanticModel = await context.Document.GetSemanticModelAsync(context.CancellationToken).ConfigureAwait(false);
        if (semanticModel is null || !AwaitableDelegateAnalyzerCommon.TryCreateSymbols(semanticModel.Compilation, out var symbols))
            return;

        foreach (var diagnostic in context.Diagnostics)
        {
            if (!TryCreateAsyncLambda(root, semanticModel, diagnostic.Location.SourceSpan, symbols.Value, out _, out _))
                continue;

            context.RegisterCodeFix(
                CodeAction.Create(
                    title: "Await the delegate in an async lambda",
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
        if (semanticModel is null || !AwaitableDelegateAnalyzerCommon.TryCreateSymbols(semanticModel.Compilation, out var symbols))
            return document;

        if (!TryCreateAsyncLambda(root, semanticModel, span, symbols.Value, out var delegateExpression, out var asyncLambda))
            return document;

        var annotation = new SyntaxAnnotation();
        var newRoot = root.ReplaceNode(delegateExpression, asyncLambda.WithTriviaFrom(delegateExpression).WithAdditionalAnnotations(annotation, Formatter.Annotation));

        // The assertion now returns a task: await it when its value is consumed in a way that awaiting preserves
        var invocation = newRoot.GetAnnotatedNodes(annotation).Single().Parent?.Parent?.Parent as InvocationExpressionSyntax;
        if (invocation?.Parent is ExpressionStatementSyntax or EqualsValueClauseSyntax or ArgumentSyntax &&
            AssertionCodeFixHelpers.CanCreateAwaitFix(invocation) &&
            AssertionCodeFixHelpers.TryCreateAwaitFix(newRoot, semanticModel, invocation, out var awaitedRoot))
        {
            newRoot = awaitedRoot;
        }

        return document.WithSyntaxRoot(newRoot.WithAdditionalAnnotations(Formatter.Annotation));
    }

    // Wraps the delegate as "async () => await expression", provided the assertion then binds to an overload that awaits it
    private static bool TryCreateAsyncLambda(SyntaxNode root, SemanticModel semanticModel, TextSpan span, AwaitableDelegateAnalyzerCommon.Symbols symbols, out ExpressionSyntax delegateExpression, out ParenthesizedLambdaExpressionSyntax asyncLambda)
    {
        delegateExpression = null!;
        asyncLambda = null!;
        if (root.FindNode(span, getInnermostNodeForTie: true) is not ExpressionSyntax expression)
            return false;

        if (expression.Parent is not ArgumentSyntax { Parent: ArgumentListSyntax { Parent: InvocationExpressionSyntax invocation } argumentList } argument)
            return false;

        ExpressionSyntax awaitedExpression;
        switch (expression)
        {
            // An async lambda converted to Action, or a block body, cannot be rewritten without changing its statements
            case AnonymousFunctionExpressionSyntax { AsyncKeyword.RawKind: (int)SyntaxKind.AsyncKeyword }:
                return false;

            case ParenthesizedLambdaExpressionSyntax { ParameterList.Parameters.Count: 0, ExpressionBody: { } body }:
                awaitedExpression = body.WithoutTrivia();
                break;

            case AnonymousFunctionExpressionSyntax:
                return false;

            default:
                awaitedExpression = SyntaxFactory.InvocationExpression(expression.WithoutTrivia());
                break;
        }

        if (!IsPrimaryExpression(awaitedExpression))
        {
            awaitedExpression = SyntaxFactory.ParenthesizedExpression(awaitedExpression);
        }

        var newLambda = SyntaxFactory.ParenthesizedLambdaExpression()
            .WithAsyncKeyword(SyntaxFactory.Token(SyntaxKind.AsyncKeyword))
            .WithExpressionBody(SyntaxFactory.AwaitExpression(awaitedExpression));

        var argumentIndex = argumentList.Arguments.IndexOf(argument);
        var newInvocation = invocation.ReplaceNode(expression, newLambda);
        if (semanticModel.GetSpeculativeSymbolInfo(invocation.SpanStart, newInvocation, SpeculativeBindingOption.BindAsExpression).Symbol is not IMethodSymbol method)
            return false;

        var parameter = argument.NameColon is { } nameColon
            ? method.Parameters.FirstOrDefault(parameter => parameter.Name == nameColon.Name.Identifier.ValueText)
            : argumentIndex < method.Parameters.Length ? method.Parameters[argumentIndex] : null;
        if (parameter is null || AwaitableDelegateAnalyzerCommon.IsNonAwaitingDelegateType(parameter.Type, symbols))
            return false;

        delegateExpression = expression;
        asyncLambda = newLambda;
        return true;
    }

    private static bool IsPrimaryExpression(ExpressionSyntax expression)
    {
        return expression is InvocationExpressionSyntax
            or MemberAccessExpressionSyntax
            or ConditionalAccessExpressionSyntax
            or ElementAccessExpressionSyntax
            or SimpleNameSyntax
            or ParenthesizedExpressionSyntax
            or BaseObjectCreationExpressionSyntax
            or LiteralExpressionSyntax
            or ThisExpressionSyntax;
    }
}
