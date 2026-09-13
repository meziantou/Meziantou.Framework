using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace Meziantou.Framework.TaggedValues.Analyzer;

/// <summary>
/// Describes symbols and expressions in diagnostic messages, so the build output alone is enough to act on.
/// </summary>
internal static class ValueTagDescriptions
{
    private const int MaxExpressionLength = 50;

    public static string Describe(IOperation operation)
    {
        while (operation is IConversionOperation { IsImplicit: true } or IAwaitOperation)
        {
            operation = operation is IConversionOperation conversion ? conversion.Operand : ((IAwaitOperation)operation).Operation;
        }

        switch (operation)
        {
            case IFieldReferenceOperation fieldReference:
                return DescribeSymbol(fieldReference.Field);

            case IPropertyReferenceOperation propertyReference:
                return DescribeSymbol(propertyReference.Property);

            case IParameterReferenceOperation parameterReference:
                return DescribeSymbol(parameterReference.Parameter);

            case ILocalReferenceOperation localReference:
                return DescribeSymbol(localReference.Local);

            case IInvocationOperation invocation:
                return DescribeSymbol(invocation.TargetMethod);
        }

        var text = operation.Syntax.ToString();
        if (text.Length <= MaxExpressionLength && text.IndexOf("\n", StringComparison.Ordinal) < 0)
            return "'" + text + "'";

        return "the value";
    }

    public static string DescribeSymbol(ISymbol symbol, bool qualified = false)
    {
        return symbol switch
        {
            IFieldSymbol field => "field '" + GetContainingTypeName(field, qualified) + field.Name + "'",
            IPropertySymbol { IsIndexer: true } indexer => "indexer '" + GetContainingTypeName(indexer, qualified) + "this[]'",
            IPropertySymbol property => "property '" + GetContainingTypeName(property, qualified) + property.Name + "'",
            IParameterSymbol { ContainingSymbol: IMethodSymbol { MethodKind: MethodKind.AnonymousFunction } } parameter => "parameter '" + parameter.Name + "' of the lambda",
            IParameterSymbol { ContainingSymbol: IMethodSymbol method } parameter => "parameter '" + parameter.Name + "' of '" + GetMethodName(method) + "'",
            IParameterSymbol parameter => "parameter '" + parameter.Name + "'",
            ILocalSymbol local => "local '" + local.Name + "'",
            IMethodSymbol { MethodKind: MethodKind.AnonymousFunction } => "the return value of the lambda",
            IMethodSymbol method => "the return value of '" + GetMethodName(method) + "'",
            _ => "'" + symbol.Name + "'",
        };
    }

    /// <summary>
    /// Returns where the declaration of <paramref name="symbol"/> is, relative to the file the diagnostic is reported in.
    /// </summary>
    public static string GetSite(ISymbol symbol, SyntaxTree? reportTree)
    {
        foreach (var location in symbol.Locations)
        {
            if (!location.IsInSource || location.SourceTree is null)
                continue;

            var line = location.GetLineSpan().StartLinePosition.Line + 1;
            if (location.SourceTree == reportTree)
                return " (line " + line.ToString(System.Globalization.CultureInfo.InvariantCulture) + ")";

            return " (" + location.SourceTree.FilePath + ":" + line.ToString(System.Globalization.CultureInfo.InvariantCulture) + ")";
        }

        return "";
    }

    private static string GetContainingTypeName(ISymbol symbol, bool qualified)
    {
        if (symbol.ContainingType is null || symbol.ContainingType.IsAnonymousType)
            return "";

        return (qualified ? symbol.ContainingType.ToDisplayString() : symbol.ContainingType.Name) + ".";
    }

    private static string GetMethodName(IMethodSymbol method)
    {
        if (method.MethodKind is MethodKind.LocalFunction)
            return method.Name;

        var containingTypeName = method.ContainingType?.Name ?? "";
        return method.MethodKind switch
        {
            MethodKind.Constructor or MethodKind.StaticConstructor => containingTypeName + "." + containingTypeName,
            MethodKind.PropertyGet or MethodKind.PropertySet when method.AssociatedSymbol is not null => containingTypeName + "." + method.AssociatedSymbol.Name,
            _ => containingTypeName + "." + method.Name,
        };
    }
}
