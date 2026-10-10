using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;

namespace Meziantou.Framework.PublicApiGenerator;

internal sealed partial class PublicApiMetadataReader
{
    private PublicApiType ReadType(TypeDefinitionHandle typeDefinitionHandle, TypeDefinition typeDefinition, string namespaceName, int declaringTypeGenericParameterCount)
    {
        var metadataName = _metadataReader.GetString(typeDefinition.Name);
        var genericContext = BuildGenericContext(typeDefinitionHandle);
        var typeKind = GetTypeKind(typeDefinition);
        var flags = GetTypeFlags(typeDefinition, typeKind);
        var allGenericParameters = ReadGenericParameters(typeDefinition.GetGenericParameters(), genericContext, typeDefinitionHandle, methodHandle: default);
        var ownGenericParameterCount = Math.Max(0, allGenericParameters.Length - declaringTypeGenericParameterCount);

        PublicApiTypeReference? baseType = null;
        if (!typeDefinition.BaseType.IsNil)
        {
            // The NullableAttribute of a type definition describes its base type
            var nullableInfo = GetNullableMetadataInfo(typeDefinitionHandle, typeDefinition.GetCustomAttributes());
            var typeAttributes = typeDefinition.GetCustomAttributes();
            baseType = TypeReferenceBuilder.Build(DecodeTypeFromEntityHandle(typeDefinition.BaseType, genericContext), nullableInfo, GetTupleElementNames(typeAttributes), GetDynamicFlags(typeAttributes));
        }

        var interfaces = ReadInterfaces(typeDefinitionHandle, typeDefinition, genericContext);

        PublicApiMethod? delegateInvokeMethod = null;
        PublicApiNamedTypeReference? enumUnderlyingType = null;
        ImmutableArray<PublicApiMember> members;
        if (typeKind == PublicApiTypeKind.Delegate)
        {
            var invokeMethodHandle = typeDefinition.GetMethods()
                .FirstOrDefault(handle => string.Equals(_metadataReader.GetString(_metadataReader.GetMethodDefinition(handle).Name), "Invoke", StringComparison.Ordinal));
            if (invokeMethodHandle.IsNil)
                throw new InvalidOperationException("Delegate type must have an Invoke method");

            delegateInvokeMethod = ReadMethod(typeDefinitionHandle, invokeMethodHandle, PublicApiMethodKind.DelegateInvoke, explicitImplementations: default);
            members = [];
        }
        else if (typeKind == PublicApiTypeKind.Enum)
        {
            enumUnderlyingType = ReadEnumUnderlyingType(typeDefinition);
            members = ReadEnumMembers(typeDefinitionHandle, typeDefinition);
        }
        else
        {
            members = ReadMembers(typeDefinitionHandle, typeDefinition);
        }

        var nestedTypes = ImmutableArray.CreateBuilder<PublicApiType>();
        foreach (var nestedTypeHandle in EnumerateNestedTypes(typeDefinitionHandle).OrderBy(handle => _metadataReader.GetString(_metadataReader.GetTypeDefinition(handle).Name), StringComparer.Ordinal))
        {
            nestedTypes.Add(ReadType(nestedTypeHandle, _metadataReader.GetTypeDefinition(nestedTypeHandle), namespaceName, allGenericParameters.Length));
        }

        var unionCaseTypes = flags.IsUnion ? ReadUnionCaseTypes(typeDefinitionHandle, typeDefinition) : [];

        return new PublicApiType(
            namespaceName,
            metadataName,
            GetAccessibility(typeDefinition.Attributes),
            ReadAttributes(typeDefinition.GetCustomAttributes()),
            CreateOrigin(typeDefinitionHandle),
            typeKind,
            flags,
            allGenericParameters,
            ownGenericParameterCount,
            baseType,
            interfaces,
            members,
            nestedTypes.ToImmutable(),
            delegateInvokeMethod,
            enumUnderlyingType,
            unionCaseTypes);
    }

    private PublicApiTypeKind GetTypeKind(TypeDefinition typeDefinition)
    {
        if ((typeDefinition.Attributes & TypeAttributes.ClassSemanticsMask) == TypeAttributes.Interface)
            return PublicApiTypeKind.Interface;

        return GetTypeFullName(typeDefinition.BaseType) switch
        {
            "System.Enum" => PublicApiTypeKind.Enum,
            "System.MulticastDelegate" => PublicApiTypeKind.Delegate,
            "System.ValueType" => PublicApiTypeKind.Struct,
            _ => PublicApiTypeKind.Class,
        };
    }

