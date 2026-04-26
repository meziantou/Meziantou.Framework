using System.Collections.Immutable;
using System.Reflection;
using System.Text;

namespace Meziantou.Framework.PublicApiGenerator;

internal static class PublicApiModelBuilder
{
    private static readonly NullabilityInfoContext NullabilityInfoContext = new();
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

    private static readonly HashSet<string> CompilerRuntimeAttributes = new(StringComparer.Ordinal)
    {
        "System.Diagnostics.CodeAnalysis.AllowNullAttribute",
        "System.Diagnostics.CodeAnalysis.DisallowNullAttribute",
        "System.Diagnostics.CodeAnalysis.DoesNotReturnAttribute",
        "System.Diagnostics.CodeAnalysis.DoesNotReturnIfAttribute",
        "System.Diagnostics.CodeAnalysis.MaybeNullAttribute",
        "System.Diagnostics.CodeAnalysis.MaybeNullWhenAttribute",
        "System.Diagnostics.CodeAnalysis.NotNullAttribute",
        "System.Diagnostics.CodeAnalysis.NotNullIfNotNullAttribute",
        "System.Diagnostics.CodeAnalysis.NotNullWhenAttribute",
        "System.SerializableAttribute",
        "System.Runtime.CompilerServices.CallerArgumentExpressionAttribute",
        "System.Runtime.CompilerServices.CallerFilePathAttribute",
        "System.Runtime.CompilerServices.CallerLineNumberAttribute",
        "System.Runtime.CompilerServices.CallerMemberNameAttribute",
        "System.Runtime.CompilerServices.ReferenceAssemblyAttribute",
        "System.Runtime.CompilerServices.NativeIntegerAttribute",
        "System.Runtime.Versioning.SupportedOSPlatformAttribute",
        "System.Runtime.Versioning.UnsupportedOSPlatformAttribute",
        "System.Runtime.Versioning.SupportedOSPlatformGuardAttribute",
        "System.Runtime.Versioning.UnsupportedOSPlatformGuardAttribute",
        "System.Runtime.Versioning.ObsoletedOSPlatformAttribute",
        "System.Runtime.CompilerServices.UnsafeValueTypeAttribute",
    };

    public static PublicApiModel Build(IEnumerable<Type> rootTypes)
    {
        var types = rootTypes
            .Where(type => type.DeclaringType is null)
            .Where(IsExternallyVisible)
            .OrderBy(type => type.Namespace, StringComparer.Ordinal)
            .ThenBy(type => type.FullName, StringComparer.Ordinal)
            .Select(BuildTypeModel)
            .ToImmutableArray();
        return new PublicApiModel(types);
    }

    public static bool IsExternallyVisible(Type type)
    {
        ArgumentNullException.ThrowIfNull(type);

        if (type.IsNested)
        {
            var isTypeVisible = type.IsNestedPublic || type.IsNestedFamily || type.IsNestedFamORAssem;
            return isTypeVisible && IsExternallyVisible(type.DeclaringType!);
        }

        return type.IsPublic;
    }

    private static PublicApiTypeModel BuildTypeModel(Type type)
    {
        var source = BuildTypeDeclaration(type, 0);
        var namespaceName = type.Namespace ?? string.Empty;
        var name = RemoveGenericArity(type.Name);
        var qualifiedName = type.FullName ?? (namespaceName + "." + type.Name);
        return new PublicApiTypeModel(namespaceName, name, qualifiedName, source);
    }

    private static string BuildTypeDeclaration(Type type, int indentationLevel)
    {
        if (IsDelegate(type))
            return BuildDelegate(type, indentationLevel);

        if (type.IsEnum)
            return BuildEnum(type, indentationLevel);

        var sb = new StringBuilder();
        AppendAttributes(sb, type.CustomAttributes, indentationLevel);

        var typeHeader = BuildTypeHeader(type);
        AppendIndentedLine(sb, indentationLevel, typeHeader.Declaration + FormatConstraintsInline(typeHeader.Constraints));

        AppendIndentedLine(sb, indentationLevel, "{");
        if (!IsRecordClass(type) && !IsRecordStruct(type))
        {
            AppendMembers(sb, type, indentationLevel + 1);
        }
        AppendNestedTypes(sb, type, indentationLevel + 1);
        AppendIndentedLine(sb, indentationLevel, "}");
        return sb.ToString();
    }

    private static bool IsDelegate(Type type)
    {
        return type.BaseType == typeof(MulticastDelegate);
    }

    private static void AppendNestedTypes(StringBuilder sb, Type type, int indentationLevel)
    {
        var nestedTypes = type.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic)
            .Where(IsExternallyVisible)
            .Where(static nestedType => !nestedType.Name.Contains('<', StringComparison.Ordinal))
            .OrderBy(static nestedType => nestedType.Name, StringComparer.Ordinal)
            .ToList();
        if (nestedTypes.Count == 0)
            return;

