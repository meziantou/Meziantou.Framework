using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace Meziantou.Framework.Analyzers.FullPath;

internal sealed class FullPathContext(Compilation compilation)
{
    public INamedTypeSymbol? FullPathType { get; } = compilation.GetTypeByMetadataName("Meziantou.Framework.FullPath");
    public INamedTypeSymbol? PathType { get; } = compilation.GetTypeByMetadataName("System.IO.Path");

    [MemberNotNullWhen(true, nameof(FullPathType))]
    public bool IsValid => FullPathType is not null;

    public bool IsFullPathType(ITypeSymbol? typeSymbol)
    {
        return SymbolEqualityComparer.Default.Equals(typeSymbol, FullPathType);
    }

    public bool IsFullPathType(IOperation operation)
    {
        return IsFullPathType(UnwrapImplicitConversion(operation).Type);
    }

    private static IOperation UnwrapImplicitConversion(IOperation operation)
    {
        while (operation is IConversionOperation conversionOperation && conversionOperation.IsImplicit)
        {
            operation = conversionOperation.Operand;
        }

        return operation;
    }
}