using System.Collections.Immutable;
using System.Reflection;
using System.Text;

namespace Meziantou.Framework.PublicApiGenerator;

internal static class PublicApiModelBuilder
{
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
        AppendIndentedLine(sb, indentationLevel, typeHeader.Declaration);
        foreach (var constraint in typeHeader.Constraints)
        {
            AppendIndentedLine(sb, indentationLevel + 1, constraint);
        }

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
            .OrderBy(static nestedType => nestedType.Name, StringComparer.Ordinal)
            .ToList();
        if (nestedTypes.Count == 0)
            return;

        if (sb.Length > 0 && sb[^1] != '\n')
        {
            sb.AppendLine();
        }

        for (var i = 0; i < nestedTypes.Count; i++)
        {
            if (i > 0)
            {
                sb.AppendLine();
            }

            sb.Append(BuildTypeDeclaration(nestedTypes[i], indentationLevel));
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

        foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
                     .Where(IsExternallyVisible)
                     .Where(static method => !method.IsSpecialName)
                     .Where(static method => !method.Name.Contains('<', StringComparison.Ordinal))
                     .OrderBy(static method => method.MetadataToken))
        {
            members.Add(BuildMethod(method, indentationLevel));
        }

        for (var i = 0; i < members.Count; i++)
        {
            if (i > 0)
            {
                sb.AppendLine();
            }

            sb.Append(members[i]);
        }
    }

    private static string BuildDelegate(Type type, int indentationLevel)
    {
        var invokeMethod = type.GetMethod("Invoke", BindingFlags.Public | BindingFlags.Instance) ?? throw new InvalidOperationException("Delegate type must have an Invoke method");
        var sb = new StringBuilder();
        AppendAttributes(sb, type.CustomAttributes, indentationLevel);

        var modifiers = GetTypeAccessibility(type);
        var genericArguments = BuildGenericArguments(type);
        var parameters = string.Join(", ", invokeMethod.GetParameters().Select(BuildParameter));
        var returnType = FormatReturnType(invokeMethod.ReturnParameter);
        var constraints = BuildTypeConstraints(type, indentationLevel);

        AppendIndentedLine(sb, indentationLevel, $"{modifiers} delegate {returnType} {EscapeIdentifier(RemoveGenericArity(type.Name))}{genericArguments}({parameters});");
        foreach (var constraint in constraints)
        {
            AppendIndentedLine(sb, indentationLevel + 1, constraint);
        }

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
        if (field.IsStatic && !field.IsLiteral)
        {
            modifiers.Add("static");
        }

        if (field.IsInitOnly)
        {
            modifiers.Add("readonly");
        }

        if (field.IsLiteral)
        {
            modifiers.Add("const");
        }

        var declaration = $"{string.Join(' ', modifiers.Where(static value => !string.IsNullOrEmpty(value)))} {FormatType(field.FieldType)} {EscapeIdentifier(field.Name)}";
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
        if (!string.IsNullOrEmpty(propertyAccessibility))
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

        var propertyName = property.GetIndexParameters().Length > 0
            ? $"this[{string.Join(", ", property.GetIndexParameters().Select(BuildParameter))}]"
            : EscapeIdentifier(property.Name);
        AppendIndentedLine(sb, indentationLevel, $"{string.Join(' ', modifiers)} {FormatType(property.PropertyType)} {propertyName}");
        AppendIndentedLine(sb, indentationLevel, "{");

        if (property.GetMethod is not null && IsExternallyVisible(property.GetMethod))
        {
            var accessorModifier = BuildAccessorModifier(property.GetMethod, representativeAccessor);
            AppendIndentedLine(sb, indentationLevel + 1, $"{accessorModifier}get;");
        }

        if (property.SetMethod is not null && IsExternallyVisible(property.SetMethod))
        {
            var accessorKeyword = IsInitOnly(property.SetMethod) ? "init" : "set";
            var accessorModifier = BuildAccessorModifier(property.SetMethod, representativeAccessor);
            AppendIndentedLine(sb, indentationLevel + 1, $"{accessorModifier}{accessorKeyword};");
        }

        AppendIndentedLine(sb, indentationLevel, "}");
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

        AppendIndentedLine(sb, indentationLevel, $"{string.Join(' ', modifiers)} event {FormatType(@event.EventHandlerType!)} {EscapeIdentifier(@event.Name)};");
        return sb.ToString();
    }

    private static string BuildMethod(MethodInfo method, int indentationLevel)
    {
        var sb = new StringBuilder();
        var methodAttributes = method.CustomAttributes;
        if (IsLibraryImportMethod(method))
        {
            methodAttributes = methodAttributes
                .Where(static attribute => !IsDllImportAttribute(attribute.AttributeType.FullName))
                .Where(static attribute => attribute.AttributeType.FullName != "System.Runtime.InteropServices.PreserveSigAttribute");
        }

        AppendAttributes(sb, methodAttributes, indentationLevel);
        AppendReturnAttributes(sb, method, indentationLevel);

        var modifiers = BuildMethodModifiers(method);
        var methodName = EscapeIdentifier(method.Name);
        var parameters = string.Join(", ", method.GetParameters().Select(BuildParameter));
        var genericArguments = BuildGenericArguments(method);
        var constraints = BuildMethodConstraints(method, indentationLevel);

        var declaration = $"{string.Join(' ', modifiers)} {FormatReturnType(method.ReturnParameter)} {methodName}{genericArguments}({parameters})";
        if (constraints.Count == 0)
        {
            declaration += BuildMethodBody(method);
            AppendIndentedLine(sb, indentationLevel, declaration);
            return sb.ToString();
        }

        AppendIndentedLine(sb, indentationLevel, declaration);
        foreach (var constraint in constraints)
        {
            AppendIndentedLine(sb, indentationLevel + 1, constraint);
        }

        AppendIndentedLine(sb, indentationLevel, BuildMethodBody(method).TrimStart());
        return sb.ToString();
    }

    private static string BuildMethodBody(MethodInfo method)
    {
        if (method.IsAbstract)
            return ";";

        if (IsLibraryImportMethod(method))
            return ";";

        if (method.GetMethodBody() is null)
            return ";";

        return " => throw null;";
    }

    private static List<string> BuildMethodModifiers(MethodInfo method)
    {
        var modifiers = new List<string>();
        var accessibility = GetMethodAccessibility(method);
        if (!string.IsNullOrEmpty(accessibility))
        {
            modifiers.Add(accessibility);
        }

        if (method.IsStatic)
        {
            modifiers.Add("static");
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

    private static string BuildParameter(ParameterInfo parameter)
    {
        var sb = new StringBuilder();
        AppendInlineAttributes(sb, parameter.CustomAttributes);

        if (parameter.IsOut)
        {
            sb.Append("out ");
        }
        else if (parameter.ParameterType.IsByRef)
        {
            if (parameter.IsIn)
            {
                sb.Append("in ");
            }
            else
            {
                sb.Append("ref ");
            }
        }
        else if (parameter.GetCustomAttributesData().Any(static attribute => attribute.AttributeType.FullName == "System.ParamArrayAttribute"))
        {
            sb.Append("params ");
        }

        var parameterType = parameter.ParameterType.IsByRef ? parameter.ParameterType.GetElementType()! : parameter.ParameterType;
        sb.Append(FormatType(parameterType));
        sb.Append(' ');
        sb.Append(EscapeIdentifier(parameter.Name ?? "value"));
        if (parameter.HasDefaultValue)
        {
            sb.Append(" = ");
            sb.Append(FormatConstant(parameter.DefaultValue));
        }

        return sb.ToString();
    }

    private static string FormatReturnType(ParameterInfo returnParameter)
    {
        var returnType = returnParameter.ParameterType;
        if (!returnType.IsByRef)
            return FormatType(returnType);

        var elementType = returnType.GetElementType()!;
        if (returnParameter.GetRequiredCustomModifiers().Any(static modifier => modifier.FullName == "System.Runtime.InteropServices.InAttribute"))
            return "ref readonly " + FormatType(elementType);

        return "ref " + FormatType(elementType);
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

            foreach (var constraint in genericArgument.GetGenericParameterConstraints())
            {
                values.Add(FormatType(constraint));
            }

            if (genericParameterAttributes.HasFlag(GenericParameterAttributes.DefaultConstructorConstraint))
            {
                values.Add("new()");
            }

            if (values.Count == 0)
                continue;

            constraints.Add($"where {EscapeIdentifier(genericArgument.Name)} : {string.Join(", ", values)}");
        }

        return constraints;
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
            .Select(FormatType)
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

        if (method.IsPublic || method.IsFamily || method.IsFamilyOrAssembly)
            return IsExternallyVisible(method.DeclaringType!);

        return false;
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

    private static string FormatType(Type type)
    {
        if (type.IsByRef)
            return FormatType(type.GetElementType()!);

        if (type.IsPointer)
            return FormatType(type.GetElementType()!) + "*";

        if (type.IsArray)
            return FormatType(type.GetElementType()!) + "[" + new string(',', type.GetArrayRank() - 1) + "]";

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
            return "string";

        if (type == typeof(object))
            return "object";

        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Nullable<>))
            return FormatType(type.GetGenericArguments()[0]) + "?";

        return BuildNamedType(type);
    }

    private static string BuildNamedType(Type type)
    {
        var name = EscapeIdentifier(RemoveGenericArity(type.Name));
        var genericArguments = type.GetGenericArguments();
        if (type.IsNested)
        {
            var declaringTypeArgumentCount = type.DeclaringType?.GetGenericArguments().Length ?? 0;
            var currentTypeArguments = genericArguments.Skip(declaringTypeArgumentCount);
            var nestedName = BuildNamedType(type.DeclaringType!) + "." + name;
            if (currentTypeArguments.Any())
            {
                nestedName += "<" + string.Join(", ", currentTypeArguments.Select(FormatType)) + ">";
            }

            return nestedName;
        }

        if (genericArguments.Length > 0)
        {
            name += "<" + string.Join(", ", genericArguments.Select(FormatType)) + ">";
        }

        if (!string.IsNullOrEmpty(type.Namespace))
            return "global::" + type.Namespace + "." + name;

        return name;
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
