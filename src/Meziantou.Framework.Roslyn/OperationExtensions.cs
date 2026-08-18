using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace Meziantou.Framework.Roslyn;

internal static class OperationExtensions
{
    public static IOperation UnwrapImplicitConversion(this IOperation operation)
    {
        while (operation is IConversionOperation { IsImplicit: true } conversionOperation)
        {
            operation = conversionOperation.Operand;
        }

        return operation;
    }
}
