using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace Meziantou.Framework.Analyzers.FullPath;

internal static class FullPathAnalyzerCommon
{
    internal const string PathGetFullPathDiagnosticId = "MFFP0001";
    internal const string DivisionWithFullPathRightDiagnosticId = "MFFP0002";
    internal const string FullPathMetadataName = "Meziantou.Framework.FullPath";
    internal const string PathMetadataName = "System.IO.Path";

    internal static bool TryGetPathGetFullPathInvocationMatch(IInvocationOperation invocationOperation, INamedTypeSymbol pathType, INamedTypeSymbol fullPathType, out IOperation fullPathExpression)
    {
        if (invocationOperation.TargetMethod is not { IsStatic: true, Name: "GetFullPath" } targetMethod ||
            !SymbolEqualityComparer.Default.Equals(targetMethod.ContainingType, pathType) ||
            targetMethod.Parameters.Length != 1 ||
            invocationOperation.Arguments.Length != 1)
        {
            return TryReturnNoMatch(out fullPathExpression);
        }

        return TryExtractFullPathExpression(invocationOperation.Arguments[0].Value, fullPathType, out fullPathExpression);
    }

    internal static bool TryGetFullPathDivisionMatch(IBinaryOperation binaryOperation, INamedTypeSymbol fullPathType, out IOperation rightFullPathExpression)
    {
        if (binaryOperation.OperatorKind != BinaryOperatorKind.Divide)
            return TryReturnNoMatch(out rightFullPathExpression);

        if (!TryExtractFullPathExpression(binaryOperation.LeftOperand, fullPathType, out _))
            return TryReturnNoMatch(out rightFullPathExpression);

        return TryExtractFullPathExpression(binaryOperation.RightOperand, fullPathType, out rightFullPathExpression);
    }

    internal static bool TryExtractFullPathExpression(IOperation operation, INamedTypeSymbol fullPathType, out IOperation fullPathExpression)
    {
        operation = UnwrapParenthesizedOperation(operation);
        if (SymbolEqualityComparer.Default.Equals(operation.Type, fullPathType))
        {
            fullPathExpression = operation;
            return true;
        }

        if (operation is IConversionOperation conversionOperation)
            return TryExtractFullPathExpression(conversionOperation.Operand, fullPathType, out fullPathExpression);

        return TryReturnNoMatch(out fullPathExpression);
    }

    private static IOperation UnwrapParenthesizedOperation(IOperation operation)
    {
        while (operation is IParenthesizedOperation parenthesizedOperation)
        {
            operation = parenthesizedOperation.Operand;
        }

        return operation;
    }

    private static bool TryReturnNoMatch(out IOperation fullPathExpression)
    {
        fullPathExpression = null!;
        return false;
    }
}
