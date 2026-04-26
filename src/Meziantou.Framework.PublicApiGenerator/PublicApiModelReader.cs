using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Text;

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
            [.. types.OrderBy(type => type.Namespace, StringComparer.Ordinal)
                     .ThenBy(type => type.QualifiedName, StringComparer.Ordinal)]);
    }

    public static PublicApiModel ReadFromReflection(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);

        var types = assembly.GetExportedTypes().Where(type => type.DeclaringType is null);
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

        if (keyword == "enum")
        {
            return $"{accessibility} enum {typeName}{genericArguments}\n{{\n}}";
        }

        var sb = new StringBuilder();
        sb.Append(accessibility);
        sb.Append(' ');
        sb.Append(keyword);
        sb.Append(' ');
        sb.Append(typeName);
        sb.Append(genericArguments);
        sb.AppendLine();
        sb.AppendLine("{");

        foreach (var member in BuildMembers(metadataReader, typeDefinitionHandle, typeDefinition))
        {
            AppendIndentedLine(sb, 1, member);
        }

        sb.AppendLine("}");
        return sb.ToString().TrimEnd('\r', '\n');
    }

    private static IEnumerable<string> BuildMembers(MetadataReader metadataReader, TypeDefinitionHandle typeDefinitionHandle, TypeDefinition typeDefinition)
    {
        foreach (var fieldHandle in typeDefinition.GetFields())
        {
            var field = metadataReader.GetFieldDefinition(fieldHandle);
            if (!IsExternallyVisible(field.Attributes) || field.Attributes.HasFlag(FieldAttributes.SpecialName))
                continue;

            yield return BuildField(metadataReader, typeDefinitionHandle, field);
        }

        foreach (var propertyHandle in EnumerateProperties(typeDefinition))
        {
            var property = metadataReader.GetPropertyDefinition(propertyHandle);
            var propertyText = BuildProperty(metadataReader, typeDefinitionHandle, property);
            if (propertyText is not null)
            {
                yield return propertyText;
            }
        }

        foreach (var eventHandle in EnumerateEvents(typeDefinition))
        {
            var eventDefinition = metadataReader.GetEventDefinition(eventHandle);
            var eventText = BuildEvent(metadataReader, typeDefinitionHandle, eventDefinition);
            if (eventText is not null)
            {
                yield return eventText;
            }
        }

        foreach (var methodHandle in typeDefinition.GetMethods())
        {
            var method = metadataReader.GetMethodDefinition(methodHandle);
            var name = metadataReader.GetString(method.Name);
            if (!IsExternallyVisible(method.Attributes) || method.Attributes.HasFlag(MethodAttributes.SpecialName) || name.Contains('<', StringComparison.Ordinal))
                continue;

            yield return BuildMethod(metadataReader, typeDefinitionHandle, methodHandle, method, name);
        }
    }

    private static string BuildField(MetadataReader metadataReader, TypeDefinitionHandle declaringTypeHandle, FieldDefinition field)
    {
        var type = DecodeFieldType(metadataReader, field);
        var nullableInfo = GetNullableMetadataInfo(metadataReader, declaringTypeHandle, field.GetCustomAttributes());
        var typeName = ApplyNullableReferenceType(
            type,
            nullableInfo);
        var modifiers = new List<string>();
        var accessibility = GetAccessibility(field.Attributes);
        if (!string.IsNullOrEmpty(accessibility))
        {
            modifiers.Add(accessibility);
        }

        if (field.Attributes.HasFlag(FieldAttributes.Static) && !field.Attributes.HasFlag(FieldAttributes.Literal))
        {
            modifiers.Add("static");
        }

        if (field.Attributes.HasFlag(FieldAttributes.InitOnly))
        {
            modifiers.Add("readonly");
        }

        if (field.Attributes.HasFlag(FieldAttributes.Literal))
        {
            modifiers.Add("const");
        }

        var declaration = $"{string.Join(' ', modifiers)} {typeName} {metadataReader.GetString(field.Name)}";
        if (field.Attributes.HasFlag(FieldAttributes.Literal) && !field.GetDefaultValue().IsNil)
        {
            declaration += " = " + FormatConstant(metadataReader.GetConstant(field.GetDefaultValue()).Value);
        }

        declaration += ";";
        return declaration;
    }

    private static string? BuildProperty(MetadataReader metadataReader, TypeDefinitionHandle declaringTypeHandle, PropertyDefinition property)
    {
        var accessors = property.GetAccessors();
        var getAccessorHandle = accessors.Getter;
        var setAccessorHandle = accessors.Setter;
        var getAccessor = getAccessorHandle.IsNil ? default(MethodDefinition?) : metadataReader.GetMethodDefinition(getAccessorHandle);
        var setAccessor = setAccessorHandle.IsNil ? default(MethodDefinition?) : metadataReader.GetMethodDefinition(setAccessorHandle);

        var isGetVisible = getAccessor is not null && IsExternallyVisible(getAccessor.Value.Attributes);
        var isSetVisible = setAccessor is not null && IsExternallyVisible(setAccessor.Value.Attributes);
        if (!isGetVisible && !isSetVisible)
            return null;

        var representativeAttributes = isGetVisible && isSetVisible
            ? GetAccessibilityRank(getAccessor!.Value.Attributes) >= GetAccessibilityRank(setAccessor!.Value.Attributes) ? getAccessor.Value.Attributes : setAccessor.Value.Attributes
            : isGetVisible ? getAccessor!.Value.Attributes : setAccessor!.Value.Attributes;

        var modifiers = new List<string>();
        var accessibility = GetAccessibility(representativeAttributes);
        if (!string.IsNullOrEmpty(accessibility))
        {
            modifiers.Add(accessibility);
        }

        if (representativeAttributes.HasFlag(MethodAttributes.Static))
        {
            modifiers.Add("static");
        }

        if (HasRequiredMemberAttribute(metadataReader, property.GetCustomAttributes()))
        {
            modifiers.Add("required");
        }

        var propertyNullableInfo = default(NullableMetadataInfo);
        if (isGetVisible && !getAccessorHandle.IsNil && getAccessor is MethodDefinition getter)
        {
            propertyNullableInfo = GetNullableMetadataInfo(
                metadataReader,
                declaringTypeHandle,
                GetParameterCustomAttributes(metadataReader, getter, sequenceNumber: 0),
                getAccessorHandle,
                property.GetCustomAttributes());
        }
        else if (isSetVisible && !setAccessorHandle.IsNil && setAccessor is MethodDefinition setter)
        {
            propertyNullableInfo = GetNullableMetadataInfo(
                metadataReader,
                declaringTypeHandle,
                GetParameterCustomAttributes(metadataReader, setter, sequenceNumber: 1),
                setAccessorHandle,
                property.GetCustomAttributes());
        }
        else
        {
            propertyNullableInfo = GetNullableMetadataInfo(metadataReader, declaringTypeHandle, property.GetCustomAttributes());
        }

        var propertyType = ApplyNullableReferenceType(DecodePropertyType(metadataReader, property), propertyNullableInfo);
        var propertyName = metadataReader.GetString(property.Name);
        var accessorText = new List<string>();
        if (isGetVisible)
        {
            var accessorModifier = BuildAccessorModifier(getAccessor!.Value.Attributes, representativeAttributes);
            var accessorBody = getAccessor.Value.Attributes.HasFlag(MethodAttributes.Abstract) ? "get;" : "get => throw null;";
            accessorText.Add(accessorModifier + accessorBody);
        }

        if (isSetVisible)
        {
            var setterSignature = DecodeMethodSignature(metadataReader, setAccessor!.Value);
            var accessorKeyword = setterSignature.ContainsIsExternalInitModifier ? "init" : "set";
            var accessorModifier = BuildAccessorModifier(setAccessor.Value.Attributes, representativeAttributes);
            var accessorBody = setAccessor.Value.Attributes.HasFlag(MethodAttributes.Abstract)
                ? accessorKeyword + ";"
                : accessorKeyword + " => throw null;";
            accessorText.Add(accessorModifier + accessorBody);
        }

        return $"{string.Join(' ', modifiers)} {propertyType} {propertyName} {{ {string.Join(' ', accessorText)} }}";
    }

    private static string? BuildEvent(MetadataReader metadataReader, TypeDefinitionHandle declaringTypeHandle, EventDefinition eventDefinition)
    {
        var accessors = eventDefinition.GetAccessors();
        if (accessors.Adder.IsNil)
            return null;

        var addMethod = metadataReader.GetMethodDefinition(accessors.Adder);
        if (!IsExternallyVisible(addMethod.Attributes))
            return null;

        var modifiers = new List<string>();
        var accessibility = GetAccessibility(addMethod.Attributes);
        if (!string.IsNullOrEmpty(accessibility))
        {
            modifiers.Add(accessibility);
        }

        if (addMethod.Attributes.HasFlag(MethodAttributes.Static))
        {
            modifiers.Add("static");
        }

        var eventNullableInfo = GetNullableMetadataInfo(
            metadataReader,
            declaringTypeHandle,
            GetParameterCustomAttributes(metadataReader, addMethod, sequenceNumber: 1),
            accessors.Adder,
            eventDefinition.GetCustomAttributes());
        var addMethodSignature = DecodeMethodSignature(metadataReader, addMethod);
        var eventType = ApplyNullableReferenceType(addMethodSignature.Signature.ParameterTypes[0], eventNullableInfo);
        var eventName = metadataReader.GetString(eventDefinition.Name);
        return $"{string.Join(' ', modifiers)} event {eventType} {eventName};";
    }

    private static string BuildMethod(MetadataReader metadataReader, TypeDefinitionHandle declaringTypeHandle, MethodDefinitionHandle methodHandle, MethodDefinition method, string name)
    {
        var signature = DecodeMethodSignature(metadataReader, method);
        var modifiers = BuildMethodModifiers(method.Attributes);
        var returnType = ApplyNullableReferenceType(
            signature.Signature.ReturnType,
            GetNullableMetadataInfo(metadataReader, declaringTypeHandle, GetParameterCustomAttributes(metadataReader, method, sequenceNumber: 0), methodHandle));
        var parameters = BuildParameters(metadataReader, declaringTypeHandle, methodHandle, method, signature.Signature.ParameterTypes);
        var methodBody = method.Attributes.HasFlag(MethodAttributes.Abstract) ? ";" : " => throw null;";
        return $"{string.Join(' ', modifiers)} {returnType} {name}({parameters}){methodBody}";
    }

    private static string BuildParameters(MetadataReader metadataReader, TypeDefinitionHandle declaringTypeHandle, MethodDefinitionHandle methodHandle, MethodDefinition method, ImmutableArray<DecodedType> parameterTypes)
    {
        var parametersBySequence = method.GetParameters()
            .ToDictionary(
                parameterHandle => metadataReader.GetParameter(parameterHandle).SequenceNumber,
                parameterHandle => parameterHandle);

        var result = new string[parameterTypes.Length];
        for (var i = 0; i < parameterTypes.Length; i++)
        {
            var sequence = i + 1;
            string parameterName;
            CustomAttributeHandleCollection? parameterAttributes;
            if (parametersBySequence.TryGetValue(sequence, out var parameterHandle))
            {
                var parameter = metadataReader.GetParameter(parameterHandle);
                parameterName = parameter.Name.IsNil
                    ? "arg" + sequence.ToString(System.Globalization.CultureInfo.InvariantCulture)
                    : metadataReader.GetString(parameter.Name);
                parameterAttributes = parameter.GetCustomAttributes();
            }
            else
            {
                parameterName = "arg" + sequence.ToString(System.Globalization.CultureInfo.InvariantCulture);
                parameterAttributes = null;
            }

            var parameterNullableInfo = GetNullableMetadataInfo(metadataReader, declaringTypeHandle, parameterAttributes, methodHandle);
            result[i] = ApplyNullableReferenceType(parameterTypes[i], parameterNullableInfo) + " " + parameterName;
        }

        return string.Join(", ", result);
    }

    private static List<string> BuildMethodModifiers(MethodAttributes attributes)
    {
        var modifiers = new List<string>();
        var accessibility = GetAccessibility(attributes);
        if (!string.IsNullOrEmpty(accessibility))
        {
            modifiers.Add(accessibility);
        }

        if (attributes.HasFlag(MethodAttributes.Static))
        {
            modifiers.Add("static");
        }

        if (attributes.HasFlag(MethodAttributes.Abstract))
        {
            modifiers.Add("abstract");
            return modifiers;
        }

        if (attributes.HasFlag(MethodAttributes.Virtual) && !attributes.HasFlag(MethodAttributes.Final))
        {
            modifiers.Add("virtual");
        }

        return modifiers;
    }

    private static string BuildAccessorModifier(MethodAttributes accessorAttributes, MethodAttributes representativeAccessorAttributes)
    {
        if (GetAccessibilityRank(accessorAttributes) == GetAccessibilityRank(representativeAccessorAttributes))
            return string.Empty;

        var accessibility = GetAccessibility(accessorAttributes);
        return string.IsNullOrEmpty(accessibility) ? string.Empty : accessibility + " ";
    }

    private static CustomAttributeHandleCollection? GetParameterCustomAttributes(MetadataReader metadataReader, MethodDefinition method, int sequenceNumber)
    {
        foreach (var parameterHandle in method.GetParameters())
        {
            var parameter = metadataReader.GetParameter(parameterHandle);
            if (parameter.SequenceNumber == sequenceNumber)
                return parameter.GetCustomAttributes();
        }

        return null;
    }

    private static NullableMetadataInfo GetNullableMetadataInfo(
        MetadataReader metadataReader,
        TypeDefinitionHandle declaringTypeHandle,
        CustomAttributeHandleCollection? targetAttributes,
        MethodDefinitionHandle methodHandle = default,
        CustomAttributeHandleCollection? secondaryTargetAttributes = null)
    {
        ImmutableArray<byte> nullableFlags = default;
        if (!TryGetNullableFlags(metadataReader, targetAttributes, out nullableFlags) &&
            secondaryTargetAttributes is not null)
        {
            _ = TryGetNullableFlags(metadataReader, secondaryTargetAttributes.Value, out nullableFlags);
        }

        var nullableContextFlag = (byte)0;
        if (!TryGetNullableContextFlag(metadataReader, targetAttributes, out nullableContextFlag) &&
            !(secondaryTargetAttributes is not null && TryGetNullableContextFlag(metadataReader, secondaryTargetAttributes.Value, out nullableContextFlag)))
        {
            if (!methodHandle.IsNil)
            {
                var method = metadataReader.GetMethodDefinition(methodHandle);
                _ = TryGetNullableContextFlag(metadataReader, method.GetCustomAttributes(), out nullableContextFlag);
            }

            if (nullableContextFlag == 0)
            {
                var currentTypeHandle = declaringTypeHandle;
                while (!currentTypeHandle.IsNil)
                {
                    var type = metadataReader.GetTypeDefinition(currentTypeHandle);
                    if (TryGetNullableContextFlag(metadataReader, type.GetCustomAttributes(), out nullableContextFlag))
                        break;

                    currentTypeHandle = type.GetDeclaringType();
                }
            }

            if (nullableContextFlag == 0 && metadataReader.IsAssembly)
            {
                _ = TryGetNullableContextFlag(metadataReader, metadataReader.GetAssemblyDefinition().GetCustomAttributes(), out nullableContextFlag);
            }
        }

        return new NullableMetadataInfo(nullableFlags, nullableContextFlag);
    }

    private static bool TryGetNullableFlags(MetadataReader metadataReader, CustomAttributeHandleCollection? attributes, out ImmutableArray<byte> nullableFlags)
    {
        if (attributes is null)
        {
            nullableFlags = default;
            return false;
        }

        foreach (var attributeHandle in attributes.Value)
        {
            var attribute = metadataReader.GetCustomAttribute(attributeHandle);
            if (!string.Equals(GetAttributeTypeFullName(metadataReader, attribute), "System.Runtime.CompilerServices.NullableAttribute", StringComparison.Ordinal))
                continue;

            if (TryDecodeNullableAttributeFlags(metadataReader, attribute, out nullableFlags))
                return true;
        }

        nullableFlags = default;
        return false;
    }

    private static bool TryGetNullableContextFlag(MetadataReader metadataReader, CustomAttributeHandleCollection? attributes, out byte nullableContextFlag)
    {
        if (attributes is null)
        {
            nullableContextFlag = 0;
            return false;
        }

        foreach (var attributeHandle in attributes.Value)
        {
            var attribute = metadataReader.GetCustomAttribute(attributeHandle);
            if (!string.Equals(GetAttributeTypeFullName(metadataReader, attribute), "System.Runtime.CompilerServices.NullableContextAttribute", StringComparison.Ordinal))
                continue;

            if (TryDecodeNullableContextAttributeFlag(metadataReader, attribute, out nullableContextFlag))
                return true;
        }

        nullableContextFlag = 0;
        return false;
    }

    private static bool TryDecodeNullableAttributeFlags(MetadataReader metadataReader, CustomAttribute attribute, out ImmutableArray<byte> nullableFlags)
    {
        var value = metadataReader.GetBlobBytes(attribute.Value);
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

        var builder = ImmutableArray.CreateBuilder<byte>(count);
        for (var i = 0; i < count; i++)
        {
            builder.Add(payload[4 + i]);
        }

        nullableFlags = builder.MoveToImmutable();
        return true;
    }

    private static bool TryDecodeNullableContextAttributeFlag(MetadataReader metadataReader, CustomAttribute attribute, out byte nullableContextFlag)
    {
        var value = metadataReader.GetBlobBytes(attribute.Value);
        if (value.Length < 5 || value[0] != 1 || value[1] != 0)
        {
            nullableContextFlag = 0;
            return false;
        }

        nullableContextFlag = value[2];
        return true;
    }

    private static string ApplyNullableReferenceType(DecodedType type, NullableMetadataInfo nullableInfo)
    {
        var annotationIndex = 0;
        return FormatDecodedType(type, nullableInfo, ref annotationIndex);
    }

    private static string FormatDecodedType(DecodedType type, NullableMetadataInfo nullableInfo, ref int annotationIndex)
    {
        var consumesAnnotation = type.Kind is not DecodedTypeKind.ByReference and not DecodedTypeKind.Pointer;
        var nullableAnnotation = consumesAnnotation
            ? GetNullableAnnotation(nullableInfo, ref annotationIndex)
            : (byte)0;

        string typeName;
        switch (type.Kind)
        {
            case DecodedTypeKind.GenericInstantiation:
            {
                var genericArguments = new string[type.TypeArguments.Length];
                for (var i = 0; i < genericArguments.Length; i++)
                {
                    genericArguments[i] = FormatDecodedType(type.TypeArguments[i], nullableInfo, ref annotationIndex);
                }

                typeName = type.Name + "<" + string.Join(", ", genericArguments) + ">";
                break;
            }
            case DecodedTypeKind.Array:
                typeName = FormatDecodedType(type.ElementType!, nullableInfo, ref annotationIndex) + "[]";
                break;
            case DecodedTypeKind.Pointer:
                typeName = FormatDecodedType(type.ElementType!, nullableInfo, ref annotationIndex) + "*";
                break;
            case DecodedTypeKind.ByReference:
                typeName = "ref " + FormatDecodedType(type.ElementType!, nullableInfo, ref annotationIndex);
                break;
            default:
                typeName = type.Name;
                break;
        }

        if (type.Kind == DecodedTypeKind.ByReference)
            return typeName;

        if (nullableAnnotation != 2 || !type.IsReferenceType || type.IsTypeParameter || typeName.EndsWith("?", StringComparison.Ordinal))
            return typeName;

        return typeName + "?";
    }

    private static byte GetNullableAnnotation(NullableMetadataInfo nullableInfo, ref int annotationIndex)
    {
        byte annotation;
        if (!nullableInfo.Flags.IsDefaultOrEmpty && annotationIndex < nullableInfo.Flags.Length)
        {
            annotation = nullableInfo.Flags[annotationIndex];
        }
        else
        {
            annotation = nullableInfo.ContextFlag;
        }

        annotationIndex++;
        return annotation == 0 ? nullableInfo.ContextFlag : annotation;
    }

    private static bool HasRequiredMemberAttribute(MetadataReader metadataReader, CustomAttributeHandleCollection attributes)
    {
        foreach (var attributeHandle in attributes)
        {
            var attribute = metadataReader.GetCustomAttribute(attributeHandle);
            if (string.Equals(GetAttributeTypeFullName(metadataReader, attribute), "System.Runtime.CompilerServices.RequiredMemberAttribute", StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static string GetAttributeTypeFullName(MetadataReader metadataReader, CustomAttribute attribute)
    {
        return attribute.Constructor.Kind switch
        {
            HandleKind.MethodDefinition => GetTypeFullName(metadataReader, metadataReader.GetMethodDefinition((MethodDefinitionHandle)attribute.Constructor).GetDeclaringType()),
            HandleKind.MemberReference => GetTypeFullName(metadataReader, metadataReader.GetMemberReference((MemberReferenceHandle)attribute.Constructor).Parent),
            _ => string.Empty,
        };
    }

    private static IEnumerable<PropertyDefinitionHandle> EnumerateProperties(TypeDefinition typeDefinition)
    {
        foreach (var propertyHandle in typeDefinition.GetProperties())
        {
            yield return propertyHandle;
        }
    }

    private static IEnumerable<EventDefinitionHandle> EnumerateEvents(TypeDefinition typeDefinition)
    {
        foreach (var eventHandle in typeDefinition.GetEvents())
        {
            yield return eventHandle;
        }
    }

    private static DecodedMethodSignature DecodeMethodSignature(MetadataReader metadataReader, MethodDefinition method)
    {
        var provider = new MetadataTypeNameProvider();
        var signature = method.DecodeSignature(provider, genericContext: null);
        return new DecodedMethodSignature(signature, provider.ContainsIsExternalInitModifier);
    }

    private static DecodedType DecodeFieldType(MetadataReader metadataReader, FieldDefinition field)
    {
        var provider = new MetadataTypeNameProvider();
        return field.DecodeSignature(provider, genericContext: null);
    }

    private static DecodedType DecodePropertyType(MetadataReader metadataReader, PropertyDefinition property)
    {
        var provider = new MetadataTypeNameProvider();
        var signature = property.DecodeSignature(provider, genericContext: null);
        return signature.ReturnType;
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

    private static bool IsExternallyVisible(MethodAttributes attributes)
    {
        return (attributes & MethodAttributes.MemberAccessMask) is MethodAttributes.Public or MethodAttributes.Family or MethodAttributes.FamORAssem;
    }

    private static bool IsExternallyVisible(FieldAttributes attributes)
    {
        return (attributes & FieldAttributes.FieldAccessMask) is FieldAttributes.Public or FieldAttributes.Family or FieldAttributes.FamORAssem;
    }

    private static string GetAccessibility(TypeAttributes attributes)
    {
        return (attributes & TypeAttributes.VisibilityMask) == TypeAttributes.Public
            ? "public"
            : "internal";
    }

    private static string GetAccessibility(MethodAttributes attributes)
    {
        return (attributes & MethodAttributes.MemberAccessMask) switch
        {
            MethodAttributes.Public => "public",
            MethodAttributes.Family => "protected",
            MethodAttributes.FamORAssem => "protected internal",
            MethodAttributes.Assembly => "internal",
            MethodAttributes.FamANDAssem => "private protected",
            MethodAttributes.Private => "private",
            _ => string.Empty,
        };
    }

    private static string GetAccessibility(FieldAttributes attributes)
    {
        return (attributes & FieldAttributes.FieldAccessMask) switch
        {
            FieldAttributes.Public => "public",
            FieldAttributes.Family => "protected",
            FieldAttributes.FamORAssem => "protected internal",
            FieldAttributes.Assembly => "internal",
            FieldAttributes.FamANDAssem => "private protected",
            FieldAttributes.Private => "private",
            _ => string.Empty,
        };
    }

    private static int GetAccessibilityRank(MethodAttributes attributes)
    {
        return (attributes & MethodAttributes.MemberAccessMask) switch
        {
            MethodAttributes.Public => 6,
            MethodAttributes.FamORAssem => 5,
            MethodAttributes.Family => 4,
            MethodAttributes.Assembly => 3,
            MethodAttributes.FamANDAssem => 2,
            MethodAttributes.Private => 1,
            _ => 0,
        };
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

    private static string FormatConstant(object? value)
    {
        return value switch
        {
            null => "null",
            string text => "\"" + text.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal) + "\"",
            char character => "'" + character.ToString().Replace("\\", "\\\\", StringComparison.Ordinal).Replace("'", "\\'", StringComparison.Ordinal) + "'",
            bool boolValue => boolValue ? "true" : "false",
            _ => Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) ?? "null",
        };
    }

    private static void AppendIndentedLine(StringBuilder sb, int indentationLevel, string line)
    {
        sb.Append(' ', indentationLevel * 4);
        sb.AppendLine(line);
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

    private sealed class MetadataTypeNameProvider : ISignatureTypeProvider<DecodedType, object?>
    {
        private const byte ElementTypeValueType = 0x11;
        private const byte ElementTypeClass = 0x12;

        public MetadataTypeNameProvider()
        {
        }

        public bool ContainsIsExternalInitModifier { get; private set; }

        public DecodedType GetArrayType(DecodedType elementType, ArrayShape shape) => new(elementType.Name, IsReferenceType: true, IsTypeParameter: false, Kind: DecodedTypeKind.Array, ElementType: elementType);

        public DecodedType GetByReferenceType(DecodedType elementType) => new(elementType.Name, IsReferenceType: elementType.IsReferenceType, IsTypeParameter: elementType.IsTypeParameter, Kind: DecodedTypeKind.ByReference, ElementType: elementType);

        public DecodedType GetFunctionPointerType(MethodSignature<DecodedType> signature) => new("delegate*", IsReferenceType: false, IsTypeParameter: false);

        public DecodedType GetGenericInstantiation(DecodedType genericType, ImmutableArray<DecodedType> typeArguments) => new(genericType.Name, genericType.IsReferenceType, IsTypeParameter: false, Kind: DecodedTypeKind.GenericInstantiation, TypeArguments: typeArguments);

        public DecodedType GetGenericMethodParameter(object? genericContext, int index) => new("TMethod" + index.ToString(System.Globalization.CultureInfo.InvariantCulture), IsReferenceType: false, IsTypeParameter: true);

        public DecodedType GetGenericTypeParameter(object? genericContext, int index) => new("T" + index.ToString(System.Globalization.CultureInfo.InvariantCulture), IsReferenceType: false, IsTypeParameter: true);

        public DecodedType GetModifiedType(DecodedType modifier, DecodedType unmodifiedType, bool isRequired)
        {
            if (string.Equals(modifier.Name, "global::System.Runtime.CompilerServices.IsExternalInit", StringComparison.Ordinal))
            {
                ContainsIsExternalInitModifier = true;
            }

            return unmodifiedType;
        }

        public DecodedType GetPinnedType(DecodedType elementType) => elementType;

        public DecodedType GetPointerType(DecodedType elementType) => new(elementType.Name, IsReferenceType: false, IsTypeParameter: false, Kind: DecodedTypeKind.Pointer, ElementType: elementType);

        public DecodedType GetPrimitiveType(PrimitiveTypeCode typeCode)
        {
            return typeCode switch
            {
                PrimitiveTypeCode.Void => new("void", IsReferenceType: false, IsTypeParameter: false),
                PrimitiveTypeCode.Boolean => new("bool", IsReferenceType: false, IsTypeParameter: false),
                PrimitiveTypeCode.Char => new("char", IsReferenceType: false, IsTypeParameter: false),
                PrimitiveTypeCode.SByte => new("sbyte", IsReferenceType: false, IsTypeParameter: false),
                PrimitiveTypeCode.Byte => new("byte", IsReferenceType: false, IsTypeParameter: false),
                PrimitiveTypeCode.Int16 => new("short", IsReferenceType: false, IsTypeParameter: false),
                PrimitiveTypeCode.UInt16 => new("ushort", IsReferenceType: false, IsTypeParameter: false),
                PrimitiveTypeCode.Int32 => new("int", IsReferenceType: false, IsTypeParameter: false),
                PrimitiveTypeCode.UInt32 => new("uint", IsReferenceType: false, IsTypeParameter: false),
                PrimitiveTypeCode.Int64 => new("long", IsReferenceType: false, IsTypeParameter: false),
                PrimitiveTypeCode.UInt64 => new("ulong", IsReferenceType: false, IsTypeParameter: false),
                PrimitiveTypeCode.Single => new("float", IsReferenceType: false, IsTypeParameter: false),
                PrimitiveTypeCode.Double => new("double", IsReferenceType: false, IsTypeParameter: false),
                PrimitiveTypeCode.String => new("string", IsReferenceType: true, IsTypeParameter: false),
                PrimitiveTypeCode.IntPtr => new("nint", IsReferenceType: false, IsTypeParameter: false),
                PrimitiveTypeCode.UIntPtr => new("nuint", IsReferenceType: false, IsTypeParameter: false),
                PrimitiveTypeCode.Object => new("object", IsReferenceType: true, IsTypeParameter: false),
                _ => new("object", IsReferenceType: true, IsTypeParameter: false),
            };
        }

        public DecodedType GetSZArrayType(DecodedType elementType) => new(elementType.Name, IsReferenceType: true, IsTypeParameter: false, Kind: DecodedTypeKind.Array, ElementType: elementType);

        public DecodedType GetTypeFromDefinition(MetadataReader reader, TypeDefinitionHandle handle, byte rawTypeKind)
        {
            var type = reader.GetTypeDefinition(handle);
            var namespaceName = type.Namespace.IsNil ? string.Empty : reader.GetString(type.Namespace);
            var name = RemoveGenericArity(reader.GetString(type.Name));
            return new(
                string.IsNullOrEmpty(namespaceName) ? name : "global::" + namespaceName + "." + name,
                IsReferenceType: rawTypeKind != ElementTypeValueType,
                IsTypeParameter: false);
        }

        public DecodedType GetTypeFromReference(MetadataReader reader, TypeReferenceHandle handle, byte rawTypeKind)
        {
            var type = reader.GetTypeReference(handle);
            var namespaceName = type.Namespace.IsNil ? string.Empty : reader.GetString(type.Namespace);
            var name = RemoveGenericArity(reader.GetString(type.Name));
            return new(
                string.IsNullOrEmpty(namespaceName) ? name : "global::" + namespaceName + "." + name,
                IsReferenceType: rawTypeKind != ElementTypeValueType,
                IsTypeParameter: false);
        }

        public DecodedType GetTypeFromSpecification(MetadataReader reader, object? genericContext, TypeSpecificationHandle handle, byte rawTypeKind)
        {
            var typeSpecification = reader.GetTypeSpecification(handle);
            var decodedType = typeSpecification.DecodeSignature(this, genericContext);
            if (rawTypeKind is ElementTypeClass or ElementTypeValueType)
                return decodedType with { IsReferenceType = rawTypeKind == ElementTypeClass };

            return decodedType;
        }
    }

    private enum DecodedTypeKind
    {
        Simple,
        GenericInstantiation,
        Array,
        ByReference,
        Pointer,
    }

    private sealed record DecodedType(
        string Name,
        bool IsReferenceType,
        bool IsTypeParameter,
        DecodedTypeKind Kind = DecodedTypeKind.Simple,
        ImmutableArray<DecodedType> TypeArguments = default,
        DecodedType? ElementType = null);

    private readonly record struct NullableMetadataInfo(ImmutableArray<byte> Flags, byte ContextFlag);

    private readonly record struct DecodedMethodSignature(MethodSignature<DecodedType> Signature, bool ContainsIsExternalInitModifier);
}
