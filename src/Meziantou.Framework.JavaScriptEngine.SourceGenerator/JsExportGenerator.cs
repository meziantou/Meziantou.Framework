using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using Meziantou.Framework.Collections;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Meziantou.Framework.JavaScriptEngine.SourceGeneration;

/// <summary>Generates the JavaScript binding of the classes marked with <c>[JsExport]</c>.</summary>
[Generator]
public sealed class JsExportGenerator : IIncrementalGenerator
{
    internal const string OutputsTrackingName = "JsExportOutputs";

    private const string RuntimeNamespace = "Meziantou.Framework.JavaScriptEngine";
    private const string Runtime = "global::" + RuntimeNamespace + ".";
    private const string Category = "Meziantou.Framework.JavaScriptEngine.SourceGeneration";
    private const string NestedClassName = "__JsExportClass";

    private static readonly DiagnosticDescriptor TypeMustBePartial = new(
        id: "MFJS001",
        title: "Exported class must be partial",
        messageFormat: "Class '{0}' is marked with [JsExport] and must be declared partial, as must its containing types",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor UnsupportedType = new(
        id: "MFJS002",
        title: "Exported class is not supported",
        messageFormat: "Class '{0}' cannot be marked with [JsExport] because it is {1}",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor UnsupportedMember = new(
        id: "MFJS003",
        title: "Member cannot be exposed to JavaScript",
        messageFormat: "Member '{0}' cannot be exposed to JavaScript: {1}. Hide it with [JsIgnore].",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor DuplicateName = new(
        id: "MFJS004",
        title: "Members have the same JavaScript name",
        messageFormat: "The JavaScript name '{0}' of member '{1}' is already used by {2}. Rename one of them with [JsName], or hide it with [JsIgnore].",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor AmbiguousConstructor = new(
        id: "MFJS005",
        title: "The constructor of the JavaScript class is ambiguous",
        messageFormat: "Class '{0}' has several public constructors: mark the constructor of the JavaScript class with [JsConstructor], or hide the others with [JsIgnore]",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor InvalidName = new(
        id: "MFJS006",
        title: "JavaScript name is empty",
        messageFormat: "The JavaScript name of '{0}' must not be empty",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    /// <inheritdoc />
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        // The transform emits the code, so the pipeline carries equatable values: an edit that does not change the output
        // does not add the source again
        var outputs = context.SyntaxProvider.ForAttributeWithMetadataName(
                RuntimeNamespace + ".JsExportAttribute",
                (node, _) => node is ClassDeclarationSyntax or RecordDeclarationSyntax,
                (context, cancellationToken) => CreateOutput(context, cancellationToken))
            .WithTrackingName(OutputsTrackingName);

        context.RegisterSourceOutput(outputs, (context, output) =>
        {
            if (output.Source is not null)
            {
                context.AddSource(output.HintName, output.Source);
            }
        });

        // Diagnostics are reported at a location in the syntax trees of the compilation, so '#pragma warning disable' applies to them
        var diagnostics = outputs.Select((output, _) => output.Diagnostics).Collect()
            .Select((diagnostics, _) => diagnostics.SelectMany(items => items).Distinct().ToImmutableEquatableArray());
        context.RegisterSourceOutput(diagnostics.Combine(context.CompilationProvider), (context, input) =>
        {
            if (input.Left.Length == 0)
            {
                return;
            }

            var trees = new Dictionary<string, SyntaxTree?>(StringComparer.Ordinal);
            foreach (var tree in input.Right.SyntaxTrees)
            {
                trees[tree.FilePath] = trees.ContainsKey(tree.FilePath) ? null : tree;
            }

            foreach (var diagnostic in input.Left)
            {
                context.ReportDiagnostic(diagnostic.ToDiagnostic(trees));
            }
        });
    }

    private static ExportOutput CreateOutput(GeneratorAttributeSyntaxContext context, CancellationToken cancellationToken)
    {
        var type = (INamedTypeSymbol)context.TargetSymbol;
        var hintName = GetHintName(type);
        var diagnostics = new List<DiagnosticInfo>();
        var typeLocation = ((TypeDeclarationSyntax)context.TargetNode).Identifier.GetLocation();
        var knownTypes = new KnownTypes(context.SemanticModel.Compilation);

        if (!ValidateType(type, typeLocation, diagnostics))
            return new ExportOutput(hintName, Source: null, diagnostics.ToImmutableEquatableArray());

        var className = type.Name;
        foreach (var argument in context.Attributes[0].NamedArguments)
        {
            if (argument.Key == "Name" && argument.Value.Value is string name)
            {
                if (string.IsNullOrWhiteSpace(name))
                {
                    diagnostics.Add(DiagnosticInfo.Create(InvalidName, typeLocation, type.ToDisplayString()));
                }
                else
                {
                    className = name;
                }
            }
        }

        // The members of the non-exported base classes are members of the class: the nearest exported base class defines its own
        var walkedTypes = new List<INamedTypeSymbol> { type };
        INamedTypeSymbol? exportedBase = null;
        for (var baseType = type.BaseType; baseType is not null && baseType.SpecialType != SpecialType.System_Object; baseType = baseType.BaseType)
        {
            if (knownTypes.HasExportAttribute(baseType))
            {
                exportedBase = baseType;
                break;
            }

            walkedTypes.Add(baseType);
        }

        var model = new TypeModel(type, className, exportedBase);
        if (!type.IsStatic && !type.IsAbstract)
        {
            model.Constructor = SelectConstructor(type, typeLocation, knownTypes, diagnostics);
        }

        cancellationToken.ThrowIfCancellationRequested();
        CollectMembers(model, walkedTypes, knownTypes, diagnostics);

        if (diagnostics.Count > 0)
            return new ExportOutput(hintName, Source: null, diagnostics.ToImmutableEquatableArray());

        var source = Emit(model);
        return new ExportOutput(hintName, source, ImmutableEquatableArray<DiagnosticInfo>.Empty);
    }

    private static bool ValidateType(INamedTypeSymbol type, Location location, List<DiagnosticInfo> diagnostics)
    {
        for (var current = type; current is not null; current = current.ContainingType)
        {
            if (current.IsGenericType)
            {
                diagnostics.Add(DiagnosticInfo.Create(UnsupportedType, location, type.ToDisplayString(), ReferenceEquals(current, type) ? "generic" : "nested in a generic type"));
                return false;
            }

            if (current.IsFileLocal)
            {
                diagnostics.Add(DiagnosticInfo.Create(UnsupportedType, location, type.ToDisplayString(), "file-local"));
                return false;
            }

            if (!IsPartial(current))
            {
                diagnostics.Add(DiagnosticInfo.Create(TypeMustBePartial, location, type.ToDisplayString()));
                return false;
            }
        }

        return true;
    }

    private static bool IsPartial(INamedTypeSymbol type)
    {
        foreach (var reference in type.DeclaringSyntaxReferences)
        {
            if (reference.GetSyntax() is TypeDeclarationSyntax declaration && declaration.Modifiers.Any(SyntaxKind.PartialKeyword))
                return true;
        }

        return false;
    }

    private static ConstructorModel? SelectConstructor(INamedTypeSymbol type, Location typeLocation, KnownTypes knownTypes, List<DiagnosticInfo> diagnostics)
    {
        var constructors = type.InstanceConstructors
            .Where(constructor => constructor.DeclaredAccessibility == Accessibility.Public && !knownTypes.HasIgnoreAttribute(constructor))
            .ToList();
        if (constructors.Count == 0)
            return null;

        var constructor = constructors[0];
        if (constructors.Count > 1)
        {
            var selected = constructors.Where(knownTypes.HasConstructorAttribute).ToList();
            if (selected.Count != 1)
            {
                diagnostics.Add(DiagnosticInfo.Create(AmbiguousConstructor, typeLocation, type.ToDisplayString()));
                return null;
            }

            constructor = selected[0];
        }

        var location = constructor.Locations.FirstOrDefault() ?? typeLocation;
        if (HasRequiredMembers(type) && !constructor.GetAttributes().Any(attribute => attribute.AttributeClass?.ToDisplayString() == "System.Diagnostics.CodeAnalysis.SetsRequiredMembersAttribute"))
        {
            diagnostics.Add(DiagnosticInfo.Create(UnsupportedMember, location, constructor.ToDisplayString(), "the class has required members that the constructor does not set ([SetsRequiredMembers])"));
            return null;
        }

        var parameters = CreateParameters(constructor, location, knownTypes, diagnostics);
        return parameters is null ? null : new ConstructorModel(parameters);
    }

    private static bool HasRequiredMembers(INamedTypeSymbol type)
    {
        for (var current = type; current is not null; current = current.BaseType)
        {
            foreach (var member in current.GetMembers())
            {
                if (member is IPropertySymbol { IsRequired: true } or IFieldSymbol { IsRequired: true })
                    return true;
            }
        }

        return false;
    }

    private static void CollectMembers(TypeModel model, List<INamedTypeSymbol> walkedTypes, KnownTypes knownTypes, List<DiagnosticInfo> diagnostics)
    {
        var instanceNames = new Dictionary<string, string>(StringComparer.Ordinal);
        var staticNames = new Dictionary<string, string>(StringComparer.Ordinal);
        if (!model.Type.IsStatic)
        {
            // The built-in properties of a class
            instanceNames.Add("constructor", "the 'constructor' property of the prototype");
            staticNames.Add("prototype", "the 'prototype' property of the constructor");
        }

        // A member of a derived class hides the members of its base classes that have the same name
        var hiddenNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (var walkedType in walkedTypes)
        {
            var declaredNames = new HashSet<string>(StringComparer.Ordinal);
            foreach (var member in walkedType.GetMembers())
            {
                if (member.DeclaredAccessibility != Accessibility.Public || member.IsImplicitlyDeclared || hiddenNames.Contains(member.Name))
                    continue;

                if (member is not (IMethodSymbol { MethodKind: MethodKind.Ordinary } or IPropertySymbol { IsIndexer: false } or IFieldSymbol))
                    continue;

                if (knownTypes.HasIgnoreAttribute(member))
                {
                    // An ignored member still hides the members of the base classes that have its name
                    if (!member.IsOverride)
                    {
                        declaredNames.Add(member.Name);
                    }

                    continue;
                }

                var hasNameAttribute = knownTypes.TryGetJsName(member, out var jsName);
                if (member.IsOverride)
                {
                    // An override is defined by the base class that declares the member, except the members of System.Object
                    var original = GetOriginalDefinition(member);
                    if (original.ContainingType.SpecialType != SpecialType.System_Object || !hasNameAttribute)
                        continue;
                }

                declaredNames.Add(member.Name);
                var location = member.Locations.FirstOrDefault();
                if (hasNameAttribute && string.IsNullOrWhiteSpace(jsName))
                {
                    diagnostics.Add(DiagnosticInfo.Create(InvalidName, location, member.ToDisplayString()));
                    continue;
                }

                jsName ??= ToCamelCase(member.Name);
                var memberModel = CreateMember(member, jsName, location, knownTypes, diagnostics);
                if (memberModel is null)
                    continue;

                var names = member.IsStatic ? staticNames : instanceNames;
                if (names.TryGetValue(jsName, out var existing))
                {
                    diagnostics.Add(DiagnosticInfo.Create(DuplicateName, location, jsName, member.ToDisplayString(), existing));
                    continue;
                }

                names.Add(jsName, "'" + member.ToDisplayString() + "'");
                model.Members.Add(memberModel);
            }

            hiddenNames.UnionWith(declaredNames);
        }
    }

    private static ISymbol GetOriginalDefinition(ISymbol member)
    {
        var current = member;
        while (true)
        {
            ISymbol? overridden = current switch
            {
                IMethodSymbol method => method.OverriddenMethod,
                IPropertySymbol property => property.OverriddenProperty,
                _ => null,
            };
            if (overridden is null)
                return current;

            current = overridden;
        }
    }

    private static MemberModel? CreateMember(ISymbol member, string jsName, Location? location, KnownTypes knownTypes, List<DiagnosticInfo> diagnostics)
    {
        switch (member)
        {
            case IMethodSymbol method:
            {
                if (method.IsGenericMethod)
                    return ReportUnsupported(member, location, "it is generic", diagnostics);

                if (method.ReturnsByRef || method.ReturnsByRefReadonly)
                    return ReportUnsupported(member, location, "it returns a reference", diagnostics);

                var returnKind = method.ReturnsVoid ? ResultKind.Void : GetResultKind(method.ReturnType, knownTypes, allowTask: true);
                if (returnKind is null)
                    return ReportUnsupported(member, location, $"its return type '{method.ReturnType.ToDisplayString()}' is not supported", diagnostics);

                var parameters = CreateParameters(method, location, knownTypes, diagnostics);
                if (parameters is null)
                    return null;

                return new MemberModel(MemberKind.Method, member, jsName, returnKind.Value, parameters, HasGetter: false, HasSetter: false, SetterType: null, SetterIsRequired: false);
            }

            case IPropertySymbol property:
            {
                if (property.ReturnsByRef || property.ReturnsByRefReadonly)
                    return ReportUnsupported(member, location, "it returns a reference", diagnostics);

                var hasGetter = property.GetMethod is { DeclaredAccessibility: Accessibility.Public };
                var hasSetter = property.SetMethod is { DeclaredAccessibility: Accessibility.Public, IsInitOnly: false };
                return CreateAccessor(member, property.Type, jsName, hasGetter, hasSetter, location, knownTypes, diagnostics);
            }

            case IFieldSymbol field:
                return CreateAccessor(member, field.Type, jsName, hasGetter: true, hasSetter: !field.IsReadOnly && !field.IsConst, location, knownTypes, diagnostics);

            default:
                return null;
        }
    }

    private static MemberModel? CreateAccessor(ISymbol member, ITypeSymbol type, string jsName, bool hasGetter, bool hasSetter, Location? location, KnownTypes knownTypes, List<DiagnosticInfo> diagnostics)
    {
        if (!hasGetter)
            return null; // A write-only property is not exposed

        var resultKind = GetResultKind(type, knownTypes, allowTask: true);
        if (resultKind is null)
            return ReportUnsupported(member, location, $"its type '{type.ToDisplayString()}' is not supported", diagnostics);

        if (hasSetter && (!IsSupportedParameterType(type, knownTypes) || knownTypes.IsRealm(type)))
            return ReportUnsupported(member, location, $"its type '{type.ToDisplayString()}' cannot be converted from a JavaScript value: make it read-only", diagnostics);

        return new MemberModel(MemberKind.Accessor, member, jsName, resultKind.Value, ImmutableEquatableArray<ParameterModel>.Empty, hasGetter, hasSetter, hasSetter ? FormatType(type) : null, hasSetter && IsRequiredReference(type));
    }

    private static MemberModel? ReportUnsupported(ISymbol member, Location? location, string reason, List<DiagnosticInfo> diagnostics)
    {
        diagnostics.Add(DiagnosticInfo.Create(UnsupportedMember, location, member.ToDisplayString(), reason));
        return null;
    }

    private static ImmutableEquatableArray<ParameterModel>? CreateParameters(IMethodSymbol method, Location? location, KnownTypes knownTypes, List<DiagnosticInfo> diagnostics)
    {
        var result = new List<ParameterModel>();
        var argumentIndex = 0;
        foreach (var parameter in method.Parameters)
        {
            string? reason = null;
            if (parameter.RefKind != RefKind.None)
            {
                reason = $"parameter '{parameter.Name}' is a ref, out or in parameter";
            }
            else if (parameter.IsParams)
            {
                reason = $"parameter '{parameter.Name}' is a params parameter";
            }
            else if (!IsSupportedParameterType(parameter.Type, knownTypes))
            {
                reason = $"the type '{parameter.Type.ToDisplayString()}' of parameter '{parameter.Name}' is not supported";
            }

            if (reason is not null)
            {
                diagnostics.Add(DiagnosticInfo.Create(UnsupportedMember, location, method.ToDisplayString(), reason));
                return null;
            }

            if (knownTypes.IsRealm(parameter.Type))
            {
                result.Add(new ParameterModel(Type: null, ArgumentIndex: -1, DefaultValue: null, IsRequired: false));
                continue;
            }

            string? defaultValue = null;
            if (parameter.HasExplicitDefaultValue)
            {
                defaultValue = FormatDefaultValue(parameter.ExplicitDefaultValue, parameter.Type);
                if (defaultValue is null)
                {
                    diagnostics.Add(DiagnosticInfo.Create(UnsupportedMember, location, method.ToDisplayString(), $"the default value of parameter '{parameter.Name}' is not supported"));
                    return null;
                }
            }

            // A non-nullable reference parameter rejects null, even when it has a default value for a missing argument
            result.Add(new ParameterModel(FormatType(parameter.Type), argumentIndex, defaultValue, defaultValue is not "default!" && IsRequiredReference(parameter.Type)));
            argumentIndex++;
        }

        return result.ToImmutableEquatableArray();
    }

    // A reference type that is not nullable (in a nullable-enabled context): undefined and null are a TypeError
    private static bool IsRequiredReference(ITypeSymbol type) => type.IsReferenceType && type.NullableAnnotation == NullableAnnotation.NotAnnotated;

    private static bool IsSupportedParameterType(ITypeSymbol type, KnownTypes knownTypes)
    {
        if (type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } nullable)
        {
            var underlyingType = nullable.TypeArguments[0];
            return IsConvertibleValueType(underlyingType, knownTypes);
        }

        return IsConvertibleValueType(type, knownTypes)
            || type.SpecialType is SpecialType.System_String or SpecialType.System_Object
            || knownTypes.IsJsValue(type)
            || knownTypes.IsJsObject(type)
            || knownTypes.IsRealm(type)
            || knownTypes.IsExported(type);
    }

    // The value types of the conversions of JsEngine.ToDotNet<T>
    private static bool IsConvertibleValueType(ITypeSymbol type, KnownTypes knownTypes)
    {
        return type.SpecialType is SpecialType.System_Boolean or SpecialType.System_Double or SpecialType.System_Single
                or SpecialType.System_Int32 or SpecialType.System_Int64 or SpecialType.System_Int16 or SpecialType.System_Byte
                or SpecialType.System_SByte or SpecialType.System_UInt16 or SpecialType.System_UInt32 or SpecialType.System_UInt64
                or SpecialType.System_Decimal or SpecialType.System_DateTime
            || knownTypes.IsBigInteger(type)
            || knownTypes.IsDateTimeOffset(type);
    }

    private static ResultKind? GetResultKind(ITypeSymbol type, KnownTypes knownTypes, bool allowTask)
    {
        if (allowTask && type is INamedTypeSymbol namedType)
        {
            if (knownTypes.IsTask(namedType))
                return ResultKind.Task;
            if (knownTypes.IsValueTask(namedType))
                return ResultKind.ValueTask;
            if (knownTypes.IsGenericTask(namedType))
                return GetResultKind(namedType.TypeArguments[0], knownTypes, allowTask: false) is null ? null : ResultKind.TaskOfT;
            if (knownTypes.IsGenericValueTask(namedType))
                return GetResultKind(namedType.TypeArguments[0], knownTypes, allowTask: false) is null ? null : ResultKind.ValueTaskOfT;
        }

        return IsConvertibleResult(type, knownTypes, new HashSet<ITypeSymbol>(SymbolEqualityComparer.Default)) ? ResultKind.Value : null;
    }

    // The types of the conversions of JsRealm.FromDotNet
    private static bool IsConvertibleResult(ITypeSymbol type, KnownTypes knownTypes, HashSet<ITypeSymbol> visiting)
    {
        if (type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } nullable)
            return IsConvertibleResult(nullable.TypeArguments[0], knownTypes, visiting);

        if (IsConvertibleValueType(type, knownTypes)
            || type.SpecialType is SpecialType.System_String or SpecialType.System_Object or SpecialType.System_Char
            || knownTypes.IsHalf(type)
            || knownTypes.IsJsValue(type)
            || knownTypes.InheritsFromJsObject(type)
            || knownTypes.IsJsonElement(type)
            || knownTypes.InheritsFromJsonNode(type)
            || knownTypes.IsExported(type))
        {
            return true;
        }

        if (!visiting.Add(type))
            return false;

        try
        {
            if (type is IArrayTypeSymbol array)
                return array.Rank == 1 && IsConvertibleResult(array.ElementType, knownTypes, visiting);

            // A dictionary whose keys are strings is an object
            if (knownTypes.GetStringDictionaryValueType(type) is { } valueType)
                return (valueType.SpecialType == SpecialType.System_Object || knownTypes.ImplementsNonGenericDictionary(type)) && IsConvertibleResult(valueType, knownTypes, visiting);

            // Any other enumerable is an array
            if (knownTypes.GetEnumerableElementType(type) is { } elementType)
                return IsConvertibleResult(elementType, knownTypes, visiting);

            return knownTypes.IsNonGenericEnumerable(type);
        }
        finally
        {
            visiting.Remove(type);
        }
    }

    private static string? FormatDefaultValue(object? value, ITypeSymbol type)
    {
        if (value is null)
            return "default!";

        var typeName = FormatType(type);
        return value switch
        {
            bool boolean => boolean ? "true" : "false",
            string text => SymbolDisplay.FormatLiteral(text, quote: true),
            double number when double.IsNaN(number) => "((" + typeName + ")double.NaN)",
            double number when double.IsPositiveInfinity(number) => "((" + typeName + ")double.PositiveInfinity)",
            double number when double.IsNegativeInfinity(number) => "((" + typeName + ")double.NegativeInfinity)",
            double number => "((" + typeName + ")(" + number.ToString("R", CultureInfo.InvariantCulture) + "D))",
            float number when float.IsNaN(number) => "((" + typeName + ")float.NaN)",
            float number when float.IsPositiveInfinity(number) => "((" + typeName + ")float.PositiveInfinity)",
            float number when float.IsNegativeInfinity(number) => "((" + typeName + ")float.NegativeInfinity)",
            float number => "((" + typeName + ")(" + number.ToString("R", CultureInfo.InvariantCulture) + "F))",
            decimal number => "((" + typeName + ")(" + number.ToString(CultureInfo.InvariantCulture) + "M))",
            ulong number => "((" + typeName + ")(" + number.ToString(CultureInfo.InvariantCulture) + "UL))",
            long or int or short or sbyte or byte or ushort or uint => "((" + typeName + ")(" + Convert.ToString(value, CultureInfo.InvariantCulture) + "L))",
            _ => null,
        };
    }

    private static string ToCamelCase(string name)
    {
        if (name.Length == 0 || !char.IsUpper(name[0]))
            return name;

        var chars = name.ToCharArray();
        for (var i = 0; i < chars.Length; i++)
        {
            if (i == 1 && !char.IsUpper(chars[i]))
                break;

            var hasNext = i + 1 < chars.Length;
            if (i > 0 && hasNext && !char.IsUpper(chars[i + 1]))
                break;

            chars[i] = char.ToLowerInvariant(chars[i]);
        }

        return new string(chars);
    }

    private static string FormatType(ITypeSymbol type) => type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

    private static string GetHintName(INamedTypeSymbol type)
    {
        var name = type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        if (name.StartsWith("global::", StringComparison.Ordinal))
        {
            name = name.Substring("global::".Length);
        }

        var builder = new StringBuilder(name.Length + 16);
        foreach (var c in name)
        {
            builder.Append(char.IsLetterOrDigit(c) || c is '.' or '_' ? c : '_');
        }

        return builder.Append(".JsExport.g.cs").ToString();
    }

    private static string Emit(TypeModel model)
    {
        var type = model.Type;
        var typeName = FormatType(type);
        var writer = new CodeWriter();
        writer.WriteLine("// <auto-generated/>");
        writer.WriteLine("#nullable enable");
        writer.WriteLine("#pragma warning disable CS0612, CS0618 // The members can be obsolete");
        writer.WriteLine();

        if (!type.ContainingNamespace.IsGlobalNamespace)
        {
            writer.WriteLine("namespace " + type.ContainingNamespace.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat.WithGlobalNamespaceStyle(SymbolDisplayGlobalNamespaceStyle.Omitted)));
            writer.OpenBlock();
        }

        var containingTypes = new List<INamedTypeSymbol>();
        for (var containingType = type.ContainingType; containingType is not null; containingType = containingType.ContainingType)
        {
            containingTypes.Insert(0, containingType);
        }

        foreach (var containingType in containingTypes)
        {
            writer.WriteLine("partial " + GetTypeKeyword(containingType) + " @" + containingType.Name);
            writer.OpenBlock();
        }

        var exportedBaseName = model.ExportedBase is null ? null : FormatType(model.ExportedBase);
        if (type.IsStatic)
        {
            writer.WriteLine("static partial class @" + type.Name);
        }
        else
        {
            writer.WriteLine("partial " + GetTypeKeyword(type) + " @" + type.Name + " : " + Runtime + "IJsExportable");
        }

        writer.OpenBlock();
        writer.WriteLine("/// <summary>Gets the JavaScript class of <see cref=\"" + typeName + "\"/>: use it with <see cref=\"" + Runtime + "JsRealm.GetClass(" + Runtime + "JsClass)\"/>.</summary>");
        writer.WriteLine("public static " + (exportedBaseName is null ? string.Empty : "new ") + Runtime + "JsClass JsClass => " + NestedClassName + ".Instance;");
        if (!type.IsStatic)
        {
            writer.WriteLine();
            writer.WriteLine(Runtime + "JsClass " + Runtime + "IJsExportable.JsClass => " + NestedClassName + ".Instance;");
        }

        writer.WriteLine();
        writer.WriteLine("private sealed class " + NestedClassName + " : " + Runtime + "JsClass");
        writer.OpenBlock();
        writer.WriteLine("public static readonly " + NestedClassName + " Instance = new();");
        writer.WriteLine();
        writer.WriteLine("private " + NestedClassName + "()");
        writer.WriteLine("    : base(" + SymbolDisplay.FormatLiteral(model.ClassName, quote: true) + ", typeof(" + typeName + "), " + (exportedBaseName is null ? "null" : exportedBaseName + ".JsClass") + ")");
        writer.OpenBlock();
        writer.CloseBlock();

        if (model.Constructor is { } constructor)
        {
            writer.WriteLine();
            writer.WriteLine("protected override int ConstructorLength => " + GetLength(constructor.Parameters).ToString(CultureInfo.InvariantCulture) + ";");
            writer.WriteLine();
            writer.WriteLine("protected override object? CreateInstance(" + Runtime + "JsRealm realm, global::System.ReadOnlySpan<" + Runtime + "JsValue> arguments)");
            writer.OpenBlock();
            writer.WriteLine("return new " + typeName + "(" + FormatArguments(constructor.Parameters) + ");");
            writer.CloseBlock();
        }

        writer.WriteLine();
        writer.WriteLine("protected override void DefineMembers(" + Runtime + "JsRealm realm, " + Runtime + "JsObject constructor, " + Runtime + "JsObject? prototype)");
        writer.OpenBlock();
        foreach (var member in model.Members)
        {
            EmitMember(writer, model, member);
        }

        writer.CloseBlock();
        writer.CloseBlock();
        writer.CloseBlock();

        foreach (var _ in containingTypes)
        {
            writer.CloseBlock();
        }

        if (!type.ContainingNamespace.IsGlobalNamespace)
        {
            writer.CloseBlock();
        }

        return writer.ToString();
    }

    private static void EmitMember(CodeWriter writer, TypeModel model, MemberModel member)
    {
        var symbol = member.Symbol;
        var target = symbol.IsStatic ? "constructor" : "prototype!";
        // A member of a base class is accessed through the base class: a member of the class that has the same name
        // (hidden, ignored or not public) must not be called instead
        var receiver = symbol.IsStatic
            ? FormatType(symbol.ContainingType)
            : SymbolEqualityComparer.Default.Equals(symbol.ContainingType, model.Type)
                ? "GetThis<" + FormatType(model.Type) + ">(thisValue)"
                : "((" + FormatType(symbol.ContainingType) + ")GetThis<" + FormatType(model.Type) + ">(thisValue))";
        var access = receiver + ".@" + symbol.Name;
        var jsName = SymbolDisplay.FormatLiteral(member.JsName, quote: true);

        if (member.Kind == MemberKind.Method)
        {
            writer.WriteLine("DefineMethod(realm, " + target + ", " + jsName + ", " + GetLength(member.Parameters).ToString(CultureInfo.InvariantCulture) + ", (thisValue, arguments) =>");
            writer.OpenBlock();
            var call = access + "(" + FormatArguments(member.Parameters) + ")";
            if (member.ResultKind == ResultKind.Void)
            {
                writer.WriteLine(call + ";");
                writer.WriteLine("return " + Runtime + "JsValue.Undefined;");
            }
            else
            {
                writer.WriteLine("return " + FormatResult(call, member.ResultKind) + ";");
            }

            writer.CloseBlockWith("});");
            return;
        }

        var getter = "(thisValue, arguments) => " + FormatResult(access, member.ResultKind);
        if (!member.HasSetter)
        {
            writer.WriteLine("DefineAccessor(realm, " + target + ", " + jsName + ", " + getter + ", null);");
            return;
        }

        var value = member.SetterIsRequired
            ? "GetRequiredArgument<" + member.SetterType + ">(realm, arguments, 0)"
            : "GetArgument<" + member.SetterType + ">(realm, arguments, 0)";
        writer.WriteLine("DefineAccessor(realm, " + target + ", " + jsName + ", " + getter + ", (thisValue, arguments) =>");
        writer.OpenBlock();
        writer.WriteLine(access + " = " + value + ";");
        writer.WriteLine("return " + Runtime + "JsValue.Undefined;");
        writer.CloseBlockWith("});");
    }

    private static string FormatResult(string expression, ResultKind kind)
    {
        return kind switch
        {
            ResultKind.Task => "realm.CreatePromise(" + expression + ")",
            ResultKind.ValueTask => "realm.CreatePromise(" + expression + ".AsTask())",
            ResultKind.TaskOfT => "realm.CreatePromise(" + expression + ", value => realm.FromDotNet(value))",
            ResultKind.ValueTaskOfT => "realm.CreatePromise(" + expression + ".AsTask(), value => realm.FromDotNet(value))",
            _ => "realm.FromDotNet(" + expression + ")",
        };
    }

    private static string FormatArguments(ImmutableEquatableArray<ParameterModel> parameters)
    {
        var builder = new StringBuilder();
        foreach (var parameter in parameters)
        {
            if (builder.Length > 0)
            {
                builder.Append(", ");
            }

            if (parameter.Type is null)
            {
                builder.Append("realm");
            }
            else if (parameter.DefaultValue is not null)
            {
                builder.Append(parameter.IsRequired ? "GetRequiredArgument<" : "GetArgument<").Append(parameter.Type).Append(">(realm, arguments, ").Append(parameter.ArgumentIndex.ToString(CultureInfo.InvariantCulture)).Append(", ").Append(parameter.DefaultValue).Append(')');
            }
            else
            {
                builder.Append(parameter.IsRequired ? "GetRequiredArgument<" : "GetArgument<").Append(parameter.Type).Append(">(realm, arguments, ").Append(parameter.ArgumentIndex.ToString(CultureInfo.InvariantCulture)).Append(')');
            }
        }

        return builder.ToString();
    }

    // The length of a function is the number of parameters before the first one that has a default value
    private static int GetLength(ImmutableEquatableArray<ParameterModel> parameters)
    {
        var length = 0;
        foreach (var parameter in parameters)
        {
            if (parameter.Type is null)
                continue;

            if (parameter.DefaultValue is not null)
                break;

            length++;
        }

        return length;
    }

    private static string GetTypeKeyword(INamedTypeSymbol type)
    {
        return type switch
        {
            { IsRecord: true, TypeKind: TypeKind.Struct } => "record struct",
            { IsRecord: true } => "record",
            { TypeKind: TypeKind.Struct } => "struct",
            { TypeKind: TypeKind.Interface } => "interface",
            _ => "class",
        };
    }

    private enum MemberKind
    {
        Method,
        Accessor,
    }

    private enum ResultKind
    {
        Void,
        Value,
        Task,
        ValueTask,
        TaskOfT,
        ValueTaskOfT,
    }

    // The symbols are used while the output is created, in the transform: they are not part of the pipeline
    private sealed class TypeModel(INamedTypeSymbol type, string className, INamedTypeSymbol? exportedBase)
    {
        public INamedTypeSymbol Type { get; } = type;

        public string ClassName { get; } = className;

        public INamedTypeSymbol? ExportedBase { get; } = exportedBase;

        public ConstructorModel? Constructor { get; set; }

        public List<MemberModel> Members { get; } = [];
    }

    private sealed record ConstructorModel(ImmutableEquatableArray<ParameterModel> Parameters);

    private sealed record MemberModel(MemberKind Kind, ISymbol Symbol, string JsName, ResultKind ResultKind, ImmutableEquatableArray<ParameterModel> Parameters, bool HasGetter, bool HasSetter, string? SetterType, bool SetterIsRequired);

    // Type is null for a JsRealm parameter, which receives the realm of the function
    private sealed record ParameterModel(string? Type, int ArgumentIndex, string? DefaultValue, bool IsRequired);

    private sealed record ExportOutput(string HintName, string? Source, ImmutableEquatableArray<DiagnosticInfo> Diagnostics);

    private sealed record LocationInfo(string FilePath, TextSpan TextSpan, LinePositionSpan LineSpan)
    {
        public static LocationInfo? Create(Location? location)
            => location is { SourceTree: { } tree } ? new LocationInfo(tree.FilePath, location.SourceSpan, location.GetLineSpan().Span) : null;

        public Location ToLocation(Dictionary<string, SyntaxTree?> trees)
            => trees.TryGetValue(FilePath, out var tree) && tree is not null ? Location.Create(tree, TextSpan) : Location.Create(FilePath, TextSpan, LineSpan);
    }

    // A diagnostic without references to the compilation
    private sealed record DiagnosticInfo(DiagnosticDescriptor Descriptor, LocationInfo? Location, ImmutableEquatableArray<string> Arguments)
    {
        public static DiagnosticInfo Create(DiagnosticDescriptor descriptor, Location? location, params object?[] arguments)
            => new(descriptor, LocationInfo.Create(location), arguments.Select(argument => argument?.ToString() ?? string.Empty).ToImmutableEquatableArray());

        public Diagnostic ToDiagnostic(Dictionary<string, SyntaxTree?> trees) => Diagnostic.Create(Descriptor, Location?.ToLocation(trees), [.. Arguments]);
    }

    private sealed class KnownTypes(Compilation compilation)
    {
        private readonly INamedTypeSymbol? _exportAttribute = compilation.GetTypeByMetadataName(RuntimeNamespace + ".JsExportAttribute");
        private readonly INamedTypeSymbol? _nameAttribute = compilation.GetTypeByMetadataName(RuntimeNamespace + ".JsNameAttribute");
        private readonly INamedTypeSymbol? _ignoreAttribute = compilation.GetTypeByMetadataName(RuntimeNamespace + ".JsIgnoreAttribute");
        private readonly INamedTypeSymbol? _constructorAttribute = compilation.GetTypeByMetadataName(RuntimeNamespace + ".JsConstructorAttribute");
        private readonly INamedTypeSymbol? _exportable = compilation.GetTypeByMetadataName(RuntimeNamespace + ".IJsExportable");
        private readonly INamedTypeSymbol? _jsValue = compilation.GetTypeByMetadataName(RuntimeNamespace + ".JsValue");
        private readonly INamedTypeSymbol? _jsObject = compilation.GetTypeByMetadataName(RuntimeNamespace + ".JsObject");
        private readonly INamedTypeSymbol? _jsRealm = compilation.GetTypeByMetadataName(RuntimeNamespace + ".JsRealm");
        private readonly INamedTypeSymbol? _bigInteger = compilation.GetTypeByMetadataName("System.Numerics.BigInteger");
        private readonly INamedTypeSymbol? _dateTimeOffset = compilation.GetTypeByMetadataName("System.DateTimeOffset");
        private readonly INamedTypeSymbol? _half = compilation.GetTypeByMetadataName("System.Half");
        private readonly INamedTypeSymbol? _jsonElement = compilation.GetTypeByMetadataName("System.Text.Json.JsonElement");
        private readonly INamedTypeSymbol? _jsonNode = compilation.GetTypeByMetadataName("System.Text.Json.Nodes.JsonNode");
        private readonly INamedTypeSymbol? _task = compilation.GetTypeByMetadataName("System.Threading.Tasks.Task");
        private readonly INamedTypeSymbol? _taskOfT = compilation.GetTypeByMetadataName("System.Threading.Tasks.Task`1");
        private readonly INamedTypeSymbol? _valueTask = compilation.GetTypeByMetadataName("System.Threading.Tasks.ValueTask");
        private readonly INamedTypeSymbol? _valueTaskOfT = compilation.GetTypeByMetadataName("System.Threading.Tasks.ValueTask`1");
        private readonly INamedTypeSymbol? _dictionary = compilation.GetTypeByMetadataName("System.Collections.Generic.IDictionary`2");
        private readonly INamedTypeSymbol? _readOnlyDictionary = compilation.GetTypeByMetadataName("System.Collections.Generic.IReadOnlyDictionary`2");
        private readonly INamedTypeSymbol? _nonGenericDictionary = compilation.GetTypeByMetadataName("System.Collections.IDictionary");
        private readonly INamedTypeSymbol? _nonGenericEnumerable = compilation.GetTypeByMetadataName("System.Collections.IEnumerable");

        private INamedTypeSymbol EnumerableOfT { get; } = compilation.GetSpecialType(SpecialType.System_Collections_Generic_IEnumerable_T);

        public bool HasExportAttribute(ISymbol symbol) => HasAttribute(symbol, _exportAttribute);

        public bool HasIgnoreAttribute(ISymbol symbol) => HasAttribute(symbol, _ignoreAttribute);

        public bool HasConstructorAttribute(ISymbol symbol) => HasAttribute(symbol, _constructorAttribute);

        public bool TryGetJsName(ISymbol symbol, out string? name)
        {
            foreach (var attribute in symbol.GetAttributes())
            {
                if (_nameAttribute is not null && SymbolEqualityComparer.Default.Equals(attribute.AttributeClass, _nameAttribute))
                {
                    name = attribute.ConstructorArguments.Length == 1 ? attribute.ConstructorArguments[0].Value as string : null;
                    name ??= string.Empty;
                    return true;
                }
            }

            name = null;
            return false;
        }

        public bool IsJsValue(ITypeSymbol type) => Is(type, _jsValue);

        public bool IsJsObject(ITypeSymbol type) => Is(type, _jsObject);

        public bool IsRealm(ITypeSymbol type) => Is(type, _jsRealm);

        public bool IsBigInteger(ITypeSymbol type) => Is(type, _bigInteger);

        public bool IsDateTimeOffset(ITypeSymbol type) => Is(type, _dateTimeOffset);

        public bool IsHalf(ITypeSymbol type) => Is(type, _half);

        public bool IsJsonElement(ITypeSymbol type) => Is(type, _jsonElement);

        public bool IsTask(INamedTypeSymbol type) => Is(type, _task);

        public bool IsValueTask(INamedTypeSymbol type) => Is(type, _valueTask);

        public bool IsGenericTask(INamedTypeSymbol type) => Is(type.OriginalDefinition, _taskOfT);

        public bool IsGenericValueTask(INamedTypeSymbol type) => Is(type.OriginalDefinition, _valueTaskOfT);

        public bool InheritsFromJsObject(ITypeSymbol type) => InheritsFrom(type, _jsObject);

        public bool InheritsFromJsonNode(ITypeSymbol type) => InheritsFrom(type, _jsonNode);

        public bool IsNonGenericEnumerable(ITypeSymbol type) => Is(type, _nonGenericEnumerable) || type.AllInterfaces.Any(item => Is(item, _nonGenericEnumerable));

        public bool ImplementsNonGenericDictionary(ITypeSymbol type) => Is(type, _nonGenericDictionary) || type.AllInterfaces.Any(item => Is(item, _nonGenericDictionary));

        // A class marked with [JsExport], or derived from one; or a class of another assembly that implements IJsExportable
        public bool IsExported(ITypeSymbol type)
        {
            if (type.TypeKind != TypeKind.Class)
                return false;

            for (var current = type; current is not null; current = current.BaseType)
            {
                if (HasExportAttribute(current))
                    return true;
            }

            return type.AllInterfaces.Any(item => Is(item, _exportable));
        }

        public ITypeSymbol? GetStringDictionaryValueType(ITypeSymbol type)
        {
            foreach (var item in GetSelfAndInterfaces(type))
            {
                if ((Is(item.OriginalDefinition, _dictionary) || Is(item.OriginalDefinition, _readOnlyDictionary)) && item.TypeArguments[0].SpecialType == SpecialType.System_String)
                    return item.TypeArguments[1];
            }

            return null;
        }

        public ITypeSymbol? GetEnumerableElementType(ITypeSymbol type)
        {
            foreach (var item in GetSelfAndInterfaces(type))
            {
                if (SymbolEqualityComparer.Default.Equals(item.OriginalDefinition, EnumerableOfT))
                    return item.TypeArguments[0];
            }

            return null;
        }

        private static IEnumerable<INamedTypeSymbol> GetSelfAndInterfaces(ITypeSymbol type)
        {
            if (type is INamedTypeSymbol namedType)
                yield return namedType;

            foreach (var item in type.AllInterfaces)
            {
                yield return item;
            }
        }

        private static bool HasAttribute(ISymbol symbol, INamedTypeSymbol? attributeType)
        {
            if (attributeType is null)
                return false;

            foreach (var attribute in symbol.GetAttributes())
            {
                if (SymbolEqualityComparer.Default.Equals(attribute.AttributeClass, attributeType))
                    return true;
            }

            return false;
        }

        private static bool Is(ITypeSymbol type, INamedTypeSymbol? expected) => expected is not null && SymbolEqualityComparer.Default.Equals(type, expected);

        private static bool InheritsFrom(ITypeSymbol type, INamedTypeSymbol? expected)
        {
            for (var current = type; current is not null; current = current.BaseType)
            {
                if (Is(current, expected))
                    return true;
            }

            return false;
        }
    }

    private sealed class CodeWriter
    {
        private readonly StringBuilder _builder = new();
        private int _indentation;

        public void WriteLine()
        {
            _builder.Append('\n');
        }

        public void WriteLine(string text)
        {
            _builder.Append(' ', _indentation * 4).Append(text).Append('\n');
        }

        public void OpenBlock()
        {
            WriteLine("{");
            _indentation++;
        }

        public void CloseBlock() => CloseBlockWith("}");

        public void CloseBlockWith(string text)
        {
            _indentation--;
            WriteLine(text);
        }

        public override string ToString() => _builder.ToString();
    }
}
