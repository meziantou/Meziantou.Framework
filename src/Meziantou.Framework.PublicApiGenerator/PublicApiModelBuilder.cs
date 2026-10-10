using System.Collections.Immutable;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace Meziantou.Framework.PublicApiGenerator;

internal static class PublicApiModelBuilder
{
    private static readonly ConditionalWeakTable<Module, StrongBox<bool>> UpdatedMemorySafetyRulesCache = new();
    private const string CompilerGeneratedRefStructObsoleteMessage = "Types with embedded references are not supported in this version of your compiler.";
    private const string RequiresPreviewFeaturesAttributeFullName = "System.Runtime.Versioning.RequiresPreviewFeaturesAttribute";
    private const string ClosedAttributeFullName = "System.Runtime.CompilerServices.ClosedAttribute";
    private const string IsClosedTypeAttributeFullName = "System.Runtime.CompilerServices.IsClosedTypeAttribute";
    private const string UnionAttributeFullName = "System.Runtime.CompilerServices.UnionAttribute";
    private const string IUnionInterfaceFullName = "System.Runtime.CompilerServices.IUnion";
    private const string MemorySafetyRulesAttributeFullName = "System.Runtime.CompilerServices.MemorySafetyRulesAttribute";
    private const string RequiresUnsafeAttributeFullName = "System.Diagnostics.CodeAnalysis.RequiresUnsafeAttribute";
    private const GenericParameterAttributes AllowByRefLikeGenericParameterConstraint = (GenericParameterAttributes)0x20;

    private static readonly HashSet<string> IrrelevantAttributes = new(StringComparer.Ordinal)
    {
        "System.CodeDom.Compiler.GeneratedCodeAttribute",
        "System.ComponentModel.EditorBrowsableAttribute",
        "System.Runtime.CompilerServices.AsyncStateMachineAttribute",
        "System.Runtime.CompilerServices.AsyncIteratorStateMachineAttribute",
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
        "System.Runtime.CompilerServices.ClosedAttribute",
        "System.Runtime.CompilerServices.IsClosedTypeAttribute",
        MemorySafetyRulesAttributeFullName,
        RequiresUnsafeAttributeFullName,
        "System.Reflection.AssemblyCompanyAttribute",
        "System.Reflection.AssemblyConfigurationAttribute",
        "System.Reflection.AssemblyCopyrightAttribute",
        "System.Reflection.AssemblyDescriptionAttribute",
        "System.Reflection.AssemblyFileVersionAttribute",
        "System.Reflection.AssemblyInformationalVersionAttribute",
        "System.Reflection.AssemblyProductAttribute",
        "System.Reflection.AssemblyTitleAttribute",
        "System.Reflection.AssemblyTrademarkAttribute",

        // Represented with the C# syntax: tuple element names, dynamic, nint and decimal default values
        "System.Runtime.CompilerServices.TupleElementNamesAttribute",
        "System.Runtime.CompilerServices.DynamicAttribute",
        "System.Runtime.CompilerServices.NativeIntegerAttribute",
        "System.Runtime.CompilerServices.DecimalConstantAttribute",

        // A P/Invoke is written as any other method: the attribute read by the interop source generator is an implementation detail,
        // and so is the attribute that the generator adds to skip the initialization of the locals
        "System.Runtime.InteropServices.LibraryImportAttribute",
        "System.Runtime.CompilerServices.SkipLocalsInitAttribute",

        // Pseudo-attributes: reflection creates them from metadata flags, they are not stored as custom attributes
        "System.SerializableAttribute",
        "System.NonSerializedAttribute",
        "System.Runtime.InteropServices.ComImportAttribute",
        "System.Runtime.InteropServices.DllImportAttribute",
        "System.Runtime.InteropServices.FieldOffsetAttribute",
        "System.Runtime.InteropServices.MarshalAsAttribute",
        "System.Runtime.InteropServices.PreserveSigAttribute",
        "System.Runtime.InteropServices.StructLayoutAttribute",
        "System.Runtime.CompilerServices.SpecialNameAttribute",
        "System.Runtime.CompilerServices.TypeForwardedToAttribute",
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
        "System.Runtime.CompilerServices.CallerArgumentExpressionAttribute",
        "System.Runtime.CompilerServices.CallerFilePathAttribute",
        "System.Runtime.CompilerServices.CallerLineNumberAttribute",
        "System.Runtime.CompilerServices.CallerMemberNameAttribute",
        "System.Runtime.CompilerServices.ReferenceAssemblyAttribute",
        "System.Runtime.Versioning.SupportedOSPlatformAttribute",
        "System.Runtime.Versioning.UnsupportedOSPlatformAttribute",
        "System.Runtime.Versioning.SupportedOSPlatformGuardAttribute",
        "System.Runtime.Versioning.UnsupportedOSPlatformGuardAttribute",
        "System.Runtime.Versioning.ObsoletedOSPlatformAttribute",
        "System.Runtime.CompilerServices.UnsafeValueTypeAttribute",
    };

    public static PublicApiModel Build(string assemblyName, IEnumerable<CustomAttributeData> assemblyAttributes, IEnumerable<Type> rootTypes)
    {
        var types = rootTypes
            .Where(type => type.DeclaringType is null)
            .Where(IsExternallyVisible)
            .OrderBy(type => type.Namespace, StringComparer.Ordinal)
            .ThenBy(type => type.FullName, StringComparer.Ordinal)
            .Select(BuildTypeModel)
            .ToImmutableArray();
        return new PublicApiModel(assemblyName, BuildAssemblyAttributesSource(assemblyAttributes), types);
    }

    private static string BuildAssemblyAttributesSource(IEnumerable<CustomAttributeData> attributes)
    {
        var sb = new StringBuilder();
        foreach (var attribute in attributes.Where(IsRequiresPreviewFeaturesAttribute))
        {
            sb.Append("[assembly: ");
            sb.Append(BuildAttributeName(attribute.AttributeType));
            sb.Append(BuildAttributeArguments(attribute));
            sb.AppendLine("]");
        }

        return sb.ToString();
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
        var isUnionDeclaration = IsUnionDeclarationType(type);
        AppendAttributes(sb, type.CustomAttributes.Where(attribute => !isUnionDeclaration || attribute.AttributeType.FullName != UnionAttributeFullName), indentationLevel);

        var typeHeader = BuildTypeHeader(type, isUnionDeclaration);
        AppendIndentedLine(sb, indentationLevel, typeHeader.Declaration + FormatConstraintsInline(typeHeader.Constraints));

        AppendIndentedLine(sb, indentationLevel, "{");
        AppendMembers(sb, type, indentationLevel + 1, isUnionDeclaration);
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

    private static void AppendMembers(StringBuilder sb, Type type, int indentationLevel, bool isUnionDeclaration)
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
            if (isUnionDeclaration && IsGeneratedUnionValueProperty(property))
                continue;

            members.Add(BuildProperty(property, indentationLevel));
        }

