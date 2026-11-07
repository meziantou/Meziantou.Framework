#pragma warning disable MA0012 // Invalid metadata is reported with BadImageFormatException, like System.Reflection.Metadata does
using System.Collections.Immutable;
using System.Reflection.Metadata;

namespace Meziantou.Framework.Assertions.Architecture.Reading;

// Decodes the signatures and the custom attribute values of an assembly of the architecture into TypeReference
internal sealed class TypeReferenceProvider : ISignatureTypeProvider<TypeReference, GenericContext>, ICustomAttributeTypeProvider<TypeReference>
{
    private static readonly TypeNameParseOptions SerializedNameParseOptions = new() { MaxNodes = 1024 };

    private readonly ArchitectureModel _architecture;
    private readonly AssemblyImage _image;

    public TypeReferenceProvider(ArchitectureModel architecture, AssemblyImage image)
    {
        _architecture = architecture;
        _image = image;
    }

    public TypeReference GetPrimitiveType(PrimitiveTypeCode typeCode) => _architecture.GetPrimitiveType(typeCode);

    public TypeReference GetTypeFromDefinition(MetadataReader reader, TypeDefinitionHandle handle, byte rawTypeKind) => GetNamedType(handle);

    public TypeReference GetTypeFromReference(MetadataReader reader, TypeReferenceHandle handle, byte rawTypeKind) => GetNamedType(handle);

    public TypeReference GetTypeFromSpecification(MetadataReader reader, GenericContext genericContext, TypeSpecificationHandle handle, byte rawTypeKind)
    {
        return reader.GetTypeSpecification(handle).DecodeSignature(this, genericContext);
    }

    public TypeReference GetSZArrayType(TypeReference elementType) => TypeReference.CreateArray(elementType, rank: 1);

    public TypeReference GetArrayType(TypeReference elementType, ArrayShape shape) => TypeReference.CreateArray(elementType, shape.Rank);

    public TypeReference GetByReferenceType(TypeReference elementType) => TypeReference.CreateByReference(elementType);

    public TypeReference GetPointerType(TypeReference elementType) => TypeReference.CreatePointer(elementType);

    public TypeReference GetPinnedType(TypeReference elementType) => elementType;

    // Modifiers (modreq(IsExternalInit), modopt(IsVolatile)...) are compiler details, not dependencies
    public TypeReference GetModifiedType(TypeReference modifier, TypeReference unmodifiedType, bool isRequired) => unmodifiedType;

    public TypeReference GetGenericInstantiation(TypeReference genericType, ImmutableArray<TypeReference> typeArguments) => TypeReference.CreateGenericInstance(genericType, typeArguments);

