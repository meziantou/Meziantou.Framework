using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

namespace Meziantou.Framework.PublicApiGenerator;

/// <summary>
/// Finds the members a type inherits from its base types. The base types that are declared in another assembly are read from the
/// metadata of that assembly, which is looked up next to the assembly being read, then in the assemblies of the current runtime.
/// The base types that cannot be found are ignored.
/// </summary>
internal sealed class InheritedMemberResolver : IDisposable
{
    private const int MaxDepth = 256;
    private const int MaxForwarderDepth = 8;

    private static readonly Lazy<Dictionary<string, string>> PlatformAssemblies = new(GetPlatformAssemblies);

    private readonly Module _mainModule;
    private readonly string? _assemblyDirectory;
    private readonly Dictionary<string, Module?> _modules = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<TypeDefinitionHandle, InheritedMemberSet> _inheritedMembers = [];
    private readonly List<PEReader> _peReaders = [];

    public InheritedMemberResolver(MetadataReader metadataReader, string? assemblyDirectory)
    {
        _mainModule = new Module(metadataReader);
        _assemblyDirectory = assemblyDirectory;
    }

    public void Dispose()
    {
        foreach (var peReader in _peReaders)
        {
            peReader.Dispose();
        }

        _peReaders.Clear();
        _modules.Clear();
    }

    /// <summary>Gets the parameters of a method, as compared by <see cref="InheritedMemberSet"/>.</summary>
    /// <param name="typeArguments">The type arguments of the declaring type. When it is not set, its generic parameters are left as is.</param>
    public static string GetParameterList(MetadataReader reader, MethodDefinition method, ImmutableArray<string> typeArguments = default)
    {
        var signature = method.DecodeSignature(SignatureTypeProvider.Instance, typeArguments);
        return GetParameterList(reader, method, signature.ParameterTypes);
    }

    /// <summary>Gets the parameters of an indexer, as compared by <see cref="InheritedMemberSet"/>.</summary>
    /// <param name="typeArguments">The type arguments of the declaring type. When it is not set, its generic parameters are left as is.</param>
    public static string GetParameterList(MetadataReader reader, PropertyDefinition property, ImmutableArray<string> typeArguments = default)
    {
        var parameterTypes = property.DecodeSignature(SignatureTypeProvider.Instance, typeArguments).ParameterTypes;
        if (parameterTypes.IsEmpty)
            return string.Empty;

        // The ref kind of the parameters is described by the accessors. The last parameter of a setter is the value.
        var accessors = property.GetAccessors();
        var accessorHandle = accessors.Getter.IsNil ? accessors.Setter : accessors.Getter;
        if (accessorHandle.IsNil)
            return InheritedMemberSet.GetParameterList(parameterTypes.Select(type => type.Name));

        return GetParameterList(reader, reader.GetMethodDefinition(accessorHandle), parameterTypes);
    }

    // parameterTypes can be shorter than the parameters of the method, as the value of a set accessor is not a parameter of the indexer
    private static string GetParameterList(MetadataReader reader, MethodDefinition method, ImmutableArray<SignatureType> parameterTypes)
    {
        var names = new string[parameterTypes.Length];
        Dictionary<int, Parameter>? parameters = null;
        for (var i = 0; i < names.Length; i++)
        {
            var type = parameterTypes[i];
            if (!type.IsByReference)
            {
                names[i] = type.Name;
                continue;
            }

            if (parameters is null)
            {
                parameters = [];
                foreach (var parameterHandle in method.GetParameters())
                {
                    var parameter = reader.GetParameter(parameterHandle);
                    parameters.TryAdd(parameter.SequenceNumber, parameter);
                }
            }

            var isOut = false;
            var isReadOnly = false;
            if (parameters.TryGetValue(i + 1, out var parameterDefinition))
            {
                isOut = parameterDefinition.Attributes.HasFlag(ParameterAttributes.Out);
                isReadOnly = HasReadOnlyReferenceAttribute(reader, parameterDefinition);
            }

            names[i] = InheritedMemberSet.GetParameterName(type.Name, isByReference: true, isOut, isReadOnly);
        }

        return InheritedMemberSet.GetParameterList(names);
    }

