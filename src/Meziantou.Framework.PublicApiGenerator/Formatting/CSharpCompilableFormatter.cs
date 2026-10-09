using System.Collections.Immutable;

namespace Meziantou.Framework.PublicApiGenerator;

/// <summary>
/// Formats symbols as the compilable stubs written by <see cref="PublicApi.Generate(string, PublicApiOptions?)"/>.
/// The output must stay identical to the one of the previous versions of the generator, as it is used to track API changes.
/// </summary>
/// <remarks>
/// Multi-line members are written relative to the indentation of the member. Nested types are referenced by their name only,
/// with the type arguments of all their containing types, which matches the way they are declared in the stub.
/// </remarks>
internal sealed class CSharpCompilableFormatter
{
    private readonly PublicApiFormattingOptions _options;

    public CSharpCompilableFormatter(PublicApiFormattingOptions options)
    {
        _options = options;
    }

    public void WriteSymbol(DeclarationWriter writer, PublicApiSymbol symbol)
    {
        switch (symbol)
        {
            case PublicApiType type:
                WriteType(writer, type);
                break;

            case PublicApiMethod { AssociatedSymbol: { } associatedSymbol }:
                WriteSymbol(writer, associatedSymbol);
                break;

            case PublicApiField field:
                if (field.DeclaringType?.TypeKind == PublicApiTypeKind.Enum)
                {
                    WriteEnumMember(writer, field);
                }
                else
                {
                    WriteField(writer, field);
                }

                break;

            case PublicApiProperty property:
                WriteProperty(writer, property);
                break;

            case PublicApiEvent @event:
                WriteEvent(writer, @event);
                break;

            case PublicApiMethod { MethodKind: PublicApiMethodKind.Constructor } constructor:
                WriteConstructor(writer, constructor, force: true);
                break;

            case PublicApiMethod method:
                WriteMethod(writer, method);
                break;
        }
    }

    public string FormatAssemblyAttributes(PublicApiAssembly assembly)
    {
        var writer = new DeclarationWriter(_options.NewLine);
        foreach (var attribute in assembly.Attributes)
        {
            if (!string.Equals(attribute.AttributeType.FullName, "System.Runtime.Versioning.RequiresPreviewFeaturesAttribute", StringComparison.Ordinal))
                continue;

            writer.Punctuation("[");
            writer.Keyword("assembly");
            writer.Punctuation(":");
            writer.Space();
            WriteAttributeContent(writer, attribute);
            writer.Punctuation("]");
            writer.WriteLine();
        }

        return writer.GetText();
    }

    public void WriteType(DeclarationWriter writer, PublicApiType type)
    {
        foreach (var attribute in GetAttributes(type.Attributes))
        {
            if (type.IsUnion && attribute.AttributeType.FullName.StartsWith("System.Runtime.CompilerServices.Union", StringComparison.Ordinal))
                continue;

            WriteAttribute(writer, attribute);
            writer.WriteLine();
        }

        var accessibility = CSharpSyntaxFacts.GetAccessibilityText(type.Accessibility);
        if (type.TypeKind == PublicApiTypeKind.Delegate)
        {
            WriteDelegate(writer, type, accessibility);
            return;
        }

        if (type.TypeKind == PublicApiTypeKind.Enum)
        {
            writer.Keyword(accessibility);
            writer.Space();
            writer.Keyword("enum");
            writer.Space();
            WriteTypeDeclarationName(writer, type);
            writer.WriteLine();
            writer.Punctuation("{");
            writer.WriteLine();
            writer.Indentation++;
            foreach (var member in type.Members)
            {
                if (member is PublicApiField field)
                {
                    WriteEnumMember(writer, field);
                    writer.Punctuation(",");
                    writer.WriteLine();
                }
            }

            writer.Indentation--;
            writer.Punctuation("}");
            writer.WriteLine();
            return;
        }

        writer.Keyword(accessibility);
        if (type.TypeKind == PublicApiTypeKind.Class)
        {
            if (type.IsStatic)
            {
                writer.Space();
                writer.Keyword("static");
            }
            else
            {
                if (type.IsClosed)
                {
                    writer.Space();
                    writer.Keyword("closed");
                }
                else if (type.IsAbstract)
                {
                    writer.Space();
                    writer.Keyword("abstract");
                }

                if (type.IsSealed)
                {
                    writer.Space();
                    writer.Keyword("sealed");
                }
            }
        }

        writer.Space();
        writer.Keyword(GetTypeKeyword(type));
        writer.Space();
        WriteTypeDeclarationName(writer, type);
        if (type.IsUnion)
        {
            writer.Punctuation("(");
            for (var i = 0; i < type.UnionCaseTypes.Length; i++)
            {
                if (i > 0)
                {
                    writer.Punctuation(",");
                    writer.Space();
                }

                WriteTypeReference(writer, type.UnionCaseTypes[i], includeNullableAnnotations: true);
            }

            writer.Punctuation(")");
        }

        WriteBaseTypes(writer, type);
        WriteConstraints(writer, type.AllGenericParameters);
        writer.WriteLine();
        writer.Punctuation("{");
        writer.WriteLine();
        writer.Indentation++;

        var extensionPropertyBlocks = GetExtensionPropertyBlocks(type);
        var extensionPropertyAccessors = extensionPropertyBlocks.SelectMany(static block => block.Accessors).ToHashSet();
        foreach (var member in type.Members)
        {
            if (member is PublicApiMethod method && extensionPropertyAccessors.Contains(method))
                continue;

            var memberWriter = writer.CreateWriter();
            if (!TryWriteTypeMember(memberWriter, type, member))
                continue;

            writer.Write(memberWriter);
            writer.WriteLine();
        }

        foreach (var block in extensionPropertyBlocks.OrderBy(static block => block.Order))
        {
            WriteExtensionPropertyBlock(writer, block);
            writer.WriteLine();
        }

        foreach (var nestedType in type.NestedTypes)
        {
            var nestedTypeWriter = writer.CreateWriter();
            WriteType(nestedTypeWriter, nestedType);
            writer.Write(nestedTypeWriter);
        }

        writer.Indentation--;
        writer.Punctuation("}");
        writer.WriteLine();
    }

