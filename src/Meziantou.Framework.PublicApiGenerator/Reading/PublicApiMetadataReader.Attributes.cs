using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;

namespace Meziantou.Framework.PublicApiGenerator;

internal sealed partial class PublicApiMetadataReader
{
    private const string NullableAttributeFullName = "System.Runtime.CompilerServices.NullableAttribute";
    private const string NullableContextAttributeFullName = "System.Runtime.CompilerServices.NullableContextAttribute";
    private const string TupleElementNamesAttributeFullName = "System.Runtime.CompilerServices.TupleElementNamesAttribute";

    private ImmutableArray<PublicApiAttribute> ReadAttributes(CustomAttributeHandleCollection attributes)
    {
        if (attributes.Count == 0)
            return [];

        var result = ImmutableArray.CreateBuilder<PublicApiAttribute>(attributes.Count);
        foreach (var attributeHandle in attributes)
        {
            var attribute = _metadataReader.GetCustomAttribute(attributeHandle);
            if (GetAttributeType(attribute) is not { } attributeType)
                continue;

            if (TryDecodeAttributeArguments(attribute, out var constructorArguments, out var namedArguments))
            {
                result.Add(new PublicApiAttribute(attributeType, constructorArguments, namedArguments, areArgumentsDecoded: true));
            }
            else
            {
                result.Add(new PublicApiAttribute(attributeType, [], [], areArgumentsDecoded: false));
            }
        }

        return result.ToImmutable();
    }

    private PublicApiNamedTypeReference? GetAttributeType(CustomAttribute attribute)
    {
        EntityHandle typeHandle;
        switch (attribute.Constructor.Kind)
        {
            case HandleKind.MethodDefinition:
                typeHandle = _metadataReader.GetMethodDefinition((MethodDefinitionHandle)attribute.Constructor).GetDeclaringType();
                break;

            case HandleKind.MemberReference:
                typeHandle = _metadataReader.GetMemberReference((MemberReferenceHandle)attribute.Constructor).Parent;
                break;

            default:
                return null;
        }

        return TypeReferenceBuilder.Build(DecodeTypeFromEntityHandle(typeHandle, genericContext: default)) as PublicApiNamedTypeReference;
    }

    private bool TryDecodeAttributeArguments(CustomAttribute attribute, out ImmutableArray<PublicApiAttributeArgument> constructorArguments, out ImmutableArray<PublicApiAttributeNamedArgument> namedArguments)
    {
        CustomAttributeValue<RawType> value;
        try
        {
            value = attribute.DecodeValue(_typeProvider);
        }
        catch (BadImageFormatException)
        {
            constructorArguments = [];
            namedArguments = [];
            return false;
        }

        constructorArguments = [.. value.FixedArguments.Select(ConvertAttributeArgument)];
        namedArguments = [.. value.NamedArguments.Select(argument => new PublicApiAttributeNamedArgument(
            argument.Name ?? string.Empty,
            argument.Kind == CustomAttributeNamedArgumentKind.Field ? PublicApiAttributeNamedArgumentKind.Field : PublicApiAttributeNamedArgumentKind.Property,
            ConvertAttributeArgument(new CustomAttributeTypedArgument<RawType>(argument.Type, argument.Value))))];
        return true;
    }

    private PublicApiAttributeArgument ConvertAttributeArgument(CustomAttributeTypedArgument<RawType> argument)
    {
        if (argument.Value is CustomAttributeTypedArgument<RawType> boxedValue)
            return ConvertAttributeArgument(boxedValue);

        var type = TypeReferenceBuilder.Build(argument.Type);
        switch (argument.Value)
        {
            case ImmutableArray<CustomAttributeTypedArgument<RawType>> values:
                return new PublicApiAttributeArgument(type, PublicApiAttributeArgumentKind.Array, values.Select(ConvertAttributeArgument).ToImmutableArray());

            case RawType typeValue:
                return new PublicApiAttributeArgument(type, PublicApiAttributeArgumentKind.Type, TypeReferenceBuilder.Build(typeValue));
        }

        if (argument.Type is RawType.Array)
            return new PublicApiAttributeArgument(type, PublicApiAttributeArgumentKind.Array, value: null);

        if (_typeProvider.IsSystemType(argument.Type))
            return new PublicApiAttributeArgument(type, PublicApiAttributeArgumentKind.Type, value: null);

        if (argument.Value is not null && argument.Type is RawType.Named named && IsEnumType(named))
            return new PublicApiAttributeArgument(type, PublicApiAttributeArgumentKind.Enum, argument.Value, GetEnumMemberNames(named, argument.Value));

        return new PublicApiAttributeArgument(type, PublicApiAttributeArgumentKind.Constant, argument.Value);
    }

