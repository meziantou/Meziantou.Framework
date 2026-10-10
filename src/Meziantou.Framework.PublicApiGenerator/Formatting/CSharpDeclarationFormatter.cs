using System.Collections.Immutable;

namespace Meziantou.Framework.PublicApiGenerator;

/// <summary>Formats symbols as C# declarations suitable for display (see <see cref="PublicApiDeclarationStyle.Declaration"/>).</summary>
internal sealed class CSharpDeclarationFormatter
{
    private readonly PublicApiFormattingOptions _options;

    public CSharpDeclarationFormatter(PublicApiFormattingOptions options)
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

            case PublicApiField field when field.DeclaringType?.TypeKind == PublicApiTypeKind.Enum:
                WriteAttributes(writer, field.Attributes, target: null);
                writer.Write(PublicApiDeclarationSegmentKind.Identifier, CSharpIdentifierHelper.EscapeIdentifier(field.Name), symbol: field);
                if (field.HasConstantValue)
                {
                    WriteAssignment(writer);
                    WriteConstant(writer, field.ConstantValue, type: null, assembly: null);
                }

                break;

            case PublicApiField field:
                WriteField(writer, field);
                break;

            case PublicApiProperty property:
                WriteProperty(writer, property, onlyAccessor: null);
                break;

            case PublicApiEvent @event:
                WriteEvent(writer, @event);
                break;

            case PublicApiMethod { AssociatedSymbol: PublicApiProperty property } accessor:
                WriteProperty(writer, property, accessor);
                break;

            case PublicApiMethod { AssociatedSymbol: PublicApiEvent @event }:
                WriteEvent(writer, @event);
                break;

