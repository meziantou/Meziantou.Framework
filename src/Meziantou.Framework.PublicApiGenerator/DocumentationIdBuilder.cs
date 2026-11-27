using System.Collections.Immutable;

namespace Meziantou.Framework.PublicApiGenerator;

/// <summary>
/// Builds XML documentation IDs following the conventions of the C# compiler
/// (https://learn.microsoft.com/dotnet/csharp/language-reference/xmldoc/#id-strings).
/// </summary>
internal static class DocumentationIdBuilder
{
    // Documentation IDs are computed for every symbol of an assembly, so they are written to a single reused builder
    [ThreadStatic]
    private static StringBuilder? s_cachedBuilder;

    public static string GetDocumentationId(PublicApiSymbol symbol)
    {
        var sb = s_cachedBuilder ?? new StringBuilder(128);
        s_cachedBuilder = null;
        sb.Clear();
        switch (symbol)
        {
            case PublicApiType type:
                sb.Append("T:");
                AppendTypeDefinitionName(sb, type);
                break;

            case PublicApiField field:
                sb.Append("F:");
                AppendMemberName(sb, field);
                break;

            case PublicApiEvent @event:
                sb.Append("E:");
                AppendMemberName(sb, @event);
                break;

            case PublicApiProperty property:
                sb.Append("P:");
                AppendMemberName(sb, property);
                AppendParameterList(sb, property.Parameters);
                break;

            case PublicApiMethod method:
                sb.Append("M:");
                AppendMemberName(sb, method);
                if (method.GenericParameters.Length > 0)
                {
                    sb.Append("``");
                    sb.Append(method.GenericParameters.Length.ToString(CultureInfo.InvariantCulture));
                }

                AppendParameterList(sb, method.Parameters);
                if (method.MethodKind == PublicApiMethodKind.Conversion)
                {
                    sb.Append('~');
                    AppendTypeName(sb, method.ReturnType);
                }

                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(symbol));
        }

