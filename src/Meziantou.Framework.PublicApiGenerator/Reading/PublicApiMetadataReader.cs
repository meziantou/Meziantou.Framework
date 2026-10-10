using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

namespace Meziantou.Framework.PublicApiGenerator;

/// <summary>Reads the public API of an assembly from its metadata, without loading it.</summary>
internal sealed partial class PublicApiMetadataReader : IDisposable
{
    internal const string MemorySafetyRulesAttributeFullName = "System.Runtime.CompilerServices.MemorySafetyRulesAttribute";
    internal const string RequiresUnsafeAttributeFullName = "System.Diagnostics.CodeAnalysis.RequiresUnsafeAttribute";
    private const string ClosedAttributeFullName = "System.Runtime.CompilerServices.ClosedAttribute";
    private const string IsClosedTypeAttributeFullName = "System.Runtime.CompilerServices.IsClosedTypeAttribute";
    private const string UnionAttributeFullName = "System.Runtime.CompilerServices.UnionAttribute";
    internal const string IUnionInterfaceFullName = "System.Runtime.CompilerServices.IUnion";
    private const GenericParameterAttributes AllowByRefLikeGenericParameterConstraint = (GenericParameterAttributes)0x20;

    private readonly PEReader _peReader;
    private readonly MetadataReader _metadataReader;
    private readonly PublicApiReadOptions _options;
    private readonly string? _assemblyDirectory;
    private readonly RawTypeProvider _typeProvider;
    private readonly InheritedMemberResolver _inheritedMemberResolver;
    private readonly Guid _moduleVersionId;
    private readonly string _moduleName;
    private readonly string? _assemblyName;
    private readonly bool _usesUpdatedMemorySafetyRules;
    private readonly Dictionary<TypeDefinitionHandle, bool> _isValueTypeDefinitionCache = [];
    private readonly Dictionary<string, TypeDefinitionHandle> _typeDefinitionsByFullName = new(StringComparer.Ordinal);
    private Dictionary<string, PrimitiveTypeCode>? _enumUnderlyingTypes;

    private PublicApiMetadataReader(PEReader peReader, PublicApiReadOptions options, string? assemblyDirectory)
    {
        _peReader = peReader;
        _metadataReader = peReader.GetMetadataReader();
        _options = options;
        _assemblyDirectory = assemblyDirectory;

        var moduleDefinition = _metadataReader.GetModuleDefinition();
        _moduleVersionId = _metadataReader.GetGuid(moduleDefinition.Mvid);
        _moduleName = _metadataReader.GetString(moduleDefinition.Name);
        _assemblyName = _metadataReader.IsAssembly ? _metadataReader.GetString(_metadataReader.GetAssemblyDefinition().Name) : null;
        _usesUpdatedMemorySafetyRules = HasAttribute(moduleDefinition.GetCustomAttributes(), MemorySafetyRulesAttributeFullName);

        foreach (var typeDefinitionHandle in _metadataReader.TypeDefinitions)
        {
            // The first definition wins, which is what the previous linear scans did
            _typeDefinitionsByFullName.TryAdd(GetTypeDefinitionFullName(typeDefinitionHandle), typeDefinitionHandle);
        }

        _typeProvider = new RawTypeProvider(_assemblyName ?? _moduleName, GetCoreLibraryName(), IsValueTypeDefinition, GetUnderlyingEnumType);
        _inheritedMemberResolver = new InheritedMemberResolver(_metadataReader, assemblyDirectory);
    }

    // The directory of the assembly is used to read the enums and the base types declared in the referenced assemblies that are next to it
    public static PublicApiAssembly Read(PEReader peReader, PublicApiReadOptions? options, string? assemblyDirectory = null)
    {
        if (!peReader.HasMetadata)
            throw new InvalidOperationException("The file does not contain .NET metadata.");

        using var reader = new PublicApiMetadataReader(peReader, options ?? new PublicApiReadOptions(), assemblyDirectory);
        return reader.ReadAssembly();
    }

    public void Dispose() => _inheritedMemberResolver.Dispose();