            case PublicApiMethod method:
                WriteMethod(writer, method);
                break;
        }
    }

    private void WriteType(DeclarationWriter writer, PublicApiType type)
    {
        WriteAttributes(writer, type.Attributes, target: null, type.IsUnion ? IsUnionAttribute : null);
        WriteKeyword(writer, CSharpSyntaxFacts.GetAccessibilityText(type.Accessibility));
        switch (type.TypeKind)
        {
            case PublicApiTypeKind.Class:
                if (type.IsStatic)
                {
                    WriteKeyword(writer, "static");
                }
                else if (type.IsClosed)
                {
                    WriteKeyword(writer, "closed");
                }
                else if (type.IsAbstract)
                {
                    WriteKeyword(writer, "abstract");
                }

                if (type.IsSealed)
                {
                    WriteKeyword(writer, "sealed");
                }

                WriteKeyword(writer, "class");
                break;

            case PublicApiTypeKind.Struct:
                if (type.IsReadOnly)
                {
                    WriteKeyword(writer, "readonly");
                }

                if (type.IsRefLike)
                {
                    WriteKeyword(writer, "ref");
                }

                WriteKeyword(writer, type.IsUnion ? "union" : "struct");
                break;

            case PublicApiTypeKind.Interface:
                WriteKeyword(writer, "interface");
                break;

            case PublicApiTypeKind.Enum:
                WriteKeyword(writer, "enum");
                break;

            case PublicApiTypeKind.Delegate:
                WriteKeyword(writer, "delegate");
                var invokeMethod = type.DelegateInvokeMethod!;
                WriteReturnType(writer, invokeMethod);
                writer.Space();
                break;
        }

        writer.Write(PublicApiDeclarationSegmentKind.Identifier, CSharpIdentifierHelper.EscapeIdentifier(type.Name), symbol: type);
        WriteGenericParameterList(writer, type.GenericParameters);
        if (type.TypeKind == PublicApiTypeKind.Delegate)
        {
            WriteParameterList(writer, type.DelegateInvokeMethod!.Parameters, "(", ")", type.Assembly);
        }

        if (type.IsUnion)
        {
            writer.Punctuation("(");
            WriteList(writer, type.UnionCaseTypes, type => WriteTypeReference(writer, type));
            writer.Punctuation(")");
        }

        var baseTypes = new List<PublicApiTypeReference>();
        if (type.TypeKind == PublicApiTypeKind.Class && type.BaseType is PublicApiNamedTypeReference baseType && !baseType.IsSystemType("Object"))
        {
            baseTypes.Add(baseType);
        }
        else if (type.TypeKind == PublicApiTypeKind.Enum && type.EnumUnderlyingType is { } underlyingType && !underlyingType.IsSystemType("Int32"))
        {
            baseTypes.Add(underlyingType);
        }

        foreach (var @interface in type.Interfaces)
        {
            if (type.IsUnion && @interface is PublicApiNamedTypeReference named && string.Equals(named.FullName, PublicApiMetadataReader.IUnionInterfaceFullName, StringComparison.Ordinal))
                continue;

            baseTypes.Add(@interface);
        }

        if (baseTypes.Count > 0)
        {
            writer.Space();
            writer.Punctuation(":");
            writer.Space();
            WriteList(writer, baseTypes, type => WriteTypeReference(writer, type));
        }

        WriteConstraints(writer, type.GenericParameters);
    }

    private static bool IsUnionAttribute(PublicApiAttribute attribute)
    {
        return attribute.AttributeType.FullName.StartsWith("System.Runtime.CompilerServices.Union", StringComparison.Ordinal);
    }

    private void WriteField(DeclarationWriter writer, PublicApiField field)
    {
        WriteAttributes(writer, field.Attributes, target: null);
        WriteKeyword(writer, CSharpSyntaxFacts.GetAccessibilityText(field.Accessibility));
        if (field.IsConst)
        {
            WriteKeyword(writer, "const");
        }
        else if (field.IsStatic)
        {
            WriteKeyword(writer, "static");
        }

        if (field.IsRequired)
        {
            WriteKeyword(writer, "required");
        }

        if (field.IsReadOnly)
        {
            WriteKeyword(writer, "readonly");
        }

        if (field.IsVolatile)
        {
            WriteKeyword(writer, "volatile");
        }

        if (field.RequiresUnsafe)
        {
            WriteKeyword(writer, "unsafe");
        }

        WriteRefKind(writer, field.RefKind, isReturn: true);
        WriteTypeReference(writer, field.Type);
        writer.Space();
        writer.Write(PublicApiDeclarationSegmentKind.Identifier, CSharpIdentifierHelper.EscapeIdentifier(field.Name), symbol: field);
        if (field.IsConst && field.HasConstantValue)
        {
            WriteAssignment(writer);
            WriteConstant(writer, field.ConstantValue, field.Type, field.Assembly);
        }
    }

    private void WriteProperty(DeclarationWriter writer, PublicApiProperty property, PublicApiMethod? onlyAccessor)
    {
        WriteAttributes(writer, property.Attributes, target: null);
        WriteMemberModifiers(writer, property, isReadOnly: property.IsReadOnly);
        if (property.IsRequired)
        {
            WriteKeyword(writer, "required");
        }

        WriteRefKind(writer, property.RefKind, isReturn: true);
        WriteTypeReference(writer, property.Type);
        writer.Space();
        if (property.IsExplicitInterfaceImplementation)
        {
            WriteExplicitInterfaceName(writer, property);
        }

        if (property.IsIndexer)
        {
            writer.Write(PublicApiDeclarationSegmentKind.Identifier, "this", symbol: property);
            WriteParameterList(writer, property.Parameters, "[", "]", property.Assembly);
        }
        else
        {
            writer.Write(PublicApiDeclarationSegmentKind.Identifier, CSharpIdentifierHelper.EscapeIdentifier(GetSimpleMemberName(property.Name)), symbol: property);
        }

        writer.Space();
        writer.Punctuation("{");
        foreach (var accessor in (ReadOnlySpan<PublicApiMethod?>)[property.GetMethod, property.SetMethod])
        {
            if (accessor is null || (onlyAccessor is not null && !ReferenceEquals(accessor, onlyAccessor)))
                continue;

            writer.Space();
            if (accessor.Accessibility != property.Accessibility && !property.IsExplicitInterfaceImplementation)
            {
                WriteKeyword(writer, CSharpSyntaxFacts.GetAccessibilityText(accessor.Accessibility));
            }

            if (accessor.IsReadOnly && !property.IsReadOnly)
            {
                WriteKeyword(writer, "readonly");
            }

            if (accessor.RequiresUnsafe && !property.RequiresUnsafe)
            {
                WriteKeyword(writer, "unsafe");
            }

            var keyword = accessor.MethodKind == PublicApiMethodKind.PropertyGet ? "get" : accessor.IsInitOnly ? "init" : "set";
            writer.Write(PublicApiDeclarationSegmentKind.Keyword, keyword, symbol: accessor);
            writer.Punctuation(";");
        }

        writer.Space();
        writer.Punctuation("}");
    }

    private void WriteEvent(DeclarationWriter writer, PublicApiEvent @event)
    {
        WriteAttributes(writer, @event.Attributes, target: null);
        WriteMemberModifiers(writer, @event, isReadOnly: false);
        WriteKeyword(writer, "event");
        WriteTypeReference(writer, @event.Type);
        writer.Space();
        if (@event.IsExplicitInterfaceImplementation)
        {
            WriteExplicitInterfaceName(writer, @event);
        }

        writer.Write(PublicApiDeclarationSegmentKind.Identifier, CSharpIdentifierHelper.EscapeIdentifier(GetSimpleMemberName(@event.Name)), symbol: @event);
    }

    private void WriteMethod(DeclarationWriter writer, PublicApiMethod method)
    {
        var declaringType = method.DeclaringType!;
        WriteAttributes(writer, method.Attributes, target: null);
        WriteAttributes(writer, method.ReturnAttributes, target: "return");
        if (method.MethodKind == PublicApiMethodKind.Destructor)
        {
            writer.Punctuation("~");
            writer.Write(PublicApiDeclarationSegmentKind.Identifier, CSharpIdentifierHelper.EscapeIdentifier(declaringType.Name), symbol: method);
            writer.Punctuation("()");
            return;
        }

        WriteMemberModifiers(writer, method, method.IsReadOnly);
        if (method.MethodKind == PublicApiMethodKind.Constructor)
        {
            writer.Write(PublicApiDeclarationSegmentKind.Identifier, CSharpIdentifierHelper.EscapeIdentifier(declaringType.Name), symbol: method);
            WriteParameterList(writer, method.Parameters, "(", ")", method.Assembly);
            return;
        }

        var operatorToken = GetOperatorToken(method);
        if (method.MethodKind == PublicApiMethodKind.Conversion && operatorToken is not null)
        {
            WriteKeyword(writer, method.Name is "op_Implicit" ? "implicit" : "explicit");
            WriteKeyword(writer, "operator");
            if (method.Name is "op_CheckedExplicit")
            {
                WriteKeyword(writer, "checked");
            }

            WriteReturnType(writer, method);
        }
        else
        {
            WriteReturnType(writer, method);
            writer.Space();
            if (operatorToken is not null)
            {
                WriteKeyword(writer, "operator");
                if (method.Name.StartsWith("op_Checked", StringComparison.Ordinal))
                {
                    WriteKeyword(writer, "checked");
                }

                writer.Write(PublicApiDeclarationSegmentKind.Operator, operatorToken, symbol: method);
            }
            else
            {
                if (method.IsExplicitInterfaceImplementation)
                {
                    WriteExplicitInterfaceName(writer, method);
                }

                writer.Write(PublicApiDeclarationSegmentKind.Identifier, CSharpIdentifierHelper.EscapeIdentifier(GetSimpleMemberName(method.Name)), symbol: method);
                WriteGenericParameterList(writer, method.GenericParameters);
            }
        }

        WriteParameterList(writer, method.Parameters, "(", ")", method.Assembly);
        WriteConstraints(writer, method.GenericParameters);
    }

    private static string? GetOperatorToken(PublicApiMethod method)
    {
        if (method.MethodKind is not (PublicApiMethodKind.Operator or PublicApiMethodKind.Conversion) || method.DeclaringType!.IsStatic)
            return null;

        var name = method.Name.StartsWith("op_Checked", StringComparison.Ordinal) ? "op_" + method.Name["op_Checked".Length..] : method.Name;
        return CSharpSyntaxFacts.GetOperatorToken(name);
    }

    private void WriteExplicitInterfaceName(DeclarationWriter writer, PublicApiMember member)
    {
        if (member.ExplicitInterfaceImplementations.Length > 0)
        {
            WriteTypeReference(writer, member.ExplicitInterfaceImplementations[0].ContainingType);
            writer.Punctuation(".");
            return;
        }

        var separatorIndex = member.Name.LastIndexOf(".", StringComparison.Ordinal);
        if (separatorIndex > 0)
        {
            writer.Write(PublicApiDeclarationSegmentKind.TypeName, member.Name[..separatorIndex]);
            writer.Punctuation(".");
        }
    }

    private static string GetSimpleMemberName(string name)
    {
        var separatorIndex = name.LastIndexOf(".", StringComparison.Ordinal);
        return separatorIndex < 0 ? name : name[(separatorIndex + 1)..];
    }

    private static void WriteMemberModifiers(DeclarationWriter writer, PublicApiMember member, bool isReadOnly)
    {
        if (member.IsExplicitInterfaceImplementation)
        {
            if (member.RequiresUnsafe)
            {
                WriteKeyword(writer, "unsafe");
            }

            return;
        }

        var isInterface = member.DeclaringType!.TypeKind == PublicApiTypeKind.Interface;
        if (!(isInterface && member.IsAbstract && !member.IsStatic))
        {
            WriteKeyword(writer, CSharpSyntaxFacts.GetAccessibilityText(member.Accessibility));
        }

        if (member.IsStatic)
        {
            WriteKeyword(writer, "static");
        }

        if (isInterface)
        {
            if (member.IsStatic && member.IsAbstract)
            {
                WriteKeyword(writer, "abstract");
            }
            else if (member.IsStatic && member.IsVirtual)
            {
                WriteKeyword(writer, "virtual");
            }
        }
        else if (member.IsAbstract)
        {
            WriteKeyword(writer, "abstract");
            if (member.IsOverride)
            {
                WriteKeyword(writer, "override");
            }
        }
        else if (member.IsVirtual)
        {
            WriteKeyword(writer, "virtual");
        }
        else if (member.IsSealed)
        {
            WriteKeyword(writer, "sealed");
            WriteKeyword(writer, "override");
        }
        else if (member.IsOverride)
        {
            WriteKeyword(writer, "override");
        }

        if (isReadOnly)
        {
            WriteKeyword(writer, "readonly");
        }

        if (member.RequiresUnsafe)
        {
            WriteKeyword(writer, "unsafe");
        }
    }

    private void WriteReturnType(DeclarationWriter writer, PublicApiMethod method)
    {
        WriteRefKind(writer, method.ReturnRefKind, isReturn: true);
        WriteTypeReference(writer, method.ReturnType);
    }

    private static void WriteRefKind(DeclarationWriter writer, PublicApiRefKind refKind, bool isReturn)
    {
        switch (refKind)
        {
            case PublicApiRefKind.Ref:
                WriteKeyword(writer, "ref");
                break;

            case PublicApiRefKind.Out:
                WriteKeyword(writer, "out");
                break;

            case PublicApiRefKind.In:
                WriteKeyword(writer, isReturn ? "ref readonly" : "in");
                break;

            case PublicApiRefKind.RefReadOnly:
                WriteKeyword(writer, "ref");
                WriteKeyword(writer, "readonly");
                break;
        }
    }

    private void WriteParameterList(DeclarationWriter writer, ImmutableArray<PublicApiParameter> parameters, string open, string close, PublicApiAssembly assembly)
    {
        writer.Punctuation(open);
        WriteList(writer, parameters, parameter => WriteParameter(writer, parameter, assembly));
        writer.Punctuation(close);
    }

    private void WriteParameter(DeclarationWriter writer, PublicApiParameter parameter, PublicApiAssembly assembly)
    {
        foreach (var attribute in GetAttributes(parameter.Attributes))
        {
            WriteAttribute(writer, attribute, target: null);
            writer.Space();
        }

        if (parameter.IsThis)
        {
            WriteKeyword(writer, "this");
        }

        if (parameter.IsParams || parameter.IsParamsCollection)
        {
            WriteKeyword(writer, "params");
        }

        if (parameter.IsScoped && parameter.RefKind != PublicApiRefKind.Out)
        {
            WriteKeyword(writer, "scoped");
        }

        WriteRefKind(writer, parameter.RefKind, isReturn: false);
        WriteTypeReference(writer, parameter.Type);
        writer.Space();
        writer.Write(PublicApiDeclarationSegmentKind.ParameterName, parameter.Name is null ? "arg" + (parameter.Ordinal + 1).ToString(CultureInfo.InvariantCulture) : CSharpIdentifierHelper.EscapeIdentifier(parameter.Name));
        if (parameter.HasDefaultValue)
        {
            WriteAssignment(writer);
            WriteConstant(writer, parameter.DefaultValue, parameter.Type, assembly);
        }
    }

    private void WriteGenericParameterList(DeclarationWriter writer, ImmutableArray<PublicApiGenericParameter> genericParameters)
    {
        if (genericParameters.IsEmpty)
            return;

        writer.Punctuation("<");
        WriteList(writer, genericParameters, genericParameter =>
        {
            foreach (var attribute in GetAttributes(genericParameter.Attributes))
            {
                WriteAttribute(writer, attribute, target: null);
                writer.Space();
            }

            if (genericParameter.Variance == PublicApiVariance.Covariant)
            {
                WriteKeyword(writer, "out");
            }
            else if (genericParameter.Variance == PublicApiVariance.Contravariant)
            {
                WriteKeyword(writer, "in");
            }

            writer.Write(PublicApiDeclarationSegmentKind.TypeParameterName, CSharpIdentifierHelper.EscapeIdentifier(genericParameter.Name));
        });
        writer.Punctuation(">");
    }

    private void WriteConstraints(DeclarationWriter writer, ImmutableArray<PublicApiGenericParameter> genericParameters)
    {
        foreach (var genericParameter in genericParameters)
        {
            var constraints = new List<Action>();
            if (genericParameter.HasReferenceTypeConstraint)
            {
                constraints.Add(() =>
                {
                    writer.Keyword("class");
                    if (genericParameter.NullableAnnotation == PublicApiNullableAnnotation.Annotated)
                    {
                        writer.Punctuation("?");
                    }
                });
            }
            else if (genericParameter.HasUnmanagedTypeConstraint)
            {
                constraints.Add(() => writer.Keyword("unmanaged"));
            }
            else if (genericParameter.HasValueTypeConstraint)
            {
                constraints.Add(() => writer.Keyword("struct"));
            }
            else if (genericParameter.NullableAnnotation == PublicApiNullableAnnotation.NotAnnotated)
            {
                constraints.Add(() => writer.Keyword("notnull"));
            }

            foreach (var constraintType in genericParameter.ConstraintTypes)
            {
                constraints.Add(() => WriteTypeReference(writer, constraintType));
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
            WriteList(writer, constraints, constraint => constraint());
        }
    }

    public void WriteTypeReference(DeclarationWriter writer, PublicApiTypeReference type)
    {
        switch (type)
        {
            case PublicApiNamedTypeReference named:
                WriteNamedTypeReference(writer, named);
                break;

            case PublicApiTypeParameterReference typeParameter:
                writer.Write(PublicApiDeclarationSegmentKind.TypeParameterName, CSharpIdentifierHelper.EscapeIdentifier(typeParameter.Name), typeParameter);
                WriteNullableAnnotation(writer, type);
                break;

            case PublicApiArrayTypeReference array:
                WriteTypeReference(writer, array.ElementType);
                writer.Punctuation("[" + new string(',', array.Rank - 1) + "]");
                WriteNullableAnnotation(writer, type);
                break;

            case PublicApiPointerTypeReference pointer:
                WriteTypeReference(writer, pointer.ElementType);
                writer.Punctuation("*");
                break;

            case PublicApiFunctionPointerTypeReference functionPointer:
                writer.Keyword("delegate");
                writer.Punctuation("*");
                var callingConvention = functionPointer.CallingConvention switch
                {
                    PublicApiCallingConvention.Managed => null,
                    PublicApiCallingConvention.CDecl => "Cdecl",
                    PublicApiCallingConvention.StdCall => "Stdcall",
                    PublicApiCallingConvention.ThisCall => "Thiscall",
                    PublicApiCallingConvention.FastCall => "Fastcall",
                    _ => string.Empty,
                };
                if (callingConvention is not null)
                {
                    writer.Space();
                    writer.Keyword("unmanaged");
                    if (callingConvention.Length > 0)
                    {
                        writer.Punctuation("[");
                        writer.Write(PublicApiDeclarationSegmentKind.Text, callingConvention);
                        writer.Punctuation("]");
                    }
                }

                writer.Punctuation("<");
                foreach (var parameter in functionPointer.Parameters)
                {
                    WriteRefKind(writer, parameter.RefKind, isReturn: false);
                    WriteTypeReference(writer, parameter.Type);
                    writer.Punctuation(",");
                    writer.Space();
                }

                WriteRefKind(writer, functionPointer.ReturnRefKind, isReturn: true);
                WriteTypeReference(writer, functionPointer.ReturnType);
                writer.Punctuation(">");
                break;
        }
    }

    private void WriteNamedTypeReference(DeclarationWriter writer, PublicApiNamedTypeReference type)
    {
        if (type.IsNullableValueType)
        {
            WriteTypeReference(writer, type.TypeArguments[0]);
            writer.Punctuation("?");
            return;
        }

        if (CSharpSyntaxFacts.GetKeywordTypeName(type) is { } keyword)
        {
            writer.Write(PublicApiDeclarationSegmentKind.TypeName, keyword, type);
            WriteNullableAnnotation(writer, type);
            return;
        }

        if (CSharpSyntaxFacts.TryGetTupleElements(type, out var elements))
        {
            writer.Punctuation("(");
            WriteList(writer, elements, element =>
            {
                WriteTypeReference(writer, element.Type);
                if (element.Name is not null)
                {
                    writer.Space();
                    writer.Write(PublicApiDeclarationSegmentKind.MemberName, CSharpIdentifierHelper.EscapeIdentifier(element.Name));
                }
            });
            writer.Punctuation(")");
            WriteNullableAnnotation(writer, type);
            return;
        }

        if (type.ContainingType is not null)
        {
            WriteNamedTypeReference(writer, type.ContainingType);
            writer.Punctuation(".");
            writer.Write(PublicApiDeclarationSegmentKind.TypeName, CSharpIdentifierHelper.EscapeIdentifier(type.Name), type);
        }
        else
        {
            var name = _options.QualifyTypeNames && type.Namespace.Length > 0 ? type.Namespace + "." + CSharpIdentifierHelper.EscapeIdentifier(type.Name) : CSharpIdentifierHelper.EscapeIdentifier(type.Name);
            writer.Write(PublicApiDeclarationSegmentKind.TypeName, name, type);
        }

        if (!type.TypeArguments.IsEmpty)
        {
            writer.Punctuation("<");
            WriteList(writer, type.TypeArguments, typeArgument => WriteTypeReference(writer, typeArgument));
            writer.Punctuation(">");
        }

        WriteNullableAnnotation(writer, type);
    }

    private static void WriteNullableAnnotation(DeclarationWriter writer, PublicApiTypeReference type)
    {
        if (type.NullableAnnotation == PublicApiNullableAnnotation.Annotated && type is not PublicApiNamedTypeReference { IsValueType: true })
        {
            writer.Punctuation("?");
        }
    }

    private void WriteConstant(DeclarationWriter writer, object? value, PublicApiTypeReference? type, PublicApiAssembly? assembly)
    {
        var enumType = type as PublicApiNamedTypeReference;
        if (enumType is { IsValueType: true, IsNullableValueType: false } && CSharpSyntaxFacts.GetKeywordTypeName(enumType) is null)
        {
            if (value is null)
            {
                writer.Keyword("default");
                return;
            }

            // A constant of an enum type is stored as its underlying value. The names of the members are only known for the enums of the same assembly.
            var memberNames = GetEnumMemberNames(assembly?.FindType(enumType.FullName), value);
            if (!memberNames.IsEmpty)
            {
                WriteList(writer, memberNames, memberName =>
                {
                    WriteTypeReference(writer, enumType);
                    writer.Punctuation(".");
                    writer.Write(PublicApiDeclarationSegmentKind.MemberName, CSharpIdentifierHelper.EscapeIdentifier(memberName));
                }, separator: " | ");
                return;
            }

            writer.Punctuation("(");
            WriteTypeReference(writer, enumType);
            writer.Punctuation(")");
        }

        var text = CSharpLiteralFormatter.Format(value);
        var kind = value switch
        {
            null or bool => PublicApiDeclarationSegmentKind.Keyword,
            string or char => PublicApiDeclarationSegmentKind.StringLiteral,
            _ => PublicApiDeclarationSegmentKind.NumericLiteral,
        };

        writer.Write(kind, text);
    }

    private static ImmutableArray<string> GetEnumMemberNames(PublicApiType? enumType, object value)
    {
        if (enumType is not { TypeKind: PublicApiTypeKind.Enum } || !EnumMetadata.TryGetValueBits(value, out var valueBits))
            return [];

        var members = new List<(ulong Value, string Name)>();
        foreach (var member in enumType.Members)
        {
            if (member is PublicApiField { HasConstantValue: true } field && EnumMetadata.TryGetValueBits(field.ConstantValue, out var memberValue))
            {
                if (memberValue == valueBits)
                    return [field.Name];

                members.Add((memberValue, field.Name));
            }
        }

        if (valueBits == 0 || !PublicApiSymbol.HasAttribute(enumType.Attributes, "System", "FlagsAttribute"))
            return [];

        // Same algorithm as Enum.ToString for flags enums: the largest values are matched first, and the names are listed from the smallest value
        var remainingValue = valueBits;
        var result = new List<string>();
        foreach (var member in members.Where(static member => member.Value != 0).OrderByDescending(static member => member.Value).ThenBy(static member => member.Name, StringComparer.Ordinal))
        {
            if ((remainingValue & member.Value) == member.Value)
            {
                result.Add(member.Name);
                remainingValue &= ~member.Value;
            }
        }

        if (remainingValue != 0)
            return [];

        result.Reverse();
        return [.. result];
    }

    private void WriteAttributes(DeclarationWriter writer, ImmutableArray<PublicApiAttribute> attributes, string? target, Func<PublicApiAttribute, bool>? exclude = null)
    {
        foreach (var attribute in GetAttributes(attributes))
        {
            if (exclude is not null && exclude(attribute))
                continue;

            WriteAttribute(writer, attribute, target);
            writer.WriteLine();
        }
    }

    private IEnumerable<PublicApiAttribute> GetAttributes(ImmutableArray<PublicApiAttribute> attributes)
    {
        if (!_options.IncludeAttributes)
            return [];

        var filter = _options.AttributeFilter;
        return attributes.Where(attribute => filter is null ? CSharpSyntaxFacts.IsDisplayedAttribute(attribute, PublicApiDeclarationStyle.Declaration) : filter(attribute));
    }

    public void WriteAttribute(DeclarationWriter writer, PublicApiAttribute attribute, string? target)
    {
        writer.Punctuation("[");
        if (target is not null)
        {
            writer.Keyword(target);
            writer.Punctuation(":");
            writer.Space();
        }

        var attributeType = attribute.AttributeType;
        var name = attributeType.Name.EndsWith("Attribute", StringComparison.Ordinal) ? attributeType.Name[..^"Attribute".Length] : attributeType.Name;
        if (attributeType.ContainingType is not null)
        {
            WriteNamedTypeReference(writer, attributeType.ContainingType);
            writer.Punctuation(".");
        }
        else if (_options.QualifyTypeNames && attributeType.Namespace.Length > 0)
        {
            name = attributeType.Namespace + "." + name;
        }

        writer.Write(PublicApiDeclarationSegmentKind.TypeName, name, attributeType);
        if (!attributeType.TypeArguments.IsEmpty)
        {
            writer.Punctuation("<");
            WriteList(writer, attributeType.TypeArguments, typeArgument => WriteTypeReference(writer, typeArgument));
            writer.Punctuation(">");
        }

        if (!attribute.ConstructorArguments.IsEmpty || !attribute.NamedArguments.IsEmpty)
        {
            writer.Punctuation("(");
            var isFirst = true;
            foreach (var argument in attribute.ConstructorArguments)
            {
                WriteSeparator(writer, ref isFirst);
                WriteAttributeArgument(writer, argument);
            }

            foreach (var argument in attribute.NamedArguments)
            {
                WriteSeparator(writer, ref isFirst);
                writer.Write(PublicApiDeclarationSegmentKind.MemberName, argument.Name);
                WriteAssignment(writer);
                WriteAttributeArgument(writer, argument.Value);
            }

            writer.Punctuation(")");
        }

        writer.Punctuation("]");
    }

    private void WriteAttributeArgument(DeclarationWriter writer, PublicApiAttributeArgument argument)
    {
        if (argument.Value is null)
        {
            writer.Keyword("null");
            return;
        }

        switch (argument.Kind)
        {
            case PublicApiAttributeArgumentKind.Array:
                var values = (ImmutableArray<PublicApiAttributeArgument>)argument.Value;
                writer.Keyword("new");
                writer.Space();
                WriteTypeReference(writer, argument.Type);
                writer.Space();
                writer.Punctuation("{");
                if (!values.IsEmpty)
                {
                    writer.Space();
                    WriteList(writer, values, value => WriteAttributeArgument(writer, value));
                }

                writer.Space();
                writer.Punctuation("}");
                break;

            case PublicApiAttributeArgumentKind.Type:
                writer.Keyword("typeof");
                writer.Punctuation("(");
                WriteTypeReference(writer, (PublicApiTypeReference)argument.Value);
                writer.Punctuation(")");
                break;

            case PublicApiAttributeArgumentKind.Enum when !argument.EnumMemberNames.IsEmpty:
                WriteList(writer, argument.EnumMemberNames, memberName =>
                {
                    WriteTypeReference(writer, argument.Type);
                    writer.Punctuation(".");
                    writer.Write(PublicApiDeclarationSegmentKind.MemberName, memberName);
                }, separator: " | ");
                break;

            case PublicApiAttributeArgumentKind.Enum:
                writer.Punctuation("(");
                WriteTypeReference(writer, argument.Type);
                writer.Punctuation(")");
                WriteConstant(writer, argument.Value, type: null, assembly: null);
                break;

            default:
                WriteConstant(writer, argument.Value, type: null, assembly: null);
                break;
        }
    }

    private static void WriteKeyword(DeclarationWriter writer, string keyword)
    {
        writer.Keyword(keyword);
        writer.Space();
    }

    private static void WriteAssignment(DeclarationWriter writer)
    {
        writer.Space();
        writer.Punctuation("=");
        writer.Space();
    }

    private static void WriteSeparator(DeclarationWriter writer, ref bool isFirst)
    {
        if (!isFirst)
        {
            writer.Punctuation(",");
            writer.Space();
        }

        isFirst = false;
    }

    private static void WriteList<T>(DeclarationWriter writer, IEnumerable<T> items, Action<T> writeItem, string separator = ", ")
    {
        var isFirst = true;
        foreach (var item in items)
        {
            if (!isFirst)
            {
                if (separator is " | ")
                {
                    writer.Space();
                    writer.Operator("|");
                    writer.Space();
                }
                else
                {
                    writer.Punctuation(",");
                    writer.Space();
                }
            }

            isFirst = false;
            writeItem(item);
        }
    }
}