    private bool IsEnumType(RawType.Named type)
    {
        if (type.IsPrimitive || type.IsSystemType("String") || type.IsSystemType("Object") || type.IsSystemType("Type"))
            return false;

        if (TryGetTypeDefinition(type, out var typeDefinitionHandle))
        {
            var typeDefinition = _metadataReader.GetTypeDefinition(typeDefinitionHandle);
            return string.Equals(GetTypeFullName(typeDefinition.BaseType), "System.Enum", StringComparison.Ordinal);
        }

        // Types used as attribute arguments are primitive types, string, Type, enums or arrays.
        // A type declared in another assembly cannot be resolved, so it is assumed to be an enum.
        return true;
    }

    private ImmutableArray<string> GetEnumMemberNames(RawType.Named enumType, object value)
    {
        if (!EnumMetadata.TryGetValueBits(value, out var valueBits))
            return [];

        EnumMetadata? enumMetadata = null;
        if (TryGetTypeDefinition(enumType, out var typeDefinitionHandle))
        {
            enumMetadata = EnumMetadata.Create(_metadataReader, typeDefinitionHandle);
        }

        enumMetadata ??= EnumMetadata.FromLoadedAssemblies(enumType.FullName);
        return enumMetadata?.GetMemberNames(valueBits) ?? [];
    }

    private PrimitiveTypeCode GetUnderlyingEnumType(RawType type)
    {
        _enumUnderlyingTypes ??= BuildEnumUnderlyingTypeMap();
        return type is RawType.Named named && _enumUnderlyingTypes.TryGetValue(named.FullName, out var typeCode)
            ? typeCode
            : PrimitiveTypeCode.Int32;
    }

    private Dictionary<string, PrimitiveTypeCode> BuildEnumUnderlyingTypeMap()
    {
        var result = new Dictionary<string, PrimitiveTypeCode>(StringComparer.Ordinal);
        foreach (var typeDefinitionHandle in _metadataReader.TypeDefinitions)
        {
            var typeDefinition = _metadataReader.GetTypeDefinition(typeDefinitionHandle);
            if (!string.Equals(GetTypeFullName(typeDefinition.BaseType), "System.Enum", StringComparison.Ordinal))
                continue;

            if (EnumMetadata.TryGetUnderlyingType(_metadataReader, typeDefinition, out var typeCode))
            {
                result.TryAdd(GetTypeDefinitionFullName(typeDefinitionHandle), typeCode);
            }
        }

        return result;
    }

    private NullableMetadataInfo GetNullableMetadataInfo(
        TypeDefinitionHandle declaringTypeHandle,
        CustomAttributeHandleCollection? targetAttributes,
        MethodDefinitionHandle methodHandle = default,
        CustomAttributeHandleCollection? secondaryTargetAttributes = null)
    {
        if (!TryGetNullableFlags(targetAttributes, out var nullableFlags) && secondaryTargetAttributes is not null)
        {
            _ = TryGetNullableFlags(secondaryTargetAttributes.Value, out nullableFlags);
        }

        // The closest NullableContextAttribute applies, even when its value is 0 (oblivious)
        if (!TryGetNullableContextFlag(targetAttributes, out var nullableContextFlag) &&
            !(secondaryTargetAttributes is not null && TryGetNullableContextFlag(secondaryTargetAttributes.Value, out nullableContextFlag)) &&
            !(!methodHandle.IsNil && TryGetNullableContextFlag(_metadataReader.GetMethodDefinition(methodHandle).GetCustomAttributes(), out nullableContextFlag)))
        {
            var found = false;
            var currentTypeHandle = declaringTypeHandle;
            while (!currentTypeHandle.IsNil)
            {
                var type = _metadataReader.GetTypeDefinition(currentTypeHandle);
                if (TryGetNullableContextFlag(type.GetCustomAttributes(), out nullableContextFlag))
                {
                    found = true;
                    break;
                }

                currentTypeHandle = type.GetDeclaringType();
            }

            if (!found && _metadataReader.IsAssembly)
            {
                _ = TryGetNullableContextFlag(_metadataReader.GetAssemblyDefinition().GetCustomAttributes(), out nullableContextFlag);
            }
        }

        return new NullableMetadataInfo(nullableFlags, nullableContextFlag);
    }

