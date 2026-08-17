using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace Meziantou.Framework.Analyzers.FullPath;

internal sealed class FullPathContext(Compilation compilation)
{
    public INamedTypeSymbol? FullPathType { get; } = compilation.GetTypeByMetadataName("Meziantou.Framework.FullPath");
    public INamedTypeSymbol? PathType { get; } = compilation.GetTypeByMetadataName("System.IO.Path");
    public INamedTypeSymbol? DirectoryType { get; } = compilation.GetTypeByMetadataName("System.IO.Directory");
    public INamedTypeSymbol? FileSystemInfoType { get; } = compilation.GetTypeByMetadataName("System.IO.FileSystemInfo");
    public INamedTypeSymbol? EnvironmentType { get; } = compilation.GetTypeByMetadataName("System.Environment");

    public bool IsFullPathMember(IMethodSymbol methodSymbol)
    {
        return methodSymbol.IsStatic && SymbolEqualityComparer.Default.Equals(methodSymbol.ContainingType, FullPathType);
    }

    /// <summary>
    /// Returns the name of the type declaring <paramref name="methodSymbol"/> when <c>FullPath</c> exposes a factory
    /// method with the same name and the same parameters, or <see langword="null"/> when there is no equivalent.
    /// </summary>
    public string? GetFullPathFactoryEquivalentTypeName(IMethodSymbol methodSymbol)
    {
        if (!methodSymbol.IsStatic)
            return null;

        return methodSymbol switch
        {
            // Path.GetTempPath() => FullPath.GetTempPath()
            { Name: "GetTempPath", Parameters.IsEmpty: true } when SymbolEqualityComparer.Default.Equals(methodSymbol.ContainingType, PathType) => "Path",

            // Environment.GetFolderPath(SpecialFolder[, SpecialFolderOption]) => FullPath.GetFolderPath(SpecialFolder[, SpecialFolderOption])
            { Name: "GetFolderPath", Parameters.Length: 1 or 2 } when SymbolEqualityComparer.Default.Equals(methodSymbol.ContainingType, EnvironmentType) => "Environment",

            _ => null,
        };
    }

    public bool IsFileSystemInfo(ITypeSymbol? typeSymbol)
    {
        for (var current = typeSymbol; current is not null; current = current.BaseType)
        {
            if (SymbolEqualityComparer.Default.Equals(current, FileSystemInfoType))
                return true;
        }

        return false;
    }

    [MemberNotNullWhen(true, nameof(FullPathType))]
    public bool IsValid => FullPathType is not null;

    public bool IsFullPathType(ITypeSymbol? typeSymbol)
    {
        return SymbolEqualityComparer.Default.Equals(typeSymbol, FullPathType);
    }

    public bool IsFullPathType(IOperation operation)
    {
        return IsFullPathType(UnwrapToFullPath(operation).Type);
    }

    public IOperation UnwrapToFullPath(IOperation operation)
    {
        return FullPathAnalyzerCommon.UnwrapToFullPath(operation, FullPathType);
    }

    /// <summary>
    /// Walks <paramref name="operation"/> and reports whether it contains at least one <see langword="return"/> with a
    /// value, and whether every such value is a <c>FullPath</c>. Nested local functions are not walked.
    /// </summary>
    public void AnalyzeReturnOperations(IOperation operation, ref bool hasReturnValue, ref bool allReturnsAreFullPath)
    {
        if (!allReturnsAreFullPath)
            return;

        if (operation is ILocalFunctionOperation)
            return;

        if (operation is IReturnOperation returnOperation && returnOperation.ReturnedValue is not null)
        {
            hasReturnValue = true;
            if (!IsFullPathType(returnOperation.ReturnedValue))
            {
                allReturnsAreFullPath = false;
                return;
            }
        }

        foreach (var childOperation in operation.ChildOperations)
        {
            AnalyzeReturnOperations(childOperation, ref hasReturnValue, ref allReturnsAreFullPath);
        }
    }
}