    private static bool HasReadOnlyReferenceAttribute(MetadataReader reader, Parameter parameter)
    {
        foreach (var attributeHandle in parameter.GetCustomAttributes())
        {
            var constructor = reader.GetCustomAttribute(attributeHandle).Constructor;
            var attributeType = constructor.Kind switch
            {
                HandleKind.MethodDefinition => reader.GetMethodDefinition((MethodDefinitionHandle)constructor).GetDeclaringType(),
                HandleKind.MemberReference => reader.GetMemberReference((MemberReferenceHandle)constructor).Parent,
                _ => default(EntityHandle),
            };

            if (InheritedMemberSet.IsReadOnlyReferenceAttribute(GetTypeFullName(reader, attributeType)))
                return true;
        }

        return false;
    }

    /// <summary>Gets the members inherited by a type of the assembly being read.</summary>
    public InheritedMemberSet GetInheritedMembers(TypeDefinitionHandle typeDefinitionHandle)
    {
        if (_inheritedMembers.TryGetValue(typeDefinitionHandle, out var result))
            return result;

        result = new InheritedMemberSet();
        var typeDefinition = _mainModule.Reader.GetTypeDefinition(typeDefinitionHandle);
        if ((typeDefinition.Attributes & TypeAttributes.ClassSemanticsMask) == TypeAttributes.Interface)
        {
            AddBaseInterfaceMembers(result, new ResolvedType(_mainModule, typeDefinitionHandle, TypeArguments: default), new HashSet<string>(StringComparer.Ordinal), depth: 0);
        }
        else
        {
            var current = new ResolvedType(_mainModule, typeDefinitionHandle, TypeArguments: default);
            for (var depth = 0; depth < MaxDepth; depth++)
            {
                var baseTypeHandle = current.Module.Reader.GetTypeDefinition(current.Handle).BaseType;
                if (baseTypeHandle.IsNil || ResolveType(current.Module, baseTypeHandle, current.TypeArguments) is not { } baseType)
                    break;

                AddMembers(result, baseType.Type);
                current = baseType.Type;
            }
        }

        _inheritedMembers.Add(typeDefinitionHandle, result);
        return result;
    }

    private void AddBaseInterfaceMembers(InheritedMemberSet result, ResolvedType type, HashSet<string> visitedInterfaces, int depth)
    {
        if (depth >= MaxDepth)
            return;

        var reader = type.Module.Reader;
        foreach (var interfaceImplementationHandle in reader.GetTypeDefinition(type.Handle).GetInterfaceImplementations())
        {
            var interfaceHandle = reader.GetInterfaceImplementation(interfaceImplementationHandle).Interface;
            if (ResolveType(type.Module, interfaceHandle, type.TypeArguments) is not { } baseInterface)
                continue;

            if (!visitedInterfaces.Add(baseInterface.Name))
                continue;

            AddMembers(result, baseInterface.Type);
            AddBaseInterfaceMembers(result, baseInterface.Type, visitedInterfaces, depth + 1);
        }
    }