    private bool TryWriteTypeMember(DeclarationWriter writer, PublicApiType type, PublicApiMember member)
    {
        switch (member)
        {
            case PublicApiField field:
                WriteField(writer, field);
                return true;

            case PublicApiProperty property:
                // Explicit implementations of properties and events have never been part of the stubs
                if (property.IsExplicitInterfaceImplementation)
                    return false;

                if (type.IsUnion && IsGeneratedUnionValueProperty(property))
                    return false;

                WriteProperty(writer, property);
                return true;

            case PublicApiEvent @event:
                if (@event.IsExplicitInterfaceImplementation)
                    return false;

                WriteEvent(writer, @event);
                return true;

            case PublicApiMethod { MethodKind: PublicApiMethodKind.Constructor } constructor:
                if (type.IsUnion && IsGeneratedUnionCaseConstructor(constructor))
                    return false;

                return WriteConstructor(writer, constructor, force: false);

            case PublicApiMethod method:
                // Explicit implementations of generic interfaces have never been part of the stubs
                if (method.IsExplicitInterfaceImplementation && method.Name.Contains('<', StringComparison.Ordinal))
                    return false;

                WriteMethod(writer, method);
                return true;

            default:
                return false;
        }
    }

    private static string GetTypeKeyword(PublicApiType type)
    {
        return type switch
        {
            { TypeKind: PublicApiTypeKind.Interface } => "interface",
            { IsUnion: true } => "union",
            { TypeKind: PublicApiTypeKind.Enum } => "enum",
            { TypeKind: PublicApiTypeKind.Delegate } => "delegate",
            { TypeKind: PublicApiTypeKind.Struct, IsRefLike: true } => "ref struct",
            { TypeKind: PublicApiTypeKind.Struct, IsReadOnly: true } => "readonly struct",
            { TypeKind: PublicApiTypeKind.Struct } => "struct",
            _ => "class",
        };
    }

    private void WriteDelegate(DeclarationWriter writer, PublicApiType type, string accessibility)
    {
        var invokeMethod = type.DelegateInvokeMethod!;
        writer.Keyword(accessibility);

        // The unsafe modifier is not allowed on type declarations under the updated memory safety rules
        if (!type.Assembly.UsesUpdatedMemorySafetyRules && HasPointerInSignature(invokeMethod))
        {
            writer.Space();
            writer.Keyword("unsafe");
        }

        writer.Space();
        writer.Keyword("delegate");
        writer.Space();
        WriteReturnType(writer, invokeMethod);
        writer.Space();
        WriteTypeDeclarationName(writer, type);
        writer.Punctuation("(");
        var parameters = BuildParameters(invokeMethod.Parameters, includeDefaultValues: true);
        for (var i = 0; i < parameters.Length; i++)
        {
            if (i > 0)
            {
                writer.Punctuation(",");
                writer.Space();
            }

            writer.Write(parameters[i].Writer);
        }

        writer.Punctuation(")");
        WriteConstraints(writer, type.AllGenericParameters);
        writer.Punctuation(";");
        writer.WriteLine();
    }

    private static void WriteTypeDeclarationName(DeclarationWriter writer, PublicApiType type)
    {
        writer.Write(PublicApiDeclarationSegmentKind.Identifier, CSharpIdentifierHelper.EscapeIdentifier(type.Name), symbol: type);

        // Nested types repeat the generic parameters of their containing types, as metadata does
        WriteGenericParameterNames(writer, type.AllGenericParameters);
    }

    private static void WriteGenericParameterNames(DeclarationWriter writer, ImmutableArray<PublicApiGenericParameter> genericParameters)
    {
        if (genericParameters.IsEmpty)
            return;

        writer.Punctuation("<");
        for (var i = 0; i < genericParameters.Length; i++)
        {
            if (i > 0)
            {
                writer.Punctuation(",");
                writer.Space();
            }

            writer.Write(PublicApiDeclarationSegmentKind.TypeParameterName, CSharpIdentifierHelper.EscapeIdentifier(genericParameters[i].Name));
        }

        writer.Punctuation(">");
    }

    private static void WriteBaseTypes(DeclarationWriter writer, PublicApiType type)
    {
        var baseTypes = new List<DeclarationWriter>();
        if (type.TypeKind == PublicApiTypeKind.Class && type.BaseType is PublicApiNamedTypeReference baseType && !baseType.IsSystemType("Object") && !baseType.IsSystemType("ValueType"))
        {
            var baseTypeWriter = writer.CreateWriter();
            WriteTypeReference(baseTypeWriter, baseType, includeNullableAnnotations: false);
            baseTypes.Add(baseTypeWriter);
        }

        var interfaces = new List<DeclarationWriter>();
        foreach (var @interface in type.Interfaces)
        {
            if (type.IsUnion && @interface is PublicApiNamedTypeReference named && string.Equals(named.FullName, PublicApiMetadataReader.IUnionInterfaceFullName, StringComparison.Ordinal))
                continue;

            var interfaceWriter = writer.CreateWriter();
            WriteTypeReference(interfaceWriter, @interface, includeNullableAnnotations: false);
            interfaces.Add(interfaceWriter);
        }

        // The interface list is sorted using the formatted names, so the generated API does not depend on the order of the metadata table.
        // The base type stays first as C# requires it to precede the interfaces.
        interfaces.Sort(static (x, y) => string.CompareOrdinal(x.GetText(), y.GetText()));
        baseTypes.AddRange(interfaces);
        if (baseTypes.Count == 0)
            return;

        writer.Space();
        writer.Punctuation(":");
        writer.Space();
        var writtenTypes = new HashSet<string>(StringComparer.Ordinal);
        var isFirst = true;
        foreach (var baseTypeWriter in baseTypes)
        {
            if (!writtenTypes.Add(baseTypeWriter.GetText()))
                continue;

            if (!isFirst)
            {
                writer.Punctuation(",");
                writer.Space();
            }

            isFirst = false;
            writer.Write(baseTypeWriter);
        }
    }

    private static void WriteConstraints(DeclarationWriter writer, ImmutableArray<PublicApiGenericParameter> genericParameters)
    {
        foreach (var genericParameter in genericParameters)
        {
            var constraints = new List<Action>();
            if (genericParameter.HasReferenceTypeConstraint)
            {
                constraints.Add(() => writer.Keyword("class"));
            }

            if (genericParameter.HasValueTypeConstraint)
            {
                constraints.Add(() => writer.Keyword("struct"));
            }

            foreach (var constraintType in genericParameter.ConstraintTypes)
            {
                constraints.Add(() => WriteTypeReference(writer, constraintType, includeNullableAnnotations: false));
            }

            if (genericParameter.HasConstructorConstraint)
            {
                constraints.Add(() =>
                {
                    writer.Keyword("new");
                    writer.Punctuation("()");
                });
            }

            if (genericParameter.AllowsRefLikeType)
            {
                constraints.Add(() =>
                {
                    writer.Keyword("allows");
                    writer.Space();
                    writer.Keyword("ref");
                    writer.Space();
                    writer.Keyword("struct");
                });
            }

            if (constraints.Count == 0)
                continue;

            writer.Space();
            writer.Keyword("where");
            writer.Space();
            writer.Write(PublicApiDeclarationSegmentKind.TypeParameterName, CSharpIdentifierHelper.EscapeIdentifier(genericParameter.Name));
            writer.Space();
            writer.Punctuation(":");
            writer.Space();
            for (var i = 0; i < constraints.Count; i++)
            {
                if (i > 0)
                {
                    writer.Punctuation(",");
                    writer.Space();
                }

                constraints[i]();
            }
        }
    }

