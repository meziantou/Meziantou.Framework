using System.Reflection;
using System.Reflection.PortableExecutable;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;

namespace Meziantou.Framework.PublicApiGenerator;

internal static class PublicApiModelReader
{
    public static PublicApiModel ReadFromMetadata(string assemblyPath)
    {
        ArgumentException.ThrowIfNullOrEmpty(assemblyPath);

        using var stream = File.OpenRead(assemblyPath);
        using var peReader = new PEReader(stream, PEStreamOptions.LeaveOpen);
        if (!peReader.HasMetadata)
            throw new InvalidOperationException($"The file '{assemblyPath}' does not contain .NET metadata.");

        var metadataReader = peReader.GetMetadataReader();
        var types = new List<PublicApiTypeModel>();
        foreach (var typeDefinitionHandle in metadataReader.TypeDefinitions)
        {
            var typeDefinition = metadataReader.GetTypeDefinition(typeDefinitionHandle);
            if (!typeDefinition.GetDeclaringType().IsNil)
                continue;

            if (!IsExternallyVisible(typeDefinition.Attributes))
                continue;

            var typeName = metadataReader.GetString(typeDefinition.Name);
            if (string.Equals(typeName, "<Module>", StringComparison.Ordinal))
                continue;

            types.Add(BuildTypeModel(metadataReader, typeDefinitionHandle, typeDefinition));
        }

        return new PublicApiModel(
            [.. types.OrderBy(static type => type.Namespace, StringComparer.Ordinal)
                     .ThenBy(static type => type.QualifiedName, StringComparer.Ordinal)]);
    }

    public static PublicApiModel ReadFromReflection(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);

        var types = assembly.GetExportedTypes().Where(static type => type.DeclaringType is null);
        return PublicApiModelBuilder.Build(types);
    }

    private static PublicApiTypeModel BuildTypeModel(MetadataReader metadataReader, TypeDefinitionHandle typeDefinitionHandle, TypeDefinition typeDefinition)
    {
        var namespaceName = typeDefinition.Namespace.IsNil ? string.Empty : metadataReader.GetString(typeDefinition.Namespace);
        var name = RemoveGenericArity(metadataReader.GetString(typeDefinition.Name));
        var qualifiedName = string.IsNullOrEmpty(namespaceName) ? name : namespaceName + "." + name;
        var source = BuildTypeSource(metadataReader, typeDefinitionHandle, typeDefinition, name);
        return new PublicApiTypeModel(namespaceName, name, qualifiedName, source);
    }

    private static string BuildTypeSource(MetadataReader metadataReader, TypeDefinitionHandle typeDefinitionHandle, TypeDefinition typeDefinition, string typeName)
    {
        var accessibility = GetAccessibility(typeDefinition.Attributes);
        var genericArguments = BuildGenericArguments(metadataReader, typeDefinitionHandle);
        var keyword = GetTypeKeyword(metadataReader, typeDefinition);

        if (keyword == "delegate")
        {
            return $"{accessibility} delegate void {typeName}{genericArguments}();";
        }

        return $"{accessibility} {keyword} {typeName}{genericArguments}\n{{\n}}";
    }

    private static string BuildGenericArguments(MetadataReader metadataReader, TypeDefinitionHandle typeDefinitionHandle)
    {
        var genericParameters = metadataReader.GetTypeDefinition(typeDefinitionHandle).GetGenericParameters();
        if (genericParameters.Count == 0)
            return string.Empty;

        var names = genericParameters
            .Select(metadataReader.GetGenericParameter)
            .Select(parameter => metadataReader.GetString(parameter.Name))
            .ToArray();
        return "<" + string.Join(", ", names) + ">";
    }

    private static string GetTypeKeyword(MetadataReader metadataReader, TypeDefinition typeDefinition)
    {
        if ((typeDefinition.Attributes & TypeAttributes.ClassSemanticsMask) == TypeAttributes.Interface)
            return "interface";

        var baseTypeName = GetTypeFullName(metadataReader, typeDefinition.BaseType);
        if (string.Equals(baseTypeName, "System.Enum", StringComparison.Ordinal))
            return "enum";

        if (string.Equals(baseTypeName, "System.MulticastDelegate", StringComparison.Ordinal))
            return "delegate";

        if (string.Equals(baseTypeName, "System.ValueType", StringComparison.Ordinal))
            return "struct";

        return "class";
    }

    private static bool IsExternallyVisible(TypeAttributes attributes)
    {
        return (attributes & TypeAttributes.VisibilityMask) == TypeAttributes.Public;
    }

    private static string GetAccessibility(TypeAttributes attributes)
    {
        return (attributes & TypeAttributes.VisibilityMask) == TypeAttributes.Public
            ? "public"
            : "internal";
    }

    private static string GetTypeFullName(MetadataReader metadataReader, EntityHandle handle)
    {
        if (handle.IsNil)
            return string.Empty;

        return handle.Kind switch
        {
            HandleKind.TypeReference => GetTypeFullName(metadataReader, metadataReader.GetTypeReference((TypeReferenceHandle)handle)),
            HandleKind.TypeDefinition => GetTypeFullName(metadataReader, metadataReader.GetTypeDefinition((TypeDefinitionHandle)handle)),
            HandleKind.TypeSpecification => string.Empty,
            _ => string.Empty,
        };
    }

    private static string GetTypeFullName(MetadataReader metadataReader, TypeReference typeReference)
    {
        var namespaceName = typeReference.Namespace.IsNil ? string.Empty : metadataReader.GetString(typeReference.Namespace);
        var name = metadataReader.GetString(typeReference.Name);
        return string.IsNullOrEmpty(namespaceName) ? name : namespaceName + "." + name;
    }

    private static string GetTypeFullName(MetadataReader metadataReader, TypeDefinition typeDefinition)
    {
        var namespaceName = typeDefinition.Namespace.IsNil ? string.Empty : metadataReader.GetString(typeDefinition.Namespace);
        var name = metadataReader.GetString(typeDefinition.Name);
        return string.IsNullOrEmpty(namespaceName) ? name : namespaceName + "." + name;
    }

    private static string RemoveGenericArity(string name)
    {
        var index = name.IndexOf('`', StringComparison.Ordinal);
        if (index < 0)
        {
            return name;
        }

        return name[..index];
    }
}