    public TypeReference GetGenericTypeParameter(GenericContext genericContext, int index)
    {
        return TypeReference.CreateGenericParameter(index < genericContext.TypeParameters.Length ? genericContext.TypeParameters[index] : "!" + index.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    public TypeReference GetGenericMethodParameter(GenericContext genericContext, int index)
    {
        return TypeReference.CreateGenericParameter(index < genericContext.MethodParameters.Length ? genericContext.MethodParameters[index] : "!!" + index.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    public TypeReference GetFunctionPointerType(MethodSignature<TypeReference> signature) => TypeReference.CreateFunctionPointer([signature.ReturnType, .. signature.ParameterTypes]);

    public TypeReference GetSystemType() => _architecture.SystemType;

    public bool IsSystemType(TypeReference type) => type.Kind == TypeReferenceKind.Named && string.Equals(type.FullName, "System.Type", StringComparison.Ordinal);

    public TypeReference GetTypeFromSerializedName(string name)
    {
        if (!TypeName.TryParse(name.AsSpan().Trim(), out var typeName, SerializedNameParseOptions))
            throw new BadImageFormatException($"The type name '{name}' is not valid.");

        return Convert(typeName);
    }

    public PrimitiveTypeCode GetUnderlyingEnumType(TypeReference type)
    {
        if (_architecture.Resolver.FindType(type.AssemblyName, type.FullName) is not { } resolved)
            throw new BadImageFormatException($"The enum '{type}' cannot be found.");

        var reader = resolved.Image.Reader;
        foreach (var fieldHandle in reader.GetTypeDefinition(resolved.Handle).GetFields())
        {
            var field = reader.GetFieldDefinition(fieldHandle);
            if (field.Attributes.HasFlag(System.Reflection.FieldAttributes.Static))
                continue;

            var blobReader = reader.GetBlobReader(field.Signature);
            blobReader.ReadSignatureHeader();

            // The values of the primitive SignatureTypeCode and PrimitiveTypeCode are the same
            return (PrimitiveTypeCode)blobReader.ReadSignatureTypeCode();
        }

        throw new BadImageFormatException($"The type '{type}' is not an enum.");
    }

    public TypeReference GetNamedType(EntityHandle handle)
    {
        return _image.TypeReferences.GetOrAdd(handle, CreateNamedType);
    }

    private TypeReference CreateNamedType(EntityHandle handle)
    {
        var reader = _image.Reader;
        switch (handle.Kind)
        {
            case HandleKind.TypeDefinition:
                {
                    var definition = reader.GetTypeDefinition((TypeDefinitionHandle)handle);
                    var declaringTypeHandle = definition.GetDeclaringType();
                    var declaringType = declaringTypeHandle.IsNil ? null : GetNamedType(declaringTypeHandle);
                    return TypeReference.CreateNamed(_architecture, GetString(reader, definition.Namespace), reader.GetString(definition.Name), _image.Name, declaringType);
                }

            case HandleKind.TypeReference:
                {
                    var reference = reader.GetTypeReference((TypeReferenceHandle)handle);
                    var scope = reference.ResolutionScope;
                    TypeReference? declaringType = null;
                    string? assemblyName;
                    switch (scope.Kind)
                    {
                        case HandleKind.TypeReference:
                            declaringType = GetNamedType(scope);
                            assemblyName = declaringType.AssemblyName;
                            break;

                        case HandleKind.AssemblyReference:
                            assemblyName = reader.GetString(reader.GetAssemblyReference((AssemblyReferenceHandle)scope).Name);
                            break;

                        case HandleKind.ModuleDefinition or HandleKind.ModuleReference:
                            assemblyName = _image.Name;
                            break;

                        default:
                            assemblyName = null;
                            break;
                    }

                    return TypeReference.CreateNamed(_architecture, GetString(reader, reference.Namespace), reader.GetString(reference.Name), assemblyName, declaringType);
                }

            default:
                throw new BadImageFormatException($"The handle {handle.Kind} is not a type.");
        }
    }

    private TypeReference Convert(TypeName typeName)
    {
        if (typeName.IsArray)
            return TypeReference.CreateArray(Convert(typeName.GetElementType()), typeName.IsSZArray ? 1 : typeName.GetArrayRank());

        if (typeName.IsPointer)
            return TypeReference.CreatePointer(Convert(typeName.GetElementType()));

        if (typeName.IsByRef)
            return TypeReference.CreateByReference(Convert(typeName.GetElementType()));

        if (typeName.IsConstructedGenericType)
            return TypeReference.CreateGenericInstance(ConvertNamed(typeName.GetGenericTypeDefinition(), typeName.AssemblyName?.Name), [.. typeName.GetGenericArguments().Select(Convert)]);

        return ConvertNamed(typeName, typeName.AssemblyName?.Name);
    }

    private TypeReference ConvertNamed(TypeName typeName, string? assemblyName)
    {
        assemblyName ??= typeName.AssemblyName?.Name;
        if (typeName.IsNested)
            return TypeReference.CreateNamed(_architecture, string.Empty, typeName.Name, assemblyName, ConvertNamed(typeName.DeclaringType, assemblyName));

        var fullName = typeName.FullName;
        var separatorIndex = fullName.LastIndexOf(".", StringComparison.Ordinal);
        return TypeReference.CreateNamed(_architecture, separatorIndex < 0 ? string.Empty : fullName[..separatorIndex], separatorIndex < 0 ? fullName : fullName[(separatorIndex + 1)..], assemblyName, declaringType: null);
    }

    private static string GetString(MetadataReader reader, StringHandle handle) => handle.IsNil ? string.Empty : reader.GetString(handle);
}
