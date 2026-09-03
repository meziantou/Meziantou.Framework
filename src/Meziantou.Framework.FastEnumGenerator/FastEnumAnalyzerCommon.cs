using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Operations;

namespace Meziantou.Framework.FastEnumGenerator;

internal static class FastEnumAnalyzerCommon
{
    internal const string FastEnumAttributeMetadataName = "Meziantou.Framework.Annotations.FastEnumAttribute";

    /// <summary>
    /// C# 14, the first version supporting extension members. Hardcoded because the referenced
    /// Microsoft.CodeAnalysis version may not declare the enum member yet.
    /// </summary>
    internal const LanguageVersion CSharp14 = (LanguageVersion)1400;

    internal static ImmutableHashSet<INamedTypeSymbol> GetFastEnumTypes(Compilation compilation, INamedTypeSymbol fastEnumAttribute)
    {
        var result = ImmutableHashSet.CreateBuilder<INamedTypeSymbol>(SymbolEqualityComparer.Default);
        foreach (var attribute in compilation.Assembly.GetAttributes())
        {
            if (!SymbolEqualityComparer.Default.Equals(attribute.AttributeClass, fastEnumAttribute))
                continue;

            if (attribute.ConstructorArguments.Length != 1 || attribute.ConstructorArguments[0].Value is not INamedTypeSymbol enumType)
                continue;

            if (!IsSupportedEnumType(enumType))
                continue;

            _ = result.Add(enumType);
        }

        return result.ToImmutable();
    }

    /// <summary>
    /// Resolves the namespace the generated class is emitted into, so a code fix can add the matching
    /// using directive. It is <c>ExtensionMethodNamespace</c> when set, and the enum's namespace otherwise.
    /// </summary>
    internal static string? GetGeneratedNamespace(Compilation compilation, INamedTypeSymbol fastEnumAttribute, INamedTypeSymbol enumType)
    {
        foreach (var attribute in compilation.Assembly.GetAttributes())
        {
            if (!SymbolEqualityComparer.Default.Equals(attribute.AttributeClass, fastEnumAttribute))
                continue;

            if (attribute.ConstructorArguments.Length != 1 || !SymbolEqualityComparer.Default.Equals(attribute.ConstructorArguments[0].Value as INamedTypeSymbol, enumType))
                continue;

            foreach (var namedArgument in attribute.NamedArguments)
            {
                if (namedArgument is { Key: "ExtensionMethodNamespace", Value.Value: string value } && !string.IsNullOrEmpty(value))
                    return value;
            }
        }

        return enumType.ContainingNamespace is { IsGlobalNamespace: false } containingNamespace
            ? containingNamespace.ToDisplayString()
            : null;
    }

    /// <summary>
    /// The generator skips enums it cannot emit code for. The analyzer must apply the same rule,
    /// otherwise it suggests members that were never generated.
    /// </summary>
    internal static bool IsSupportedEnumType(INamedTypeSymbol type)
    {
        return type.TypeKind is TypeKind.Enum && HasEnumMembers(type);
    }

    internal static bool HasEnumMembers(INamedTypeSymbol enumType)
    {
        foreach (var member in enumType.GetMembers())
        {
            if (member is IFieldSymbol { ConstantValue: not null })
                return true;
        }

        return false;
    }

    internal static bool SupportsExtensionMembers(Compilation compilation)
    {
        return compilation is CSharpCompilation { LanguageVersion: >= CSharp14 };
    }

    /// <summary>
    /// Members emitted inside an <c>extension(TEnum)</c> block only exist when the consuming
    /// compilation supports extension members.
    /// </summary>
    internal static bool RequiresExtensionMembers(FastEnumMethodKind methodKind)
    {
        return methodKind is FastEnumMethodKind.Parse or FastEnumMethodKind.TryParse or FastEnumMethodKind.GetNames or FastEnumMethodKind.GetValues or FastEnumMethodKind.IsDefined;
    }

    internal static bool TryGetFastEnumInvocationMatch(IInvocationOperation invocationOperation, INamedTypeSymbol enumType, ImmutableHashSet<INamedTypeSymbol> fastEnumTypes, bool supportsExtensionMembers, out FastEnumInvocationMatch match)
    {
        if (!TryGetFastEnumInvocationMatchCore(invocationOperation, enumType, fastEnumTypes, out match))
            return false;

        if (!supportsExtensionMembers && RequiresExtensionMembers(match.MethodKind))
        {
            match = default;
            return false;
        }

        return true;
    }

