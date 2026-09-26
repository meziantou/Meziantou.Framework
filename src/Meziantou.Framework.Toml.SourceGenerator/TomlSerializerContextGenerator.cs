using System;
using System.Collections.Immutable;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Meziantou.Framework.Toml.SourceGeneration;

[Generator]
public sealed class TomlSerializerContextGenerator : IIncrementalGenerator
{
    private static readonly DiagnosticDescriptor ContextMustBePartial = new(
        id: "MFTOML001",
        title: "Toml serializer context must be partial",
        messageFormat: "Type '{0}' derives from Meziantou.Framework.Toml.Serialization.TomlSerializerContext and must be declared partial to support source generation",
        category: "Meziantou.Framework.Toml.SourceGeneration",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor InvalidConverterType = new(
        id: "MFTOML002",
        title: "Invalid converter type",
        messageFormat: "Converter type '{0}' is invalid: {1}",
        category: "Meziantou.Framework.Toml.SourceGeneration",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor UnsupportedMemberType = new(
        id: "MFTOML003",
        title: "Unsupported member type",
        messageFormat: "Type '{0}' contains member '{1}' of unsupported type '{2}'. Add [TomlSerializable(typeof({2}))] to the context or change the member type.",
        category: "Meziantou.Framework.Toml.SourceGeneration",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor UnsupportedDictionaryKeyType = new(
        id: "MFTOML004",
        title: "Unsupported dictionary key type",
        messageFormat: "Type '{0}' contains member '{1}' of dictionary-like type '{2}' with non-string keys. TOML table keys must be strings.",
        category: "Meziantou.Framework.Toml.SourceGeneration",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor InvalidSourceGenerationOption = new(
        id: "MFTOML005",
        title: "Invalid source generation option",
        messageFormat: "Invalid source generation option on context '{0}': {1}",
        category: "Meziantou.Framework.Toml.SourceGeneration",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor InvalidExtensionDataMember = new(
        id: "MFTOML006",
        title: "Invalid extension data member",
        messageFormat: "Type '{0}' extension data member '{1}' is invalid: {2}",
        category: "Meziantou.Framework.Toml.SourceGeneration",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor InvalidPolymorphismConfiguration = new(
        id: "MFTOML007",
        title: "Invalid polymorphism configuration",
        messageFormat: "Type '{0}' polymorphism configuration is invalid: {1}",
        category: "Meziantou.Framework.Toml.SourceGeneration",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor JsonSerializableNotSupported = new(
        id: "MFTOML008",
        title: "JsonSerializable is not supported on TOML contexts",
        messageFormat: "Type '{0}' derives from Meziantou.Framework.Toml.Serialization.TomlSerializerContext and uses [JsonSerializable]. Replace [JsonSerializable(...)] with [TomlSerializable(...)] on the context.",
        category: "Meziantou.Framework.Toml.SourceGeneration",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor InvalidDerivedTypeMapping = new(
        id: "MFTOML009",
        title: "Invalid derived type mapping",
        messageFormat: "Context '{0}' derived type mapping is invalid: {1}",
        category: "Meziantou.Framework.Toml.SourceGeneration",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor MissingPolymorphicConfigurationOnDerivedTypeMappingBase = new(
        id: "MFTOML010",
        title: "Derived type mapping base type has no polymorphic configuration",
        messageFormat: "Context '{0}' registers a derived type mapping for base type '{1}' without [TomlPolymorphic], [TomlDerivedType], [JsonPolymorphic], or [JsonDerivedType]. Serializer options defaults will be used.",
        category: "Meziantou.Framework.Toml.SourceGeneration",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor InvalidAttributeUsage = new(
        id: "MFTOML011",
        title: "Invalid TOML attribute usage",
        messageFormat: "Type '{0}' member '{1}' has invalid TOML attribute usage: {2}",
        category: "Meziantou.Framework.Toml.SourceGeneration",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor UnusedConverterFactory = new(
        id: "MFTOML012",
        title: "Converter factory is not used by generated code",
        messageFormat: "Converter factory '{0}' is not used by the generated code, which resolves converters at build time. Apply [TomlConverter] to the type or member instead.",
        category: "Meziantou.Framework.Toml.SourceGeneration",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    private const string TomlSerializerContextMetadataName = "Meziantou.Framework.Toml.Serialization.TomlSerializerContext";
    private const string TomlSerializableAttributeMetadataName = "Meziantou.Framework.Toml.Serialization.TomlSerializableAttribute";
    private const string TomlDerivedTypeMappingAttributeMetadataName = "Meziantou.Framework.Toml.Serialization.TomlDerivedTypeMappingAttribute";
    private const string JsonSerializableAttributeMetadataName = "System.Text.Json.Serialization.JsonSerializableAttribute";
    private const string JsonSourceGenerationOptionsAttributeMetadataName = "System.Text.Json.Serialization.JsonSourceGenerationOptionsAttribute";
    private const string JsonObjectCreationHandlingAttributeMetadataName = "System.Text.Json.Serialization.JsonObjectCreationHandlingAttribute";
    private const string TomlObjectCreationHandlingAttributeMetadataName = "Meziantou.Framework.Toml.Serialization.TomlObjectCreationHandlingAttribute";
    private const string TomlSourceGenerationOptionsAttributeMetadataName = "Meziantou.Framework.Toml.Serialization.TomlSourceGenerationOptionsAttribute";
    private const string TomlConverterMetadataName = "Meziantou.Framework.Toml.Serialization.TomlConverter";
    private const string TomlConverterFactoryMetadataName = "Meziantou.Framework.Toml.Serialization.TomlConverterFactory";
    private const string TomlConverterAttributeMetadataName = "Meziantou.Framework.Toml.Serialization.TomlConverterAttribute";
    private const string JsonConverterAttributeMetadataName = "System.Text.Json.Serialization.JsonConverterAttribute";
    private const string TomlSingleOrArrayAttributeMetadataName = "Meziantou.Framework.Toml.Serialization.TomlSingleOrArrayAttribute";
    private const string TomlOnSerializingMetadataName = "Meziantou.Framework.Toml.Serialization.ITomlOnSerializing";
    private const string TomlOnSerializedMetadataName = "Meziantou.Framework.Toml.Serialization.ITomlOnSerialized";
    private const string TomlOnDeserializingMetadataName = "Meziantou.Framework.Toml.Serialization.ITomlOnDeserializing";
    private const string TomlOnDeserializedMetadataName = "Meziantou.Framework.Toml.Serialization.ITomlOnDeserialized";
    private const string SetsRequiredMembersAttributeMetadataName = "System.Diagnostics.CodeAnalysis.SetsRequiredMembersAttribute";
    private const string GeneratedCodeTool = "Meziantou.Framework.Toml.SourceGenerator";
    private static readonly string GeneratedCodeVersion = typeof(TomlSerializerContextGenerator).Assembly.GetName().Version?.ToString() ?? "0.0.0.0";

    private static readonly SymbolDisplayFormat FullyQualifiedNullableFormat =
        SymbolDisplayFormat.FullyQualifiedFormat.WithMiscellaneousOptions(
            SymbolDisplayFormat.FullyQualifiedFormat.MiscellaneousOptions | SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier);

    private sealed class ContextModel
    {
        public ContextModel(
            INamedTypeSymbol contextSymbol,
            string namespaceName,
            string typeName,
            ImmutableArray<RootTypeModel> rootTypes,
            ImmutableArray<DerivedTypeMappingModel> derivedTypeMappings,
            SourceGenOptions options,
            bool isValid,
            bool usesJsonSerializable)
        {
            ContextSymbol = contextSymbol;
            NamespaceName = namespaceName;
            TypeName = typeName;
            RootTypes = rootTypes;
            DerivedTypeMappings = derivedTypeMappings;
            Options = options;
            IsValid = isValid;
            UsesJsonSerializable = usesJsonSerializable;
        }

        public INamedTypeSymbol ContextSymbol { get; }
        public string NamespaceName { get; }
        public string TypeName { get; }
        public ImmutableArray<RootTypeModel> RootTypes { get; }
        public ImmutableArray<DerivedTypeMappingModel> DerivedTypeMappings { get; }
        public SourceGenOptions Options { get; }
        public bool IsValid { get; }
        public bool UsesJsonSerializable { get; }
    }

    private sealed class RootTypeModel
    {
        public RootTypeModel(ITypeSymbol type, string? typeInfoPropertyName)
        {
            Type = type;
            TypeInfoPropertyName = typeInfoPropertyName;
        }

        public ITypeSymbol Type { get; }
        public string? TypeInfoPropertyName { get; }
    }

    private sealed class DerivedTypeMappingModel
    {
        public DerivedTypeMappingModel(ITypeSymbol baseType, ITypeSymbol derivedType, string? discriminator, Location? location)
        {
            BaseType = baseType;
            DerivedType = derivedType;
            Discriminator = discriminator;
            Location = location;
        }

        public ITypeSymbol BaseType { get; }
        public ITypeSymbol DerivedType { get; }
        public string? Discriminator { get; }
        public Location? Location { get; }
    }

    private sealed class SourceGenOptions
    {
        public bool? WriteIndented { get; set; }
        public int? IndentSize { get; set; }
        public int? NewLine { get; set; }
        public string? PropertyNamingPolicyExpression { get; set; }
        public string? DictionaryKeyPolicyExpression { get; set; }
        public int? PreferredObjectCreationHandling { get; set; }
        public bool? PropertyNameCaseInsensitive { get; set; }
        public bool? IncludeFields { get; set; }
        public bool? IgnoreReadOnlyFields { get; set; }
        public bool? IgnoreReadOnlyProperties { get; set; }
        public bool? RespectRequiredConstructorParameters { get; set; }
        public bool? RespectNullableAnnotations { get; set; }
        public int? UnmappedMemberHandling { get; set; }
        public int? DefaultIgnoreCondition { get; set; }
        public int? DuplicateKeyHandling { get; set; }
        public int? MaxDepth { get; set; }
        public int? MappingOrder { get; set; }
        public int? DottedKeyHandling { get; set; }
        public int? RootValueHandling { get; set; }
        public string? RootValueKeyName { get; set; }
        public int? InlineTablePolicy { get; set; }
        public int? TableArrayStyle { get; set; }
        public ImmutableArray<ITypeSymbol> ConverterTypes { get; set; } = ImmutableArray<ITypeSymbol>.Empty;
    }

    private const int DefaultPreferredObjectCreationHandling = 0;
    private const bool DefaultPropertyNameCaseInsensitive = false;
    private const int DefaultDefaultIgnoreCondition = 1;
    private const int DefaultDuplicateKeyHandling = 0;
    private const int DefaultMappingOrder = 0;

    private static bool GetEffectivePropertyNameCaseInsensitive(SourceGenOptions options) => options.PropertyNameCaseInsensitive ?? DefaultPropertyNameCaseInsensitive;

    private static int GetEffectiveMappingOrder(SourceGenOptions options) => options.MappingOrder ?? DefaultMappingOrder;

    private static bool ShouldThrowOnDuplicate(SourceGenOptions options) => (options.DuplicateKeyHandling ?? DefaultDuplicateKeyHandling) == 0;

    private static ObjectCreationHandlingKind GetEffectiveObjectCreationHandling(SourceGenOptions options)
    {
        return (options.PreferredObjectCreationHandling ?? DefaultPreferredObjectCreationHandling) switch
        {
            1 => ObjectCreationHandlingKind.Populate,
            _ => ObjectCreationHandlingKind.Replace,
        };
    }

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var candidateContexts = context.SyntaxProvider
            .CreateSyntaxProvider(static (node, _) => node is ClassDeclarationSyntax, static (ctx, _) => TryCreateContextModel(ctx))
            .Where(static model => model is not null)!
            .Collect();

        context.RegisterSourceOutput(context.CompilationProvider.Combine(candidateContexts), static (spc, input) =>
        {
            var compilation = input.Left;
            var models = input.Right;

            // De-dup contexts by metadata name.
            var byMetadataName = new Dictionary<string, ContextModel>(StringComparer.Ordinal);
            foreach (var model in models)
            {
                if (model is null)
                {
                    continue;
                }

                byMetadataName[model.ContextSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)] = model;
            }

            foreach (var model in byMetadataName.Values)
            {
                EmitContext(spc, model);
            }
        });
    }

    private static ContextModel? TryCreateContextModel(GeneratorSyntaxContext syntaxContext)
    {
        if (syntaxContext.Node is not ClassDeclarationSyntax classDeclaration)
        {
            return null;
        }

        if (syntaxContext.SemanticModel.GetDeclaredSymbol(classDeclaration) is not INamedTypeSymbol classSymbol)
        {
            return null;
        }

        if (!DerivesFromTomlSerializerContext(classSymbol))
        {
            return null;
        }

        var roots = ImmutableArray.CreateBuilder<RootTypeModel>();
        var derivedTypeMappings = ImmutableArray.CreateBuilder<DerivedTypeMappingModel>();
        var options = new SourceGenOptions();
        var usesJsonSerializable = false;

        foreach (var attribute in classSymbol.GetAttributes())
        {
            if (IsTomlSerializableAttribute(attribute))
            {
                if (attribute.ConstructorArguments.Length != 1)
                {
                    continue;
                }

                var argument = attribute.ConstructorArguments[0];
                if (argument.Kind != TypedConstantKind.Type || argument.Value is not ITypeSymbol typeSymbol)
                {
                    continue;
                }

                roots.Add(new RootTypeModel(typeSymbol, GetTypeInfoPropertyNameOverride(attribute)));
                continue;
            }

            if (TryCreateDerivedTypeMappingModel(attribute, out var derivedTypeMapping))
            {
                derivedTypeMappings.Add(derivedTypeMapping);
                continue;
            }

            if (IsJsonSerializableAttribute(attribute))
            {
                usesJsonSerializable = true;
                continue;
            }

            if (IsJsonSourceGenerationOptionsAttribute(attribute))
            {
                ApplyJsonSourceGenerationOptionsAttribute(attribute, options);
                continue;
            }

            if (IsTomlSourceGenerationOptionsAttribute(attribute))
            {
                ApplyTomlSourceGenerationOptionsAttribute(attribute, options);
            }
        }

        if (roots.Count == 0 && !usesJsonSerializable)
        {
            return null;
        }

        var isPartial = classDeclaration.Modifiers.Any(static modifier => modifier.IsKind(SyntaxKind.PartialKeyword));
        var containingNamespace = classSymbol.ContainingNamespace;
        var namespaceName = containingNamespace is { IsGlobalNamespace: false } ? containingNamespace.ToDisplayString() : string.Empty;
        var typeName = classSymbol.Name;

        return new ContextModel(
            classSymbol,
            namespaceName,
            typeName,
            roots.ToImmutable(),
            derivedTypeMappings.ToImmutable(),
            options,
            isValid: isPartial,
            usesJsonSerializable: usesJsonSerializable);
    }

    private static void EmitContext(SourceProductionContext context, ContextModel model)
    {
        if (model.UsesJsonSerializable)
        {
            context.ReportDiagnostic(Diagnostic.Create(JsonSerializableNotSupported, model.ContextSymbol.Locations.FirstOrDefault(), model.ContextSymbol.ToDisplayString()));
        }

        if (model.RootTypes.Length == 0)
        {
            return;
        }

        if (!model.IsValid)
        {
            context.ReportDiagnostic(Diagnostic.Create(ContextMustBePartial, model.ContextSymbol.Locations.FirstOrDefault(), model.ContextSymbol.ToDisplayString()));
            return;
        }

        ValidateConverters(context, model);
        ValidateSourceGenerationOptions(context, model);
        if (!ValidateRootAttributes(context, model))
        {
            return;
        }

        var derivedTypeMappings = ValidateDerivedTypeMappings(context, model);
        var expanded = ExpandTypeGraph(context, model, derivedTypeMappings);
        var ordered = expanded
            .OrderBy(static t => t.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat), StringComparer.Ordinal)
            .ToImmutableArray();

        s_typeInfoNames = CreateTypeInfoNames(model, ordered);
        try
        {
            EmitContext(context, model, derivedTypeMappings, ordered);
        }
        finally
        {
            s_typeInfoNames = null;
        }
    }

    private static void EmitContext(SourceProductionContext context, ContextModel model, ImmutableArray<DerivedTypeMappingModel> derivedTypeMappings, ImmutableArray<ITypeSymbol> ordered)
    {
        var builder = new StringBuilder();
        builder.AppendLine("// <auto-generated/>");
        builder.AppendLine("#nullable enable");
        builder.AppendLine("using System;");
        builder.AppendLine("using System.Collections.Generic;");
        builder.AppendLine("using Meziantou.Framework.Toml;");
        builder.AppendLine("using Meziantou.Framework.Toml.Serialization;");
        builder.AppendLine();

        if (!string.IsNullOrEmpty(model.NamespaceName))
        {
            builder.Append("namespace ").Append(model.NamespaceName).AppendLine(";");
            builder.AppendLine();
        }

        AppendGeneratedTypeAttributes(builder, string.Empty);
        builder.Append("partial class ").Append(model.TypeName).AppendLine();
        builder.AppendLine("{");

        builder.Append("    private ").Append(model.TypeName).AppendLine("(TomlSerializerOptions options, bool _generated) : base(options) { }");
        builder.AppendLine();

        builder.Append("    public static ").Append(model.TypeName).AppendLine(" Default { get; } = new(CreateDefaultOptions(), _generated: true);");
        builder.AppendLine();

        if (!model.Options.ConverterTypes.IsDefaultOrEmpty)
        {
            builder.AppendLine("    private static readonly Type[] s_sourceGenerationConverterTypes =");
            builder.AppendLine("    [");
            foreach (var converterType in model.Options.ConverterTypes)
            {
                builder.Append("        typeof(").Append(converterType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)).AppendLine("),");
            }

            builder.AppendLine("    ];");
            builder.AppendLine();
        }

        EmitCreateDefaultOptions(builder, model);

        foreach (var type in ordered)
        {
            EmitTypeInfoProperty(builder, context, model, derivedTypeMappings, type);
        }

        builder.AppendLine();
        builder.AppendLine("    public override TomlTypeInfo? GetTypeInfo(Type type, TomlSerializerOptions options)");
        builder.AppendLine("    {");
        builder.AppendLine("        if (type is null) throw new global::System.ArgumentNullException(nameof(type));");
        builder.AppendLine("        if (options is null) throw new global::System.ArgumentNullException(nameof(options));");
        builder.AppendLine();
        builder.AppendLine("        if (!global::System.Object.ReferenceEquals(options, Options))");
        builder.AppendLine("        {");
        builder.AppendLine("            if (!global::System.Object.ReferenceEquals(options.TypeInfoResolver, this))");
        builder.AppendLine("            {");
        builder.AppendLine("                throw new global::System.InvalidOperationException(");
        builder.AppendLine("                    $\"The provided {nameof(global::Meziantou.Framework.Toml.TomlSerializerOptions)} instance does not match the options associated with the context '{GetType()}'. \" +");
        builder.AppendLine("                    $\"Use overloads that accept a {nameof(global::Meziantou.Framework.Toml.Serialization.TomlSerializerContext)} or a {nameof(global::Meziantou.Framework.Toml.TomlTypeInfo)} directly.\");");
        builder.AppendLine("            }");
        builder.AppendLine();
        builder.Append("            var __runtimeTypeInfo = ResolveRuntimeConverterTypeInfo(options, type, ")
            .Append(model.Options.ConverterTypes.IsDefaultOrEmpty ? "null" : "s_sourceGenerationConverterTypes")
            .AppendLine(");");
        builder.AppendLine("            if (__runtimeTypeInfo is not null) return __runtimeTypeInfo;");
        foreach (var type in ordered)
        {
            builder.Append("        if (type == typeof(")
                .Append(type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat))
                .Append(")) return Create")
                .Append(GetTypeInfoPropertyName(type))
                .AppendLine("(options);");
        }
        builder.AppendLine("            return null;");
        builder.AppendLine("        }");
        builder.AppendLine();
        foreach (var type in ordered)
        {
            builder.Append("        if (type == typeof(")
                .Append(type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat))
                .Append(")) return ")
                .Append(GetTypeInfoPropertyName(type))
                .AppendLine(";");
        }
        builder.AppendLine("        return null;");
        builder.AppendLine("    }");

        foreach (var type in ordered)
        {
            if (!HasStaticOptionsConverter(model.Options, type) && GetDeclaredConverter(type, type) is null && TryGetPocoShape(context, model, type, out var poco))
            {
                EmitPocoTypeInfo(builder, model, type, poco);
            }
        }

        builder.AppendLine("}");

        context.AddSource(GetHintName(model.ContextSymbol), builder.ToString());
    }

    // Two contexts can have the same name in different namespaces or containing types
    private static string GetHintName(INamedTypeSymbol contextSymbol)
    {
        var name = contextSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat).Replace("global::", "", StringComparison.Ordinal);
        var builder = new StringBuilder(name.Length + 26);
        foreach (var c in name)
        {
            builder.Append(char.IsLetterOrDigit(c) || c is '.' or '_' ? c : '_');
        }

        return builder.Append(".TomlSerializerContext.g.cs").ToString();
    }

    private static void AppendGeneratedTypeAttributes(StringBuilder builder, string indent)
    {
        builder.Append(indent)
            .Append("[global::System.CodeDom.Compiler.GeneratedCode(\"")
            .Append(EscapeStringLiteral(GeneratedCodeTool))
            .Append("\", \"")
            .Append(EscapeStringLiteral(GeneratedCodeVersion))
            .AppendLine("\")]");
        builder.Append(indent).AppendLine("[global::System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]");
    }

    private static void EmitCreateDefaultOptions(StringBuilder builder, ContextModel model)
    {
        builder.AppendLine("    private static TomlSerializerOptions CreateDefaultOptions()");
        builder.AppendLine("    {");
        builder.AppendLine("        var options = TomlSerializerOptions.Default;");

        if (!model.Options.ConverterTypes.IsDefaultOrEmpty)
        {
            builder.AppendLine("        var converters = new TomlConverter[]");
            builder.AppendLine("        {");
            foreach (var converterType in model.Options.ConverterTypes)
            {
                builder.Append("            new ").Append(converterType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)).AppendLine("(),");
            }
            builder.AppendLine("        };");
            builder.AppendLine("        options = options with { Converters = converters };");
        }

        if (!string.IsNullOrEmpty(model.Options.PropertyNamingPolicyExpression))
        {
            builder.Append("        options = options with { PropertyNamingPolicy = ").Append(model.Options.PropertyNamingPolicyExpression).AppendLine(" };");
        }

        if (!string.IsNullOrEmpty(model.Options.DictionaryKeyPolicyExpression))
        {
            builder.Append("        options = options with { DictionaryKeyPolicy = ").Append(model.Options.DictionaryKeyPolicyExpression).AppendLine(" };");
        }

        if (model.Options.PreferredObjectCreationHandling is not null && TryGetObjectCreationHandlingExpression(model.Options.PreferredObjectCreationHandling.Value, out var objectCreationHandlingExpression))
        {
            builder.Append("        options = options with { PreferredObjectCreationHandling = ").Append(objectCreationHandlingExpression).AppendLine(" };");
        }

        if (model.Options.PropertyNameCaseInsensitive is not null)
        {
            builder.Append("        options = options with { PropertyNameCaseInsensitive = ").Append(model.Options.PropertyNameCaseInsensitive.Value ? "true" : "false").AppendLine(" };");
        }

        if (model.Options.IncludeFields is not null)
        {
            builder.Append("        options = options with { IncludeFields = ").Append(model.Options.IncludeFields.Value ? "true" : "false").AppendLine(" };");
        }

        if (model.Options.IgnoreReadOnlyFields is not null)
        {
            builder.Append("        options = options with { IgnoreReadOnlyFields = ").Append(model.Options.IgnoreReadOnlyFields.Value ? "true" : "false").AppendLine(" };");
        }

        if (model.Options.IgnoreReadOnlyProperties is not null)
        {
            builder.Append("        options = options with { IgnoreReadOnlyProperties = ").Append(model.Options.IgnoreReadOnlyProperties.Value ? "true" : "false").AppendLine(" };");
        }

        if (model.Options.RespectRequiredConstructorParameters is not null)
        {
            builder.Append("        options = options with { RespectRequiredConstructorParameters = ").Append(model.Options.RespectRequiredConstructorParameters.Value ? "true" : "false").AppendLine(" };");
        }

        if (model.Options.RespectNullableAnnotations is not null)
        {
            builder.Append("        options = options with { RespectNullableAnnotations = ").Append(model.Options.RespectNullableAnnotations.Value ? "true" : "false").AppendLine(" };");
        }

        if (model.Options.UnmappedMemberHandling is not null && TryGetTomlUnmappedMemberHandlingExpression(model.Options.UnmappedMemberHandling.Value, out var unmappedMemberHandlingExpression))
        {
            builder.Append("        options = options with { UnmappedMemberHandling = ").Append(unmappedMemberHandlingExpression).AppendLine(" };");
        }

        if (model.Options.DefaultIgnoreCondition is not null && TryGetTomlIgnoreConditionExpression(model.Options.DefaultIgnoreCondition.Value, out var ignoreConditionExpression))
        {
            builder.Append("        options = options with { DefaultIgnoreCondition = ").Append(ignoreConditionExpression).AppendLine(" };");
        }

        if (model.Options.DuplicateKeyHandling is not null && TryGetTomlDuplicateKeyHandlingExpression(model.Options.DuplicateKeyHandling.Value, out var duplicateKeyHandlingExpression))
        {
            builder.Append("        options = options with { DuplicateKeyHandling = ").Append(duplicateKeyHandlingExpression).AppendLine(" };");
        }

        if (model.Options.MaxDepth is not null)
        {
            builder.Append("        options = options with { MaxDepth = ").Append(model.Options.MaxDepth.Value.ToString(CultureInfo.InvariantCulture)).AppendLine(" };");
        }

        if (model.Options.MappingOrder is not null && TryGetTomlMappingOrderPolicyExpression(model.Options.MappingOrder.Value, out var mappingOrderExpression))
        {
            builder.Append("        options = options with { MappingOrder = ").Append(mappingOrderExpression).AppendLine(" };");
        }

        if (model.Options.DottedKeyHandling is not null && TryGetTomlDottedKeyHandlingExpression(model.Options.DottedKeyHandling.Value, out var dottedKeyHandlingExpression))
        {
            builder.Append("        options = options with { DottedKeyHandling = ").Append(dottedKeyHandlingExpression).AppendLine(" };");
        }

        if (model.Options.WriteIndented is not null)
        {
            builder.Append("        options = options with { WriteIndented = ").Append(model.Options.WriteIndented.Value ? "true" : "false").AppendLine(" };");
        }

        if (model.Options.IndentSize is not null)
        {
            builder.Append("        options = options with { IndentSize = ").Append(model.Options.IndentSize.Value.ToString(CultureInfo.InvariantCulture)).AppendLine(" };");
        }

        if (model.Options.NewLine is not null && TryGetTomlNewLineKindExpression(model.Options.NewLine.Value, out var newLineExpression))
        {
            builder.Append("        options = options with { NewLine = ").Append(newLineExpression).AppendLine(" };");
        }

        if (model.Options.RootValueHandling is not null && TryGetTomlRootValueHandlingExpression(model.Options.RootValueHandling.Value, out var rootValueHandlingExpression))
        {
            builder.Append("        options = options with { RootValueHandling = ").Append(rootValueHandlingExpression).AppendLine(" };");
        }

        if (!string.IsNullOrWhiteSpace(model.Options.RootValueKeyName))
        {
            builder.Append("        options = options with { RootValueKeyName = \"").Append(EscapeStringLiteral(model.Options.RootValueKeyName!)).AppendLine("\" };");
        }

        if (model.Options.InlineTablePolicy is not null && TryGetTomlInlineTablePolicyExpression(model.Options.InlineTablePolicy.Value, out var inlineTableExpression))
        {
            builder.Append("        options = options with { InlineTablePolicy = ").Append(inlineTableExpression).AppendLine(" };");
        }

        if (model.Options.TableArrayStyle is not null && TryGetTomlTableArrayStyleExpression(model.Options.TableArrayStyle.Value, out var tableArrayExpression))
        {
            builder.Append("        options = options with { TableArrayStyle = ").Append(tableArrayExpression).AppendLine(" };");
        }

        builder.AppendLine("        return options;");
        builder.AppendLine("    }");
        builder.AppendLine();
    }

    private static void EmitTypeInfoProperty(
        StringBuilder builder,
        SourceProductionContext context,
        ContextModel model,
        ImmutableArray<DerivedTypeMappingModel> derivedTypeMappings,
        ITypeSymbol type)
    {
        var propertyName = GetTypeInfoPropertyName(type);
        var publicPropertyName = TryGetCustomTypeInfoPropertyName(model, type, out var customPropertyName)
            ? customPropertyName
            : propertyName;
        var propertyAccessibility = string.Equals(publicPropertyName, propertyName, StringComparison.Ordinal) ? "public" : "private";
        var typeName = type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        var usesJsonStringEnumConverter = type.TypeKind == TypeKind.Enum && HasJsonStringEnumConverterAttribute(type);
        ITypeSymbol? staticOptionsConverterType = null;
        var usesStaticOptionsConverter = !usesJsonStringEnumConverter && TryGetStaticOptionsConverterType(model.Options, type, out staticOptionsConverterType);

        builder.Append("    private TomlTypeInfo<").Append(typeName).Append(">? _").Append(propertyName).AppendLine(";");
        builder.Append("    ").Append(propertyAccessibility).Append(" TomlTypeInfo<").Append(typeName).Append("> ").Append(propertyName).AppendLine();
        builder.Append("        => _").Append(propertyName).Append(" ??= Create").Append(propertyName).AppendLine("(Options);");
        builder.AppendLine();

        if (!string.Equals(publicPropertyName, propertyName, StringComparison.Ordinal))
        {
            builder.Append("    public TomlTypeInfo<").Append(typeName).Append("> ").Append(publicPropertyName).Append(" => ").Append(propertyName).AppendLine(";");
            builder.AppendLine();
        }

        builder.Append("    private TomlTypeInfo<").Append(typeName).Append("> Create").Append(propertyName).AppendLine("(TomlSerializerOptions options)");
        builder.AppendLine("    {");

        // Same precedence as the reflection resolver: the converter declared on the type wins
        var declaredConverter = GetDeclaredConverter(type, type);
        if (declaredConverter is { Error: null, IsStringEnum: false })
        {
            builder.Append("        return ").Append(GetDeclaredConverterTypeInfoExpression(declaredConverter, type, "options")).AppendLine(";");
            builder.AppendLine("    }");
            builder.AppendLine();
            return;
        }

        if (usesStaticOptionsConverter)
        {
            builder.Append("        return CreateConverterTypeInfo<")
                .Append(typeName)
                .Append(">(options, new ")
                .Append(staticOptionsConverterType!.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat))
                .AppendLine("());");
        }
        else if (IsBuiltInType(type))
        {
            if (usesJsonStringEnumConverter)
            {
                builder.Append("        return CreateStringEnumTypeInfo<").Append(typeName).AppendLine(">(options);");
            }
            else
            {
                builder.Append("        return GetBuiltInTypeInfo<").Append(typeName).AppendLine(">(options);");
            }
        }
        else if (TryGetNullableUnderlyingType(type, out var nullableUnderlyingType))
        {
            builder.Append("        return CreateSourceGeneratedNullableTypeInfo<")
                .Append(nullableUnderlyingType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat))
                .AppendLine(">(this, options);");
        }
        else if (TryGetArrayElementType(type, out var arrayElementType))
        {
            builder.Append("        return CreateSourceGeneratedArrayTypeInfo<")
                .Append(arrayElementType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat))
                .AppendLine(">(this, options);");
        }
        else if (TryGetSequenceElementType(type, out var enumerableElementType, out var kind))
        {
            if (kind == SequenceKind.List)
            {
                builder.Append("        return CreateSourceGeneratedListTypeInfo<")
                    .Append(enumerableElementType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat))
                    .AppendLine(">(this, options);");
            }
            else if (kind == SequenceKind.ListBackedEnumerable)
            {
                builder.Append("        return CreateSourceGeneratedListBackedEnumerableTypeInfo<")
                    .Append(typeName)
                    .Append(", ")
                    .Append(enumerableElementType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat))
                    .AppendLine(">(this, options);");
            }
            else if (kind == SequenceKind.MutableCollection)
            {
                builder.Append("        return CreateSourceGeneratedMutableCollectionTypeInfo<")
                    .Append(typeName)
                    .Append(", ")
                    .Append(enumerableElementType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat))
                    .AppendLine(">(this, options);");
            }
            else if (kind == SequenceKind.HashSet)
            {
                builder.Append("        return CreateSourceGeneratedHashSetTypeInfo<")
                    .Append(enumerableElementType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat))
                    .AppendLine(">(this, options);");
            }
            else if (kind == SequenceKind.HashSetBackedEnumerable)
            {
                builder.Append("        return CreateSourceGeneratedHashSetBackedEnumerableTypeInfo<")
                    .Append(typeName)
                    .Append(", ")
                    .Append(enumerableElementType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat))
                    .AppendLine(">(this, options);");
            }
            else if (kind == SequenceKind.ImmutableArray)
            {
                builder.Append("        return CreateSourceGeneratedImmutableArrayTypeInfo<")
                    .Append(enumerableElementType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat))
                    .AppendLine(">(this, options);");
            }
            else if (kind == SequenceKind.ImmutableList)
            {
                builder.Append("        return CreateSourceGeneratedImmutableListTypeInfo<")
                    .Append(enumerableElementType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat))
                    .AppendLine(">(this, options);");
            }
            else if (kind == SequenceKind.ImmutableHashSet)
            {
                builder.Append("        return CreateSourceGeneratedImmutableHashSetTypeInfo<")
                    .Append(enumerableElementType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat))
                    .AppendLine(">(this, options);");
            }
            else
            {
                builder.Append("        throw new global::System.InvalidOperationException(\"Unsupported sequence kind.\");");
            }
        }
        else if (TryGetDictionaryValueType(type, out var dictionaryValueType))
        {
            var isConcreteDictionary = type is INamedTypeSymbol namedDictionary && TryGetConcreteDictionaryKeyValueTypes(namedDictionary, out _, out _) &&
                !(namedDictionary.IsGenericType && namedDictionary.ConstructedFrom.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) == "global::System.Collections.Generic.Dictionary<TKey, TValue>");
            builder.Append(isConcreteDictionary ? "        return CreateSourceGeneratedConcreteDictionaryTypeInfo<" : "        return CreateSourceGeneratedDictionaryTypeInfo<")
                .Append(typeName)
                .Append(", ")
                .Append(dictionaryValueType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat))
                .AppendLine(">(this, options);");
        }
        else if (TryGetPolymorphicShape(context, model, derivedTypeMappings, type, out var polymorphic, reportDiagnostics: false))
        {
            builder.Append("        global::Meziantou.Framework.Toml.TomlTypeInfo<").Append(typeName).AppendLine(">? __baseTypeInfo = null;");
            if (TryGetPocoShape(context, model, type, out _))
            {
                builder.Append("        __baseTypeInfo = new __TomlTypeInfo_").Append(propertyName).AppendLine("(this, options);");
            }

            builder.AppendLine("        var __derivedTypeInfoByDiscriminator = new global::System.Collections.Generic.Dictionary<string, global::Meziantou.Framework.Toml.TomlTypeInfo>(global::System.StringComparer.Ordinal)");
            builder.AppendLine("        {");
            foreach (var derived in polymorphic.DerivedTypes)
            {
                if (derived.Discriminator is null) continue; // skip default derived type in dictionary
                builder.Append("            [\"").Append(EscapeStringLiteral(derived.Discriminator)).Append("\"] = Create").Append(GetTypeInfoPropertyName(derived.Type)).AppendLine("(options),");
            }
            builder.AppendLine("        };");

            var discriminatorExpression = polymorphic.DiscriminatorPropertyName is null
                ? "null"
                : "\"" + EscapeStringLiteral(polymorphic.DiscriminatorPropertyName) + "\"";

            var defaultDerivedTypeExpression = polymorphic.DefaultDerivedType is not null
                ? "Create" + GetTypeInfoPropertyName(polymorphic.DefaultDerivedType) + "(options)"
                : "null";

            var unknownHandlingExpression = polymorphic.UnknownDerivedTypeHandlingOverride is { } handlingValue
                ? $"(global::Meziantou.Framework.Toml.TomlUnknownDerivedTypeHandling){handlingValue}"
                : "null";

            builder.Append("        return new global::Meziantou.Framework.Toml.Serialization.TomlPolymorphicTypeInfo<")
                .Append(typeName)
                .Append(">(options, __baseTypeInfo, ")
                .Append(discriminatorExpression)
                .Append(", __derivedTypeInfoByDiscriminator, ")
                .Append(defaultDerivedTypeExpression)
                .Append(", ")
                .Append(unknownHandlingExpression)
                .AppendLine(");");
        }
        else
        {
            builder.Append("        return new __TomlTypeInfo_").Append(propertyName).AppendLine("(this, options);");
        }

        builder.AppendLine("    }");
        builder.AppendLine();
    }

    private static void EmitPocoTypeInfo(StringBuilder builder, ContextModel model, ITypeSymbol type, PocoShape poco)
    {
        var propertyName = GetTypeInfoPropertyName(type);
        var typeName = type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        var readReturnType = type.IsValueType ? typeName : typeName + "?";
        var callsOnSerializing = ImplementsInterface(type, TomlOnSerializingMetadataName);
        var callsOnSerialized = ImplementsInterface(type, TomlOnSerializedMetadataName);
        var callsOnDeserializing = ImplementsInterface(type, TomlOnDeserializingMetadataName);
        var callsOnDeserialized = ImplementsInterface(type, TomlOnDeserializedMetadataName);

        builder.AppendLine();
        AppendGeneratedTypeAttributes(builder, "    ");
        builder.Append("    private sealed class __TomlTypeInfo_").Append(propertyName).Append(" : TomlTypeInfo<").Append(typeName).AppendLine(">");
        builder.AppendLine("    {");
        builder.Append("        private readonly ").Append(model.TypeName).AppendLine(" _context;");
        builder.AppendLine("        private readonly global::System.Collections.Generic.Dictionary<global::System.Type, TomlTypeInfo>? _typeInfoCache;");
        builder.AppendLine();
        builder.Append("        public __TomlTypeInfo_").Append(propertyName).Append('(').Append(model.TypeName).AppendLine(" context, TomlSerializerOptions options) : base(options)");
        builder.AppendLine("        {");
        builder.AppendLine("            _context = context;");
        builder.AppendLine("            if (!global::System.Object.ReferenceEquals(options, context.Options))");
        builder.AppendLine("            {");
        builder.AppendLine("                _typeInfoCache = new global::System.Collections.Generic.Dictionary<global::System.Type, TomlTypeInfo>();");
        foreach (var memberType in GetPocoRuntimeResolvedTypes(poco))
        {
            builder.Append("                InitializeTypeInfo<").Append(memberType.ToDisplayString(FullyQualifiedNullableFormat)).AppendLine(">();");
        }
        builder.AppendLine("            }");
        builder.AppendLine("        }");
        builder.AppendLine();
        builder.AppendLine("        private TomlTypeInfo GetTypeInfo<T>(TomlTypeInfo generatedTypeInfo)");
        builder.AppendLine("        {");
        builder.AppendLine("            var type = typeof(T);");
        builder.AppendLine("            if (_typeInfoCache is null)");
        builder.AppendLine("            {");
        builder.AppendLine("                return generatedTypeInfo;");
        builder.AppendLine("            }");
        builder.AppendLine();
        builder.AppendLine("            if (_typeInfoCache.TryGetValue(type, out var cached))");
        builder.AppendLine("            {");
        builder.AppendLine("                return cached;");
        builder.AppendLine("            }");
        builder.AppendLine();
        builder.AppendLine("            var resolved = _context.GetTypeInfo(type, Options) ?? throw new global::System.InvalidOperationException($\"No generated metadata is available for type '{type.FullName}' in the provided context.\");");
        builder.AppendLine("            _typeInfoCache[type] = resolved;");
        builder.AppendLine("            return resolved;");
        builder.AppendLine("        }");
        builder.AppendLine();
        builder.AppendLine("        private void InitializeTypeInfo<T>()");
        builder.AppendLine("        {");
        builder.AppendLine("            var type = typeof(T);");
        builder.AppendLine("            _typeInfoCache![type] = _context.GetTypeInfo(type, Options) ?? throw new global::System.InvalidOperationException($\"No generated metadata is available for type '{type.FullName}' in the provided context.\");");
        builder.AppendLine("        }");
        builder.AppendLine();
        builder.AppendLine("        public override bool WritesTable => true;");
        builder.AppendLine();

        EmitMemberConverterTypeInfos(builder, poco);
        EmitNonPublicGetterAccessors(builder, poco);
        EmitNonPublicSetterAccessors(builder, poco);

        builder.Append("        public override void Write(TomlWriter writer, ").Append(typeName).AppendLine(" value)");
        builder.AppendLine("        {");
        if (!type.IsValueType)
        {
            builder.AppendLine("            if (value is null) throw new TomlException(\"TOML does not support null values.\");");
        }
        if (callsOnSerializing)
        {
            builder.AppendLine("            ((global::Meziantou.Framework.Toml.Serialization.ITomlOnSerializing)value).OnTomlSerializing();");
        }
        builder.AppendLine("            writer.WriteStartTable();");
        string? usedKeysVariable = null;
        if (poco.ExtensionData is { } extensionData)
        {
            var usedKeysComparer = GetEffectivePropertyNameCaseInsensitive(model.Options)
                ? "global::System.StringComparer.OrdinalIgnoreCase"
                : "global::System.StringComparer.Ordinal";
            usedKeysVariable = "__usedKeys";
            builder.Append("            var __extensionData = value.").Append(extensionData.MemberName).AppendLine(";");
            builder.AppendLine("            global::System.Collections.Generic.HashSet<string>? __usedKeys = null;");
            builder.AppendLine("            if (__extensionData is not null)");
            builder.AppendLine("            {");
            builder.Append("                __usedKeys = new global::System.Collections.Generic.HashSet<string>(").Append(usedKeysComparer).AppendLine(");");
            builder.AppendLine("            }");
        }
        for (var i = 0; i < poco.Members.Length; i++)
        {
            var member = poco.Members[i];
            var writeIgnore = GetEffectiveWriteIgnore(member, model.Options);
            if (writeIgnore == WriteIgnoreKind.WhenWriting)
            {
                continue;
            }

            builder.Append("            var __member").Append(i.ToString(CultureInfo.InvariantCulture)).Append(" = ").Append(GetMemberReadExpression(member, "value")).AppendLine(";");
        }

        var declarationOrder = Enumerable.Range(0, poco.Members.Length).ToArray();
        var alphabeticalOrder = declarationOrder
            .OrderBy(i => poco.Members[i].SerializedName, StringComparer.Ordinal)
            .ToArray();
        var orderThenDeclaration = declarationOrder
            .OrderBy(i => poco.Members[i].Order)
            .ThenBy(i => i)
            .ToArray();
        var orderThenAlphabetical = declarationOrder
            .OrderBy(i => poco.Members[i].Order)
            .ThenBy(i => poco.Members[i].SerializedName, StringComparer.Ordinal)
            .ToArray();

        static void EmitWriteSequence(StringBuilder builder, PocoShape poco, int[] indices, string? usedKeysVariable, SourceGenOptions options)
        {
            for (var sequenceIndex = 0; sequenceIndex < indices.Length; sequenceIndex++)
            {
                var i = indices[sequenceIndex];
                var member = poco.Members[i];
                var memberTypeName = member.Type.ToDisplayString(FullyQualifiedNullableFormat);
                var canBeNull = CanBeNull(member.Type);
                EmitWriteMember(builder, member, i, memberTypeName, canBeNull, usedKeysVariable, options, poco.DottedKeyHandling);
            }
        }

        var mappingOrder = poco.MappingOrder ?? GetEffectiveMappingOrder(model.Options);
        var selectedOrder = mappingOrder switch
        {
            1 => alphabeticalOrder,
            2 => orderThenDeclaration,
            3 => orderThenAlphabetical,
            _ => declarationOrder,
        };
        EmitWriteSequence(builder, poco, selectedOrder, usedKeysVariable, model.Options);
        if (poco.ExtensionData is { } extensionDataWrite)
        {
            var extensionValueTypeInfo = GetTypeInfoPropertyName(extensionDataWrite.ValueType);
            var writeValueArgument = CanBeNull(extensionDataWrite.ValueType) ? "__pair.Value!" : "__pair.Value";
            var dictionaryKeyPolicyExpression = model.Options.DictionaryKeyPolicyExpression;

            builder.AppendLine("            if (__extensionData is not null)");
            builder.AppendLine("            {");
            builder.AppendLine("                foreach (var __pair in __extensionData)");
            builder.AppendLine("                {");
            builder.AppendLine("                    var __key = __pair.Key;");
            if (!string.IsNullOrEmpty(dictionaryKeyPolicyExpression))
            {
                builder.Append("                    __key = ").Append(dictionaryKeyPolicyExpression).AppendLine(".ConvertName(__key);");
            }
            builder.AppendLine("                    if (__usedKeys is not null && __usedKeys.Contains(__key)) throw new TomlException($\"Extension data key '{__key}' conflicts with an existing member key.\");");
            builder.AppendLine("                    writer.WritePropertyName(__key);");
            builder.Append("                    ").Append(GetTypeInfoAccess(extensionDataWrite.ValueType)).Append(".Write(writer, ").Append(writeValueArgument).AppendLine(");");
            builder.AppendLine("                }");
            builder.AppendLine("            }");
        }
        builder.AppendLine("            writer.WriteEndTable();");
        if (callsOnSerialized)
        {
            builder.AppendLine("            ((global::Meziantou.Framework.Toml.Serialization.ITomlOnSerialized)value).OnTomlSerialized();");
        }
        builder.AppendLine("        }");
        builder.AppendLine();

        var constructor = poco.Constructor;
        if (constructor is { ErrorMessage: not null } ctorError)
        {
            builder.Append("        public override ").Append(readReturnType).AppendLine(" Read(TomlReader reader)");
            builder.AppendLine("        {");
            builder.AppendLine("            if (reader.TokenType != TomlTokenType.StartTable) throw reader.CreateException($\"Expected {TomlTokenType.StartTable} token but was {reader.TokenType}.\");");
            builder.Append("            throw new TomlException(\"").Append(EscapeStringLiteral(ctorError.ErrorMessage!)).AppendLine("\");");
            builder.AppendLine("        }");
        }
        else if (poco.RequiresGeneratedObjectInitializer)
        {
            var bufferedConstructor = constructor ?? new PocoConstructor(null, ImmutableArray<PocoConstructorParameter>.Empty, null, setsRequiredMembers: false);
            EmitPocoReadWithConstructor(builder, model, type, poco, bufferedConstructor, readReturnType, callsOnDeserializing, callsOnDeserialized, useObjectInitializerConstruction: true);
            if (!poco.Members.Any(static member => member.IsInitOnly))
            {
                EmitPocoReadIntoExisting(builder, typeName, poco, callsOnDeserializing, callsOnDeserialized, model.Options);
            }
        }
        else if (constructor is { IsValid: true } ctor && !ctor.Parameters.IsDefaultOrEmpty)
        {
            EmitPocoReadWithConstructor(builder, model, type, poco, ctor, readReturnType, callsOnDeserializing, callsOnDeserialized, useObjectInitializerConstruction: false);
            if (!poco.Members.Any(static member => member.IsInitOnly))
            {
                EmitPocoReadIntoExisting(builder, typeName, poco, callsOnDeserializing, callsOnDeserialized, model.Options);
            }
        }
        else
        {
            builder.Append("        public override ").Append(readReturnType).AppendLine(" Read(TomlReader reader)");
            builder.AppendLine("        {");
            builder.AppendLine("            if (reader.TokenType != TomlTokenType.StartTable) throw reader.CreateException($\"Expected {TomlTokenType.StartTable} token but was {reader.TokenType}.\");");
            builder.AppendLine("            var tableStartSpan = reader.CurrentSpan;");
            builder.Append("            var value = new ").Append(typeName).AppendLine("();");
            if (callsOnDeserializing)
            {
                builder.AppendLine("            ((global::Meziantou.Framework.Toml.Serialization.ITomlOnDeserializing)value).OnTomlDeserializing();");
            }
            var hasRequiredMembers = poco.Members.Any(static m => m.IsRequired);
            var throwOnDuplicate = ShouldThrowOnDuplicate(model.Options);
            if (poco.Members.Length <= 64)
            {
                if (poco.Members.Length > 0 && (throwOnDuplicate || hasRequiredMembers))
                {
                    builder.AppendLine("            ulong seenMask = 0;");
                }
                if (hasRequiredMembers)
                {
                    ulong requiredMask = 0;
                    for (var i = 0; i < poco.Members.Length; i++)
                    {
                        if (poco.Members[i].IsRequired)
                        {
                            requiredMask |= 1UL << i;
                        }
                    }

                    builder.Append("            const ulong requiredMask = ").Append(requiredMask.ToString(CultureInfo.InvariantCulture)).AppendLine("UL;");
                }
            }
            else
            {
                if (throwOnDuplicate || hasRequiredMembers)
                {
                    builder.Append("            var seen = new bool[").Append(poco.Members.Length.ToString(CultureInfo.InvariantCulture)).AppendLine("];");
                }
            }
            builder.AppendLine("            reader.Read();");
            builder.AppendLine("            while (reader.TokenType != TomlTokenType.EndTable)");
            builder.AppendLine("            {");
            builder.AppendLine("                if (reader.TokenType != TomlTokenType.PropertyName) throw reader.CreateException($\"Expected {TomlTokenType.PropertyName} token but was {reader.TokenType}.\");");
            if (GetEffectivePropertyNameCaseInsensitive(model.Options))
            {
                builder.AppendLine("                var name = reader.PropertyName!;");
                builder.AppendLine("                reader.Read();");
                EmitMemberDispatch(builder, poco, ignoreCase: true, model.Options);
            }
            else
            {
                EmitMemberDispatch(builder, poco, ignoreCase: false, model.Options);
            }
            builder.AppendLine("            }");
            builder.AppendLine("            var endTableSpan = reader.CurrentSpan;");
            builder.AppendLine("            reader.Read();");
            builder.AppendLine("            ThrowIfDeserializationDiagnostics(reader);");
            if (hasRequiredMembers)
            {
                if (poco.Members.Length <= 64)
                {
                    builder.AppendLine("            if (requiredMask != 0 && (seenMask & requiredMask) != requiredMask)");
                    builder.AppendLine("            {");
                    builder.AppendLine("                var span = endTableSpan ?? tableStartSpan;");
                    for (var i = 0; i < poco.Members.Length; i++)
                    {
                        if (!poco.Members[i].IsRequired)
                        {
                            continue;
                        }

                        var serializedName = EscapeStringLiteral(poco.Members[i].SerializedName);
                        builder.Append("                if ((seenMask & (1UL << ").Append(i.ToString(CultureInfo.InvariantCulture)).AppendLine(")) == 0)");
                        builder.AppendLine("                {");
                        builder.Append("                    throw span is { } locatedSpan ? new TomlException(locatedSpan, $\"Missing required TOML key '")
                            .Append(serializedName)
                            .Append("' when deserializing '{typeof(")
                            .Append(typeName)
                            .Append(").FullName}'.\") : new TomlException($\"Missing required TOML key '")
                            .Append(serializedName)
                            .Append("' when deserializing '{typeof(")
                            .Append(typeName)
                            .Append(").FullName}'.\");");
                        builder.AppendLine("                }");
                    }
                    builder.AppendLine("            }");
                }
                else
                {
                    builder.AppendLine("            if (seen is not null)");
                    builder.AppendLine("            {");
                    builder.AppendLine("                var span = endTableSpan ?? tableStartSpan;");
                    for (var i = 0; i < poco.Members.Length; i++)
                    {
                        if (!poco.Members[i].IsRequired)
                        {
                            continue;
                        }

                        var serializedName = EscapeStringLiteral(poco.Members[i].SerializedName);
                        builder.Append("                if (!seen[").Append(i.ToString(CultureInfo.InvariantCulture)).AppendLine("])");
                        builder.AppendLine("                {");
                        builder.Append("                    throw span is { } locatedSpan ? new TomlException(locatedSpan, $\"Missing required TOML key '")
                            .Append(serializedName)
                            .Append("' when deserializing '{typeof(")
                            .Append(typeName)
                            .Append(").FullName}'.\") : new TomlException($\"Missing required TOML key '")
                            .Append(serializedName)
                            .Append("' when deserializing '{typeof(")
                            .Append(typeName)
                            .Append(").FullName}'.\");");
                        builder.AppendLine("                }");
                    }
                    builder.AppendLine("            }");
                }
            }
            if (callsOnDeserialized)
            {
                builder.AppendLine("            ((global::Meziantou.Framework.Toml.Serialization.ITomlOnDeserialized)value).OnTomlDeserialized();");
            }
            builder.AppendLine("            return value;");
            builder.AppendLine("        }");
            EmitPocoReadIntoExisting(builder, typeName, poco, callsOnDeserializing, callsOnDeserialized, model.Options);
        }

        builder.AppendLine("    }");
    }

    private static void EmitMemberConverterTypeInfos(StringBuilder builder, PocoShape poco)
    {
        foreach (var member in poco.Members)
        {
            if (member.ConverterTypeInfoName is not { } name || member.Converter is not { } converter)
            {
                continue;
            }

            var memberTypeName = member.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            builder.Append("        private TomlTypeInfo<").Append(memberTypeName).Append(">? _").Append(name).AppendLine(";");
            builder.Append("        private TomlTypeInfo<").Append(memberTypeName).Append("> ").Append(name).Append(" => _").Append(name).Append(" ??= ")
                .Append(GetDeclaredConverterTypeInfoExpression(converter, member.Type, "Options")).AppendLine(";");
            builder.AppendLine();
        }
    }

    private static string GetDeclaredConverterTypeInfoExpression(DeclaredConverter converter, ITypeSymbol convertedType, string optionsExpression)
    {
        var typeName = convertedType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        if (converter.IsStringEnum)
        {
            return "CreateStringEnumTypeInfo<" + typeName + ">(" + optionsExpression + ")";
        }

        return "CreateAttributeConverterTypeInfo<" + typeName + ">(" + optionsExpression + ", new " + converter.ConverterType!.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) + "())";
    }

    private static void EmitNonPublicGetterAccessors(StringBuilder builder, PocoShape poco)
    {
        foreach (var member in poco.Members)
        {
            if (member.GetterAccessorName is null)
            {
                continue;
            }

            var declaringTypeName = member.DeclaringType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            var memberTypeName = member.Type.ToDisplayString(FullyQualifiedNullableFormat);
            var bindingFlags = "global::System.Reflection.BindingFlags.Instance | global::System.Reflection.BindingFlags.Public | global::System.Reflection.BindingFlags.NonPublic";
            builder.Append("        private static ").Append(memberTypeName).Append(' ').Append(member.GetterAccessorName).Append('(').Append(declaringTypeName).AppendLine(" __instance)");
            builder.AppendLine("        {");
            if (member.IsField)
            {
                builder.Append("            return (").Append(memberTypeName).Append(")typeof(").Append(declaringTypeName).Append(").GetField(\"").Append(EscapeStringLiteral(member.MemberName)).Append("\", ").Append(bindingFlags).AppendLine(")!.GetValue(__instance)!;");
            }
            else
            {
                builder.Append("            return (").Append(memberTypeName).Append(")typeof(").Append(declaringTypeName).Append(").GetProperty(\"").Append(EscapeStringLiteral(member.MemberName)).Append("\", ").Append(bindingFlags).AppendLine(")!.GetValue(__instance)!;");
            }
            builder.AppendLine("        }");
            builder.AppendLine();
        }
    }

    private static void EmitNonPublicSetterAccessors(StringBuilder builder, PocoShape poco)
    {
        foreach (var member in poco.Members)
        {
            if (member.SetterAccessorName is null)
            {
                continue;
            }

            var declaringTypeName = member.DeclaringType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            var memberTypeName = member.Type.ToDisplayString(FullyQualifiedNullableFormat);
            var bindingFlags = "global::System.Reflection.BindingFlags.Instance | global::System.Reflection.BindingFlags.Public | global::System.Reflection.BindingFlags.NonPublic";
            var memberLookup = "typeof(" + declaringTypeName + ")." + (member.IsField ? "GetField" : "GetProperty") + "(\"" + EscapeStringLiteral(member.MemberName) + "\", " + bindingFlags + ")!";
            var isValueType = member.DeclaringType.IsValueType;
            builder.Append("        private static void ").Append(member.SetterAccessorName).Append('(').Append(isValueType ? "ref " : "").Append(declaringTypeName).Append(" __instance, ").Append(memberTypeName).AppendLine(" __value)");
            builder.AppendLine("        {");
            if (isValueType)
            {
                // Set the member on a boxed copy, then copy it back
                builder.AppendLine("            object __boxed = __instance;");
                builder.Append("            ").Append(memberLookup).AppendLine(".SetValue(__boxed, __value);");
                builder.Append("            __instance = (").Append(declaringTypeName).AppendLine(")__boxed;");
            }
            else
            {
                builder.Append("            ").Append(memberLookup).AppendLine(".SetValue(__instance, __value);");
            }

            builder.AppendLine("        }");
            builder.AppendLine();
        }
    }

    private static string GetSetterAccessorCall(PocoMember member, string instanceExpression, string valueExpression)
        => member.SetterAccessorName + "(" + (member.DeclaringType.IsValueType ? "ref " : "") + instanceExpression + ", " + valueExpression + ")";

    private static string GetMemberReadExpression(PocoMember member, string instanceExpression)
    {
        return member.GetterAccessorName is null
            ? instanceExpression + "." + member.MemberName
            : member.GetterAccessorName + "(" + instanceExpression + ")";
    }

    private static void EmitPocoReadWithConstructor(
        StringBuilder builder,
        ContextModel model,
        ITypeSymbol type,
        PocoShape poco,
        PocoConstructor ctor,
        string readReturnType,
        bool callsOnDeserializing,
        bool callsOnDeserialized,
        bool useObjectInitializerConstruction)
    {
        var typeName = type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        var readNonNullableTypeName = typeName;
        var hasRequiredMembers = poco.Members.Any(static m => m.IsRequired);
        var useSeenMask = poco.Members.Length <= 64;
        var throwOnDuplicate = ShouldThrowOnDuplicate(model.Options);
        var wrapConstructionErrors = !ctor.Parameters.IsDefaultOrEmpty;

        string? GetLinkedParameterExpression(int memberIndex)
        {
            for (var i = 0; i < ctor.Parameters.Length; i++)
            {
                if (ctor.Parameters[i].LinkedMemberIndex == memberIndex)
                {
                    return "__arg" + i.ToString(CultureInfo.InvariantCulture);
                }
            }

            return null;
        }

        builder.Append("        public override ").Append(readReturnType).AppendLine(" Read(TomlReader reader)");
        builder.AppendLine("        {");
        builder.AppendLine("            if (reader.TokenType != TomlTokenType.StartTable) throw reader.CreateException($\"Expected {TomlTokenType.StartTable} token but was {reader.TokenType}.\");");
        builder.AppendLine("            var tableStartSpan = reader.CurrentSpan;");

        // Constructor argument locals.
        for (var i = 0; i < ctor.Parameters.Length; i++)
        {
            var parameter = ctor.Parameters[i];
            var parameterTypeName = parameter.ParameterType.ToDisplayString(FullyQualifiedNullableFormat);
            var defaultLiteral = GetDefaultLiteral(parameter.ParameterType);
            builder.Append("            ").Append(parameterTypeName).Append(" __arg").Append(i.ToString(CultureInfo.InvariantCulture)).Append(" = ").Append(defaultLiteral).AppendLine(";");
            builder.Append("            bool __argSeen").Append(i.ToString(CultureInfo.InvariantCulture)).AppendLine(" = false;");
        }

        // Deferred member assignment locals.
        for (var i = 0; i < poco.Members.Length; i++)
        {
            var member = poco.Members[i];
            if (!member.CanSet)
            {
                continue;
            }

            var memberTypeName = member.Type.ToDisplayString(FullyQualifiedNullableFormat);
            var defaultLiteral = GetDefaultLiteral(member.Type);
            builder.Append("            ").Append(memberTypeName).Append(" __memberValue").Append(i.ToString(CultureInfo.InvariantCulture)).Append(" = ").Append(defaultLiteral).AppendLine(";");
            builder.Append("            bool __memberSeen").Append(i.ToString(CultureInfo.InvariantCulture)).AppendLine(" = false;");
        }

        PocoExtensionData? extensionData = poco.ExtensionData;
        if (extensionData is not null)
        {
            var extensionValueTypeName = extensionData.ValueType.ToDisplayString(FullyQualifiedNullableFormat);
            builder.Append("            global::System.Collections.Generic.Dictionary<string, ").Append(extensionValueTypeName).AppendLine(">? __extensionData = null;");
        }

        // Required/duplicate tracking for members.
        if (useSeenMask)
        {
            if (poco.Members.Length > 0 && (throwOnDuplicate || hasRequiredMembers))
            {
                builder.AppendLine("            ulong seenMask = 0;");
            }

            if (hasRequiredMembers)
            {
                ulong requiredMask = 0;
                for (var i = 0; i < poco.Members.Length; i++)
                {
                    if (poco.Members[i].IsRequired)
                    {
                        requiredMask |= 1UL << i;
                    }
                }

                builder.Append("            const ulong requiredMask = ").Append(requiredMask.ToString(CultureInfo.InvariantCulture)).AppendLine("UL;");
            }
        }
        else
        {
            if (throwOnDuplicate || hasRequiredMembers)
            {
                builder.Append("            var seen = new bool[").Append(poco.Members.Length.ToString(CultureInfo.InvariantCulture)).AppendLine("];");
            }
        }

        builder.AppendLine("            reader.Read();");
        builder.AppendLine("            while (reader.TokenType != TomlTokenType.EndTable)");
        builder.AppendLine("            {");
        builder.AppendLine("                if (reader.TokenType != TomlTokenType.PropertyName) throw reader.CreateException($\"Expected {TomlTokenType.PropertyName} token but was {reader.TokenType}.\");");
        if (GetEffectivePropertyNameCaseInsensitive(model.Options))
        {
            builder.AppendLine("                {");
            builder.AppendLine("                    var name = reader.PropertyName!;");
            builder.AppendLine("                    reader.Read();");

        var comparison = "StringComparison.OrdinalIgnoreCase";
        var wroteAnyCondition = false;

        // Read-ignored members are matched before constructor parameters so they are skipped, not bound.
        for (var i = 0; i < poco.Members.Length; i++)
        {
            var member = poco.Members[i];
            if (!member.IsIgnoredOnRead)
            {
                continue;
            }

            var prefix = wroteAnyCondition ? "else if" : "if";
            wroteAnyCondition = true;
            builder.Append("                    ").Append(prefix).Append("(string.Equals(name, \"").Append(EscapeStringLiteral(member.SerializedName)).Append("\", ").Append(comparison).AppendLine("))");
            builder.AppendLine("                    {");
            builder.AppendLine("                        reader.Skip();");
            builder.AppendLine("                        continue;");
            builder.AppendLine("                    }");
        }

        // Parameters first.
        for (var i = 0; i < ctor.Parameters.Length; i++)
        {
            var parameter = ctor.Parameters[i];
            var prefix = wroteAnyCondition ? "else if" : "if";
            wroteAnyCondition = true;
            builder.Append("                    ").Append(prefix).Append("(string.Equals(name, \"").Append(EscapeStringLiteral(parameter.KeyName)).Append("\", ").Append(comparison).AppendLine("))");
            builder.AppendLine("                    {");
            if (throwOnDuplicate)
            {
                builder.AppendLine("                        if (__argSeen" + i.ToString(CultureInfo.InvariantCulture) + ")");
                builder.AppendLine("                        {");
                if (CanEmitTableHeaderExtension(parameter))
                {
                    EmitRepeatedTableExtensionIntoTarget(builder, parameter.ParameterType, "__arg" + i.ToString(CultureInfo.InvariantCulture), "__arg" + i.ToString(CultureInfo.InvariantCulture), "                            ");
                }
                builder.AppendLine("                            throw reader.CreateException($\"Duplicate key '{name}' was encountered.\");");
                builder.AppendLine("                        }");
            }
            builder.Append("                        __argSeen").Append(i.ToString(CultureInfo.InvariantCulture)).AppendLine(" = true;");
            if (parameter.LinkedMemberIndex >= 0 && parameter.LinkedMemberIndex < poco.Members.Length && poco.Members[parameter.LinkedMemberIndex].HasSingleOrArray && parameter.ConverterTypeInfoName is null)
            {
                var linkedMember = poco.Members[parameter.LinkedMemberIndex];
                EmitSingleOrArrayReadAssignment(builder, parameter.ParameterType, "__arg" + i.ToString(CultureInfo.InvariantCulture), "                        ", "Member '" + EscapeStringLiteral(linkedMember.MemberName) + "' on '" + EscapeStringLiteral(typeName) + "' uses [TomlSingleOrArray]");
            }
            else
            {
                builder.Append("                        __arg").Append(i.ToString(CultureInfo.InvariantCulture)).Append(" = ").Append(GetConstructorParameterReadExpression(parameter, typeName)).AppendLine(";");
            }

            if (parameter.LinkedMemberIndex >= 0 && parameter.LinkedMemberIndex < poco.Members.Length)
            {
                var linkedMember = poco.Members[parameter.LinkedMemberIndex];
                if (linkedMember.IsRequired)
                {
                    if (useSeenMask)
                    {
                        builder.Append("                        seenMask |= 1UL << ").Append(parameter.LinkedMemberIndex.ToString(CultureInfo.InvariantCulture)).AppendLine(";");
                    }
                    else
                    {
                        builder.Append("                        if (seen is not null) seen[").Append(parameter.LinkedMemberIndex.ToString(CultureInfo.InvariantCulture)).AppendLine("] = true;");
                    }
                }

                if (linkedMember.CanSet && SymbolEqualityComparer.Default.Equals(linkedMember.Type, parameter.ParameterType))
                {
                    builder.Append("                        __memberValue").Append(parameter.LinkedMemberIndex.ToString(CultureInfo.InvariantCulture)).Append(" = __arg").Append(i.ToString(CultureInfo.InvariantCulture)).AppendLine(";");
                    builder.Append("                        __memberSeen").Append(parameter.LinkedMemberIndex.ToString(CultureInfo.InvariantCulture)).AppendLine(" = true;");
                }
            }

            builder.AppendLine("                        continue;");
            builder.AppendLine("                    }");
        }

        // Members.
        for (var i = 0; i < poco.Members.Length; i++)
        {
            var member = poco.Members[i];
            if (member.IsIgnoredOnRead)
            {
                continue;
            }

            var prefix = wroteAnyCondition ? "else if" : "if";
            wroteAnyCondition = true;
            builder.Append("                    ").Append(prefix).Append("(string.Equals(name, \"").Append(EscapeStringLiteral(member.SerializedName)).Append("\", ").Append(comparison).AppendLine("))");
            builder.AppendLine("                    {");

            var existingExpression = member.CanSet
                ? "__memberValue" + i.ToString(CultureInfo.InvariantCulture)
                : GetLinkedParameterExpression(i);
            if (useSeenMask)
            {
                if (throwOnDuplicate)
                {
                    builder.Append("                        const ulong bit = 1UL << ").Append(i.ToString(CultureInfo.InvariantCulture)).AppendLine(";");
                    builder.AppendLine("                        if ((seenMask & bit) != 0)");
                    builder.AppendLine("                        {");
                    if (existingExpression is not null && CanEmitTableHeaderExtension(member))
                    {
                        EmitRepeatedTableExtensionIntoTarget(builder, member.Type, existingExpression, existingExpression, "                            ");
                    }
                    builder.AppendLine("                            throw reader.CreateException($\"Duplicate key '{name}' was encountered.\");");
                    builder.AppendLine("                        }");
                    builder.AppendLine("                        seenMask |= bit;");
                }
                else if (member.IsRequired)
                {
                    builder.Append("                        const ulong bit = 1UL << ").Append(i.ToString(CultureInfo.InvariantCulture)).AppendLine(";");
                    builder.AppendLine("                        seenMask |= bit;");
                }
            }
            else
            {
                if (throwOnDuplicate)
                {
                    builder.Append("                        if (seen[").Append(i.ToString(CultureInfo.InvariantCulture)).AppendLine("])");
                    builder.AppendLine("                        {");
                    if (existingExpression is not null && CanEmitTableHeaderExtension(member))
                    {
                        EmitRepeatedTableExtensionIntoTarget(builder, member.Type, existingExpression, existingExpression, "                            ");
                    }
                    builder.AppendLine("                            throw reader.CreateException($\"Duplicate key '{name}' was encountered.\");");
                    builder.AppendLine("                        }");
                    builder.Append("                        seen[").Append(i.ToString(CultureInfo.InvariantCulture)).AppendLine("] = true;");
                }
                else if (member.IsRequired)
                {
                    builder.Append("                        seen[").Append(i.ToString(CultureInfo.InvariantCulture)).AppendLine("] = true;");
                }
            }

            if (!member.CanSet && !member.HasSingleOrArray)
            {
                builder.AppendLine("                        reader.Skip();");
                builder.AppendLine("                        continue;");
                builder.AppendLine("                    }");
                continue;
            }

            if (member.HasSingleOrArray)
            {
                EmitSingleOrArrayReadAssignment(builder, member.Type, "__memberValue" + i.ToString(CultureInfo.InvariantCulture), "                        ", "Member '" + EscapeStringLiteral(member.MemberName) + "' on '" + EscapeStringLiteral(typeName) + "' uses [TomlSingleOrArray]");
            }
            else
            {
                builder.Append("                        __memberValue").Append(i.ToString(CultureInfo.InvariantCulture)).Append(" = ").Append(GetMemberReadExpression(member)).AppendLine(";");
            }
            builder.Append("                        __memberSeen").Append(i.ToString(CultureInfo.InvariantCulture)).AppendLine(" = true;");
            builder.AppendLine("                        continue;");
            builder.AppendLine("                    }");
        }

        if (extensionData is not null)
        {
            builder.AppendLine("                    if (__extensionData is null) __extensionData = new();");
            builder.Append("                    __extensionData[name] = ").Append(GetTypeInfoReadExpression(extensionData.ValueType)).AppendLine(";");
            builder.AppendLine("                    continue;");
            builder.AppendLine("                    reader.Skip();");
        }
        else
        {
            EmitUnmappedMember(builder, "                    ", poco.DisallowUnmappedMembers, typeName);
        }
            builder.AppendLine("                }");
        }
        else
        {
            builder.AppendLine("                {");

            // Case-sensitive: hash dispatch for parameters + members.
            var actionsByHash = new Dictionary<ulong, List<(bool IsParameter, int Index, string KeyName)>>();
        for (var i = 0; i < poco.Members.Length; i++)
        {
            var member = poco.Members[i];
            if (!member.IsIgnoredOnRead)
            {
                continue;
            }

            var hash = ComputePropertyNameHash56(member.SerializedName);
            if (!actionsByHash.TryGetValue(hash, out var bucket))
            {
                bucket = new List<(bool, int, string)>(capacity: 1);
                actionsByHash.Add(hash, bucket);
            }

            bucket.Add((false, i, member.SerializedName));
        }

        for (var i = 0; i < ctor.Parameters.Length; i++)
        {
            var keyName = ctor.Parameters[i].KeyName;
            var hash = ComputePropertyNameHash56(keyName);
            if (!actionsByHash.TryGetValue(hash, out var bucket))
            {
                bucket = new List<(bool, int, string)>(capacity: 1);
                actionsByHash.Add(hash, bucket);
            }

            bucket.Add((true, i, keyName));
        }

        for (var i = 0; i < poco.Members.Length; i++)
        {
            if (poco.Members[i].IsIgnoredOnRead)
            {
                continue;
            }

            var keyName = poco.Members[i].SerializedName;
            var hash = ComputePropertyNameHash56(keyName);
            if (!actionsByHash.TryGetValue(hash, out var bucket))
            {
                bucket = new List<(bool, int, string)>(capacity: 1);
                actionsByHash.Add(hash, bucket);
            }

            bucket.Add((false, i, keyName));
        }

        builder.AppendLine("                    if (reader.TryGetPropertyNameHash(out var hash))");
        builder.AppendLine("                    {");
        builder.AppendLine("                        switch (hash)");
        builder.AppendLine("                        {");

        foreach (var kvp in actionsByHash.OrderBy(static pair => pair.Key))
        {
            builder.Append("                            case 0x").Append(kvp.Key.ToString("X", CultureInfo.InvariantCulture)).AppendLine("UL:");
            builder.AppendLine("                            {");

            foreach (var action in kvp.Value)
            {
                builder.Append("                                if (reader.PropertyNameEquals(\"").Append(EscapeStringLiteral(action.KeyName)).AppendLine("\"))");
                builder.AppendLine("                                {");

                if (action.IsParameter)
                {
                    var parameter = ctor.Parameters[action.Index];
                    if (throwOnDuplicate)
                    {
                        builder.Append("                                    if (__argSeen").Append(action.Index.ToString(CultureInfo.InvariantCulture)).AppendLine(")");
                        builder.AppendLine("                                    {");
                        builder.AppendLine("                                        var __duplicateName = reader.PropertyName!;");
                        builder.AppendLine("                                        reader.Read();");
                        if (CanEmitTableHeaderExtension(parameter))
                        {
                            EmitRepeatedTableExtensionIntoTarget(builder, parameter.ParameterType, "__arg" + action.Index.ToString(CultureInfo.InvariantCulture), "__arg" + action.Index.ToString(CultureInfo.InvariantCulture), "                                        ");
                        }
                        builder.AppendLine("                                        throw reader.CreateException($\"Duplicate key '{__duplicateName}' was encountered.\");");
                        builder.AppendLine("                                    }");
                    }
                    builder.Append("                                    __argSeen").Append(action.Index.ToString(CultureInfo.InvariantCulture)).AppendLine(" = true;");
                    builder.AppendLine("                                    reader.Read();");
                    if (parameter.LinkedMemberIndex >= 0 && parameter.LinkedMemberIndex < poco.Members.Length && poco.Members[parameter.LinkedMemberIndex].HasSingleOrArray && parameter.ConverterTypeInfoName is null)
                    {
                        var linkedMember = poco.Members[parameter.LinkedMemberIndex];
                        EmitSingleOrArrayReadAssignment(builder, parameter.ParameterType, "__arg" + action.Index.ToString(CultureInfo.InvariantCulture), "                                    ", "Member '" + EscapeStringLiteral(linkedMember.MemberName) + "' on '" + EscapeStringLiteral(typeName) + "' uses [TomlSingleOrArray]");
                    }
                    else
                    {
                        builder.Append("                                    __arg").Append(action.Index.ToString(CultureInfo.InvariantCulture)).Append(" = ").Append(GetConstructorParameterReadExpression(parameter, typeName)).AppendLine(";");
                    }

                    if (parameter.LinkedMemberIndex >= 0 && parameter.LinkedMemberIndex < poco.Members.Length)
                    {
                        var linkedMember = poco.Members[parameter.LinkedMemberIndex];
                        if (linkedMember.IsRequired)
                        {
                            if (useSeenMask)
                            {
                                builder.Append("                                    seenMask |= 1UL << ").Append(parameter.LinkedMemberIndex.ToString(CultureInfo.InvariantCulture)).AppendLine(";");
                            }
                            else
                            {
                                builder.Append("                                    if (seen is not null) seen[").Append(parameter.LinkedMemberIndex.ToString(CultureInfo.InvariantCulture)).AppendLine("] = true;");
                            }
                        }

                        if (linkedMember.CanSet && SymbolEqualityComparer.Default.Equals(linkedMember.Type, parameter.ParameterType))
                        {
                            builder.Append("                                    __memberValue").Append(parameter.LinkedMemberIndex.ToString(CultureInfo.InvariantCulture)).Append(" = __arg").Append(action.Index.ToString(CultureInfo.InvariantCulture)).AppendLine(";");
                            builder.Append("                                    __memberSeen").Append(parameter.LinkedMemberIndex.ToString(CultureInfo.InvariantCulture)).AppendLine(" = true;");
                        }
                    }

                    builder.AppendLine("                                    continue;");
                }
                else
                {
                    var member = poco.Members[action.Index];
                    if (member.IsIgnoredOnRead)
                    {
                        builder.AppendLine("                                    reader.Read();");
                        builder.AppendLine("                                    reader.Skip();");
                        builder.AppendLine("                                    continue;");
                        builder.AppendLine("                                }");
                        continue;
                    }

                    var existingExpression = member.CanSet
                        ? "__memberValue" + action.Index.ToString(CultureInfo.InvariantCulture)
                        : GetLinkedParameterExpression(action.Index);
                    if (useSeenMask)
                    {
                        if (throwOnDuplicate)
                        {
                            builder.Append("                                    const ulong bit = 1UL << ").Append(action.Index.ToString(CultureInfo.InvariantCulture)).AppendLine(";");
                            builder.AppendLine("                                    if ((seenMask & bit) != 0)");
                            builder.AppendLine("                                    {");
                            builder.AppendLine("                                        var __duplicateName = reader.PropertyName!;");
                            builder.AppendLine("                                        reader.Read();");
                            if (existingExpression is not null && CanEmitTableHeaderExtension(member))
                            {
                                EmitRepeatedTableExtensionIntoTarget(builder, member.Type, existingExpression, existingExpression, "                                        ");
                            }
                            builder.AppendLine("                                        throw reader.CreateException($\"Duplicate key '{__duplicateName}' was encountered.\");");
                            builder.AppendLine("                                    }");
                            builder.AppendLine("                                    seenMask |= bit;");
                        }
                        else if (member.IsRequired)
                        {
                            builder.Append("                                    const ulong bit = 1UL << ").Append(action.Index.ToString(CultureInfo.InvariantCulture)).AppendLine(";");
                            builder.AppendLine("                                    seenMask |= bit;");
                        }
                    }
                    else
                    {
                        if (throwOnDuplicate)
                        {
                            builder.Append("                                    if (seen[").Append(action.Index.ToString(CultureInfo.InvariantCulture)).AppendLine("])");
                            builder.AppendLine("                                    {");
                            builder.AppendLine("                                        var __duplicateName = reader.PropertyName!;");
                            builder.AppendLine("                                        reader.Read();");
                            if (existingExpression is not null && CanEmitTableHeaderExtension(member))
                            {
                                EmitRepeatedTableExtensionIntoTarget(builder, member.Type, existingExpression, existingExpression, "                                        ");
                            }
                            builder.AppendLine("                                        throw reader.CreateException($\"Duplicate key '{__duplicateName}' was encountered.\");");
                            builder.AppendLine("                                    }");
                            builder.Append("                                    seen[").Append(action.Index.ToString(CultureInfo.InvariantCulture)).AppendLine("] = true;");
                        }
                        else if (member.IsRequired)
                        {
                            builder.Append("                                    seen[").Append(action.Index.ToString(CultureInfo.InvariantCulture)).AppendLine("] = true;");
                        }
                    }

                    builder.AppendLine("                                    reader.Read();");
                    if (!member.CanSet && !member.HasSingleOrArray)
                    {
                        builder.AppendLine("                                    reader.Skip();");
                        builder.AppendLine("                                    continue;");
                    }
                    else
                    {
                        if (member.HasSingleOrArray)
                        {
                            EmitSingleOrArrayReadAssignment(builder, member.Type, "__memberValue" + action.Index.ToString(CultureInfo.InvariantCulture), "                                    ", "Member '" + EscapeStringLiteral(member.MemberName) + "' on '" + EscapeStringLiteral(typeName) + "' uses [TomlSingleOrArray]");
                        }
                        else
                        {
                            builder.Append("                                    __memberValue").Append(action.Index.ToString(CultureInfo.InvariantCulture)).Append(" = ").Append(GetMemberReadExpression(member)).AppendLine(";");
                        }
                        builder.Append("                                    __memberSeen").Append(action.Index.ToString(CultureInfo.InvariantCulture)).AppendLine(" = true;");
                        builder.AppendLine("                                    continue;");
                    }
                }

                builder.AppendLine("                                }");
            }

            builder.AppendLine("                                break;");
            builder.AppendLine("                            }");
        }

        builder.AppendLine("                        }");
        builder.AppendLine("                    }");

        if (extensionData is not null)
        {
            builder.AppendLine("                    {");
            builder.AppendLine("                        var name = reader.PropertyName!;");
            builder.AppendLine("                        reader.Read();");
            builder.AppendLine("                        if (__extensionData is null) __extensionData = new();");
            builder.Append("                        __extensionData[name] = ").Append(GetTypeInfoReadExpression(extensionData.ValueType)).AppendLine(";");
            builder.AppendLine("                        continue;");
            builder.AppendLine("                    }");
        }

        if (extensionData is null && poco.DisallowUnmappedMembers)
        {
            builder.AppendLine("                    var name = reader.PropertyName!;");
        }

        builder.AppendLine("                    reader.Read();");
        EmitUnmappedMember(builder, "                    ", extensionData is null && poco.DisallowUnmappedMembers, typeName);
            builder.AppendLine("                }");
        }
        builder.AppendLine("            }");

        builder.AppendLine("            var endTableSpan = reader.CurrentSpan;");
        builder.AppendLine("            reader.Read();");

        // Validate constructor parameters.
        for (var i = 0; i < ctor.Parameters.Length; i++)
        {
            var parameter = ctor.Parameters[i];
            builder.Append("            if (!__argSeen").Append(i.ToString(CultureInfo.InvariantCulture)).AppendLine(")");
            builder.AppendLine("            {");
            if (parameter.HasDefaultValue && parameter.DefaultValueExpression is not null)
            {
                builder.Append("                __arg").Append(i.ToString(CultureInfo.InvariantCulture)).Append(" = ").Append(parameter.DefaultValueExpression).AppendLine(";");
                builder.AppendLine("            }");
            }
            else if (model.Options.RespectRequiredConstructorParameters == false)
            {
                builder.Append("                __arg").Append(i.ToString(CultureInfo.InvariantCulture)).AppendLine(" = default!;");
                builder.AppendLine("            }");
            }
            else
            {
                var keyName = EscapeStringLiteral(parameter.KeyName);
                builder.AppendLine("                var span = endTableSpan ?? tableStartSpan;");
                builder.Append("                throw span is { } locatedSpan ? new TomlException(locatedSpan, $\"Missing required constructor parameter '")
                    .Append(keyName)
                    .Append("' when deserializing '{typeof(")
                    .Append(typeName)
                    .Append(").FullName}'.\") : new TomlException($\"Missing required constructor parameter '")
                    .Append(keyName)
                    .Append("' when deserializing '{typeof(")
                    .Append(typeName)
                    .Append(").FullName}'.\");");
                builder.AppendLine();
                builder.AppendLine("            }");
            }
        }

        // Validate required members.
        if (hasRequiredMembers)
        {
            if (useSeenMask)
            {
                builder.AppendLine("            if (requiredMask != 0 && (seenMask & requiredMask) != requiredMask)");
                builder.AppendLine("            {");
                builder.AppendLine("                var span = endTableSpan ?? tableStartSpan;");
                for (var i = 0; i < poco.Members.Length; i++)
                {
                    if (!poco.Members[i].IsRequired)
                    {
                        continue;
                    }

                    var serializedName = EscapeStringLiteral(poco.Members[i].SerializedName);
                    builder.Append("                if ((seenMask & (1UL << ").Append(i.ToString(CultureInfo.InvariantCulture)).AppendLine(")) == 0)");
                    builder.AppendLine("                {");
                    builder.Append("                    throw span is { } locatedSpan ? new TomlException(locatedSpan, $\"Missing required TOML key '")
                        .Append(serializedName)
                        .Append("' when deserializing '{typeof(")
                        .Append(typeName)
                        .Append(").FullName}'.\") : new TomlException($\"Missing required TOML key '")
                        .Append(serializedName)
                        .Append("' when deserializing '{typeof(")
                        .Append(typeName)
                        .Append(").FullName}'.\");");
                    builder.AppendLine("                }");
                }
                builder.AppendLine("            }");
            }
            else
            {
                builder.AppendLine("            if (seen is not null)");
                builder.AppendLine("            {");
                builder.AppendLine("                var span = endTableSpan ?? tableStartSpan;");
                for (var i = 0; i < poco.Members.Length; i++)
                {
                    if (!poco.Members[i].IsRequired)
                    {
                        continue;
                    }

                    var serializedName = EscapeStringLiteral(poco.Members[i].SerializedName);
                    builder.Append("                if (!seen[").Append(i.ToString(CultureInfo.InvariantCulture)).AppendLine("])");
                    builder.AppendLine("                {");
                    builder.Append("                    throw span is { } locatedSpan ? new TomlException(locatedSpan, $\"Missing required TOML key '")
                        .Append(serializedName)
                        .Append("' when deserializing '{typeof(")
                        .Append(typeName)
                        .Append(").FullName}'.\") : new TomlException($\"Missing required TOML key '")
                        .Append(serializedName)
                        .Append("' when deserializing '{typeof(")
                        .Append(typeName)
                        .Append(").FullName}'.\");");
                    builder.AppendLine("                }");
                }
                builder.AppendLine("            }");
            }
        }

        // Construct instance.
        if (useObjectInitializerConstruction)
        {
            var templateInitializerAssignments = ImmutableArray.CreateBuilder<string>();
            var finalInitializerAssignments = ImmutableArray.CreateBuilder<string>();
            var needsTemplate = false;

            for (var i = 0; i < poco.Members.Length; i++)
            {
                var member = poco.Members[i];
                if (!member.CanSet)
                {
                    if (member.HasSingleOrArray)
                    {
                        var addCollectionExpression = GetSingleOrArrayAddCollectionExpression(member.Type, "__existing", "__memberValue" + i.ToString(CultureInfo.InvariantCulture) + "!");
                        builder.Append("            if (__memberSeen").Append(i.ToString(CultureInfo.InvariantCulture)).AppendLine(")");
                        builder.AppendLine("            {");
                        builder.Append("                var __existing = value.").Append(member.MemberName).AppendLine(";");
                        builder.AppendLine("                if (__existing is null)");
                        builder.AppendLine("                {");
                        builder.Append("                    throw new TomlException($\"Member '").Append(EscapeStringLiteral(member.MemberName))
                            .Append("' on '{value.GetType().FullName}' uses [TomlSingleOrArray] but the existing collection is null or cannot be populated.\");")
                            .AppendLine();
                        builder.AppendLine("                }");
                        if (addCollectionExpression is null)
                        {
                            builder.Append("                throw new TomlException($\"Member '").Append(EscapeStringLiteral(member.MemberName))
                                .Append("' on '{value.GetType().FullName}' uses [TomlSingleOrArray] but '")
                                .Append(EscapeStringLiteral(member.Type.ToDisplayString(FullyQualifiedNullableFormat)))
                                .AppendLine("' doesn't support populating the existing collection.\");");
                        }
                        else
                        {
                            builder.Append("                _ = ").Append(addCollectionExpression).AppendLine(";");
                        }
                        builder.AppendLine("            }");
                    }

                    continue;
                }

                // A member set by an accessor cannot be in an object initializer; it is set after the construction
                if (member.SetterAccessorName is not null)
                {
                    continue;
                }

                needsTemplate = true;
                finalInitializerAssignments.Add(member.MemberName + " = __memberValue" + i.ToString(CultureInfo.InvariantCulture));

                if (!member.IsCompilerRequired)
                {
                    continue;
                }

                var templateValueExpression = "__memberValue" + i.ToString(CultureInfo.InvariantCulture);
                if (GetLinkedParameterExpression(i) is { } linkedParameterExpression)
                {
                    templateValueExpression = "__memberSeen" + i.ToString(CultureInfo.InvariantCulture) + " ? __memberValue" + i.ToString(CultureInfo.InvariantCulture) + " : " + linkedParameterExpression;
                }

                templateInitializerAssignments.Add(member.MemberName + " = " + templateValueExpression);
            }

            if (extensionData is { CanSet: true })
            {
                needsTemplate = true;
                finalInitializerAssignments.Add(extensionData.MemberName + " = __extensionDataValue");
                if (extensionData.IsCompilerRequired)
                {
                    templateInitializerAssignments.Add(extensionData.MemberName + " = " + extensionData.CreateExpression);
                }
            }

            if (needsTemplate)
            {
                builder.Append("            ").Append(readNonNullableTypeName).AppendLine(" __template;");
                EmitPocoConstructionAssignment(builder, "__template", typeName, ctor.Parameters, templateInitializerAssignments.ToImmutable(), wrapConstructionErrors);

                for (var i = 0; i < poco.Members.Length; i++)
                {
                    var member = poco.Members[i];
                    if (!member.CanSet || member.SetterAccessorName is not null)
                    {
                        continue;
                    }

                    builder.Append("            if (!__memberSeen").Append(i.ToString(CultureInfo.InvariantCulture)).AppendLine(")");
                    builder.AppendLine("            {");
                    builder.Append("                __memberValue").Append(i.ToString(CultureInfo.InvariantCulture)).Append(" = __template.").Append(member.MemberName).AppendLine(";");
                    builder.AppendLine("            }");
                }

                if (extensionData is { CanSet: true })
                {
                    builder.Append("            var __extensionDataValue = __template.").Append(extensionData.MemberName).AppendLine(";");
                    builder.AppendLine("            if (__extensionData is not null && __extensionData.Count != 0)");
                    builder.AppendLine("            {");
                    builder.AppendLine("                if (__extensionDataValue is null)");
                    builder.AppendLine("                {");
                    builder.Append("                    __extensionDataValue = ").Append(extensionData.CreateExpression).AppendLine(";");
                    builder.AppendLine("                }");
                    builder.AppendLine("                foreach (var __pair in __extensionData)");
                    builder.AppendLine("                {");
                    builder.AppendLine("                    __extensionDataValue[__pair.Key] = __pair.Value;");
                    builder.AppendLine("                }");
                    builder.AppendLine("            }");
                }
            }

            builder.Append("            ").Append(readNonNullableTypeName).AppendLine(" value;");
            EmitPocoConstructionAssignment(builder, "value", typeName, ctor.Parameters, finalInitializerAssignments.ToImmutable(), wrapConstructionErrors);

            for (var i = 0; i < poco.Members.Length; i++)
            {
                var member = poco.Members[i];
                if (member.SetterAccessorName is not null)
                {
                    builder.Append("            if (__memberSeen").Append(i.ToString(CultureInfo.InvariantCulture)).Append(") ")
                        .Append(GetSetterAccessorCall(member, "value", "__memberValue" + i.ToString(CultureInfo.InvariantCulture))).AppendLine(";");
                }
            }

            if (callsOnDeserializing)
            {
                builder.AppendLine("            ((global::Meziantou.Framework.Toml.Serialization.ITomlOnDeserializing)value).OnTomlDeserializing();");
            }

            if (extensionData is { CanSet: false })
            {
                var nullInitializationMessage = EscapeStringLiteral($"Extension data member '{extensionData.MemberName}' is null and cannot be initialized.");
                builder.AppendLine("            if (__extensionData is not null && __extensionData.Count != 0)");
                builder.AppendLine("            {");
                builder.Append("                var __target = value.").Append(extensionData.MemberName).AppendLine(";");
                builder.AppendLine("                if (__target is null)");
                builder.AppendLine("                {");
                builder.Append("                    throw new TomlException(\"").Append(nullInitializationMessage).AppendLine("\");");
                builder.AppendLine("                }");
                builder.AppendLine("                foreach (var __pair in __extensionData)");
                builder.AppendLine("                {");
                builder.AppendLine("                    __target[__pair.Key] = __pair.Value;");
                builder.AppendLine("                }");
                builder.AppendLine("            }");
            }
        }
        else
        {
            builder.Append("            ").Append(readNonNullableTypeName).AppendLine(" value;");
            EmitPocoConstructionAssignment(builder, "value", typeName, ctor.Parameters, ImmutableArray<string>.Empty, wrapConstructionErrors);

            if (callsOnDeserializing)
            {
                builder.AppendLine("            ((global::Meziantou.Framework.Toml.Serialization.ITomlOnDeserializing)value).OnTomlDeserializing();");
            }

            for (var i = 0; i < poco.Members.Length; i++)
            {
                var member = poco.Members[i];
                if (!member.CanSet)
                {
                    continue;
                }

                builder.Append("            if (__memberSeen").Append(i.ToString(CultureInfo.InvariantCulture)).AppendLine(")");
                builder.AppendLine("            {");
                if (member.SetterAccessorName is not null)
                {
                    builder.Append("                ").Append(GetSetterAccessorCall(member, "value", "__memberValue" + i.ToString(CultureInfo.InvariantCulture))).AppendLine(";");
                }
                else
                {
                    builder.Append("                value.").Append(member.MemberName).Append(" = __memberValue").Append(i.ToString(CultureInfo.InvariantCulture)).AppendLine(";");
                }

                builder.AppendLine("            }");
            }

            if (extensionData is not null)
            {
                builder.AppendLine("            if (__extensionData is not null && __extensionData.Count != 0)");
                builder.AppendLine("            {");
                builder.Append("                var __target = value.").Append(extensionData.MemberName).AppendLine(";");
                builder.AppendLine("                if (__target is null)");
                builder.AppendLine("                {");
                builder.Append("                    __target = ").Append(extensionData.CreateExpression).AppendLine(";");
                builder.Append("                    value.").Append(extensionData.MemberName).AppendLine(" = __target;");
                builder.AppendLine("                }");
                builder.AppendLine("                foreach (var __pair in __extensionData)");
                builder.AppendLine("                {");
                builder.AppendLine("                    __target[__pair.Key] = __pair.Value;");
                builder.AppendLine("                }");
                builder.AppendLine("            }");
            }
        }

        if (callsOnDeserialized)
        {
            builder.AppendLine("            ((global::Meziantou.Framework.Toml.Serialization.ITomlOnDeserialized)value).OnTomlDeserialized();");
        }

        builder.AppendLine("            return value;");
        builder.AppendLine("        }");
    }

    private static void EmitPocoReadIntoExisting(
        StringBuilder builder,
        string typeName,
        PocoShape poco,
        bool callsOnDeserializing,
        bool callsOnDeserialized,
        SourceGenOptions options)
    {
        builder.AppendLine();
        builder.AppendLine("        public override object? ReadInto(TomlReader reader, object? existingValue)");
        builder.AppendLine("        {");
        builder.Append("            if (existingValue is not ").Append(typeName).AppendLine(" value)");
        builder.AppendLine("            {");
        builder.AppendLine("                return Read(reader);");
        builder.AppendLine("            }");
        builder.AppendLine("            if (reader.TokenType != TomlTokenType.StartTable) throw reader.CreateException($\"Expected {TomlTokenType.StartTable} token but was {reader.TokenType}.\");");
        builder.AppendLine("            var tableStartSpan = reader.CurrentSpan;");
        if (callsOnDeserializing)
        {
            builder.AppendLine("            ((global::Meziantou.Framework.Toml.Serialization.ITomlOnDeserializing)value).OnTomlDeserializing();");
        }

        var hasRequiredMembers = poco.Members.Any(static m => m.IsRequired);
        var throwOnDuplicate = ShouldThrowOnDuplicate(options);
        if (poco.Members.Length <= 64)
        {
            if (poco.Members.Length > 0 && (throwOnDuplicate || hasRequiredMembers))
            {
                builder.AppendLine("            ulong seenMask = 0;");
            }

            if (hasRequiredMembers)
            {
                ulong requiredMask = 0;
                for (var i = 0; i < poco.Members.Length; i++)
                {
                    if (poco.Members[i].IsRequired)
                    {
                        requiredMask |= 1UL << i;
                    }
                }

                builder.Append("            const ulong requiredMask = ").Append(requiredMask.ToString(CultureInfo.InvariantCulture)).AppendLine("UL;");
            }
        }
        else
        {
            if (throwOnDuplicate || hasRequiredMembers)
            {
                builder.Append("            var seen = new bool[").Append(poco.Members.Length.ToString(CultureInfo.InvariantCulture)).AppendLine("];");
            }
        }

        builder.AppendLine("            reader.Read();");
        builder.AppendLine("            while (reader.TokenType != TomlTokenType.EndTable)");
        builder.AppendLine("            {");
        builder.AppendLine("                if (reader.TokenType != TomlTokenType.PropertyName) throw reader.CreateException($\"Expected {TomlTokenType.PropertyName} token but was {reader.TokenType}.\");");
        if (GetEffectivePropertyNameCaseInsensitive(options))
        {
            builder.AppendLine("                var name = reader.PropertyName!;");
            builder.AppendLine("                reader.Read();");
            EmitMemberDispatch(builder, poco, ignoreCase: true, options);
        }
        else
        {
            EmitMemberDispatch(builder, poco, ignoreCase: false, options);
        }
        builder.AppendLine("            }");
        builder.AppendLine("            var endTableSpan = reader.CurrentSpan;");
        builder.AppendLine("            reader.Read();");

        if (hasRequiredMembers)
        {
            if (poco.Members.Length <= 64)
            {
                builder.AppendLine("            if (requiredMask != 0 && (seenMask & requiredMask) != requiredMask)");
                builder.AppendLine("            {");
                builder.AppendLine("                var span = endTableSpan ?? tableStartSpan;");
                for (var i = 0; i < poco.Members.Length; i++)
                {
                    if (!poco.Members[i].IsRequired)
                    {
                        continue;
                    }

                    var serializedName = EscapeStringLiteral(poco.Members[i].SerializedName);
                    builder.Append("                if ((seenMask & (1UL << ").Append(i.ToString(CultureInfo.InvariantCulture)).AppendLine(")) == 0)");
                    builder.AppendLine("                {");
                    builder.Append("                    throw span is { } locatedSpan ? new TomlException(locatedSpan, $\"Missing required TOML key '")
                        .Append(serializedName)
                        .Append("' when deserializing '{typeof(")
                        .Append(typeName)
                        .Append(").FullName}'.\") : new TomlException($\"Missing required TOML key '")
                        .Append(serializedName)
                        .Append("' when deserializing '{typeof(")
                        .Append(typeName)
                        .Append(").FullName}'.\");");
                    builder.AppendLine("                }");
                }
                builder.AppendLine("            }");
            }
            else
            {
                builder.AppendLine("            if (seen is not null)");
                builder.AppendLine("            {");
                builder.AppendLine("                var span = endTableSpan ?? tableStartSpan;");
                for (var i = 0; i < poco.Members.Length; i++)
                {
                    if (!poco.Members[i].IsRequired)
                    {
                        continue;
                    }

                    var serializedName = EscapeStringLiteral(poco.Members[i].SerializedName);
                    builder.Append("                if (!seen[").Append(i.ToString(CultureInfo.InvariantCulture)).AppendLine("])");
                    builder.AppendLine("                {");
                    builder.Append("                    throw span is { } locatedSpan ? new TomlException(locatedSpan, $\"Missing required TOML key '")
                        .Append(serializedName)
                        .Append("' when deserializing '{typeof(")
                        .Append(typeName)
                        .Append(").FullName}'.\") : new TomlException($\"Missing required TOML key '")
                        .Append(serializedName)
                        .Append("' when deserializing '{typeof(")
                        .Append(typeName)
                        .Append(").FullName}'.\");");
                    builder.AppendLine("                }");
                }
                builder.AppendLine("            }");
            }
        }

        if (callsOnDeserialized)
        {
            builder.AppendLine("            ((global::Meziantou.Framework.Toml.Serialization.ITomlOnDeserialized)value).OnTomlDeserialized();");
        }

        builder.AppendLine("            return value;");
        builder.AppendLine("        }");
    }

    private static void EmitPocoConstructionAssignment(
        StringBuilder builder,
        string targetName,
        string typeName,
        ImmutableArray<PocoConstructorParameter> parameters,
        ImmutableArray<string> initializerAssignments,
        bool wrapConstructionErrors)
    {
        void EmitAssignmentBody(string indent)
        {
            builder.Append(indent).Append(targetName).Append(" = new ").Append(typeName).Append('(');
            for (var i = 0; i < parameters.Length; i++)
            {
                if (i != 0)
                {
                    builder.Append(", ");
                }

                builder.Append("__arg").Append(i.ToString(CultureInfo.InvariantCulture));
            }

            builder.Append(')');
            if (initializerAssignments.IsDefaultOrEmpty)
            {
                builder.AppendLine(";");
                return;
            }

            builder.AppendLine();
            builder.Append(indent).AppendLine("{");
            for (var i = 0; i < initializerAssignments.Length; i++)
            {
                builder.Append(indent).Append("    ").Append(initializerAssignments[i]).AppendLine(",");
            }

            builder.Append(indent).AppendLine("};");
        }

        if (!wrapConstructionErrors)
        {
            EmitAssignmentBody("            ");
            return;
        }

        builder.AppendLine("            try");
        builder.AppendLine("            {");
        EmitAssignmentBody("                ");
        builder.AppendLine("            }");
        builder.AppendLine("            catch (Exception ex)");
        builder.AppendLine("            {");
        builder.AppendLine("                throw new TomlException($\"Failed to create an instance of '{typeof(" + typeName + ").FullName}'.\", ex);");
        builder.AppendLine("            }");
    }

    private static string? GetPopulateConditionExpression(PocoMember member, SourceGenOptions options)
    {
        var objectCreationHandling = member.ObjectCreationHandling == ObjectCreationHandlingKind.Default
            ? GetEffectiveObjectCreationHandling(options)
            : member.ObjectCreationHandling;

        return objectCreationHandling switch
        {
            ObjectCreationHandlingKind.Replace => null,
            ObjectCreationHandlingKind.Populate => "true",
            _ => null,
        };
    }

    private static string GetSingleOrArrayPopulateConditionExpression(PocoMember member, SourceGenOptions options)
    {
        var populateCondition = GetPopulateConditionExpression(member, options);
        if (!member.CanSet)
        {
            return "true";
        }

        return populateCondition ?? "false";
    }

    private static bool TryGetSingleOrArrayElementType(ITypeSymbol type, out ITypeSymbol elementType, out bool isArray, out SequenceKind kind)
    {
        if (TryGetArrayElementType(type, out elementType))
        {
            isArray = true;
            kind = default;
            return true;
        }

        if (TryGetSequenceElementType(type, out elementType, out kind))
        {
            isArray = false;
            return true;
        }

        elementType = null!;
        isArray = false;
        kind = default;
        return false;
    }

    private static bool CanPopulateSingleOrArraySequence(SequenceKind kind)
    {
        return kind is SequenceKind.List or SequenceKind.ListBackedEnumerable or SequenceKind.MutableCollection or SequenceKind.HashSet or SequenceKind.HashSetBackedEnumerable;
    }

    private static string? GetSingleOrArrayCreateExpression(ITypeSymbol collectionType)
    {
        if (!TryGetSingleOrArrayElementType(collectionType, out var elementType, out var isArray, out var kind))
        {
            return null;
        }

        var collectionTypeName = collectionType.ToDisplayString(FullyQualifiedNullableFormat);
        var elementTypeName = elementType.ToDisplayString(FullyQualifiedNullableFormat);
        var readElementExpression = GetTypeInfoReadExpression(elementType);

        if (isArray)
        {
            return "CreateSingleElementArray<" + elementTypeName + ">(" + readElementExpression + ")";
        }

        return kind switch
        {
            SequenceKind.List or SequenceKind.ListBackedEnumerable => "CreateSingleElementList<" + elementTypeName + ">(" + readElementExpression + ")",
            SequenceKind.MutableCollection => "CreateSingleElementCollection<" + collectionTypeName + ", " + elementTypeName + ">(" + readElementExpression + ")",
            SequenceKind.HashSet or SequenceKind.HashSetBackedEnumerable => "CreateSingleElementHashSet<" + elementTypeName + ">(" + readElementExpression + ")",
            SequenceKind.ImmutableArray => "CreateSingleElementImmutableArray<" + elementTypeName + ">(" + readElementExpression + ")",
            SequenceKind.ImmutableList => "CreateSingleElementImmutableList<" + elementTypeName + ">(" + readElementExpression + ")",
            SequenceKind.ImmutableHashSet => "CreateSingleElementImmutableHashSet<" + elementTypeName + ">(" + readElementExpression + ")",
            _ => null,
        };
    }

    private static string? GetSingleOrArrayCanPopulateExpression(ITypeSymbol collectionType, string existingExpression)
    {
        if (!TryGetSingleOrArrayElementType(collectionType, out var elementType, out var isArray, out var kind) || isArray || !CanPopulateSingleOrArraySequence(kind))
        {
            return null;
        }

        var elementTypeName = elementType.ToDisplayString(FullyQualifiedNullableFormat);
        return "CanPopulateSingleOrArrayCollection<" + elementTypeName + ">(" + existingExpression + ")";
    }

    private static string? GetSingleOrArrayAddSingleExpression(ITypeSymbol collectionType, string existingExpression)
    {
        if (!TryGetSingleOrArrayElementType(collectionType, out var elementType, out var isArray, out var kind) || isArray || !CanPopulateSingleOrArraySequence(kind))
        {
            return null;
        }

        var elementTypeName = elementType.ToDisplayString(FullyQualifiedNullableFormat);
        var readElementExpression = GetTypeInfoReadExpression(elementType);
        return "AddSingleElementToSingleOrArrayCollection<" + elementTypeName + ">(" + existingExpression + ", " + readElementExpression + ")";
    }

    private static string? GetSingleOrArrayAddCollectionExpression(ITypeSymbol collectionType, string existingExpression, string incomingExpression)
    {
        if (!TryGetSingleOrArrayElementType(collectionType, out var elementType, out var isArray, out var kind) || isArray || !CanPopulateSingleOrArraySequence(kind))
        {
            return null;
        }

        var elementTypeName = elementType.ToDisplayString(FullyQualifiedNullableFormat);
        return "AddCollectionToSingleOrArrayCollection<" + elementTypeName + ">(" + existingExpression + ", (global::System.Collections.Generic.IEnumerable<" + elementTypeName + ">)" + incomingExpression + ")";
    }

    private static void EmitSingleOrArrayReadAssignment(StringBuilder builder, ITypeSymbol collectionType, string targetExpression, string indent, string errorPrefix)
    {
        var collectionTypeName = collectionType.ToDisplayString(FullyQualifiedNullableFormat);
        var createExpression = GetSingleOrArrayCreateExpression(collectionType);

        builder.Append(indent).AppendLine("if (reader.TokenType == TomlTokenType.StartArray)");
        builder.Append(indent).AppendLine("{");
        builder.Append(indent).Append("    ").Append(targetExpression).Append(" = ").Append(GetTypeInfoReadExpression(collectionType)).AppendLine(";");
        builder.Append(indent).AppendLine("}");
        builder.Append(indent).AppendLine("else");
        builder.Append(indent).AppendLine("{");
        if (createExpression is null)
        {
            builder.Append(indent).Append("    throw new TomlException(\"").Append(errorPrefix)
                .Append(" but '").Append(EscapeStringLiteral(collectionTypeName))
                .AppendLine("' is not a supported collection type.\");");
        }
        else
        {
            builder.Append(indent).Append("    ").Append(targetExpression).Append(" = (").Append(collectionTypeName).Append(')').Append(createExpression).AppendLine("!;");
        }
        builder.Append(indent).AppendLine("}");
    }

    private static void EmitRepeatedTableExtensionIntoTarget(StringBuilder builder, ITypeSymbol type, string existingExpression, string targetExpression, string indent)
    {
        var typeName = type.ToDisplayString(FullyQualifiedNullableFormat);
        builder.Append(indent).Append("if (reader.TokenType == TomlTokenType.StartTable && reader.CurrentSpan is null && ").Append(existingExpression).AppendLine(" is not null)");
        builder.Append(indent).AppendLine("{");
        builder.Append(indent).Append("    var __tableExtension = ").Append(GetTypeInfoAccess(type)).Append(".ReadInto(reader, ").Append(existingExpression).AppendLine(");");
        builder.Append(indent).Append("    ").Append(targetExpression).Append(" = (").Append(typeName).AppendLine(")__tableExtension!;");
        builder.Append(indent).AppendLine("    continue;");
        builder.Append(indent).AppendLine("}");
    }

    private static void EmitRepeatedTableExtensionIntoReadOnlyMember(StringBuilder builder, PocoMember member, string indent)
    {
        var memberAccess = "value." + member.MemberName;
        var memberTypeName = member.Type.ToDisplayString(FullyQualifiedNullableFormat);
        builder.Append(indent).Append("if (reader.TokenType == TomlTokenType.StartTable && reader.CurrentSpan is null && ").Append(memberAccess).AppendLine(" is not null)");
        builder.Append(indent).AppendLine("{");
        builder.Append(indent).Append("    var __tableExtension = ").Append(GetMemberTypeInfoAccess(member)).Append(".ReadInto(reader, ").Append(memberAccess).AppendLine(");");
        builder.Append(indent).Append("    if (!object.ReferenceEquals(").Append(memberAccess).Append(", __tableExtension))").AppendLine();
        builder.Append(indent).AppendLine("    {");
        builder.Append(indent).Append("        throw new TomlException($\"Member '").Append(EscapeStringLiteral(member.MemberName))
            .Append("' on '{value.GetType().FullName}' cannot be extended by an additional TOML table definition because '")
            .Append(EscapeStringLiteral(memberTypeName))
            .AppendLine("' does not support in-place population.\");");
        builder.Append(indent).AppendLine("    }");
        builder.Append(indent).AppendLine("    continue;");
        builder.Append(indent).AppendLine("}");
    }

    // A member with a converter is read as a whole by its converter
    private static bool CanEmitTableHeaderExtension(PocoMember member) => !member.Type.IsValueType && member.ConverterTypeInfoName is null;

    private static bool CanEmitTableHeaderExtension(PocoConstructorParameter parameter) => !parameter.ParameterType.IsValueType && parameter.ConverterTypeInfoName is null;

    private static void EmitSingleOrArrayMemberRead(StringBuilder builder, PocoMember member, string indent, SourceGenOptions options, string memberAccess)
    {
        var memberTypeName = member.Type.ToDisplayString(FullyQualifiedNullableFormat);
        var populateCondition = GetSingleOrArrayPopulateConditionExpression(member, options);
        var errorPrefix = $"Member '{EscapeStringLiteral(member.MemberName)}' on '{{value.GetType().FullName}}' uses [TomlSingleOrArray]";
        var createExpression = GetSingleOrArrayCreateExpression(member.Type);
        var canPopulateExpression = GetSingleOrArrayCanPopulateExpression(member.Type, memberAccess + "!");
        var addSingleExpression = GetSingleOrArrayAddSingleExpression(member.Type, memberAccess + "!");

        builder.Append(indent).AppendLine("if (reader.TokenType == TomlTokenType.StartArray)");
        builder.Append(indent).AppendLine("{");
        builder.Append(indent).Append("    if (").Append(populateCondition).Append(" && ").Append(memberAccess).AppendLine(" is not null)");
        builder.Append(indent).AppendLine("    {");
        if (canPopulateExpression is not null)
        {
            builder.Append(indent).Append("        if (").Append(canPopulateExpression).AppendLine(")");
            builder.Append(indent).AppendLine("        {");
            builder.Append(indent).Append("            var __populated = ").Append(GetTypeInfoAccess(member.Type)).Append(".ReadInto(reader, ").Append(memberAccess).AppendLine(");");
            if (member.CanSet)
            {
                builder.Append(indent).Append("            ").Append(memberAccess).Append(" = (").Append(memberTypeName).AppendLine(")__populated!;");
            }
            else
            {
                builder.Append(indent).Append("            if (!object.ReferenceEquals(").Append(memberAccess).Append(", __populated))").AppendLine();
                builder.Append(indent).AppendLine("            {");
                builder.Append(indent).Append("                throw new TomlException($\"").Append(errorPrefix)
                    .Append(" but '").Append(EscapeStringLiteral(memberTypeName))
                    .AppendLine("' doesn't support populating the existing collection.\");");
                builder.Append(indent).AppendLine("            }");
            }
            builder.Append(indent).AppendLine("        }");
            builder.Append(indent).AppendLine("        else");
            builder.Append(indent).AppendLine("        {");
        }

        if (!member.CanSet)
        {
            builder.Append(indent).Append("            throw new TomlException($\"").Append(errorPrefix)
                .Append(" but '").Append(EscapeStringLiteral(memberTypeName))
                .AppendLine("' doesn't support populating the existing collection.\");");
        }
        else
        {
            builder.Append(indent).Append("            ").Append(memberAccess).Append(" = ").Append(GetTypeInfoReadExpression(member.Type)).AppendLine(";");
        }

        if (canPopulateExpression is not null)
        {
            builder.Append(indent).AppendLine("        }");
        }
        builder.Append(indent).AppendLine("    }");
        builder.Append(indent).AppendLine("    else");
        builder.Append(indent).AppendLine("    {");
        if (!member.CanSet)
        {
            builder.Append(indent).Append("        throw new TomlException($\"").Append(errorPrefix)
                .AppendLine(" but the existing collection is null or cannot be populated.\");");
        }
        else
        {
            builder.Append(indent).Append("        ").Append(memberAccess).Append(" = ").Append(GetTypeInfoReadExpression(member.Type)).AppendLine(";");
        }
        builder.Append(indent).AppendLine("    }");
        builder.Append(indent).AppendLine("}");
        builder.Append(indent).AppendLine("else");
        builder.Append(indent).AppendLine("{");
        if (createExpression is null)
        {
            builder.Append(indent).Append("    throw new TomlException($\"").Append(errorPrefix)
                .Append(" but '").Append(EscapeStringLiteral(memberTypeName))
                .AppendLine("' is not a supported collection type.\");");
        }
        else
        {
            builder.Append(indent).Append("    if (").Append(populateCondition).Append(" && ").Append(memberAccess).AppendLine(" is not null)");
            builder.Append(indent).AppendLine("    {");
            if (canPopulateExpression is not null && addSingleExpression is not null)
            {
                builder.Append(indent).Append("        if (").Append(canPopulateExpression).AppendLine(")");
                builder.Append(indent).AppendLine("        {");
                builder.Append(indent).Append("            _ = ").Append(addSingleExpression).AppendLine(";");
                builder.Append(indent).AppendLine("        }");
                builder.Append(indent).AppendLine("        else");
                builder.Append(indent).AppendLine("        {");
            }

            if (!member.CanSet)
            {
                builder.Append(indent).Append("            throw new TomlException($\"").Append(errorPrefix)
                    .Append(" but '").Append(EscapeStringLiteral(memberTypeName))
                    .AppendLine("' doesn't support populating the existing collection.\");");
            }
            else
            {
                builder.Append(indent).Append("            ").Append(memberAccess).Append(" = (").Append(memberTypeName).Append(')').Append(createExpression).AppendLine("!;");
            }

            if (canPopulateExpression is not null && addSingleExpression is not null)
            {
                builder.Append(indent).AppendLine("        }");
            }

            builder.Append(indent).AppendLine("    }");
            builder.Append(indent).AppendLine("    else");
            builder.Append(indent).AppendLine("    {");
            if (!member.CanSet)
            {
                builder.Append(indent).Append("        throw new TomlException($\"").Append(errorPrefix)
                    .AppendLine(" but the existing collection is null or cannot be populated.\");");
            }
            else
            {
                builder.Append(indent).Append("        ").Append(memberAccess).Append(" = (").Append(memberTypeName).Append(')').Append(createExpression).AppendLine("!;");
            }
            builder.Append(indent).AppendLine("    }");
        }
        builder.Append(indent).AppendLine("}");
    }

    private static void EmitMemberRead(StringBuilder builder, PocoMember member, int index, string indent, SourceGenOptions options)
    {
        if (member.SetterAccessorName is null)
        {
            EmitMemberRead(builder, member, index, indent, options, "value." + member.MemberName);
            return;
        }

        // The member is not accessible from the context: read it into a local, then set it with the generated accessor
        var local = "__accessorValue" + index.ToString(CultureInfo.InvariantCulture);
        builder.Append(indent).AppendLine("{");
        builder.Append(indent).Append("    var ").Append(local).Append(" = ").Append(GetMemberReadExpression(member, "value")).AppendLine(";");
        EmitMemberRead(builder, member, index, indent + "    ", options, local);
        builder.Append(indent).Append("    ").Append(GetSetterAccessorCall(member, "value", local)).AppendLine(";");
        builder.Append(indent).AppendLine("}");
    }

    private static void EmitMemberRead(StringBuilder builder, PocoMember member, int index, string indent, SourceGenOptions options, string memberAccess)
    {
        if (member.HasSingleOrArray && member.ConverterTypeInfoName is null)
        {
            EmitSingleOrArrayMemberRead(builder, member, indent, options, memberAccess);
            return;
        }

        var memberTypeName = member.Type.ToDisplayString(FullyQualifiedNullableFormat);
        var typeInfoAccess = GetMemberTypeInfoAccess(member);
        var populateCondition = member.ConverterTypeInfoName is null ? GetPopulateConditionExpression(member, options) : null;
        var existingLocal = "__existing" + index.ToString(CultureInfo.InvariantCulture);
        var populatedLocal = "__populated" + index.ToString(CultureInfo.InvariantCulture);
        var populateErrorPrefix =
            $"Member '{EscapeStringLiteral(member.MemberName)}' on '{{value.GetType().FullName}}' uses TomlObjectCreationHandling.Populate";
        var isExplicitPopulate = member.HasExplicitObjectCreationHandling && member.ObjectCreationHandling == ObjectCreationHandlingKind.Populate;

        if (populateCondition is null)
        {
            if (!member.CanSet)
            {
                builder.Append(indent).AppendLine("reader.Skip();");
            }
            else
            {
                builder.Append(indent).Append(memberAccess).Append(" = ").Append(GetMemberReadExpression(member)).AppendLine(";");
            }

            return;
        }

        if (populateCondition != "true")
        {
            builder.Append(indent).Append("if (").Append(populateCondition).AppendLine(")");
            builder.Append(indent).AppendLine("{");
        }

        if (!member.CanSet && member.Type.IsValueType)
        {
            if (isExplicitPopulate)
            {
                builder.Append(indent).Append("    throw new TomlException($\"")
                    .Append(populateErrorPrefix)
                    .Append(" but requires a setter because '")
                    .Append(EscapeStringLiteral(memberTypeName))
                    .AppendLine("' is a value type.\");");
            }
            else
            {
                builder.Append(populateCondition == "true" ? indent : indent + "    ").AppendLine("reader.Skip();");
            }
        }
        else
        {
            var branchIndent = populateCondition == "true" ? indent : indent + "    ";
            builder.Append(branchIndent).Append("var ").Append(existingLocal).Append(" = ").Append(memberAccess).AppendLine(";");

            if (CanBeNull(member.Type))
            {
                builder.Append(branchIndent).Append("if (").Append(existingLocal).AppendLine(" is null)");
                builder.Append(branchIndent).AppendLine("{");
                if (!member.CanSet)
                {
                    builder.Append(branchIndent).AppendLine("    reader.Skip();");
                }
                else
                {
                    builder.Append(branchIndent).Append("    ").Append(memberAccess).Append(" = ").Append(GetMemberReadExpression(member)).AppendLine(";");
                }
                builder.Append(branchIndent).AppendLine("}");
                builder.Append(branchIndent).AppendLine("else");
                builder.Append(branchIndent).AppendLine("{");
            }

            var populateIndent = CanBeNull(member.Type)
                ? (populateCondition == "true" ? indent + "    " : indent + "        ")
                : (populateCondition == "true" ? indent : indent + "    ");
            builder.Append(populateIndent).Append("var ").Append(populatedLocal).Append(" = ").Append(typeInfoAccess).Append(".ReadInto(reader, ").Append(existingLocal).AppendLine(");");
            if (member.CanSet)
            {
                builder.Append(populateIndent).Append(memberAccess).Append(" = (").Append(memberTypeName).Append(')').Append(populatedLocal).AppendLine("!;");
            }
            else
            {
                builder.Append(populateIndent).Append("if (!object.ReferenceEquals(").Append(existingLocal).Append(", ").Append(populatedLocal).AppendLine("))");
                builder.Append(populateIndent).AppendLine("{");
                if (isExplicitPopulate)
                {
                    builder.Append(populateIndent).Append("    throw new TomlException($\"")
                        .Append(populateErrorPrefix)
                        .AppendLine(" but it doesn't support populating.\");");
                }
                builder.Append(populateIndent).AppendLine("}");
            }

            if (CanBeNull(member.Type))
            {
                builder.Append(branchIndent).AppendLine("}");
            }
        }

        if (populateCondition != "true")
        {
            builder.Append(indent).AppendLine("}");
        }

        if (populateCondition == "true")
        {
            return;
        }

        builder.Append(indent).AppendLine("else");
        builder.Append(indent).AppendLine("{");
        if (!member.CanSet)
        {
            builder.Append(indent).AppendLine("    reader.Skip();");
        }
        else
        {
            builder.Append(indent).Append("    ").Append(memberAccess).Append(" = ").Append(GetMemberReadExpression(member)).AppendLine(";");
        }
        builder.Append(indent).AppendLine("}");
    }

    private static void EmitRecoverableMemberRead(StringBuilder builder, PocoMember member, int index, string indent, SourceGenOptions options)
    {
        builder.Append(indent).AppendLine("var __readTokenType = reader.TokenType;");
        builder.Append(indent).AppendLine("var __readSpan = reader.CurrentSpan;");
        builder.Append(indent).AppendLine("try");
        builder.Append(indent).AppendLine("{");
        EmitMemberRead(builder, member, index, indent + "    ", options);
        builder.Append(indent).AppendLine("}");
        builder.Append(indent).AppendLine("catch (TomlException __ex) when (TryAddDeserializationDiagnostic(reader, __readTokenType, __readSpan, __ex))");
        builder.Append(indent).AppendLine("{");
        builder.Append(indent).AppendLine("    continue;");
        builder.Append(indent).AppendLine("}");
    }

    private static void EmitMemberDispatch(StringBuilder builder, PocoShape poco, bool ignoreCase, SourceGenOptions options)
    {
        var useSeenMask = poco.Members.Length <= 64;
        var throwOnDuplicate = ShouldThrowOnDuplicate(options);
        if (ignoreCase)
        {
            var comparison = "StringComparison.OrdinalIgnoreCase";
            for (var i = 0; i < poco.Members.Length; i++)
            {
                var member = poco.Members[i];
                var alwaysThrowsOnRead =
                    !member.CanSet &&
                    member.Type.IsValueType &&
                    member.HasExplicitObjectCreationHandling &&
                    member.ObjectCreationHandling == ObjectCreationHandlingKind.Populate;
                builder.Append("                    ").Append(i == 0 ? "if" : "else if").Append("(string.Equals(name, \"").Append(EscapeStringLiteral(member.SerializedName)).Append("\", ").Append(comparison).AppendLine("))");
                builder.AppendLine("                    {");
                if (member.IsIgnoredOnRead)
                {
                    builder.AppendLine("                        reader.Skip();");
                    builder.AppendLine("                        continue;");
                    builder.AppendLine("                    }");
                    continue;
                }

                if (useSeenMask)
                {
                    if (throwOnDuplicate)
                    {
                        builder.Append("                        const ulong bit = 1UL << ").Append(i.ToString(CultureInfo.InvariantCulture)).AppendLine(";");
                        builder.AppendLine("                        if ((seenMask & bit) != 0)");
                        builder.AppendLine("                        {");
                        if (member.CanSet)
                        {
                            if (CanEmitTableHeaderExtension(member))
                            {
                                EmitRepeatedTableExtensionIntoTarget(builder, member.Type, "value." + member.MemberName, "value." + member.MemberName, "                            ");
                            }
                        }
                        else
                        {
                            if (CanEmitTableHeaderExtension(member))
                            {
                                EmitRepeatedTableExtensionIntoReadOnlyMember(builder, member, "                            ");
                            }
                        }
                        builder.AppendLine("                            throw reader.CreateException($\"Duplicate key '{name}' was encountered.\");");
                        builder.AppendLine("                        }");
                        builder.AppendLine("                        seenMask |= bit;");
                    }
                    else if (member.IsRequired)
                    {
                        builder.Append("                        const ulong bit = 1UL << ").Append(i.ToString(CultureInfo.InvariantCulture)).AppendLine(";");
                        builder.AppendLine("                        seenMask |= bit;");
                    }
                }
                else
                {
                    if (throwOnDuplicate)
                    {
                        builder.Append("                        if (seen[").Append(i.ToString(CultureInfo.InvariantCulture)).AppendLine("])");
                        builder.AppendLine("                        {");
                        if (member.CanSet)
                        {
                            if (CanEmitTableHeaderExtension(member))
                            {
                                EmitRepeatedTableExtensionIntoTarget(builder, member.Type, "value." + member.MemberName, "value." + member.MemberName, "                            ");
                            }
                        }
                        else
                        {
                            if (CanEmitTableHeaderExtension(member))
                            {
                                EmitRepeatedTableExtensionIntoReadOnlyMember(builder, member, "                            ");
                            }
                        }
                        builder.AppendLine("                            throw reader.CreateException($\"Duplicate key '{name}' was encountered.\");");
                        builder.AppendLine("                        }");
                        builder.Append("                        seen[").Append(i.ToString(CultureInfo.InvariantCulture)).AppendLine("] = true;");
                    }
                    else if (member.IsRequired)
                    {
                        builder.Append("                        seen[").Append(i.ToString(CultureInfo.InvariantCulture)).AppendLine("] = true;");
                    }
                }
                EmitRecoverableMemberRead(builder, member, i, "                        ", options);
                if (!alwaysThrowsOnRead)
                {
                    builder.AppendLine("                        continue;");
                }
                builder.AppendLine("                    }");
            }

            if (poco.ExtensionData is { } extensionData)
            {
                builder.Append("                    var __extensionData = value.").Append(extensionData.MemberName).AppendLine(";");
                builder.AppendLine("                    if (__extensionData is null)");
                builder.AppendLine("                    {");
                builder.Append("                        __extensionData = ").Append(extensionData.CreateExpression).AppendLine(";");
                builder.Append("                        value.").Append(extensionData.MemberName).AppendLine(" = __extensionData;");
                builder.AppendLine("                    }");
                builder.Append("                    __extensionData[name] = ").Append(GetTypeInfoReadExpression(extensionData.ValueType)).AppendLine(";");
                builder.AppendLine("                    continue;");
                return;
            }

            EmitUnmappedMember(builder, "                    ", poco.DisallowUnmappedMembers, poco.TypeName);
            return;
        }

        var membersByHash = new Dictionary<ulong, List<(int Index, PocoMember Member)>>();
        for (var i = 0; i < poco.Members.Length; i++)
        {
            var member = poco.Members[i];
            var hash = ComputePropertyNameHash56(member.SerializedName);
            if (!membersByHash.TryGetValue(hash, out var bucket))
            {
                bucket = new List<(int, PocoMember)>(capacity: 1);
                membersByHash.Add(hash, bucket);
            }

            bucket.Add((i, member));
        }

        builder.AppendLine("                    if (reader.TryGetPropertyNameHash(out var hash))");
        builder.AppendLine("                    {");
        builder.AppendLine("                        switch (hash)");
        builder.AppendLine("                        {");

        foreach (var kvp in membersByHash.OrderBy(static pair => pair.Key))
        {
            builder.Append("                            case 0x").Append(kvp.Key.ToString("X", CultureInfo.InvariantCulture)).AppendLine("UL:");
            builder.AppendLine("                            {");

            foreach (var (index, member) in kvp.Value)
            {
                var alwaysThrowsOnRead =
                    !member.CanSet &&
                    member.Type.IsValueType &&
                    member.HasExplicitObjectCreationHandling &&
                    member.ObjectCreationHandling == ObjectCreationHandlingKind.Populate;
                builder.Append("                                if (reader.PropertyNameEquals(\"").Append(EscapeStringLiteral(member.SerializedName)).AppendLine("\"))");
                builder.AppendLine("                                {");
                if (member.IsIgnoredOnRead)
                {
                    builder.AppendLine("                                    reader.Read();");
                    builder.AppendLine("                                    reader.Skip();");
                    builder.AppendLine("                                    continue;");
                    builder.AppendLine("                                }");
                    continue;
                }

                if (useSeenMask)
                {
                    if (throwOnDuplicate)
                    {
                        builder.Append("                                    const ulong bit = 1UL << ").Append(index.ToString(CultureInfo.InvariantCulture)).AppendLine(";");
                        builder.AppendLine("                                    if ((seenMask & bit) != 0)");
                        builder.AppendLine("                                    {");
                        if (!member.IsRequired)
                        {
                            builder.AppendLine("                                        var __duplicateName = reader.PropertyName!;");
                            builder.AppendLine("                                        reader.Read();");
                        }
                        if (member.CanSet)
                        {
                            if (CanEmitTableHeaderExtension(member))
                            {
                                EmitRepeatedTableExtensionIntoTarget(builder, member.Type, "value." + member.MemberName, "value." + member.MemberName, "                                        ");
                            }
                        }
                        else
                        {
                            if (CanEmitTableHeaderExtension(member))
                            {
                                EmitRepeatedTableExtensionIntoReadOnlyMember(builder, member, "                                        ");
                            }
                        }
                        if (member.IsRequired)
                        {
                            builder.AppendLine("                                        throw reader.CreateException($\"Duplicate key '{reader.PropertyName}' was encountered.\");");
                        }
                        else
                        {
                            builder.AppendLine("                                        throw reader.CreateException($\"Duplicate key '{__duplicateName}' was encountered.\");");
                        }
                        builder.AppendLine("                                    }");
                        builder.AppendLine("                                    seenMask |= bit;");
                    }
                    else if (member.IsRequired)
                    {
                        builder.Append("                                    const ulong bit = 1UL << ").Append(index.ToString(CultureInfo.InvariantCulture)).AppendLine(";");
                        builder.AppendLine("                                    seenMask |= bit;");
                    }
                }
                else
                {
                    if (throwOnDuplicate)
                    {
                        builder.Append("                                    if (seen[").Append(index.ToString(CultureInfo.InvariantCulture)).AppendLine("])");
                        builder.AppendLine("                                    {");
                        builder.AppendLine("                                        var __duplicateName = reader.PropertyName!;");
                        builder.AppendLine("                                        reader.Read();");
                        if (member.CanSet)
                        {
                            if (CanEmitTableHeaderExtension(member))
                            {
                                EmitRepeatedTableExtensionIntoTarget(builder, member.Type, "value." + member.MemberName, "value." + member.MemberName, "                                        ");
                            }
                        }
                        else
                        {
                            if (CanEmitTableHeaderExtension(member))
                            {
                                EmitRepeatedTableExtensionIntoReadOnlyMember(builder, member, "                                        ");
                            }
                        }
                        builder.AppendLine("                                        throw reader.CreateException($\"Duplicate key '{__duplicateName}' was encountered.\");");
                        builder.AppendLine("                                    }");
                        builder.Append("                                    seen[").Append(index.ToString(CultureInfo.InvariantCulture)).AppendLine("] = true;");
                    }
                    else if (member.IsRequired)
                    {
                        builder.Append("                                    seen[").Append(index.ToString(CultureInfo.InvariantCulture)).AppendLine("] = true;");
                    }
                }
                builder.AppendLine("                                    reader.Read();");
                EmitRecoverableMemberRead(builder, member, index, "                                    ", options);
                if (!alwaysThrowsOnRead)
                {
                    builder.AppendLine("                                    continue;");
                }
                builder.AppendLine("                                }");
            }

            builder.AppendLine("                                break;");
            builder.AppendLine("                            }");
        }

        builder.AppendLine("                        }");
        builder.AppendLine("                    }");
        if (poco.ExtensionData is { } extensionDataRead)
        {
            builder.AppendLine("                    {");
            builder.AppendLine("                        var name = reader.PropertyName!;");
            builder.AppendLine("                        reader.Read();");
            builder.Append("                        var __extensionData = value.").Append(extensionDataRead.MemberName).AppendLine(";");
            builder.AppendLine("                        if (__extensionData is null)");
            builder.AppendLine("                        {");
            builder.Append("                            __extensionData = ").Append(extensionDataRead.CreateExpression).AppendLine(";");
            builder.Append("                            value.").Append(extensionDataRead.MemberName).AppendLine(" = __extensionData;");
            builder.AppendLine("                        }");
            builder.Append("                        __extensionData[name] = ").Append(GetTypeInfoReadExpression(extensionDataRead.ValueType)).AppendLine(";");
            builder.AppendLine("                        continue;");
            builder.AppendLine("                    }");
            return;
        }

        if (poco.DisallowUnmappedMembers)
        {
            builder.AppendLine("                    var name = reader.PropertyName!;");
        }

        builder.AppendLine("                    reader.Read();");
        EmitUnmappedMember(builder, "                    ", poco.DisallowUnmappedMembers, poco.TypeName);
    }

    private static void EmitWriteMember(StringBuilder builder, PocoMember member, int index, string memberTypeName, bool canBeNull, string? usedKeysVariable, SourceGenOptions options, int? dottedKeyHandling)
    {
        var localName = "__member" + index.ToString(CultureInfo.InvariantCulture);
        var writeArgument = canBeNull ? localName + "!" : localName;
        var writeIndent = "                ";
        var openIndent = "            ";
        var serializedName = EscapeStringLiteral(member.SerializedName);
        var defaultLiteral = GetDefaultLiteral(member.Type);
        var defaultIsNull = member.Type.IsReferenceType || TryGetNullableUnderlyingType(member.Type, out _);
        var writeIgnore = GetEffectiveWriteIgnore(member, options);

        void EmitPropertyName(string indent)
        {
            if (member.HasFormattingMetadata)
            {
                builder.Append(indent).Append("ApplyPropertyMetadata(writer, \"").Append(serializedName).Append("\", ");
                EmitPropertyMetadataInitializer(builder, member);
                builder.AppendLine(");");
            }

            if (dottedKeyHandling is not null && TryGetTomlDottedKeyHandlingExpression(dottedKeyHandling.Value, out var dottedKeyHandlingExpression))
            {
                builder.Append(indent).Append("WritePropertyName(writer, \"").Append(serializedName).Append("\", ").Append(dottedKeyHandlingExpression).AppendLine(");");
            }
            else
            {
                builder.Append(indent).Append("writer.WritePropertyName(\"").Append(serializedName).AppendLine("\");");
            }
        }

        if (writeIgnore == WriteIgnoreKind.WhenWriting)
        {
            return;
        }

        if (member.DisallowNullOnSerialize)
        {
            builder.Append(openIndent).Append("if (").Append(localName).Append(" is null) throw new TomlException($\"The member '")
                .Append(EscapeInterpolatedStringLiteral(member.MemberName))
                .Append("' on '{typeof(")
                .Append(member.OwnerTypeName)
                .AppendLine(").FullName}' cannot be serialized as null because it is declared as non-nullable.\");");
        }

        if (writeIgnore == WriteIgnoreKind.WhenWritingNull)
        {
            if (canBeNull)
            {
                builder.Append(openIndent).Append("if (").Append(localName).AppendLine(" is not null)");
                builder.Append(openIndent).AppendLine("{");
                EmitPropertyName(writeIndent);
                if (usedKeysVariable is not null)
                {
                    builder.Append(writeIndent).Append(usedKeysVariable).Append("?.Add(\"").Append(serializedName).AppendLine("\");");
                }
                builder.Append(writeIndent).Append(GetMemberTypeInfoAccess(member)).Append(".Write(writer, ").Append(localName).AppendLine(");");
                builder.Append(openIndent).AppendLine("}");
                return;
            }

            EmitPropertyName(openIndent);
            if (usedKeysVariable is not null)
            {
                builder.Append(openIndent).Append(usedKeysVariable).Append("?.Add(\"").Append(serializedName).AppendLine("\");");
            }
            builder.Append(openIndent).Append(GetMemberTypeInfoAccess(member)).Append(".Write(writer, ").Append(localName).AppendLine(");");
            return;
        }

        if (writeIgnore == WriteIgnoreKind.WhenWritingDefault)
        {
            if (defaultIsNull && canBeNull)
            {
                builder.Append(openIndent).Append("if (").Append(localName).AppendLine(" is not null)");
            }
            else
            {
                builder.Append(openIndent).Append("if (!global::System.Collections.Generic.EqualityComparer<")
                    .Append(memberTypeName)
                    .Append(">.Default.Equals(")
                    .Append(localName)
                    .Append(", ")
                    .Append(defaultLiteral)
                    .AppendLine("))");
            }

            builder.Append(openIndent).AppendLine("{");
            EmitPropertyName(writeIndent);
            if (usedKeysVariable is not null)
            {
                builder.Append(writeIndent).Append(usedKeysVariable).Append("?.Add(\"").Append(serializedName).AppendLine("\");");
            }
            builder.Append(writeIndent).Append(GetMemberTypeInfoAccess(member)).Append(".Write(writer, ").Append(writeArgument).AppendLine(");");
            builder.Append(openIndent).AppendLine("}");
            return;
        }

        EmitPropertyName(openIndent);
        if (usedKeysVariable is not null)
        {
            builder.Append(openIndent).Append(usedKeysVariable).Append("?.Add(\"").Append(serializedName).AppendLine("\");");
        }

        builder.Append(openIndent).Append(GetMemberTypeInfoAccess(member)).Append(".Write(writer, ").Append(writeArgument).AppendLine(");");
    }

    private static bool CanBeNull(ITypeSymbol type)
    {
        if (type.IsReferenceType)
        {
            return true;
        }

        return TryGetNullableUnderlyingType(type, out _);
    }

    private static void EmitPropertyMetadataInitializer(StringBuilder builder, PocoMember member)
    {
        builder.Append("new global::Meziantou.Framework.Toml.Model.TomlPropertyMetadata { ");
        var hasPrevious = false;

        void AppendSeparator()
        {
            if (hasPrevious)
            {
                builder.Append(", ");
            }

            hasPrevious = true;
        }

        if (member.TableArrayStyle is not null && TryGetTomlTableArrayStyleExpression(member.TableArrayStyle.Value, out var tableArrayStyleExpression))
        {
            AppendSeparator();
            builder.Append("TableArrayStyle = ").Append(tableArrayStyleExpression);
        }

        if (member.InlineTablePolicy is not null && TryGetTomlInlineTablePolicyExpression(member.InlineTablePolicy.Value, out var inlineTablePolicyExpression))
        {
            AppendSeparator();
            builder.Append("InlineTablePolicy = ").Append(inlineTablePolicyExpression);
        }

        if (member.StringStyle is not null && TryGetTomlStringStyleExpression(member.StringStyle.Value, out var stringStyleExpression))
        {
            AppendSeparator();
            builder.Append("StringStyle = ").Append(stringStyleExpression);
        }

        if (member.PreferLiteralWhenNoEscapes is not null)
        {
            AppendSeparator();
            builder.Append("PreferLiteralWhenNoEscapes = ").Append(member.PreferLiteralWhenNoEscapes.Value ? "true" : "false");
        }

        if (member.AllowHexEscapes is not null)
        {
            AppendSeparator();
            builder.Append("AllowHexEscapes = ").Append(member.AllowHexEscapes.Value ? "true" : "false");
        }

        builder.Append(" }");
    }

    private static string GetDefaultLiteral(ITypeSymbol type)
    {
        return type.IsReferenceType && type.NullableAnnotation != NullableAnnotation.Annotated
            ? "default!"
            : "default";
    }

    private static bool TryGetNullableUnderlyingType(ITypeSymbol type, out ITypeSymbol underlyingType)
    {
        underlyingType = null!;

        if (type is not INamedTypeSymbol named ||
            !named.IsGenericType ||
            named.ConstructedFrom.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) != "global::System.Nullable<T>")
        {
            return false;
        }

        underlyingType = named.TypeArguments[0];
        return true;
    }

    private static ImmutableArray<ITypeSymbol> ExpandTypeGraph(
        SourceProductionContext context,
        ContextModel model,
        ImmutableArray<DerivedTypeMappingModel> derivedTypeMappings)
    {
        var queue = new Queue<ITypeSymbol>(model.RootTypes.Select(static root => root.Type));
        var seen = new HashSet<ITypeSymbol>(SymbolEqualityComparer.Default);
        var list = new List<ITypeSymbol>();

        foreach (var mapping in derivedTypeMappings)
        {
            queue.Enqueue(mapping.BaseType);
            queue.Enqueue(mapping.DerivedType);
        }

        while (queue.Count != 0)
        {
            var current = queue.Dequeue();
            if (!seen.Add(current))
            {
                continue;
            }

            list.Add(current);

            if (HasStaticOptionsConverter(model.Options, current))
            {
                continue;
            }

            if (GetDeclaredConverter(current, current) is { } typeConverter)
            {
                if (typeConverter.Error is { } typeConverterError)
                {
                    context.ReportDiagnostic(Diagnostic.Create(
                        InvalidConverterType,
                        current.Locations.FirstOrDefault() ?? model.ContextSymbol.Locations.FirstOrDefault(),
                        typeConverter.ConverterType?.ToDisplayString() ?? "null",
                        typeConverterError));
                }

                continue;
            }

            if (TryGetNullableUnderlyingType(current, out var nullableUnderlyingType))
            {
                if (IsSupportedMemberType(nullableUnderlyingType, model.Options, derivedTypeMappings))
                {
                    queue.Enqueue(nullableUnderlyingType);
                }

                continue;
            }

            if (TryGetArrayElementType(current, out var arrayElementType))
            {
                if (IsSupportedMemberType(arrayElementType, model.Options, derivedTypeMappings))
                {
                    queue.Enqueue(arrayElementType);
                }

                continue;
            }

            if (TryGetSequenceElementType(current, out var enumerableElementType, out _))
            {
                if (IsSupportedMemberType(enumerableElementType, model.Options, derivedTypeMappings))
                {
                    queue.Enqueue(enumerableElementType);
                }

                continue;
            }

            if (TryGetDictionaryValueType(current, out var dictionaryValueType))
            {
                if (IsSupportedMemberType(dictionaryValueType, model.Options, derivedTypeMappings))
                {
                    queue.Enqueue(dictionaryValueType);
                }

                continue;
            }

            if (TryGetPocoShape(context, model, current, out var poco))
            {
                foreach (var member in poco.Members)
                {
                    if (TryGetDictionaryKeyValueTypes(member.Type, out var dictKey, out var dictValue) &&
                        dictKey.SpecialType != SpecialType.System_String)
                    {
                        context.ReportDiagnostic(Diagnostic.Create(
                            UnsupportedDictionaryKeyType,
                            model.ContextSymbol.Locations.FirstOrDefault(),
                            current.ToDisplayString(),
                            member.MemberName,
                            member.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)));
                        continue;
                    }

                    if (member.ConverterTypeInfoName is not null)
                    {
                        continue;
                    }

                    if (!IsSupportedMemberType(member.Type, model.Options, derivedTypeMappings))
                    {
                        context.ReportDiagnostic(Diagnostic.Create(
                            UnsupportedMemberType,
                            model.ContextSymbol.Locations.FirstOrDefault(),
                            current.ToDisplayString(),
                            member.MemberName,
                            member.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)));
                        continue;
                    }

                    queue.Enqueue(member.Type);
                }

                if (poco.Constructor is { IsValid: true } ctor)
                {
                    foreach (var parameter in ctor.Parameters)
                    {
                        if (parameter.ConverterTypeInfoName is not null)
                        {
                            continue;
                        }

                        if (!IsSupportedMemberType(parameter.ParameterType, model.Options, derivedTypeMappings))
                        {
                            context.ReportDiagnostic(Diagnostic.Create(
                                UnsupportedMemberType,
                                model.ContextSymbol.Locations.FirstOrDefault(),
                                current.ToDisplayString(),
                                parameter.ParameterName,
                                parameter.ParameterType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)));
                            continue;
                        }

                        queue.Enqueue(parameter.ParameterType);
                    }
                }

                if (poco.ExtensionData is { } extensionData)
                {
                    if (!IsSupportedMemberType(extensionData.ValueType, model.Options, derivedTypeMappings))
                    {
                        context.ReportDiagnostic(Diagnostic.Create(
                            UnsupportedMemberType,
                            model.ContextSymbol.Locations.FirstOrDefault(),
                            current.ToDisplayString(),
                            extensionData.MemberName,
                            extensionData.ValueType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)));
                    }
                    else
                    {
                        queue.Enqueue(extensionData.ValueType);
                    }
                }
            }

            if (TryGetPolymorphicShape(context, model, derivedTypeMappings, current, out var polymorphic))
            {
                foreach (var derived in polymorphic.DerivedTypes)
                {
                    if (!IsSupportedMemberType(derived.Type, model.Options, derivedTypeMappings))
                    {
                        context.ReportDiagnostic(Diagnostic.Create(
                            InvalidPolymorphismConfiguration,
                            model.ContextSymbol.Locations.FirstOrDefault(),
                            current.ToDisplayString(),
                            $"Derived type '{derived.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}' is not supported by the source generator."));
                        continue;
                    }

                    queue.Enqueue(derived.Type);
                }

                if (polymorphic.DefaultDerivedType is not null)
                {
                    queue.Enqueue(polymorphic.DefaultDerivedType);
                }
            }
        }

        return list.ToImmutableArray();
    }

    private static bool ValidateRootAttributes(SourceProductionContext context, ContextModel model)
    {
        foreach (var root in model.RootTypes)
        {
            if (root.TypeInfoPropertyName is null)
            {
                continue;
            }

            if (!SyntaxFacts.IsValidIdentifier(root.TypeInfoPropertyName))
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    InvalidSourceGenerationOption,
                    model.ContextSymbol.Locations.FirstOrDefault(),
                    model.ContextSymbol.ToDisplayString(),
                    $"TomlSerializable TypeInfoPropertyName '{root.TypeInfoPropertyName}' must be a valid C# identifier."));
                return false;
            }
        }

        return true;
    }

    private static bool HasPolymorphismAttributes(INamedTypeSymbol type)
    {
        foreach (var attr in type.GetAttributes())
        {
            var attrName = attr.AttributeClass?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            if (attrName == "global::Meziantou.Framework.Toml.Serialization.TomlPolymorphicAttribute" ||
                attrName == "global::System.Text.Json.Serialization.JsonPolymorphicAttribute" ||
                attrName == "global::Meziantou.Framework.Toml.Serialization.TomlDerivedTypeAttribute" ||
                     attrName == "global::System.Text.Json.Serialization.JsonDerivedTypeAttribute")
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasPolymorphismConfiguration(INamedTypeSymbol type, ImmutableArray<DerivedTypeMappingModel> derivedTypeMappings)
    {
        if (HasPolymorphismAttributes(type))
        {
            return true;
        }

        foreach (var mapping in derivedTypeMappings)
        {
            if (SymbolEqualityComparer.Default.Equals(mapping.BaseType, type))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsSupportedMemberType(ITypeSymbol type, SourceGenOptions options, ImmutableArray<DerivedTypeMappingModel> derivedTypeMappings)
    {
        if (HasStaticOptionsConverter(options, type) || GetDeclaredConverter(type, type) is not null)
        {
            return true;
        }

        if (TryGetNullableUnderlyingType(type, out var nullableUnderlyingType))
        {
            return IsSupportedMemberType(nullableUnderlyingType, options, derivedTypeMappings);
        }

        // v1 generator milestone: built-in scalars/containers and POCOs.
        if (IsBuiltInType(type))
        {
            return true;
        }

        if (TryGetArrayElementType(type, out var arrayElementType))
        {
            return IsSupportedMemberType(arrayElementType, options, derivedTypeMappings);
        }

        if (TryGetSequenceElementType(type, out var enumerableElementType, out _))
        {
            return IsSupportedMemberType(enumerableElementType, options, derivedTypeMappings);
        }

        if (TryGetDictionaryKeyValueTypes(type, out var dictKeyType, out var dictValueType))
        {
            if (dictKeyType.SpecialType != SpecialType.System_String)
            {
                return false;
            }

            return IsSupportedMemberType(dictValueType, options, derivedTypeMappings);
        }

        if (type is INamedTypeSymbol named)
        {
            if (named.TypeKind == TypeKind.Struct)
            {
                return true;
            }

            if (named.TypeKind == TypeKind.Class)
            {
                return !named.IsAbstract || HasPolymorphismConfiguration(named, derivedTypeMappings);
            }

            if (named.TypeKind == TypeKind.Interface)
            {
                return HasPolymorphismConfiguration(named, derivedTypeMappings);
            }
        }

        return false;
    }

    private sealed class DeclaredConverter
    {
        public DeclaredConverter(ITypeSymbol? converterType, bool isStringEnum, string? error)
        {
            ConverterType = converterType;
            IsStringEnum = isStringEnum;
            Error = error;
        }

        public ITypeSymbol? ConverterType { get; }
        public bool IsStringEnum { get; }
        public string? Error { get; }
    }

    private sealed class PocoMember
    {
        public PocoMember(string memberName, string serializedName, ITypeSymbol type, ITypeSymbol declaringType, int order, WriteIgnoreKind writeIgnore, bool isIgnoredOnRead, ObjectCreationHandlingKind objectCreationHandling, bool hasExplicitObjectCreationHandling, bool hasSingleOrArray, bool isRequired, bool isCompilerRequired, bool canSet, bool isInitOnly, bool isField, string? getterAccessorName, int? tableArrayStyle, int? inlineTablePolicy, int? stringStyle, bool? preferLiteralWhenNoEscapes, bool? allowHexEscapes)
        {
            MemberName = memberName;
            SerializedName = serializedName;
            Type = type;
            DeclaringType = declaringType;
            Order = order;
            WriteIgnore = writeIgnore;
            IsIgnoredOnRead = isIgnoredOnRead;
            ObjectCreationHandling = objectCreationHandling;
            HasExplicitObjectCreationHandling = hasExplicitObjectCreationHandling;
            HasSingleOrArray = hasSingleOrArray;
            IsRequired = isRequired;
            IsCompilerRequired = isCompilerRequired;
            CanSet = canSet;
            IsInitOnly = isInitOnly;
            IsField = isField;
            GetterAccessorName = getterAccessorName;
            TableArrayStyle = tableArrayStyle;
            InlineTablePolicy = inlineTablePolicy;
            StringStyle = stringStyle;
            PreferLiteralWhenNoEscapes = preferLiteralWhenNoEscapes;
            AllowHexEscapes = allowHexEscapes;
        }

        public string MemberName { get; }
        public string SerializedName { get; }
        public ITypeSymbol Type { get; }
        public ITypeSymbol DeclaringType { get; }
        public int Order { get; }
        public WriteIgnoreKind WriteIgnore { get; }
        public bool IsIgnoredOnRead { get; }
        public ObjectCreationHandlingKind ObjectCreationHandling { get; }
        public bool HasExplicitObjectCreationHandling { get; }
        public bool HasSingleOrArray { get; }
        public bool IsRequired { get; }
        public bool IsCompilerRequired { get; }
        public bool CanSet { get; }
        public bool IsInitOnly { get; }
        public bool IsField { get; }
        public string? GetterAccessorName { get; }
        public int? TableArrayStyle { get; }
        public int? InlineTablePolicy { get; }
        public int? StringStyle { get; }
        public bool? PreferLiteralWhenNoEscapes { get; }
        public bool? AllowHexEscapes { get; }
        public bool HasFormattingMetadata => TableArrayStyle is not null || InlineTablePolicy is not null || StringStyle is not null || PreferLiteralWhenNoEscapes is not null || AllowHexEscapes is not null;

        /// <summary>Gets or sets the fully qualified name of the serialized type, used in error messages.</summary>
        public string OwnerTypeName { get; set; } = "";
        public bool DisallowNullOnSerialize { get; set; }
        public bool DisallowNullOnDeserialize { get; set; }

        // The converter declared on the member with [TomlConverter] or [JsonConverter]
        public DeclaredConverter? Converter { get; set; }

        // The generated property returning the member's converter type info, when the member has a converter
        public string? ConverterTypeInfoName { get; set; }

        // The generated method setting a member whose setter is not accessible from the context ([TomlInclude])
        public string? SetterAccessorName { get; set; }
    }

    private sealed class PocoExtensionData
    {
        public PocoExtensionData(string memberName, ITypeSymbol memberType, ITypeSymbol valueType, string createExpression, bool canSet, bool isInitOnly, bool isCompilerRequired)
        {
            MemberName = memberName;
            MemberType = memberType;
            ValueType = valueType;
            CreateExpression = createExpression;
            CanSet = canSet;
            IsInitOnly = isInitOnly;
            IsCompilerRequired = isCompilerRequired;
        }

        public string MemberName { get; }
        public ITypeSymbol MemberType { get; }
        public ITypeSymbol ValueType { get; }
        public string CreateExpression { get; }
        public bool CanSet { get; }
        public bool IsInitOnly { get; }
        public bool IsCompilerRequired { get; }
    }

    private sealed class PocoConstructor
    {
        public PocoConstructor(IMethodSymbol? constructor, ImmutableArray<PocoConstructorParameter> parameters, string? errorMessage, bool setsRequiredMembers)
        {
            Constructor = constructor;
            Parameters = parameters;
            ErrorMessage = errorMessage;
            SetsRequiredMembers = setsRequiredMembers;
        }

        public IMethodSymbol? Constructor { get; }
        public ImmutableArray<PocoConstructorParameter> Parameters { get; }
        public string? ErrorMessage { get; }
        public bool SetsRequiredMembers { get; }
        public bool IsValid => ErrorMessage is null && Constructor is not null;
    }

    private readonly struct PocoConstructorParameter
    {
        public PocoConstructorParameter(
            string keyName,
            string parameterName,
            ITypeSymbol parameterType,
            bool hasDefaultValue,
            string? defaultValueExpression,
            int linkedMemberIndex,
            bool disallowNull)
        {
            KeyName = keyName;
            ParameterName = parameterName;
            ParameterType = parameterType;
            HasDefaultValue = hasDefaultValue;
            DefaultValueExpression = defaultValueExpression;
            LinkedMemberIndex = linkedMemberIndex;
            DisallowNull = disallowNull;
        }

        public string KeyName { get; }
        public string ParameterName { get; }
        public ITypeSymbol ParameterType { get; }
        public bool HasDefaultValue { get; }
        public string? DefaultValueExpression { get; }
        public int LinkedMemberIndex { get; }
        public bool DisallowNull { get; }

        // The converter type info of the linked member, like the reflection resolver
        public string? ConverterTypeInfoName { get; init; }
    }

    private sealed class PocoShape
    {
        public PocoShape(ImmutableArray<PocoMember> members, PocoExtensionData? extensionData, PocoConstructor? constructor, bool requiresGeneratedObjectInitializer, int? mappingOrder, int? dottedKeyHandling)
        {
            Members = members;
            ExtensionData = extensionData;
            Constructor = constructor;
            RequiresGeneratedObjectInitializer = requiresGeneratedObjectInitializer;
            MappingOrder = mappingOrder;
            DottedKeyHandling = dottedKeyHandling;
        }

        public ImmutableArray<PocoMember> Members { get; }
        public PocoExtensionData? ExtensionData { get; }
        public PocoConstructor? Constructor { get; }
        public bool RequiresGeneratedObjectInitializer { get; }
        public int? MappingOrder { get; }
        public int? DottedKeyHandling { get; }
        public bool DisallowUnmappedMembers { get; set; }
        public string TypeName { get; set; } = "";
    }

    private static bool RequiresGeneratedObjectInitializer(
        ImmutableArray<PocoMember> members,
        PocoExtensionData? extensionData,
        PocoConstructor? constructor,
        bool parameterlessConstructorSetsRequiredMembers)
    {
        var hasInitOnlyMembers = members.Any(static member => member.IsInitOnly) || extensionData?.IsInitOnly == true;
        if (hasInitOnlyMembers)
        {
            return true;
        }

        var hasCompilerRequiredMembers = members.Any(static member => member.IsCompilerRequired) || extensionData?.IsCompilerRequired == true;
        if (!hasCompilerRequiredMembers)
        {
            return false;
        }

        return !(constructor?.SetsRequiredMembers ?? parameterlessConstructorSetsRequiredMembers);
    }

    private sealed class PolymorphicDerivedType
    {
        public PolymorphicDerivedType(ITypeSymbol type, string? discriminator)
        {
            Type = type;
            Discriminator = discriminator;
        }

        public ITypeSymbol Type { get; }
        public string? Discriminator { get; }
    }

    private sealed class PolymorphicShape
    {
        public PolymorphicShape(string? discriminatorPropertyName, ImmutableArray<PolymorphicDerivedType> derivedTypes, ITypeSymbol? defaultDerivedType, int? unknownDerivedTypeHandlingOverride)
        {
            DiscriminatorPropertyName = discriminatorPropertyName;
            DerivedTypes = derivedTypes;
            DefaultDerivedType = defaultDerivedType;
            UnknownDerivedTypeHandlingOverride = unknownDerivedTypeHandlingOverride;
        }

        public string? DiscriminatorPropertyName { get; }
        public ImmutableArray<PolymorphicDerivedType> DerivedTypes { get; }
        public ITypeSymbol? DefaultDerivedType { get; }
        /// <summary>
        /// Resolved unknown handling value (as int matching TomlUnknownDerivedTypeHandling enum), or null to use options default.
        /// </summary>
        public int? UnknownDerivedTypeHandlingOverride { get; }
    }

    private enum WriteIgnoreKind
    {
        None = 0,
        WhenWritingNull = 1,
        WhenWritingDefault = 2,
        WhenWriting = 4,
    }

    private static WriteIgnoreKind GetEffectiveWriteIgnore(PocoMember member, SourceGenOptions options)
    {
        if (member.WriteIgnore != WriteIgnoreKind.None)
        {
            return member.WriteIgnore;
        }

        return GetDefaultWriteIgnore(options);
    }

    private static WriteIgnoreKind GetDefaultWriteIgnore(SourceGenOptions options)
    {
        return (options.DefaultIgnoreCondition ?? DefaultDefaultIgnoreCondition) switch
        {
            0 or 5 => WriteIgnoreKind.None,
            1 => WriteIgnoreKind.WhenWritingNull,
            2 => WriteIgnoreKind.WhenWritingDefault,
            3 or 4 => WriteIgnoreKind.WhenWriting,
            _ => WriteIgnoreKind.None,
        };
    }

    private enum ObjectCreationHandlingKind
    {
        Default = 0,
        Replace = 1,
        Populate = 2,
    }

    private readonly struct IgnoreBehavior
    {
        public IgnoreBehavior(bool ignoreAlways, bool ignoreOnRead, WriteIgnoreKind writeIgnore)
        {
            IgnoreAlways = ignoreAlways;
            IgnoreOnRead = ignoreOnRead;
            WriteIgnore = writeIgnore;
        }

        public bool IgnoreAlways { get; }
        public bool IgnoreOnRead { get; }
        public WriteIgnoreKind WriteIgnore { get; }
    }

    private static bool TryGetPocoShape(SourceProductionContext context, ContextModel model, ITypeSymbol type, out PocoShape shape)
    {
        shape = null!;

        if (TryGetNullableUnderlyingType(type, out _))
        {
            return false;
        }

        if (IsBuiltInType(type))
        {
            return false;
        }

        if (TryGetArrayElementType(type, out _) ||
            TryGetSequenceElementType(type, out _, out _) ||
            TryGetDictionaryKeyValueTypes(type, out _, out _))
        {
            return false;
        }

        if (type is not INamedTypeSymbol named)
        {
            return false;
        }

        if (named.TypeKind is not (TypeKind.Class or TypeKind.Struct))
        {
            return false;
        }

        if (named.IsAbstract)
        {
            return false;
        }

        IMethodSymbol? selectedConstructor = null;
        string? constructorError = null;
        var parameterlessConstructorSetsRequiredMembers = false;
        if (named.TypeKind == TypeKind.Class)
        {
            var publicConstructors = named.InstanceConstructors
                .Where(static ctor => !ctor.IsStatic && ctor.DeclaredAccessibility == Accessibility.Public)
                .ToArray();

            if (publicConstructors.Length == 0)
            {
                constructorError = "No suitable constructor was found for deserialization.";
            }
            else
            {
                var annotated = publicConstructors
                    .Where(static ctor =>
                        HasAttribute(ctor, "Meziantou.Framework.Toml.Serialization.TomlConstructorAttribute") ||
                        HasAttribute(ctor, "System.Text.Json.Serialization.JsonConstructorAttribute"))
                    .ToArray();

                if (annotated.Length > 1)
                {
                    constructorError = "Multiple constructors were annotated for deserialization.";
                    selectedConstructor = annotated[0];
                }
                else if (annotated.Length == 1)
                {
                    selectedConstructor = annotated[0];
                }
                else if (publicConstructors.FirstOrDefault(static ctor => ctor.Parameters.Length == 0) is { } parameterlessConstructor)
                {
                    selectedConstructor = null;
                    parameterlessConstructorSetsRequiredMembers = HasAttribute(parameterlessConstructor, SetsRequiredMembersAttributeMetadataName);
                }
                else if (publicConstructors.Length == 1)
                {
                    selectedConstructor = publicConstructors[0];
                }
                else
                {
                    constructorError = "No suitable constructor was found for deserialization.";
                    selectedConstructor = publicConstructors[0];
                }
            }
        }

        // Like System.Text.Json, a constructor with [SetsRequiredMembers] makes the C# required modifier optional
        var honorRequiredModifier = selectedConstructor is not null
            ? !HasAttribute(selectedConstructor, SetsRequiredMembersAttributeMetadataName)
            : !parameterlessConstructorSetsRequiredMembers;

        var namingPolicy = model.Options.PropertyNamingPolicyExpression;
        var ownerTypeName = named.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        var respectNullableAnnotations = model.Options.RespectNullableAnnotations ?? true;
        var disallowUnmappedMembers = GetUnmappedMemberHandling(named, model.Options) == 1;
        var typeObjectCreationHandling = GetObjectCreationHandling(named);
        var typeMappingOrder = GetTypeLevelMappingOrder(named);
        var typeDottedKeyHandling = GetTypeLevelDottedKeyHandling(named);

        var members = ImmutableArray.CreateBuilder<PocoMember>();
        PocoExtensionData? extensionData = null;
        foreach (var member in EnumerateSerializableInstanceProperties(named))
        {
            if (member.IsIndexer)
            {
                continue;
            }

            if (member.GetMethod is null)
            {
                continue;
            }

            var hasInclude = HasAttribute(member, "Meziantou.Framework.Toml.Serialization.TomlIncludeAttribute") ||
                HasAttribute(member, "System.Text.Json.Serialization.JsonIncludeAttribute");
            if (member.GetMethod.DeclaredAccessibility != Accessibility.Public && !hasInclude)
            {
                continue;
            }

            var getterAccessible = IsAccessibleFromGeneratedContext(member.GetMethod.DeclaredAccessibility);
            var getterAccessorName = getterAccessible ? null : "__Get" + members.Count.ToString(CultureInfo.InvariantCulture);
            // Like the reflection resolver, [TomlInclude] makes a non-public setter usable
            var setterAccessible = member.SetMethod is not null && IsAccessibleFromGeneratedContext(member.SetMethod.DeclaredAccessibility);
            var canSet = member.SetMethod is not null && (setterAccessible || hasInclude);
            var setterAccessorName = canSet && !setterAccessible ? "__Set" + members.Count.ToString(CultureInfo.InvariantCulture) : null;
            var isInitOnly = member.SetMethod?.IsInitOnly == true;
            var isCompilerRequired = member.IsRequired;
            var hasSingleOrArray = HasAttribute(member, TomlSingleOrArrayAttributeMetadataName);
            var memberObjectCreationHandling = GetObjectCreationHandling(member);
            var hasExplicitObjectCreationHandling = memberObjectCreationHandling != ObjectCreationHandlingKind.Default;
            var objectCreationHandling = memberObjectCreationHandling;
            if (objectCreationHandling == ObjectCreationHandlingKind.Default)
            {
                objectCreationHandling = typeObjectCreationHandling;
            }

            if (IsExtensionData(member))
            {
                if (extensionData is not null)
                {
                    context.ReportDiagnostic(Diagnostic.Create(
                        InvalidExtensionDataMember,
                        model.ContextSymbol.Locations.FirstOrDefault(),
                        type.ToDisplayString(),
                        member.Name,
                        "Multiple extension data members were found. Only a single member can be annotated with [TomlExtensionData]/[JsonExtensionData]."));
                    continue;
                }

                if (!TryGetExtensionDataValueType(member.Type, out var extensionValueType))
                {
                    context.ReportDiagnostic(Diagnostic.Create(
                        InvalidExtensionDataMember,
                        model.ContextSymbol.Locations.FirstOrDefault(),
                        type.ToDisplayString(),
                        member.Name,
                        "Extension data members must be dictionary-like with string keys (for example IDictionary<string, object> or TomlTable)."));
                    continue;
                }

                if (!TryGetExtensionDataCreateExpression(member.Type, extensionValueType, out var createExpression))
                {
                    context.ReportDiagnostic(Diagnostic.Create(
                        InvalidExtensionDataMember,
                        model.ContextSymbol.Locations.FirstOrDefault(),
                        type.ToDisplayString(),
                        member.Name,
                        "Extension data members must be instantiable (public parameterless constructor) or use an interface type such as IDictionary<string, TValue>."));
                    continue;
                }

                extensionData = new PocoExtensionData(member.Name, member.Type, extensionValueType, createExpression, canSet, isInitOnly, isCompilerRequired);
                continue;
            }

            var ignore = GetIgnoreBehavior(member);
            if (ignore.IgnoreAlways)
            {
                continue;
            }

            // Same rule as the reflection resolver: a property without a public setter (or [TomlInclude]) is read-only.
            // An init accessor makes the property writable.
            var isReadOnlyProperty = member.SetMethod is null || (member.SetMethod.DeclaredAccessibility != Accessibility.Public && !hasInclude);
            var writeIgnore = isReadOnlyProperty && model.Options.IgnoreReadOnlyProperties == true ? WriteIgnoreKind.WhenWriting : ignore.WriteIgnore;

            var serializedName = GetSerializedName(member, member.Name, namingPolicy);
            var order = GetOrder(member);
            var required = IsRequired(member, honorRequiredModifier) && !ignore.IgnoreOnRead;
            var formatting = GetFormattingMetadata(member);
            if (formatting.StringStyle is not null && member.Type.SpecialType != SpecialType.System_String)
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    InvalidAttributeUsage,
                    member.Locations.FirstOrDefault(),
                    type.ToDisplayString(),
                    member.Name,
                    "[TomlStringStyle] can only be applied to string members."));
                continue;
            }

            var declaredConverter = GetDeclaredConverter(member, member.Type);
            if (declaredConverter?.Error is { } converterError)
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    InvalidConverterType,
                    member.Locations.FirstOrDefault(),
                    declaredConverter.ConverterType?.ToDisplayString() ?? "null",
                    converterError));
                continue;
            }

            members.Add(new PocoMember(member.Name, serializedName, member.Type, member.ContainingType, order, writeIgnore, ignore.IgnoreOnRead, objectCreationHandling, hasExplicitObjectCreationHandling, hasSingleOrArray, required, isCompilerRequired, canSet, isInitOnly, isField: false, getterAccessorName, formatting.TableArrayStyle, formatting.InlineTablePolicy, formatting.StringStyle, formatting.PreferLiteralWhenNoEscapes, formatting.AllowHexEscapes)
            {
                OwnerTypeName = ownerTypeName,
                DisallowNullOnSerialize = respectNullableAnnotations && IsNonNullableReferenceType(member.Type) && !HasAttribute(member, MaybeNullAttributeMetadataName),
                DisallowNullOnDeserialize = respectNullableAnnotations && IsNonNullableReferenceType(member.Type) && !HasAttribute(member, AllowNullAttributeMetadataName),
                Converter = declaredConverter,
                ConverterTypeInfoName = declaredConverter is null ? null : "MemberConverterTypeInfo" + members.Count.ToString(CultureInfo.InvariantCulture),
                SetterAccessorName = setterAccessorName,
            });
        }

        foreach (var member in EnumerateSerializableInstanceFields(named))
        {
            if (member.IsImplicitlyDeclared || member.IsConst || member.IsStatic)
            {
                continue;
            }

            if (IsExtensionData(member))
            {
                if (extensionData is not null)
                {
                    context.ReportDiagnostic(Diagnostic.Create(
                        InvalidExtensionDataMember,
                        model.ContextSymbol.Locations.FirstOrDefault(),
                        type.ToDisplayString(),
                        member.Name,
                        "Multiple extension data members were found. Only a single member can be annotated with [TomlExtensionData]/[JsonExtensionData]."));
                    continue;
                }

                if (!TryGetExtensionDataValueType(member.Type, out var extensionValueType))
                {
                    context.ReportDiagnostic(Diagnostic.Create(
                        InvalidExtensionDataMember,
                        model.ContextSymbol.Locations.FirstOrDefault(),
                        type.ToDisplayString(),
                        member.Name,
                        "Extension data members must be dictionary-like with string keys (for example IDictionary<string, object> or TomlTable)."));
                    continue;
                }

                if (!TryGetExtensionDataCreateExpression(member.Type, extensionValueType, out var createExpression))
                {
                    context.ReportDiagnostic(Diagnostic.Create(
                        InvalidExtensionDataMember,
                        model.ContextSymbol.Locations.FirstOrDefault(),
                        type.ToDisplayString(),
                        member.Name,
                        "Extension data members must be instantiable (public parameterless constructor) or use an interface type such as IDictionary<string, TValue>."));
                    continue;
                }

                extensionData = new PocoExtensionData(member.Name, member.Type, extensionValueType, createExpression, canSet: true, isInitOnly: false, isCompilerRequired: member.IsRequired);
                continue;
            }

            var hasInclude = HasAttribute(member, "Meziantou.Framework.Toml.Serialization.TomlIncludeAttribute") ||
                HasAttribute(member, "System.Text.Json.Serialization.JsonIncludeAttribute");
            if (!hasInclude && !(model.Options.IncludeFields == true && member.DeclaredAccessibility == Accessibility.Public))
            {
                continue;
            }

            var ignore = GetIgnoreBehavior(member);
            if (ignore.IgnoreAlways)
            {
                continue;
            }

            var hasSingleOrArray = HasAttribute(member, TomlSingleOrArrayAttributeMetadataName);
            var memberObjectCreationHandling = GetObjectCreationHandling(member);
            var hasExplicitObjectCreationHandling = memberObjectCreationHandling != ObjectCreationHandlingKind.Default;
            var objectCreationHandling = memberObjectCreationHandling;
            if (objectCreationHandling == ObjectCreationHandlingKind.Default)
            {
                objectCreationHandling = typeObjectCreationHandling;
            }

            var serializedName = GetSerializedName(member, member.Name, namingPolicy);
            var order = GetOrder(member);
            var required = IsRequired(member, honorRequiredModifier) && !ignore.IgnoreOnRead;
            var getterAccessorName = IsAccessibleFromGeneratedContext(member.DeclaredAccessibility) ? null : "__Get" + members.Count.ToString(CultureInfo.InvariantCulture);
            var fieldAccessible = IsAccessibleFromGeneratedContext(member.DeclaredAccessibility);
            var canSet = !member.IsReadOnly && (fieldAccessible || hasInclude);
            var setterAccessorName = canSet && !fieldAccessible ? "__Set" + members.Count.ToString(CultureInfo.InvariantCulture) : null;
            var fieldWriteIgnore = member.IsReadOnly && model.Options.IgnoreReadOnlyFields == true ? WriteIgnoreKind.WhenWriting : ignore.WriteIgnore;
            var formatting = GetFormattingMetadata(member);
            if (formatting.StringStyle is not null && member.Type.SpecialType != SpecialType.System_String)
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    InvalidAttributeUsage,
                    member.Locations.FirstOrDefault(),
                    type.ToDisplayString(),
                    member.Name,
                    "[TomlStringStyle] can only be applied to string members."));
                continue;
            }

            var declaredConverter = GetDeclaredConverter(member, member.Type);
            if (declaredConverter?.Error is { } converterError)
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    InvalidConverterType,
                    member.Locations.FirstOrDefault(),
                    declaredConverter.ConverterType?.ToDisplayString() ?? "null",
                    converterError));
                continue;
            }

            members.Add(new PocoMember(member.Name, serializedName, member.Type, member.ContainingType, order, fieldWriteIgnore, ignore.IgnoreOnRead, objectCreationHandling, hasExplicitObjectCreationHandling, hasSingleOrArray, required, member.IsRequired, canSet, isInitOnly: false, isField: true, getterAccessorName, formatting.TableArrayStyle, formatting.InlineTablePolicy, formatting.StringStyle, formatting.PreferLiteralWhenNoEscapes, formatting.AllowHexEscapes)
            {
                OwnerTypeName = ownerTypeName,
                DisallowNullOnSerialize = respectNullableAnnotations && IsNonNullableReferenceType(member.Type) && !HasAttribute(member, MaybeNullAttributeMetadataName),
                DisallowNullOnDeserialize = respectNullableAnnotations && IsNonNullableReferenceType(member.Type) && !HasAttribute(member, AllowNullAttributeMetadataName),
                Converter = declaredConverter,
                ConverterTypeInfoName = declaredConverter is null ? null : "MemberConverterTypeInfo" + members.Count.ToString(CultureInfo.InvariantCulture),
                SetterAccessorName = setterAccessorName,
            });
        }

        PocoConstructor? constructorModel = null;
        if (constructorError is not null && selectedConstructor is null)
        {
            constructorModel = new PocoConstructor(null, ImmutableArray<PocoConstructorParameter>.Empty, constructorError, setsRequiredMembers: false);
        }
        else if (selectedConstructor is not null)
        {
            var setsRequiredMembers = HasAttribute(selectedConstructor, SetsRequiredMembersAttributeMetadataName);
            if (selectedConstructor.Parameters.Length > 0)
            {
                var memberIndexByClrName = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                var membersSoFar = members.ToImmutable();
                for (var i = 0; i < membersSoFar.Length; i++)
                {
                    memberIndexByClrName[membersSoFar[i].MemberName] = i;
                }

                var parameterComparer = GetEffectivePropertyNameCaseInsensitive(model.Options) ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
                var parameterKeys = new HashSet<string>(parameterComparer);
                var parameters = ImmutableArray.CreateBuilder<PocoConstructorParameter>(selectedConstructor.Parameters.Length);

                for (var i = 0; i < selectedConstructor.Parameters.Length; i++)
                {
                    var parameter = selectedConstructor.Parameters[i];
                    var parameterName = string.IsNullOrEmpty(parameter.Name) ? $"arg{i}" : parameter.Name;

                    var linkedMemberIndex = memberIndexByClrName.TryGetValue(parameterName, out var linkedIndex) ? linkedIndex : -1;
                    var keyName = linkedMemberIndex >= 0
                        ? membersSoFar[linkedMemberIndex].SerializedName
                        : GetConstructorParameterKeyName(parameterName, namingPolicy);

                    if (!parameterKeys.Add(keyName))
                    {
                        constructorError ??= $"Constructor parameter name collision for key '{keyName}' on type '{type.ToDisplayString()}'.";
                    }

                    var hasDefaultValue = parameter.HasExplicitDefaultValue;
                    string? defaultValueExpression = null;
                    if (hasDefaultValue && !TryGetDefaultValueExpression(parameter.Type, parameter.ExplicitDefaultValue, out defaultValueExpression))
                    {
                        constructorError ??= $"Constructor parameter '{parameterName}' on type '{type.ToDisplayString()}' has an unsupported default value.";
                        hasDefaultValue = false;
                    }

                    var parameterDisallowNull = respectNullableAnnotations && IsNonNullableReferenceType(parameter.Type) && !HasAttribute(parameter, AllowNullAttributeMetadataName);
                    parameters.Add(new PocoConstructorParameter(keyName, parameterName, parameter.Type, hasDefaultValue, defaultValueExpression, linkedMemberIndex, parameterDisallowNull)
                    {
                        ConverterTypeInfoName = linkedMemberIndex >= 0 ? membersSoFar[linkedMemberIndex].ConverterTypeInfoName : null,
                    });
                }

                constructorModel = new PocoConstructor(selectedConstructor, parameters.ToImmutable(), constructorError, setsRequiredMembers);
                shape = new PocoShape(
                    membersSoFar,
                    extensionData,
                    constructorModel,
                    RequiresGeneratedObjectInitializer(membersSoFar, extensionData, constructorModel, parameterlessConstructorSetsRequiredMembers),
                    typeMappingOrder,
                    typeDottedKeyHandling)
                {
                    DisallowUnmappedMembers = disallowUnmappedMembers,
                    TypeName = ownerTypeName,
                };
                return true;
            }

            constructorModel = new PocoConstructor(selectedConstructor, ImmutableArray<PocoConstructorParameter>.Empty, constructorError, setsRequiredMembers);
        }

        var finalMembers = members.ToImmutable();
        shape = new PocoShape(
            finalMembers,
            extensionData,
            constructorModel,
            RequiresGeneratedObjectInitializer(finalMembers, extensionData, constructorModel, parameterlessConstructorSetsRequiredMembers),
            typeMappingOrder,
            typeDottedKeyHandling)
        {
            DisallowUnmappedMembers = disallowUnmappedMembers,
            TypeName = ownerTypeName,
        };
        return true;
    }

    private static IEnumerable<IPropertySymbol> EnumerateSerializableInstanceProperties(INamedTypeSymbol type)
    {
        var hiddenPropertyNames = new HashSet<string>(StringComparer.Ordinal);

        for (var current = type; current is not null; current = current.BaseType)
        {
            foreach (var member in current.GetMembers().OfType<IPropertySymbol>())
            {
                if (member.IsStatic)
                {
                    continue;
                }

                if (!hiddenPropertyNames.Add(member.Name))
                {
                    continue;
                }

                yield return member;
            }
        }
    }

    private static IEnumerable<IFieldSymbol> EnumerateSerializableInstanceFields(INamedTypeSymbol type)
    {
        for (var current = type; current is not null; current = current.BaseType)
        {
            foreach (var member in current.GetMembers().OfType<IFieldSymbol>())
            {
                if (member.IsStatic)
                {
                    continue;
                }

                yield return member;
            }
        }
    }

    private static bool IsAccessibleFromGeneratedContext(Accessibility accessibility)
    {
        return accessibility is Accessibility.Public or Accessibility.Internal or Accessibility.ProtectedOrInternal;
    }

    private static bool TryGetPolymorphicShape(
        SourceProductionContext context,
        ContextModel model,
        ImmutableArray<DerivedTypeMappingModel> derivedTypeMappings,
        ITypeSymbol type,
        out PolymorphicShape shape,
        bool reportDiagnostics = true)
    {
        shape = null!;

        if (type is not INamedTypeSymbol named)
        {
            return false;
        }

        if (named.TypeKind is not (TypeKind.Class or TypeKind.Interface))
        {
            return false;
        }

        string? tomlDiscriminatorPropertyName = null;
        string? jsonDiscriminatorPropertyName = null;
        int? tomlUnknownHandling = null;
        int? jsonUnknownHandling = null;

        foreach (var attr in named.GetAttributes())
        {
            var attrName = attr.AttributeClass?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            if (attrName == "global::Meziantou.Framework.Toml.Serialization.TomlPolymorphicAttribute")
            {
                foreach (var kvp in attr.NamedArguments)
                {
                    if (kvp.Key == "TypeDiscriminatorPropertyName" && kvp.Value.Value is string s && !string.IsNullOrEmpty(s))
                    {
                        tomlDiscriminatorPropertyName = s;
                    }
                    else if (kvp.Key == "UnknownDerivedTypeHandling" && kvp.Value.Value is int intVal)
                    {
                        // -1 = Unspecified, 0 = Fail, 1 = FallBackToBaseType
                        if (intVal != -1)
                        {
                            tomlUnknownHandling = intVal;
                        }
                    }
                }
            }
            else if (attrName == "global::System.Text.Json.Serialization.JsonPolymorphicAttribute")
            {
                foreach (var kvp in attr.NamedArguments)
                {
                    if (kvp.Key == "TypeDiscriminatorPropertyName" && kvp.Value.Value is string s && !string.IsNullOrEmpty(s))
                    {
                        jsonDiscriminatorPropertyName = s;
                    }
                    else if (kvp.Key == "UnknownDerivedTypeHandling" && kvp.Value.Value is int intVal)
                    {
                        // JsonUnknownDerivedTypeHandling: FailSerialization=0, FallBackToBaseType=1, FallBackToNearestAncestor=2
                        jsonUnknownHandling = intVal == 1 ? 1 : 0; // 1 => FallBackToBaseType, anything else => Fail
                    }
                }
            }
        }

        var derived = ImmutableArray.CreateBuilder<PolymorphicDerivedType>();
        var discriminatorSet = new HashSet<string>(StringComparer.Ordinal);
        var derivedTypeSet = new HashSet<ITypeSymbol>(SymbolEqualityComparer.Default);
        ITypeSymbol? defaultDerivedType = null;

        foreach (var attr in named.GetAttributes())
        {
            var attrName = attr.AttributeClass?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            if (attrName == "global::Meziantou.Framework.Toml.Serialization.TomlDerivedTypeAttribute")
            {
                if (attr.ConstructorArguments.Length == 1 &&
                    attr.ConstructorArguments[0].Kind == TypedConstantKind.Type &&
                    attr.ConstructorArguments[0].Value is ITypeSymbol defaultType)
                {
                    // TomlDerivedTypeAttribute(Type) - default derived type (no discriminator)
                    if (defaultDerivedType is not null)
                    {
                        if (reportDiagnostics)
                        {
                            context.ReportDiagnostic(Diagnostic.Create(
                                InvalidPolymorphismConfiguration,
                                model.ContextSymbol.Locations.FirstOrDefault(),
                                type.ToDisplayString(),
                                "Only one default derived type (no discriminator) can be registered."));
                        }
                        continue;
                    }

                    if (!ValidateDerivedType(defaultType, discriminator: null))
                    {
                        continue;
                    }

                    if (!derivedTypeSet.Add(defaultType))
                    {
                        if (reportDiagnostics)
                        {
                            context.ReportDiagnostic(Diagnostic.Create(
                                InvalidPolymorphismConfiguration,
                                model.ContextSymbol.Locations.FirstOrDefault(),
                                type.ToDisplayString(),
                                $"Derived type '{defaultType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}' is registered more than once."));
                        }
                        continue;
                    }

                    defaultDerivedType = defaultType;
                }
                else if (attr.ConstructorArguments.Length == 2 &&
                    attr.ConstructorArguments[0].Kind == TypedConstantKind.Type &&
                    attr.ConstructorArguments[0].Value is ITypeSymbol derivedType)
                {
                    // TomlDerivedTypeAttribute(Type, string) or TomlDerivedTypeAttribute(Type, int)
                    var discriminatorObj = attr.ConstructorArguments[1].Value;
                    var discriminator = discriminatorObj switch
                    {
                        string s => s,
                        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
                        _ => discriminatorObj?.ToString() ?? string.Empty,
                    };

                    if (string.IsNullOrEmpty(discriminator))
                    {
                        if (reportDiagnostics)
                        {
                            context.ReportDiagnostic(Diagnostic.Create(
                                InvalidPolymorphismConfiguration,
                                model.ContextSymbol.Locations.FirstOrDefault(),
                                type.ToDisplayString(),
                            "TomlDerivedTypeAttribute must specify a derived type and a non-empty discriminator."));
                        }
                        continue;
                    }

                    AddStrictDerivedType(derivedType, discriminator);
                }
                else
                {
                    if (reportDiagnostics)
                    {
                        context.ReportDiagnostic(Diagnostic.Create(
                            InvalidPolymorphismConfiguration,
                            model.ContextSymbol.Locations.FirstOrDefault(),
                            type.ToDisplayString(),
                            "TomlDerivedTypeAttribute must specify a derived type and an optional discriminator."));
                    }
                    continue;
                }
            }
            else if (attrName == "global::System.Text.Json.Serialization.JsonDerivedTypeAttribute")
            {
                if (attr.ConstructorArguments.Length < 1 ||
                    attr.ConstructorArguments[0].Kind != TypedConstantKind.Type ||
                    attr.ConstructorArguments[0].Value is not ITypeSymbol derivedType)
                {
                    if (reportDiagnostics)
                    {
                        context.ReportDiagnostic(Diagnostic.Create(
                            InvalidPolymorphismConfiguration,
                            model.ContextSymbol.Locations.FirstOrDefault(),
                            type.ToDisplayString(),
                            "JsonDerivedTypeAttribute must specify a derived type."));
                    }
                    continue;
                }

                if (attr.ConstructorArguments.Length < 2 || attr.ConstructorArguments[1].IsNull)
                {
                    TryAddLowerPrecedenceDefaultDerivedType(derivedType);
                    continue;
                }

                var discriminatorObj = attr.ConstructorArguments[1].Value;
                var discriminator = discriminatorObj switch
                {
                    string s => s,
                    IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
                    _ => discriminatorObj?.ToString() ?? string.Empty,
                };

                if (string.IsNullOrEmpty(discriminator))
                {
                    continue;
                }

                TryAddLowerPrecedenceDerivedType(derivedType, discriminator);
            }
        }

        foreach (var mapping in derivedTypeMappings)
        {
            if (!SymbolEqualityComparer.Default.Equals(mapping.BaseType, type))
            {
                continue;
            }

            if (mapping.Discriminator is null)
            {
                TryAddLowerPrecedenceDefaultDerivedType(mapping.DerivedType);
            }
            else
            {
                TryAddLowerPrecedenceDerivedType(mapping.DerivedType, mapping.Discriminator);
            }
        }

        if (derived.Count == 0 && defaultDerivedType is null)
        {
            return false;
        }

        var discriminatorPropertyName = tomlDiscriminatorPropertyName ?? jsonDiscriminatorPropertyName;
        // Priority chain: TomlPolymorphicAttribute → JsonPolymorphicAttribute → options (null = use options)
        int? resolvedUnknownHandling = tomlUnknownHandling ?? jsonUnknownHandling;
        shape = new PolymorphicShape(discriminatorPropertyName, derived.ToImmutable(), defaultDerivedType, resolvedUnknownHandling);
        return true;

        void AddStrictDerivedType(ITypeSymbol derivedType, string discriminator)
        {
            if (!discriminatorSet.Add(discriminator))
            {
                if (reportDiagnostics)
                {
                    context.ReportDiagnostic(Diagnostic.Create(
                        InvalidPolymorphismConfiguration,
                        model.ContextSymbol.Locations.FirstOrDefault(),
                        type.ToDisplayString(),
                        $"Discriminator '{discriminator}' is registered more than once."));
                }
                return;
            }

            if (!derivedTypeSet.Add(derivedType))
            {
                if (reportDiagnostics)
                {
                    context.ReportDiagnostic(Diagnostic.Create(
                        InvalidPolymorphismConfiguration,
                        model.ContextSymbol.Locations.FirstOrDefault(),
                        type.ToDisplayString(),
                        $"Derived type '{derivedType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}' is registered more than once."));
                }
                return;
            }

            if (!ValidateDerivedType(derivedType, discriminator))
            {
                return;
            }

            derived.Add(new PolymorphicDerivedType(derivedType, discriminator));
        }

        void TryAddLowerPrecedenceDefaultDerivedType(ITypeSymbol derivedType)
        {
            if (!ValidateDerivedType(derivedType, discriminator: null))
            {
                return;
            }

            if (!CanAddLowerPrecedenceMapping(derivedType, discriminator: null, defaultDerivedType, discriminatorSet, derivedTypeSet))
            {
                return;
            }

            defaultDerivedType ??= derivedType;
            derivedTypeSet.Add(derivedType);
        }

        void TryAddLowerPrecedenceDerivedType(ITypeSymbol derivedType, string discriminator)
        {
            if (!ValidateDerivedType(derivedType, discriminator))
            {
                return;
            }

            if (!CanAddLowerPrecedenceMapping(derivedType, discriminator, defaultDerivedType, discriminatorSet, derivedTypeSet))
            {
                return;
            }

            derivedTypeSet.Add(derivedType);
            discriminatorSet.Add(discriminator);
            derived.Add(new PolymorphicDerivedType(derivedType, discriminator));
        }

        bool ValidateDerivedType(ITypeSymbol derivedType, string? discriminator)
        {
            if (discriminator is not null && discriminator.Length == 0)
            {
                if (reportDiagnostics)
                {
                    context.ReportDiagnostic(Diagnostic.Create(
                        InvalidPolymorphismConfiguration,
                        model.ContextSymbol.Locations.FirstOrDefault(),
                        type.ToDisplayString(),
                        "Derived type discriminators cannot be empty."));
                }

                return false;
            }

            if (!IsAssignableTo(derivedType, type))
            {
                if (reportDiagnostics)
                {
                    context.ReportDiagnostic(Diagnostic.Create(
                        InvalidPolymorphismConfiguration,
                        model.ContextSymbol.Locations.FirstOrDefault(),
                        type.ToDisplayString(),
                        $"Derived type '{derivedType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}' is not assignable to base type '{type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}'."));
                }

                return false;
            }

            if (derivedType is INamedTypeSymbol derivedNamed && (derivedNamed.TypeKind != TypeKind.Class || derivedNamed.IsAbstract))
            {
                if (reportDiagnostics)
                {
                    context.ReportDiagnostic(Diagnostic.Create(
                        InvalidPolymorphismConfiguration,
                        model.ContextSymbol.Locations.FirstOrDefault(),
                        type.ToDisplayString(),
                        $"Derived type '{derivedType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}' must be a non-abstract class."));
                }

                return false;
            }

            return true;
        }
    }

    private static bool CanAddLowerPrecedenceMapping(
        ITypeSymbol derivedType,
        string? discriminator,
        ITypeSymbol? defaultDerivedType,
        HashSet<string> discriminatorSet,
        HashSet<ITypeSymbol> derivedTypeSet)
    {
        if (derivedTypeSet.Contains(derivedType))
        {
            return false;
        }

        if (discriminator is null)
        {
            return defaultDerivedType is null;
        }

        return !discriminatorSet.Contains(discriminator);
    }

    private static bool IsAssignableTo(ITypeSymbol derivedType, ITypeSymbol baseType)
    {
        if (SymbolEqualityComparer.Default.Equals(derivedType, baseType))
        {
            return true;
        }

        if (derivedType is not INamedTypeSymbol named)
        {
            return false;
        }

        for (var current = named.BaseType; current is not null; current = current.BaseType)
        {
            if (SymbolEqualityComparer.Default.Equals(current, baseType))
            {
                return true;
            }
        }

        foreach (var iface in named.AllInterfaces)
        {
            if (SymbolEqualityComparer.Default.Equals(iface, baseType))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsRequired(ISymbol member, bool honorRequiredModifier)
    {
        return (honorRequiredModifier && member switch
               {
                   IPropertySymbol property when property.IsRequired => true,
                   IFieldSymbol field when field.IsRequired => true,
                   _ => false,
               }) ||
               HasAttribute(member, "Meziantou.Framework.Toml.Serialization.TomlRequiredAttribute") ||
               HasAttribute(member, "System.Text.Json.Serialization.JsonRequiredAttribute");
    }

    private static bool IsExtensionData(ISymbol member)
    {
        return HasAttribute(member, "Meziantou.Framework.Toml.Serialization.TomlExtensionDataAttribute") ||
               HasAttribute(member, "System.Text.Json.Serialization.JsonExtensionDataAttribute");
    }

    private static bool TryGetExtensionDataValueType(ITypeSymbol type, out ITypeSymbol valueType)
    {
        valueType = null!;

        if (type is not INamedTypeSymbol named)
        {
            return false;
        }

        static bool IsStringDictionaryInterface(INamedTypeSymbol iface, out ITypeSymbol dictionaryValueType)
        {
            dictionaryValueType = null!;
            if (!iface.IsGenericType)
            {
                return false;
            }

            if (iface.ConstructedFrom.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) != "global::System.Collections.Generic.IDictionary<TKey, TValue>")
            {
                return false;
            }

            if (iface.TypeArguments[0].SpecialType != SpecialType.System_String)
            {
                return false;
            }

            dictionaryValueType = iface.TypeArguments[1];
            return true;
        }

        if (IsStringDictionaryInterface(named, out var directValueType))
        {
            valueType = directValueType;
            return true;
        }

        foreach (var iface in named.AllInterfaces)
        {
            if (IsStringDictionaryInterface(iface, out var ifaceValueType))
            {
                valueType = ifaceValueType;
                return true;
            }
        }

        return false;
    }

    private static bool TryGetExtensionDataCreateExpression(ITypeSymbol memberType, ITypeSymbol valueType, out string createExpression)
    {
        createExpression = null!;

        var memberTypeNoOuterNullability = memberType.WithNullableAnnotation(NullableAnnotation.NotAnnotated);
        var memberTypeNameNoNullability = memberTypeNoOuterNullability.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        var memberTypeName = memberTypeNoOuterNullability.ToDisplayString(FullyQualifiedNullableFormat);
        if (memberTypeNameNoNullability == "global::Meziantou.Framework.Toml.Model.TomlTable")
        {
            createExpression = "new global::Meziantou.Framework.Toml.Model.TomlTable()";
            return true;
        }

        if (memberTypeNoOuterNullability is not INamedTypeSymbol named)
        {
            return false;
        }

        if (named.TypeKind is (TypeKind.Class or TypeKind.Struct) &&
            !named.IsAbstract &&
            named.TypeKind != TypeKind.Interface)
        {
            if (named.TypeKind == TypeKind.Struct ||
                named.Constructors.Any(static c => c.Parameters.Length == 0 && c.DeclaredAccessibility == Accessibility.Public))
            {
                createExpression = "new " + memberTypeName + "()";
                return true;
            }
        }

        if (named.TypeKind == TypeKind.Interface && TryGetExtensionDataValueType(memberTypeNoOuterNullability, out _))
        {
            var valueTypeName = valueType.ToDisplayString(FullyQualifiedNullableFormat);
            createExpression = $"new global::System.Collections.Generic.Dictionary<string, {valueTypeName}>()";
            return true;
        }

        return false;
    }

    private static string GetConstructorParameterKeyName(string parameterName, string? namingPolicyExpression)
    {
        if (namingPolicyExpression is not null && TryConvertKnownName(parameterName, namingPolicyExpression, out var converted))
        {
            return converted;
        }

        return parameterName;
    }

    private static bool TryGetDefaultValueExpression(ITypeSymbol type, object? value, out string expression)
    {
        expression = null!;

        if (value is null)
        {
            if (type.IsReferenceType || TryGetNullableUnderlyingType(type, out _))
            {
                expression = "null";
                return true;
            }

            return false;
        }

        if (type.TypeKind == TypeKind.Enum)
        {
            var enumTypeName = type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            if (value is int i32)
            {
                expression = $"({enumTypeName}){i32.ToString(CultureInfo.InvariantCulture)}";
                return true;
            }

            if (value is byte b)
            {
                expression = $"({enumTypeName}){b.ToString(CultureInfo.InvariantCulture)}";
                return true;
            }

            if (value is short i16)
            {
                expression = $"({enumTypeName}){i16.ToString(CultureInfo.InvariantCulture)}";
                return true;
            }

            if (value is long i64)
            {
                expression = $"({enumTypeName}){i64.ToString(CultureInfo.InvariantCulture)}L";
                return true;
            }

            return false;
        }

        if (type.SpecialType == SpecialType.System_String && value is string s)
        {
            expression = $"\"{EscapeStringLiteral(s)}\"";
            return true;
        }

        if (type.SpecialType == SpecialType.System_Boolean && value is bool bval)
        {
            expression = bval ? "true" : "false";
            return true;
        }

        if (type.SpecialType == SpecialType.System_Char && value is char c)
        {
            expression = c switch
            {
                '\'' => "'\\''",
                '\\' => "'\\\\'",
                '\n' => "'\\n'",
                '\r' => "'\\r'",
                '\t' => "'\\t'",
                _ => c >= ' ' && c <= '~' ? $"'{c}'" : $"'\\u{((int)c).ToString("X4", CultureInfo.InvariantCulture)}'",
            };
            return true;
        }

        switch (value)
        {
            case sbyte sb:
                expression = sb.ToString(CultureInfo.InvariantCulture);
                return true;
            case byte bb:
                expression = bb.ToString(CultureInfo.InvariantCulture);
                return true;
            case short sh:
                expression = sh.ToString(CultureInfo.InvariantCulture);
                return true;
            case ushort ush:
                expression = ush.ToString(CultureInfo.InvariantCulture);
                return true;
            case int ii:
                expression = ii.ToString(CultureInfo.InvariantCulture);
                return true;
            case uint ui:
                expression = ui.ToString(CultureInfo.InvariantCulture) + "u";
                return true;
            case long ll:
                expression = ll.ToString(CultureInfo.InvariantCulture) + "L";
                return true;
            case ulong ull:
                expression = ull.ToString(CultureInfo.InvariantCulture) + "UL";
                return true;
            case float ff:
                if (float.IsNaN(ff))
                {
                    expression = "float.NaN";
                }
                else if (float.IsPositiveInfinity(ff))
                {
                    expression = "float.PositiveInfinity";
                }
                else if (float.IsNegativeInfinity(ff))
                {
                    expression = "float.NegativeInfinity";
                }
                else
                {
                    expression = ff.ToString("R", CultureInfo.InvariantCulture) + "f";
                }
                return true;
            case double dd:
                if (double.IsNaN(dd))
                {
                    expression = "double.NaN";
                }
                else if (double.IsPositiveInfinity(dd))
                {
                    expression = "double.PositiveInfinity";
                }
                else if (double.IsNegativeInfinity(dd))
                {
                    expression = "double.NegativeInfinity";
                }
                else
                {
                    expression = dd.ToString("R", CultureInfo.InvariantCulture);
                }
                return true;
            case decimal dec:
                expression = dec.ToString(CultureInfo.InvariantCulture) + "m";
                return true;
        }

        return false;
    }

    private static bool TryGetArrayElementType(ITypeSymbol type, out ITypeSymbol elementType)
    {
        if (type is IArrayTypeSymbol array && array.Rank == 1)
        {
            elementType = array.ElementType;
            return true;
        }

        elementType = null!;
        return false;
    }

    private enum SequenceKind
    {
        List = 0,
        ListBackedEnumerable = 1,
        MutableCollection = 2,
        HashSet = 3,
        HashSetBackedEnumerable = 4,
        ImmutableArray = 5,
        ImmutableList = 6,
        ImmutableHashSet = 7,
    }

    private static bool TryGetSequenceElementType(ITypeSymbol type, out ITypeSymbol elementType, out SequenceKind kind)
    {
        elementType = null!;
        kind = default;

        if (type is not INamedTypeSymbol named)
        {
            return false;
        }

        if (IsDictionaryLikeType(named))
        {
            return false;
        }

        if (named.IsGenericType)
        {
            var constructedFrom = named.ConstructedFrom.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            if (constructedFrom == "global::System.Collections.Generic.List<T>")
            {
                elementType = named.TypeArguments[0];
                kind = SequenceKind.List;
                return true;
            }

            if (constructedFrom == "global::System.Collections.Generic.HashSet<T>")
            {
                elementType = named.TypeArguments[0];
                kind = SequenceKind.HashSet;
                return true;
            }

            if (constructedFrom == "global::System.Collections.Generic.ISet<T>")
            {
                elementType = named.TypeArguments[0];
                kind = SequenceKind.HashSetBackedEnumerable;
                return true;
            }

            if (constructedFrom == "global::System.Collections.Immutable.ImmutableArray<T>")
            {
                elementType = named.TypeArguments[0];
                kind = SequenceKind.ImmutableArray;
                return true;
            }

            if (constructedFrom == "global::System.Collections.Immutable.ImmutableList<T>")
            {
                elementType = named.TypeArguments[0];
                kind = SequenceKind.ImmutableList;
                return true;
            }

            if (constructedFrom == "global::System.Collections.Immutable.ImmutableHashSet<T>")
            {
                elementType = named.TypeArguments[0];
                kind = SequenceKind.ImmutableHashSet;
                return true;
            }

            if (constructedFrom is
                "global::System.Collections.Generic.IEnumerable<T>" or
                "global::System.Collections.Generic.ICollection<T>" or
                "global::System.Collections.Generic.IReadOnlyCollection<T>" or
                "global::System.Collections.Generic.IList<T>" or
                "global::System.Collections.Generic.IReadOnlyList<T>")
            {
                elementType = named.TypeArguments[0];
                kind = SequenceKind.ListBackedEnumerable;
                return true;
            }
        }

        if (TryGetMutableCollectionElementType(named, out elementType))
        {
            kind = SequenceKind.MutableCollection;
            return true;
        }

        return false;
    }

    private static bool TryGetMutableCollectionElementType(INamedTypeSymbol type, out ITypeSymbol elementType)
    {
        elementType = null!;

        if (type.TypeKind != TypeKind.Class || type.IsAbstract || !HasPublicParameterlessConstructor(type))
        {
            return false;
        }

        ITypeSymbol? matchedElementType = null;
        foreach (var interfaceType in type.AllInterfaces)
        {
            if (!interfaceType.IsGenericType ||
                interfaceType.ConstructedFrom.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) != "global::System.Collections.Generic.ICollection<T>")
            {
                continue;
            }

            var currentElementType = interfaceType.TypeArguments[0];
            if (matchedElementType is not null && !SymbolEqualityComparer.Default.Equals(matchedElementType, currentElementType))
            {
                return false;
            }

            matchedElementType = currentElementType;
        }

        if (matchedElementType is null)
        {
            return false;
        }

        elementType = matchedElementType;
        return true;
    }

    private static bool HasPublicParameterlessConstructor(INamedTypeSymbol type)
    {
        foreach (var constructor in type.InstanceConstructors)
        {
            if (!constructor.IsStatic &&
                constructor.DeclaredAccessibility == Accessibility.Public &&
                constructor.Parameters.Length == 0)
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsDictionaryLikeType(INamedTypeSymbol type)
    {
        if (TryGetDictionaryKeyValueTypes(type, out _, out _))
        {
            return true;
        }

        foreach (var interfaceType in type.AllInterfaces)
        {
            if (!interfaceType.IsGenericType)
            {
                continue;
            }

            var constructedFrom = interfaceType.ConstructedFrom.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            if (constructedFrom is
                "global::System.Collections.Generic.IDictionary<TKey, TValue>" or
                "global::System.Collections.Generic.IReadOnlyDictionary<TKey, TValue>")
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryGetDictionaryKeyValueTypes(ITypeSymbol type, out ITypeSymbol keyType, out ITypeSymbol valueType)
    {
        keyType = null!;
        valueType = null!;

        if (type is not INamedTypeSymbol named)
        {
            return false;
        }

        if (!named.IsGenericType)
        {
            return TryGetConcreteDictionaryKeyValueTypes(named, out keyType, out valueType);
        }

        var constructedFrom = named.ConstructedFrom.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        if (constructedFrom is not
            "global::System.Collections.Generic.Dictionary<TKey, TValue>" and not
            "global::System.Collections.Generic.IDictionary<TKey, TValue>" and not
            "global::System.Collections.Generic.IReadOnlyDictionary<TKey, TValue>")
        {
            return TryGetConcreteDictionaryKeyValueTypes(named, out keyType, out valueType);
        }

        keyType = named.TypeArguments[0];
        valueType = named.TypeArguments[1];
        return true;
    }

    // A class implementing IDictionary<TKey, TValue> that the generated code can create, such as SortedDictionary<,>
    private static bool TryGetConcreteDictionaryKeyValueTypes(INamedTypeSymbol type, out ITypeSymbol keyType, out ITypeSymbol valueType)
    {
        keyType = null!;
        valueType = null!;
        if (type.TypeKind != TypeKind.Class || type.IsAbstract || !HasPublicParameterlessConstructor(type))
        {
            return false;
        }

        foreach (var interfaceType in type.AllInterfaces)
        {
            if (interfaceType.IsGenericType &&
                interfaceType.ConstructedFrom.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) == "global::System.Collections.Generic.IDictionary<TKey, TValue>")
            {
                keyType = interfaceType.TypeArguments[0];
                valueType = interfaceType.TypeArguments[1];
                return true;
            }
        }

        return false;
    }

    private static bool TryGetDictionaryValueType(ITypeSymbol type, out ITypeSymbol valueType)
    {
        valueType = null!;
        if (!TryGetDictionaryKeyValueTypes(type, out var keyType, out var innerValueType))
        {
            return false;
        }

        if (keyType.SpecialType != SpecialType.System_String)
        {
            return false;
        }

        valueType = innerValueType;
        return true;
    }

    // The converter declared with [TomlConverter] or [JsonConverter], resolved like the reflection resolver does
    private static DeclaredConverter? GetDeclaredConverter(ISymbol symbol, ITypeSymbol convertedType)
    {
        ITypeSymbol? converterType;
        if (TryGetAttribute(symbol, TomlConverterAttributeMetadataName, out var attribute))
        {
            converterType = attribute.ConstructorArguments.Length == 1 ? attribute.ConstructorArguments[0].Value as ITypeSymbol : null;
        }
        else if (TryGetAttribute(symbol, JsonConverterAttributeMetadataName, out attribute) &&
            attribute.ConstructorArguments.Length == 1 && attribute.ConstructorArguments[0].Value is ITypeSymbol jsonConverterType)
        {
            if (convertedType.TypeKind == TypeKind.Enum && IsJsonStringEnumConverter(jsonConverterType))
            {
                return new DeclaredConverter(jsonConverterType, isStringEnum: true, error: null);
            }

            converterType = jsonConverterType;
        }
        else
        {
            return null;
        }

        string? error = null;
        if (converterType is not INamedTypeSymbol { TypeKind: TypeKind.Class } named || !DerivesFrom(named, "global::" + TomlConverterMetadataName))
        {
            error = $"Converters must derive from {TomlConverterMetadataName}.";
        }
        else if (named.IsAbstract)
        {
            error = "Converters must not be abstract.";
        }
        else if (!HasPublicParameterlessConstructor(named))
        {
            error = "Converters must have a public parameterless constructor.";
        }
        else if (!IsTypeAccessibleFromGeneratedContext(named))
        {
            error = "Converters must be accessible from the generated context (public or internal).";
        }

        return new DeclaredConverter(converterType, isStringEnum: false, error);
    }

    private static bool IsTypeAccessibleFromGeneratedContext(INamedTypeSymbol type)
    {
        for (INamedTypeSymbol? current = type; current is not null; current = current.ContainingType)
        {
            if (current.DeclaredAccessibility is not (Accessibility.Public or Accessibility.Internal or Accessibility.ProtectedOrInternal) || current.IsFileLocal)
            {
                return false;
            }
        }

        return true;
    }

    private static bool HasJsonStringEnumConverterAttribute(ITypeSymbol type)
    {
        foreach (var attr in type.GetAttributes())
        {
            if (attr.AttributeClass?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) != "global::System.Text.Json.Serialization.JsonConverterAttribute" ||
                attr.ConstructorArguments.Length == 0 ||
                attr.ConstructorArguments[0].Kind != TypedConstantKind.Type ||
                attr.ConstructorArguments[0].Value is not ITypeSymbol converterType)
            {
                continue;
            }

            if (IsJsonStringEnumConverter(converterType))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsJsonStringEnumConverter(ITypeSymbol converterType)
    {
        if (converterType is not INamedTypeSymbol named)
        {
            return false;
        }

        var constructedFrom = named.IsGenericType
            ? named.ConstructedFrom.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
            : named.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        return constructedFrom is
            "global::System.Text.Json.Serialization.JsonStringEnumConverter" or
            "global::System.Text.Json.Serialization.JsonStringEnumConverter<TEnum>";
    }

    private static bool HasAttribute(ISymbol symbol, string attributeMetadataName)
        => TryGetAttribute(symbol, attributeMetadataName, out _);

    private static bool TryGetAttribute(ISymbol symbol, string attributeMetadataName, out AttributeData attribute)
    {
        foreach (var current in EnumerateSelfAndBaseSymbols(symbol))
        {
            foreach (var attr in current.GetAttributes())
            {
                if (attr.AttributeClass?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) == "global::" + attributeMetadataName)
                {
                    attribute = attr;
                    return true;
                }
            }
        }

        attribute = null!;
        return false;
    }

    private static IEnumerable<ISymbol> EnumerateSelfAndBaseSymbols(ISymbol symbol)
    {
        for (var current = symbol; current is not null; current = GetBaseSymbol(current))
        {
            yield return current;
        }
    }

    private static ISymbol? GetBaseSymbol(ISymbol symbol)
    {
        return symbol switch
        {
            IPropertySymbol property => property.OverriddenProperty,
            IMethodSymbol method => method.OverriddenMethod,
            IEventSymbol @event => @event.OverriddenEvent,
            INamedTypeSymbol named => named.BaseType,
            _ => null,
        };
    }

    private static bool ImplementsInterface(ITypeSymbol type, string interfaceMetadataName)
    {
        if (type is not INamedTypeSymbol named)
        {
            return false;
        }

        foreach (var iface in named.AllInterfaces)
        {
            if (iface.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) == "global::" + interfaceMetadataName)
            {
                return true;
            }
        }

        return false;
    }

    private static string GetSerializedName(ISymbol member, string memberName, string? namingPolicyExpression)
    {
        if (TryGetAttribute(member, "Meziantou.Framework.Toml.Serialization.TomlPropertyNameAttribute", out var tomlAttr) &&
            tomlAttr.ConstructorArguments.Length == 1 &&
            tomlAttr.ConstructorArguments[0].Value is string tomlName)
        {
            return tomlName;
        }

        if (TryGetAttribute(member, "System.Text.Json.Serialization.JsonPropertyNameAttribute", out var jsonAttr) &&
            jsonAttr.ConstructorArguments.Length == 1 &&
            jsonAttr.ConstructorArguments[0].Value is string jsonName)
        {
            return jsonName;
        }

        if (namingPolicyExpression is not null && TryConvertKnownName(memberName, namingPolicyExpression, out var converted))
        {
            return converted;
        }

        return memberName;
    }

    // [TomlObjectCreationHandling] takes precedence over [JsonObjectCreationHandling]
    private static ObjectCreationHandlingKind GetObjectCreationHandling(ISymbol symbol)
    {
        if (TryGetAttribute(symbol, TomlObjectCreationHandlingAttributeMetadataName, out var attr) ||
            TryGetAttribute(symbol, JsonObjectCreationHandlingAttributeMetadataName, out attr))
        {
            if (attr.ConstructorArguments.Length == 1 && attr.ConstructorArguments[0].Value is int constructorValue)
            {
                return constructorValue switch
                {
                    0 => ObjectCreationHandlingKind.Replace,
                    1 => ObjectCreationHandlingKind.Populate,
                    _ => ObjectCreationHandlingKind.Default,
                };
            }

            foreach (var namedArgument in attr.NamedArguments)
            {
                if (namedArgument.Key == "Handling" && namedArgument.Value.Value is int namedValue)
                {
                    return namedValue switch
                    {
                        0 => ObjectCreationHandlingKind.Replace,
                        1 => ObjectCreationHandlingKind.Populate,
                        _ => ObjectCreationHandlingKind.Default,
                    };
                }
            }
        }

        return ObjectCreationHandlingKind.Default;
    }

    private static int GetOrder(ISymbol member)
    {
        if (TryGetAttribute(member, "Meziantou.Framework.Toml.Serialization.TomlPropertyOrderAttribute", out var tomlAttr) &&
            tomlAttr.ConstructorArguments.Length == 1 &&
            tomlAttr.ConstructorArguments[0].Value is int tomlOrder)
        {
            return tomlOrder;
        }

        if (TryGetAttribute(member, "System.Text.Json.Serialization.JsonPropertyOrderAttribute", out var jsonAttr) &&
            jsonAttr.ConstructorArguments.Length == 1 &&
            jsonAttr.ConstructorArguments[0].Value is int jsonOrder)
        {
            return jsonOrder;
        }

        return 0;
    }

    private static (int? TableArrayStyle, int? InlineTablePolicy, int? StringStyle, bool? PreferLiteralWhenNoEscapes, bool? AllowHexEscapes) GetFormattingMetadata(ISymbol member)
    {
        int? tableArrayStyle = null;
        int? inlineTablePolicy = null;
        int? stringStyle = null;
        bool? preferLiteralWhenNoEscapes = null;
        bool? allowHexEscapes = null;

        if (TryGetAttribute(member, "Meziantou.Framework.Toml.Serialization.TomlTableArrayStyleAttribute", out var tableArrayStyleAttr) &&
            tableArrayStyleAttr.ConstructorArguments.Length == 1 &&
            tableArrayStyleAttr.ConstructorArguments[0].Value is int tableArrayStyleValue)
        {
            tableArrayStyle = tableArrayStyleValue;
        }

        if (TryGetAttribute(member, "Meziantou.Framework.Toml.Serialization.TomlInlineTableAttribute", out var inlineTableAttr) &&
            inlineTableAttr.ConstructorArguments.Length == 1 &&
            inlineTableAttr.ConstructorArguments[0].Value is int inlineTablePolicyValue)
        {
            inlineTablePolicy = inlineTablePolicyValue;
        }

        if (TryGetAttribute(member, "Meziantou.Framework.Toml.Serialization.TomlStringStyleAttribute", out var stringStyleAttr) &&
            stringStyleAttr.ConstructorArguments.Length == 1 &&
            stringStyleAttr.ConstructorArguments[0].Value is int stringStyleValue)
        {
            stringStyle = stringStyleValue;

            foreach (var namedArgument in stringStyleAttr.NamedArguments)
            {
                if (namedArgument.Value.Value is not int preference)
                {
                    continue;
                }

                var boolValue = preference switch
                {
                    1 => true,
                    2 => false,
                    _ => (bool?)null,
                };

                switch (namedArgument.Key)
                {
                    case "PreferLiteralWhenNoEscapes":
                        preferLiteralWhenNoEscapes = boolValue;
                        break;
                    case "AllowHexEscapes":
                        allowHexEscapes = boolValue;
                        break;
                }
            }
        }

        return (tableArrayStyle, inlineTablePolicy, stringStyle, preferLiteralWhenNoEscapes, allowHexEscapes);
    }

    private static int? GetTypeLevelMappingOrder(ITypeSymbol type)
        => TryGetAttribute(type, "Meziantou.Framework.Toml.Serialization.TomlMappingOrderAttribute", out var attr) &&
           attr.ConstructorArguments.Length == 1 &&
           attr.ConstructorArguments[0].Value is int value
            ? value
            : null;

    private static int? GetTypeLevelDottedKeyHandling(ITypeSymbol type)
        => TryGetAttribute(type, "Meziantou.Framework.Toml.Serialization.TomlDottedKeyHandlingAttribute", out var attr) &&
           attr.ConstructorArguments.Length == 1 &&
           attr.ConstructorArguments[0].Value is int value
            ? value
            : null;

    private static bool TryConvertKnownName(string name, string namingPolicyExpression, out string converted)
    {
        TomlNamingPolicy? policy = namingPolicyExpression switch
        {
            "global::Meziantou.Framework.Toml.TomlNamingPolicy.CamelCase" => TomlNamingPolicy.CamelCase,
            "global::Meziantou.Framework.Toml.TomlNamingPolicy.SnakeCaseLower" => TomlNamingPolicy.SnakeCaseLower,
            "global::Meziantou.Framework.Toml.TomlNamingPolicy.SnakeCaseUpper" => TomlNamingPolicy.SnakeCaseUpper,
            "global::Meziantou.Framework.Toml.TomlNamingPolicy.KebabCaseLower" => TomlNamingPolicy.KebabCaseLower,
            "global::Meziantou.Framework.Toml.TomlNamingPolicy.KebabCaseUpper" => TomlNamingPolicy.KebabCaseUpper,
            "global::Meziantou.Framework.Toml.TomlNamingPolicy.PascalCase" => TomlNamingPolicy.PascalCase,
            _ => null,
        };

        if (policy is null)
        {
            converted = string.Empty;
            return false;
        }

        converted = policy.ConvertName(name);
        return true;
    }

    private static IgnoreBehavior GetIgnoreBehavior(ISymbol symbol)
    {
        if (TryGetAttribute(symbol, "Meziantou.Framework.Toml.Serialization.TomlIgnoreAttribute", out var tomlAttr))
        {
            var toml = TomlIgnoreAttributeModel.From(tomlAttr);
            return toml.Condition switch
            {
                0 => new IgnoreBehavior(ignoreAlways: false, ignoreOnRead: false, writeIgnore: WriteIgnoreKind.None),
                1 => new IgnoreBehavior(ignoreAlways: false, ignoreOnRead: false, writeIgnore: WriteIgnoreKind.WhenWritingNull),
                2 => new IgnoreBehavior(ignoreAlways: false, ignoreOnRead: false, writeIgnore: WriteIgnoreKind.WhenWritingDefault),
                4 => new IgnoreBehavior(ignoreAlways: false, ignoreOnRead: false, writeIgnore: WriteIgnoreKind.WhenWriting),
                5 => new IgnoreBehavior(ignoreAlways: false, ignoreOnRead: true, writeIgnore: WriteIgnoreKind.None),
                _ => new IgnoreBehavior(ignoreAlways: true, ignoreOnRead: false, writeIgnore: WriteIgnoreKind.None),
            };
        }

        if (TryGetAttribute(symbol, "System.Text.Json.Serialization.JsonIgnoreAttribute", out var jsonAttr))
        {
            var condition = JsonIgnoreAttributeModel.From(jsonAttr).Condition ?? 1;
            return condition switch
            {
                0 => new IgnoreBehavior(ignoreAlways: false, ignoreOnRead: false, writeIgnore: WriteIgnoreKind.None),
                1 => new IgnoreBehavior(ignoreAlways: true, ignoreOnRead: false, writeIgnore: WriteIgnoreKind.None),
                2 => new IgnoreBehavior(ignoreAlways: false, ignoreOnRead: false, writeIgnore: WriteIgnoreKind.WhenWritingDefault),
                3 => new IgnoreBehavior(ignoreAlways: false, ignoreOnRead: false, writeIgnore: WriteIgnoreKind.WhenWritingNull),
                4 => new IgnoreBehavior(ignoreAlways: false, ignoreOnRead: false, writeIgnore: WriteIgnoreKind.WhenWriting),
                5 => new IgnoreBehavior(ignoreAlways: false, ignoreOnRead: true, writeIgnore: WriteIgnoreKind.None),
                _ => new IgnoreBehavior(ignoreAlways: false, ignoreOnRead: false, writeIgnore: WriteIgnoreKind.None),
            };
        }

        return new IgnoreBehavior(ignoreAlways: false, ignoreOnRead: false, writeIgnore: WriteIgnoreKind.None);
    }

    private readonly struct TomlIgnoreAttributeModel
    {
        public TomlIgnoreAttributeModel(int condition)
        {
            Condition = condition;
        }

        public int Condition { get; }

        public static TomlIgnoreAttributeModel From(AttributeData attribute)
        {
            foreach (var namedArg in attribute.NamedArguments)
            {
                if (namedArg.Key == "Condition" && namedArg.Value.Value is int value)
                {
                    return new TomlIgnoreAttributeModel(value);
                }
            }

            // Default: Always.
            return new TomlIgnoreAttributeModel(3);
        }
    }

    private readonly struct JsonIgnoreAttributeModel
    {
        public JsonIgnoreAttributeModel(int? condition)
        {
            Condition = condition;
        }

        public int? Condition { get; }

        public static JsonIgnoreAttributeModel From(AttributeData attribute)
        {
            foreach (var namedArg in attribute.NamedArguments)
            {
                if (namedArg.Key == "Condition" && namedArg.Value.Value is int value)
                {
                    return new JsonIgnoreAttributeModel(value);
                }
            }

            // Default: Always.
            return new JsonIgnoreAttributeModel(null);
        }
    }

    private static bool IsBuiltInType(ITypeSymbol type)
    {
        if (type.TypeKind == TypeKind.Enum)
        {
            return true;
        }

        switch (type.SpecialType)
        {
            case SpecialType.System_Char:
            case SpecialType.System_String:
            case SpecialType.System_Boolean:
            case SpecialType.System_SByte:
            case SpecialType.System_Byte:
            case SpecialType.System_Int16:
            case SpecialType.System_UInt16:
            case SpecialType.System_Int32:
            case SpecialType.System_UInt32:
            case SpecialType.System_Int64:
            case SpecialType.System_UInt64:
            case SpecialType.System_IntPtr:
            case SpecialType.System_UIntPtr:
            case SpecialType.System_Single:
            case SpecialType.System_Double:
            case SpecialType.System_Decimal:
            case SpecialType.System_DateTime:
            case SpecialType.System_Object:
                return true;
        }

        var metadataName = type is INamedTypeSymbol named
            ? named.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
            : type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

        return metadataName is
            "global::System.DateTimeOffset" or
            "global::System.Guid" or
            "global::System.TimeSpan" or
            "global::System.Uri" or
            "global::System.Version" or
            "global::System.Half" or
            "global::System.Int128" or
            "global::System.UInt128" or
            "global::Meziantou.Framework.Toml.TomlDateTime" or
            "global::System.DateOnly" or
            "global::System.TimeOnly" or
            "global::Meziantou.Framework.Toml.Model.TomlObject" or
            "global::Meziantou.Framework.Toml.Model.TomlTable" or
            "global::Meziantou.Framework.Toml.Model.TomlArray" or
            "global::Meziantou.Framework.Toml.Model.TomlTableArray";
    }

    // The unique name of the type info property of each type of the context being emitted. The generated members of a
    // type (the property, its backing field, its Create method and its type info class) derive from this name.
    [ThreadStatic]
    private static Dictionary<ITypeSymbol, string>? s_typeInfoNames;

    private static string GetTypeInfoPropertyName(ITypeSymbol type)
        => s_typeInfoNames is not null && s_typeInfoNames.TryGetValue(type, out var name) ? name : GetSimpleTypeInfoName(type);

    private static string GetSimpleTypeInfoName(ITypeSymbol type)
    {
        if (type is IArrayTypeSymbol array)
        {
            var suffix = array.Rank == 1
                ? "Array"
                : "Array" + array.Rank.ToString(CultureInfo.InvariantCulture);

            return SanitizeIdentifier(GetSimpleTypeInfoName(array.ElementType) + suffix);
        }

        if (type is INamedTypeSymbol named && named.IsGenericType)
        {
            var baseName = named.Name;
            var args = string.Concat(named.TypeArguments.Select(static a => GetSimpleTypeInfoName(a)));
            return SanitizeIdentifier(baseName + args);
        }

        return SanitizeIdentifier(type.Name);
    }

    private static Dictionary<ITypeSymbol, string> CreateTypeInfoNames(ContextModel model, ImmutableArray<ITypeSymbol> types)
    {
        var names = new Dictionary<ITypeSymbol, string>(SymbolEqualityComparer.Default);
        var usedIdentifiers = new HashSet<string>(StringComparer.Ordinal)
        {
            model.TypeName,
            "Default",
            "Options",
            "GetTypeInfo",
            "CreateDefaultOptions",
            "s_sourceGenerationConverterTypes",
        };

        // The members inherited from TomlSerializerContext and object, and the members declared by the user
        for (var current = model.ContextSymbol; current is not null; current = current.BaseType)
        {
            foreach (var member in current.GetMembers())
            {
                usedIdentifiers.Add(member.Name);
            }
        }

        // Explicit names first, then the roots keep their simple name when possible, then the other types
        foreach (var root in model.RootTypes)
        {
            if (root.TypeInfoPropertyName is { } customName && !names.ContainsKey(root.Type))
            {
                Assign(root.Type, customName);
            }
        }

        foreach (var root in model.RootTypes)
        {
            AssignUnique(root.Type);
        }

        foreach (var type in types)
        {
            AssignUnique(type);
        }

        return names;

        void AssignUnique(ITypeSymbol type)
        {
            if (names.ContainsKey(type))
            {
                return;
            }

            var simpleName = GetSimpleTypeInfoName(type);
            if (IsAvailable(simpleName))
            {
                Assign(type, simpleName);
                return;
            }

            var qualifiedName = SanitizeIdentifier(type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat).Replace("global::", "", StringComparison.Ordinal).Replace('.', '_'));
            if (IsAvailable(qualifiedName))
            {
                Assign(type, qualifiedName);
                return;
            }

            for (var suffix = 2; ; suffix++)
            {
                var candidate = qualifiedName + suffix.ToString(CultureInfo.InvariantCulture);
                if (IsAvailable(candidate))
                {
                    Assign(type, candidate);
                    return;
                }
            }
        }

        bool IsAvailable(string name)
            => !usedIdentifiers.Contains(name) &&
               !usedIdentifiers.Contains("_" + name) &&
               !usedIdentifiers.Contains("Create" + name) &&
               !usedIdentifiers.Contains("__TomlTypeInfo_" + name);

        void Assign(ITypeSymbol type, string name)
        {
            names[type] = name;
            usedIdentifiers.Add(name);
            usedIdentifiers.Add("_" + name);
            usedIdentifiers.Add("Create" + name);
            usedIdentifiers.Add("__TomlTypeInfo_" + name);
        }
    }

    private static string GetTypeInfoAccess(ITypeSymbol type)
        => "GetTypeInfo<" + type.ToDisplayString(FullyQualifiedNullableFormat) + ">(_context." + GetTypeInfoPropertyName(type) + ")";

    private static string GetTypeInfoReadExpression(ITypeSymbol type)
        => "(" + type.ToDisplayString(FullyQualifiedNullableFormat) + ")" + GetTypeInfoAccess(type) + ".ReadAsObject(reader)!";

    private const string AllowNullAttributeMetadataName = "System.Diagnostics.CodeAnalysis.AllowNullAttribute";
    private const string MaybeNullAttributeMetadataName = "System.Diagnostics.CodeAnalysis.MaybeNullAttribute";

    // Only reference types annotated as non-nullable are enforced, like the reflection resolver
    private static bool IsNonNullableReferenceType(ITypeSymbol type)
        => type.IsReferenceType && type.NullableAnnotation == NullableAnnotation.NotAnnotated;

    private static string EscapeInterpolatedStringLiteral(string value)
        => EscapeStringLiteral(value).Replace("{", "{{", StringComparison.Ordinal).Replace("}", "}}", StringComparison.Ordinal);

    // Reads a member value, rejecting null when the member is declared as non-nullable
    private static string GetMemberTypeInfoAccess(PocoMember member)
        => member.ConverterTypeInfoName ?? GetTypeInfoAccess(member.Type);

    private static string GetMemberReadExpression(PocoMember member)
    {
        if (!member.DisallowNullOnDeserialize)
        {
            return "(" + member.Type.ToDisplayString(FullyQualifiedNullableFormat) + ")" + GetMemberTypeInfoAccess(member) + ".ReadAsObject(reader)!";
        }

        return "((" + member.Type.ToDisplayString(FullyQualifiedNullableFormat) + "?)" + GetMemberTypeInfoAccess(member) + ".ReadAsObject(reader) ?? throw reader.CreateException($\"The TOML key '" +
            EscapeInterpolatedStringLiteral(member.SerializedName) + "' cannot be null because '{typeof(" + member.OwnerTypeName + ").FullName}' declares it as non-nullable.\"))";
    }

    private static string GetConstructorParameterReadExpression(PocoConstructorParameter parameter, string typeName)
    {
        var typeInfoAccess = parameter.ConverterTypeInfoName ?? GetTypeInfoAccess(parameter.ParameterType);
        if (!parameter.DisallowNull)
        {
            return "(" + parameter.ParameterType.ToDisplayString(FullyQualifiedNullableFormat) + ")" + typeInfoAccess + ".ReadAsObject(reader)!";
        }

        return "((" + parameter.ParameterType.ToDisplayString(FullyQualifiedNullableFormat) + "?)" + typeInfoAccess + ".ReadAsObject(reader) ?? throw reader.CreateException($\"The constructor parameter '" +
            EscapeInterpolatedStringLiteral(parameter.ParameterName) + "' on '{typeof(" + typeName + ").FullName}' cannot be null because it is declared as non-nullable.\"))";
    }

    // Emits the statement handling a key that matches no member. The key must be in the "name" local.
    private static void EmitUnmappedMember(StringBuilder builder, string indent, bool disallow, string typeName)
    {
        if (disallow)
        {
            builder.Append(indent).Append("throw reader.CreateException($\"The TOML key '{name}' could not be mapped to '{typeof(").Append(typeName).AppendLine(").FullName}'.\");");
            return;
        }

        builder.Append(indent).AppendLine("reader.Skip();");
    }

    // [TomlUnmappedMemberHandling] takes precedence over [JsonUnmappedMemberHandling]; both apply to the declaring type only
    private static int GetUnmappedMemberHandling(INamedTypeSymbol type, SourceGenOptions options)
    {
        foreach (var metadataName in new[] { "Meziantou.Framework.Toml.Serialization.TomlUnmappedMemberHandlingAttribute", "System.Text.Json.Serialization.JsonUnmappedMemberHandlingAttribute" })
        {
            foreach (var attribute in type.GetAttributes())
            {
                if (attribute.AttributeClass?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) == "global::" + metadataName &&
                    attribute.ConstructorArguments.Length == 1 &&
                    attribute.ConstructorArguments[0].Value is int value)
                {
                    return value;
                }
            }
        }

        return options.UnmappedMemberHandling ?? 0;
    }

    private static ImmutableArray<ITypeSymbol> GetPocoRuntimeResolvedTypes(PocoShape poco)
    {
        var types = ImmutableArray.CreateBuilder<ITypeSymbol>();
        foreach (var member in poco.Members)
        {
            if (member.ConverterTypeInfoName is null)
            {
                AddIfMissing(types, member.Type);
            }
        }

        if (poco.ExtensionData is { } extensionData)
        {
            AddIfMissing(types, extensionData.ValueType);
        }

        if (poco.Constructor is { } constructor)
        {
            foreach (var parameter in constructor.Parameters)
            {
                if (parameter.ConverterTypeInfoName is null)
                {
                    AddIfMissing(types, parameter.ParameterType);
                }
            }
        }

        return types.ToImmutable();

        static void AddIfMissing(ImmutableArray<ITypeSymbol>.Builder types, ITypeSymbol type)
        {
            foreach (var existing in types)
            {
                if (SymbolEqualityComparer.Default.Equals(existing, type))
                {
                    return;
                }
            }

            types.Add(type);
        }
    }

    private static bool TryGetCustomTypeInfoPropertyName(ContextModel model, ITypeSymbol type, out string propertyName)
    {
        foreach (var root in model.RootTypes)
        {
            if (SymbolEqualityComparer.Default.Equals(root.Type, type) && root.TypeInfoPropertyName is not null)
            {
                propertyName = root.TypeInfoPropertyName;
                return true;
            }
        }

        propertyName = null!;
        return false;
    }

    private static string SanitizeIdentifier(string name)
    {
        var builder = new StringBuilder(name.Length);
        for (var i = 0; i < name.Length; i++)
        {
            var c = name[i];
            if (char.IsLetterOrDigit(c) || c == '_')
            {
                builder.Append(c);
            }
        }

        if (builder.Length == 0)
        {
            return "Type";
        }

        if (!char.IsLetter(builder[0]) && builder[0] != '_')
        {
            builder.Insert(0, '_');
        }

        return builder.ToString();
    }

    private static bool DerivesFromTomlSerializerContext(INamedTypeSymbol symbol)
    {
        for (var current = symbol; current is not null; current = current.BaseType)
        {
            if (current.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) == "global::" + TomlSerializerContextMetadataName)
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsJsonSerializableAttribute(AttributeData attribute)
        => attribute.AttributeClass?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) == "global::" + JsonSerializableAttributeMetadataName;

    private static bool IsTomlSerializableAttribute(AttributeData attribute)
        => attribute.AttributeClass?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) == "global::" + TomlSerializableAttributeMetadataName;

    private static bool TryCreateDerivedTypeMappingModel(AttributeData attribute, out DerivedTypeMappingModel model)
    {
        model = null!;

        if (attribute.AttributeClass?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) != "global::" + TomlDerivedTypeMappingAttributeMetadataName)
        {
            return false;
        }

        if (attribute.ConstructorArguments.Length < 2 ||
            attribute.ConstructorArguments[0].Kind != TypedConstantKind.Type ||
            attribute.ConstructorArguments[0].Value is not ITypeSymbol baseType ||
            attribute.ConstructorArguments[1].Kind != TypedConstantKind.Type ||
            attribute.ConstructorArguments[1].Value is not ITypeSymbol derivedType)
        {
            return false;
        }

        string? discriminator = null;
        if (attribute.ConstructorArguments.Length >= 3)
        {
            discriminator = attribute.ConstructorArguments[2].Value switch
            {
                string s => s,
                int i => i.ToString(CultureInfo.InvariantCulture),
                IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
                _ => attribute.ConstructorArguments[2].Value?.ToString(),
            };
        }

        model = new DerivedTypeMappingModel(baseType, derivedType, discriminator, attribute.ApplicationSyntaxReference?.GetSyntax().GetLocation());
        return true;
    }

    private static string? GetTypeInfoPropertyNameOverride(AttributeData attribute)
    {
        foreach (var namedArgument in attribute.NamedArguments)
        {
            if (namedArgument.Key == "TypeInfoPropertyName" && namedArgument.Value.Value is string propertyName)
            {
                return propertyName;
            }
        }

        return null;
    }

    private static bool IsJsonSourceGenerationOptionsAttribute(AttributeData attribute)
        => attribute.AttributeClass?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) == "global::" + JsonSourceGenerationOptionsAttributeMetadataName;

    private static bool IsTomlSourceGenerationOptionsAttribute(AttributeData attribute)
        => attribute.AttributeClass?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) == "global::" + TomlSourceGenerationOptionsAttributeMetadataName;

    private static ImmutableArray<DerivedTypeMappingModel> ValidateDerivedTypeMappings(SourceProductionContext context, ContextModel model)
    {
        var builder = ImmutableArray.CreateBuilder<DerivedTypeMappingModel>(model.DerivedTypeMappings.Length);
        var warnedBaseTypes = new HashSet<ITypeSymbol>(SymbolEqualityComparer.Default);

        foreach (var mapping in model.DerivedTypeMappings)
        {
            if (mapping.Discriminator is { Length: 0 })
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    InvalidDerivedTypeMapping,
                    mapping.Location ?? model.ContextSymbol.Locations.FirstOrDefault(),
                    model.ContextSymbol.ToDisplayString(),
                    $"Derived type mapping from '{mapping.BaseType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)}' to '{mapping.DerivedType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)}' must use a non-empty discriminator."));
                continue;
            }

            if (!IsAssignableTo(mapping.DerivedType, mapping.BaseType))
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    InvalidDerivedTypeMapping,
                    mapping.Location ?? model.ContextSymbol.Locations.FirstOrDefault(),
                    model.ContextSymbol.ToDisplayString(),
                    $"Type '{mapping.DerivedType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)}' is not assignable to base type '{mapping.BaseType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)}'."));
                continue;
            }

            if (mapping.DerivedType is not INamedTypeSymbol derivedType ||
                derivedType.TypeKind != TypeKind.Class ||
                derivedType.IsAbstract)
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    InvalidDerivedTypeMapping,
                    mapping.Location ?? model.ContextSymbol.Locations.FirstOrDefault(),
                    model.ContextSymbol.ToDisplayString(),
                    $"Derived type '{mapping.DerivedType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)}' must be a non-abstract class."));
                continue;
            }

            if (warnedBaseTypes.Add(mapping.BaseType) &&
                mapping.BaseType is INamedTypeSymbol baseType &&
                !HasPolymorphismAttributes(baseType))
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    MissingPolymorphicConfigurationOnDerivedTypeMappingBase,
                    mapping.Location ?? model.ContextSymbol.Locations.FirstOrDefault(),
                    model.ContextSymbol.ToDisplayString(),
                    mapping.BaseType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)));
            }

            builder.Add(mapping);
        }

        return builder.ToImmutable();
    }

    private static void ApplyJsonSourceGenerationOptionsAttribute(AttributeData attribute, SourceGenOptions options)
    {
        foreach (var namedArgument in attribute.NamedArguments)
        {
            var name = namedArgument.Key;
            var value = namedArgument.Value;
            switch (name)
            {
                case "PropertyNamingPolicy":
                    options.PropertyNamingPolicyExpression ??= ToNamingPolicyExpression(value);
                    break;
                case "DictionaryKeyPolicy":
                    options.DictionaryKeyPolicyExpression ??= ToNamingPolicyExpression(value);
                    break;
                case "PreferredObjectCreationHandling":
                    if (options.PreferredObjectCreationHandling is null && value.Value is int objectCreationHandling) options.PreferredObjectCreationHandling = objectCreationHandling;
                    break;
                case "PropertyNameCaseInsensitive":
                    if (options.PropertyNameCaseInsensitive is null && value.Value is bool pnci) options.PropertyNameCaseInsensitive = pnci;
                    break;
                case "IncludeFields":
                    if (options.IncludeFields is null && value.Value is bool includeFields) options.IncludeFields = includeFields;
                    break;
                case "IgnoreReadOnlyFields":
                    if (options.IgnoreReadOnlyFields is null && value.Value is bool ignoreReadOnlyFields) options.IgnoreReadOnlyFields = ignoreReadOnlyFields;
                    break;
                case "IgnoreReadOnlyProperties":
                    if (options.IgnoreReadOnlyProperties is null && value.Value is bool ignoreReadOnlyProperties) options.IgnoreReadOnlyProperties = ignoreReadOnlyProperties;
                    break;
                case "RespectRequiredConstructorParameters":
                    if (options.RespectRequiredConstructorParameters is null && value.Value is bool respectRequiredConstructorParameters) options.RespectRequiredConstructorParameters = respectRequiredConstructorParameters;
                    break;
                case "RespectNullableAnnotations":
                    if (options.RespectNullableAnnotations is null && value.Value is bool respectNullableAnnotations) options.RespectNullableAnnotations = respectNullableAnnotations;
                    break;
                case "UnmappedMemberHandling":
                    // JsonUnmappedMemberHandling uses the same values as TomlUnmappedMemberHandling
                    if (options.UnmappedMemberHandling is null && value.Value is int unmappedMemberHandling) options.UnmappedMemberHandling = unmappedMemberHandling;
                    break;
            }
        }
    }

    private static void ApplyTomlSourceGenerationOptionsAttribute(AttributeData attribute, SourceGenOptions options)
    {
        foreach (var namedArgument in attribute.NamedArguments)
        {
            var name = namedArgument.Key;
            var value = namedArgument.Value;
            switch (name)
            {
                case "WriteIndented":
                    if (value.Value is bool wi) options.WriteIndented = wi;
                    break;
                case "IndentSize":
                    if (value.Value is int indent) options.IndentSize = indent;
                    break;
                case "NewLine":
                    if (value.Value is int nl) options.NewLine = nl;
                    break;
                case "PropertyNameCaseInsensitive":
                    if (value.Value is bool pnci) options.PropertyNameCaseInsensitive = pnci;
                    break;
                case "IncludeFields":
                    if (value.Value is bool includeFields) options.IncludeFields = includeFields;
                    break;
                case "IgnoreReadOnlyFields":
                    if (value.Value is bool ignoreReadOnlyFields) options.IgnoreReadOnlyFields = ignoreReadOnlyFields;
                    break;
                case "IgnoreReadOnlyProperties":
                    if (value.Value is bool ignoreReadOnlyProperties) options.IgnoreReadOnlyProperties = ignoreReadOnlyProperties;
                    break;
                case "RespectRequiredConstructorParameters":
                    if (value.Value is bool respectRequiredConstructorParameters) options.RespectRequiredConstructorParameters = respectRequiredConstructorParameters;
                    break;
                case "RespectNullableAnnotations":
                    if (value.Value is bool respectNullableAnnotations) options.RespectNullableAnnotations = respectNullableAnnotations;
                    break;
                case "UnmappedMemberHandling":
                    if (value.Value is int unmappedMemberHandling) options.UnmappedMemberHandling = unmappedMemberHandling;
                    break;
                case "PropertyNamingPolicy":
                    options.PropertyNamingPolicyExpression = ToNamingPolicyExpression(value);
                    break;
                case "DictionaryKeyPolicy":
                    options.DictionaryKeyPolicyExpression = ToNamingPolicyExpression(value);
                    break;
                case "PreferredObjectCreationHandling":
                    if (value.Value is int preferredObjectCreationHandling) options.PreferredObjectCreationHandling = preferredObjectCreationHandling;
                    break;
                case "DefaultIgnoreCondition":
                    if (value.Value is int dic) options.DefaultIgnoreCondition = dic;
                    break;
                case "DuplicateKeyHandling":
                    if (value.Value is int dkh) options.DuplicateKeyHandling = dkh;
                    break;
                case "MaxDepth":
                    if (value.Value is int maxDepth) options.MaxDepth = maxDepth;
                    break;
                case "MappingOrder":
                    if (value.Value is int mappingOrder) options.MappingOrder = mappingOrder;
                    break;
                case "DottedKeyHandling":
                    if (value.Value is int dottedKeyHandling) options.DottedKeyHandling = dottedKeyHandling;
                    break;
                case "RootValueHandling":
                    if (value.Value is int rootValueHandling) options.RootValueHandling = rootValueHandling;
                    break;
                case "RootValueKeyName":
                    if (value.Value is string rootValueKeyName) options.RootValueKeyName = rootValueKeyName;
                    break;
                case "InlineTablePolicy":
                    if (value.Value is int inlineTablePolicy) options.InlineTablePolicy = inlineTablePolicy;
                    break;
                case "TableArrayStyle":
                    if (value.Value is int tableArrayStyle) options.TableArrayStyle = tableArrayStyle;
                    break;
                case "Converters":
                    if (value.Kind == TypedConstantKind.Array && !value.Values.IsDefault)
                    {
                        var converterTypes = ImmutableArray.CreateBuilder<ITypeSymbol>();
                        foreach (var item in value.Values)
                        {
                            if (item.Kind == TypedConstantKind.Type && item.Value is ITypeSymbol typeSymbol)
                            {
                                converterTypes.Add(typeSymbol);
                            }
                        }

                        options.ConverterTypes = converterTypes.ToImmutable();
                    }
                    break;
            }
        }
    }

    private static void ValidateConverters(SourceProductionContext context, ContextModel model)
    {
        if (model.Options.ConverterTypes.IsDefaultOrEmpty)
        {
            return;
        }

        foreach (var type in model.Options.ConverterTypes)
        {
            if (type is not INamedTypeSymbol named)
            {
                continue;
            }

            if (named.TypeKind != TypeKind.Class)
            {
                context.ReportDiagnostic(Diagnostic.Create(InvalidConverterType, model.ContextSymbol.Locations.FirstOrDefault(), type.ToDisplayString(), "Converters must be classes."));
                continue;
            }

            if (named.IsAbstract)
            {
                context.ReportDiagnostic(Diagnostic.Create(InvalidConverterType, model.ContextSymbol.Locations.FirstOrDefault(), type.ToDisplayString(), "Converters must not be abstract."));
                continue;
            }

            if (!DerivesFrom(named, "global::" + TomlConverterMetadataName))
            {
                context.ReportDiagnostic(Diagnostic.Create(InvalidConverterType, model.ContextSymbol.Locations.FirstOrDefault(), type.ToDisplayString(), $"Converters must derive from {TomlConverterMetadataName}."));
                continue;
            }

            if (!named.Constructors.Any(static c => c.Parameters.Length == 0 && c.DeclaredAccessibility == Accessibility.Public))
            {
                context.ReportDiagnostic(Diagnostic.Create(InvalidConverterType, model.ContextSymbol.Locations.FirstOrDefault(), type.ToDisplayString(), "Converters must have a public parameterless constructor."));
                continue;
            }

            if (DerivesFrom(named, "global::" + TomlConverterFactoryMetadataName))
            {
                context.ReportDiagnostic(Diagnostic.Create(UnusedConverterFactory, model.ContextSymbol.Locations.FirstOrDefault(), type.ToDisplayString()));
            }
        }
    }

    private static void ValidateSourceGenerationOptions(SourceProductionContext context, ContextModel model)
    {
        if (model.Options.IndentSize is not null && model.Options.IndentSize.Value < 1)
        {
            context.ReportDiagnostic(Diagnostic.Create(
                InvalidSourceGenerationOption,
                model.ContextSymbol.Locations.FirstOrDefault(),
                model.ContextSymbol.ToDisplayString(),
                "IndentSize must be at least 1."));
            model.Options.IndentSize = null;
        }

        ValidateEnumOption(context, model, "PreferredObjectCreationHandling", model.Options.PreferredObjectCreationHandling, TryGetObjectCreationHandlingExpression, v => model.Options.PreferredObjectCreationHandling = v);
        ValidateEnumOption(context, model, "NewLine", model.Options.NewLine, TryGetTomlNewLineKindExpression, v => model.Options.NewLine = v);
        ValidateEnumOption(context, model, "DefaultIgnoreCondition", model.Options.DefaultIgnoreCondition, TryGetTomlIgnoreConditionExpression, v => model.Options.DefaultIgnoreCondition = v);
        ValidateEnumOption(context, model, "DuplicateKeyHandling", model.Options.DuplicateKeyHandling, TryGetTomlDuplicateKeyHandlingExpression, v => model.Options.DuplicateKeyHandling = v);
        ValidateEnumOption(context, model, "UnmappedMemberHandling", model.Options.UnmappedMemberHandling, TryGetTomlUnmappedMemberHandlingExpression, v => model.Options.UnmappedMemberHandling = v);
        if (model.Options.MaxDepth is not null && model.Options.MaxDepth.Value < 0)
        {
            context.ReportDiagnostic(Diagnostic.Create(
                InvalidSourceGenerationOption,
                model.ContextSymbol.Locations.FirstOrDefault(),
                model.ContextSymbol.ToDisplayString(),
                "MaxDepth must be greater than or equal to 0."));
            model.Options.MaxDepth = null;
        }

        ValidateEnumOption(context, model, "MappingOrder", model.Options.MappingOrder, TryGetTomlMappingOrderPolicyExpression, v => model.Options.MappingOrder = v);
        ValidateEnumOption(context, model, "DottedKeyHandling", model.Options.DottedKeyHandling, TryGetTomlDottedKeyHandlingExpression, v => model.Options.DottedKeyHandling = v);
        ValidateEnumOption(context, model, "RootValueHandling", model.Options.RootValueHandling, TryGetTomlRootValueHandlingExpression, v => model.Options.RootValueHandling = v);
        ValidateEnumOption(context, model, "InlineTablePolicy", model.Options.InlineTablePolicy, TryGetTomlInlineTablePolicyExpression, v => model.Options.InlineTablePolicy = v);
        ValidateEnumOption(context, model, "TableArrayStyle", model.Options.TableArrayStyle, TryGetTomlTableArrayStyleExpression, v => model.Options.TableArrayStyle = v);

        if (model.Options.RootValueKeyName is not null && string.IsNullOrWhiteSpace(model.Options.RootValueKeyName))
        {
            context.ReportDiagnostic(Diagnostic.Create(
                InvalidSourceGenerationOption,
                model.ContextSymbol.Locations.FirstOrDefault(),
                model.ContextSymbol.ToDisplayString(),
                "RootValueKeyName cannot be empty or whitespace."));
            model.Options.RootValueKeyName = null;
        }
    }

    private delegate bool TryGetEnumExpression(int value, out string expression);

    private static void ValidateEnumOption(
        SourceProductionContext context,
        ContextModel model,
        string optionName,
        int? value,
        TryGetEnumExpression tryGetExpression,
        Action<int?> clear)
    {
        if (value is null)
        {
            return;
        }

        if (tryGetExpression(value.Value, out _))
        {
            return;
        }

        context.ReportDiagnostic(Diagnostic.Create(
            InvalidSourceGenerationOption,
            model.ContextSymbol.Locations.FirstOrDefault(),
            model.ContextSymbol.ToDisplayString(),
            $"{optionName} has unsupported value {(value.Value).ToString(CultureInfo.InvariantCulture)}."));
        clear(null);
    }

    private static bool HasStaticOptionsConverter(SourceGenOptions options, ITypeSymbol type)
        => TryGetStaticOptionsConverterType(options, type, out _);

    private static bool TryGetStaticOptionsConverterType(SourceGenOptions options, ITypeSymbol type, out ITypeSymbol converterType)
    {
        if (!options.ConverterTypes.IsDefaultOrEmpty)
        {
            foreach (var candidate in options.ConverterTypes)
            {
                if (candidate is not INamedTypeSymbol named)
                {
                    continue;
                }

                for (var current = named.BaseType; current is not null; current = current.BaseType)
                {
                    if (current.IsGenericType &&
                        current.ConstructedFrom.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) == "global::Meziantou.Framework.Toml.Serialization.TomlConverter<T>" &&
                        current.TypeArguments.Length == 1 &&
                        SymbolEqualityComparer.Default.Equals(current.TypeArguments[0], type))
                    {
                        converterType = candidate;
                        return true;
                    }
                }
            }
        }

        converterType = null!;
        return false;
    }

    private static bool DerivesFrom(INamedTypeSymbol symbol, string baseTypeMetadataName)
    {
        for (var current = symbol.BaseType; current is not null; current = current.BaseType)
        {
            if (current.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) == baseTypeMetadataName)
            {
                return true;
            }
        }

        return false;
    }

    // TomlKnownNamingPolicy uses the same values as System.Text.Json's JsonKnownNamingPolicy, so this also maps
    // the values of [JsonSourceGenerationOptions]
    private static string? ToNamingPolicyExpression(TypedConstant constant)
    {
        if (constant.Kind != TypedConstantKind.Enum || constant.Value is not int value)
        {
            return null;
        }

        var policy = (TomlKnownNamingPolicy)value;
        return policy switch
        {
            TomlKnownNamingPolicy.CamelCase => "global::Meziantou.Framework.Toml.TomlNamingPolicy.CamelCase",
            TomlKnownNamingPolicy.SnakeCaseLower => "global::Meziantou.Framework.Toml.TomlNamingPolicy.SnakeCaseLower",
            TomlKnownNamingPolicy.SnakeCaseUpper => "global::Meziantou.Framework.Toml.TomlNamingPolicy.SnakeCaseUpper",
            TomlKnownNamingPolicy.KebabCaseLower => "global::Meziantou.Framework.Toml.TomlNamingPolicy.KebabCaseLower",
            TomlKnownNamingPolicy.KebabCaseUpper => "global::Meziantou.Framework.Toml.TomlNamingPolicy.KebabCaseUpper",
            TomlKnownNamingPolicy.PascalCase => "global::Meziantou.Framework.Toml.TomlNamingPolicy.PascalCase",
            _ => null,
        };
    }

    private static bool TryGetTomlIgnoreConditionExpression(int value, out string expression)
    {
        expression = value switch
        {
            0 => "global::Meziantou.Framework.Toml.TomlIgnoreCondition.Never",
            1 => "global::Meziantou.Framework.Toml.TomlIgnoreCondition.WhenWritingNull",
            2 => "global::Meziantou.Framework.Toml.TomlIgnoreCondition.WhenWritingDefault",
            3 => "global::Meziantou.Framework.Toml.TomlIgnoreCondition.Always",
            4 => "global::Meziantou.Framework.Toml.TomlIgnoreCondition.WhenWriting",
            5 => "global::Meziantou.Framework.Toml.TomlIgnoreCondition.WhenReading",
            _ => null!,
        };

        return expression is not null;
    }

    private static bool TryGetTomlUnmappedMemberHandlingExpression(int value, out string expression)
    {
        expression = value switch
        {
            0 => "global::Meziantou.Framework.Toml.TomlUnmappedMemberHandling.Skip",
            1 => "global::Meziantou.Framework.Toml.TomlUnmappedMemberHandling.Disallow",
            _ => null!,
        };

        return expression is not null;
    }

    private static bool TryGetObjectCreationHandlingExpression(int value, out string expression)
    {
        expression = value switch
        {
            0 => "global::Meziantou.Framework.Toml.TomlObjectCreationHandling.Replace",
            1 => "global::Meziantou.Framework.Toml.TomlObjectCreationHandling.Populate",
            _ => null!,
        };

        return expression is not null;
    }

    private static bool TryGetTomlDuplicateKeyHandlingExpression(int value, out string expression)
    {
        expression = value switch
        {
            0 => "global::Meziantou.Framework.Toml.TomlDuplicateKeyHandling.Error",
            1 => "global::Meziantou.Framework.Toml.TomlDuplicateKeyHandling.LastWins",
            _ => null!,
        };

        return expression is not null;
    }

    private static bool TryGetTomlMappingOrderPolicyExpression(int value, out string expression)
    {
        expression = value switch
        {
            0 => "global::Meziantou.Framework.Toml.TomlMappingOrderPolicy.Declaration",
            1 => "global::Meziantou.Framework.Toml.TomlMappingOrderPolicy.Alphabetical",
            2 => "global::Meziantou.Framework.Toml.TomlMappingOrderPolicy.OrderThenDeclaration",
            3 => "global::Meziantou.Framework.Toml.TomlMappingOrderPolicy.OrderThenAlphabetical",
            _ => null!,
        };

        return expression is not null;
    }

    private static bool TryGetTomlDottedKeyHandlingExpression(int value, out string expression)
    {
        expression = value switch
        {
            0 => "global::Meziantou.Framework.Toml.TomlDottedKeyHandling.Literal",
            1 => "global::Meziantou.Framework.Toml.TomlDottedKeyHandling.Expand",
            _ => null!,
        };

        return expression is not null;
    }

    private static bool TryGetTomlRootValueHandlingExpression(int value, out string expression)
    {
        expression = value switch
        {
            0 => "global::Meziantou.Framework.Toml.TomlRootValueHandling.Error",
            1 => "global::Meziantou.Framework.Toml.TomlRootValueHandling.WrapInRootKey",
            _ => null!,
        };

        return expression is not null;
    }

    private static bool TryGetTomlNewLineKindExpression(int value, out string expression)
    {
        expression = value switch
        {
            0 => "global::Meziantou.Framework.Toml.TomlNewLineKind.Lf",
            1 => "global::Meziantou.Framework.Toml.TomlNewLineKind.CrLf",
            _ => null!,
        };

        return expression is not null;
    }

    private static bool TryGetTomlInlineTablePolicyExpression(int value, out string expression)
    {
        expression = value switch
        {
            0 => "global::Meziantou.Framework.Toml.TomlInlineTablePolicy.Never",
            1 => "global::Meziantou.Framework.Toml.TomlInlineTablePolicy.WhenSmall",
            2 => "global::Meziantou.Framework.Toml.TomlInlineTablePolicy.Always",
            _ => null!,
        };

        return expression is not null;
    }

    private static bool TryGetTomlTableArrayStyleExpression(int value, out string expression)
    {
        expression = value switch
        {
            0 => "global::Meziantou.Framework.Toml.TomlTableArrayStyle.Headers",
            1 => "global::Meziantou.Framework.Toml.TomlTableArrayStyle.InlineArrayOfTables",
            _ => null!,
        };

        return expression is not null;
    }

    private static bool TryGetTomlStringStyleExpression(int value, out string expression)
    {
        expression = value switch
        {
            0 => "global::Meziantou.Framework.Toml.TomlStringStyle.Basic",
            1 => "global::Meziantou.Framework.Toml.TomlStringStyle.Literal",
            2 => "global::Meziantou.Framework.Toml.TomlStringStyle.MultilineBasic",
            3 => "global::Meziantou.Framework.Toml.TomlStringStyle.MultilineLiteral",
            _ => null!,
        };

        return expression is not null;
    }

    private static string EscapeStringLiteral(string value)
        => value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal);

    private static ulong ComputePropertyNameHash56(string value)
    {
        const ulong OffsetBasis = 14695981039346656037UL;
        const ulong Prime = 1099511628211UL;
        const ulong Mask = 0x00FF_FFFF_FFFF_FFFFUL;

        var hash = OffsetBasis;
        for (var i = 0; i < value.Length; i++)
        {
            var c1 = value[i];
            var codePoint = (int)c1;
            if (char.IsHighSurrogate(c1) && i + 1 < value.Length)
            {
                var c2 = value[i + 1];
                if (char.IsLowSurrogate(c2))
                {
                    codePoint = char.ConvertToUtf32(c1, c2);
                    i++;
                }
            }

            hash ^= unchecked((uint)codePoint);
            hash *= Prime;
        }

        return hash & Mask;
    }
}

