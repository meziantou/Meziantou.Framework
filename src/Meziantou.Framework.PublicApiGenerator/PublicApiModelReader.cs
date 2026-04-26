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
    private const string CompilerGeneratedRefStructObsoleteMessage = "Types with embedded references are not supported in this version of your compiler.";
    private const GenericParameterAttributes AllowByRefLikeGenericParameterConstraint = (GenericParameterAttributes)0x20;

    private static readonly HashSet<string> IrrelevantAttributes = new(StringComparer.Ordinal)
    {
        "System.CodeDom.Compiler.GeneratedCodeAttribute",
        "System.ComponentModel.EditorBrowsableAttribute",
        "System.Runtime.CompilerServices.AsyncStateMachineAttribute",
        "System.Runtime.CompilerServices.CompilerGeneratedAttribute",
        "System.Runtime.CompilerServices.CompilationRelaxationsAttribute",
        "System.Runtime.CompilerServices.ExtensionAttribute",
        "System.Runtime.CompilerServices.ExtensionMarkerAttribute",
        "System.Runtime.CompilerServices.RuntimeCompatibilityAttribute",
        "System.Runtime.CompilerServices.IteratorStateMachineAttribute",
        "System.Runtime.CompilerServices.IsReadOnlyAttribute",
        "System.Runtime.CompilerServices.IsByRefLikeAttribute",
        "System.Runtime.CompilerServices.CompilerFeatureRequiredAttribute",
        "System.Runtime.CompilerServices.NullableAttribute",
        "System.Runtime.CompilerServices.NullableContextAttribute",
        "System.Runtime.CompilerServices.IsUnmanagedAttribute",
        "System.Reflection.DefaultMemberAttribute",
        "System.Diagnostics.DebuggableAttribute",
        "System.Diagnostics.DebuggerNonUserCodeAttribute",
        "System.Diagnostics.DebuggerStepThroughAttribute",
        "System.Runtime.InteropServices.DefaultParameterValueAttribute",
        "System.Runtime.InteropServices.OptionalAttribute",
        "System.Runtime.InteropServices.InAttribute",
        "System.Runtime.InteropServices.OutAttribute",
        "System.Runtime.CompilerServices.RequiresLocationAttribute",
        "System.ParamArrayAttribute",
        "System.Runtime.CompilerServices.ParamCollectionAttribute",
        "System.Runtime.CompilerServices.ScopedRefAttribute",
        "System.Reflection.AssemblyCompanyAttribute",
        "System.Reflection.AssemblyConfigurationAttribute",
        "System.Reflection.AssemblyCopyrightAttribute",
        "System.Reflection.AssemblyDescriptionAttribute",
        "System.Reflection.AssemblyFileVersionAttribute",
        "System.Reflection.AssemblyInformationalVersionAttribute",
        "System.Reflection.AssemblyProductAttribute",
        "System.Reflection.AssemblyTitleAttribute",
        "System.Reflection.AssemblyTrademarkAttribute",
    };

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
        var inheritance = BuildTypeInheritance(metadataReader, typeDefinition);
        var constraints = BuildTypeConstraints(metadataReader, typeDefinitionHandle);
        var typeAttributes = BuildAttributes(metadataReader, typeDefinition.GetCustomAttributes());

        if (keyword == "delegate")
        {
            var invokeMethodHandle = typeDefinition.GetMethods()
                .FirstOrDefault(handle => string.Equals(metadataReader.GetString(metadataReader.GetMethodDefinition(handle).Name), "Invoke", StringComparison.Ordinal));
            if (invokeMethodHandle.IsNil)
                throw new InvalidOperationException("Delegate type must have an Invoke method");

            var invokeMethod = metadataReader.GetMethodDefinition(invokeMethodHandle);
            var invokeSignature = DecodeMethodSignature(metadataReader, invokeMethod);
            var returnType = ApplyNullableReferenceType(
                invokeSignature.Signature.ReturnType,
                GetNullableMetadataInfo(metadataReader, typeDefinitionHandle, GetParameterCustomAttributes(metadataReader, invokeMethod, sequenceNumber: 0), invokeMethodHandle));
            var parameters = BuildParameterDeclarations(metadataReader, typeDefinitionHandle, invokeMethodHandle, invokeMethod, invokeSignature.Signature.ParameterTypes, isExtensionMethod: false);
            var unsafeModifier = RequiresUnsafeContext(invokeSignature.Signature.ReturnType, invokeSignature.Signature.ParameterTypes) ? " unsafe" : string.Empty;

            var delegateBuilder = new StringBuilder();
            foreach (var attribute in typeAttributes)
            {
                delegateBuilder.AppendLine(attribute);
            }

            delegateBuilder.Append($"{accessibility}{unsafeModifier} delegate {returnType} {typeName}{genericArguments}({string.Join(", ", parameters.Select(static parameter => parameter.Text))}){FormatConstraintsInline(constraints)};");
            return delegateBuilder.ToString();
        }

        if (keyword == "enum")
        {
            var enumDeclaration = $"{accessibility} enum {typeName}{genericArguments}";
            var enumBody = BuildEnumMembers(metadataReader, typeDefinition).ToArray();
            var enumBuilder = new StringBuilder();
            foreach (var attribute in typeAttributes)
            {
                enumBuilder.AppendLine(attribute);
            }

            enumBuilder.AppendLine(enumDeclaration);
            enumBuilder.AppendLine("{");
            for (var i = 0; i < enumBody.Length; i++)
            {
                var suffix = i == enumBody.Length - 1 ? string.Empty : ",";
                AppendIndentedLine(enumBuilder, 1, enumBody[i] + suffix);
            }

            enumBuilder.AppendLine("}");
            return enumBuilder.ToString();
        }

        var sb = new StringBuilder();
        foreach (var attribute in typeAttributes)
        {
            sb.AppendLine(attribute);
        }

        var typeModifiers = new List<string> { accessibility };
        if (keyword == "class")
        {
            if (typeDefinition.Attributes.HasFlag(TypeAttributes.Abstract) && typeDefinition.Attributes.HasFlag(TypeAttributes.Sealed))
            {
                typeModifiers.Add("static");
            }
            else
            {
                if (typeDefinition.Attributes.HasFlag(TypeAttributes.Abstract))
                {
                    typeModifiers.Add("abstract");
                }

                if (typeDefinition.Attributes.HasFlag(TypeAttributes.Sealed))
                {
                    typeModifiers.Add("sealed");
                }
            }
        }

        sb.Append(string.Join(' ', typeModifiers.Where(static modifier => !string.IsNullOrEmpty(modifier))));
        sb.Append(' ');
        sb.Append(keyword);
        sb.Append(' ');
        sb.Append(typeName);
        sb.Append(genericArguments);
        sb.Append(inheritance);
        sb.Append(FormatConstraintsInline(constraints));
        sb.AppendLine();
        sb.AppendLine("{");

        foreach (var member in BuildMembers(metadataReader, typeDefinitionHandle, typeDefinition))
        {
            AppendIndentedLine(sb, 1, member);
        }

        var nestedTypeHandles = EnumerateNestedTypes(metadataReader, typeDefinitionHandle)
            .OrderBy(handle => metadataReader.GetString(metadataReader.GetTypeDefinition(handle).Name), StringComparer.Ordinal)
            .ToArray();
        for (var i = 0; i < nestedTypeHandles.Length; i++)
        {
            var nestedTypeHandle = nestedTypeHandles[i];
            var nestedTypeDefinition = metadataReader.GetTypeDefinition(nestedTypeHandle);
            var nestedTypeName = RemoveGenericArity(metadataReader.GetString(nestedTypeDefinition.Name));
            var nestedTypeSource = BuildTypeSource(metadataReader, nestedTypeHandle, nestedTypeDefinition, nestedTypeName);
            AppendIndentedBlock(sb, 1, nestedTypeSource);
        }

        sb.AppendLine("}");
        return sb.ToString();
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

        var methodHandles = typeDefinition.GetMethods().ToArray();
        var extensionPropertyBlocks = BuildExtensionPropertyBlocks(metadataReader, typeDefinitionHandle, methodHandles);
        var extensionPropertyAccessorHandles = extensionPropertyBlocks.SelectMany(static block => block.Accessors).ToHashSet();

        foreach (var methodHandle in methodHandles)
        {
            if (extensionPropertyAccessorHandles.Contains(methodHandle))
                continue;

            var method = metadataReader.GetMethodDefinition(methodHandle);
            var name = metadataReader.GetString(method.Name);
            if (string.Equals(name, ".ctor", StringComparison.Ordinal))
            {
                if (!IsExternallyVisible(method.Attributes))
                    continue;

                var constructorText = BuildConstructor(metadataReader, typeDefinitionHandle, methodHandle, method);
                if (constructorText is not null)
                {
                    yield return constructorText;
                }

                continue;
            }

            if ((!IsExternallyVisible(method.Attributes) && !IsExplicitInterfaceImplementation(method.Attributes, name)) ||
                (method.Attributes.HasFlag(MethodAttributes.SpecialName) && !IsOperatorMethod(name)) ||
                name.Contains('<', StringComparison.Ordinal))
                continue;

            yield return BuildMethod(metadataReader, typeDefinitionHandle, methodHandle, method, name);
        }

        foreach (var extensionPropertyBlock in extensionPropertyBlocks.OrderBy(static block => block.Order))
        {
            yield return extensionPropertyBlock.Content;
        }
    }

    private static IEnumerable<TypeDefinitionHandle> EnumerateNestedTypes(MetadataReader metadataReader, TypeDefinitionHandle parentTypeHandle)
    {
        foreach (var typeDefinitionHandle in metadataReader.TypeDefinitions)
        {
            var typeDefinition = metadataReader.GetTypeDefinition(typeDefinitionHandle);
            if (typeDefinition.GetDeclaringType() != parentTypeHandle)
                continue;

            if (!IsExternallyVisibleNested(typeDefinition.Attributes))
                continue;

            var nestedTypeName = metadataReader.GetString(typeDefinition.Name);
            if (nestedTypeName.Contains('<', StringComparison.Ordinal))
                continue;

            yield return typeDefinitionHandle;
        }
    }

    private static bool IsExternallyVisibleNested(TypeAttributes attributes)
    {
        return (attributes & TypeAttributes.VisibilityMask) is TypeAttributes.NestedPublic or TypeAttributes.NestedFamily or TypeAttributes.NestedFamORAssem;
    }

    private static void AppendIndentedBlock(StringBuilder sb, int indentationLevel, string text)
    {
        var lines = text.TrimEnd('\r', '\n').Split('\n');
        foreach (var line in lines)
        {
            var content = line.TrimEnd('\r');
            if (content.Length == 0)
            {
                AppendIndentedLine(sb, 0, string.Empty);
            }
            else
            {
                AppendIndentedLine(sb, indentationLevel, content);
            }
        }
    }

    private static IEnumerable<string> BuildEnumMembers(MetadataReader metadataReader, TypeDefinition typeDefinition)
    {
        foreach (var fieldHandle in typeDefinition.GetFields())
        {
            var field = metadataReader.GetFieldDefinition(fieldHandle);
            if (!field.Attributes.HasFlag(FieldAttributes.Literal) || field.Attributes.HasFlag(FieldAttributes.SpecialName))
                continue;

            var name = metadataReader.GetString(field.Name);
            if (field.GetDefaultValue().IsNil)
            {
                yield return name;
                continue;
            }

            var constant = DecodeConstantValue(metadataReader, metadataReader.GetConstant(field.GetDefaultValue()));
            yield return name + " = " + FormatConstant(constant);
        }
    }

    private static string BuildTypeInheritance(MetadataReader metadataReader, TypeDefinition typeDefinition)
    {
        var keyword = GetTypeKeyword(metadataReader, typeDefinition);
        var baseTypes = new List<string>();
        if (keyword == "class" && !typeDefinition.BaseType.IsNil)
        {
            var baseTypeName = GetTypeFullName(metadataReader, typeDefinition.BaseType);
            if (!string.Equals(baseTypeName, "System.Object", StringComparison.Ordinal) &&
                !string.Equals(baseTypeName, "System.ValueType", StringComparison.Ordinal))
            {
                baseTypes.Add(FormatDecodedTypeWithoutNullable(DecodeTypeFromEntityHandle(metadataReader, typeDefinition.BaseType)));
            }
        }

        foreach (var interfaceImplementationHandle in typeDefinition.GetInterfaceImplementations())
        {
            var interfaceImplementation = metadataReader.GetInterfaceImplementation(interfaceImplementationHandle);
            baseTypes.Add(FormatDecodedTypeWithoutNullable(DecodeTypeFromEntityHandle(metadataReader, interfaceImplementation.Interface)));
        }

        if (baseTypes.Count == 0)
            return string.Empty;

        return " : " + string.Join(", ", baseTypes.Distinct(StringComparer.Ordinal).OrderBy(static value => value, StringComparer.Ordinal));
    }

    private static List<string> BuildTypeConstraints(MetadataReader metadataReader, TypeDefinitionHandle typeDefinitionHandle)
    {
        return BuildConstraints(metadataReader, metadataReader.GetTypeDefinition(typeDefinitionHandle).GetGenericParameters());
    }

    private static List<string> BuildConstraints(MetadataReader metadataReader, GenericParameterHandleCollection genericParameters)
    {
        var constraints = new List<string>();
        foreach (var genericParameterHandle in genericParameters)
        {
            var genericParameter = metadataReader.GetGenericParameter(genericParameterHandle);
            var values = new List<string>();
            var hasStructConstraint = false;
            if (genericParameter.Attributes.HasFlag(GenericParameterAttributes.ReferenceTypeConstraint))
            {
                values.Add("class");
            }

            if (genericParameter.Attributes.HasFlag(GenericParameterAttributes.NotNullableValueTypeConstraint))
            {
                values.Add("struct");
                hasStructConstraint = true;
            }

            foreach (var constraintHandle in genericParameter.GetConstraints())
            {
                var constraint = metadataReader.GetGenericParameterConstraint(constraintHandle);
                var constraintType = FormatDecodedTypeWithoutNullable(DecodeTypeFromEntityHandle(metadataReader, constraint.Type));
                if (hasStructConstraint && string.Equals(constraintType, "System.ValueType", StringComparison.Ordinal))
                    continue;

                values.Add(constraintType);
            }

            if (genericParameter.Attributes.HasFlag(GenericParameterAttributes.DefaultConstructorConstraint) && !hasStructConstraint)
            {
                values.Add("new()");
            }

            if (genericParameter.Attributes.HasFlag(AllowByRefLikeGenericParameterConstraint))
            {
                values.Add("allows ref struct");
            }

            if (values.Count == 0)
                continue;

            constraints.Add($"where {metadataReader.GetString(genericParameter.Name)} : {string.Join(", ", values)}");
        }

        return constraints;
    }

    private static string BuildField(MetadataReader metadataReader, TypeDefinitionHandle declaringTypeHandle, FieldDefinition field)
    {
        var type = DecodeFieldType(metadataReader, field);
        var nullableInfo = GetNullableMetadataInfo(metadataReader, declaringTypeHandle, field.GetCustomAttributes());
        var isByRefField = type.Kind == DecodedTypeKind.ByReference;
        var typeName = isByRefField
            ? BuildByRefFieldType(
                type,
                nullableInfo,
                field.Attributes.HasFlag(FieldAttributes.InitOnly),
                HasAttribute(metadataReader, field.GetCustomAttributes(), "System.Runtime.CompilerServices.IsReadOnlyAttribute") ||
                type.ElementType?.HasIsReadOnlyModifier == true)
            : ApplyNullableReferenceType(
                type,
                nullableInfo);
        var attributes = BuildAttributes(metadataReader, field.GetCustomAttributes());
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

        if (field.Attributes.HasFlag(FieldAttributes.InitOnly) && !isByRefField)
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
            declaration += " = " + FormatConstant(DecodeConstantValue(metadataReader, metadataReader.GetConstant(field.GetDefaultValue())));
        }

        declaration += ";";
        return ComposeAttributedMember(attributes, declaration);
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
        var declaringType = metadataReader.GetTypeDefinition(declaringTypeHandle);
        var shouldEmitAccessibility = !((declaringType.Attributes & TypeAttributes.ClassSemanticsMask) == TypeAttributes.Interface &&
                                        representativeAttributes.HasFlag(MethodAttributes.Abstract));
        var accessibility = GetAccessibility(representativeAttributes);
        if (shouldEmitAccessibility && !string.IsNullOrEmpty(accessibility))
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

        NullableMetadataInfo propertyNullableInfo;
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

        var propertySignature = DecodePropertySignature(metadataReader, property);
        var propertyType = ApplyNullableReferenceType(propertySignature.ReturnType, propertyNullableInfo);
        var propertyName = propertySignature.ParameterTypes.Length > 0
            ? "this[" + BuildIndexerParameters(metadataReader, declaringTypeHandle, getAccessorHandle, setAccessorHandle, getAccessor, setAccessor, propertySignature.ParameterTypes) + "]"
            : metadataReader.GetString(property.Name);
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
                : accessorKeyword + " { }";
            accessorText.Add(accessorModifier + accessorBody);
        }

        var modifiersPrefix = modifiers.Count > 0 ? string.Join(' ', modifiers) + " " : string.Empty;
        var declaration = $"{modifiersPrefix}{propertyType} {propertyName} {{ {string.Join(' ', accessorText)} }}";
        var attributes = BuildAttributes(metadataReader, property.GetCustomAttributes());
        return ComposeAttributedMember(attributes, declaration);
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
        var declaration = $"{string.Join(' ', modifiers)} event {eventType} {eventName};";
        var attributes = BuildAttributes(metadataReader, eventDefinition.GetCustomAttributes());
        return ComposeAttributedMember(attributes, declaration);
    }

    private static string BuildMethod(MetadataReader metadataReader, TypeDefinitionHandle declaringTypeHandle, MethodDefinitionHandle methodHandle, MethodDefinition method, string name)
    {
        var signature = DecodeMethodSignature(metadataReader, method);
        if (IsDestructor(method, signature, name))
        {
            var declaringTypeDefinition = metadataReader.GetTypeDefinition(declaringTypeHandle);
            var typeName = RemoveGenericArity(metadataReader.GetString(declaringTypeDefinition.Name));
            var destructorBody = method.Attributes.HasFlag(MethodAttributes.Abstract) ? ";" : " { }";
            return $"~{typeName}(){destructorBody}";
        }

        var isExplicitInterfaceImplementation = IsExplicitInterfaceImplementation(method.Attributes, name);
        var declaringType = metadataReader.GetTypeDefinition(declaringTypeHandle);
        var modifiers = isExplicitInterfaceImplementation
            ? []
            : BuildMethodModifiers(method.Attributes, declaringType.Attributes.HasFlag(TypeAttributes.Interface));
        var genericArguments = BuildGenericArguments(metadataReader, method.GetGenericParameters());
        var constraints = BuildConstraints(metadataReader, method.GetGenericParameters());
        var isExtensionMethod = method.Attributes.HasFlag(MethodAttributes.Static) &&
                                HasAttribute(metadataReader, method.GetCustomAttributes(), "System.Runtime.CompilerServices.ExtensionAttribute");
        var returnNullableInfo = GetNullableMetadataInfo(metadataReader, declaringTypeHandle, GetParameterCustomAttributes(metadataReader, method, sequenceNumber: 0), methodHandle);
        var returnType = ApplyNullableReferenceType(signature.Signature.ReturnType, returnNullableInfo);
        var parameters = BuildParameterDeclarations(metadataReader, declaringTypeHandle, methodHandle, method, signature.Signature.ParameterTypes, isExtensionMethod);
        var methodBody = BuildMethodBody(metadataReader, method, signature);
        var methodName = isExplicitInterfaceImplementation ? BuildExplicitInterfaceMethodName(name) : name;
        var modifiersPrefix = modifiers.Count > 0 ? string.Join(' ', modifiers) + " " : string.Empty;
        var unsafeModifier = RequiresUnsafeContext(signature.Signature.ReturnType, signature.Signature.ParameterTypes) ? "unsafe " : string.Empty;
        var requiresNullableDisableDirective = RequiresNullableDirectives(signature.Signature.ReturnType, returnNullableInfo) ||
                                               RequiresNullableDisableDirectiveForParameters(metadataReader, declaringTypeHandle, methodHandle, method, signature.Signature.ParameterTypes);
        var methodSuffix = FormatConstraintsInline(constraints) + methodBody;
        string declaration;
        if (TryGetOperatorKeyword(name) is { } operatorKeyword && CanEmitOperator(declaringType))
        {
            var methodPrefix = operatorKeyword is "implicit" or "explicit"
                ? $"{modifiersPrefix}{unsafeModifier}{operatorKeyword} operator {returnType}"
                : $"{modifiersPrefix}{unsafeModifier}{returnType} operator {operatorKeyword}";
            declaration = BuildDeclarationWithParameters(methodPrefix, parameters, methodSuffix, requiresNullableDisableDirective);
        }
        else
        {
            var methodPrefix = $"{modifiersPrefix}{unsafeModifier}{returnType} {methodName}{genericArguments}";
            declaration = BuildDeclarationWithParameters(methodPrefix, parameters, methodSuffix, requiresNullableDisableDirective);
        }

        var attributes = BuildAttributes(metadataReader, method.GetCustomAttributes());
        return ComposeAttributedMember(attributes, declaration);
    }

    private static string? BuildConstructor(MetadataReader metadataReader, TypeDefinitionHandle declaringTypeHandle, MethodDefinitionHandle methodHandle, MethodDefinition method)
    {
        var signature = DecodeMethodSignature(metadataReader, method);
        var declaringType = metadataReader.GetTypeDefinition(declaringTypeHandle);
        var typeName = RemoveGenericArity(metadataReader.GetString(declaringType.Name));
        var accessibility = GetAccessibility(method.Attributes);
        var modifiersPrefix = string.IsNullOrEmpty(accessibility) ? string.Empty : accessibility + " ";
        var unsafeModifier = RequiresUnsafeContext(signature.Signature.ReturnType, signature.Signature.ParameterTypes) ? "unsafe " : string.Empty;
        var parameters = BuildParameterDeclarations(metadataReader, declaringTypeHandle, methodHandle, method, signature.Signature.ParameterTypes, isExtensionMethod: false);
        var requiresNullableDisableDirective = RequiresNullableDisableDirectiveForParameters(metadataReader, declaringTypeHandle, methodHandle, method, signature.Signature.ParameterTypes);
        var initializer = BuildConstructorInitializer(metadataReader, declaringType);
        if (signature.Signature.ParameterTypes.Length == 0 && string.IsNullOrEmpty(initializer))
            return null;

        var methodBody = method.Attributes.HasFlag(MethodAttributes.Abstract) ? ";" : " { }";
        var declaration = BuildDeclarationWithParameters(modifiersPrefix + unsafeModifier + typeName, parameters, initializer + methodBody, requiresNullableDisableDirective);
        var attributes = BuildAttributes(metadataReader, method.GetCustomAttributes());
        return ComposeAttributedMember(attributes, declaration);
    }

    private static ImmutableArray<ParameterDeclarationMetadata> BuildParameterDeclarations(
        MetadataReader metadataReader,
        TypeDefinitionHandle declaringTypeHandle,
        MethodDefinitionHandle methodHandle,
        MethodDefinition method,
        ImmutableArray<DecodedType> parameterTypes,
        bool isExtensionMethod)
    {
        var parametersBySequence = method.GetParameters()
            .ToDictionary(
                parameterHandle => metadataReader.GetParameter(parameterHandle).SequenceNumber,
                parameterHandle => parameterHandle);

        var result = new ParameterDeclarationMetadata[parameterTypes.Length];
        for (var i = 0; i < parameterTypes.Length; i++)
        {
            var sequence = i + 1;
            string parameterName;
            CustomAttributeHandleCollection? parameterAttributes;
            var parameterAttributeFlags = default(ParameterAttributes);
            var hasInAttribute = false;
            var hasIsReadOnlyAttribute = false;
            var hasRequiresLocationAttribute = false;
            var hasParamArrayAttribute = false;
            var hasParamCollectionAttribute = false;
            var defaultValue = string.Empty;
            if (parametersBySequence.TryGetValue(sequence, out var parameterHandle))
            {
                var parameter = metadataReader.GetParameter(parameterHandle);
                parameterName = parameter.Name.IsNil
                    ? "arg" + sequence.ToString(System.Globalization.CultureInfo.InvariantCulture)
                    : metadataReader.GetString(parameter.Name);
                parameterAttributeFlags = parameter.Attributes;
                parameterAttributes = parameter.GetCustomAttributes();
                hasInAttribute = HasAttribute(metadataReader, parameterAttributes.Value, "System.Runtime.InteropServices.InAttribute");
                hasIsReadOnlyAttribute = HasAttribute(metadataReader, parameterAttributes.Value, "System.Runtime.CompilerServices.IsReadOnlyAttribute");
                hasRequiresLocationAttribute = HasAttribute(metadataReader, parameterAttributes.Value, "System.Runtime.CompilerServices.RequiresLocationAttribute");
                hasParamArrayAttribute = HasAttribute(metadataReader, parameterAttributes.Value, "System.ParamArrayAttribute");
                hasParamCollectionAttribute = HasAttribute(metadataReader, parameterAttributes.Value, "System.Runtime.CompilerServices.ParamCollectionAttribute");
                if (parameter.Attributes.HasFlag(ParameterAttributes.HasDefault) && !parameter.GetDefaultValue().IsNil)
                {
                    defaultValue = " = " + FormatConstant(DecodeConstantValue(metadataReader, metadataReader.GetConstant(parameter.GetDefaultValue())));
                }
            }
            else
            {
                parameterName = "arg" + sequence.ToString(System.Globalization.CultureInfo.InvariantCulture);
                parameterAttributes = null;
            }

            var parameterNullableInfo = GetNullableMetadataInfo(
                metadataReader,
                declaringTypeHandle,
                parameterAttributes,
                methodHandle,
                includeDeclaringTypeContext: false);
            var parameterAttributeText = parameterAttributes is null
                ? string.Empty
                : BuildInlineAttributes(BuildAttributes(metadataReader, parameterAttributes.Value));
            var parameterType = ApplyNullableReferenceType(parameterTypes[i], parameterNullableInfo);
            if (parameterTypes[i].Kind == DecodedTypeKind.ByReference && parameterType.StartsWith("ref ", StringComparison.Ordinal))
            {
                parameterType = parameterType[4..];
            }

            var parameterModifier = BuildParameterModifier(
                parameterTypes[i],
                parameterAttributeFlags,
                hasInAttribute,
                hasIsReadOnlyAttribute,
                hasRequiresLocationAttribute,
                hasParamArrayAttribute,
                hasParamCollectionAttribute);
            var extensionThisModifier = isExtensionMethod && i == 0 ? "this " : string.Empty;
            result[i] = new ParameterDeclarationMetadata(
                parameterAttributeText + extensionThisModifier + parameterModifier + parameterType + " " + parameterName + defaultValue,
                RequiresNullableDirectives(parameterTypes[i], parameterNullableInfo));
        }

        return result.ToImmutableArray();
    }

    private static bool RequiresNullableDisableDirectiveForParameters(
        MetadataReader metadataReader,
        TypeDefinitionHandle declaringTypeHandle,
        MethodDefinitionHandle methodHandle,
        MethodDefinition method,
        ImmutableArray<DecodedType> parameterTypes)
    {
        var parametersBySequence = method.GetParameters()
            .ToDictionary(
                parameterHandle => metadataReader.GetParameter(parameterHandle).SequenceNumber,
                parameterHandle => metadataReader.GetParameter(parameterHandle));

        for (var i = 0; i < parameterTypes.Length; i++)
        {
            var sequence = i + 1;
            var parameterAttributes = parametersBySequence.TryGetValue(sequence, out var parameter)
                ? parameter.GetCustomAttributes()
                : default(CustomAttributeHandleCollection?);

            var nullableInfo = GetNullableMetadataInfo(
                metadataReader,
                declaringTypeHandle,
                parameterAttributes,
                methodHandle,
                includeDeclaringTypeContext: true);
            if (RequiresNullableDirectives(parameterTypes[i], nullableInfo))
                return true;
        }

        return false;
    }

    private static string BuildDeclarationWithParameters(
        string declarationPrefix,
        IReadOnlyList<ParameterDeclarationMetadata> parameters,
        string declarationSuffix,
        bool wrapWithNullableDisableDirective = false)
    {
        var hasNullableAnnotations = declarationPrefix.Contains('?', StringComparison.Ordinal) ||
                                     parameters.Any(static parameter => parameter.Text.Contains('?', StringComparison.Ordinal));
        var shouldEmitNullableDirectives = parameters.Any(static parameter => parameter.RequiresNullableDirectives) && hasNullableAnnotations;
        var shouldWrapWithNullableDisableDirective = wrapWithNullableDisableDirective && !hasNullableAnnotations;
        if (!shouldEmitNullableDirectives)
        {
            var declaration = declarationPrefix + "(" + string.Join(", ", parameters.Select(static parameter => parameter.Text)) + ")" + declarationSuffix;
            if (!shouldWrapWithNullableDisableDirective)
                return declaration;

            return string.Concat(
                "#nullable disable",
                Environment.NewLine,
                "    ",
                declaration,
                Environment.NewLine,
                "    #nullable restore");
        }

        var sb = new StringBuilder();
        if (shouldWrapWithNullableDisableDirective)
        {
            sb.AppendLine("#nullable disable");
        }

        sb.Append(declarationPrefix);
        sb.AppendLine("(");
        for (var i = 0; i < parameters.Count; i++)
        {
            var parameter = parameters[i];
            var parameterSuffix = i < parameters.Count - 1 ? "," : string.Empty;
            if (parameter.RequiresNullableDirectives)
            {
                sb.AppendLine("    #nullable disable");
                sb.Append("        ");
                sb.AppendLine(parameter.Text + parameterSuffix);
                sb.AppendLine("    #nullable restore");
            }
            else
            {
                sb.Append("        ");
                sb.AppendLine(parameter.Text + parameterSuffix);
            }
        }

        sb.Append("        )");
        sb.Append(declarationSuffix);
        if (shouldWrapWithNullableDisableDirective)
        {
            sb.AppendLine();
            sb.Append("    #nullable restore");
        }

        return sb.ToString();
    }

    private static string BuildIndexerParameters(
        MetadataReader metadataReader,
        TypeDefinitionHandle declaringTypeHandle,
        MethodDefinitionHandle getAccessorHandle,
        MethodDefinitionHandle setAccessorHandle,
        MethodDefinition? getAccessor,
        MethodDefinition? setAccessor,
        ImmutableArray<DecodedType> propertyParameterTypes)
    {
        var accessorMethod = getAccessor ?? setAccessor!.Value;
        var accessorHandle = getAccessor is not null ? getAccessorHandle : setAccessorHandle;
        var parametersBySequence = accessorMethod.GetParameters()
            .ToDictionary(
                parameterHandle => metadataReader.GetParameter(parameterHandle).SequenceNumber,
                parameterHandle => metadataReader.GetParameter(parameterHandle));

        var result = new string[propertyParameterTypes.Length];
        for (var i = 0; i < propertyParameterTypes.Length; i++)
        {
            var sequence = i + 1;
            var parameterName = "arg" + sequence.ToString(System.Globalization.CultureInfo.InvariantCulture);
            var parameterAttributes = default(ParameterAttributes);
            var customAttributes = default(CustomAttributeHandleCollection?);
            var hasInAttribute = false;
            var hasIsReadOnlyAttribute = false;
            var hasRequiresLocationAttribute = false;
            var hasParamArrayAttribute = false;
            var hasParamCollectionAttribute = false;
            if (parametersBySequence.TryGetValue(sequence, out var parameter))
            {
                parameterName = parameter.Name.IsNil ? parameterName : metadataReader.GetString(parameter.Name);
                parameterAttributes = parameter.Attributes;
                customAttributes = parameter.GetCustomAttributes();
                hasInAttribute = HasAttribute(metadataReader, customAttributes.Value, "System.Runtime.InteropServices.InAttribute");
                hasIsReadOnlyAttribute = HasAttribute(metadataReader, customAttributes.Value, "System.Runtime.CompilerServices.IsReadOnlyAttribute");
                hasRequiresLocationAttribute = HasAttribute(metadataReader, customAttributes.Value, "System.Runtime.CompilerServices.RequiresLocationAttribute");
                hasParamArrayAttribute = HasAttribute(metadataReader, customAttributes.Value, "System.ParamArrayAttribute");
                hasParamCollectionAttribute = HasAttribute(metadataReader, customAttributes.Value, "System.Runtime.CompilerServices.ParamCollectionAttribute");
            }

            var parameterNullableInfo = GetNullableMetadataInfo(metadataReader, declaringTypeHandle, customAttributes, accessorHandle);
            var parameterType = ApplyNullableReferenceType(propertyParameterTypes[i], parameterNullableInfo);
            if (propertyParameterTypes[i].Kind == DecodedTypeKind.ByReference && parameterType.StartsWith("ref ", StringComparison.Ordinal))
            {
                parameterType = parameterType[4..];
            }

            var inlineAttributes = customAttributes is null
                ? string.Empty
                : BuildInlineAttributes(BuildAttributes(metadataReader, customAttributes.Value));
            var parameterModifier = BuildParameterModifier(
                propertyParameterTypes[i],
                parameterAttributes,
                hasInAttribute,
                hasIsReadOnlyAttribute,
                hasRequiresLocationAttribute,
                hasParamArrayAttribute,
                hasParamCollectionAttribute);
            result[i] = inlineAttributes + parameterModifier + parameterType + " " + parameterName;
        }

        return string.Join(", ", result);
    }

    private static string BuildByRefFieldType(DecodedType type, NullableMetadataInfo nullableInfo, bool isInitOnly, bool isRefReadonly)
    {
        var elementType = ApplyNullableReferenceType(type.ElementType!, nullableInfo);
        if (isInitOnly)
        {
            return isRefReadonly
                ? "readonly ref readonly " + elementType
                : "readonly ref " + elementType;
        }

        return isRefReadonly
            ? "ref readonly " + elementType
            : "ref " + elementType;
    }

    private static bool RequiresNullableDirectives(DecodedType parameterType, NullableMetadataInfo nullableInfo)
    {
        while (parameterType.Kind is DecodedTypeKind.ByReference or DecodedTypeKind.Pointer)
        {
            if (parameterType.ElementType is null)
                return false;

            parameterType = parameterType.ElementType;
        }

        if (!parameterType.IsReferenceType || parameterType.IsTypeParameter)
            return false;

        var topLevelAnnotation = nullableInfo.Flags.IsDefaultOrEmpty ? nullableInfo.ContextFlag : nullableInfo.Flags[0];
        return topLevelAnnotation == 0;
    }

    private static bool RequiresUnsafeContext(DecodedType returnType, ImmutableArray<DecodedType> parameterTypes)
    {
        if (ContainsPointer(returnType))
            return true;

        foreach (var parameterType in parameterTypes)
        {
            if (ContainsPointer(parameterType))
                return true;
        }

        return false;
    }

    private static bool ContainsPointer(DecodedType type)
    {
        if (type.Kind == DecodedTypeKind.Pointer)
            return true;

        if (type.ElementType is not null && ContainsPointer(type.ElementType))
            return true;

        if (type.TypeArguments.IsDefaultOrEmpty)
            return false;

        foreach (var typeArgument in type.TypeArguments)
        {
            if (ContainsPointer(typeArgument))
                return true;
        }

        return false;
    }

    private static string BuildParameterModifier(
        DecodedType parameterType,
        ParameterAttributes parameterAttributes,
        bool hasInAttribute,
        bool hasIsReadOnlyAttribute,
        bool hasRequiresLocationAttribute,
        bool hasParamArrayAttribute,
        bool hasParamCollectionAttribute)
    {
        if (parameterType.Kind != DecodedTypeKind.ByReference)
            return (hasParamArrayAttribute || hasParamCollectionAttribute) ? "params " : string.Empty;

        var hasInModifier = parameterType.ElementType?.HasInModifier == true;
        var hasIsReadOnlyModifier = parameterType.ElementType?.HasIsReadOnlyModifier == true;
        var hasRequiresLocationModifier = parameterType.ElementType?.HasRequiresLocationModifier == true;

        if (parameterAttributes.HasFlag(ParameterAttributes.Out))
            return "out ";

        if (hasRequiresLocationAttribute || hasRequiresLocationModifier)
            return "ref readonly ";

        if (hasInAttribute || hasInModifier || hasIsReadOnlyAttribute || hasIsReadOnlyModifier)
        {
            return "in ";
        }

        return "ref ";
    }

    private static string BuildConstructorInitializer(MetadataReader metadataReader, TypeDefinition declaringType)
    {
        if (declaringType.BaseType.IsNil)
            return string.Empty;

        var baseTypeFullName = GetTypeFullName(metadataReader, declaringType.BaseType);
        if (string.Equals(baseTypeFullName, "System.Object", StringComparison.Ordinal) ||
            string.Equals(baseTypeFullName, "System.ValueType", StringComparison.Ordinal))
            return string.Empty;

        if (!TryGetTypeDefinition(metadataReader, declaringType.BaseType, out var baseTypeHandle))
            return string.Empty;

        var baseType = metadataReader.GetTypeDefinition(baseTypeHandle);
        MethodDefinitionHandle selectedBaseConstructorHandle = default;
        var hasAccessibleParameterlessConstructor = false;
        foreach (var baseMethodHandle in baseType.GetMethods())
        {
            var baseMethod = metadataReader.GetMethodDefinition(baseMethodHandle);
            if (baseMethod.Attributes.HasFlag(MethodAttributes.Static))
                continue;

            if (!string.Equals(metadataReader.GetString(baseMethod.Name), ".ctor", StringComparison.Ordinal))
                continue;

            if (!IsExternallyVisible(baseMethod.Attributes))
                continue;

            var baseSignature = DecodeMethodSignature(metadataReader, baseMethod);
            if (baseSignature.Signature.ParameterTypes.Length == 0)
            {
                hasAccessibleParameterlessConstructor = true;
                break;
            }

            if (selectedBaseConstructorHandle.IsNil || MetadataTokens.GetToken(baseMethodHandle) < MetadataTokens.GetToken(selectedBaseConstructorHandle))
            {
                selectedBaseConstructorHandle = baseMethodHandle;
            }
        }

        if (hasAccessibleParameterlessConstructor || selectedBaseConstructorHandle.IsNil)
            return string.Empty;

        var selectedBaseConstructor = metadataReader.GetMethodDefinition(selectedBaseConstructorHandle);
        var selectedSignature = DecodeMethodSignature(metadataReader, selectedBaseConstructor);
        var arguments = string.Join(", ", selectedSignature.Signature.ParameterTypes.Select(static parameterType =>
        {
            var type = parameterType.Kind == DecodedTypeKind.ByReference
                ? parameterType.ElementType!
                : parameterType;
            return "default(" + FormatDecodedTypeWithoutNullable(type) + ")";
        }));
        return " : base(" + arguments + ")";
    }

    private static List<ExtensionPropertyBlockMetadata> BuildExtensionPropertyBlocks(MetadataReader metadataReader, TypeDefinitionHandle declaringTypeHandle, IReadOnlyList<MethodDefinitionHandle> methodHandles)
    {
        var declaringType = metadataReader.GetTypeDefinition(declaringTypeHandle);
        if (!(declaringType.Attributes.HasFlag(TypeAttributes.Abstract) && declaringType.Attributes.HasFlag(TypeAttributes.Sealed)))
            return [];

        var blocks = new Dictionary<(string ReceiverTypeKey, string PropertyName), ExtensionPropertyBuilderMetadata>();
        foreach (var methodHandle in methodHandles)
        {
            var method = metadataReader.GetMethodDefinition(methodHandle);
            if (!TryGetExtensionPropertyAccessorInfo(metadataReader, method, out var accessorType, out var propertyName))
                continue;

            var signature = DecodeMethodSignature(metadataReader, method);
            if (accessorType == "get" && signature.Signature.ParameterTypes.Length < 1)
                continue;

            if (accessorType == "set" && signature.Signature.ParameterTypes.Length < 2)
                continue;

            var receiverNullableInfo = GetNullableMetadataInfo(
                metadataReader,
                declaringTypeHandle,
                GetParameterCustomAttributes(metadataReader, method, sequenceNumber: 1),
                methodHandle);
            var receiverType = ApplyNullableReferenceType(signature.Signature.ParameterTypes[0], receiverNullableInfo);
            var receiverName = GetParameterName(metadataReader, method, sequenceNumber: 1, fallbackName: "value");
            var receiverTypeKey = receiverType;
            var key = (receiverTypeKey, propertyName);

            if (!blocks.TryGetValue(key, out var block))
            {
                block = new ExtensionPropertyBuilderMetadata(receiverType, receiverName, propertyName, PropertyType: null, Getter: default, Setter: default, Order: MetadataTokens.GetToken(methodHandle));
            }

            string propertyType;
            if (accessorType == "get")
            {
                var propertyNullableInfo = GetNullableMetadataInfo(
                    metadataReader,
                    declaringTypeHandle,
                    GetParameterCustomAttributes(metadataReader, method, sequenceNumber: 0),
                    methodHandle);
                propertyType = ApplyNullableReferenceType(signature.Signature.ReturnType, propertyNullableInfo);
                block = block with { Getter = methodHandle, PropertyType = propertyType, Order = Math.Min(block.Order, MetadataTokens.GetToken(methodHandle)) };
            }
            else
            {
                var propertyNullableInfo = GetNullableMetadataInfo(
                    metadataReader,
                    declaringTypeHandle,
                    GetParameterCustomAttributes(metadataReader, method, sequenceNumber: 2),
                    methodHandle);
                propertyType = ApplyNullableReferenceType(signature.Signature.ParameterTypes[1], propertyNullableInfo);
                block = block with { Setter = methodHandle, PropertyType = propertyType, Order = Math.Min(block.Order, MetadataTokens.GetToken(methodHandle)) };
            }

            blocks[key] = block;
        }

        var result = new List<ExtensionPropertyBlockMetadata>();
        foreach (var block in blocks.Values)
        {
            if (block.Getter.IsNil && block.Setter.IsNil)
                continue;

            if (string.IsNullOrEmpty(block.PropertyType))
                continue;

            var content = BuildExtensionPropertyBlock(block);
            var accessors = new List<MethodDefinitionHandle>();
            if (!block.Getter.IsNil)
            {
                accessors.Add(block.Getter);
            }

            if (!block.Setter.IsNil)
            {
                accessors.Add(block.Setter);
            }

            result.Add(new ExtensionPropertyBlockMetadata(content, accessors, block.Order));
        }

        return result;
    }

    private static bool TryGetExtensionPropertyAccessorInfo(MetadataReader metadataReader, MethodDefinition method, out string accessorType, out string propertyName)
    {
        accessorType = string.Empty;
        propertyName = string.Empty;

        if (!method.Attributes.HasFlag(MethodAttributes.Static) || method.Attributes.HasFlag(MethodAttributes.SpecialName))
            return false;

        if (HasAttribute(metadataReader, method.GetCustomAttributes(), "System.Runtime.CompilerServices.ExtensionAttribute"))
            return false;

        var methodName = metadataReader.GetString(method.Name);
        if (methodName.StartsWith("get_", StringComparison.Ordinal))
        {
            accessorType = "get";
            propertyName = methodName[4..];
            return true;
        }

        if (methodName.StartsWith("set_", StringComparison.Ordinal))
        {
            accessorType = "set";
            propertyName = methodName[4..];
            return true;
        }

        return false;
    }

    private static string BuildExtensionPropertyBlock(ExtensionPropertyBuilderMetadata block)
    {
        var accessorDeclarations = new List<string>();
        if (!block.Getter.IsNil)
        {
            accessorDeclarations.Add("get => throw null;");
        }

        if (!block.Setter.IsNil)
        {
            accessorDeclarations.Add("set { }");
        }

        var builder = new StringBuilder();
        builder.Append("extension(");
        builder.Append(block.ReceiverType);
        builder.Append(' ');
        builder.Append(block.ReceiverName);
        builder.AppendLine(")");
        builder.AppendLine("    {");
        builder.Append("        public ");
        builder.Append(block.PropertyType);
        builder.Append(' ');
        builder.Append(block.PropertyName);
        builder.Append(" { ");
        builder.Append(string.Join(' ', accessorDeclarations));
        builder.AppendLine(" }");
        builder.Append("    }");
        return builder.ToString();
    }

    private static string GetParameterName(MetadataReader metadataReader, MethodDefinition method, int sequenceNumber, string fallbackName)
    {
        foreach (var parameterHandle in method.GetParameters())
        {
            var parameter = metadataReader.GetParameter(parameterHandle);
            if (parameter.SequenceNumber == sequenceNumber)
            {
                if (parameter.Name.IsNil)
                    return fallbackName;

                return metadataReader.GetString(parameter.Name);
            }
        }

        return fallbackName;
    }

    private static bool IsVoidReturnType(DecodedType returnType)
    {
        return string.Equals(returnType.Name, "void", StringComparison.Ordinal);
    }

    private static string BuildMethodBody(MetadataReader metadataReader, MethodDefinition method, DecodedMethodSignature signature)
    {
        if (method.Attributes.HasFlag(MethodAttributes.Abstract))
            return ";";

        if (!IsVoidReturnType(signature.Signature.ReturnType))
            return " => throw null;";

        var hasOutParameter = method.GetParameters()
            .Select(metadataReader.GetParameter)
            .Where(static parameter => parameter.SequenceNumber > 0 && parameter.Attributes.HasFlag(ParameterAttributes.Out))
            .Any();
        if (hasOutParameter)
            return " => throw null;";

        return " { }";
    }

    private static List<string> BuildMethodModifiers(MethodAttributes attributes, bool declaringTypeIsInterface)
    {
        var modifiers = new List<string>();
        var shouldEmitAccessibility = !(declaringTypeIsInterface && attributes.HasFlag(MethodAttributes.Abstract));
        if (shouldEmitAccessibility)
        {
            var accessibility = GetAccessibility(attributes);
            if (!string.IsNullOrEmpty(accessibility))
            {
                modifiers.Add(accessibility);
            }
        }

        if (attributes.HasFlag(MethodAttributes.Static))
        {
            modifiers.Add("static");
        }

        if (declaringTypeIsInterface)
        {
            return modifiers;
        }

        if (attributes.HasFlag(MethodAttributes.Abstract))
        {
            modifiers.Add("abstract");
            return modifiers;
        }

        if (attributes.HasFlag(MethodAttributes.Virtual) && !attributes.HasFlag(MethodAttributes.Final))
        {
            if (attributes.HasFlag(MethodAttributes.NewSlot))
            {
                modifiers.Add("virtual");
            }
            else
            {
                modifiers.Add("override");
            }
        }
        else if (attributes.HasFlag(MethodAttributes.Virtual) &&
                 attributes.HasFlag(MethodAttributes.Final) &&
                 !attributes.HasFlag(MethodAttributes.NewSlot))
        {
            modifiers.Add("sealed");
            modifiers.Add("override");
        }

        return modifiers;
    }

    private static string FormatConstraintsInline(List<string> constraints)
    {
        if (constraints.Count == 0)
            return string.Empty;

        return " " + string.Join(" ", constraints);
    }

    private static bool IsDestructor(MethodDefinition method, DecodedMethodSignature signature, string name)
    {
        return string.Equals(name, "Finalize", StringComparison.Ordinal) &&
               !method.Attributes.HasFlag(MethodAttributes.Static) &&
               signature.Signature.ParameterTypes.Length == 0;
    }

    private static bool IsExplicitInterfaceImplementation(MethodAttributes attributes, string name)
    {
        return attributes.HasFlag(MethodAttributes.Private) &&
               attributes.HasFlag(MethodAttributes.Final) &&
               attributes.HasFlag(MethodAttributes.Virtual) &&
               name.Contains('.', StringComparison.Ordinal);
    }

    private static bool IsOperatorMethod(string methodName)
    {
        return methodName.StartsWith("op_", StringComparison.Ordinal);
    }

    private static bool CanEmitOperator(TypeDefinition declaringType)
    {
        return !(declaringType.Attributes.HasFlag(TypeAttributes.Abstract) && declaringType.Attributes.HasFlag(TypeAttributes.Sealed));
    }

    private static string? TryGetOperatorKeyword(string methodName)
    {
        return methodName switch
        {
            "op_UnaryPlus" => "+",
            "op_UnaryNegation" => "-",
            "op_LogicalNot" => "!",
            "op_OnesComplement" => "~",
            "op_Increment" => "++",
            "op_Decrement" => "--",
            "op_True" => "true",
            "op_False" => "false",
            "op_Implicit" => "implicit",
            "op_Explicit" => "explicit",
            "op_Addition" => "+",
            "op_Subtraction" => "-",
            "op_Multiply" => "*",
            "op_Multiplication" => "*",
            "op_Division" => "/",
            "op_Modulus" => "%",
            "op_BitwiseAnd" => "&",
            "op_BitwiseOr" => "|",
            "op_ExclusiveOr" => "^",
            "op_LeftShift" => "<<",
            "op_RightShift" => ">>",
            "op_UnsignedRightShift" => ">>>",
            "op_Equality" => "==",
            "op_Inequality" => "!=",
            "op_LessThan" => "<",
            "op_GreaterThan" => ">",
            "op_LessThanOrEqual" => "<=",
            "op_GreaterThanOrEqual" => ">=",
            "op_AdditionAssignment" => "+=",
            "op_SubtractionAssignment" => "-=",
            "op_MultiplyAssignment" => "*=",
            "op_MultiplicationAssignment" => "*=",
            "op_DivisionAssignment" => "/=",
            "op_ModulusAssignment" => "%=",
            "op_BitwiseAndAssignment" => "&=",
            "op_BitwiseOrAssignment" => "|=",
            "op_ExclusiveOrAssignment" => "^=",
            "op_LeftShiftAssignment" => "<<=",
            "op_RightShiftAssignment" => ">>=",
            "op_UnsignedRightShiftAssignment" => ">>>=",
            "op_IncrementAssignment" => "++",
            "op_DecrementAssignment" => "--",
            _ => null,
        };
    }

    private static string BuildExplicitInterfaceMethodName(string methodName)
    {
        var separatorIndex = methodName.LastIndexOf('.');
        if (separatorIndex < 0)
            return methodName;

        var interfaceName = methodName[..separatorIndex];
        var memberName = methodName[(separatorIndex + 1)..];
        return interfaceName + "." + memberName;
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
        CustomAttributeHandleCollection? secondaryTargetAttributes = null,
        bool includeDeclaringTypeContext = true)
    {
        ImmutableArray<byte> nullableFlags;
        if (!TryGetNullableFlags(metadataReader, targetAttributes, out nullableFlags) &&
            secondaryTargetAttributes is not null)
        {
            _ = TryGetNullableFlags(metadataReader, secondaryTargetAttributes.Value, out nullableFlags);
        }

        byte nullableContextFlag;
        if (!TryGetNullableContextFlag(metadataReader, targetAttributes, out nullableContextFlag) &&
            !(secondaryTargetAttributes is not null && TryGetNullableContextFlag(metadataReader, secondaryTargetAttributes.Value, out nullableContextFlag)))
        {
            if (!methodHandle.IsNil)
            {
                var method = metadataReader.GetMethodDefinition(methodHandle);
                _ = TryGetNullableContextFlag(metadataReader, method.GetCustomAttributes(), out nullableContextFlag);
            }

            if (nullableContextFlag == 0 && includeDeclaringTypeContext)
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

            if (nullableContextFlag == 0 && includeDeclaringTypeContext && metadataReader.IsAssembly)
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

    private static string FormatDecodedTypeWithoutNullable(DecodedType type)
    {
        var annotationIndex = 0;
        return FormatDecodedType(type, default, ref annotationIndex);
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
        _ = metadataReader;
        var provider = new MetadataTypeNameProvider();
        var signature = method.DecodeSignature(provider, genericContext: null);
        return new DecodedMethodSignature(signature, provider.ContainsIsExternalInitModifier);
    }

    private static DecodedType DecodeFieldType(MetadataReader metadataReader, FieldDefinition field)
    {
        _ = metadataReader;
        var provider = new MetadataTypeNameProvider();
        return field.DecodeSignature(provider, genericContext: null);
    }

    private static MethodSignature<DecodedType> DecodePropertySignature(MetadataReader metadataReader, PropertyDefinition property)
    {
        _ = metadataReader;
        var provider = new MetadataTypeNameProvider();
        return property.DecodeSignature(provider, genericContext: null);
    }

    private static DecodedType DecodeTypeFromEntityHandle(MetadataReader metadataReader, EntityHandle handle)
    {
        var provider = new MetadataTypeNameProvider();
        return handle.Kind switch
        {
            HandleKind.TypeReference => provider.GetTypeFromReference(metadataReader, (TypeReferenceHandle)handle, rawTypeKind: 0x12),
            HandleKind.TypeDefinition => DecodeTypeDefinitionHandle(metadataReader, provider, (TypeDefinitionHandle)handle),
            HandleKind.TypeSpecification => provider.GetTypeFromSpecification(metadataReader, genericContext: null, (TypeSpecificationHandle)handle, rawTypeKind: 0),
            _ => new DecodedType("object", IsReferenceType: true, IsTypeParameter: false),
        };
    }

    private static DecodedType DecodeTypeDefinitionHandle(MetadataReader metadataReader, MetadataTypeNameProvider provider, TypeDefinitionHandle typeDefinitionHandle)
    {
        var typeDefinition = metadataReader.GetTypeDefinition(typeDefinitionHandle);
        var rawTypeKind = GetTypeKeyword(metadataReader, typeDefinition) is "struct" or "enum"
            ? (byte)0x11
            : (byte)0x12;
        return provider.GetTypeFromDefinition(metadataReader, typeDefinitionHandle, rawTypeKind);
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

    private static string BuildGenericArguments(MetadataReader metadataReader, GenericParameterHandleCollection genericParameters)
    {
        if (genericParameters.Count == 0)
            return string.Empty;

        var names = genericParameters
            .Select(metadataReader.GetGenericParameter)
            .Select(parameter => metadataReader.GetString(parameter.Name))
            .ToArray();
        return "<" + string.Join(", ", names) + ">";
    }

    private static bool HasAttribute(MetadataReader metadataReader, CustomAttributeHandleCollection attributes, string attributeTypeFullName)
    {
        foreach (var attributeHandle in attributes)
        {
            var attribute = metadataReader.GetCustomAttribute(attributeHandle);
            if (string.Equals(GetAttributeTypeFullName(metadataReader, attribute), attributeTypeFullName, StringComparison.Ordinal))
                return true;
        }

        return false;
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
        {
            if (HasAttribute(metadataReader, typeDefinition.GetCustomAttributes(), "System.Runtime.CompilerServices.IsByRefLikeAttribute"))
                return "ref struct";

            if (HasAttribute(metadataReader, typeDefinition.GetCustomAttributes(), "System.Runtime.CompilerServices.IsReadOnlyAttribute"))
                return "readonly struct";

            return "struct";
        }

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
        return (attributes & TypeAttributes.VisibilityMask) switch
        {
            TypeAttributes.Public => "public",
            TypeAttributes.NotPublic => "internal",
            TypeAttributes.NestedPublic => "public",
            TypeAttributes.NestedFamily => "protected",
            TypeAttributes.NestedFamORAssem => "protected internal",
            TypeAttributes.NestedFamANDAssem => "private protected",
            TypeAttributes.NestedAssembly => "internal",
            TypeAttributes.NestedPrivate => "private",
            _ => "internal",
        };
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

    private static bool TryGetTypeDefinition(MetadataReader metadataReader, EntityHandle handle, out TypeDefinitionHandle typeDefinitionHandle)
    {
        if (handle.Kind == HandleKind.TypeDefinition)
        {
            typeDefinitionHandle = (TypeDefinitionHandle)handle;
            return true;
        }

        if (handle.Kind == HandleKind.TypeReference)
        {
            var typeReference = metadataReader.GetTypeReference((TypeReferenceHandle)handle);
            var namespaceName = typeReference.Namespace.IsNil ? string.Empty : metadataReader.GetString(typeReference.Namespace);
            var name = metadataReader.GetString(typeReference.Name);
            foreach (var candidateHandle in metadataReader.TypeDefinitions)
            {
                var candidate = metadataReader.GetTypeDefinition(candidateHandle);
                var candidateNamespace = candidate.Namespace.IsNil ? string.Empty : metadataReader.GetString(candidate.Namespace);
                if (string.Equals(candidateNamespace, namespaceName, StringComparison.Ordinal) &&
                    string.Equals(metadataReader.GetString(candidate.Name), name, StringComparison.Ordinal))
                {
                    typeDefinitionHandle = candidateHandle;
                    return true;
                }
            }
        }

        typeDefinitionHandle = default;
        return false;
    }

    private static List<string> BuildAttributes(MetadataReader metadataReader, CustomAttributeHandleCollection attributes)
    {
        var result = new List<string>();
        foreach (var attributeHandle in attributes)
        {
            var attribute = metadataReader.GetCustomAttribute(attributeHandle);
            var attributeTypeFullName = GetAttributeTypeFullName(metadataReader, attribute);
            if (!ShouldIncludeAttribute(attributeTypeFullName))
                continue;

            if (IsCompilerGeneratedRefStructObsoleteAttribute(metadataReader, attribute, attributeTypeFullName))
                continue;

            result.Add(BuildAttribute(metadataReader, attribute, attributeTypeFullName));
        }

        return result;
    }

    private static bool ShouldIncludeAttribute(string? attributeTypeFullName)
    {
        if (string.IsNullOrEmpty(attributeTypeFullName))
            return false;

        if (IrrelevantAttributes.Contains(attributeTypeFullName))
            return false;

        if (string.Equals(attributeTypeFullName, "System.Runtime.CompilerServices.RequiredMemberAttribute", StringComparison.Ordinal))
            return false;

        return attributeTypeFullName.StartsWith("System.", StringComparison.Ordinal);
    }

    private static bool IsCompilerGeneratedRefStructObsoleteAttribute(MetadataReader metadataReader, CustomAttribute attribute, string? attributeTypeFullName)
    {
        if (!string.Equals(attributeTypeFullName, "System.ObsoleteAttribute", StringComparison.Ordinal))
            return false;

        if (!TryReadObsoleteAttributeArguments(metadataReader.GetBlobBytes(attribute.Value), out var message, out var isError))
            return false;

        return isError == true && string.Equals(message, CompilerGeneratedRefStructObsoleteMessage, StringComparison.Ordinal);
    }

    private static string BuildAttribute(MetadataReader metadataReader, CustomAttribute attribute, string attributeTypeFullName)
    {
        var attributeName = BuildAttributeName(attributeTypeFullName);
        var arguments = BuildAttributeArguments(metadataReader, attribute, attributeTypeFullName);
        return "[" + attributeName + arguments + "]";
    }

    private static string ComposeAttributedMember(List<string> attributes, string declaration)
    {
        if (attributes.Count == 0)
            return declaration;

        var builder = new StringBuilder();
        for (var i = 0; i < attributes.Count; i++)
        {
            if (i > 0)
            {
                builder.Append("    ");
            }

            builder.AppendLine(attributes[i]);
        }

        builder.Append("    ");
        builder.Append(declaration);
        return builder.ToString();
    }

    private static string BuildInlineAttributes(List<string> attributes)
    {
        if (attributes.Count == 0)
            return string.Empty;

        var builder = new StringBuilder();
        foreach (var attribute in attributes)
        {
            builder.Append(attribute);
            builder.Append(' ');
        }

        return builder.ToString();
    }

    private static string BuildAttributeName(string attributeTypeFullName)
    {
        var name = attributeTypeFullName;
        if (name.EndsWith("Attribute", StringComparison.Ordinal))
            return name[..^9];

        return name;
    }

    private static string BuildAttributeArguments(MetadataReader metadataReader, CustomAttribute attribute, string attributeTypeFullName)
    {
        if (string.Equals(attributeTypeFullName, "System.CLSCompliantAttribute", StringComparison.Ordinal))
        {
            if (TryReadBooleanAttributeArgument(metadataReader.GetBlobBytes(attribute.Value), out var boolValue))
            {
                return "(" + (boolValue ? "true" : "false") + ")";
            }
        }

        if (string.Equals(attributeTypeFullName, "System.ObsoleteAttribute", StringComparison.Ordinal))
        {
            if (TryReadObsoleteAttributeArguments(metadataReader.GetBlobBytes(attribute.Value), out var message, out var isError))
            {
                return isError is null
                    ? "(" + FormatConstant(message) + ")"
                    : "(" + FormatConstant(message) + ", " + (isError.Value ? "true" : "false") + ")";
            }
        }

        if (string.Equals(attributeTypeFullName, "System.Runtime.Versioning.UnsupportedOSPlatformAttribute", StringComparison.Ordinal))
        {
            if (TryReadStringAttributeArgument(metadataReader.GetBlobBytes(attribute.Value), out var platformName))
            {
                return "(" + FormatConstant(platformName) + ")";
            }
        }

        if (string.Equals(attributeTypeFullName, "System.Diagnostics.CodeAnalysis.NotNullWhenAttribute", StringComparison.Ordinal) ||
            string.Equals(attributeTypeFullName, "System.Diagnostics.CodeAnalysis.MaybeNullWhenAttribute", StringComparison.Ordinal) ||
            string.Equals(attributeTypeFullName, "System.Diagnostics.CodeAnalysis.DoesNotReturnIfAttribute", StringComparison.Ordinal))
        {
            if (TryReadBooleanAttributeArgument(metadataReader.GetBlobBytes(attribute.Value), out var boolValue))
            {
                return "(" + (boolValue ? "true" : "false") + ")";
            }
        }

        if (string.Equals(attributeTypeFullName, "System.Diagnostics.CodeAnalysis.NotNullIfNotNullAttribute", StringComparison.Ordinal))
        {
            if (TryReadStringAttributeArgument(metadataReader.GetBlobBytes(attribute.Value), out var parameterName))
            {
                return "(" + FormatConstant(parameterName) + ")";
            }
        }

        return string.Empty;
    }

    private static bool TryReadBooleanAttributeArgument(byte[] value, out bool result)
    {
        if (value.Length >= 3 && value[0] == 1 && value[1] == 0)
        {
            result = value[2] != 0;
            return true;
        }

        result = false;
        return false;
    }

    private static bool TryReadObsoleteAttributeArguments(byte[] value, out string message, out bool? isError)
    {
        isError = null;

        if (!TryReadStringAttributeArgument(value, out message))
            return false;

        if (value.Length < 4)
            return true;

        var payload = value.AsSpan(2);
        if (!TryReadSerializedString(payload, out _, out var consumed))
            return true;

        if (payload.Length >= consumed + 3)
        {
            isError = payload[consumed] != 0;
        }

        return true;
    }

    private static bool TryReadStringAttributeArgument(byte[] value, out string text)
    {
        if (value.Length < 3 || value[0] != 1 || value[1] != 0)
        {
            text = string.Empty;
            return false;
        }

        var payload = value.AsSpan(2);
        return TryReadSerializedString(payload, out text, out _);
    }

    private static bool TryReadSerializedString(ReadOnlySpan<byte> payload, out string text, out int consumed)
    {
        consumed = 0;
        if (payload.IsEmpty)
        {
            text = string.Empty;
            return false;
        }

        if (payload[0] == 0xFF)
        {
            text = string.Empty;
            consumed = 1;
            return true;
        }

        if (!TryReadCompressedInteger(payload, out var length, out var prefixLength))
        {
            text = string.Empty;
            return false;
        }

        if (payload.Length < prefixLength + length)
        {
            text = string.Empty;
            return false;
        }

        text = Encoding.UTF8.GetString(payload.Slice(prefixLength, length));
        consumed = prefixLength + length;
        return true;
    }

    private static bool TryReadCompressedInteger(ReadOnlySpan<byte> data, out int value, out int consumed)
    {
        value = 0;
        consumed = 0;
        if (data.IsEmpty)
            return false;

        var first = data[0];
        if ((first & 0x80) == 0)
        {
            value = first;
            consumed = 1;
            return true;
        }

        if ((first & 0xC0) == 0x80)
        {
            if (data.Length < 2)
                return false;

            value = ((first & 0x3F) << 8) | data[1];
            consumed = 2;
            return true;
        }

        if ((first & 0xE0) == 0xC0)
        {
            if (data.Length < 4)
                return false;

            value = ((first & 0x1F) << 24) | (data[1] << 16) | (data[2] << 8) | data[3];
            consumed = 4;
            return true;
        }

        return false;
    }

    private static object? DecodeConstantValue(MetadataReader metadataReader, Constant constant)
    {
        var value = metadataReader.GetBlobBytes(constant.Value);
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

        public DecodedType GetGenericInstantiation(DecodedType genericType, ImmutableArray<DecodedType> typeArguments)
        {
            if (string.Equals(genericType.Name, "System.Nullable", StringComparison.Ordinal) && typeArguments.Length == 1)
            {
                return new(typeArguments[0].Name + "?", IsReferenceType: false, IsTypeParameter: false);
            }

            return new(genericType.Name, genericType.IsReferenceType, IsTypeParameter: false, Kind: DecodedTypeKind.GenericInstantiation, TypeArguments: typeArguments);
        }

        public DecodedType GetGenericMethodParameter(object? genericContext, int index) => new("TMethod" + index.ToString(System.Globalization.CultureInfo.InvariantCulture), IsReferenceType: false, IsTypeParameter: true);

        public DecodedType GetGenericTypeParameter(object? genericContext, int index) => new("T" + index.ToString(System.Globalization.CultureInfo.InvariantCulture), IsReferenceType: false, IsTypeParameter: true);

        public DecodedType GetModifiedType(DecodedType modifier, DecodedType unmodifiedType, bool isRequired)
        {
            if (string.Equals(modifier.Name, "System.Runtime.CompilerServices.IsExternalInit", StringComparison.Ordinal))
            {
                ContainsIsExternalInitModifier = true;
            }

            if (string.Equals(modifier.Name, "System.Runtime.InteropServices.InAttribute", StringComparison.Ordinal))
                return unmodifiedType with { HasInModifier = true };

            if (string.Equals(modifier.Name, "System.Runtime.CompilerServices.IsReadOnlyAttribute", StringComparison.Ordinal))
                return unmodifiedType with { HasIsReadOnlyModifier = true };

            if (string.Equals(modifier.Name, "System.Runtime.CompilerServices.RequiresLocationAttribute", StringComparison.Ordinal))
                return unmodifiedType with { HasRequiresLocationModifier = true };

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
                string.IsNullOrEmpty(namespaceName) ? name : namespaceName + "." + name,
                IsReferenceType: rawTypeKind != ElementTypeValueType,
                IsTypeParameter: false);
        }

        public DecodedType GetTypeFromReference(MetadataReader reader, TypeReferenceHandle handle, byte rawTypeKind)
        {
            var type = reader.GetTypeReference(handle);
            var namespaceName = type.Namespace.IsNil ? string.Empty : reader.GetString(type.Namespace);
            var name = RemoveGenericArity(reader.GetString(type.Name));
            return new(
                string.IsNullOrEmpty(namespaceName) ? name : namespaceName + "." + name,
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

    private sealed record ExtensionPropertyBuilderMetadata(
        string ReceiverType,
        string ReceiverName,
        string PropertyName,
        string? PropertyType,
        MethodDefinitionHandle Getter,
        MethodDefinitionHandle Setter,
        int Order);

    private sealed record ExtensionPropertyBlockMetadata(
        string Content,
        IReadOnlyList<MethodDefinitionHandle> Accessors,
        int Order);

    private sealed record ParameterDeclarationMetadata(string Text, bool RequiresNullableDirectives);

    private sealed record DecodedType(
        string Name,
        bool IsReferenceType,
        bool IsTypeParameter,
        DecodedTypeKind Kind = DecodedTypeKind.Simple,
        ImmutableArray<DecodedType> TypeArguments = default,
        DecodedType? ElementType = null,
        bool HasInModifier = false,
        bool HasIsReadOnlyModifier = false,
        bool HasRequiresLocationModifier = false);

    private readonly record struct NullableMetadataInfo(ImmutableArray<byte> Flags, byte ContextFlag);

    private readonly record struct DecodedMethodSignature(MethodSignature<DecodedType> Signature, bool ContainsIsExternalInitModifier);
}
