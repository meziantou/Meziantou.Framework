using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace Meziantou.Framework.Analyzers.FullPath;

[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(RedundantFromPathCodeFixProvider))]
public sealed class RedundantFromPathCodeFixProvider : FullPathCodeFixProviderBase
{
    public override ImmutableArray<string> FixableDiagnosticIds => [FullPathAnalyzerCommon.RedundantFromPathDiagnosticId];

    protected override string Title => "Simplify the FullPath.FromPath call";

    private protected override bool TryGetReplacementExpression(
        SemanticModel semanticModel,
        ExpressionSyntax expressionSyntax,
        FullPathContext analyzerContext,
        CancellationToken cancellationToken,
        out ExpressionSyntax replacementExpression)
    {
        replacementExpression = null!;

        if (semanticModel.GetOperation(expressionSyntax, cancellationToken) is not IInvocationOperation { Arguments.Length: 1 } invocationOperation)
            return false;

        if (invocationOperation.TargetMethod is not { Name: "FromPath" } targetMethod || !analyzerContext.IsFullPathMember(targetMethod))
            return false;

        var argument = invocationOperation.Arguments[0].Value;

        // FullPath.FromPath(fullPath) -> fullPath
        if (analyzerContext.IsFullPathType(argument))
        {
            if (analyzerContext.UnwrapToFullPath(argument).Syntax is not ExpressionSyntax fullPathExpression)
                return false;

            replacementExpression = fullPathExpression.WithoutTrivia();
            return true;
        }

        if (analyzerContext.UnwrapToFullPath(argument) is not IInvocationOperation { TargetMethod.IsStatic: true } innerInvocation ||
            !SymbolEqualityComparer.Default.Equals(innerInvocation.TargetMethod.ContainingType, analyzerContext.PathType))
        {
            return false;
        }

        if (innerInvocation.Syntax is not InvocationExpressionSyntax innerSyntax)
            return false;

        // FullPath.FromPath(Path.GetFullPath(path)) -> FullPath.FromPath(path)
        if (innerInvocation.TargetMethod.Name is "GetFullPath" && innerInvocation.Arguments.Length == 1)
        {
            if (innerInvocation.Arguments[0].Value.Syntax is not ExpressionSyntax innerArgument)
                return false;

            replacementExpression = ((InvocationExpressionSyntax)expressionSyntax)
                .WithArgumentList(SyntaxFactory.ArgumentList(SyntaxFactory.SingletonSeparatedList(SyntaxFactory.Argument(innerArgument.WithoutTrivia()))));
            return true;
        }

        // FullPath.FromPath(Path.Combine(path1, path2)) -> FullPath.Combine(path1, path2)
        if (innerInvocation.TargetMethod.Name is "Combine")
        {
            if (expressionSyntax is not InvocationExpressionSyntax { Expression: MemberAccessExpressionSyntax fromPathAccess })
                return false;

            replacementExpression = SyntaxFactory.InvocationExpression(
                fromPathAccess.WithName(SyntaxFactory.IdentifierName("Combine")).WithoutTrivia(),
                innerSyntax.ArgumentList.WithoutTrivia());
            return true;
        }

        return false;
    }
}