    private static void AddMembers(InheritedMemberSet result, ResolvedType type)
    {
        var reader = type.Module.Reader;
        var typeDefinition = reader.GetTypeDefinition(type.Handle);
        foreach (var fieldHandle in typeDefinition.GetFields())
        {
            var field = reader.GetFieldDefinition(fieldHandle);
            if (IsExternallyVisible(field.Attributes) && !field.Attributes.HasFlag(FieldAttributes.SpecialName))
            {
                result.Add(InheritedMemberKind.Field, reader.GetString(field.Name));
            }
        }

        foreach (var propertyHandle in typeDefinition.GetProperties())
        {
            var property = reader.GetPropertyDefinition(propertyHandle);
            var accessors = property.GetAccessors();
            if (IsExternallyVisible(reader, accessors.Getter) || IsExternallyVisible(reader, accessors.Setter))
            {
                result.Add(InheritedMemberKind.Property, reader.GetString(property.Name), arity: 0, GetParameterList(reader, property, type.TypeArguments));
            }
        }

        foreach (var eventHandle in typeDefinition.GetEvents())
        {
            var eventDefinition = reader.GetEventDefinition(eventHandle);
            var accessors = eventDefinition.GetAccessors();
            if (IsExternallyVisible(reader, accessors.Adder) || IsExternallyVisible(reader, accessors.Remover))
            {
                result.Add(InheritedMemberKind.Event, reader.GetString(eventDefinition.Name));
            }
        }

        foreach (var methodHandle in typeDefinition.GetMethods())
        {
            // Accessors, constructors and operators are special-name methods, and they are not hidden by name
            var method = reader.GetMethodDefinition(methodHandle);
            if (!IsExternallyVisible(method.Attributes) || method.Attributes.HasFlag(MethodAttributes.SpecialName))
                continue;

            var name = reader.GetString(method.Name);
            var signature = method.DecodeSignature(SignatureTypeProvider.Instance, type.TypeArguments);
            if (InheritedMemberSet.IsDestructor(name, method.Attributes.HasFlag(MethodAttributes.Static), signature.ParameterTypes.Length))
                continue;

            result.Add(InheritedMemberKind.Method, name, signature.GenericParameterCount, GetParameterList(reader, method, signature.ParameterTypes));
        }

        var genericParameterCount = typeDefinition.GetGenericParameters().Count;
        foreach (var nestedTypeHandle in typeDefinition.GetNestedTypes())
        {
            var nestedType = reader.GetTypeDefinition(nestedTypeHandle);
            if ((nestedType.Attributes & TypeAttributes.VisibilityMask) is TypeAttributes.NestedPublic or TypeAttributes.NestedFamily or TypeAttributes.NestedFamORAssem)
            {
                var name = MetadataNameHelper.RemoveGenericArity(reader.GetString(nestedType.Name));
                result.Add(InheritedMemberKind.Type, name, nestedType.GetGenericParameters().Count - genericParameterCount);
            }
        }
    }

    private static bool IsExternallyVisible(MetadataReader reader, MethodDefinitionHandle methodHandle)
    {
        return !methodHandle.IsNil && IsExternallyVisible(reader.GetMethodDefinition(methodHandle).Attributes);
    }

    private static bool IsExternallyVisible(MethodAttributes attributes)
    {
        return (attributes & MethodAttributes.MemberAccessMask) is MethodAttributes.Public or MethodAttributes.Family or MethodAttributes.FamORAssem;
    }

    private static bool IsExternallyVisible(FieldAttributes attributes)
    {
        return (attributes & FieldAttributes.FieldAccessMask) is FieldAttributes.Public or FieldAttributes.Family or FieldAttributes.FamORAssem;
    }

    // typeArguments are the type arguments of the type that refers to the handle, expressed with the generic parameters of the type being read
    private (ResolvedType Type, string Name)? ResolveType(Module module, EntityHandle handle, ImmutableArray<string> typeArguments)
    {
        SignatureType type;
        switch (handle.Kind)
        {
            case HandleKind.TypeDefinition:
                type = SignatureTypeProvider.Instance.GetTypeFromDefinition(module.Reader, (TypeDefinitionHandle)handle, rawTypeKind: 0);
                break;

            case HandleKind.TypeReference:
                type = SignatureTypeProvider.Instance.GetTypeFromReference(module.Reader, (TypeReferenceHandle)handle, rawTypeKind: 0);
                break;

            case HandleKind.TypeSpecification:
                type = module.Reader.GetTypeSpecification((TypeSpecificationHandle)handle).DecodeSignature(SignatureTypeProvider.Instance, typeArguments);
                break;

            default:
                return null;
        }

        if (ResolveTypeDefinition(module, type.Handle) is not { } definition)
            return null;

        return (new ResolvedType(definition.Module, definition.Handle, type.TypeArguments), type.Name);
    }

    private (Module Module, TypeDefinitionHandle Handle)? ResolveTypeDefinition(Module module, EntityHandle handle)
    {
        switch (handle.Kind)
        {
            case HandleKind.TypeDefinition:
                return (module, (TypeDefinitionHandle)handle);

            case HandleKind.TypeReference:
                {
                    var reader = module.Reader;
                    var fullName = GetTypeFullName(reader, handle);

                    // A nested type is declared in the same module as its containing type
                    var typeReference = reader.GetTypeReference((TypeReferenceHandle)handle);
                    while (typeReference.ResolutionScope.Kind == HandleKind.TypeReference)
                    {
                        typeReference = reader.GetTypeReference((TypeReferenceHandle)typeReference.ResolutionScope);
                    }

                    var declaringModule = typeReference.ResolutionScope.Kind switch
                    {
                        HandleKind.AssemblyReference => GetModule(reader.GetString(reader.GetAssemblyReference((AssemblyReferenceHandle)typeReference.ResolutionScope).Name)),
                        HandleKind.ModuleDefinition => module,
                        _ => null,
                    };

                    if (FindType(declaringModule, fullName, depth: 0) is not { } result)
                        return null;

                    return (result.Module, result.Handle);
                }

            default:
                return null;
        }
    }