    private static void WriteEnumMember(DeclarationWriter writer, PublicApiField field)
    {
        writer.Write(PublicApiDeclarationSegmentKind.Identifier, CSharpIdentifierHelper.EscapeIdentifier(field.Name), symbol: field);
        if (field.HasConstantValue)
        {
            writer.Space();
            writer.Punctuation("=");
            writer.Space();
            WriteConstant(writer, field.ConstantValue);
        }
    }

    private void WriteField(DeclarationWriter writer, PublicApiField field)
    {
        WriteMemberAttributes(writer, field.Attributes);
        var modifiers = new List<string> { CSharpSyntaxFacts.GetAccessibilityText(field.Accessibility) };
        if (field.IsStatic && !field.IsConst)
        {
            modifiers.Add("static");
        }

        if (field.IsReadOnly && field.RefKind == PublicApiRefKind.None)
        {
            modifiers.Add("readonly");
        }

        if (field.IsConst)
        {
            modifiers.Add("const");
        }

        if (field.RequiresUnsafe)
        {
            modifiers.Add("unsafe");
        }

        WriteKeywords(writer, modifiers);
        writer.Space();
        if (field.RefKind != PublicApiRefKind.None)
        {
            if (field.IsReadOnly)
            {
                writer.Keyword("readonly");
                writer.Space();
            }

            writer.Keyword("ref");
            writer.Space();
            if (field.RefKind == PublicApiRefKind.RefReadOnly)
            {
                writer.Keyword("readonly");
                writer.Space();
            }
        }

        WriteTypeReference(writer, field.Type, includeNullableAnnotations: true);
        writer.Space();
        writer.Write(PublicApiDeclarationSegmentKind.Identifier, CSharpIdentifierHelper.EscapeIdentifier(field.Name), symbol: field);
        if (field.IsConst && field.HasConstantValue)
        {
            writer.Space();
            writer.Punctuation("=");
            writer.Space();
            WriteConstant(writer, field.ConstantValue);
        }

        writer.Punctuation(";");
    }

    private void WriteProperty(DeclarationWriter writer, PublicApiProperty property)
    {
        WriteMemberAttributes(writer, property.Attributes);
        var declaringType = property.DeclaringType!;
        var isInterface = declaringType.TypeKind == PublicApiTypeKind.Interface;
        var modifiers = new List<string>();
        if (!(isInterface && property.IsAbstract))
        {
            modifiers.Add(CSharpSyntaxFacts.GetAccessibilityText(property.Accessibility));
        }

        if (property.IsStatic)
        {
            modifiers.Add("static");
        }

        if (!isInterface)
        {
            AddInheritanceModifiers(modifiers, property);
        }

        if (property.IsRequired)
        {
            modifiers.Add("required");
        }

        var getter = property.GetMethod;
        var setter = property.SetMethod;
        var isGetReadOnly = getter?.IsReadOnly is true;
        var isSetReadOnly = setter?.IsReadOnly is true;

        // readonly can be set on the property or on its accessors, but not on both
        if (property.IsReadOnly)
        {
            modifiers.Add("readonly");
            isGetReadOnly = false;
            isSetReadOnly = false;
        }

        var isGetUnsafe = getter?.RequiresUnsafe is true;
        var isSetUnsafe = setter?.RequiresUnsafe is true;

        // The unsafe modifier can be set on the property or on its accessors, but not on both
        var isPropertyUnsafe = property.RequiresUnsafe ||
                               ((isGetUnsafe || isSetUnsafe) && isGetUnsafe == (getter is not null) && isSetUnsafe == (setter is not null));
        if (isPropertyUnsafe)
        {
            modifiers.Add("unsafe");
            isGetUnsafe = false;
            isSetUnsafe = false;
        }

        if (modifiers.Count > 0)
        {
            WriteKeywords(writer, modifiers);
            writer.Space();
        }

        if (property.RefKind != PublicApiRefKind.None)
        {
            writer.Keyword("ref");
            writer.Space();
        }

        WriteTypeReference(writer, property.Type, includeNullableAnnotations: true);
        writer.Space();
        if (property.IsIndexer)
        {
            writer.Write(PublicApiDeclarationSegmentKind.Identifier, "this", symbol: property);
            writer.Punctuation("[");
            var parameters = BuildParameters(property.Parameters, includeDefaultValues: false);
            for (var i = 0; i < parameters.Length; i++)
            {
                if (i > 0)
                {
                    writer.Punctuation(",");
                    writer.Space();
                }

                writer.Write(parameters[i].Writer);
            }

            writer.Punctuation("]");
        }
        else
        {
            writer.Write(PublicApiDeclarationSegmentKind.Identifier, CSharpIdentifierHelper.EscapeIdentifier(property.Name), symbol: property);
        }

        writer.Space();
        writer.Punctuation("{");
        if (getter is not null)
        {
            writer.Space();
            WriteAccessorModifiers(writer, getter, property.Accessibility, isGetReadOnly, isGetUnsafe);
            writer.Keyword("get");
            if (getter.IsAbstract)
            {
                writer.Punctuation(";");
            }
            else
            {
                WriteThrowNullBody(writer);
            }
        }

        if (setter is not null)
        {
            writer.Space();
            WriteAccessorModifiers(writer, setter, property.Accessibility, isSetReadOnly, isSetUnsafe);
            writer.Keyword(setter.IsInitOnly ? "init" : "set");
            if (setter.IsAbstract)
            {
                writer.Punctuation(";");
            }
            else
            {
                writer.Space();
                writer.Punctuation("{");
                writer.Space();
                writer.Punctuation("}");
            }
        }

        writer.Space();
        writer.Punctuation("}");
    }

