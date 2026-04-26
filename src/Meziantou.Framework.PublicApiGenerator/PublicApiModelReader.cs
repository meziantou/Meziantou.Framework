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

        foreach (var member in BuildMembers(metadataReader, typeDefinition))
        {
            AppendIndentedLine(sb, 1, member);
        }

        sb.AppendLine("}");
        return sb.ToString().TrimEnd('\r', '\n');
    }

    private static IEnumerable<string> BuildMembers(MetadataReader metadataReader, TypeDefinition typeDefinition)
    {
        foreach (var fieldHandle in typeDefinition.GetFields())
        {
            var field = metadataReader.GetFieldDefinition(fieldHandle);
            if (!IsExternallyVisible(field.Attributes) || field.Attributes.HasFlag(FieldAttributes.SpecialName))
                continue;

            yield return BuildField(metadataReader, fieldHandle, field);
        }

        foreach (var propertyHandle in EnumerateProperties(typeDefinition))
        {
            var property = metadataReader.GetPropertyDefinition(propertyHandle);
            var propertyText = BuildProperty(metadataReader, propertyHandle, property);
            if (propertyText is not null)
            {
                yield return propertyText;
            }
        }

        foreach (var eventHandle in EnumerateEvents(typeDefinition))
        {
            var eventDefinition = metadataReader.GetEventDefinition(eventHandle);
            var eventText = BuildEvent(metadataReader, eventDefinition);
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

            yield return BuildMethod(metadataReader, method, name);
        }
    }

    private static string BuildField(MetadataReader metadataReader, FieldDefinitionHandle fieldHandle, FieldDefinition field)
    {
        var typeName = DecodeFieldType(metadataReader, field);
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

    private static string? BuildProperty(MetadataReader metadataReader, PropertyDefinitionHandle propertyHandle, PropertyDefinition property)
    {
        var accessors = property.GetAccessors();
        var getAccessor = accessors.Getter.IsNil ? default(MethodDefinition?) : metadataReader.GetMethodDefinition(accessors.Getter);
        var setAccessor = accessors.Setter.IsNil ? default(MethodDefinition?) : metadataReader.GetMethodDefinition(accessors.Setter);

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

        var propertyType = DecodePropertyType(metadataReader, property);
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

    private static string? BuildEvent(MetadataReader metadataReader, EventDefinition eventDefinition)
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

        var eventType = FormatTypeFromEntityHandle(metadataReader, eventDefinition.Type);
        var eventName = metadataReader.GetString(eventDefinition.Name);
        return $"{string.Join(' ', modifiers)} event {eventType} {eventName};";
    }

    private static string BuildMethod(MetadataReader metadataReader, MethodDefinition method, string name)
    {
        var signature = DecodeMethodSignature(metadataReader, method);
        var modifiers = BuildMethodModifiers(method.Attributes);
        var parameters = BuildParameters(metadataReader, method, signature.Signature.ParameterTypes);
        var methodBody = method.Attributes.HasFlag(MethodAttributes.Abstract) ? ";" : " => throw null;";
        return $"{string.Join(' ', modifiers)} {signature.Signature.ReturnType} {name}({parameters}){methodBody}";
    }

    private static string BuildParameters(MetadataReader metadataReader, MethodDefinition method, ImmutableArray<string> parameterTypes)
    {
        var parametersBySequence = method.GetParameters()
            .Select(metadataReader.GetParameter)
            .Where(parameter => parameter.SequenceNumber > 0)
            .ToDictionary(parameter => parameter.SequenceNumber, parameter => parameter);

        var result = new string[parameterTypes.Length];
        for (var i = 0; i < parameterTypes.Length; i++)
        {
            var sequence = i + 1;
            var parameter = parametersBySequence.TryGetValue(sequence, out var value) ? value : default(Parameter);
            var parameterName = parameter.Name.IsNil ? "arg" + sequence.ToString(System.Globalization.CultureInfo.InvariantCulture) : metadataReader.GetString(parameter.Name);
            result[i] = parameterTypes[i] + " " + parameterName;
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
        var provider = new MetadataTypeNameProvider(metadataReader);
        var signature = method.DecodeSignature(provider, genericContext: null);
        return new DecodedMethodSignature(signature, provider.ContainsIsExternalInitModifier);
    }

    private static string DecodeFieldType(MetadataReader metadataReader, FieldDefinition field)
    {
        var provider = new MetadataTypeNameProvider(metadataReader);
        return field.DecodeSignature(provider, genericContext: null);
    }

    private static string DecodePropertyType(MetadataReader metadataReader, PropertyDefinition property)
    {
        var provider = new MetadataTypeNameProvider(metadataReader);
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

    private static string FormatTypeFromEntityHandle(MetadataReader metadataReader, EntityHandle handle)
    {
        return handle.Kind switch
        {
            HandleKind.TypeReference => new MetadataTypeNameProvider(metadataReader).GetTypeFromReference(metadataReader, (TypeReferenceHandle)handle, default),
            HandleKind.TypeDefinition => new MetadataTypeNameProvider(metadataReader).GetTypeFromDefinition(metadataReader, (TypeDefinitionHandle)handle, default),
            _ => "object",
        };
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

    private sealed class MetadataTypeNameProvider : ISignatureTypeProvider<string, object?>
    {
        private readonly MetadataReader _metadataReader;

        public MetadataTypeNameProvider(MetadataReader metadataReader)
        {
            _metadataReader = metadataReader;
        }

        public bool ContainsIsExternalInitModifier { get; private set; }

        public string GetArrayType(string elementType, ArrayShape shape) => elementType + "[]";

        public string GetByReferenceType(string elementType) => "ref " + elementType;

        public string GetFunctionPointerType(MethodSignature<string> signature) => "delegate*";

        public string GetGenericInstantiation(string genericType, ImmutableArray<string> typeArguments) => genericType + "<" + string.Join(", ", typeArguments) + ">";

        public string GetGenericMethodParameter(object? genericContext, int index) => "TMethod" + index.ToString(System.Globalization.CultureInfo.InvariantCulture);

        public string GetGenericTypeParameter(object? genericContext, int index) => "T" + index.ToString(System.Globalization.CultureInfo.InvariantCulture);

        public string GetModifiedType(string modifier, string unmodifiedType, bool isRequired)
        {
            if (string.Equals(modifier, "global::System.Runtime.CompilerServices.IsExternalInit", StringComparison.Ordinal))
            {
                ContainsIsExternalInitModifier = true;
            }

            return unmodifiedType;
        }

        public string GetPinnedType(string elementType) => elementType;

        public string GetPointerType(string elementType) => elementType + "*";

        public string GetPrimitiveType(PrimitiveTypeCode typeCode)
        {
            return typeCode switch
            {
                PrimitiveTypeCode.Void => "void",
                PrimitiveTypeCode.Boolean => "bool",
                PrimitiveTypeCode.Char => "char",
                PrimitiveTypeCode.SByte => "sbyte",
                PrimitiveTypeCode.Byte => "byte",
                PrimitiveTypeCode.Int16 => "short",
                PrimitiveTypeCode.UInt16 => "ushort",
                PrimitiveTypeCode.Int32 => "int",
                PrimitiveTypeCode.UInt32 => "uint",
                PrimitiveTypeCode.Int64 => "long",
                PrimitiveTypeCode.UInt64 => "ulong",
                PrimitiveTypeCode.Single => "float",
                PrimitiveTypeCode.Double => "double",
                PrimitiveTypeCode.String => "string",
                PrimitiveTypeCode.IntPtr => "nint",
                PrimitiveTypeCode.UIntPtr => "nuint",
                PrimitiveTypeCode.Object => "object",
                _ => "object",
            };
        }

        public string GetSZArrayType(string elementType) => elementType + "[]";

        public string GetTypeFromDefinition(MetadataReader reader, TypeDefinitionHandle handle, byte rawTypeKind)
        {
            var type = reader.GetTypeDefinition(handle);
            var namespaceName = type.Namespace.IsNil ? string.Empty : reader.GetString(type.Namespace);
            var name = RemoveGenericArity(reader.GetString(type.Name));
            return string.IsNullOrEmpty(namespaceName) ? name : "global::" + namespaceName + "." + name;
        }

        public string GetTypeFromReference(MetadataReader reader, TypeReferenceHandle handle, byte rawTypeKind)
        {
            var type = reader.GetTypeReference(handle);
            var namespaceName = type.Namespace.IsNil ? string.Empty : reader.GetString(type.Namespace);
            var name = RemoveGenericArity(reader.GetString(type.Name));
            return string.IsNullOrEmpty(namespaceName) ? name : "global::" + namespaceName + "." + name;
        }

        public string GetTypeFromSpecification(MetadataReader reader, object? genericContext, TypeSpecificationHandle handle, byte rawTypeKind)
        {
            var typeSpecification = reader.GetTypeSpecification(handle);
            return typeSpecification.DecodeSignature(this, genericContext);
        }
    }

    private readonly record struct DecodedMethodSignature(MethodSignature<string> Signature, bool ContainsIsExternalInitModifier);
}