    private PublicApiTypeFlags GetTypeFlags(TypeDefinition typeDefinition, PublicApiTypeKind typeKind)
    {
        var attributes = typeDefinition.Attributes;
        var customAttributes = typeDefinition.GetCustomAttributes();
        switch (typeKind)
        {
            case PublicApiTypeKind.Class:
                var isStatic = attributes.HasFlag(TypeAttributes.Abstract) && attributes.HasFlag(TypeAttributes.Sealed);
                return new PublicApiTypeFlags(
                    IsStatic: isStatic,
                    IsAbstract: !isStatic && attributes.HasFlag(TypeAttributes.Abstract),
                    IsSealed: !isStatic && attributes.HasFlag(TypeAttributes.Sealed),
                    IsClosed: !isStatic && (HasAttribute(customAttributes, ClosedAttributeFullName) || HasAttribute(customAttributes, IsClosedTypeAttributeFullName)));

            case PublicApiTypeKind.Struct:
                return new PublicApiTypeFlags(
                    IsReadOnly: HasAttribute(customAttributes, "System.Runtime.CompilerServices.IsReadOnlyAttribute"),
                    IsRefLike: HasAttribute(customAttributes, "System.Runtime.CompilerServices.IsByRefLikeAttribute"),
                    IsUnion: IsUnionDeclarationType(typeDefinition));

            default:
                return default;
        }
    }

    private bool IsUnionDeclarationType(TypeDefinition typeDefinition)
    {
        if (!HasAttribute(typeDefinition.GetCustomAttributes(), UnionAttributeFullName))
            return false;

        foreach (var interfaceImplementationHandle in typeDefinition.GetInterfaceImplementations())
        {
            var interfaceImplementation = _metadataReader.GetInterfaceImplementation(interfaceImplementationHandle);
            if (string.Equals(GetTypeFullName(interfaceImplementation.Interface), IUnionInterfaceFullName, StringComparison.Ordinal))
                return true;
        }

        return false;
    }

    private ImmutableArray<PublicApiTypeReference> ReadInterfaces(TypeDefinitionHandle typeDefinitionHandle, TypeDefinition typeDefinition, RawGenericContext genericContext)
    {
        var result = new List<PublicApiTypeReference>();
        foreach (var interfaceImplementationHandle in typeDefinition.GetInterfaceImplementations())
        {
            var interfaceImplementation = _metadataReader.GetInterfaceImplementation(interfaceImplementationHandle);
            if (!IsInterfaceExternallyVisible(interfaceImplementation.Interface))
                continue;

            var nullableInfo = GetNullableMetadataInfo(typeDefinitionHandle, interfaceImplementation.GetCustomAttributes());
            var tupleElementNames = GetTupleElementNames(interfaceImplementation.GetCustomAttributes());
            result.Add(TypeReferenceBuilder.Build(DecodeTypeFromEntityHandle(interfaceImplementation.Interface, genericContext), nullableInfo, tupleElementNames));
        }

        // Sorted so that the model does not depend on the order of the metadata table
        return [.. result.OrderBy(static type => DocumentationIdBuilder.GetTypeName(type), StringComparer.Ordinal)];
    }

    // Interfaces defined in the assembly must be visible from outside it.
    // Interfaces defined in other assemblies cannot be resolved here, so they are kept.
    private bool IsInterfaceExternallyVisible(EntityHandle handle)
    {
        switch (handle.Kind)
        {
            case HandleKind.TypeDefinition:
                return IsExternallyVisible(_metadataReader.GetTypeDefinition((TypeDefinitionHandle)handle));

            case HandleKind.TypeSpecification:
                var typeSpecification = _metadataReader.GetTypeSpecification((TypeSpecificationHandle)handle);
                var blobReader = _metadataReader.GetBlobReader(typeSpecification.Signature);
                if (blobReader.ReadSignatureTypeCode() is not SignatureTypeCode.GenericTypeInstance)
                    return true;

                blobReader.ReadCompressedInteger(); // CLASS or VALUETYPE
                return IsInterfaceExternallyVisible(blobReader.ReadTypeHandle());

            default:
                return true;
        }
    }