    private static bool TryGetFastEnumInvocationMatchCore(IInvocationOperation invocationOperation, INamedTypeSymbol enumType, ImmutableHashSet<INamedTypeSymbol> fastEnumTypes, out FastEnumInvocationMatch match)
    {
        if (TryGetEnumToStringInvocationMatch(invocationOperation, fastEnumTypes, out match))
            return true;

        if (!TryGetSystemEnumMethodKind(invocationOperation.TargetMethod, enumType, out var methodKind))
        {
            match = default;
            return false;
        }

        if (!TryGetTargetEnumType(invocationOperation, out var targetEnumType))
        {
            match = default;
            return false;
        }

        if (!fastEnumTypes.Contains(targetEnumType))
        {
            match = default;
            return false;
        }

        match = new FastEnumInvocationMatch(methodKind, targetEnumType);
        return true;
    }

    private static bool TryGetEnumToStringInvocationMatch(IInvocationOperation invocationOperation, ImmutableHashSet<INamedTypeSymbol> fastEnumTypes, out FastEnumInvocationMatch match)
    {
        if (invocationOperation is
            {
                TargetMethod.Name: nameof(object.ToString),
                TargetMethod.IsStatic: false,
                Arguments.Length: 0,
                Instance.Type: INamedTypeSymbol { TypeKind: TypeKind.Enum } enumType,
            } &&
            fastEnumTypes.Contains(enumType))
        {
            match = new FastEnumInvocationMatch(FastEnumMethodKind.ToString, enumType);
            return true;
        }

        match = default;
        return false;
    }

    private static bool TryGetSystemEnumMethodKind(IMethodSymbol method, INamedTypeSymbol enumType, out FastEnumMethodKind methodKind)
    {
        if (!method.IsStatic || !SymbolEqualityComparer.Default.Equals(method.ContainingType, enumType))
        {
            methodKind = FastEnumMethodKind.None;
            return false;
        }

        // Unrecognized System.Enum members (Format, ToObject, GetUnderlyingType, TryFormat, ...) must map
        // to None. Mapping them to `default` previously made every one of them look like Enum.Parse.
        methodKind = method.Name switch
        {
            nameof(Enum.Parse) => FastEnumMethodKind.Parse,
            nameof(Enum.TryParse) => FastEnumMethodKind.TryParse,
            nameof(Enum.GetNames) => FastEnumMethodKind.GetNames,
            nameof(Enum.GetValues) => FastEnumMethodKind.GetValues,
            nameof(Enum.GetName) => FastEnumMethodKind.GetName,
            nameof(Enum.IsDefined) => FastEnumMethodKind.IsDefined,
            _ => FastEnumMethodKind.None,
        };

        return methodKind is not FastEnumMethodKind.None;
    }

    private static bool TryGetTargetEnumType(IInvocationOperation invocationOperation, out INamedTypeSymbol targetEnumType)
    {
        if (invocationOperation.TargetMethod.IsGenericMethod &&
            invocationOperation.TargetMethod.TypeArguments.Length >= 1 &&
            invocationOperation.TargetMethod.TypeArguments[0] is INamedTypeSymbol { TypeKind: TypeKind.Enum } typeArgument)
        {
            targetEnumType = typeArgument;
            return true;
        }

        if (invocationOperation.Arguments.Length > 0 &&
            UnwrapConversion(invocationOperation.Arguments[0].Value) is ITypeOfOperation { TypeOperand: INamedTypeSymbol { TypeKind: TypeKind.Enum } typeOfOperation })
        {
            targetEnumType = typeOfOperation;
            return true;
        }

        targetEnumType = null!;
        return false;
    }

    internal static bool HasTypeOfFirstArgument(IInvocationOperation invocationOperation)
    {
        return invocationOperation.Arguments.Length > 0 && UnwrapConversion(invocationOperation.Arguments[0].Value) is ITypeOfOperation;
    }

    private static IOperation UnwrapConversion(IOperation operation)
    {
        while (operation is IConversionOperation conversionOperation)
        {
            operation = conversionOperation.Operand;
        }

        return operation;
    }
}
