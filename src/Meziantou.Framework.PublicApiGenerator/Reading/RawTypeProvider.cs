using System.Collections.Immutable;
using System.Reflection.Metadata;

namespace Meziantou.Framework.PublicApiGenerator;

internal sealed class RawTypeProvider : ISignatureTypeProvider<RawType, RawGenericContext>, ICustomAttributeTypeProvider<RawType>
{
    private const byte ElementTypeValueType = 0x11;
    private const byte ElementTypeClass = 0x12;

    private readonly string? _currentAssemblyName;
    private readonly string? _coreLibraryName;
    private readonly Func<TypeDefinitionHandle, bool> _isValueTypeDefinition;
    private readonly Func<RawType, PrimitiveTypeCode> _getUnderlyingEnumType;

    public RawTypeProvider(string? currentAssemblyName, string? coreLibraryName, Func<TypeDefinitionHandle, bool> isValueTypeDefinition, Func<RawType, PrimitiveTypeCode> getUnderlyingEnumType)
    {
        _currentAssemblyName = currentAssemblyName;
        _coreLibraryName = coreLibraryName;
        _isValueTypeDefinition = isValueTypeDefinition;
        _getUnderlyingEnumType = getUnderlyingEnumType;
    }

    // Set when the IsExternalInit modifier is found while decoding a signature, which identifies init accessors
    public bool ContainsIsExternalInitModifier { get; set; }

    public RawType GetPrimitiveType(PrimitiveTypeCode typeCode)
    {
        var (name, isValueType) = typeCode switch
        {
            PrimitiveTypeCode.Void => ("Void", true),
            PrimitiveTypeCode.Boolean => ("Boolean", true),
            PrimitiveTypeCode.Char => ("Char", true),
            PrimitiveTypeCode.SByte => ("SByte", true),
            PrimitiveTypeCode.Byte => ("Byte", true),
            PrimitiveTypeCode.Int16 => ("Int16", true),
            PrimitiveTypeCode.UInt16 => ("UInt16", true),
            PrimitiveTypeCode.Int32 => ("Int32", true),
            PrimitiveTypeCode.UInt32 => ("UInt32", true),
            PrimitiveTypeCode.Int64 => ("Int64", true),
            PrimitiveTypeCode.UInt64 => ("UInt64", true),
            PrimitiveTypeCode.Single => ("Single", true),
            PrimitiveTypeCode.Double => ("Double", true),
            PrimitiveTypeCode.IntPtr => ("IntPtr", true),
            PrimitiveTypeCode.UIntPtr => ("UIntPtr", true),
            PrimitiveTypeCode.TypedReference => ("TypedReference", true),
            PrimitiveTypeCode.String => ("String", false),
            _ => ("Object", false),
        };

        return new RawType.Named("System", name, ContainingType: null, _coreLibraryName, isValueType, IsPrimitive: true);
    }

    public RawType GetTypeFromDefinition(MetadataReader reader, TypeDefinitionHandle handle, byte rawTypeKind)
    {
        var isValueType = rawTypeKind switch
        {
            ElementTypeValueType => true,
            ElementTypeClass => false,
            _ => _isValueTypeDefinition(handle),
        };

        return GetTypeFromDefinition(reader, handle, isValueType);
    }

    public RawType.Named GetTypeFromDefinition(MetadataReader reader, TypeDefinitionHandle handle, bool isValueType)
    {
        var typeDefinition = reader.GetTypeDefinition(handle);
        var declaringTypeHandle = typeDefinition.GetDeclaringType();
        var containingType = declaringTypeHandle.IsNil ? null : GetTypeFromDefinition(reader, declaringTypeHandle, _isValueTypeDefinition(declaringTypeHandle));
        var namespaceName = typeDefinition.Namespace.IsNil || containingType is not null ? string.Empty : reader.GetString(typeDefinition.Namespace);
        return new RawType.Named(namespaceName, reader.GetString(typeDefinition.Name), containingType, _currentAssemblyName, isValueType);
    }

    public RawType GetTypeFromReference(MetadataReader reader, TypeReferenceHandle handle, byte rawTypeKind)
    {
        return GetTypeFromReference(reader, handle, rawTypeKind == ElementTypeValueType);
    }

