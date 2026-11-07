using System.Reflection.Metadata;

namespace Meziantou.Framework.Assertions.Architecture.Reading;

internal static class MetadataNames
{
    // "MyApp.Domain" contains MyApp.Domain and MyApp.Domain.Events, not MyApp.DomainEvents
    public static bool IsInNamespaceOrChildren(string typeNamespace, string @namespace)
    {
        return @namespace.Length == 0
            || string.Equals(typeNamespace, @namespace, StringComparison.Ordinal)
            || (typeNamespace.Length > @namespace.Length && typeNamespace[@namespace.Length] == '.' && typeNamespace.StartsWith(@namespace, StringComparison.Ordinal));
    }

    // Returns the full name of a TypeDef or a TypeRef, using '+' for nested types
    public static string GetFullName(MetadataReader reader, EntityHandle handle)
    {
        StringHandle nameHandle;
        StringHandle namespaceHandle;
        EntityHandle declaringType;
        switch (handle.Kind)
        {
            case HandleKind.TypeDefinition:
                {
                    var typeDefinition = reader.GetTypeDefinition((TypeDefinitionHandle)handle);
                    nameHandle = typeDefinition.Name;
                    namespaceHandle = typeDefinition.Namespace;
                    declaringType = typeDefinition.GetDeclaringType();
                    break;
                }

            case HandleKind.TypeReference:
                {
                    var typeReference = reader.GetTypeReference((TypeReferenceHandle)handle);
                    nameHandle = typeReference.Name;
                    namespaceHandle = typeReference.Namespace;
                    declaringType = typeReference.ResolutionScope.Kind == HandleKind.TypeReference ? typeReference.ResolutionScope : default;
                    break;
                }

            case HandleKind.TypeSpecification when GetGenericTypeDefinition(reader, handle) is { IsNil: false } definition:
                return GetFullName(reader, definition);

            default:
                return string.Empty;
        }

        var name = reader.GetString(nameHandle);
        if (!declaringType.IsNil)
            return GetFullName(reader, declaringType) + "+" + name;

        var namespaceName = namespaceHandle.IsNil ? string.Empty : reader.GetString(namespaceHandle);
        return namespaceName.Length == 0 ? name : namespaceName + "." + name;
    }

    // Returns the TypeDef or TypeRef of a generic instantiation (List<int> → List`1), or a nil handle
    public static EntityHandle GetGenericTypeDefinition(MetadataReader reader, EntityHandle handle)
    {
        if (handle.Kind != HandleKind.TypeSpecification)
            return default;

        try
        {
            var blobReader = reader.GetBlobReader(reader.GetTypeSpecification((TypeSpecificationHandle)handle).Signature);
            if (blobReader.ReadSignatureTypeCode() != SignatureTypeCode.GenericTypeInstance)
                return default;

            blobReader.ReadSignatureTypeCode(); // class or valuetype
            return blobReader.ReadTypeHandle();
        }
        catch (BadImageFormatException)
        {
            return default;
        }
    }

    // Returns the TypeDef or TypeRef of a type handle, unwrapping generic instantiations
    public static EntityHandle GetNamedTypeHandle(MetadataReader reader, EntityHandle handle)
    {
        return handle.Kind switch
        {
            HandleKind.TypeDefinition or HandleKind.TypeReference => handle,
            HandleKind.TypeSpecification => GetGenericTypeDefinition(reader, handle),
            _ => default,
        };
    }

    public static string RemoveArity(string name)
    {
        var index = name.IndexOf('`', StringComparison.Ordinal);
        return index < 0 ? name : name[..index];
    }

    public static int GetArity(string name)
    {
        var index = name.IndexOf('`', StringComparison.Ordinal);
        return index >= 0 && int.TryParse(name.AsSpan(index + 1), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var arity) ? arity : 0;
    }

    // The name of a type of the architecture, as written in a metadata name: "Ns.Outer+Inner`1"
    public static string GetFullName(Type type)
    {
        if (type.IsGenericParameter)
            throw new ArgumentException($"The type '{type}' is a generic parameter.", nameof(type));

        if (type.IsGenericType && !type.IsGenericTypeDefinition)
            type = type.GetGenericTypeDefinition();

        if (type.HasElementType)
            throw new ArgumentException($"The type '{type}' is not a named type.", nameof(type));

        return type.FullName ?? throw new ArgumentException($"The type '{type}' has no full name.", nameof(type));
    }

    public static string GetAttributeTypeFullName(MetadataReader reader, CustomAttribute attribute)
    {
        var parent = attribute.Constructor.Kind switch
        {
            HandleKind.MethodDefinition => reader.GetMethodDefinition((MethodDefinitionHandle)attribute.Constructor).GetDeclaringType(),
            HandleKind.MemberReference => reader.GetMemberReference((MemberReferenceHandle)attribute.Constructor).Parent,
            _ => default,
        };

        return parent.IsNil ? string.Empty : GetFullName(reader, parent);
    }

    public static bool HasAttribute(MetadataReader reader, CustomAttributeHandleCollection attributes, string fullName)
    {
        foreach (var handle in attributes)
        {
            if (string.Equals(GetAttributeTypeFullName(reader, reader.GetCustomAttribute(handle)), fullName, StringComparison.Ordinal))
                return true;
        }

        return false;
    }

    // The compiler names the code it generates for a member "<Member>suffix" (lambdas, local functions, state machines, backing fields).
    // Returns "Member", or null when the name has no such prefix or the prefix is empty ("<>c__DisplayClass0_0").
    public static string? GetGeneratedCodeOwnerName(string name)
    {
        if (!IsGeneratedName(name))
            return null;

        var depth = 0;
        for (var i = 0; i < name.Length; i++)
        {
            switch (name[i])
            {
                case '<':
                    depth++;
                    break;

                case '>':
                    depth--;
                    if (depth == 0)
                        return i > 1 ? name[1..i] : null;

                    break;
            }
        }

        return null;
    }

    public static bool IsGeneratedName(string name) => name.Length > 0 && name[0] == '<';

    // Members whose name starts with '<' are generated by the compiler, except the entry point of top-level statements
    public static bool IsCompilerGeneratedMemberName(string name) => IsGeneratedName(name) && name is not "<Main>$";
}