        var result = sb.ToString();
        s_cachedBuilder = sb;
        return result;
    }

    // The members of a C# 14 extension block are declared by the compiler-generated grouping type nested in the static class
    private static void AppendMemberName(StringBuilder sb, PublicApiMember member)
    {
        AppendTypeDefinitionName(sb, member.DeclaringType!);
        if (member.ExtensionBlock is { } block)
        {
            sb.Append('.');
            sb.Append(block.GroupingTypeMetadataName);
        }

        sb.Append('.');
        AppendEscapedMemberName(sb, member.Name);
    }

    private static void AppendParameterList(StringBuilder sb, ImmutableArray<PublicApiParameter> parameters)
    {
        if (parameters.IsEmpty)
            return;

        sb.Append('(');
        for (var i = 0; i < parameters.Length; i++)
        {
            if (i > 0)
            {
                sb.Append(',');
            }

            AppendTypeName(sb, parameters[i].Type);
            if (parameters[i].RefKind != PublicApiRefKind.None)
            {
                sb.Append('@');
            }
        }

        sb.Append(')');
    }

    // Same as EscapeMemberName
    private static void AppendEscapedMemberName(StringBuilder sb, string name)
    {
        var value = name.AsSpan();
        var aliasSeparatorIndex = value.IndexOf("::", StringComparison.Ordinal);
        if (aliasSeparatorIndex >= 0)
        {
            value = value[(aliasSeparatorIndex + 2)..];
        }

        foreach (var c in value)
        {
            sb.Append(c switch
            {
                '.' => '#',
                '<' => '{',
                '>' => '}',
                _ => c,
            });
        }
    }

    public static string GetMethodDocumentationId(string declaringTypeName, string name, int genericParameterCount, ImmutableArray<string> parameterTypes, string? conversionReturnType)
    {
        var sb = new StringBuilder("M:");
        sb.Append(declaringTypeName);
        sb.Append('.');
        sb.Append(EscapeMemberName(name));
        if (genericParameterCount > 0)
        {
            sb.Append("``");
            sb.Append(genericParameterCount.ToString(CultureInfo.InvariantCulture));
        }

        sb.Append(GetParameterList(parameterTypes));
        if (conversionReturnType is not null)
        {
            sb.Append('~');
            sb.Append(conversionReturnType);
        }

        return sb.ToString();
    }

    public static string GetParameterList(ImmutableArray<PublicApiParameter> parameters)
    {
        return GetParameterList([.. parameters.Select(GetParameterTypeName)]);
    }

    public static string GetParameterList(ImmutableArray<string> parameterTypes)
    {
        if (parameterTypes.IsDefaultOrEmpty)
            return string.Empty;

        return "(" + string.Join(',', parameterTypes) + ")";
    }

    // The compiler uses the metadata name, in which it replaces the characters that have a meaning in documentation IDs
    // (e.g. "System.IDisposable.Dispose" becomes "System#IDisposable#Dispose" and ".ctor" becomes "#ctor")
    public static string EscapeMemberName(string name)
    {
        var aliasSeparatorIndex = name.IndexOf("::", StringComparison.Ordinal);
        if (aliasSeparatorIndex >= 0)
        {
            name = name[(aliasSeparatorIndex + 2)..];
        }

        return name.Replace('.', '#').Replace('<', '{').Replace('>', '}');
    }

    public static string GetTypeDefinitionName(PublicApiType type)
    {
        if (type.DeclaringType is not null)
            return GetTypeDefinitionName(type.DeclaringType) + "." + type.MetadataName;

        return type.Namespace.Length == 0 ? type.MetadataName : type.Namespace + "." + type.MetadataName;
    }

    public static string GetTypeDefinitionName(PublicApiNamedTypeReference type)
    {
        if (type.ContainingType is not null)
            return GetTypeDefinitionName(type.ContainingType) + "." + type.MetadataName;

        return type.Namespace.Length == 0 ? type.MetadataName : type.Namespace + "." + type.MetadataName;
    }

    private static void AppendTypeDefinitionName(StringBuilder sb, PublicApiType type)
    {
        if (type.DeclaringType is not null)
        {
            AppendTypeDefinitionName(sb, type.DeclaringType);
            sb.Append('.');
        }
        else if (type.Namespace.Length > 0)
        {
            sb.Append(type.Namespace);
            sb.Append('.');
        }

        sb.Append(type.MetadataName);
    }

    private static void AppendTypeDefinitionName(StringBuilder sb, PublicApiNamedTypeReference type)
    {
        if (type.ContainingType is not null)
        {
            AppendTypeDefinitionName(sb, type.ContainingType);
            sb.Append('.');
        }
        else if (type.Namespace.Length > 0)
        {
            sb.Append(type.Namespace);
            sb.Append('.');
        }

        sb.Append(type.MetadataName);
    }

    // Same as type.GetAllTypeArguments().Any(), without allocating an enumerator
    private static bool HasTypeArguments(PublicApiNamedTypeReference type)
    {
        for (var current = type; current is not null; current = current.ContainingType)
        {
            if (!current.TypeArguments.IsEmpty)
                return true;
        }

        return false;
    }

    // The name of a type used in a signature (e.g. "System.Collections.Generic.List{System.String}")
    public static string GetTypeName(PublicApiTypeReference type)
    {
        var sb = new StringBuilder();
        AppendTypeName(sb, type);
        return sb.ToString();
    }

    private static string GetParameterTypeName(PublicApiParameter parameter)
    {
        var typeName = GetTypeName(parameter.Type);
        return parameter.RefKind == PublicApiRefKind.None ? typeName : typeName + "@";
    }

    private static void AppendTypeName(StringBuilder sb, PublicApiTypeReference type)
    {
        switch (type)
        {
            case PublicApiNamedTypeReference named:
                if (!HasTypeArguments(named))
                {
                    AppendTypeDefinitionName(sb, named);
                    return;
                }

                AppendConstructedTypeName(sb, named);
                return;

            case PublicApiTypeParameterReference typeParameter:
                sb.Append(typeParameter.IsMethodTypeParameter ? "``" : "`");
                sb.Append(typeParameter.Ordinal.ToString(CultureInfo.InvariantCulture));
                return;

            case PublicApiArrayTypeReference array:
                AppendTypeName(sb, array.ElementType);
                if (array.IsSZArray)
                {
                    sb.Append("[]");
                    return;
                }

                sb.Append('[');
                for (var i = 0; i < array.Rank; i++)
                {
                    if (i > 0)
                    {
                        sb.Append(',');
                    }

                    sb.Append("0:");
                }

                sb.Append(']');
                return;

            case PublicApiPointerTypeReference pointer:
                AppendTypeName(sb, pointer.ElementType);
                sb.Append('*');
                return;

            case PublicApiFunctionPointerTypeReference:
                // The C# compiler does not emit anything for function pointer types
                return;
        }
    }

    private static void AppendConstructedTypeName(StringBuilder sb, PublicApiNamedTypeReference type)
    {
        if (type.ContainingType is not null)
        {
            AppendConstructedTypeName(sb, type.ContainingType);
            sb.Append('.');
        }
        else if (type.Namespace.Length > 0)
        {
            sb.Append(type.Namespace);
            sb.Append('.');
        }

        sb.Append(type.Name);
        if (type.TypeArguments.IsEmpty)
            return;

        sb.Append('{');
        for (var i = 0; i < type.TypeArguments.Length; i++)
        {
            if (i > 0)
            {
                sb.Append(',');
            }

            AppendTypeName(sb, type.TypeArguments[i]);
        }

        sb.Append('}');
    }
}
