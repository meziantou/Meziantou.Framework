using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace Meziantou.Framework.Analyzers.FullPath;

[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(FromPathWithFileSystemInfoCodeFixProvider))]
public sealed class FromPathWithFileSystemInfoCodeFixProvider : FullPathCodeFixProviderBase
{
    public override ImmutableArray<string> FixableDiagnosticIds => [FullPathAnalyzerCommon.FromPathWithFileSystemInfoDiagnosticId];

    protected override string Title => "Use FullPath.FromFileSystemInfo";

    private protected override bool TryGetReplacementExpression(
        SemanticModel semanticModel,
        ExpressionSyntax expressionSyntax,
        FullPathContext analyzerContext,
        CancellationToken cancellationToken,
        out ExpressionSyntax replacementExpression)
    {
        if (semanticModel.GetOperation(expressionSyntax, cancellationToken) is IInvocationOperation { Arguments.Length: 1 } invocationOperation &&
            invocationOperation.TargetMethod is { Name: "FromPath" } targetMethod &&
            analyzerContext.IsFullPathMember(targetMethod) &&
            invocationOperation.Arguments[0].Value is IPropertyReferenceOperation { Property.Name: "FullName", Instance: { } instance } &&
            analyzerContext.IsFileSystemInfo(instance.Type) &&
            instance.Syntax is ExpressionSyntax instanceExpression &&
            expressionSyntax is InvocationExpressionSyntax { Expression: MemberAccessExpressionSyntax fromPathAccess })
        {
            replacementExpression = SyntaxFactory.InvocationExpression(
                fromPathAccess.WithName(SyntaxFactory.IdentifierName("FromFileSystemInfo")).WithoutTrivia(),
                SyntaxFactory.ArgumentList(SyntaxFactory.SingletonSeparatedList(SyntaxFactory.Argument(instanceExpression.WithoutTrivia()))));
            return true;
        }

        replacementExpression = null!;
        return false;
    }
}