    private IEnumerable<TypeDefinitionHandle> EnumerateNestedTypes(TypeDefinitionHandle parentTypeHandle)
    {
        foreach (var typeDefinitionHandle in _metadataReader.GetTypeDefinition(parentTypeHandle).GetNestedTypes())
        {
            var typeDefinition = _metadataReader.GetTypeDefinition(typeDefinitionHandle);
            if (!IsExternallyVisibleNested(typeDefinition.Attributes))
                continue;

            // Types generated by the compiler, such as the extension block declarations, have unspeakable names
            if (_metadataReader.GetString(typeDefinition.Name).Contains('<', StringComparison.Ordinal))
                continue;

            yield return typeDefinitionHandle;
        }
    }

    private PublicApiNamedTypeReference? ReadEnumUnderlyingType(TypeDefinition typeDefinition)
    {
        foreach (var fieldHandle in typeDefinition.GetFields())
        {
            var field = _metadataReader.GetFieldDefinition(fieldHandle);
            if (field.Attributes.HasFlag(FieldAttributes.Static))
                continue;

            if (field.DecodeSignature(_typeProvider, default) is RawType.Named named)
                return TypeReferenceBuilder.BuildNamed(named);
        }

        return null;
    }

    private ImmutableArray<PublicApiMember> ReadEnumMembers(TypeDefinitionHandle typeDefinitionHandle, TypeDefinition typeDefinition)
    {
        var result = ImmutableArray.CreateBuilder<PublicApiMember>();
        foreach (var fieldHandle in typeDefinition.GetFields())
        {
            var field = _metadataReader.GetFieldDefinition(fieldHandle);
            if (!field.Attributes.HasFlag(FieldAttributes.Literal) || field.Attributes.HasFlag(FieldAttributes.SpecialName))
                continue;

            result.Add(ReadField(typeDefinitionHandle, fieldHandle, field, isEnumMember: true));
        }

        return result.ToImmutable();
    }

    private ImmutableArray<PublicApiTypeReference> ReadUnionCaseTypes(TypeDefinitionHandle typeDefinitionHandle, TypeDefinition typeDefinition)
    {
        var result = ImmutableArray.CreateBuilder<PublicApiTypeReference>();
        foreach (var methodHandle in typeDefinition.GetMethods())
        {
            var method = _metadataReader.GetMethodDefinition(methodHandle);
            if (!IsGeneratedUnionCaseConstructor(typeDefinitionHandle, methodHandle, method))
                continue;

            var signature = DecodeMethodSignature(typeDefinitionHandle, methodHandle, method);
            var nullableInfo = GetNullableMetadataInfo(typeDefinitionHandle, GetParameterCustomAttributes(method, sequenceNumber: 1), methodHandle);
            result.Add(TypeReferenceBuilder.Build(signature.Signature.ParameterTypes[0], nullableInfo, GetTupleElementNames(GetParameterCustomAttributes(method, sequenceNumber: 1))));
        }

        return result.ToImmutable();
    }

    internal bool IsGeneratedUnionCaseConstructor(TypeDefinitionHandle declaringTypeHandle, MethodDefinitionHandle methodHandle, MethodDefinition method)
    {
        if (!string.Equals(_metadataReader.GetString(method.Name), ".ctor", StringComparison.Ordinal) ||
            !IsExternallyVisible(method.Attributes))
        {
            return false;
        }

        var signature = DecodeMethodSignature(declaringTypeHandle, methodHandle, method);
        return !method.Attributes.HasFlag(MethodAttributes.Static) &&
               signature.Signature.ParameterTypes.Length == 1 &&
               signature.Signature.ParameterTypes[0] is not RawType.ByReference;
    }