        foreach (var @event in type.GetEvents(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
                     .Where(IsExternallyVisible)
                     .OrderBy(static @event => @event.MetadataToken))
        {
            members.Add(BuildEvent(@event, indentationLevel));
        }

        // Constructors and methods are written in metadata order
        var methodMembers = new List<(int MetadataToken, string Text)>();
        foreach (var constructor in type.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
                     .Where(IsExternallyVisible))
        {
            if (isUnionDeclaration && IsGeneratedUnionCaseConstructor(constructor))
                continue;

            var constructorText = BuildConstructor(constructor, indentationLevel);
            if (constructorText is not null)
            {
                methodMembers.Add((constructor.MetadataToken, constructorText));
            }
        }

        var methods = type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .Where(IsExternallyVisible)
            .Where(static method => !method.IsSpecialName || IsOperatorMethod(method))
            .Where(static method => !IsUnspeakableName(method))
            .OrderBy(static method => method.MetadataToken)
            .ToArray();

        var extensionPropertyBlocks = BuildExtensionPropertyBlocks(methods, indentationLevel);
        var extensionPropertyAccessors = extensionPropertyBlocks.SelectMany(static block => block.Accessors).ToHashSet();
        foreach (var method in methods)
        {
            if (extensionPropertyAccessors.Contains(method))
                continue;

            methodMembers.Add((method.MetadataToken, BuildMethod(method, indentationLevel)));
        }

        members.AddRange(methodMembers.OrderBy(static member => member.MetadataToken).Select(static member => member.Text));

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
        // The unsafe modifier is not allowed on type declarations under the updated memory safety rules
        var unsafeModifier = !HasUpdatedMemorySafetyRules(type.Module) && RequiresUnsafeContext(invokeMethod) ? " unsafe" : string.Empty;
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
        foreach (var field in fields)
        {
            var value = Convert.ChangeType(field.GetRawConstantValue(), baseType, System.Globalization.CultureInfo.InvariantCulture);
            AppendIndentedLine(sb, indentationLevel + 1, $"{EscapeIdentifier(field.Name)} = {FormatConstant(value)},");
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

        // A decimal constant is compiled to a static readonly field, and its value is stored in a DecimalConstantAttribute
        var decimalConstant = field is { IsStatic: true, IsInitOnly: true } && field.FieldType == typeof(decimal) ? GetDecimalConstant(field.GetCustomAttributesData()) : null;
        var isConst = field.IsLiteral || decimalConstant is not null;
        if (field.IsStatic && !isConst)
        {
            modifiers.Add("static");
        }

        if (field.IsInitOnly && !isByRefField && !isConst)
        {
            modifiers.Add("readonly");
        }

        if (isConst)
        {
            modifiers.Add("const");
        }

        if (IsRequiresUnsafeMember(field))
        {
            modifiers.Add("unsafe");
        }

        var fieldNullability = new NullabilityInfoContext().Create(field);
        var fieldAnnotations = CreateTypeAnnotations(field);
        var fieldType = isByRefField
            ? BuildByRefFieldType(field, fieldNullability, fieldAnnotations)
            : FormatType(field.FieldType, fieldNullability, fieldAnnotations);
        var declaration = $"{string.Join(' ', modifiers.Where(static value => !string.IsNullOrEmpty(value)))} {fieldType} {EscapeIdentifier(field.Name)}";
        if (decimalConstant is not null)
        {
            declaration += " = " + FormatConstant(decimalConstant);
        }
        else if (field.IsLiteral)
        {
            declaration += " = " + FormatConstant(field.FieldType, field.GetRawConstantValue());
        }

        declaration += ";";
        AppendIndentedLine(sb, indentationLevel, declaration);
        return sb.ToString();
    }

    private static string BuildProperty(PropertyInfo property, int indentationLevel)
    {
        var sb = new StringBuilder();
        AppendAttributes(sb, property.CustomAttributes, indentationLevel);

        // The compiler moves the flow analysis attributes of a property to its accessors
        var propertyAttributes = property.GetCustomAttributesData();
        if (property.SetMethod is { } valueSetter && IsExternallyVisible(valueSetter))
        {
            AppendAccessorFlowAttributes(sb, propertyAttributes, valueSetter.GetParameters()[^1].GetCustomAttributesData(), indentationLevel, "System.Diagnostics.CodeAnalysis.AllowNullAttribute", "System.Diagnostics.CodeAnalysis.DisallowNullAttribute");
        }

        if (property.GetMethod is { } valueGetter && IsExternallyVisible(valueGetter))
        {
            AppendAccessorFlowAttributes(sb, propertyAttributes, valueGetter.ReturnParameter.GetCustomAttributesData(), indentationLevel, "System.Diagnostics.CodeAnalysis.MaybeNullAttribute", "System.Diagnostics.CodeAnalysis.NotNullAttribute");
        }

        var accessors = new[] { property.GetMethod, property.SetMethod }.Where(static method => method is not null).Cast<MethodInfo>().ToArray();
        var representativeAccessor = accessors.OrderByDescending(GetAccessibilityRank).First();
        var propertyAccessibility = GetMethodAccessibility(representativeAccessor);
        var modifiers = new List<string>();
        var isExplicitInterfaceImplementation = IsExplicitInterfaceImplementation(representativeAccessor);
        if (!isExplicitInterfaceImplementation)
        {
            var shouldEmitAccessibility = !(representativeAccessor.DeclaringType?.IsInterface == true && representativeAccessor.IsAbstract);
            if (shouldEmitAccessibility && !string.IsNullOrEmpty(propertyAccessibility))
            {
                modifiers.Add(propertyAccessibility);
            }

            if (representativeAccessor.IsStatic)
            {
                modifiers.Add("static");
            }

            if (representativeAccessor.DeclaringType?.IsInterface != true)
            {
                AddInheritanceModifiers(modifiers, representativeAccessor);
            }

            if (IsRequiredMember(property.CustomAttributes))
            {
                modifiers.Add("required");
            }
        }

        var getMethod = property.GetMethod is { } getter && IsExternallyVisible(getter) ? getter : null;
        var setMethod = property.SetMethod is { } setter && IsExternallyVisible(setter) ? setter : null;
        var isGetReadOnly = getMethod is not null && IsReadOnlyMember(getMethod);
        var isSetReadOnly = setMethod is not null && IsReadOnlyMember(setMethod);

        // readonly can be set on the property or on its accessors, but not on both
        var isPropertyReadOnly = isGetReadOnly == (getMethod is not null) && isSetReadOnly == (setMethod is not null);
        if (isPropertyReadOnly)
        {
            modifiers.Add("readonly");
            isGetReadOnly = false;
            isSetReadOnly = false;
        }

        var isGetUnsafe = getMethod is not null && IsRequiresUnsafeMember(getMethod);
        var isSetUnsafe = setMethod is not null && IsRequiresUnsafeMember(setMethod);

        // The unsafe modifier can be set on the property or on its accessors, but not on both
        var isPropertyUnsafe = IsRequiresUnsafeMember(property) ||
                               (isGetUnsafe || isSetUnsafe) && isGetUnsafe == (getMethod is not null) && isSetUnsafe == (setMethod is not null);
        if (isPropertyUnsafe)
        {
            modifiers.Add("unsafe");
            isGetUnsafe = false;
            isSetUnsafe = false;
        }

        var indexParameters = property.GetMethod?.GetParameters() ?? property.SetMethod?.GetParameters().SkipLast(1).ToArray() ?? [];
        var explicitInterfaceQualifier = isExplicitInterfaceImplementation ? GetExplicitInterfaceQualifier(property.Name) : string.Empty;
        var propertyName = indexParameters.Length > 0
            ? $"{explicitInterfaceQualifier}this[{string.Join(", ", indexParameters.Select(static parameter => BuildParameter(parameter, isExtensionReceiver: false)))}]"
            : isExplicitInterfaceImplementation
                ? BuildExplicitInterfaceMethodName(property.Name)
                : EscapeIdentifier(property.Name);
        var propertyTypeParameter = property.GetMethod?.ReturnParameter ?? property.SetMethod?.GetParameters().Last();
        var propertyNullability = propertyTypeParameter is not null ? new NullabilityInfoContext().Create(propertyTypeParameter) : null;
        var propertyAnnotations = propertyTypeParameter is not null ? CreateTypeAnnotations(propertyTypeParameter, property.GetCustomAttributesData()) : null;
        var accessorDeclarations = new List<string>();

        if (getMethod is not null)
        {
            var accessorModifier = BuildAccessorModifier(getMethod, representativeAccessor) + (isGetReadOnly ? "readonly " : string.Empty) + (isGetUnsafe ? "unsafe " : string.Empty);
            var getAccessor = getMethod.IsAbstract ? "get;" : "get => throw null;";
            accessorDeclarations.Add($"{accessorModifier}{getAccessor}");
        }

        if (setMethod is not null)
        {
            var accessorKeyword = IsInitOnly(setMethod) ? "init" : "set";
            var accessorModifier = BuildAccessorModifier(setMethod, representativeAccessor) + (isSetReadOnly ? "readonly " : string.Empty) + (isSetUnsafe ? "unsafe " : string.Empty);
            var setAccessor = setMethod.IsAbstract ? $"{accessorKeyword};" : $"{accessorKeyword} {{ }}";
            accessorDeclarations.Add($"{accessorModifier}{setAccessor}");
        }

        var accessorText = string.Join(' ', accessorDeclarations);
        var modifiersPrefix = modifiers.Count > 0 ? string.Join(' ', modifiers) + " " : string.Empty;
        AppendIndentedLine(sb, indentationLevel, $"{modifiersPrefix}{FormatType(property.PropertyType, propertyNullability, propertyAnnotations)} {propertyName} {{ {accessorText} }}");
        return sb.ToString();
    }

    private static void AppendAccessorFlowAttributes(StringBuilder sb, IList<CustomAttributeData> propertyAttributes, IList<CustomAttributeData> accessorAttributes, int indentationLevel, string firstAttributeName, string secondAttributeName)
    {
        foreach (var attribute in accessorAttributes)
        {
            var fullName = attribute.AttributeType.FullName;
            if (fullName != firstAttributeName && fullName != secondAttributeName)
                continue;

            if (propertyAttributes.Any(propertyAttribute => propertyAttribute.AttributeType.FullName == fullName))
                continue;

            AppendIndentedLine(sb, indentationLevel, BuildAttribute(attribute));
        }
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

        if (addMethod.DeclaringType?.IsInterface != true)
        {
            AddInheritanceModifiers(modifiers, addMethod);
        }

        // Event accessors cannot be marked as unsafe individually
        if (IsRequiresUnsafeMember(@event))
        {
            modifiers.Add("unsafe");
        }

        var eventNullability = new NullabilityInfoContext().Create(addMethod.GetParameters().Single());
        var eventType = FormatType(@event.EventHandlerType!, eventNullability, CreateTypeAnnotations(addMethod.GetParameters().Single()));
        if (IsExplicitInterfaceImplementation(addMethod))
        {
            // The explicit implementation of an event has no modifiers, and must declare its accessors
            AppendIndentedLine(sb, indentationLevel, $"event {eventType} {BuildExplicitInterfaceMethodName(@event.Name)} {{ add {{ }} remove {{ }} }}");
            return sb.ToString();
        }

        AppendIndentedLine(sb, indentationLevel, $"{string.Join(' ', modifiers)} event {eventType} {EscapeIdentifier(@event.Name)};");
        return sb.ToString();
    }

    private static string? BuildConstructor(ConstructorInfo constructor, int indentationLevel)
    {
        var sb = new StringBuilder();
        AppendAttributes(sb, constructor.CustomAttributes, indentationLevel);

        var accessibility = GetMethodAccessibility(constructor);
        var modifiersPrefix = string.IsNullOrEmpty(accessibility) ? string.Empty : accessibility + " ";
        var requiresUnsafe = RequiresUnsafe(constructor);
        var unsafeModifier = requiresUnsafe ? "unsafe " : string.Empty;
        var typeName = EscapeIdentifier(RemoveGenericArity(constructor.DeclaringType!.Name));
        var parametersList = constructor.GetParameters();
        var parameters = parametersList.Select(static parameter => BuildParameterDeclaration(parameter, isExtensionReceiver: false)).ToArray();
        var requiresNullableDisableDirective = parametersList.Any(static parameter => RequiresNullableDisableDirective(parameter));
        var initializer = BuildConstructorInitializer(constructor);
        // A parameterless constructor requiring an unsafe context is not equivalent to the implicit one as it doesn't satisfy the new() constraint
        if (parametersList.Length == 0 && string.IsNullOrEmpty(initializer) && !requiresUnsafe && !constructor.CustomAttributes.Any(IsRequiresPreviewFeaturesAttribute))
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
        AppendAttributes(sb, method.CustomAttributes, indentationLevel);
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
        var isOverride = method.GetBaseDefinition() != method;
        var constraints = isExplicitInterfaceImplementation || isOverride
            ? BuildInheritedConstraints(method, [.. parameters.Select(static parameter => parameter.Text), FormatReturnType(method.ReturnParameter)])
            : BuildMethodConstraints(method, indentationLevel);
        var modifiersPrefix = modifiers.Count > 0 ? string.Join(' ', modifiers) + " " : string.Empty;
        var unsafeModifier = RequiresUnsafe(method) ? "unsafe " : string.Empty;
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

        if (IsReadOnlyMember(method))
        {
            modifiers.Add("readonly");
        }

        if (declaringTypeIsInterface)
        {
            return modifiers;
        }

        AddInheritanceModifiers(modifiers, method);
        return modifiers;
    }

    private static void AddInheritanceModifiers(List<string> modifiers, MethodInfo method)
    {
        if (method.IsAbstract)
        {
            modifiers.Add("abstract");
            return;
        }

        if (method.IsVirtual && !method.IsFinal)
        {
            // A method that is its own base definition introduces the member, otherwise it overrides an inherited one
            modifiers.Add(method.GetBaseDefinition() == method ? "virtual" : "override");
        }
        else if (method.IsVirtual && method.IsFinal && method.GetBaseDefinition() != method)
        {
            modifiers.Add("sealed");
            modifiers.Add("override");
        }
    }

    private static ParameterDeclaration BuildParameterDeclaration(ParameterInfo parameter, bool isExtensionReceiver)
    {
        var sb = new StringBuilder();
        var usesDefaultValueSyntax = UsesDefaultValueSyntax(parameter);
        if (parameter.IsOptional && !usesDefaultValueSyntax)
        {
            AppendOptionalParameterAttributes(sb, parameter);
        }

        AppendInlineAttributes(sb, parameter.CustomAttributes);

        if (isExtensionReceiver)
        {
            sb.Append("this ");
        }

        // The [In] and [Out] attributes can also be set on parameters that are not passed by reference (e.g. arrays in interop signatures)
        var isOut = parameter.ParameterType.IsByRef && parameter.IsOut && !parameter.IsIn;
        if (IsScopedParameter(parameter) && !isOut && !IsParamsParameter(parameter))
        {
            sb.Append("scoped ");
        }

        if (isOut)
        {
            sb.Append("out ");
        }
        else if (parameter.ParameterType.IsByRef)
        {
            if (IsRefReadOnlyParameter(parameter))
            {
                sb.Append("ref readonly ");
            }
            else if (IsInParameter(parameter))
            {
                sb.Append("in ");
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
        var parameterNullability = new NullabilityInfoContext().Create(parameter);
        sb.Append(FormatType(parameterType, parameterNullability, CreateTypeAnnotations(parameter)));
        sb.Append(' ');
        sb.Append(EscapeIdentifier(parameter.Name ?? "value"));

        if (usesDefaultValueSyntax)
        {
            sb.Append(" = ");
            sb.Append(FormatDefaultValue(parameterType, parameter.DefaultValue));
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
               !parameterType.IsFunctionPointer &&
               !parameterType.IsGenericParameter &&
               nullabilityInfo.ReadState == NullabilityState.Unknown;
    }

    private static bool RequiresNullableDisableDirective(ParameterInfo parameter)
    {
        var parameterType = parameter.ParameterType.IsByRef ? parameter.ParameterType.GetElementType()! : parameter.ParameterType;
        var parameterNullability = new NullabilityInfoContext().Create(parameter);
        return RequiresNullableDirectives(parameterType, parameterNullability);
    }

    private static bool HasUpdatedMemorySafetyRules(Module module)
    {
        return UpdatedMemorySafetyRulesCache.GetValue(
            module,
            static value => new StrongBox<bool>(HasAttribute(value.GetCustomAttributesData(), MemorySafetyRulesAttributeFullName))).Value;
    }

    private static bool HasAttribute(IEnumerable<CustomAttributeData> attributes, string attributeTypeFullName)
    {
        return attributes.Any(attribute => attribute.AttributeType.FullName == attributeTypeFullName);
    }

    private static bool IsReadOnlyMember(MethodInfo method)
    {
        // Members of a readonly struct are implicitly readonly and carry no attribute, so this only matches per-member readonly
        return method.DeclaringType is { IsValueType: true, IsInterface: false } &&
               HasAttribute(method.GetCustomAttributesData(), "System.Runtime.CompilerServices.IsReadOnlyAttribute");
    }

    private static bool IsRequiresUnsafeMember(MemberInfo member)
    {
        return HasUpdatedMemorySafetyRules(member.Module) &&
               HasAttribute(member.GetCustomAttributesData(), RequiresUnsafeAttributeFullName);
    }

    private static bool RequiresUnsafe(MethodBase method)
    {
        // Under the updated memory safety rules, the compiler marks the members requiring an unsafe context with an attribute.
        // Otherwise, a member requires an unsafe context when a pointer type appears in its signature (compiler compat mode).
        return HasUpdatedMemorySafetyRules(method.Module)
            ? HasAttribute(method.GetCustomAttributesData(), RequiresUnsafeAttributeFullName)
            : RequiresUnsafeContext(method);
    }

    private static bool RequiresUnsafeContext(MethodBase method)
    {
        if (method is MethodInfo methodInfo && ContainsPointer(methodInfo.ReturnType))
            return true;

        return method.GetParameters().Any(parameter => ContainsPointer(parameter.ParameterType));
    }

    private static bool ContainsPointer(Type type)
    {
        if (type.IsPointer || type.IsFunctionPointer)
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
        var receiverNullability = new NullabilityInfoContext().Create(receiverParameter);
        var receiverType = receiverParameter.ParameterType.IsByRef
            ? receiverParameter.ParameterType.GetElementType()!
            : receiverParameter.ParameterType;
        var receiverTypeText = FormatType(receiverType, receiverNullability, CreateTypeAnnotations(receiverParameter));
        var receiverName = EscapeIdentifier(receiverParameter.Name ?? "value");

        string propertyType;
        if (block.Getter is not null)
        {
            propertyType = FormatReturnType(block.Getter.ReturnParameter);
        }
        else
        {
            var setterValueParameter = block.Setter!.GetParameters()[1];
            propertyType = FormatType(setterValueParameter.ParameterType, new NullabilityInfoContext().Create(setterValueParameter), CreateTypeAnnotations(setterValueParameter));
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
            var returnNullability = new NullabilityInfoContext().Create(returnParameter);
            return FormatType(returnType, returnNullability, CreateTypeAnnotations(returnParameter));
        }

        var elementType = returnType.GetElementType()!;
        var returnNullabilityInfo = new NullabilityInfoContext().Create(returnParameter);
        var elementNullability = returnNullabilityInfo.ElementType;
        var elementAnnotations = CreateTypeAnnotations(returnParameter);
        if (returnParameter.GetRequiredCustomModifiers().Any(static modifier => modifier.FullName == "System.Runtime.InteropServices.InAttribute"))
            return "ref readonly " + FormatType(elementType, elementNullability, elementAnnotations);

        return "ref " + FormatType(elementType, elementNullability, elementAnnotations);
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

        // A type nested in a generic type is a generic type definition, even when it does not declare any generic parameter
        var declaringTypeGenericArgumentsCount = type.DeclaringType?.GetGenericArguments().Length ?? 0;
        var currentTypeGenericArguments = type.GetGenericArguments().Skip(declaringTypeGenericArgumentsCount).ToArray();
        if (currentTypeGenericArguments.Length == 0)
            return string.Empty;

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

    // The constraints of an override or of an explicit implementation are inherited, and cannot be repeated.
    // Only the ones that tell what T? means can be written: class, struct, or default when T has none of them.
    private static List<string> BuildInheritedConstraints(MethodInfo method, string[] signatureTypes)
    {
        var constraints = new List<string>();
        if (!method.IsGenericMethodDefinition)
            return constraints;

        foreach (var genericArgument in method.GetGenericArguments())
        {
            var name = EscapeIdentifier(genericArgument.Name);
            var attributes = genericArgument.GenericParameterAttributes;
            if (attributes.HasFlag(GenericParameterAttributes.ReferenceTypeConstraint))
            {
                constraints.Add($"where {name} : class");
            }
            else if (attributes.HasFlag(GenericParameterAttributes.NotNullableValueTypeConstraint))
            {
                constraints.Add($"where {name} : struct");
            }
            else if (signatureTypes.Any(type => ContainsAnnotatedTypeParameter(type, name)))
            {
                constraints.Add($"where {name} : default");
            }
        }

        return constraints;
    }

    // Searches "T?" in a formatted type, where T is not part of a longer or qualified name
    private static bool ContainsAnnotatedTypeParameter(string type, string name)
    {
        var index = 0;
        while ((index = type.IndexOf(name, index, StringComparison.Ordinal)) >= 0)
        {
            var end = index + name.Length;
            var isStartOfName = index == 0 || !(char.IsLetterOrDigit(type[index - 1]) || type[index - 1] is '_' or '.' or '@');
            if (isStartOfName && end < type.Length && type[end] == '?')
                return true;

            index = end;
        }

        return false;
    }

    private static List<string> BuildConstraints(IEnumerable<Type> genericArguments, int indentationLevel)
    {
        _ = indentationLevel;
        var constraints = new List<string>();
        foreach (var genericArgument in genericArguments)
        {
            if (!genericArgument.IsGenericParameter)
                continue;

            var values = new List<string>();
            var genericParameterAttributes = genericArgument.GenericParameterAttributes;
            var hasStructConstraint = genericParameterAttributes.HasFlag(GenericParameterAttributes.NotNullableValueTypeConstraint);

            // The nullable flag of a generic parameter encodes the 'class?' and 'notnull' constraints
            var nullableFlags = GetNullableFlags(genericArgument.GetCustomAttributesData(), (MemberInfo?)genericArgument.DeclaringMethod ?? genericArgument.DeclaringType);
            var nullableFlag = nullableFlags is { Length: > 0 } ? nullableFlags[0] : (byte)0;
            if (genericParameterAttributes.HasFlag(GenericParameterAttributes.ReferenceTypeConstraint))
            {
                values.Add(nullableFlag == 2 ? "class?" : "class");
            }
            else if (!hasStructConstraint && nullableFlag == 1)
            {
                values.Add("notnull");
            }

            if (hasStructConstraint)
            {
                var isUnmanaged = genericArgument.GetCustomAttributesData().Any(static attribute => attribute.AttributeType.FullName == "System.Runtime.CompilerServices.IsUnmanagedAttribute");
                values.Add(isUnmanaged ? "unmanaged" : "struct");
            }

            var constraintTypes = genericArgument.GetGenericParameterConstraints();
            for (var i = 0; i < constraintTypes.Length; i++)
            {
                if (hasStructConstraint && constraintTypes[i] == typeof(ValueType))
                    continue;

                values.Add(FormatType(constraintTypes[i], nullabilityInfo: null, CreateConstraintAnnotations(genericArgument, i, constraintTypes.Length)));
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

    // A DateTime default value is stored in a DateTimeConstantAttribute, and cannot be written as a constant
    private static bool HasConstantDefaultValue(ParameterInfo parameter)
    {
        return parameter.HasDefaultValue && parameter.DefaultValue is not DateTime;
    }

    // A default value can only be written with the C# syntax when all the following parameters have one too, except a params parameter.
    // Otherwise, the parameter is written with the attributes the default value is compiled to.
    private static bool UsesDefaultValueSyntax(ParameterInfo parameter)
    {
        if (!HasConstantDefaultValue(parameter))
            return false;

        var parameters = parameter.Member switch
        {
            MethodBase method => method.GetParameters(),
            PropertyInfo property => property.GetIndexParameters(),
            _ => [],
        };

        // The value parameter of a setter follows the parameters of the indexer
        var count = parameter.Member is MethodInfo { IsSpecialName: true } accessor && accessor.Name.StartsWith("set_", StringComparison.Ordinal) ? parameters.Length - 1 : parameters.Length;
        for (var i = parameter.Position + 1; i < count; i++)
        {
            if (!IsParamsParameter(parameters[i]) && !HasConstantDefaultValue(parameters[i]))
                return false;
        }

        return true;
    }

    // [Optional], followed by the attribute that stores the default value when the parameter has one
    private static void AppendOptionalParameterAttributes(StringBuilder sb, ParameterInfo parameter)
    {
        sb.Append("[System.Runtime.InteropServices.Optional] ");
        if (!HasConstantDefaultValue(parameter))
            return;

        if (parameter.DefaultValue is decimal decimalValue)
        {
            var bits = decimal.GetBits(decimalValue);
            object[] arguments = [(byte)(bits[3] >> 16), (byte)(bits[3] < 0 ? 128 : 0), unchecked((uint)bits[2]), unchecked((uint)bits[1]), unchecked((uint)bits[0])];
            sb.Append("[System.Runtime.CompilerServices.DecimalConstant(").AppendJoin(", ", arguments.Select(FormatConstant)).Append(")] ");
            return;
        }

        // The default value of a value type or of a type parameter needs no attribute
        var parameterType = parameter.ParameterType.IsByRef ? parameter.ParameterType.GetElementType()! : parameter.ParameterType;
        var value = FormatDefaultValue(parameterType, parameter.DefaultValue);
        if (value is "default")
            return;

        sb.Append("[System.Runtime.InteropServices.DefaultParameterValue(").Append(value).Append(")] ");
    }

    private static bool IsInParameter(ParameterInfo parameter)
    {
        return parameter.GetCustomAttributesData().Any(static attribute => attribute.AttributeType.FullName == "System.Runtime.CompilerServices.IsReadOnlyAttribute") ||
               parameter.GetRequiredCustomModifiers().Any(static modifier => modifier.FullName == "System.Runtime.InteropServices.InAttribute");
    }

    private static bool IsRefReadOnlyParameter(ParameterInfo parameter)
    {
        return parameter.GetCustomAttributesData().Any(static attribute => attribute.AttributeType.FullName == "System.Runtime.CompilerServices.RequiresLocationAttribute");
    }

    private static bool IsScopedParameter(ParameterInfo parameter)
    {
        return parameter.GetCustomAttributesData().Any(static attribute => attribute.AttributeType.FullName == "System.Runtime.CompilerServices.ScopedRefAttribute");
    }

    private static string BuildByRefFieldType(FieldInfo field, NullabilityInfo fieldNullability, TypeAnnotations? annotations)
    {
        var elementType = field.FieldType.GetElementType()!;
        var elementNullability = fieldNullability.ElementType;
        var isRefReadonly = field.CustomAttributes.Any(static attribute => attribute.AttributeType.FullName == "System.Runtime.CompilerServices.IsReadOnlyAttribute");
        if (field.IsInitOnly)
        {
            return isRefReadonly
                ? "readonly ref readonly " + FormatType(elementType, elementNullability, annotations)
                : "readonly ref " + FormatType(elementType, elementNullability, annotations);
        }

        return isRefReadonly
            ? "ref readonly " + FormatType(elementType, elementNullability, annotations)
            : "ref " + FormatType(elementType, elementNullability, annotations);
    }

    private static (string Declaration, IReadOnlyList<string> Constraints) BuildTypeHeader(Type type, bool isUnionDeclaration)
    {
        var modifiers = new List<string> { GetTypeAccessibility(type) };
        var isClosedType = IsClosedType(type);
        if (type.IsAbstract && type.IsSealed)
        {
            modifiers.Add("static");
        }
        else
        {
            if (isClosedType)
            {
                modifiers.Add("closed");
            }
            else if (type.IsAbstract && !type.IsInterface)
            {
                modifiers.Add("abstract");
            }

            if (type.IsSealed && !type.IsValueType && !type.IsEnum)
            {
                modifiers.Add("sealed");
            }
        }

        var keyword = GetTypeKeyword(type);
        var typeName = EscapeIdentifier(RemoveGenericArity(type.Name));
        var genericArguments = BuildGenericArguments(type);
        var unionCaseTypes = isUnionDeclaration ? BuildUnionCaseTypes(type) : string.Empty;
        var inheritance = BuildInheritance(type, isUnionDeclaration);
        var constraints = BuildTypeConstraints(type, indentationLevel: 0);

        var declaration = $"{string.Join(' ', modifiers.Where(static value => !string.IsNullOrEmpty(value)))} {keyword} {typeName}{genericArguments}{unionCaseTypes}{inheritance}";
        return (declaration, constraints);
    }

    private static bool IsClosedType(Type type)
    {
        if (type.IsValueType || type.IsInterface || type.IsEnum || IsDelegate(type))
            return false;

        return type.GetCustomAttributesData().Any(static attribute =>
            attribute.AttributeType.FullName is ClosedAttributeFullName or IsClosedTypeAttributeFullName);
    }

    // Reflection does not tell which interfaces a type declares. When the metadata of the assembly cannot be read,
    // the ones implemented by the base type are considered inherited.
    private static IEnumerable<Type> GetDeclaredInterfaces(Type type)
    {
        var interfaces = type.GetInterfaces();
        if (type.IsInterface || type.BaseType is null)
            return interfaces;

        return interfaces.Except(type.BaseType.GetInterfaces());
    }

    private static bool IsVisibleInterface(Type @interface)
    {
        return IsExternallyVisible(@interface) || @interface.IsPublic;
    }

    private static string BuildInheritance(Type type, bool isUnionDeclaration)
    {
        var baseTypes = new List<string>();
        if (!type.IsInterface &&
            !type.IsValueType &&
            !type.IsEnum &&
            type.BaseType is not null &&
            type.BaseType != typeof(object) &&
            type.BaseType != typeof(ValueType))
        {
            // The NullableAttribute of a type describes its base type
            var typeAttributes = type.GetCustomAttributesData();
            baseTypes.Add(FormatType(type.BaseType, nullabilityInfo: null, CreateTypeAnnotations(typeAttributes, nullableFlags: GetNullableFlags(typeAttributes, type))));
        }

        // The interface list is sorted using the formatted names, so the generated API does not depend on the order reported by the runtime.
        // The base type stays first as C# requires it to precede the interfaces.
        // Reflection does not expose the interface implementations of a type, so the interfaces it declares are read from the metadata of the assembly,
        // with their nullable flags and the names of their tuple elements. An interface without flags uses the nullable context of the type.
        byte[]? nullableContextFlags = null;
        if (LoadedAssemblyMetadata.TryGetDeclaredInterfaces(type, out var declaredInterfaces))
        {
            nullableContextFlags = GetNullableFlags([], type);
        }
        else
        {
            declaredInterfaces = [.. GetDeclaredInterfaces(type).Select(static @interface => (@interface, (byte[]?)null, (string?[]?)null))];
        }

        var interfaces = declaredInterfaces
            .Where(static declaration => IsVisibleInterface(declaration.Interface))
            .Where(declaration => !isUnionDeclaration || declaration.Interface.FullName != IUnionInterfaceFullName)
            .Select(declaration =>
            {
                var nullableFlags = declaration.NullableFlags ?? nullableContextFlags;
                return nullableFlags is null && declaration.TupleElementNames is null
                    ? FormatType(declaration.Interface)
                    : FormatType(declaration.Interface, nullabilityInfo: null, new TypeAnnotations(declaration.TupleElementNames, dynamicFlags: null, dynamicIndex: 0, declaredNullability: null, nullableFlags));
            })
            .OrderBy(static value => value, StringComparer.Ordinal);
        baseTypes.AddRange(interfaces);

        if (baseTypes.Count == 0)
            return string.Empty;

        return " : " + string.Join(", ", baseTypes.Distinct(StringComparer.Ordinal));
    }

    private static string GetTypeKeyword(Type type)
    {
        if (IsUnionDeclarationType(type))
            return "union";

        if (type.IsInterface)
            return "interface";

        if (type.IsEnum)
            return "enum";

        if (IsDelegate(type))
            return "delegate";

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

    private static bool IsReadOnlyStruct(Type type)
    {
        return type.IsValueType &&
               type.GetCustomAttributesData().Any(static attribute => attribute.AttributeType.FullName == "System.Runtime.CompilerServices.IsReadOnlyAttribute");
    }

    private static bool IsUnionDeclarationType(Type type)
    {
        return type.IsValueType &&
               !type.IsEnum &&
               type.GetCustomAttributesData().Any(static attribute => attribute.AttributeType.FullName == UnionAttributeFullName) &&
               type.GetInterfaces().Any(static @interface => @interface.FullName == IUnionInterfaceFullName);
    }

    private static string BuildUnionCaseTypes(Type type)
    {
        var caseTypes = type.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(IsGeneratedUnionCaseConstructor)
            .OrderBy(static constructor => constructor.MetadataToken)
            .Select(constructor => FormatType(constructor.GetParameters()[0].ParameterType, new NullabilityInfoContext().Create(constructor.GetParameters()[0]), CreateTypeAnnotations(constructor.GetParameters()[0])))
            .ToArray();
        return "(" + string.Join(", ", caseTypes) + ")";
    }

    private static bool IsGeneratedUnionCaseConstructor(ConstructorInfo constructor)
    {
        return IsExternallyVisible(constructor) &&
               !constructor.IsStatic &&
               constructor.GetParameters() is [var parameter] &&
               !parameter.ParameterType.IsByRef;
    }

    private static bool IsGeneratedUnionValueProperty(PropertyInfo property)
    {
        return string.Equals(property.Name, "Value", StringComparison.Ordinal) &&
               property.PropertyType == typeof(object) &&
               property.GetIndexParameters().Length == 0;
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
            return IsExternallyVisible(method.DeclaringType!) && ImplementsVisibleInterface(methodInfo);

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

    // The name of a compiler-generated method contains angle brackets, and so does the name of the explicit implementation of a member of a generic interface (e.g. "System.IEquatable<T>.Equals")
    private static bool IsUnspeakableName(MethodInfo method)
    {
        var name = method.Name;
        if (IsExplicitInterfaceImplementation(method))
            return name.StartsWith('<', StringComparison.Ordinal) || name[(name.LastIndexOf('.', StringComparison.Ordinal) + 1)..].Contains('<', StringComparison.Ordinal);

        return name.Contains('<', StringComparison.Ordinal);
    }

    // The explicit implementation of a member of an interface that is not visible outside the assembly is not part of the public API.
    // Reflection does not expose the implemented member, so the interface is found from the name of the method (e.g. "Namespace.IInterface<T>.Member").
    private static bool ImplementsVisibleInterface(MethodInfo method)
    {
        var separatorIndex = method.Name.LastIndexOf('.', StringComparison.Ordinal);
        if (separatorIndex < 0)
            return true;

        var interfaceName = RemoveTypeArguments(method.Name.AsSpan(0, separatorIndex));
        foreach (var @interface in method.DeclaringType!.GetInterfaces())
        {
            if (IsVisibleInterface(@interface))
                continue;

            var definition = @interface.IsGenericType ? @interface.GetGenericTypeDefinition() : @interface;
            if (definition.FullName is { } fullName && string.Equals(RemoveGenericArities(fullName), interfaceName, StringComparison.Ordinal))
                return false;
        }

        return true;
    }

    // "Namespace.Outer<T>.IInterface<System.Int32>" => "Namespace.Outer.IInterface"
    private static string RemoveTypeArguments(ReadOnlySpan<char> name)
    {
        var sb = new StringBuilder(name.Length);
        var depth = 0;
        foreach (var c in name)
        {
            if (c == '<')
            {
                depth++;
            }
            else if (c == '>')
            {
                depth--;
            }
            else if (depth == 0)
            {
                sb.Append(c);
            }
        }

        return sb.ToString();
    }

    // "Namespace.Outer`1+IInterface`1" => "Namespace.Outer.IInterface"
    private static string RemoveGenericArities(string fullName)
    {
        var sb = new StringBuilder(fullName.Length);
        for (var i = 0; i < fullName.Length; i++)
        {
            var c = fullName[i];
            if (c == '`')
            {
                while (i + 1 < fullName.Length && char.IsAsciiDigit(fullName[i + 1]))
                {
                    i++;
                }
            }
            else
            {
                sb.Append(c == '+' ? '.' : c);
            }
        }

        return sb.ToString();
    }

    private static string BuildExplicitInterfaceMethodName(string methodName)
    {
        var separatorIndex = methodName.LastIndexOf('.', StringComparison.Ordinal);
        if (separatorIndex < 0)
            return EscapeIdentifier(methodName);

        return GetExplicitInterfaceQualifier(methodName) + EscapeIdentifier(methodName[(separatorIndex + 1)..]);
    }

    // Returns the interface of an explicit implementation, followed by a dot (e.g. "System.IDisposable.")
    private static string GetExplicitInterfaceQualifier(string memberName)
    {
        var separatorIndex = memberName.LastIndexOf('.', StringComparison.Ordinal);
        if (separatorIndex < 0)
            return string.Empty;

        var interfaceName = memberName[..separatorIndex];
        if (interfaceName.StartsWith("global::", StringComparison.Ordinal))
        {
            interfaceName = interfaceName["global::".Length..];
        }

        return interfaceName + ".";
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

    private static bool IsRequiresPreviewFeaturesAttribute(CustomAttributeData attribute)
    {
        return attribute.AttributeType.FullName == RequiresPreviewFeaturesAttributeFullName;
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
            return "typeof(" + FormatTypeOfOperand((Type)argument.Value) + ")";

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

    private static string FormatType(Type type, NullabilityInfo? nullabilityInfo = null, TypeAnnotations? annotations = null)
    {
        // The dynamic flags are stored in a pre-order traversal of the type, with one flag per type
        var isDynamic = annotations?.ReadDynamicFlag() ?? false;
        if (type.IsByRef)
            return FormatType(type.GetElementType()!, nullabilityInfo?.ElementType, annotations);

        var declaredNullability = annotations?.TakeDeclaredNullability();
        if (type.IsPointer)
            return FormatType(type.GetElementType()!, nullabilityInfo?.ElementType, annotations) + "*";

        // The nullable flags are stored in a pre-order traversal of the type: non-generic value types and Nullable<T> have no flag
        var hasNullableFlag = type.IsGenericParameter || !type.IsValueType || (type.IsGenericType && type.GetGenericTypeDefinition() != typeof(Nullable<>));
        var nullableFlag = hasNullableFlag ? annotations?.ReadNullableFlag() : null;

        // The flags are used when reflection has no nullability information for the type (e.g. the constraints of a generic parameter)
        var nullableReference = declaredNullability ?? (nullabilityInfo is not null ? nullabilityInfo.ReadState == NullabilityState.Nullable : nullableFlag == 2);

        if (type.IsFunctionPointer)
        {
            return CSharpTypeFormatter.FormatFunctionPointer(
                type.IsUnmanagedFunctionPointer,
                [.. type.GetFunctionPointerParameterTypes().Select(parameterType => FormatType(parameterType))],
                FormatType(type.GetFunctionPointerReturnType()));
        }

        if (type.IsArray)
        {
            var arrayType = FormatType(type.GetElementType()!, nullabilityInfo?.ElementType, annotations) + "[" + new string(',', type.GetArrayRank() - 1) + "]";
            return AppendNullableSuffix(type, arrayType, nullableReference);
        }

        // NullabilityInfo tells whether a value of the type parameter can be null, which depends on its constraints.
        // The annotation of the type is read from the flags instead.
        if (type.IsGenericParameter)
            return EscapeIdentifier(type.Name) + (nullableFlag == 2 ? "?" : string.Empty);

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

        if (type == typeof(nint))
            return "nint";

        if (type == typeof(nuint))
            return "nuint";

        if (type == typeof(string))
            return nullableReference ? "string?" : "string";

        if (type == typeof(object))
            return (isDynamic ? "dynamic" : "object") + (nullableReference ? "?" : string.Empty);

        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Nullable<>))
        {
            // The nullability of a Nullable<T> describes its underlying type
            return FormatType(type.GetGenericArguments()[0], nullabilityInfo, annotations) + "?";
        }

        if (IsValueTuple(type))
            return FormatValueTuple(type, nullabilityInfo, annotations);

        return BuildNamedType(type, nullabilityInfo, nullableReference, annotations);
    }

    // The type arguments of an unbound generic type are omitted (e.g. typeof(Dictionary<,>))
    private static string FormatTypeOfOperand(Type type)
    {
        return type.IsGenericTypeDefinition
            ? BuildNamedType(type, type.GetGenericArguments(), nullabilityInfo: null, omitTypeArguments: true)
            : FormatType(type);
    }

    private static string BuildNamedType(Type type, NullabilityInfo? nullabilityInfo, bool nullableReference, TypeAnnotations? annotations)
    {
        // The generic arguments of a nested type start with the ones of its containing types
        var name = BuildNamedType(type, type.GetGenericArguments(), nullabilityInfo, annotations: annotations);
        return AppendNullableSuffix(type, name, nullableReference);
    }

    private static bool IsValueTuple(Type type)
    {
        return type.IsConstructedGenericType && type.Namespace is "System" && type.DeclaringType is null && type.Name.StartsWith("ValueTuple`", StringComparison.Ordinal);
    }

    // A tuple of more than 7 elements stores the 8th and following elements in its last type argument, which is a tuple itself
    private static bool IsValueTupleRest(Type[] typeArguments, int index)
    {
        return index == 7 && typeArguments.Length == 8 && IsValueTuple(typeArguments[index]);
    }

    private static int GetValueTupleCardinality(Type type)
    {
        var typeArguments = type.GetGenericArguments();
        return IsValueTupleRest(typeArguments, 7) ? 7 + GetValueTupleCardinality(typeArguments[7]) : typeArguments.Length;
    }

    // A tuple is written with the tuple syntax when its elements are named (e.g. (int Count, string Name))
    private static string FormatValueTuple(Type type, NullabilityInfo? nullabilityInfo, TypeAnnotations? annotations)
    {
        var names = annotations?.ReadTupleElementNames(GetValueTupleCardinality(type));
        var elements = new List<string>();
        AppendValueTupleElements(type, nullabilityInfo, annotations, elements);
        if (names is not null && names.Length == elements.Count && elements.Count > 1)
            return "(" + string.Join(", ", elements.Select((element, index) => names[index] is { } name ? element + " " + EscapeIdentifier(name) : element)) + ")";

        return BuildUnnamedValueTuple(elements, startIndex: 0);
    }

    private static void AppendValueTupleElements(Type type, NullabilityInfo? nullabilityInfo, TypeAnnotations? annotations, List<string> elements)
    {
        var typeArguments = type.GetGenericArguments();
        for (var i = 0; i < typeArguments.Length; i++)
        {
            var typeArgumentNullability = GetGenericTypeArgumentNullability(nullabilityInfo, i, nestedOffset: 0);
            if (IsValueTupleRest(typeArguments, i))
            {
                // The last type argument is a type of its own in the attributes: it has a dynamic flag, a nullable flag, and unused element names
                _ = annotations?.ReadDynamicFlag();
                _ = annotations?.ReadNullableFlag();
                _ = annotations?.ReadTupleElementNames(GetValueTupleCardinality(typeArguments[i]));
                AppendValueTupleElements(typeArguments[i], typeArgumentNullability, annotations, elements);
            }
            else
            {
                elements.Add(FormatType(typeArguments[i], typeArgumentNullability, annotations));
            }
        }
    }

    private static string BuildUnnamedValueTuple(List<string> elements, int startIndex)
    {
        var count = elements.Count - startIndex;
        if (count <= 7)
            return "System.ValueTuple<" + string.Join(", ", elements.Skip(startIndex)) + ">";

        return "System.ValueTuple<" + string.Join(", ", elements.Skip(startIndex).Take(7)) + ", " + BuildUnnamedValueTuple(elements, startIndex + 7) + ">";
    }

    private static string BuildNamedType(Type type, Type[] genericArguments, NullabilityInfo? nullabilityInfo, bool omitTypeArguments = false, TypeAnnotations? annotations = null)
    {
        var name = EscapeIdentifier(RemoveGenericArity(type.Name));
        var declaringTypeArgumentCount = 0;
        if (type.DeclaringType is { } declaringType)
        {
            // Type.DeclaringType is always a generic type definition, so each level takes its type arguments from the nested type
            declaringTypeArgumentCount = declaringType.GetGenericArguments().Length;
            name = BuildNamedType(declaringType, genericArguments, nullabilityInfo, omitTypeArguments, annotations) + "." + name;
        }
        else if (!string.IsNullOrEmpty(type.Namespace))
        {
            name = type.Namespace + "." + name;
        }

        var typeArgumentCount = type.GetGenericArguments().Length;
        if (typeArgumentCount > declaringTypeArgumentCount && omitTypeArguments)
        {
            name += "<" + new string(',', typeArgumentCount - declaringTypeArgumentCount - 1) + ">";
        }
        else if (typeArgumentCount > declaringTypeArgumentCount)
        {
            var currentTypeArguments = genericArguments[declaringTypeArgumentCount..typeArgumentCount];
            name += "<" + string.Join(", ", currentTypeArguments.Select((argument, index) => FormatType(argument, GetGenericTypeArgumentNullability(nullabilityInfo, index, declaringTypeArgumentCount), annotations))) + ">";
        }

        return name;
    }

    private static TypeAnnotations? CreateTypeAnnotations(ParameterInfo parameter, IList<CustomAttributeData>? memberAttributes = null)
    {
        var attributes = parameter.GetCustomAttributesData();

        // A by-ref type and each custom modifier have a dynamic flag before the one of the type
        var skippedDynamicFlags = (parameter.ParameterType.IsByRef ? 1 : 0) + parameter.GetRequiredCustomModifiers().Length + parameter.GetOptionalCustomModifiers().Length;
        var hasFlowAttribute = attributes.Any(IsNullableFlowAttribute) || (memberAttributes is not null && memberAttributes.Any(IsNullableFlowAttribute));
        var declaredNullability = hasFlowAttribute ? GetDeclaredNullability(attributes, parameter.Member) : null;
        var nullableFlags = parameter.ParameterType.ContainsGenericParameters ? GetNullableFlags(attributes, parameter.Member) : null;
        return CreateTypeAnnotations(attributes, skippedDynamicFlags, declaredNullability, nullableFlags);
    }

    // Reflection does not expose the attributes of the constraints, so they are read from the metadata of the assembly
    private static TypeAnnotations? CreateConstraintAnnotations(Type genericParameter, int constraintIndex, int constraintCount)
    {
        if (!LoadedAssemblyMetadata.TryGetConstraintAnnotations(genericParameter, constraintIndex, constraintCount, out var nullableFlags, out var tupleElementNames))
            return null;

        nullableFlags ??= GetNullableFlags([], (MemberInfo?)genericParameter.DeclaringMethod ?? genericParameter.DeclaringType);
        if (nullableFlags is null && tupleElementNames is null)
            return null;

        return new TypeAnnotations(tupleElementNames, dynamicFlags: null, dynamicIndex: 0, declaredNullability: null, nullableFlags);
    }

    private static TypeAnnotations? CreateTypeAnnotations(FieldInfo field)
    {
        var attributes = field.GetCustomAttributesData();
        var skippedDynamicFlags = (field.FieldType.IsByRef ? 1 : 0) + field.GetRequiredCustomModifiers().Length + field.GetOptionalCustomModifiers().Length;
        var declaredNullability = attributes.Any(IsNullableFlowAttribute) ? GetDeclaredNullability(attributes, field.DeclaringType) : null;
        var nullableFlags = field.FieldType.ContainsGenericParameters ? GetNullableFlags(attributes, field.DeclaringType) : null;
        return CreateTypeAnnotations(attributes, skippedDynamicFlags, declaredNullability, nullableFlags);
    }

    private static TypeAnnotations? CreateTypeAnnotations(IList<CustomAttributeData> attributes, int skippedDynamicFlags = 0, bool? declaredNullability = null, byte[]? nullableFlags = null)
    {
        string?[]? tupleElementNames = null;
        bool[]? dynamicFlags = null;
        foreach (var attribute in attributes)
        {
            switch (attribute.AttributeType.FullName)
            {
                case "System.Runtime.CompilerServices.TupleElementNamesAttribute" when attribute.ConstructorArguments is [{ Value: IReadOnlyCollection<CustomAttributeTypedArgument> names }]:
                    tupleElementNames = [.. names.Select(static name => name.Value as string)];
                    break;

                case "System.Runtime.CompilerServices.DynamicAttribute":
                    // Without flags, the type itself is dynamic
                    dynamicFlags = attribute.ConstructorArguments is [{ Value: IReadOnlyCollection<CustomAttributeTypedArgument> flags }]
                        ? [.. flags.Select(static flag => flag.Value is true)]
                        : [.. Enumerable.Repeat(false, skippedDynamicFlags), true];
                    break;
            }
        }

        if (tupleElementNames is null && dynamicFlags is null && declaredNullability is null && nullableFlags is null)
            return null;

        return new TypeAnnotations(tupleElementNames, dynamicFlags, skippedDynamicFlags, declaredNullability, nullableFlags);
    }

    // These attributes change the nullability reported by NullabilityInfo for the type they are applied to
    private static bool IsNullableFlowAttribute(CustomAttributeData attribute)
    {
        return attribute.AttributeType.FullName is "System.Diagnostics.CodeAnalysis.NotNullAttribute"
            or "System.Diagnostics.CodeAnalysis.MaybeNullAttribute"
            or "System.Diagnostics.CodeAnalysis.MaybeNullWhenAttribute";
    }

    // Reads the annotation of the type as written in the source code
    private static bool? GetDeclaredNullability(IList<CustomAttributeData> attributes, MemberInfo? nullableContext)
    {
        // The first flag is the one of the type itself
        return GetNullableFlags(attributes, nullableContext) switch
        {
            [1, ..] => false,
            [2, ..] => true,
            _ => null,
        };
    }

    // Returns the flags of the NullableAttribute, or the flag of the NullableContextAttribute of the containing members. A single flag applies to every type.
    private static byte[]? GetNullableFlags(IList<CustomAttributeData> attributes, MemberInfo? nullableContext)
    {
        var flags = GetNullableFlags(attributes, "System.Runtime.CompilerServices.NullableAttribute");
        for (var member = nullableContext; flags is null && member is not null; member = member.DeclaringType)
        {
            flags = GetNullableFlags(member.GetCustomAttributesData(), "System.Runtime.CompilerServices.NullableContextAttribute");
        }

        if (flags is null && nullableContext is not null)
        {
            flags = GetNullableFlags(nullableContext.Module.Assembly.GetCustomAttributesData(), "System.Runtime.CompilerServices.NullableContextAttribute");
        }

        return flags;
    }

    private static byte[]? GetNullableFlags(IList<CustomAttributeData> attributes, string attributeFullName)
    {
        foreach (var attribute in attributes)
        {
            if (attribute.AttributeType.FullName != attributeFullName || attribute.ConstructorArguments.Count != 1)
                continue;

            return attribute.ConstructorArguments[0].Value switch
            {
                byte value => [value],
                IReadOnlyCollection<CustomAttributeTypedArgument> values => [.. values.Select(static value => value.Value is byte flag ? flag : (byte)0)],
                _ => null,
            };
        }

        return null;
    }

    private static decimal? GetDecimalConstant(IList<CustomAttributeData> attributes)
    {
        foreach (var attribute in attributes)
        {
            if (attribute.AttributeType.FullName != "System.Runtime.CompilerServices.DecimalConstantAttribute" || attribute.ConstructorArguments.Count != 5)
                continue;

            var arguments = attribute.ConstructorArguments;
            if (arguments[0].Value is not byte scale || arguments[1].Value is not byte sign)
                continue;

            var high = unchecked((int)Convert.ToInt64(arguments[2].Value, CultureInfo.InvariantCulture));
            var middle = unchecked((int)Convert.ToInt64(arguments[3].Value, CultureInfo.InvariantCulture));
            var low = unchecked((int)Convert.ToInt64(arguments[4].Value, CultureInfo.InvariantCulture));
            return new decimal(low, middle, high, sign != 0, scale);
        }

        return null;
    }

    // The data that the compiler stores in attributes next to a signature: the names of the tuple elements, the dynamic flags and the nullable flags
    // (all in a pre-order traversal of the type), and the annotation of the type when the flow analysis attributes hide it.
    private sealed class TypeAnnotations(string?[]? tupleElementNames, bool[]? dynamicFlags, int dynamicIndex, bool? declaredNullability, byte[]? nullableFlags)
    {
        private int _tupleElementNameIndex;
        private int _dynamicIndex = dynamicIndex;
        private int _nullableIndex;
        private bool? _declaredNullability = declaredNullability;

        // 1: not annotated, 2: annotated, 0: oblivious
        public byte? ReadNullableFlag()
        {
            if (nullableFlags is null)
                return null;

            var index = _nullableIndex++;
            if (nullableFlags.Length == 1)
                return nullableFlags[0];

            return index < nullableFlags.Length ? nullableFlags[index] : (byte)0;
        }

        public bool ReadDynamicFlag()
        {
            if (dynamicFlags is null)
                return false;

            var index = _dynamicIndex++;
            return index < dynamicFlags.Length && dynamicFlags[index];
        }

        // Returns null when none of the elements is named
        public string?[]? ReadTupleElementNames(int count)
        {
            if (tupleElementNames is null)
                return null;

            var names = new string?[count];
            for (var i = 0; i < count; i++)
            {
                names[i] = _tupleElementNameIndex < tupleElementNames.Length ? tupleElementNames[_tupleElementNameIndex] : null;
                _tupleElementNameIndex++;
            }

            return names.Any(static name => name is not null) ? names : null;
        }

        // Only applies to the outermost type
        public bool? TakeDeclaredNullability()
        {
            var result = _declaredNullability;
            _declaredNullability = null;
            return result;
        }
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

    // The default value of a value type or of a type parameter is read as null
    private static string FormatDefaultValue(Type type, object? value)
    {
        if (value is null && (type.IsGenericParameter || (type.IsValueType && Nullable.GetUnderlyingType(type) is null)))
            return "default";

        return FormatConstant(type, value);
    }

    // A constant of an enum type, or of a nullable enum type, can be read as its underlying value
    private static string FormatConstant(Type type, object? value)
    {
        var enumType = Nullable.GetUnderlyingType(type) ?? type;
        return value is not null && enumType.IsEnum
            ? FormatEnumValue(enumType, value)
            : FormatConstant(value);
    }

    private static string FormatConstant(object? value)
    {
        return value switch
        {
            Enum enumValue => FormatEnumValue(enumValue.GetType(), enumValue),
            _ => CSharpLiteralFormatter.Format(value),
        };
    }

    private static string FormatEnumValue(Type enumType, object enumValue)
    {
        var formattedTypeName = FormatType(enumType);
        var underlyingValue = enumValue is Enum
            ? Convert.ChangeType(enumValue, Enum.GetUnderlyingType(enumType), System.Globalization.CultureInfo.InvariantCulture)
            : enumValue;

        if (TryFormatEnumValueUsingMembers(enumType, formattedTypeName, underlyingValue, out var result))
            return result;

        return "(" + formattedTypeName + ")" + Convert.ToString(underlyingValue, System.Globalization.CultureInfo.InvariantCulture);
    }

    private static bool TryFormatEnumValueUsingMembers(Type enumType, string formattedTypeName, object? enumValue, out string result)
    {
        result = string.Empty;
        if (enumValue is null || !TryGetEnumValueBits(enumValue, out var enumValueBits))
            return false;

        var members = GetEnumMembersDescending(enumType);
        if (members.Length == 0)
            return false;

        foreach (var member in members)
        {
            if (member.Value == enumValueBits)
            {
                result = formattedTypeName + "." + member.Name;
                return true;
            }
        }

        if (!HasAttribute(enumType.GetCustomAttributesData(), "System.FlagsAttribute"))
            return false;

        var remainingValue = enumValueBits;
        var formattedMemberNames = new List<string>();
        foreach (var member in members)
        {
            if (member.Value == 0)
                continue;

            if ((remainingValue & member.Value) != member.Value)
                continue;

            formattedMemberNames.Add(formattedTypeName + "." + member.Name);
            remainingValue &= ~member.Value;
        }

        if (remainingValue != 0 || formattedMemberNames.Count == 0)
            return false;

        // The members are matched from the largest value to the smallest one, but they are formatted from the smallest to the largest one
        formattedMemberNames.Reverse();
        result = string.Join(" | ", formattedMemberNames);
        return true;
    }

    private static ImmutableArray<EnumMember> GetEnumMembersDescending(Type enumType)
    {
        var members = new List<EnumMember>();
        foreach (var field in enumType.GetFields(BindingFlags.Public | BindingFlags.Static))
        {
            if (field.GetRawConstantValue() is { } constantValue && TryGetEnumValueBits(constantValue, out var memberValue))
            {
                members.Add(new EnumMember(memberValue, field.Name));
            }
        }

        // Several members can share the same value, so they are ordered by name to always use the same one
        return
        [
            .. members
                .OrderByDescending(static member => member.Value)
                .ThenBy(static member => member.Name, StringComparer.Ordinal),
        ];
    }

    private static bool TryGetEnumValueBits(object value, out ulong bits)
    {
        bits = value switch
        {
            sbyte sbyteValue => unchecked((ulong)sbyteValue),
            byte byteValue => byteValue,
            short shortValue => unchecked((ulong)shortValue),
            ushort ushortValue => ushortValue,
            int intValue => unchecked((ulong)intValue),
            uint uintValue => uintValue,
            long longValue => unchecked((ulong)longValue),
            ulong ulongValue => ulongValue,
            _ => 0,
        };

        return value is sbyte or byte or short or ushort or int or uint or long or ulong;
    }

    private static string RemoveGenericArity(string name)
    {
        var index = name.IndexOf('`', StringComparison.Ordinal);
        if (index < 0)
            return name;

        return name[..index];
    }

    private static string EscapeIdentifier(string identifier) => CSharpIdentifierHelper.EscapeIdentifier(identifier);

    private static void AppendIndentedLine(StringBuilder sb, int indentationLevel, string text)
    {
        if (text.Length > 0)
        {
            sb.Append(' ', indentationLevel * 4);
        }

        sb.AppendLine(text);
    }

    private readonly record struct EnumMember(ulong Value, string Name);

    private sealed record ParameterDeclaration(string Text, bool RequiresNullableDirectives);
    private sealed record ExtensionPropertyBuilderReflection(ParameterInfo ReceiverParameter, string PropertyName, MethodInfo? Getter, MethodInfo? Setter, int Order);
    private sealed record ExtensionPropertyBlockReflection(string Content, IReadOnlyList<MethodInfo> Accessors, int Order);
}
