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
        RuleIdentifiers.UseMatchesDiagnosticId,
        RuleIdentifiers.UseDoesNotMatchDiagnosticId,
        RuleIdentifiers.UseStringContainsDiagnosticId,
        RuleIdentifiers.UseStringDoesNotContainDiagnosticId,
        RuleIdentifiers.UseStringStartsWithDiagnosticId,
        RuleIdentifiers.UseStringDoesNotStartWithDiagnosticId,
        RuleIdentifiers.UseStringEndsWithDiagnosticId,
        RuleIdentifiers.UseStringDoesNotEndWithDiagnosticId,
        RuleIdentifiers.UseCollectionContainsDiagnosticId,
        RuleIdentifiers.UseCollectionDoesNotContainDiagnosticId,
        RuleIdentifiers.UseCollectionAnyContainsDiagnosticId,
        RuleIdentifiers.UseCollectionAnyDoesNotContainDiagnosticId,
        RuleIdentifiers.UseCollectionAllDiagnosticId,
        RuleIdentifiers.UseCollectionDoesNotAllDiagnosticId,
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

        TrueFalseConditionMethodSelectionAnalyzerCommon.TryCreateSymbols(semanticModel.Compilation, out var symbols);

        foreach (var diagnostic in context.Diagnostics)
        {
            // For TrueFalseCondition rules, the diagnostic is on the inner invocation;
            // we need to find the outer Assert.True/False invocation to replace.
            InvocationExpressionSyntax? invocationExpression;
            if (IsTrueFalseConditionDiagnostic(diagnostic.Id))
            {
                invocationExpression = root.FindNode(diagnostic.Location.SourceSpan, getInnermostNodeForTie: true)
                    .AncestorsAndSelf()
                    .OfType<InvocationExpressionSyntax>()
                    .Skip(1) // skip the inner invocation, get the outer Assert.True/False
                    .FirstOrDefault();
            }
            else
            {
                invocationExpression = root.FindNode(diagnostic.Location.SourceSpan, getInnermostNodeForTie: true).FirstAncestorOrSelf<InvocationExpressionSyntax>();
            }

            if (invocationExpression is null)
                continue;

            if (!TryGetCodeFixTitle(semanticModel, invocationExpression, assertType, symbols, context.CancellationToken, out var title))
                continue;

            context.RegisterCodeFix(
                CodeAction.Create(
                    title: title,
                    createChangedDocument: cancellationToken => ApplyFixAsync(context.Document, invocationExpression, assertType, symbols, cancellationToken),
                    equivalenceKey: GetType().FullName),
                diagnostic);
        }
    }

    private static async Task<Document> ApplyFixAsync(Document document, InvocationExpressionSyntax invocationExpression, INamedTypeSymbol assertType, TrueFalseConditionMethodSelectionAnalyzerCommon.Symbols? symbols, CancellationToken cancellationToken)
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        if (root is null)
            return document;

        var semanticModel = await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
        if (semanticModel is null)
            return document;

        if (!TryCreateFixedInvocation(semanticModel, invocationExpression, assertType, symbols, cancellationToken, out var fixedInvocation))
            return document;

        var newRoot = root.ReplaceNode(invocationExpression, fixedInvocation.WithAdditionalAnnotations(Formatter.Annotation));
        return document.WithSyntaxRoot(newRoot);
    }

    private static bool TryGetCodeFixTitle(SemanticModel semanticModel, InvocationExpressionSyntax invocationExpression, INamedTypeSymbol assertType, TrueFalseConditionMethodSelectionAnalyzerCommon.Symbols? symbols, CancellationToken cancellationToken, out string title)
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

        if (symbols is not null &&
            TrueFalseConditionMethodSelectionAnalyzerCommon.TryGetMatch(invocationOperation, assertType, symbols.Value, out var trueFalseMatch))
        {
            title = "Use Assert." + trueFalseMatch.AssertionMethodName;
            return true;
        }

        title = null!;
        return false;
    }

    private static bool TryCreateFixedInvocation(SemanticModel semanticModel, InvocationExpressionSyntax invocationExpression, INamedTypeSymbol assertType, TrueFalseConditionMethodSelectionAnalyzerCommon.Symbols? symbols, CancellationToken cancellationToken, out InvocationExpressionSyntax fixedInvocation)
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
            var assignableTypeSyntax = SyntaxFactory.ParseTypeName(assignableTypeCheckMatch.Type.ToMinimalDisplayString(semanticModel, invocationExpression.SpanStart));
            fixedInvocation = invocationExpression
                .WithExpression(ReplaceMethodNameWithTypeArgument(invocationExpression.Expression, assignableTypeCheckMatch.AssertionMethodName, assignableTypeSyntax))
                .WithArgumentList(SyntaxFactory.ArgumentList([SyntaxFactory.Argument(actualAssignableExpression.WithoutTrivia())]));
            return true;
        }

        if (symbols is not null &&
            TrueFalseConditionMethodSelectionAnalyzerCommon.TryGetMatch(invocationOperation, assertType, symbols.Value, out var trueFalseMatch))
        {
            var argExpressions = new SyntaxNodeOrToken[trueFalseMatch.Arguments.Length * 2 - 1];
            for (var i = 0; i < trueFalseMatch.Arguments.Length; i++)
            {
                if (i > 0)
                    argExpressions[i * 2 - 1] = SyntaxFactory.Token(SyntaxKind.CommaToken).WithTrailingTrivia(SyntaxFactory.Space);

                if (!TryGetExpressionSyntax(trueFalseMatch.Arguments[i], out var argExpr))
                {
                    fixedInvocation = null!;
                    return false;
                }

                argExpressions[i * 2] = SyntaxFactory.Argument(argExpr.WithoutTrivia());
            }

            var argumentList = SyntaxFactory.ArgumentList(SyntaxFactory.SeparatedList<ArgumentSyntax>(argExpressions));

            if (trueFalseMatch.HasIgnoreCase)
            {
                var ignoreCaseExpr = trueFalseMatch.IgnoreCaseValue == true
                    ? SyntaxFactory.LiteralExpression(SyntaxKind.TrueLiteralExpression)
                    : (ExpressionSyntax)SyntaxFactory.LiteralExpression(SyntaxKind.FalseLiteralExpression);

                argumentList = argumentList.AddArguments(SyntaxFactory.Argument(ignoreCaseExpr));
            }

            fixedInvocation = invocationExpression
                .WithExpression(ReplaceMethodName(invocationExpression.Expression, trueFalseMatch.AssertionMethodName))
                .WithArgumentList(argumentList);
            return true;
        }

        fixedInvocation = null!;
        return false;
    }

    private static bool IsTrueFalseConditionDiagnostic(string diagnosticId)
    {
        return diagnosticId is
            RuleIdentifiers.UseMatchesDiagnosticId or
            RuleIdentifiers.UseDoesNotMatchDiagnosticId or
            RuleIdentifiers.UseStringContainsDiagnosticId or
            RuleIdentifiers.UseStringDoesNotContainDiagnosticId or
            RuleIdentifiers.UseStringStartsWithDiagnosticId or
            RuleIdentifiers.UseStringDoesNotStartWithDiagnosticId or
            RuleIdentifiers.UseStringEndsWithDiagnosticId or
            RuleIdentifiers.UseStringDoesNotEndWithDiagnosticId or
            RuleIdentifiers.UseCollectionContainsDiagnosticId or
            RuleIdentifiers.UseCollectionDoesNotContainDiagnosticId or
            RuleIdentifiers.UseCollectionAnyContainsDiagnosticId or
            RuleIdentifiers.UseCollectionAnyDoesNotContainDiagnosticId or
            RuleIdentifiers.UseCollectionAllDiagnosticId or
            RuleIdentifiers.UseCollectionDoesNotAllDiagnosticId;
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
