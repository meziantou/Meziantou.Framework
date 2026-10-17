using System.Collections.Immutable;

namespace Meziantou.Framework.PublicApiGenerator;

/// <summary>
/// Builds XML documentation IDs following the conventions of the C# compiler
/// (https://learn.microsoft.com/dotnet/csharp/language-reference/xmldoc/#id-strings).
/// </summary>
internal static class DocumentationIdBuilder
{
    public static string GetDocumentationId(PublicApiSymbol symbol)
    {
        return symbol switch
        {
            PublicApiType type => "T:" + GetTypeDefinitionName(type),
            PublicApiField field => "F:" + GetTypeDefinitionName(field.DeclaringType!) + "." + EscapeMemberName(field.Name),
            PublicApiEvent @event => "E:" + GetTypeDefinitionName(@event.DeclaringType!) + "." + EscapeMemberName(@event.Name),
            PublicApiProperty property => "P:" + GetTypeDefinitionName(property.DeclaringType!) + "." + EscapeMemberName(property.Name) + GetParameterList(property.Parameters),
            PublicApiMethod method => GetMethodDocumentationId(
                GetTypeDefinitionName(method.DeclaringType!),
                method.Name,
                method.GenericParameters.Length,
                [.. method.Parameters.Select(GetParameterTypeName)],
                method.MethodKind == PublicApiMethodKind.Conversion ? GetTypeName(method.ReturnType) : null),
            _ => throw new ArgumentOutOfRangeException(nameof(symbol)),
        };
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
                if (!named.GetAllTypeArguments().Any())
                {
                    sb.Append(GetTypeDefinitionName(named));
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