    private static void WriteAccessorModifiers(DeclarationWriter writer, PublicApiMethod accessor, PublicApiAccessibility propertyAccessibility, bool isReadOnly, bool isUnsafe)
    {
        if (accessor.Accessibility != propertyAccessibility)
        {
            writer.Keyword(CSharpSyntaxFacts.GetAccessibilityText(accessor.Accessibility));
            writer.Space();
        }

        if (isReadOnly)
        {
            writer.Keyword("readonly");
            writer.Space();
        }

        if (isUnsafe)
        {
            writer.Keyword("unsafe");
            writer.Space();
        }
    }

    private void WriteEvent(DeclarationWriter writer, PublicApiEvent @event)
    {
        WriteMemberAttributes(writer, @event.Attributes);
        var modifiers = new List<string> { CSharpSyntaxFacts.GetAccessibilityText(@event.Accessibility) };
        if (@event.IsStatic)
        {
            modifiers.Add("static");
        }

        if (@event.DeclaringType!.TypeKind != PublicApiTypeKind.Interface)
        {
            AddInheritanceModifiers(modifiers, @event);
        }

        // Event accessors cannot be marked as unsafe individually
        if (@event.RequiresUnsafe)
        {
            modifiers.Add("unsafe");
        }

        WriteKeywords(writer, modifiers);
        writer.Space();
        writer.Keyword("event");
        writer.Space();
        WriteTypeReference(writer, @event.Type, includeNullableAnnotations: true);
        writer.Space();
        writer.Write(PublicApiDeclarationSegmentKind.Identifier, CSharpIdentifierHelper.EscapeIdentifier(@event.Name), symbol: @event);
        writer.Punctuation(";");
    }

    private void WriteMethod(DeclarationWriter writer, PublicApiMethod method)
    {
        var declaringType = method.DeclaringType!;
        if (method.MethodKind == PublicApiMethodKind.Destructor)
        {
            writer.Punctuation("~");
            writer.Write(PublicApiDeclarationSegmentKind.Identifier, CSharpIdentifierHelper.EscapeIdentifier(declaringType.Name), symbol: method);
            writer.Punctuation("()");
            if (method.IsAbstract)
            {
                writer.Punctuation(";");
            }
            else
            {
                writer.Space();
                writer.Punctuation("{");
                writer.Space();
                writer.Punctuation("}");
            }

            return;
        }

        var isInterface = declaringType.TypeKind == PublicApiTypeKind.Interface;
        var modifiers = new List<string>();
        if (!method.IsExplicitInterfaceImplementation)
        {
            if (!(isInterface && method.IsAbstract))
            {
                modifiers.Add(CSharpSyntaxFacts.GetAccessibilityText(method.Accessibility));
            }

            if (method.IsStatic)
            {
                modifiers.Add("static");
            }

            if (method.IsReadOnly)
            {
                modifiers.Add("readonly");
            }

            if (!isInterface)
            {
                AddInheritanceModifiers(modifiers, method);
            }
        }

        var prefix = writer.CreateWriter();
        if (modifiers.Count > 0)
        {
            WriteKeywords(prefix, modifiers);
            prefix.Space();
        }

        if (RequiresUnsafeModifier(method))
        {
            prefix.Keyword("unsafe");
            prefix.Space();
        }

        var operatorToken = CSharpSyntaxFacts.GetOperatorToken(method.Name);
        if (operatorToken is not null && !declaringType.IsStatic)
        {
            if (operatorToken is "implicit" or "explicit")
            {
                prefix.Keyword(operatorToken);
                prefix.Space();
                prefix.Keyword("operator");
                prefix.Space();
                WriteReturnType(prefix, method);
            }
            else
            {
                WriteReturnType(prefix, method);
                prefix.Space();
                prefix.Keyword("operator");
                prefix.Space();
                prefix.Write(PublicApiDeclarationSegmentKind.Operator, operatorToken, symbol: method);
            }
        }
        else
        {
            WriteReturnType(prefix, method);
            prefix.Space();
            if (method.IsExplicitInterfaceImplementation)
            {
                WriteExplicitInterfaceMemberName(prefix, method);
            }
            else
            {
                prefix.Write(PublicApiDeclarationSegmentKind.Identifier, CSharpIdentifierHelper.EscapeIdentifier(method.Name), symbol: method);
            }

            WriteGenericParameterNames(prefix, method.GenericParameters);
        }

        var suffix = writer.CreateWriter();
        WriteConstraints(suffix, method.GenericParameters);
        if (method.IsAbstract)
        {
            suffix.Punctuation(";");
        }
        else if (!method.ReturnsVoid || method.Parameters.Any(static parameter => parameter.HasOutAttribute))
        {
            WriteThrowNullBody(suffix);
        }
        else
        {
            suffix.Space();
            suffix.Punctuation("{");
            suffix.Space();
            suffix.Punctuation("}");
        }

        var parameters = BuildParameters(method.Parameters, includeDefaultValues: true);
        var requiresNullableDisableDirective = RequiresNullableDirectives(method.ReturnType) || parameters.Any(static parameter => parameter.RequiresNullableDirectives);
        WriteMemberAttributes(writer, method.Attributes);
        WriteDeclarationWithParameters(writer, prefix, parameters, suffix, requiresNullableDisableDirective);
    }

    private static void WriteExplicitInterfaceMemberName(DeclarationWriter writer, PublicApiMethod method)
    {
        var name = method.Name;
        var separatorIndex = name.LastIndexOf(".", StringComparison.Ordinal);
        if (separatorIndex < 0)
        {
            writer.Write(PublicApiDeclarationSegmentKind.Identifier, CSharpIdentifierHelper.EscapeIdentifier(name), symbol: method);
            return;
        }

        var interfaceName = name[..separatorIndex];
        if (interfaceName.StartsWith("global::", StringComparison.Ordinal))
        {
            interfaceName = interfaceName["global::".Length..];
        }

        var interfaceType = method.ExplicitInterfaceImplementations.Length > 0 ? method.ExplicitInterfaceImplementations[0].ContainingType : null;
        writer.Write(PublicApiDeclarationSegmentKind.TypeName, interfaceName, interfaceType);
        writer.Punctuation(".");
        writer.Write(PublicApiDeclarationSegmentKind.Identifier, CSharpIdentifierHelper.EscapeIdentifier(name[(separatorIndex + 1)..]), symbol: method);
    }