    private PublicApiAssembly ReadAssembly()
    {
        var types = new List<PublicApiType>();
        foreach (var typeDefinitionHandle in _metadataReader.TypeDefinitions)
        {
            var typeDefinition = _metadataReader.GetTypeDefinition(typeDefinitionHandle);
            if (!typeDefinition.GetDeclaringType().IsNil)
                continue;

            if (!IsExternallyVisible(typeDefinition.Attributes))
                continue;

            if (string.Equals(_metadataReader.GetString(typeDefinition.Name), "<Module>", StringComparison.Ordinal))
                continue;

            var namespaceName = typeDefinition.Namespace.IsNil ? string.Empty : _metadataReader.GetString(typeDefinition.Namespace);
            types.Add(ReadType(typeDefinitionHandle, typeDefinition, namespaceName, declaringTypeGenericParameterCount: 0));
        }

        var orderedTypes = types
            .OrderBy(static type => type.Namespace, StringComparer.Ordinal)
            .ThenBy(static type => type.FullName, StringComparer.Ordinal)
            .ToImmutableArray();

        var moduleDefinition = _metadataReader.GetModuleDefinition();
        var (pdbReferences, hasEmbeddedPdb) = ReadDebugDirectory();
        var module = new PublicApiModule(_moduleName, _moduleVersionId, ReadAttributes(moduleDefinition.GetCustomAttributes()), pdbReferences, hasEmbeddedPdb);

        string name;
        Version version;
        string? cultureName = null;
        string? publicKeyToken = null;
        ImmutableArray<PublicApiAttribute> attributes;
        if (_metadataReader.IsAssembly)
        {
            var assemblyDefinition = _metadataReader.GetAssemblyDefinition();
            name = _metadataReader.GetString(assemblyDefinition.Name);
            version = assemblyDefinition.Version;
            cultureName = assemblyDefinition.Culture.IsNil ? null : _metadataReader.GetString(assemblyDefinition.Culture);
            if (string.IsNullOrEmpty(cultureName))
            {
                cultureName = null;
            }

            var token = assemblyDefinition.GetAssemblyName().GetPublicKeyToken();
            publicKeyToken = token is { Length: > 0 } ? Convert.ToHexStringLower(token) : null;
            attributes = ReadAttributes(assemblyDefinition.GetCustomAttributes());
        }
        else
        {
            name = Path.GetFileNameWithoutExtension(_moduleName);
            version = new Version(0, 0, 0, 0);
            attributes = [];
        }

        var targetFrameworkMoniker = PublicApiTargetFramework.GetFrameworkMonikerFromMetadata(_metadataReader);
        var targetFramework = _options.TargetFramework;
        if (string.IsNullOrWhiteSpace(targetFramework))
        {
            targetFramework = targetFrameworkMoniker is null ? null : PublicApiTargetFramework.TryConvertFrameworkMonikerToTargetFramework(targetFrameworkMoniker) ?? targetFrameworkMoniker;
        }

        return new PublicApiAssembly(
            name,
            version,
            cultureName,
            publicKeyToken,
            module,
            targetFramework,
            targetFrameworkMoniker,
            _options.InputIdentity,
            _usesUpdatedMemorySafetyRules,
            attributes,
            orderedTypes)
        {
            IsModuleOnly = !_metadataReader.IsAssembly,
        };
    }

    private (ImmutableArray<PublicApiPdbReference> PdbReferences, bool HasEmbeddedPdb) ReadDebugDirectory()
    {
        try
        {
            var pdbReferences = ImmutableArray.CreateBuilder<PublicApiPdbReference>();
            var hasEmbeddedPdb = false;
            foreach (var entry in _peReader.ReadDebugDirectory())
            {
                if (entry.Type == DebugDirectoryEntryType.CodeView)
                {
                    var data = _peReader.ReadCodeViewDebugDirectoryData(entry);
                    pdbReferences.Add(new PublicApiPdbReference(data.Guid, data.Age, entry.Stamp, data.Path, entry.IsPortableCodeView));
                }
                else if (entry.Type == DebugDirectoryEntryType.EmbeddedPortablePdb)
                {
                    hasEmbeddedPdb = true;
                }
            }

            return (pdbReferences.ToImmutable(), hasEmbeddedPdb);
        }
        catch (BadImageFormatException)
        {
            return ([], false);
        }
    }

    private PublicApiMetadataOrigin CreateOrigin(EntityHandle handle) => new(_moduleVersionId, _moduleName, MetadataTokens.GetToken(handle));