    private ResolvedType? FindType(Module? module, string fullName, int depth)
    {
        if (module is null || depth > MaxForwarderDepth)
            return null;

        if (module.Types.TryGetValue(fullName, out var handle))
            return new ResolvedType(module, handle, TypeArguments: default);

        // The type forwarders of a nested type refer to the type forwarder of its containing type
        var separatorIndex = fullName.IndexOf('+', StringComparison.Ordinal);
        var topLevelTypeName = separatorIndex < 0 ? fullName : fullName[..separatorIndex];
        if (module.TypeForwarders.TryGetValue(topLevelTypeName, out var assemblyName))
            return FindType(GetModule(assemblyName), fullName, depth + 1);

        return null;
    }

    private Module? GetModule(string assemblyName)
    {
        if (_modules.TryGetValue(assemblyName, out var module))
            return module;

        if (_assemblyDirectory is not null)
        {
            module = OpenModule(Path.Combine(_assemblyDirectory, assemblyName + ".dll")) ?? OpenModule(Path.Combine(_assemblyDirectory, assemblyName + ".exe"));
        }

        if (module is null && PlatformAssemblies.Value.TryGetValue(assemblyName, out var path))
        {
            module = OpenModule(path);
        }

        _modules.Add(assemblyName, module);
        return module;
    }

    private Module? OpenModule(string path)
    {
        if (!File.Exists(path))
            return null;

        PEReader? peReader = null;
        try
        {
            var stream = File.OpenRead(path);
            try
            {
                peReader = new PEReader(stream);
            }
            catch
            {
                stream.Dispose();
                throw;
            }

            if (peReader.HasMetadata)
            {
                var module = new Module(peReader.GetMetadataReader());
                _peReaders.Add(peReader);
                peReader = null;
                return module;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or BadImageFormatException)
        {
        }
        finally
        {
            peReader?.Dispose();
        }

        return null;
    }

    // The assemblies of the runtime that runs the generator, which is how reflection resolves the base types
    private static Dictionary<string, string> GetPlatformAssemblies()
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") is string platformAssemblies)
        {
            foreach (var path in platformAssemblies.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            {
                result.TryAdd(Path.GetFileNameWithoutExtension(path), path);
            }
        }

        return result;
    }

    // Returns the full name of a TypeDef or a TypeRef, using '+' for nested types
    private static string GetTypeFullName(MetadataReader reader, EntityHandle handle)
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

            default:
                return string.Empty;
        }

        var name = reader.GetString(nameHandle);
        if (!declaringType.IsNil)
            return GetTypeFullName(reader, declaringType) + "+" + name;