    private bool WriteConstructor(DeclarationWriter writer, PublicApiMethod constructor, bool force)
    {
        var declaringType = constructor.DeclaringType!;
        var requiresUnsafe = RequiresUnsafeModifier(constructor);
        var initializer = writer.CreateWriter();
        WriteConstructorInitializer(initializer, declaringType);

        // A parameterless constructor requiring an unsafe context is not equivalent to the implicit one as it doesn't satisfy the new() constraint
        if (!force &&
            constructor.Parameters.IsEmpty &&
            initializer.IsEmpty &&
            !requiresUnsafe &&
            !PublicApiSymbol.HasAttribute(constructor.Attributes, "System.Runtime.Versioning", "RequiresPreviewFeaturesAttribute"))
        {
            return false;
        }

        var prefix = writer.CreateWriter();
        prefix.Keyword(CSharpSyntaxFacts.GetAccessibilityText(constructor.Accessibility));
        prefix.Space();
        if (requiresUnsafe)
        {
            prefix.Keyword("unsafe");
            prefix.Space();
        }

        prefix.Write(PublicApiDeclarationSegmentKind.Identifier, CSharpIdentifierHelper.EscapeIdentifier(declaringType.Name), symbol: constructor);

        var suffix = writer.CreateWriter();
        suffix.Write(initializer);
        if (constructor.IsAbstract)
        {
            suffix.Punctuation(";");
        }
        else
        {
            suffix.Space();
            suffix.Punctuation("{");
            suffix.Space();
            suffix.Punctuation("}");
        }

        var parameters = BuildParameters(constructor.Parameters, includeDefaultValues: true);
        WriteMemberAttributes(writer, constructor.Attributes);
        WriteDeclarationWithParameters(writer, prefix, parameters, suffix, parameters.Any(static parameter => parameter.RequiresNullableDirectives));
        return true;
    }

    private static void WriteConstructorInitializer(DeclarationWriter writer, PublicApiType declaringType)
    {
        // The base type must be declared in the same assembly to know its constructors. Constructed generic types are not resolved.
        if (declaringType.BaseType is not PublicApiNamedTypeReference baseType ||
            baseType.IsSystemType("Object") ||
            baseType.IsSystemType("ValueType") ||
            baseType.GetAllTypeArguments().Any() ||
            declaringType.Assembly.FindType(baseType.FullName) is not { } baseTypeDefinition)
        {
            return;
        }

        PublicApiMethod? selectedConstructor = null;
        foreach (var member in baseTypeDefinition.Members)
        {
            if (member is not PublicApiMethod { MethodKind: PublicApiMethodKind.Constructor, IsStatic: false } constructor)
                continue;

            if (constructor.Parameters.IsEmpty)
                return;

            if (selectedConstructor is null || constructor.Origin!.MetadataToken < selectedConstructor.Origin!.MetadataToken)
            {
                selectedConstructor = constructor;
            }
        }

        if (selectedConstructor is null)
            return;

        writer.Space();
        writer.Punctuation(":");
        writer.Space();
        writer.Keyword("base");
        writer.Punctuation("(");
        for (var i = 0; i < selectedConstructor.Parameters.Length; i++)
        {
            if (i > 0)
            {
                writer.Punctuation(",");
                writer.Space();
            }

            writer.Keyword("default");
            writer.Punctuation("(");
            WriteTypeReference(writer, selectedConstructor.Parameters[i].Type, includeNullableAnnotations: false);
            writer.Punctuation(")");
        }

        writer.Punctuation(")");
    }

    private static void WriteDeclarationWithParameters(DeclarationWriter writer, DeclarationWriter prefix, ImmutableArray<ParameterText> parameters, DeclarationWriter suffix, bool wrapWithNullableDisableDirective)
    {
        var hasNullableAnnotations = prefix.ContainsText('?') || parameters.Any(static parameter => parameter.Writer.ContainsText('?'));
        var shouldEmitNullableDirectives = parameters.Any(static parameter => parameter.RequiresNullableDirectives) && hasNullableAnnotations;
        var shouldWrapWithNullableDisableDirective = wrapWithNullableDisableDirective && !hasNullableAnnotations;
        if (shouldWrapWithNullableDisableDirective)
        {
            writer.Text("#nullable disable");
            writer.WriteLine();
        }

        writer.Write(prefix);
        writer.Punctuation("(");
        if (!shouldEmitNullableDirectives)
        {
            for (var i = 0; i < parameters.Length; i++)
            {
                if (i > 0)
                {
                    writer.Punctuation(",");
                    writer.Space();
                }

                writer.Write(parameters[i].Writer);
            }

            writer.Punctuation(")");
            writer.Write(suffix);
        }
        else
        {
            writer.WriteLine();
            for (var i = 0; i < parameters.Length; i++)
            {
                var parameter = parameters[i];
                if (parameter.RequiresNullableDirectives)
                {
                    writer.Text("#nullable disable");
                    writer.WriteLine();
                }

                writer.Indentation++;
                writer.Write(parameter.Writer);
                if (i < parameters.Length - 1)
                {
                    writer.Punctuation(",");
                }

                writer.WriteLine();
                writer.Indentation--;
                if (parameter.RequiresNullableDirectives)
                {
                    writer.Text("#nullable restore");
                    writer.WriteLine();
                }
            }

            writer.Indentation++;
            writer.Punctuation(")");
            writer.Indentation--;
            writer.Write(suffix);
        }

        if (shouldWrapWithNullableDisableDirective)
        {
            writer.WriteLine();
            writer.Text("#nullable restore");
        }
    }

    private ImmutableArray<ParameterText> BuildParameters(ImmutableArray<PublicApiParameter> parameters, bool includeDefaultValues)
    {
        var result = ImmutableArray.CreateBuilder<ParameterText>(parameters.Length);
        foreach (var parameter in parameters)
        {
            var writer = new DeclarationWriter(_options.NewLine);
            foreach (var attribute in GetAttributes(parameter.Attributes))
            {
                WriteAttribute(writer, attribute);
                writer.Space();
            }

            if (parameter.IsThis)
            {
                writer.Keyword("this");
                writer.Space();
            }

            var scoped = parameter.IsScoped;
            switch (parameter.RefKind)
            {
                case PublicApiRefKind.None:
                    if (parameter.IsParams || parameter.IsParamsCollection)
                    {
                        writer.Keyword("params");
                        writer.Space();
                    }
                    else if (scoped)
                    {
                        writer.Keyword("scoped");
                        writer.Space();
                    }

                    break;

                case PublicApiRefKind.Out:
                    writer.Keyword("out");
                    writer.Space();
                    break;

                default:
                    if (scoped)
                    {
                        writer.Keyword("scoped");
                        writer.Space();
                    }

                    if (parameter.RefKind == PublicApiRefKind.In)
                    {
                        writer.Keyword("in");
                        writer.Space();
                    }
                    else
                    {
                        writer.Keyword("ref");
                        writer.Space();
                        if (parameter.RefKind == PublicApiRefKind.RefReadOnly)
                        {
                            writer.Keyword("readonly");
                            writer.Space();
                        }
                    }

                    break;
            }

            WriteTypeReference(writer, parameter.Type, includeNullableAnnotations: true);
            writer.Space();
            writer.Write(PublicApiDeclarationSegmentKind.ParameterName, GetParameterName(parameter));
            if (includeDefaultValues && parameter.HasDefaultValue)
            {
                writer.Space();
                writer.Punctuation("=");
                writer.Space();
                WriteConstant(writer, parameter.DefaultValue);
            }

            result.Add(new ParameterText(writer, RequiresNullableDirectives(parameter.Type)));
        }

        return result.MoveToImmutable();
    }