    // The core library is the assembly that declares System.Object. Primitive types are encoded without a type reference,
    // so their assembly is inferred from the references to the other types of the core library.
    private string? GetCoreLibraryName()
    {
        string? fallback = null;
        foreach (var typeReferenceHandle in _metadataReader.TypeReferences)
        {
            var typeReference = _metadataReader.GetTypeReference(typeReferenceHandle);
            if (typeReference.ResolutionScope.Kind != HandleKind.AssemblyReference || typeReference.Namespace.IsNil)
                continue;

            if (!string.Equals(_metadataReader.GetString(typeReference.Namespace), "System", StringComparison.Ordinal))
                continue;

            var name = _metadataReader.GetString(typeReference.Name);
            if (name is "Object" or "ValueType" or "Enum" or "String" or "Int32" or "Attribute" or "MulticastDelegate")
            {
                var assemblyName = _metadataReader.GetString(_metadataReader.GetAssemblyReference((AssemblyReferenceHandle)typeReference.ResolutionScope).Name);
                if (name is "Object")
                    return assemblyName;

                fallback ??= assemblyName;
            }
        }

        if (fallback is not null)
            return fallback;

        // The assembly may be the core library
        return _typeDefinitionsByFullName.ContainsKey("System.Object") ? _assemblyName : null;
    }

    private string GetTypeDefinitionFullName(TypeDefinitionHandle handle)
    {
        var typeDefinition = _metadataReader.GetTypeDefinition(handle);
        var name = _metadataReader.GetString(typeDefinition.Name);
        var declaringTypeHandle = typeDefinition.GetDeclaringType();
        if (!declaringTypeHandle.IsNil)
            return GetTypeDefinitionFullName(declaringTypeHandle) + "+" + name;

        var namespaceName = typeDefinition.Namespace.IsNil ? string.Empty : _metadataReader.GetString(typeDefinition.Namespace);
        return namespaceName.Length == 0 ? name : namespaceName + "." + name;
    }

    private bool IsValueTypeDefinition(TypeDefinitionHandle handle)
    {
        if (_isValueTypeDefinitionCache.TryGetValue(handle, out var result))
            return result;

        var typeDefinition = _metadataReader.GetTypeDefinition(handle);
        if ((typeDefinition.Attributes & TypeAttributes.ClassSemanticsMask) == TypeAttributes.Interface)
        {
            result = false;
        }
        else
        {
            var baseTypeName = GetTypeFullName(typeDefinition.BaseType);
            result = baseTypeName is "System.ValueType" or "System.Enum" &&
                     !string.Equals(GetTypeDefinitionFullName(handle), "System.Enum", StringComparison.Ordinal);
        }

        _isValueTypeDefinitionCache[handle] = result;
        return result;
    }

    private bool TryGetTypeDefinition(RawType.Named type, out TypeDefinitionHandle handle)
    {
        return _typeDefinitionsByFullName.TryGetValue(type.FullName, out handle);
    }

    // Returns the full name of a TypeDef or a TypeRef, using '+' for nested types
    private string GetTypeFullName(EntityHandle handle)
    {
        if (handle.IsNil)
            return string.Empty;

        switch (handle.Kind)
        {
            case HandleKind.TypeDefinition:
                return GetTypeDefinitionFullName((TypeDefinitionHandle)handle);

            case HandleKind.TypeReference:
                {
                    var typeReference = _metadataReader.GetTypeReference((TypeReferenceHandle)handle);
                    var name = _metadataReader.GetString(typeReference.Name);
                    if (typeReference.ResolutionScope.Kind == HandleKind.TypeReference)
                        return GetTypeFullName(typeReference.ResolutionScope) + "+" + name;

                    var namespaceName = typeReference.Namespace.IsNil ? string.Empty : _metadataReader.GetString(typeReference.Namespace);
                    return namespaceName.Length == 0 ? name : namespaceName + "." + name;
                }

            default:
                return string.Empty;
        }
    }

    private string GetAttributeTypeFullName(CustomAttribute attribute)
    {
        return attribute.Constructor.Kind switch
        {
            HandleKind.MethodDefinition => GetTypeFullName(_metadataReader.GetMethodDefinition((MethodDefinitionHandle)attribute.Constructor).GetDeclaringType()),
            HandleKind.MemberReference => GetTypeFullName(_metadataReader.GetMemberReference((MemberReferenceHandle)attribute.Constructor).Parent),
            _ => string.Empty,
        };
    }

