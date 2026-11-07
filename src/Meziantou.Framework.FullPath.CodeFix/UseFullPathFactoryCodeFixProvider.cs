using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using Microsoft.CodeAnalysis.Simplification;

namespace Meziantou.Framework.Analyzers.FullPath;

[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(UseFullPathFactoryCodeFixProvider))]
public sealed class UseFullPathFactoryCodeFixProvider : FullPathCodeFixProviderBase
{
    public override ImmutableArray<string> FixableDiagnosticIds => [FullPathAnalyzerCommon.UseFullPathFactoryDiagnosticId];

    protected override string Title => "Use the FullPath equivalent";

    private protected override bool TryGetReplacementExpression(
        SemanticModel semanticModel,
        ExpressionSyntax expressionSyntax,
        FullPathContext analyzerContext,
        CancellationToken cancellationToken,
        out ExpressionSyntax replacementExpression)
    {
        if (semanticModel.GetOperation(expressionSyntax, cancellationToken) is IInvocationOperation { Syntax: InvocationExpressionSyntax invocationSyntax } invocationOperation &&
            analyzerContext.GetFullPathFactoryEquivalentTypeName(invocationOperation.TargetMethod) is not null &&
            analyzerContext.FullPathType is { } fullPathType)
        {
            // The name is simplified to 'FullPath' when the namespace is imported
            var fullPathTypeName = SyntaxFactory
                .ParseExpression(fullPathType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat))
                .WithAdditionalAnnotations(Simplifier.Annotation);

            replacementExpression = SyntaxFactory.InvocationExpression(
                SyntaxFactory.MemberAccessExpression(
                    SyntaxKind.SimpleMemberAccessExpression,
                    fullPathTypeName,
                    SyntaxFactory.IdentifierName(invocationOperation.TargetMethod.Name)),
                invocationSyntax.ArgumentList.WithoutTrivia());
            return true;
        }

        replacementExpression = null!;
        return false;
    }
}