    private bool TryGetNullableFlags(CustomAttributeHandleCollection? attributes, out ImmutableArray<byte> nullableFlags)
    {
        if (attributes is not null)
        {
            foreach (var attributeHandle in attributes.Value)
            {
                var attribute = _metadataReader.GetCustomAttribute(attributeHandle);
                if (!string.Equals(GetAttributeTypeFullName(attribute), NullableAttributeFullName, StringComparison.Ordinal))
                    continue;

                if (TryDecodeNullableAttributeFlags(attribute, out nullableFlags))
                    return true;
            }
        }

        nullableFlags = default;
        return false;
    }

    private bool TryGetNullableContextFlag(CustomAttributeHandleCollection? attributes, out byte nullableContextFlag)
    {
        if (attributes is not null)
        {
            foreach (var attributeHandle in attributes.Value)
            {
                var attribute = _metadataReader.GetCustomAttribute(attributeHandle);
                if (!string.Equals(GetAttributeTypeFullName(attribute), NullableContextAttributeFullName, StringComparison.Ordinal))
                    continue;

                var value = _metadataReader.GetBlobBytes(attribute.Value);
                if (value.Length < 5 || value[0] != 1 || value[1] != 0)
                    continue;

                nullableContextFlag = value[2];
                return true;
            }
        }

        nullableContextFlag = 0;
        return false;
    }

    private bool TryDecodeNullableAttributeFlags(CustomAttribute attribute, out ImmutableArray<byte> nullableFlags)
    {
        var value = _metadataReader.GetBlobBytes(attribute.Value);
        if (value.Length < 5 || value[0] != 1 || value[1] != 0)
        {
            nullableFlags = default;
            return false;
        }

        var payload = value.AsSpan(2);
        if (payload.Length == 3)
        {
            nullableFlags = [payload[0]];
            return true;
        }

        if (payload.Length < 7)
        {
            nullableFlags = default;
            return false;
        }

        var count = BinaryPrimitives.ReadInt32LittleEndian(payload);
        if (count <= 0 || payload.Length < 4 + count + 2)
        {
            nullableFlags = default;
            return false;
        }

        nullableFlags = [.. payload.Slice(4, count)];
        return true;
    }

    private ImmutableArray<string?> GetTupleElementNames(CustomAttributeHandleCollection? attributes)
    {
        if (attributes is null)
            return default;

        foreach (var attributeHandle in attributes.Value)
        {
            var attribute = _metadataReader.GetCustomAttribute(attributeHandle);
            if (!string.Equals(GetAttributeTypeFullName(attribute), TupleElementNamesAttributeFullName, StringComparison.Ordinal))
                continue;

            try
            {
                var blobReader = _metadataReader.GetBlobReader(attribute.Value);
                if (blobReader.ReadUInt16() != 1)
                    return default;

                var count = blobReader.ReadInt32();
                if (count < 0)
                    return default;

                var builder = ImmutableArray.CreateBuilder<string?>(count);
                for (var i = 0; i < count; i++)
                {
                    builder.Add(blobReader.ReadSerializedString());
                }

                return builder.MoveToImmutable();
            }
            catch (BadImageFormatException)
            {
                return default;
            }
        }

        return default;
    }

    private object? DecodeConstantValue(Constant constant)
    {
        var value = _metadataReader.GetBlobBytes(constant.Value);
        var span = value.AsSpan();
        return constant.TypeCode switch
        {
            ConstantTypeCode.NullReference => null,
            ConstantTypeCode.Boolean => span[0] != 0,
            ConstantTypeCode.Char => (char)BinaryPrimitives.ReadUInt16LittleEndian(span),
            ConstantTypeCode.SByte => unchecked((sbyte)span[0]),
            ConstantTypeCode.Byte => span[0],
            ConstantTypeCode.Int16 => BinaryPrimitives.ReadInt16LittleEndian(span),
            ConstantTypeCode.UInt16 => BinaryPrimitives.ReadUInt16LittleEndian(span),
            ConstantTypeCode.Int32 => BinaryPrimitives.ReadInt32LittleEndian(span),
            ConstantTypeCode.UInt32 => BinaryPrimitives.ReadUInt32LittleEndian(span),
            ConstantTypeCode.Int64 => BinaryPrimitives.ReadInt64LittleEndian(span),
            ConstantTypeCode.UInt64 => BinaryPrimitives.ReadUInt64LittleEndian(span),
            ConstantTypeCode.Single => BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(span)),
            ConstantTypeCode.Double => BitConverter.Int64BitsToDouble(BinaryPrimitives.ReadInt64LittleEndian(span)),
            ConstantTypeCode.String => Encoding.Unicode.GetString(value),
            _ => null,
        };
    }
}
