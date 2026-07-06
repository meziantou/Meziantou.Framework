using System;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Operations;

namespace Meziantou.Framework.Analyzers.Assertions;

internal static class AssertionsAnalyzerHelpers
{
    public const string AssertMetadataName = "Meziantou.Framework.Assertions.Assert";
    public const string CSharpUnionAttributeMetadataName = "System.Runtime.CompilerServices.UnionAttribute";
    public const string CSharpUnionInterfaceMetadataName = "System.Runtime.CompilerServices.IUnion";

    public static bool IsAssertReferenceEqualsInvocation(IInvocationOperation invocationOperation, INamedTypeSymbol assertType)
    {
        return invocationOperation.TargetMethod is { IsStatic: true, Name: "ReferenceEquals" } targetMethod &&
            SymbolEqualityComparer.Default.Equals(targetMethod.ContainingType, assertType);
    }

    public static bool IsAssertIsTypeInvocation(IInvocationOperation invocationOperation, INamedTypeSymbol assertType)
    {
        return invocationOperation.TargetMethod is { IsStatic: true, Name: "IsType" } targetMethod &&
            SymbolEqualityComparer.Default.Equals(targetMethod.ContainingType, assertType);
    }

    public static IOperation UnwrapImplicitConversion(IOperation operation)
    {
        while (operation is IConversionOperation { IsImplicit: true } conversionOperation)
        {
            operation = conversionOperation.Operand;
        }

        return operation;
    }

    public static bool IsValueType(ITypeSymbol? type)
    {
        return type?.IsValueType == true;
    }

    public static bool IsNonNullableValueType(ITypeSymbol? type, INamedTypeSymbol? unionAttributeType, INamedTypeSymbol? unionInterfaceType)
    {
        return type is { IsValueType: true } &&
               !IsNullableValueType(type) &&
               !IsCSharpUnionType(type, unionAttributeType, unionInterfaceType);
    }

    private static bool IsNullableValueType(ITypeSymbol type)
    {
        return type.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T;
    }

    private static bool IsCSharpUnionType(ITypeSymbol type, INamedTypeSymbol? unionAttributeType, INamedTypeSymbol? unionInterfaceType)
    {
        if (IsCSharpUnionDeclaration(type))
            return true;

        if (unionAttributeType is not null)
        {
            foreach (var attribute in type.GetAttributes())
            {
                if (SymbolEqualityComparer.Default.Equals(attribute.AttributeClass, unionAttributeType))
                    return true;
            }
        }

        return unionInterfaceType is not null &&
               type.AllInterfaces.Contains(unionInterfaceType, SymbolEqualityComparer.Default);
    }

    private static bool IsCSharpUnionDeclaration(ITypeSymbol type)
    {
        // When updating to a newer version of Roslyn, this check may need to be updated to use a different syntax kind.
        foreach (var syntaxReference in type.DeclaringSyntaxReferences)
        {
            if (syntaxReference.GetSyntax().IsKind((SyntaxKind)9082 /* UnionDeclaration */))
                return true;
        }

        return false;
    }
}