    private static string GetParameterName(PublicApiParameter parameter)
    {
        return parameter.Name is null
            ? "arg" + (parameter.Ordinal + 1).ToString(CultureInfo.InvariantCulture)
            : CSharpIdentifierHelper.EscapeIdentifier(parameter.Name);
    }

    // An oblivious reference type must be written in a '#nullable disable' context, as it would be read as non-nullable otherwise
    private static bool RequiresNullableDirectives(PublicApiTypeReference type)
    {
        while (type is PublicApiPointerTypeReference pointer)
        {
            type = pointer.ElementType;
        }

        var isReferenceType = type is PublicApiArrayTypeReference || type is PublicApiNamedTypeReference { IsValueType: false };
        return isReferenceType && type.NullableAnnotation == PublicApiNullableAnnotation.Oblivious;
    }

    private static bool RequiresUnsafeModifier(PublicApiMethod method)
    {
        // Under the updated memory safety rules, the compiler marks the members requiring an unsafe context with an attribute.
        // Otherwise, a member requires an unsafe context when a pointer type appears in its signature (compiler compat mode).
        return method.Assembly.UsesUpdatedMemorySafetyRules
            ? method.RequiresUnsafe
            : HasPointerInSignature(method);
    }

    private static bool HasPointerInSignature(PublicApiMethod method)
    {
        return CSharpSyntaxFacts.ContainsPointer(method.ReturnType) || method.Parameters.Any(static parameter => CSharpSyntaxFacts.ContainsPointer(parameter.Type));
    }

    private static void WriteReturnType(DeclarationWriter writer, PublicApiMethod method)
    {
        if (method.ReturnRefKind != PublicApiRefKind.None)
        {
            writer.Keyword("ref");
            writer.Space();
        }

        WriteTypeReference(writer, method.ReturnType, includeNullableAnnotations: true);
    }

    private static void WriteThrowNullBody(DeclarationWriter writer)
    {
        writer.Space();
        writer.Punctuation("=>");
        writer.Space();
        writer.Keyword("throw");
        writer.Space();
        writer.Keyword("null");
        writer.Punctuation(";");
    }

    private static void AddInheritanceModifiers(List<string> modifiers, PublicApiMember member)
    {
        if (member.IsAbstract)
        {
            modifiers.Add("abstract");
        }
        else if (member.IsVirtual)
        {
            modifiers.Add("virtual");
        }
        else if (member.IsSealed)
        {
            modifiers.Add("sealed");
            modifiers.Add("override");
        }
        else if (member.IsOverride)
        {
            modifiers.Add("override");
        }
    }

    private static void WriteKeywords(DeclarationWriter writer, List<string> keywords)
    {
        for (var i = 0; i < keywords.Count; i++)
        {
            if (i > 0)
            {
                writer.Space();
            }

            writer.Keyword(keywords[i]);
        }
    }

    private void WriteMemberAttributes(DeclarationWriter writer, ImmutableArray<PublicApiAttribute> attributes)
    {
        foreach (var attribute in GetAttributes(attributes))
        {
            WriteAttribute(writer, attribute);
            writer.WriteLine();
        }
    }

    private IEnumerable<PublicApiAttribute> GetAttributes(ImmutableArray<PublicApiAttribute> attributes)
    {
        if (!_options.IncludeAttributes)
            return [];

        var filter = _options.AttributeFilter;
        return attributes.Where(attribute => filter is null ? CSharpSyntaxFacts.IsDisplayedAttribute(attribute, PublicApiDeclarationStyle.Compilable) : filter(attribute));
    }

    private static void WriteAttribute(DeclarationWriter writer, PublicApiAttribute attribute)
    {
        writer.Punctuation("[");
        WriteAttributeContent(writer, attribute);
        writer.Punctuation("]");
    }

    private static void WriteAttributeContent(DeclarationWriter writer, PublicApiAttribute attribute)
    {
        var name = attribute.AttributeType.FullName;
        if (name.EndsWith("Attribute", StringComparison.Ordinal))
        {
            name = name[..^"Attribute".Length];
        }

        writer.Write(PublicApiDeclarationSegmentKind.TypeName, name, attribute.AttributeType);
        if (attribute.ConstructorArguments.IsEmpty && attribute.NamedArguments.IsEmpty)
            return;

        writer.Punctuation("(");
        var isFirst = true;
        foreach (var argument in attribute.ConstructorArguments)
        {
            if (!isFirst)
            {
                writer.Punctuation(",");
                writer.Space();
            }

            isFirst = false;
            WriteAttributeArgument(writer, argument);
        }

        foreach (var argument in attribute.NamedArguments)
        {
            if (!isFirst)
            {
                writer.Punctuation(",");
                writer.Space();
            }

            isFirst = false;
            writer.Write(PublicApiDeclarationSegmentKind.MemberName, argument.Name);
            writer.Space();
            writer.Punctuation("=");
            writer.Space();
            WriteAttributeArgument(writer, argument.Value);
        }

        writer.Punctuation(")");
    }

