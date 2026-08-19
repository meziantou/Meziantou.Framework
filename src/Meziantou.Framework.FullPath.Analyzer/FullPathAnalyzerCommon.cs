using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;
using Meziantou.Framework.Roslyn;

namespace Meziantou.Framework.Analyzers.FullPath;

internal static class FullPathAnalyzerCommon
{
    internal const string PathGetFullPathDiagnosticId = "MFFP0001";
    internal const string DivisionWithFullPathRightDiagnosticId = "MFFP0002";
    internal const string PathGetFullPathWithFullPathBaseDiagnosticId = "MFFP0003";
    internal const string PathCombineWithFullPathDiagnosticId = "MFFP0004";
    internal const string PathGetFileNameWithFullPathDiagnosticId = "MFFP0005";
    internal const string PathGetFileNameWithoutExtensionWithFullPathDiagnosticId = "MFFP0006";
    internal const string PathGetExtensionWithFullPathDiagnosticId = "MFFP0007";
    internal const string PathGetDirectoryNameWithFullPathDiagnosticId = "MFFP0008";
    internal const string PathChangeExtensionWithFullPathDiagnosticId = "MFFP0009";
    internal const string PathGetRelativePathWithFullPathDiagnosticId = "MFFP0010";
    internal const string MethodShouldReturnFullPathDiagnosticId = "MFFP0011";
    internal const string PropertyShouldReturnFullPathDiagnosticId = "MFFP0012";
    internal const string VariableShouldBeFullPathDiagnosticId = "MFFP0013";
    internal const string ParameterShouldBeFullPathDiagnosticId = "MFFP0014";
    internal const string CompareFullPathAsStringDiagnosticId = "MFFP0015";
    internal const string FullPathEqualsStringDiagnosticId = "MFFP0016";
    internal const string RedundantPathPredicateDiagnosticId = "MFFP0018";
    internal const string RedundantFromPathDiagnosticId = "MFFP0019";
    internal const string FromPathWithFileSystemInfoDiagnosticId = "MFFP0020";
    internal const string UseIsEmptyDiagnosticId = "MFFP0021";
    internal const string UseCreateParentDirectoryDiagnosticId = "MFFP0022";
    internal const string DirectoryGetParentWithFullPathDiagnosticId = "MFFP0023";
    internal const string UseFullPathFactoryDiagnosticId = "MFFP0024";

    /// <summary>
    /// Returns the underlying <c>FullPath</c> operation of an expression that produces its string representation,
    /// or the operation itself when it is not such an expression.
    /// </summary>
    /// <remarks>
    /// Handles the implicit conversion to <see cref="string"/>, the explicit cast, the <c>Value</c> and <c>RawValue</c>
    /// properties, and <c>ToString()</c>, so that <c>fullPath</c>, <c>(string)fullPath</c>, <c>fullPath.Value</c>,
    /// <c>fullPath.RawValue</c>, and <c>fullPath.ToString()</c> are all recognized.
    /// </remarks>
    internal static IOperation UnwrapToFullPath(IOperation operation, ITypeSymbol? fullPathType)
    {
        while (true)
        {
            switch (operation)
            {
                case IConversionOperation conversionOperation when conversionOperation.IsImplicit || IsFullPathType(conversionOperation.Operand.Type, fullPathType):
                    operation = conversionOperation.Operand;
                    break;

                case IPropertyReferenceOperation { Instance: not null } propertyReferenceOperation
                    when propertyReferenceOperation.Property.Name is "Value" or "RawValue" && IsFullPathType(propertyReferenceOperation.Instance.Type, fullPathType):
                    operation = propertyReferenceOperation.Instance;
                    break;

                case IInvocationOperation { Instance: not null } invocationOperation
                    when invocationOperation.TargetMethod is { Name: "ToString", Parameters.IsEmpty: true } && IsFullPathType(invocationOperation.Instance.Type, fullPathType):
                    operation = invocationOperation.Instance;
                    break;

                default:
                    return operation;
            }
        }
    }

    private static bool IsFullPathType(ITypeSymbol? typeSymbol, ITypeSymbol? fullPathType)
    {
        return SymbolEqualityComparer.Default.Equals(typeSymbol, fullPathType);
    }

    internal static bool CanChangeDeclaredType(this ISymbol symbol)
    {
        if (symbol.IsOverride || symbol.IsVirtual || symbol.IsAbstract)
            return false;

        return !symbol.IsOverrideOrInterfaceImplementation();
    }
}
