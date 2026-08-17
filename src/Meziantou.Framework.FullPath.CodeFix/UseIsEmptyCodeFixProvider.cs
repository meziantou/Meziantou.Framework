using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace Meziantou.Framework.Analyzers.FullPath;

[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(UseIsEmptyCodeFixProvider))]
public sealed class UseIsEmptyCodeFixProvider : FullPathCodeFixProviderBase
{
    public override ImmutableArray<string> FixableDiagnosticIds => [FullPathAnalyzerCommon.UseIsEmptyDiagnosticId];

    protected override string Title => "Use FullPath.IsEmpty";

    private protected override bool TryGetReplacementExpression(
        SemanticModel semanticModel,
        ExpressionSyntax expressionSyntax,
        FullPathContext analyzerContext,
        CancellationToken cancellationToken,
        out ExpressionSyntax replacementExpression)
    {
        replacementExpression = null!;

        switch (semanticModel.GetOperation(expressionSyntax, cancellationToken))
        {
            // string.IsNullOrEmpty(fullPath) / string.IsNullOrWhiteSpace(fullPath)
            case IInvocationOperation { Arguments.Length: 1 } invocationOperation:
                if (analyzerContext.UnwrapToFullPath(invocationOperation.Arguments[0].Value) is not { Syntax: ExpressionSyntax argumentExpression } argument ||
                    !analyzerContext.IsFullPathType(argument.Type))
                {
                    return false;
                }

                replacementExpression = CreateIsEmptyExpression(argumentExpression, negate: false);
                return true;

            case IBinaryOperation binaryOperation:
                var negate = binaryOperation.OperatorKind is BinaryOperatorKind.NotEquals;
                if (TryGetFullPathExpression(binaryOperation.LeftOperand, binaryOperation.RightOperand, analyzerContext, out var fullPathExpression) ||
                    TryGetFullPathExpression(binaryOperation.RightOperand, binaryOperation.LeftOperand, analyzerContext, out fullPathExpression))
                {
                    replacementExpression = CreateIsEmptyExpression(fullPathExpression, negate);
                    return true;
                }

                return false;

            default:
                return false;
        }
    }

    private static bool TryGetFullPathExpression(IOperation operand, IOperation otherOperand, FullPathContext analyzerContext, out ExpressionSyntax fullPathExpression)
    {
        fullPathExpression = null!;

        // fullPath == FullPath.Empty
        if (operand is IPropertyReferenceOperation { Property: { IsStatic: true, Name: "Empty" } property } &&
            analyzerContext.IsFullPathType(property.ContainingType))
        {
            if (analyzerContext.UnwrapToFullPath(otherOperand) is not { Syntax: ExpressionSyntax expression } unwrapped || !analyzerContext.IsFullPathType(unwrapped.Type))
                return false;

            fullPathExpression = expression;
            return true;
        }

        // fullPath.Value.Length == 0
        if (operand is ILiteralOperation { ConstantValue: { HasValue: true, Value: 0 } } &&
            otherOperand is IPropertyReferenceOperation { Property.Name: "Length", Instance: { } instance } &&
            analyzerContext.IsFullPathType(instance) &&
            analyzerContext.UnwrapToFullPath(instance).Syntax is ExpressionSyntax instanceExpression)
        {
            fullPathExpression = instanceExpression;
            return true;
        }

        return false;
    }
}
