using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace Meziantou.Framework.Analyzers.FullPath;

[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(RedundantPathPredicateCodeFixProvider))]
public sealed class RedundantPathPredicateCodeFixProvider : FullPathCodeFixProviderBase
{
    public override ImmutableArray<string> FixableDiagnosticIds => [FullPathAnalyzerCommon.RedundantPathPredicateDiagnosticId];

    protected override string Title => "Use !FullPath.IsEmpty";

    private protected override bool TryGetReplacementExpression(
        SemanticModel semanticModel,
        ExpressionSyntax expressionSyntax,
        FullPathContext analyzerContext,
        CancellationToken cancellationToken,
        out ExpressionSyntax replacementExpression)
    {
        // Path.IsPathRooted and Path.IsPathFullyQualified are true for every FullPath except the empty one,
        // for which they are false. '!IsEmpty' is therefore an exact replacement.
        if (semanticModel.GetOperation(expressionSyntax, cancellationToken) is IInvocationOperation { Arguments.Length: 1 } invocationOperation &&
            invocationOperation.TargetMethod is { IsStatic: true, Name: "IsPathRooted" or "IsPathFullyQualified" } targetMethod &&
            SymbolEqualityComparer.Default.Equals(targetMethod.ContainingType, analyzerContext.PathType))
        {
            var fullPathOperation = analyzerContext.UnwrapToFullPath(invocationOperation.Arguments[0].Value);
            if (analyzerContext.IsFullPathType(fullPathOperation.Type) && fullPathOperation.Syntax is ExpressionSyntax fullPathExpression)
            {
                replacementExpression = CreateIsEmptyExpression(fullPathExpression, negate: true);
                return true;
            }
        }

        replacementExpression = null!;
        return false;
    }
}