        var namespaceName = namespaceHandle.IsNil ? string.Empty : reader.GetString(namespaceHandle);
        return namespaceName.Length == 0 ? name : namespaceName + "." + name;
    }

    private readonly record struct ResolvedType(Module Module, TypeDefinitionHandle Handle, ImmutableArray<string> TypeArguments);

    // A type of a signature: its canonical name, and the handle and the type arguments of a named type
    private readonly record struct SignatureType(string Name, EntityHandle Handle = default, ImmutableArray<string> TypeArguments = default, bool IsByReference = false);

    private sealed class Module(MetadataReader reader)
    {
        public MetadataReader Reader { get; } = reader;

        public Dictionary<string, TypeDefinitionHandle> Types
        {
            get
            {
                if (field is null)
                {
                    field = new Dictionary<string, TypeDefinitionHandle>(StringComparer.Ordinal);
                    foreach (var typeDefinitionHandle in Reader.TypeDefinitions)
                    {
                        field.TryAdd(GetTypeFullName(Reader, typeDefinitionHandle), typeDefinitionHandle);
                    }
                }

                return field;
            }
        }

        // Full name of the top-level types that are forwarded to another assembly
        public Dictionary<string, string> TypeForwarders
        {
            get
            {
                if (field is null)
                {
                    field = new Dictionary<string, string>(StringComparer.Ordinal);
                    foreach (var exportedTypeHandle in Reader.ExportedTypes)
                    {
                        var exportedType = Reader.GetExportedType(exportedTypeHandle);
                        if (!exportedType.IsForwarder || exportedType.Implementation.Kind != HandleKind.AssemblyReference)
                            continue;

                        var name = Reader.GetString(exportedType.Name);
                        var namespaceName = exportedType.Namespace.IsNil ? string.Empty : Reader.GetString(exportedType.Namespace);
                        var assemblyName = Reader.GetString(Reader.GetAssemblyReference((AssemblyReferenceHandle)exportedType.Implementation).Name);
                        field.TryAdd(namespaceName.Length == 0 ? name : namespaceName + "." + name, assemblyName);
                    }
                }

                return field;
            }
        }
    }

    // The generic context contains the type arguments of the declaring type. When it is not set, the generic parameters are left as is.
    private sealed class SignatureTypeProvider : ISignatureTypeProvider<SignatureType, ImmutableArray<string>>
    {
        public static SignatureTypeProvider Instance { get; } = new();

        public SignatureType GetPrimitiveType(PrimitiveTypeCode typeCode) => new(InheritedMemberSet.GetPrimitiveTypeName(typeCode));

        public SignatureType GetTypeFromDefinition(MetadataReader reader, TypeDefinitionHandle handle, byte rawTypeKind) => new(GetTypeFullName(reader, handle), handle);

        public SignatureType GetTypeFromReference(MetadataReader reader, TypeReferenceHandle handle, byte rawTypeKind) => new(GetTypeFullName(reader, handle), handle);

        public SignatureType GetTypeFromSpecification(MetadataReader reader, ImmutableArray<string> genericContext, TypeSpecificationHandle handle, byte rawTypeKind)
        {
            return reader.GetTypeSpecification(handle).DecodeSignature(this, genericContext);
        }

        public SignatureType GetGenericInstantiation(SignatureType genericType, ImmutableArray<SignatureType> typeArguments)
        {
            var typeArgumentNames = typeArguments.Select(type => type.Name).ToImmutableArray();
            return new(InheritedMemberSet.GetGenericInstantiationName(genericType.Name, typeArgumentNames), genericType.Handle, typeArgumentNames);
        }

        public SignatureType GetGenericTypeParameter(ImmutableArray<string> genericContext, int index)
        {
            return new(!genericContext.IsDefault && index < genericContext.Length ? genericContext[index] : InheritedMemberSet.GetGenericTypeParameterName(index));
        }

        public SignatureType GetGenericMethodParameter(ImmutableArray<string> genericContext, int index) => new(InheritedMemberSet.GetGenericMethodParameterName(index));

        public SignatureType GetSZArrayType(SignatureType elementType) => new(InheritedMemberSet.GetArrayName(elementType.Name, rank: 1, isSZArray: true));

        public SignatureType GetArrayType(SignatureType elementType, ArrayShape shape) => new(InheritedMemberSet.GetArrayName(elementType.Name, shape.Rank, isSZArray: false));

        public SignatureType GetByReferenceType(SignatureType elementType) => new(InheritedMemberSet.GetByReferenceName(elementType.Name), IsByReference: true);

        public SignatureType GetPointerType(SignatureType elementType) => new(InheritedMemberSet.GetPointerName(elementType.Name));

        public SignatureType GetFunctionPointerType(MethodSignature<SignatureType> signature)
        {
            var isUnmanaged = signature.Header.CallingConvention != SignatureCallingConvention.Default;
            return new(InheritedMemberSet.GetFunctionPointerName(signature.ReturnType.Name, signature.ParameterTypes.Select(type => type.Name), isUnmanaged));
        }

        // Custom modifiers are not part of the signature the compiler uses to find hidden members
        public SignatureType GetModifiedType(SignatureType modifier, SignatureType unmodifiedType, bool isRequired) => unmodifiedType;

        public SignatureType GetPinnedType(SignatureType elementType) => elementType;
    }
}