    private static void WriteAttributeArgument(DeclarationWriter writer, PublicApiAttributeArgument argument)
    {
        if (argument.Value is null)
        {
            writer.Keyword("null");
            return;
        }

        switch (argument.Kind)
        {
            case PublicApiAttributeArgumentKind.Array:
                {
                    var values = (ImmutableArray<PublicApiAttributeArgument>)argument.Value;
                    writer.Keyword("new");
                    writer.Space();
                    var elementType = (argument.Type as PublicApiArrayTypeReference)?.ElementType;
                    if (elementType is null)
                    {
                        writer.Write(PublicApiDeclarationSegmentKind.TypeName, "object");
                    }
                    else
                    {
                        WriteAttributeTypeReference(writer, elementType);
                    }

                    writer.Punctuation("[]");
                    writer.Space();
                    writer.Punctuation("{");
                    writer.Space();
                    for (var i = 0; i < values.Length; i++)
                    {
                        if (i > 0)
                        {
                            writer.Punctuation(",");
                            writer.Space();
                        }

                        WriteAttributeArgument(writer, values[i]);
                    }

                    writer.Space();
                    writer.Punctuation("}");
                    return;
                }

            case PublicApiAttributeArgumentKind.Type:
                writer.Keyword("typeof");
                writer.Punctuation("(");
                WriteAttributeTypeReference(writer, (PublicApiTypeReference)argument.Value);
                writer.Punctuation(")");
                return;

            case PublicApiAttributeArgumentKind.Enum:
                if (!argument.EnumMemberNames.IsEmpty)
                {
                    for (var i = 0; i < argument.EnumMemberNames.Length; i++)
                    {
                        if (i > 0)
                        {
                            writer.Space();
                            writer.Operator("|");
                            writer.Space();
                        }

                        WriteAttributeTypeReference(writer, argument.Type);
                        writer.Punctuation(".");
                        writer.Write(PublicApiDeclarationSegmentKind.MemberName, argument.EnumMemberNames[i]);
                    }

                    return;
                }

                writer.Punctuation("(");
                WriteAttributeTypeReference(writer, argument.Type);
                writer.Punctuation(")");
                WriteConstant(writer, argument.Value);
                return;

            default:
                WriteConstant(writer, argument.Value);
                return;
        }
    }

    // Types used in attribute arguments are written without nullable annotations.
    // Types decoded from serialized names (typeof, named arguments) are written with their full name, without keywords.
    private static void WriteAttributeTypeReference(DeclarationWriter writer, PublicApiTypeReference type)
    {
        if (IsFromSerializedName(type))
        {
            WriteSerializedTypeName(writer, type);
            return;
        }

        WriteTypeReference(writer, type, includeNullableAnnotations: false);
    }

    private static bool IsFromSerializedName(PublicApiTypeReference type)
    {
        return type switch
        {
            PublicApiNamedTypeReference named => named.IsFromSerializedName,
            PublicApiArrayTypeReference array => IsFromSerializedName(array.ElementType),
            PublicApiPointerTypeReference pointer => IsFromSerializedName(pointer.ElementType),
            _ => false,
        };
    }

    private static void WriteSerializedTypeName(DeclarationWriter writer, PublicApiTypeReference type)
    {
        switch (type)
        {
            case PublicApiNamedTypeReference named:
                {
                    var path = new List<PublicApiNamedTypeReference>();
                    for (var current = named; current is not null; current = current.ContainingType)
                    {
                        path.Add(current);
                    }

                    path.Reverse();
                    for (var i = 0; i < path.Count; i++)
                    {
                        if (i > 0)
                        {
                            writer.Punctuation(".");
                        }

                        var name = i == 0 && path[i].Namespace.Length > 0 ? path[i].Namespace + "." + path[i].Name : path[i].Name;
                        writer.Write(PublicApiDeclarationSegmentKind.TypeName, name, path[i]);
                    }

                    var typeArguments = named.GetAllTypeArguments().ToArray();
                    if (typeArguments.Length > 0)
                    {
                        writer.Punctuation("<");
                        for (var i = 0; i < typeArguments.Length; i++)
                        {
                            if (i > 0)
                            {
                                writer.Punctuation(",");
                                writer.Space();
                            }

                            WriteSerializedTypeName(writer, typeArguments[i]);
                        }

                        writer.Punctuation(">");
                    }

                    return;
                }

            case PublicApiArrayTypeReference array:
                WriteSerializedTypeName(writer, array.ElementType);
                writer.Punctuation("[" + new string(',', array.Rank - 1) + "]");
                return;

            case PublicApiPointerTypeReference pointer:
                WriteSerializedTypeName(writer, pointer.ElementType);
                writer.Punctuation("*");
                return;

            default:
                writer.Write(PublicApiDeclarationSegmentKind.TypeName, type.ToString(), type);
                return;
        }
    }

    public static void WriteTypeReference(DeclarationWriter writer, PublicApiTypeReference type, bool includeNullableAnnotations)
    {
        var typeWriter = writer.CreateWriter();
        var isReferenceType = false;
        switch (type)
        {
            case PublicApiNamedTypeReference named:
                if (named.IsNullableValueType)
                {
                    WriteTypeReference(typeWriter, named.TypeArguments[0], includeNullableAnnotations);
                    typeWriter.Punctuation("?");
                    break;
                }

                isReferenceType = !named.IsValueType;
                typeWriter.Write(PublicApiDeclarationSegmentKind.TypeName, GetCompilableTypeName(named), named);
                var typeArguments = named.GetAllTypeArguments().ToArray();
                if (typeArguments.Length > 0)
                {
                    typeWriter.Punctuation("<");
                    for (var i = 0; i < typeArguments.Length; i++)
                    {
                        if (i > 0)
                        {
                            typeWriter.Punctuation(",");
                            typeWriter.Space();
                        }

                        WriteTypeReference(typeWriter, typeArguments[i], includeNullableAnnotations);
                    }

                    typeWriter.Punctuation(">");
                }

                break;

            case PublicApiTypeParameterReference typeParameter:
                typeWriter.Write(PublicApiDeclarationSegmentKind.TypeParameterName, CSharpIdentifierHelper.EscapeIdentifier(typeParameter.Name), typeParameter);
                break;

            case PublicApiArrayTypeReference array:
                isReferenceType = true;
                WriteTypeReference(typeWriter, array.ElementType, includeNullableAnnotations);
                typeWriter.Punctuation("[" + new string(',', array.Rank - 1) + "]");
                break;

            case PublicApiPointerTypeReference pointer:
                WriteTypeReference(typeWriter, pointer.ElementType, includeNullableAnnotations);
                typeWriter.Punctuation("*");
                break;

            case PublicApiFunctionPointerTypeReference functionPointer:
                // The calling conventions encoded as modifiers are not written: the reflection reader cannot see them
                typeWriter.Keyword("delegate");
                typeWriter.Punctuation("*");
                if (functionPointer.CallingConvention != PublicApiCallingConvention.Managed)
                {
                    typeWriter.Space();
                    typeWriter.Keyword("unmanaged");
                }

                typeWriter.Punctuation("<");
                foreach (var parameter in functionPointer.Parameters)
                {
                    if (parameter.RefKind != PublicApiRefKind.None)
                    {
                        typeWriter.Keyword("ref");
                        typeWriter.Space();
                    }

                    WriteTypeReference(typeWriter, parameter.Type, includeNullableAnnotations: false);
                    typeWriter.Punctuation(",");
                    typeWriter.Space();
                }

                if (functionPointer.ReturnRefKind != PublicApiRefKind.None)
                {
                    typeWriter.Keyword("ref");
                    typeWriter.Space();
                }

                WriteTypeReference(typeWriter, functionPointer.ReturnType, includeNullableAnnotations: false);
                typeWriter.Punctuation(">");
                break;
        }

        if (includeNullableAnnotations && isReferenceType && type.NullableAnnotation == PublicApiNullableAnnotation.Annotated && !typeWriter.EndsWith('?'))
        {
            typeWriter.Punctuation("?");
        }

        writer.Write(typeWriter);
    }

