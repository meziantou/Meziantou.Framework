using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace Meziantou.Framework.Analyzers.FullPath;

[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(UseCreateParentDirectoryCodeFixProvider))]
public sealed class UseCreateParentDirectoryCodeFixProvider : FullPathCodeFixProviderBase
{
    public override ImmutableArray<string> FixableDiagnosticIds => [FullPathAnalyzerCommon.UseCreateParentDirectoryDiagnosticId];

    protected override string Title => "Use FullPath.CreateParentDirectory";

    private protected override bool TryGetReplacementExpression(
        SemanticModel semanticModel,
        ExpressionSyntax expressionSyntax,
        FullPathContext analyzerContext,
        CancellationToken cancellationToken,
        out ExpressionSyntax replacementExpression)
    {
        if (semanticModel.GetOperation(expressionSyntax, cancellationToken) is IInvocationOperation { Arguments.Length: 1 } invocationOperation &&
            invocationOperation.TargetMethod is { IsStatic: true, Name: "CreateDirectory" } targetMethod &&
            SymbolEqualityComparer.Default.Equals(targetMethod.ContainingType, analyzerContext.DirectoryType) &&
            analyzerContext.UnwrapToFullPath(invocationOperation.Arguments[0].Value) is IPropertyReferenceOperation { Property.Name: "Parent", Instance: { } instance } property &&
            analyzerContext.IsFullPathType(property.Property.ContainingType) &&
            analyzerContext.IsFullPathType(instance.Type) &&
            instance.Syntax is ExpressionSyntax instanceExpression)
        {
            replacementExpression = SyntaxFactory.InvocationExpression(
                SyntaxFactory.MemberAccessExpression(
                    SyntaxKind.SimpleMemberAccessExpression,
                    Parenthesize(instanceExpression.WithoutTrivia()),
                    SyntaxFactory.IdentifierName("CreateParentDirectory")));
            return true;
        }

        replacementExpression = null!;
        return false;
    }
}