        foreach (var nestedType in nestedTypes)
        {
            sb.Append(BuildTypeDeclaration(nestedType, indentationLevel));
        }
    }

    private static void AppendMembers(StringBuilder sb, Type type, int indentationLevel)
    {
        var members = new List<string>();

        foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
                     .Where(IsExternallyVisible)
                     .Where(static field => !field.IsSpecialName)
                     .OrderBy(static field => field.MetadataToken))
        {
            members.Add(BuildField(field, indentationLevel));
        }

        foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
                     .Where(IsExternallyVisible)
                     .OrderBy(static property => property.MetadataToken))
        {
            members.Add(BuildProperty(property, indentationLevel));
        }

        foreach (var @event in type.GetEvents(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
                     .Where(IsExternallyVisible)
                     .OrderBy(static @event => @event.MetadataToken))
        {
            members.Add(BuildEvent(@event, indentationLevel));
        }

        foreach (var constructor in type.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
                     .Where(IsExternallyVisible)
                     .OrderBy(static constructor => constructor.MetadataToken))
        {
            var constructorText = BuildConstructor(constructor, indentationLevel);
            if (constructorText is not null)
            {
                members.Add(constructorText);
            }
        }

        var methods = type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .Where(IsExternallyVisible)
            .Where(static method => !method.IsSpecialName || IsOperatorMethod(method))
            .Where(static method => !method.Name.Contains('<', StringComparison.Ordinal))
            .OrderBy(static method => method.MetadataToken)
            .ToArray();

        var extensionPropertyBlocks = BuildExtensionPropertyBlocks(methods, indentationLevel);
        var extensionPropertyAccessors = extensionPropertyBlocks.SelectMany(static block => block.Accessors).ToHashSet();
        foreach (var method in methods)
        {
            if (extensionPropertyAccessors.Contains(method))
                continue;

            members.Add(BuildMethod(method, indentationLevel));
        }

        foreach (var extensionPropertyBlock in extensionPropertyBlocks.OrderBy(static block => block.Order))
        {
            members.Add(extensionPropertyBlock.Content);
        }

        for (var i = 0; i < members.Count; i++)
        {
            sb.Append(members[i]);
        }
    }

    private static string BuildDelegate(Type type, int indentationLevel)
    {
        var invokeMethod = type.GetMethod("Invoke", BindingFlags.Public | BindingFlags.Instance) ?? throw new InvalidOperationException("Delegate type must have an Invoke method");
        var sb = new StringBuilder();
        AppendAttributes(sb, type.CustomAttributes, indentationLevel);

        var modifiers = GetTypeAccessibility(type);
        var unsafeModifier = RequiresUnsafeContext(invokeMethod) ? " unsafe" : string.Empty;
        var genericArguments = BuildGenericArguments(type);
        var parameters = string.Join(", ", invokeMethod.GetParameters().Select(static parameter => BuildParameter(parameter, isExtensionReceiver: false)));
        var returnType = FormatReturnType(invokeMethod.ReturnParameter);
        var constraints = BuildTypeConstraints(type, indentationLevel);

        AppendIndentedLine(sb, indentationLevel, $"{modifiers}{unsafeModifier} delegate {returnType} {EscapeIdentifier(RemoveGenericArity(type.Name))}{genericArguments}({parameters}){FormatConstraintsInline(constraints)};");

        return sb.ToString();
    }

    private static string BuildEnum(Type type, int indentationLevel)
    {
        var sb = new StringBuilder();
        AppendAttributes(sb, type.CustomAttributes, indentationLevel);

        var baseType = Enum.GetUnderlyingType(type);
        var baseTypeSuffix = baseType == typeof(int) ? string.Empty : " : " + FormatType(baseType);
        AppendIndentedLine(sb, indentationLevel, $"{GetTypeAccessibility(type)} enum {EscapeIdentifier(type.Name)}{baseTypeSuffix}");
        AppendIndentedLine(sb, indentationLevel, "{");

        var fields = type.GetFields(BindingFlags.Public | BindingFlags.Static)
            .OrderBy(static field => field.MetadataToken)
            .ToArray();
        for (var i = 0; i < fields.Length; i++)
        {
            var separator = i == fields.Length - 1 ? string.Empty : ",";
            var value = Convert.ChangeType(fields[i].GetRawConstantValue(), baseType, System.Globalization.CultureInfo.InvariantCulture);
            AppendIndentedLine(sb, indentationLevel + 1, $"{EscapeIdentifier(fields[i].Name)} = {FormatConstant(value)}{separator}");
        }

        AppendIndentedLine(sb, indentationLevel, "}");
        return sb.ToString();
    }

    private static string BuildField(FieldInfo field, int indentationLevel)
    {
        var sb = new StringBuilder();
        AppendAttributes(sb, field.CustomAttributes, indentationLevel);

        var modifiers = new List<string> { GetFieldAccessibility(field) };
        var isByRefField = field.FieldType.IsByRef;
        if (field.IsStatic && !field.IsLiteral)
        {
            modifiers.Add("static");
        }

        if (field.IsInitOnly && !isByRefField)
        {
            modifiers.Add("readonly");
        }

        if (field.IsLiteral)
        {
            modifiers.Add("const");
        }

        var fieldNullability = NullabilityInfoContext.Create(field);
        var fieldType = isByRefField
            ? BuildByRefFieldType(field, fieldNullability)
            : FormatType(field.FieldType, fieldNullability);
        var declaration = $"{string.Join(' ', modifiers.Where(static value => !string.IsNullOrEmpty(value)))} {fieldType} {EscapeIdentifier(field.Name)}";
        if (field.IsLiteral)
        {
            declaration += " = " + FormatConstant(field.GetRawConstantValue());
        }

        declaration += ";";
        AppendIndentedLine(sb, indentationLevel, declaration);
        return sb.ToString();
    }

    private static string BuildProperty(PropertyInfo property, int indentationLevel)
    {
        var sb = new StringBuilder();
        AppendAttributes(sb, property.CustomAttributes, indentationLevel);

        var accessors = new[] { property.GetMethod, property.SetMethod }.Where(static method => method is not null).Cast<MethodInfo>().ToArray();
        var representativeAccessor = accessors.OrderByDescending(GetAccessibilityRank).First();
        var propertyAccessibility = GetMethodAccessibility(representativeAccessor);
        var modifiers = new List<string>();
        var shouldEmitAccessibility = !(representativeAccessor.DeclaringType?.IsInterface == true && representativeAccessor.IsAbstract);
        if (shouldEmitAccessibility && !string.IsNullOrEmpty(propertyAccessibility))
        {
            modifiers.Add(propertyAccessibility);
        }

        if (representativeAccessor.IsStatic)
        {
            modifiers.Add("static");
        }

        if (IsRequiredMember(property.CustomAttributes))
        {
            modifiers.Add("required");
        }

        var indexParameters = property.GetMethod?.GetParameters() ?? property.SetMethod?.GetParameters().SkipLast(1).ToArray() ?? [];
        var propertyName = indexParameters.Length > 0
            ? $"this[{string.Join(", ", indexParameters.Select(static parameter => BuildParameter(parameter, isExtensionReceiver: false)))}]"
            : EscapeIdentifier(property.Name);
        var propertyNullability = property.GetMethod is not null
            ? NullabilityInfoContext.Create(property.GetMethod.ReturnParameter)
            : property.SetMethod is not null
                ? NullabilityInfoContext.Create(property.SetMethod.GetParameters().Last())
                : null;
        var accessorDeclarations = new List<string>();

        var getMethod = property.GetMethod;
        if (getMethod is not null && IsExternallyVisible(getMethod))
        {
            var accessorModifier = BuildAccessorModifier(getMethod, representativeAccessor);
            var getAccessor = getMethod.IsAbstract ? "get;" : "get => throw null;";
            accessorDeclarations.Add($"{accessorModifier}{getAccessor}");
        }

        var setMethod = property.SetMethod;
        if (setMethod is not null && IsExternallyVisible(setMethod))
        {
            var accessorKeyword = IsInitOnly(setMethod) ? "init" : "set";
            var accessorModifier = BuildAccessorModifier(setMethod, representativeAccessor);
            var setAccessor = setMethod.IsAbstract ? $"{accessorKeyword};" : $"{accessorKeyword} {{ }}";
            accessorDeclarations.Add($"{accessorModifier}{setAccessor}");
        }

        var accessorText = string.Join(' ', accessorDeclarations);
        var modifiersPrefix = modifiers.Count > 0 ? string.Join(' ', modifiers) + " " : string.Empty;
        AppendIndentedLine(sb, indentationLevel, $"{modifiersPrefix}{FormatType(property.PropertyType, propertyNullability)} {propertyName} {{ {accessorText} }}");
        return sb.ToString();
    }

    private static string BuildEvent(EventInfo @event, int indentationLevel)
    {
        var sb = new StringBuilder();
        AppendAttributes(sb, @event.CustomAttributes, indentationLevel);

        var addMethod = @event.AddMethod ?? throw new InvalidOperationException("Event should have add method");
        var modifiers = new List<string>();
        var accessibility = GetMethodAccessibility(addMethod);
        if (!string.IsNullOrEmpty(accessibility))
        {
            modifiers.Add(accessibility);
        }

        if (addMethod.IsStatic)
        {
            modifiers.Add("static");
        }

        var eventNullability = NullabilityInfoContext.Create(addMethod.GetParameters().Single());
        AppendIndentedLine(sb, indentationLevel, $"{string.Join(' ', modifiers)} event {FormatType(@event.EventHandlerType!, eventNullability)} {EscapeIdentifier(@event.Name)};");
        return sb.ToString();
    }

    private static string? BuildConstructor(ConstructorInfo constructor, int indentationLevel)
    {
        var sb = new StringBuilder();
        AppendAttributes(sb, constructor.CustomAttributes, indentationLevel);

        var accessibility = GetMethodAccessibility(constructor);
        var modifiersPrefix = string.IsNullOrEmpty(accessibility) ? string.Empty : accessibility + " ";
        var unsafeModifier = RequiresUnsafeContext(constructor) ? "unsafe " : string.Empty;
        var typeName = EscapeIdentifier(RemoveGenericArity(constructor.DeclaringType!.Name));
        var parametersList = constructor.GetParameters();
        var parameters = parametersList.Select(static parameter => BuildParameterDeclaration(parameter, isExtensionReceiver: false)).ToArray();
        var requiresNullableDisableDirective = parametersList.Any(static parameter => RequiresNullableDisableDirective(parameter));
        var initializer = BuildConstructorInitializer(constructor);
        if (parametersList.Length == 0 && string.IsNullOrEmpty(initializer))
            return null;

        AppendMemberWithParameters(
            sb,
            indentationLevel,
            modifiersPrefix + unsafeModifier + typeName,
            parameters,
            initializer + BuildConstructorBody(constructor),
            requiresNullableDisableDirective);
        return sb.ToString();
    }

    private static string BuildMethod(MethodInfo method, int indentationLevel)
    {
        var sb = new StringBuilder();
        var isDestructor = IsDestructor(method);
        var isExplicitInterfaceImplementation = IsExplicitInterfaceImplementation(method);
        var methodAttributes = method.CustomAttributes;
        if (IsLibraryImportMethod(method))
        {
            methodAttributes = methodAttributes
                .Where(static attribute => !IsDllImportAttribute(attribute.AttributeType.FullName))
                .Where(static attribute => attribute.AttributeType.FullName != "System.Runtime.InteropServices.PreserveSigAttribute");
        }

        AppendAttributes(sb, methodAttributes, indentationLevel);
        AppendReturnAttributes(sb, method, indentationLevel);

        if (isDestructor)
        {
            var typeName = EscapeIdentifier(RemoveGenericArity(method.DeclaringType!.Name));
            AppendIndentedLine(sb, indentationLevel, $"~{typeName}(){BuildMethodBody(method)}");
            return sb.ToString();
        }

        var modifiers = isExplicitInterfaceImplementation ? [] : BuildMethodModifiers(method);
        var methodName = isExplicitInterfaceImplementation
            ? BuildExplicitInterfaceMethodName(method.Name)
            : EscapeIdentifier(method.Name);
        var isExtensionMethod = IsExtensionMethod(method);
        var parameters = method.GetParameters().Select((parameter, index) => BuildParameterDeclaration(parameter, isExtensionMethod && index == 0)).ToArray();
        var genericArguments = BuildGenericArguments(method);
        var constraints = BuildMethodConstraints(method, indentationLevel);
        var modifiersPrefix = modifiers.Count > 0 ? string.Join(' ', modifiers) + " " : string.Empty;
        var unsafeModifier = RequiresUnsafeContext(method) ? "unsafe " : string.Empty;
        var requiresNullableDisableDirective = RequiresNullableDisableDirective(method.ReturnParameter) ||
                                               method.GetParameters().Any(static parameter => RequiresNullableDisableDirective(parameter));
        var methodBody = BuildMethodBody(method);
        var methodSuffix = FormatConstraintsInline(constraints) + methodBody;

        if (TryGetOperatorKeyword(method.Name) is { } operatorKeyword && CanEmitOperator(method))
        {
            var returnType = FormatReturnType(method.ReturnParameter);
            var methodPrefix = operatorKeyword is "implicit" or "explicit"
                ? $"{modifiersPrefix}{unsafeModifier}{operatorKeyword} operator {returnType}"
                : $"{modifiersPrefix}{unsafeModifier}{returnType} operator {operatorKeyword}";
            AppendMemberWithParameters(sb, indentationLevel, methodPrefix, parameters, methodSuffix, requiresNullableDisableDirective);
        }
        else
        {
            var methodPrefix = $"{modifiersPrefix}{unsafeModifier}{FormatReturnType(method.ReturnParameter)} {methodName}{genericArguments}";
            AppendMemberWithParameters(sb, indentationLevel, methodPrefix, parameters, methodSuffix, requiresNullableDisableDirective);
        }

        return sb.ToString();
    }

    private static void AppendMemberWithParameters(
        StringBuilder sb,
        int indentationLevel,
        string declarationPrefix,
        IReadOnlyList<ParameterDeclaration> parameters,
        string declarationSuffix,
        bool wrapWithNullableDisableDirective = false)
    {
        var hasNullableAnnotations = declarationPrefix.Contains('?', StringComparison.Ordinal) ||
                                     parameters.Any(static parameter => parameter.Text.Contains('?', StringComparison.Ordinal));
        var shouldEmitNullableDirectives = parameters.Any(static parameter => parameter.RequiresNullableDirectives) && hasNullableAnnotations;
        var shouldWrapWithNullableDisableDirective = wrapWithNullableDisableDirective && !hasNullableAnnotations;
        if (shouldWrapWithNullableDisableDirective)
        {
            AppendIndentedLine(sb, indentationLevel, "#nullable disable");
        }

        if (!shouldEmitNullableDirectives)
        {
            AppendIndentedLine(sb, indentationLevel, $"{declarationPrefix}({string.Join(", ", parameters.Select(static parameter => parameter.Text))}){declarationSuffix}");
            if (shouldWrapWithNullableDisableDirective)
            {
                AppendIndentedLine(sb, indentationLevel, "#nullable restore");
            }

            return;
        }

        AppendIndentedLine(sb, indentationLevel, declarationPrefix + "(");
        for (var i = 0; i < parameters.Count; i++)
        {
            var parameter = parameters[i];
            var parameterSuffix = i < parameters.Count - 1 ? "," : string.Empty;
            if (parameter.RequiresNullableDirectives)
            {
                AppendIndentedLine(sb, indentationLevel, "#nullable disable");
                AppendIndentedLine(sb, indentationLevel + 1, parameter.Text + parameterSuffix);
                AppendIndentedLine(sb, indentationLevel, "#nullable restore");
            }
            else
            {
                AppendIndentedLine(sb, indentationLevel + 1, parameter.Text + parameterSuffix);
            }
        }

        AppendIndentedLine(sb, indentationLevel + 1, ")" + declarationSuffix);
        if (shouldWrapWithNullableDisableDirective)
        {
            AppendIndentedLine(sb, indentationLevel, "#nullable restore");
        }
    }

    private static string BuildConstructorBody(ConstructorInfo constructor)
    {
        if (constructor.GetMethodBody() is null)
            return ";";

        return " { }";
    }

    private static string BuildConstructorInitializer(ConstructorInfo constructor)
    {
        var declaringType = constructor.DeclaringType;
        if (declaringType is null || declaringType.IsValueType)
            return string.Empty;

        var baseType = declaringType.BaseType;
        if (baseType is null || baseType == typeof(object) || baseType == typeof(ValueType))
            return string.Empty;

        var baseConstructors = baseType.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
            .Where(IsExternallyVisible)
            .OrderBy(static ctor => ctor.MetadataToken)
            .ToList();
        if (baseConstructors.Count == 0 || baseConstructors.Any(static ctor => ctor.GetParameters().Length == 0))
            return string.Empty;

        var selectedConstructor = baseConstructors[0];
        var arguments = string.Join(", ", selectedConstructor.GetParameters().Select(static parameter =>
        {
            var parameterType = parameter.ParameterType.IsByRef
                ? parameter.ParameterType.GetElementType()!
                : parameter.ParameterType;
            return "default(" + FormatType(parameterType) + ")";
        }));
        return " : base(" + arguments + ")";
    }

    private static string BuildMethodBody(MethodInfo method)
    {
        if (method.IsAbstract)
            return ";";

        if (IsLibraryImportMethod(method))
            return ";";

        if (method.GetMethodBody() is null)
            return ";";

        if (method.ReturnType == typeof(void))
            return BuildVoidMethodBody(method);

        return " => throw null;";
    }

    private static string BuildVoidMethodBody(MethodInfo method)
    {
        if (method.GetParameters().Any(static parameter => parameter.IsOut))
            return " => throw null;";

        return " { }";
    }

    private static List<string> BuildMethodModifiers(MethodInfo method)
    {
        var modifiers = new List<string>();
        var declaringTypeIsInterface = method.DeclaringType?.IsInterface == true;
        var shouldEmitAccessibility = !(declaringTypeIsInterface && method.IsAbstract);
        if (shouldEmitAccessibility)
        {
            var accessibility = GetMethodAccessibility(method);
            if (!string.IsNullOrEmpty(accessibility))
            {
                modifiers.Add(accessibility);
            }
        }

        if (method.IsStatic)
        {
            modifiers.Add("static");
        }

        if (declaringTypeIsInterface)
        {
            return modifiers;
        }

        if (method.IsAbstract)
        {
            modifiers.Add("abstract");
            return modifiers;
        }

        if (method.IsVirtual && !method.IsFinal)
        {
            if (method.GetBaseDefinition() == method)
            {
                modifiers.Add("virtual");
            }
            else
            {
                modifiers.Add("override");
            }
        }
        else if (method.IsVirtual && method.IsFinal && method.GetBaseDefinition() != method)
        {
            modifiers.Add("sealed");
            modifiers.Add("override");
        }

        if (IsLibraryImportMethod(method))
        {
            modifiers.Add("partial");
        }
        else if (method.GetMethodBody() is null && method.GetCustomAttributesData().Any(static attribute => IsDllImportAttribute(attribute.AttributeType.FullName)))
        {
            modifiers.Add("extern");
        }

        return modifiers;
    }

    private static ParameterDeclaration BuildParameterDeclaration(ParameterInfo parameter, bool isExtensionReceiver)
    {
        var sb = new StringBuilder();
        AppendInlineAttributes(sb, parameter.CustomAttributes);

        if (isExtensionReceiver)
        {
            sb.Append("this ");
        }

        if (parameter.IsOut)
        {
            sb.Append("out ");
        }
        else if (parameter.ParameterType.IsByRef)
        {
            if (parameter.IsIn)
            {
                sb.Append(IsRefReadOnlyParameter(parameter) ? "ref readonly " : "in ");
            }
            else
            {
                sb.Append("ref ");
            }
        }
        else if (IsParamsParameter(parameter))
        {
            sb.Append("params ");
        }

        var parameterType = parameter.ParameterType.IsByRef ? parameter.ParameterType.GetElementType()! : parameter.ParameterType;
        var parameterNullability = NullabilityInfoContext.Create(parameter);
        sb.Append(FormatType(parameterType, parameterNullability));
        sb.Append(' ');
        sb.Append(EscapeIdentifier(parameter.Name ?? "value"));
        if (parameter.HasDefaultValue)
        {
            sb.Append(" = ");
            sb.Append(FormatConstant(parameter.DefaultValue));
        }

        return new ParameterDeclaration(sb.ToString(), RequiresNullableDirectives(parameterType, parameterNullability));
    }

    private static string BuildParameter(ParameterInfo parameter, bool isExtensionReceiver)
    {
        return BuildParameterDeclaration(parameter, isExtensionReceiver).Text;
    }

    private static bool RequiresNullableDirectives(Type parameterType, NullabilityInfo nullabilityInfo)
    {
        if (parameterType.IsByRef || parameterType.IsPointer)
        {
            var elementType = parameterType.GetElementType();
            return elementType is not null &&
                   nullabilityInfo.ElementType is not null &&
                   RequiresNullableDirectives(elementType, nullabilityInfo.ElementType);
        }

        return !parameterType.IsValueType &&
               !parameterType.IsGenericParameter &&
               nullabilityInfo.ReadState == NullabilityState.Unknown;
    }

    private static bool RequiresNullableDisableDirective(ParameterInfo parameter)
    {
        var parameterType = parameter.ParameterType.IsByRef ? parameter.ParameterType.GetElementType()! : parameter.ParameterType;
        var parameterNullability = NullabilityInfoContext.Create(parameter);
        return RequiresNullableDirectives(parameterType, parameterNullability);
    }

    private static bool RequiresUnsafeContext(MethodBase method)
    {
        if (method is MethodInfo methodInfo && ContainsPointer(methodInfo.ReturnType))
            return true;

        return method.GetParameters().Any(parameter => ContainsPointer(parameter.ParameterType));
    }

    private static bool ContainsPointer(Type type)
    {
        if (type.IsPointer)
            return true;

        if (type.IsByRef || type.IsArray)
        {
            var elementType = type.GetElementType();
            return elementType is not null && ContainsPointer(elementType);
        }

        if (!type.IsGenericType)
            return false;

        return type.GetGenericArguments().Any(ContainsPointer);
    }

    private static bool IsExtensionMethod(MethodInfo method)
    {
        return method.IsStatic &&
               method.GetCustomAttributesData().Any(static attribute => attribute.AttributeType.FullName == "System.Runtime.CompilerServices.ExtensionAttribute");
    }

    private static bool IsOperatorMethod(MethodInfo method)
    {
        return method.IsSpecialName && method.Name.StartsWith("op_", StringComparison.Ordinal);
    }

    private static List<ExtensionPropertyBlockReflection> BuildExtensionPropertyBlocks(IReadOnlyList<MethodInfo> methods, int indentationLevel)
    {
        var blocks = new Dictionary<(Type ReceiverType, string PropertyName), ExtensionPropertyBuilderReflection>();
        foreach (var method in methods)
        {
            if (!TryGetExtensionPropertyAccessorInfo(method, out var accessorType, out var propertyName))
                continue;

            var parameters = method.GetParameters();
            var receiverParameter = parameters[0];
            var receiverType = receiverParameter.ParameterType.IsByRef
                ? receiverParameter.ParameterType.GetElementType()!
                : receiverParameter.ParameterType;
            var key = (receiverType, propertyName);
            if (!blocks.TryGetValue(key, out var block))
            {
                block = new ExtensionPropertyBuilderReflection(receiverParameter, propertyName, Getter: null, Setter: null, Order: method.MetadataToken);
            }

            if (accessorType == "get")
            {
                blocks[key] = block with { Getter = method, Order = Math.Min(block.Order, method.MetadataToken) };
            }
            else
            {
                blocks[key] = block with { Setter = method, Order = Math.Min(block.Order, method.MetadataToken) };
            }
        }

        var result = new List<ExtensionPropertyBlockReflection>();
        foreach (var block in blocks.Values)
        {
            if (block.Getter is null && block.Setter is null)
                continue;

            var content = BuildExtensionPropertyBlock(block, indentationLevel);
            var accessors = new List<MethodInfo>();
            if (block.Getter is not null)
            {
                accessors.Add(block.Getter);
            }

            if (block.Setter is not null)
            {
                accessors.Add(block.Setter);
            }

            result.Add(new ExtensionPropertyBlockReflection(content, accessors, block.Order));
        }

        return result;
    }

    private static bool TryGetExtensionPropertyAccessorInfo(MethodInfo method, out string accessorType, out string propertyName)
    {
        accessorType = string.Empty;
        propertyName = string.Empty;

        var declaringType = method.DeclaringType;
        if (declaringType is null || !(declaringType.IsAbstract && declaringType.IsSealed))
            return false;

        if (!method.IsStatic || method.IsSpecialName)
            return false;

        if (method.GetCustomAttributesData().Any(static attribute => attribute.AttributeType.FullName == "System.Runtime.CompilerServices.ExtensionAttribute"))
            return false;

        if (method.Name.StartsWith("get_", StringComparison.Ordinal))
        {
            if (method.GetParameters().Length < 1)
                return false;

            accessorType = "get";
            propertyName = method.Name[4..];
            return true;
        }

        if (method.Name.StartsWith("set_", StringComparison.Ordinal))
        {
            if (method.GetParameters().Length < 2)
                return false;

            accessorType = "set";
            propertyName = method.Name[4..];
            return true;
        }

        return false;
    }

    private static string BuildExtensionPropertyBlock(ExtensionPropertyBuilderReflection block, int indentationLevel)
    {
        var sb = new StringBuilder();
        var receiverParameter = block.ReceiverParameter;
        var receiverNullability = NullabilityInfoContext.Create(receiverParameter);
        var receiverType = receiverParameter.ParameterType.IsByRef
            ? receiverParameter.ParameterType.GetElementType()!
            : receiverParameter.ParameterType;
        var receiverTypeText = FormatType(receiverType, receiverNullability);
        var receiverName = EscapeIdentifier(receiverParameter.Name ?? "value");

        string propertyType;
        if (block.Getter is not null)
        {
            propertyType = FormatReturnType(block.Getter.ReturnParameter);
        }
        else
        {
            var setterValueParameter = block.Setter!.GetParameters()[1];
            propertyType = FormatType(setterValueParameter.ParameterType, NullabilityInfoContext.Create(setterValueParameter));
        }

        var accessorDeclarations = new List<string>();
        if (block.Getter is not null)
        {
            accessorDeclarations.Add("get => throw null;");
        }

        if (block.Setter is not null)
        {
            accessorDeclarations.Add("set { }");
        }

        AppendIndentedLine(sb, indentationLevel, $"extension({receiverTypeText} {receiverName})");
        AppendIndentedLine(sb, indentationLevel, "{");
        AppendIndentedLine(sb, indentationLevel + 1, $"public {propertyType} {EscapeIdentifier(block.PropertyName)} {{ {string.Join(' ', accessorDeclarations)} }}");
        AppendIndentedLine(sb, indentationLevel, "}");
        return sb.ToString();
    }

    private static bool CanEmitOperator(MethodInfo method)
    {
        var declaringType = method.DeclaringType;
        if (declaringType is null)
            return false;

        return !(declaringType.IsAbstract && declaringType.IsSealed);
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

    private static bool IsParamsParameter(ParameterInfo parameter)
    {
        return parameter.GetCustomAttributesData().Any(static attribute =>
            attribute.AttributeType.FullName is "System.ParamArrayAttribute" or "System.Runtime.CompilerServices.ParamCollectionAttribute");
    }

    private static string FormatReturnType(ParameterInfo returnParameter)
    {
        var returnType = returnParameter.ParameterType;
        if (!returnType.IsByRef)
        {
            var returnNullability = NullabilityInfoContext.Create(returnParameter);
            return FormatType(returnType, returnNullability);
        }

        var elementType = returnType.GetElementType()!;
        var returnNullabilityInfo = NullabilityInfoContext.Create(returnParameter);
        var elementNullability = returnNullabilityInfo.ElementType;
        if (returnParameter.GetRequiredCustomModifiers().Any(static modifier => modifier.FullName == "System.Runtime.InteropServices.InAttribute"))
            return "ref readonly " + FormatType(elementType, elementNullability);

        return "ref " + FormatType(elementType, elementNullability);
    }

    private static string BuildAccessorModifier(MethodInfo accessor, MethodInfo representativeAccessor)
    {
        var accessorRank = GetAccessibilityRank(accessor);
        var representativeRank = GetAccessibilityRank(representativeAccessor);
        if (accessorRank == representativeRank)
            return string.Empty;

        var accessibility = GetMethodAccessibility(accessor);
        return string.IsNullOrEmpty(accessibility) ? string.Empty : accessibility + " ";
    }

    private static bool IsInitOnly(MethodInfo method)
    {
        return method.ReturnParameter.GetRequiredCustomModifiers().Any(static modifier => modifier.FullName == "System.Runtime.CompilerServices.IsExternalInit");
    }

    private static string BuildGenericArguments(Type type)
    {
        if (!type.IsGenericTypeDefinition)
            return string.Empty;

        var declaringTypeGenericArgumentsCount = type.DeclaringType?.GetGenericArguments().Length ?? 0;
        var currentTypeGenericArguments = type.GetGenericArguments().Skip(declaringTypeGenericArgumentsCount);
        return "<" + string.Join(", ", currentTypeGenericArguments.Select(static argument => EscapeIdentifier(argument.Name))) + ">";
    }

    private static string BuildGenericArguments(MethodInfo method)
    {
        if (!method.IsGenericMethodDefinition)
            return string.Empty;

        return "<" + string.Join(", ", method.GetGenericArguments().Select(static argument => EscapeIdentifier(argument.Name))) + ">";
    }

    private static List<string> BuildTypeConstraints(Type type, int indentationLevel)
    {
        if (!type.IsGenericTypeDefinition)
            return [];

        var declaringTypeGenericArgumentsCount = type.DeclaringType?.GetGenericArguments().Length ?? 0;
        return BuildConstraints(type.GetGenericArguments().Skip(declaringTypeGenericArgumentsCount), indentationLevel);
    }

    private static List<string> BuildMethodConstraints(MethodInfo method, int indentationLevel)
    {
        if (!method.IsGenericMethodDefinition)
            return [];

        return BuildConstraints(method.GetGenericArguments(), indentationLevel);
    }

    private static List<string> BuildConstraints(IEnumerable<Type> genericArguments, int indentationLevel)
    {
        var constraints = new List<string>();
        foreach (var genericArgument in genericArguments)
        {
            if (!genericArgument.IsGenericParameter)
                continue;

            var values = new List<string>();
            var genericParameterAttributes = genericArgument.GenericParameterAttributes;
            if (genericParameterAttributes.HasFlag(GenericParameterAttributes.ReferenceTypeConstraint))
            {
                values.Add("class");
            }

            if (genericParameterAttributes.HasFlag(GenericParameterAttributes.NotNullableValueTypeConstraint))
            {
                values.Add("struct");
            }

            var hasStructConstraint = genericParameterAttributes.HasFlag(GenericParameterAttributes.NotNullableValueTypeConstraint);
            foreach (var constraint in genericArgument.GetGenericParameterConstraints())
            {
                if (hasStructConstraint && constraint == typeof(ValueType))
                    continue;

                values.Add(FormatType(constraint));
            }

            if (genericParameterAttributes.HasFlag(GenericParameterAttributes.DefaultConstructorConstraint) && !hasStructConstraint)
            {
                values.Add("new()");
            }

            if (genericParameterAttributes.HasFlag(AllowByRefLikeGenericParameterConstraint))
            {
                values.Add("allows ref struct");
            }

            if (values.Count == 0)
                continue;

            constraints.Add($"where {EscapeIdentifier(genericArgument.Name)} : {string.Join(", ", values)}");
        }

        return constraints;
    }

    private static string FormatConstraintsInline(IReadOnlyList<string> constraints)
    {
        if (constraints.Count == 0)
            return string.Empty;

        return " " + string.Join(" ", constraints);
    }

    private static bool IsRefReadOnlyParameter(ParameterInfo parameter)
    {
        return parameter.GetCustomAttributesData().Any(static attribute => attribute.AttributeType.FullName == "System.Runtime.CompilerServices.RequiresLocationAttribute");
    }

    private static string BuildByRefFieldType(FieldInfo field, NullabilityInfo fieldNullability)
    {
        var elementType = field.FieldType.GetElementType()!;
        var elementNullability = fieldNullability.ElementType;
        var isRefReadonly = field.CustomAttributes.Any(static attribute => attribute.AttributeType.FullName == "System.Runtime.CompilerServices.IsReadOnlyAttribute");
        if (field.IsInitOnly)
        {
            return isRefReadonly
                ? "readonly ref readonly " + FormatType(elementType, elementNullability)
                : "readonly ref " + FormatType(elementType, elementNullability);
        }

        return isRefReadonly
            ? "ref readonly " + FormatType(elementType, elementNullability)
            : "ref " + FormatType(elementType, elementNullability);
    }

    private static (string Declaration, IReadOnlyList<string> Constraints) BuildTypeHeader(Type type)
    {
        var modifiers = new List<string> { GetTypeAccessibility(type) };
        if (type.IsAbstract && type.IsSealed)
        {
            modifiers.Add("static");
        }
        else
        {
            if (type.IsAbstract && !type.IsInterface)
            {
                modifiers.Add("abstract");
            }

            if (type.IsSealed && !type.IsValueType && !type.IsEnum)
            {
                modifiers.Add("sealed");
            }
        }

        var hasLibraryImport = type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Any(IsLibraryImportMethod);
        if (hasLibraryImport && !type.IsInterface)
        {
            modifiers.Add("partial");
        }

        var keyword = GetTypeKeyword(type);
        var typeName = EscapeIdentifier(RemoveGenericArity(type.Name));
        var genericArguments = BuildGenericArguments(type);
        var inheritance = BuildInheritance(type);
        var constraints = BuildTypeConstraints(type, indentationLevel: 0);

        var declaration = $"{string.Join(' ', modifiers.Where(static value => !string.IsNullOrEmpty(value)))} {keyword} {typeName}{genericArguments}{inheritance}";
        return (declaration, constraints);
    }

    private static string BuildInheritance(Type type)
    {
        var baseTypes = new List<string>();
        if (!type.IsInterface &&
            !type.IsValueType &&
            !type.IsEnum &&
            type.BaseType is not null &&
            type.BaseType != typeof(object) &&
            type.BaseType != typeof(ValueType))
        {
            baseTypes.Add(FormatType(type.BaseType));
        }

        var interfaces = type.GetInterfaces()
            .Where(@interface => IsExternallyVisible(@interface) || @interface.IsPublic)
            .OrderBy(static @interface => @interface.FullName, StringComparer.Ordinal)
            .Select(static @interface => FormatType(@interface))
            .ToList();
        baseTypes.AddRange(interfaces);

        if (baseTypes.Count == 0)
            return string.Empty;

        return " : " + string.Join(", ", baseTypes.Distinct(StringComparer.Ordinal));
    }

    private static string GetTypeKeyword(Type type)
    {
        if (type.IsInterface)
            return "interface";

        if (type.IsEnum)
            return "enum";

        if (IsDelegate(type))
            return "delegate";

        if (IsRecordStruct(type))
            return "record struct";

        if (IsRecordClass(type))
            return "record";

        if (type.IsValueType)
        {
            if (type.IsByRefLike)
                return "ref struct";

            if (IsReadOnlyStruct(type))
                return "readonly struct";

            return "struct";
        }

        return "class";
    }

    private static bool IsRecordClass(Type type)
    {
        if (type.IsValueType || type.IsInterface || type.IsEnum)
            return false;

        return type.GetMethod("<Clone>$", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly) is not null;
    }

    private static bool IsRecordStruct(Type type)
    {
        if (!type.IsValueType || type.IsEnum)
            return false;

        return type.GetMethod("PrintMembers", BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly) is not null;
    }

    private static bool IsReadOnlyStruct(Type type)
    {
        return type.IsValueType &&
               type.GetCustomAttributesData().Any(static attribute => attribute.AttributeType.FullName == "System.Runtime.CompilerServices.IsReadOnlyAttribute");
    }

    private static string GetTypeAccessibility(Type type)
    {
        if (type.IsNested)
        {
            if (type.IsNestedPublic)
                return "public";

            if (type.IsNestedFamily)
                return "protected";

            if (type.IsNestedFamORAssem)
                return "protected internal";

            if (type.IsNestedFamANDAssem)
                return "private protected";

            if (type.IsNestedAssembly)
                return "internal";

            if (type.IsNestedPrivate)
                return "private";
        }
        else if (type.IsPublic)
        {
            return "public";
        }

        return "internal";
    }

    private static bool IsExternallyVisible(MethodBase? method)
    {
        if (method is null)
            return false;

        if (method is MethodInfo methodInfo && IsExplicitInterfaceImplementation(methodInfo))
            return IsExternallyVisible(method.DeclaringType!);

        if (method.IsPublic || method.IsFamily || method.IsFamilyOrAssembly)
            return IsExternallyVisible(method.DeclaringType!);

        return false;
    }

    private static bool IsDestructor(MethodInfo method)
    {
        return !method.IsStatic &&
               string.Equals(method.Name, "Finalize", StringComparison.Ordinal) &&
               method.ReturnType == typeof(void) &&
               method.GetParameters().Length == 0 &&
               method.IsFamily &&
               method.IsVirtual &&
               method.GetBaseDefinition().DeclaringType == typeof(object);
    }

    private static bool IsExplicitInterfaceImplementation(MethodInfo method)
    {
        return method.IsPrivate &&
               method.IsFinal &&
               method.IsVirtual &&
               method.Name.Contains('.', StringComparison.Ordinal);
    }

    private static string BuildExplicitInterfaceMethodName(string methodName)
    {
        var separatorIndex = methodName.LastIndexOf('.');
        if (separatorIndex < 0)
            return EscapeIdentifier(methodName);

        var interfaceName = methodName[..separatorIndex];
        var memberName = methodName[(separatorIndex + 1)..];
        return interfaceName + "." + EscapeIdentifier(memberName);
    }

    private static bool IsExternallyVisible(FieldInfo field)
    {
        return field.IsPublic || field.IsFamily || field.IsFamilyOrAssembly;
    }

    private static bool IsExternallyVisible(PropertyInfo property)
    {
        return IsExternallyVisible(property.GetMethod) || IsExternallyVisible(property.SetMethod);
    }

    private static bool IsExternallyVisible(EventInfo @event)
    {
        return IsExternallyVisible(@event.AddMethod) || IsExternallyVisible(@event.RemoveMethod);
    }

    private static string GetFieldAccessibility(FieldInfo field)
    {
        if (field.IsPublic)
            return "public";

        if (field.IsFamily)
            return "protected";

        if (field.IsFamilyOrAssembly)
            return "protected internal";

        if (field.IsFamilyAndAssembly)
            return "private protected";

        if (field.IsAssembly)
            return "internal";

        return "private";
    }

    private static string GetMethodAccessibility(MethodBase method)
    {
        if (method.IsPublic)
            return "public";

        if (method.IsFamily)
            return "protected";

        if (method.IsFamilyOrAssembly)
            return "protected internal";

        if (method.IsFamilyAndAssembly)
            return "private protected";

        if (method.IsAssembly)
            return "internal";

        return "private";
    }

    private static int GetAccessibilityRank(MethodBase method)
    {
        if (method.IsPublic)
            return 5;

        if (method.IsFamilyOrAssembly)
            return 4;

        if (method.IsFamily)
            return 3;

        if (method.IsAssembly)
            return 2;

        if (method.IsFamilyAndAssembly)
            return 1;

        return 0;
    }

    private static void AppendAttributes(StringBuilder sb, IEnumerable<CustomAttributeData> attributes, int indentationLevel)
    {
        foreach (var attribute in attributes.Where(ShouldIncludeAttribute))
        {
            AppendIndentedLine(sb, indentationLevel, BuildAttribute(attribute));
        }
    }

    private static void AppendReturnAttributes(StringBuilder sb, MethodInfo method, int indentationLevel)
    {
        foreach (var attribute in method.ReturnParameter.CustomAttributes.Where(ShouldIncludeAttribute))
        {
            AppendIndentedLine(sb, indentationLevel, "[return: " + BuildAttributeName(attribute.AttributeType) + BuildAttributeArguments(attribute) + "]");
        }
    }

    private static void AppendInlineAttributes(StringBuilder sb, IEnumerable<CustomAttributeData> attributes)
    {
        foreach (var attribute in attributes.Where(ShouldIncludeAttribute))
        {
            sb.Append(BuildAttribute(attribute));
            sb.Append(' ');
        }
    }

    private static bool ShouldIncludeAttribute(CustomAttributeData attribute)
    {
        var fullName = attribute.AttributeType.FullName;
        if (string.IsNullOrEmpty(fullName))
            return false;

        if (IrrelevantAttributes.Contains(fullName))
            return false;

        if (fullName == "System.Runtime.CompilerServices.RequiredMemberAttribute")
            return false;

        if (IsCompilerGeneratedRefStructObsoleteAttribute(attribute))
            return false;

        if (CompilerRuntimeAttributes.Contains(fullName))
            return true;

        if (fullName.StartsWith("System.Runtime.InteropServices.", StringComparison.Ordinal))
            return true;

        if (fullName.StartsWith("System.Diagnostics.CodeAnalysis.", StringComparison.Ordinal))
            return true;

        if (fullName.StartsWith("System.Runtime.Versioning.", StringComparison.Ordinal))
            return true;

        return fullName.StartsWith("System.", StringComparison.Ordinal);
    }

    private static bool IsCompilerGeneratedRefStructObsoleteAttribute(CustomAttributeData attribute)
    {
        if (attribute.AttributeType.FullName != "System.ObsoleteAttribute")
            return false;

        if (attribute.ConstructorArguments.Count != 2)
            return false;

        if (attribute.ConstructorArguments[0].Value is not string message)
            return false;

        if (attribute.ConstructorArguments[1].Value is not bool isError)
            return false;

        return string.Equals(message, CompilerGeneratedRefStructObsoleteMessage, StringComparison.Ordinal) && isError;
    }

    private static bool IsRequiredMember(IEnumerable<CustomAttributeData> attributes)
    {
        return attributes.Any(static attribute => attribute.AttributeType.FullName == "System.Runtime.CompilerServices.RequiredMemberAttribute");
    }

    private static bool IsLibraryImportMethod(MethodInfo method)
    {
        return method.GetCustomAttributesData().Any(static attribute => attribute.AttributeType.FullName == "System.Runtime.InteropServices.LibraryImportAttribute");
    }

    private static bool IsDllImportAttribute(string? fullName)
    {
        return string.Equals(fullName, "System.Runtime.InteropServices.DllImportAttribute", StringComparison.Ordinal);
    }

    private static string BuildAttribute(CustomAttributeData attribute)
    {
        return "[" + BuildAttributeName(attribute.AttributeType) + BuildAttributeArguments(attribute) + "]";
    }

    private static string BuildAttributeName(Type attributeType)
    {
        var name = FormatType(attributeType);
        if (name.EndsWith("Attribute", StringComparison.Ordinal))
        {
            name = name[..^9];
        }

        return name;
    }

    private static string BuildAttributeArguments(CustomAttributeData attribute)
    {
        if (attribute.ConstructorArguments.Count == 0 && attribute.NamedArguments.Count == 0)
            return string.Empty;

        var values = new List<string>(attribute.ConstructorArguments.Count + attribute.NamedArguments.Count);
        values.AddRange(attribute.ConstructorArguments.Select(FormatAttributeArgument));
        values.AddRange(attribute.NamedArguments.Select(FormatNamedAttributeArgument));
        return "(" + string.Join(", ", values) + ")";
    }

    private static string FormatNamedAttributeArgument(CustomAttributeNamedArgument argument)
    {
        return $"{argument.MemberName} = {FormatAttributeArgument(argument.TypedValue)}";
    }

    private static string FormatAttributeArgument(CustomAttributeTypedArgument argument)
    {
        if (argument.Value is null)
            return "null";

        if (argument.ArgumentType == typeof(string))
            return FormatConstant(argument.Value);

        if (argument.ArgumentType == typeof(char))
            return FormatConstant(argument.Value);

        if (argument.ArgumentType == typeof(Type))
            return "typeof(" + FormatType((Type)argument.Value) + ")";

        if (argument.ArgumentType.IsEnum)
            return FormatEnumValue(argument.ArgumentType, argument.Value);

        if (argument.ArgumentType.IsArray)
        {
            if (argument.Value is not IReadOnlyCollection<CustomAttributeTypedArgument> values)
                return "null";

            var elementType = argument.ArgumentType.GetElementType() ?? typeof(object);
            return "new " + FormatType(elementType) + "[] { " + string.Join(", ", values.Select(FormatAttributeArgument)) + " }";
        }

        return FormatConstant(argument.Value);
    }

    private static string FormatType(Type type, NullabilityInfo? nullabilityInfo = null)
    {
        var nullableReference = nullabilityInfo?.ReadState == NullabilityState.Nullable;
        if (type.IsByRef)
            return FormatType(type.GetElementType()!, nullabilityInfo?.ElementType);

        if (type.IsPointer)
            return FormatType(type.GetElementType()!, nullabilityInfo?.ElementType) + "*";

        if (type.IsArray)
        {
            var arrayType = FormatType(type.GetElementType()!, nullabilityInfo?.ElementType) + "[" + new string(',', type.GetArrayRank() - 1) + "]";
            return AppendNullableSuffix(type, arrayType, nullableReference);
        }

        if (type.IsGenericParameter)
            return EscapeIdentifier(type.Name);

        if (type == typeof(void))
            return "void";

        if (type == typeof(bool))
            return "bool";

        if (type == typeof(byte))
            return "byte";

        if (type == typeof(sbyte))
            return "sbyte";

        if (type == typeof(short))
            return "short";

        if (type == typeof(ushort))
            return "ushort";

        if (type == typeof(int))
            return "int";

        if (type == typeof(uint))
            return "uint";

        if (type == typeof(long))
            return "long";

        if (type == typeof(ulong))
            return "ulong";

        if (type == typeof(float))
            return "float";

        if (type == typeof(double))
            return "double";

        if (type == typeof(decimal))
            return "decimal";

        if (type == typeof(char))
            return "char";

        if (type == typeof(string))
            return nullableReference ? "string?" : "string";

        if (type == typeof(object))
            return nullableReference ? "object?" : "object";

        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Nullable<>))
        {
            var nullableGenericArgument = nullabilityInfo?.GenericTypeArguments is { Length: > 0 } genericArguments
                ? genericArguments[0]
                : null;
            return FormatType(type.GetGenericArguments()[0], nullableGenericArgument) + "?";
        }

        return BuildNamedType(type, nullabilityInfo, nullableReference);
    }

    private static string BuildNamedType(Type type, NullabilityInfo? nullabilityInfo, bool nullableReference)
    {
        var name = EscapeIdentifier(RemoveGenericArity(type.Name));
        var genericArguments = type.GetGenericArguments();
        if (type.IsNested)
        {
            var declaringTypeArgumentCount = type.DeclaringType?.GetGenericArguments().Length ?? 0;
            var currentTypeArguments = genericArguments.Skip(declaringTypeArgumentCount);
            var nestedName = BuildNamedType(type.DeclaringType!, nullabilityInfo: null, nullableReference: false) + "." + name;
            if (currentTypeArguments.Any())
            {
                nestedName += "<" + string.Join(", ", currentTypeArguments.Select((argument, index) => FormatType(argument, GetGenericTypeArgumentNullability(nullabilityInfo, index, declaringTypeArgumentCount)))) + ">";
            }

            return AppendNullableSuffix(type, nestedName, nullableReference);
        }

        if (genericArguments.Length > 0)
        {
            name += "<" + string.Join(", ", genericArguments.Select((argument, index) => FormatType(argument, GetGenericTypeArgumentNullability(nullabilityInfo, index, 0)))) + ">";
        }

        if (!string.IsNullOrEmpty(type.Namespace))
            return AppendNullableSuffix(type, type.Namespace + "." + name, nullableReference);

        return AppendNullableSuffix(type, name, nullableReference);
    }

    private static NullabilityInfo? GetGenericTypeArgumentNullability(NullabilityInfo? nullabilityInfo, int index, int nestedOffset)
    {
        if (nullabilityInfo?.GenericTypeArguments is not { Length: > 0 } genericTypeArguments)
            return null;

        var primaryIndex = nestedOffset + index;
        if (primaryIndex < genericTypeArguments.Length)
            return genericTypeArguments[primaryIndex];

        if (index < genericTypeArguments.Length)
            return genericTypeArguments[index];

        return null;
    }

    private static string AppendNullableSuffix(Type type, string name, bool nullableReference)
    {
        if (!nullableReference || type.IsValueType || type.IsGenericParameter || name.EndsWith("?", StringComparison.Ordinal))
            return name;

        return name + "?";
    }

    private static string FormatConstant(object? value)
    {
        return value switch
        {
            null => "null",
            string text => "\"" + text.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal) + "\"",
            char character => "'" + (character == '\'' ? "\\'" : character.ToString()) + "'",
            bool boolean => boolean ? "true" : "false",
            float floatValue => floatValue.ToString("R", System.Globalization.CultureInfo.InvariantCulture) + "f",
            double doubleValue => doubleValue.ToString("R", System.Globalization.CultureInfo.InvariantCulture) + "d",
            decimal decimalValue => decimalValue.ToString(System.Globalization.CultureInfo.InvariantCulture) + "m",
            long longValue => longValue.ToString(System.Globalization.CultureInfo.InvariantCulture) + "L",
            ulong ulongValue => ulongValue.ToString(System.Globalization.CultureInfo.InvariantCulture) + "UL",
            uint uintValue => uintValue.ToString(System.Globalization.CultureInfo.InvariantCulture) + "U",
            short shortValue => shortValue.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ushort ushortValue => ushortValue.ToString(System.Globalization.CultureInfo.InvariantCulture),
            byte byteValue => byteValue.ToString(System.Globalization.CultureInfo.InvariantCulture),
            sbyte sbyteValue => sbyteValue.ToString(System.Globalization.CultureInfo.InvariantCulture),
            int intValue => intValue.ToString(System.Globalization.CultureInfo.InvariantCulture),
            Enum enumValue => FormatEnumValue(enumValue.GetType(), enumValue),
            _ => Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) ?? "default",
        };
    }

    private static string FormatEnumValue(Type enumType, object enumValue)
    {
        var typedEnumValue = Enum.ToObject(enumType, enumValue);
        var text = typedEnumValue.ToString();
        if (string.IsNullOrEmpty(text))
            return "(" + FormatType(enumType) + ")" + Convert.ToString(enumValue, System.Globalization.CultureInfo.InvariantCulture);

        if (char.IsDigit(text[0]) || text[0] == '-')
            return "(" + FormatType(enumType) + ")" + Convert.ToString(enumValue, System.Globalization.CultureInfo.InvariantCulture);

        if (text.Contains(", ", StringComparison.Ordinal))
        {
            return string.Join(
                " | ",
                text.Split(", ", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Select(memberName => FormatType(enumType) + "." + memberName));
        }

        return FormatType(enumType) + "." + text;
    }

    private static string RemoveGenericArity(string name)
    {
        var index = name.IndexOf('`', StringComparison.Ordinal);
        if (index < 0)
            return name;

        return name[..index];
    }

    private static string EscapeIdentifier(string identifier)
    {
        if (CSharpKeywords.Contains(identifier))
        {
            return "@" + identifier;
        }

        return identifier;
    }

    private static void AppendIndentedLine(StringBuilder sb, int indentationLevel, string text)
    {
        if (text.Length > 0)
        {
            sb.Append(' ', indentationLevel * 4);
        }

        sb.AppendLine(text);
    }

    private sealed record ParameterDeclaration(string Text, bool RequiresNullableDirectives);
    private sealed record ExtensionPropertyBuilderReflection(ParameterInfo ReceiverParameter, string PropertyName, MethodInfo? Getter, MethodInfo? Setter, int Order);
    private sealed record ExtensionPropertyBlockReflection(string Content, IReadOnlyList<MethodInfo> Accessors, int Order);

    private static readonly HashSet<string> CSharpKeywords = new(StringComparer.Ordinal)
    {
        "abstract", "as", "base", "bool", "break", "byte", "case", "catch", "char", "checked", "class", "const",
        "continue", "decimal", "default", "delegate", "do", "double", "else", "enum", "event", "explicit",
        "extern", "false", "finally", "fixed", "float", "for", "foreach", "goto", "if", "implicit", "in",
        "int", "interface", "internal", "is", "lock", "long", "namespace", "new", "null", "object", "operator",
        "out", "override", "params", "private", "protected", "public", "readonly", "record", "ref", "return",
        "sbyte", "sealed", "short", "sizeof", "stackalloc", "static", "string", "struct", "switch", "this",
        "throw", "true", "try", "typeof", "uint", "ulong", "unchecked", "unsafe", "ushort", "using", "virtual",
        "void", "volatile", "while", "required", "file", "scoped",
    };
}