    private RawType.Named GetTypeFromReference(MetadataReader reader, TypeReferenceHandle handle, bool isValueType)
    {
        var typeReference = reader.GetTypeReference(handle);
        RawType.Named? containingType = null;
        string? assemblyName;
        var scope = typeReference.ResolutionScope;
        switch (scope.Kind)
        {
            case HandleKind.TypeReference:
                containingType = GetTypeFromReference(reader, (TypeReferenceHandle)scope, isValueType: false);
                assemblyName = containingType.AssemblyName;
                break;

            case HandleKind.AssemblyReference:
                assemblyName = reader.GetString(reader.GetAssemblyReference((AssemblyReferenceHandle)scope).Name);
                break;

            default:
                assemblyName = _currentAssemblyName;
                break;
        }

        var namespaceName = typeReference.Namespace.IsNil || containingType is not null ? string.Empty : reader.GetString(typeReference.Namespace);
        return new RawType.Named(namespaceName, reader.GetString(typeReference.Name), containingType, assemblyName, isValueType);
    }

    public RawType GetTypeFromSpecification(MetadataReader reader, RawGenericContext genericContext, TypeSpecificationHandle handle, byte rawTypeKind)
    {
        var decodedType = reader.GetTypeSpecification(handle).DecodeSignature(this, genericContext);
        if (rawTypeKind is ElementTypeClass or ElementTypeValueType && decodedType is RawType.GenericInstance genericInstance)
        {
            return genericInstance with { Definition = genericInstance.Definition with { IsValueType = rawTypeKind == ElementTypeValueType } };
        }

        return decodedType;
    }

    public RawType GetSZArrayType(RawType elementType) => new RawType.Array(elementType, Rank: 1, IsSZArray: true);

    public RawType GetArrayType(RawType elementType, ArrayShape shape) => new RawType.Array(elementType, shape.Rank, IsSZArray: false);

    public RawType GetByReferenceType(RawType elementType) => new RawType.ByReference(elementType);

    public RawType GetPointerType(RawType elementType) => new RawType.Pointer(elementType);

    public RawType GetPinnedType(RawType elementType) => elementType;

    public RawType GetFunctionPointerType(MethodSignature<RawType> signature) => new RawType.FunctionPointer(signature);

    public RawType GetGenericInstantiation(RawType genericType, ImmutableArray<RawType> typeArguments)
    {
        var definition = genericType as RawType.Named ?? new RawType.Named("System", "Object", ContainingType: null, _coreLibraryName, IsValueType: false);
        return new RawType.GenericInstance(definition, typeArguments);
    }

    public RawType GetGenericMethodParameter(RawGenericContext genericContext, int index) => new RawType.TypeParameter(genericContext.GetMethodParameterName(index), index, IsMethodTypeParameter: true);

    public RawType GetGenericTypeParameter(RawGenericContext genericContext, int index) => new RawType.TypeParameter(genericContext.GetTypeParameterName(index), index, IsMethodTypeParameter: false);

    public RawType GetModifiedType(RawType modifier, RawType unmodifiedType, bool isRequired)
    {
        if (modifier is not RawType.Named { ContainingType: null } modifierType)
            return unmodifiedType;

        switch (modifierType.Namespace, modifierType.MetadataName)
        {
            case ("System.Runtime.CompilerServices", "IsExternalInit"):
                ContainsIsExternalInitModifier = true;
                return unmodifiedType;

            case ("System.Runtime.InteropServices", "InAttribute"):
                return unmodifiedType with { HasInModifier = true };

            case ("System.Runtime.InteropServices", "OutAttribute"):
                return unmodifiedType with { HasOutModifier = true };

            case ("System.Runtime.CompilerServices", "IsReadOnlyAttribute"):
                return unmodifiedType with { HasIsReadOnlyModifier = true };

            case ("System.Runtime.CompilerServices", "RequiresLocationAttribute"):
                return unmodifiedType with { HasRequiresLocationModifier = true };

            case ("System.Runtime.CompilerServices", "IsVolatile"):
                return unmodifiedType with { HasIsVolatileModifier = true };

            default:
                return unmodifiedType;
        }
    }

    public RawType GetSystemType() => new RawType.Named("System", "Type", ContainingType: null, _coreLibraryName, IsValueType: false);

    public bool IsSystemType(RawType type) => type is RawType.Named named && named.IsSystemType("Type");

    public RawType GetTypeFromSerializedName(string name)
    {
        return SerializedTypeNameParser.Parse(name) ?? new RawType.Named(string.Empty, name, ContainingType: null, AssemblyName: null, IsValueType: false, IsFromSerializedName: true);
    }

    public PrimitiveTypeCode GetUnderlyingEnumType(RawType type) => _getUnderlyingEnumType(type);
}