    private ImmutableArray<PublicApiGenericParameter> ReadGenericParameters(GenericParameterHandleCollection genericParameters, RawGenericContext genericContext, TypeDefinitionHandle declaringTypeHandle, MethodDefinitionHandle methodHandle)
    {
        if (genericParameters.Count == 0)
            return [];

        var result = ImmutableArray.CreateBuilder<PublicApiGenericParameter>(genericParameters.Count);
        foreach (var genericParameterHandle in genericParameters)
        {
            var genericParameter = _metadataReader.GetGenericParameter(genericParameterHandle);
            var attributes = genericParameter.Attributes;
            var hasValueTypeConstraint = attributes.HasFlag(GenericParameterAttributes.NotNullableValueTypeConstraint);
            var hasUnmanagedTypeConstraint = hasValueTypeConstraint && HasAttribute(genericParameter.GetCustomAttributes(), "System.Runtime.CompilerServices.IsUnmanagedAttribute");
            var constraintTypes = ImmutableArray.CreateBuilder<PublicApiTypeReference>();
            foreach (var constraintHandle in genericParameter.GetConstraints())
            {
                var constraint = _metadataReader.GetGenericParameterConstraint(constraintHandle);
                var rawType = DecodeTypeFromEntityHandle(constraint.Type, genericContext);

                // The struct constraint is encoded as a System.ValueType constraint
                if (hasValueTypeConstraint && rawType is RawType.Named named && named.IsSystemType("ValueType"))
                    continue;

                var nullableInfo = GetNullableMetadataInfo(declaringTypeHandle, constraint.GetCustomAttributes(), methodHandle);
                constraintTypes.Add(TypeReferenceBuilder.Build(rawType, nullableInfo, GetTupleElementNames(constraint.GetCustomAttributes())));
            }

            var variance = (attributes & GenericParameterAttributes.VarianceMask) switch
            {
                GenericParameterAttributes.Covariant => PublicApiVariance.Covariant,
                GenericParameterAttributes.Contravariant => PublicApiVariance.Contravariant,
                _ => PublicApiVariance.None,
            };

            var genericParameterNullableInfo = GetNullableMetadataInfo(declaringTypeHandle, genericParameter.GetCustomAttributes(), methodHandle);
            var nullableAnnotation = (genericParameterNullableInfo.Flags.IsDefaultOrEmpty ? genericParameterNullableInfo.ContextFlag : genericParameterNullableInfo.Flags[0]) switch
            {
                1 => PublicApiNullableAnnotation.NotAnnotated,
                2 => PublicApiNullableAnnotation.Annotated,
                _ => PublicApiNullableAnnotation.Oblivious,
            };

            result.Add(new PublicApiGenericParameter(
                _metadataReader.GetString(genericParameter.Name),
                genericParameter.Index,
                isMethodTypeParameter: !methodHandle.IsNil,
                variance,
                hasReferenceTypeConstraint: attributes.HasFlag(GenericParameterAttributes.ReferenceTypeConstraint),
                hasValueTypeConstraint,
                hasUnmanagedTypeConstraint,
                hasConstructorConstraint: attributes.HasFlag(GenericParameterAttributes.DefaultConstructorConstraint) && !hasValueTypeConstraint,
                allowsRefLikeType: attributes.HasFlag(AllowByRefLikeGenericParameterConstraint),
                constraintTypes.ToImmutable(),
                nullableAnnotation,
                ReadAttributes(genericParameter.GetCustomAttributes())));
        }

        return result.MoveToImmutable();
    }

    private RawGenericContext BuildGenericContext(TypeDefinitionHandle declaringTypeHandle, MethodDefinitionHandle methodHandle = default)
    {
        var typeParameterNames = declaringTypeHandle.IsNil
            ? []
            : GetGenericParameterNames(_metadataReader.GetTypeDefinition(declaringTypeHandle).GetGenericParameters());
        var methodParameterNames = methodHandle.IsNil
            ? []
            : GetGenericParameterNames(_metadataReader.GetMethodDefinition(methodHandle).GetGenericParameters());
        return new RawGenericContext(typeParameterNames, methodParameterNames);
    }

    private ImmutableArray<string> GetGenericParameterNames(GenericParameterHandleCollection genericParameters)
    {
        if (genericParameters.Count == 0)
            return [];

        var result = ImmutableArray.CreateBuilder<string>(genericParameters.Count);
        foreach (var genericParameterHandle in genericParameters)
        {
            result.Add(_metadataReader.GetString(_metadataReader.GetGenericParameter(genericParameterHandle).Name));
        }

        return result.MoveToImmutable();
    }

    private RawType DecodeTypeFromEntityHandle(EntityHandle handle, RawGenericContext genericContext)
    {
        return handle.Kind switch
        {
            HandleKind.TypeReference => _typeProvider.GetTypeFromReference(_metadataReader, (TypeReferenceHandle)handle, rawTypeKind: 0x12),
            HandleKind.TypeDefinition => _typeProvider.GetTypeFromDefinition(_metadataReader, (TypeDefinitionHandle)handle, rawTypeKind: 0),
            HandleKind.TypeSpecification => _typeProvider.GetTypeFromSpecification(_metadataReader, genericContext, (TypeSpecificationHandle)handle, rawTypeKind: 0),
            _ => _typeProvider.GetPrimitiveType(PrimitiveTypeCode.Object),
        };
    }
}