    private bool HasAttribute(CustomAttributeHandleCollection attributes, string attributeTypeFullName)
    {
        foreach (var attributeHandle in attributes)
        {
            var attribute = _metadataReader.GetCustomAttribute(attributeHandle);
            if (string.Equals(GetAttributeTypeFullName(attribute), attributeTypeFullName, StringComparison.Ordinal))
                return true;
        }

        return false;
    }

    private bool HasAttribute(CustomAttributeHandleCollection? attributes, string attributeTypeFullName)
    {
        return attributes is not null && HasAttribute(attributes.Value, attributeTypeFullName);
    }

    private bool IsRequiresUnsafeMember(CustomAttributeHandleCollection attributes)
    {
        return _usesUpdatedMemorySafetyRules && HasAttribute(attributes, RequiresUnsafeAttributeFullName);
    }

    private static bool IsExternallyVisible(TypeAttributes attributes)
    {
        return (attributes & TypeAttributes.VisibilityMask) == TypeAttributes.Public;
    }

    private static bool IsExternallyVisibleNested(TypeAttributes attributes)
    {
        return (attributes & TypeAttributes.VisibilityMask) is TypeAttributes.NestedPublic or TypeAttributes.NestedFamily or TypeAttributes.NestedFamORAssem;
    }

    private static bool IsExternallyVisible(MethodAttributes attributes)
    {
        return (attributes & MethodAttributes.MemberAccessMask) is MethodAttributes.Public or MethodAttributes.Family or MethodAttributes.FamORAssem;
    }

    private static bool IsExternallyVisible(FieldAttributes attributes)
    {
        return (attributes & FieldAttributes.FieldAccessMask) is FieldAttributes.Public or FieldAttributes.Family or FieldAttributes.FamORAssem;
    }

    private bool IsExternallyVisible(TypeDefinition typeDefinition)
    {
        var declaringTypeHandle = typeDefinition.GetDeclaringType();
        if (declaringTypeHandle.IsNil)
            return IsExternallyVisible(typeDefinition.Attributes);

        return IsExternallyVisibleNested(typeDefinition.Attributes) && IsExternallyVisible(_metadataReader.GetTypeDefinition(declaringTypeHandle));
    }

    private static PublicApiAccessibility GetAccessibility(TypeAttributes attributes)
    {
        return (attributes & TypeAttributes.VisibilityMask) switch
        {
            TypeAttributes.Public or TypeAttributes.NestedPublic => PublicApiAccessibility.Public,
            TypeAttributes.NestedFamily => PublicApiAccessibility.Protected,
            TypeAttributes.NestedFamORAssem => PublicApiAccessibility.ProtectedInternal,
            TypeAttributes.NestedFamANDAssem => PublicApiAccessibility.PrivateProtected,
            TypeAttributes.NestedPrivate => PublicApiAccessibility.Private,
            _ => PublicApiAccessibility.Internal,
        };
    }

    private static PublicApiAccessibility GetAccessibility(MethodAttributes attributes)
    {
        return (attributes & MethodAttributes.MemberAccessMask) switch
        {
            MethodAttributes.Public => PublicApiAccessibility.Public,
            MethodAttributes.Family => PublicApiAccessibility.Protected,
            MethodAttributes.FamORAssem => PublicApiAccessibility.ProtectedInternal,
            MethodAttributes.Assembly => PublicApiAccessibility.Internal,
            MethodAttributes.FamANDAssem => PublicApiAccessibility.PrivateProtected,
            _ => PublicApiAccessibility.Private,
        };
    }

    private static PublicApiAccessibility GetAccessibility(FieldAttributes attributes)
    {
        return (attributes & FieldAttributes.FieldAccessMask) switch
        {
            FieldAttributes.Public => PublicApiAccessibility.Public,
            FieldAttributes.Family => PublicApiAccessibility.Protected,
            FieldAttributes.FamORAssem => PublicApiAccessibility.ProtectedInternal,
            FieldAttributes.Assembly => PublicApiAccessibility.Internal,
            FieldAttributes.FamANDAssem => PublicApiAccessibility.PrivateProtected,
            _ => PublicApiAccessibility.Private,
        };
    }
}