    private static string GetCompilableTypeName(PublicApiNamedTypeReference type)
    {
        if (type.IsPrimitive)
        {
            // TypedReference has never been mapped, and was written as object
            return CSharpSyntaxFacts.GetKeywordTypeName(type) ?? "object";
        }

        if (type.IsSystemType("Decimal"))
            return "decimal";

        // Nested types are written using their name only
        if (type.ContainingType is not null || type.Namespace.Length == 0)
            return type.Name;

        return type.Namespace + "." + type.Name;
    }

    private static void WriteConstant(DeclarationWriter writer, object? value)
    {
        var text = CSharpLiteralFormatter.Format(value);
        var kind = value switch
        {
            null or bool => PublicApiDeclarationSegmentKind.Keyword,
            string or char => PublicApiDeclarationSegmentKind.StringLiteral,
            _ => PublicApiDeclarationSegmentKind.NumericLiteral,
        };

        writer.Write(kind, text);
    }

    private static bool IsGeneratedUnionCaseConstructor(PublicApiMethod constructor)
    {
        return !constructor.IsStatic && constructor.Parameters.Length == 1 && constructor.Parameters[0].RefKind == PublicApiRefKind.None;
    }

    private static bool IsGeneratedUnionValueProperty(PublicApiProperty property)
    {
        return string.Equals(property.Name, "Value", StringComparison.Ordinal) &&
               property.GetMethod is not null &&
               property.Parameters.IsEmpty &&
               property.Type is PublicApiNamedTypeReference type &&
               type.IsSystemType("Object");
    }

    private static List<ExtensionPropertyBlock> GetExtensionPropertyBlocks(PublicApiType type)
    {
        // C# 14 extension properties are compiled to static get_/set_ methods whose first parameter is the receiver
        if (!type.IsStatic)
            return [];

        var blocks = new Dictionary<(string ReceiverType, string PropertyName), ExtensionPropertyBlock>();
        var order = new List<(string ReceiverType, string PropertyName)>();
        foreach (var member in type.Members)
        {
            if (member is not PublicApiMethod { IsStatic: true, MethodKind: PublicApiMethodKind.Ordinary, IsExtensionMethod: false } method)
                continue;

            bool isGetter;
            if (method.Name.StartsWith("get_", StringComparison.Ordinal) && method.Parameters.Length >= 1)
            {
                isGetter = true;
            }
            else if (method.Name.StartsWith("set_", StringComparison.Ordinal) && method.Parameters.Length >= 2)
            {
                isGetter = false;
            }
            else
            {
                continue;
            }

            var key = (DeclarationKey(method.Parameters[0].Type), method.Name[4..]);
            if (!blocks.TryGetValue(key, out var block))
            {
                block = new ExtensionPropertyBlock(method.Parameters[0], key.Item2);
                blocks.Add(key, block);
                order.Add(key);
            }

            if (isGetter)
            {
                block.Getter = method;
                block.PropertyType = method.ReturnType;
            }
            else
            {
                block.Setter = method;
                block.PropertyType = method.Parameters[1].Type;
            }

            block.Order = Math.Min(block.Order, method.Origin!.MetadataToken);
        }

        return [.. order.Select(key => blocks[key])];

        static string DeclarationKey(PublicApiTypeReference receiverType)
        {
            var writer = new DeclarationWriter("\n");
            WriteTypeReference(writer, receiverType, includeNullableAnnotations: true);
            return writer.GetText();
        }
    }

    private static void WriteExtensionPropertyBlock(DeclarationWriter writer, ExtensionPropertyBlock block)
    {
        writer.Keyword("extension");
        writer.Punctuation("(");
        if (block.Receiver.RefKind != PublicApiRefKind.None)
        {
            writer.Keyword("ref");
            writer.Space();
        }

        WriteTypeReference(writer, block.Receiver.Type, includeNullableAnnotations: true);
        writer.Space();
        writer.Write(PublicApiDeclarationSegmentKind.ParameterName, block.Receiver.Name is null ? "value" : CSharpIdentifierHelper.EscapeIdentifier(block.Receiver.Name));
        writer.Punctuation(")");
        writer.WriteLine();
        writer.Punctuation("{");
        writer.WriteLine();
        writer.Indentation++;
        writer.Keyword("public");
        writer.Space();
        WriteTypeReference(writer, block.PropertyType!, includeNullableAnnotations: true);
        writer.Space();
        writer.Write(PublicApiDeclarationSegmentKind.Identifier, CSharpIdentifierHelper.EscapeIdentifier(block.PropertyName));
        writer.Space();
        writer.Punctuation("{");
        if (block.Getter is not null)
        {
            writer.Space();
            writer.Keyword("get");
            WriteThrowNullBody(writer);
        }

        if (block.Setter is not null)
        {
            writer.Space();
            writer.Keyword("set");
            writer.Space();
            writer.Punctuation("{");
            writer.Space();
            writer.Punctuation("}");
        }

        writer.Space();
        writer.Punctuation("}");
        writer.WriteLine();
        writer.Indentation--;
        writer.Punctuation("}");
    }

    private sealed class ExtensionPropertyBlock(PublicApiParameter receiver, string propertyName)
    {
        public PublicApiParameter Receiver { get; } = receiver;

        public string PropertyName { get; } = propertyName;

        public PublicApiTypeReference? PropertyType { get; set; }

        public PublicApiMethod? Getter { get; set; }

        public PublicApiMethod? Setter { get; set; }

        public int Order { get; set; } = int.MaxValue;

        public IEnumerable<PublicApiMethod> Accessors
        {
            get
            {
                if (Getter is not null)
                    yield return Getter;

                if (Setter is not null)
                    yield return Setter;
            }
        }
    }

    private sealed record ParameterText(DeclarationWriter Writer, bool RequiresNullableDirectives);
}
