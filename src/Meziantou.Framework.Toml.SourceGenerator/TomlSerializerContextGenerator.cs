using System;
using System.Collections.Immutable;
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

namespace Meziantou.Framework.Toml.SourceGeneration;

[Generator]
public sealed class TomlSerializerContextGenerator : IIncrementalGenerator
{
    private static readonly DiagnosticDescriptor ContextMustBePartial = new(
        id: "MFTOML001",
        title: "Toml serializer context must be partial",
        messageFormat: "Type '{0}' derives from Meziantou.Framework.Toml.Serialization.TomlSerializerContext and must be declared partial, as must its containing types, to support source generation",
        category: "Meziantou.Framework.Toml.SourceGeneration",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor ContextMustNotBeGeneric = new(
        id: "MFTOML014",
        title: "Toml serializer context must not be generic",
        messageFormat: "Type '{0}' derives from Meziantou.Framework.Toml.Serialization.TomlSerializerContext and must not be generic to support source generation",
        category: "Meziantou.Framework.Toml.SourceGeneration",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor InaccessibleType = new(
        id: "MFTOML015",
        title: "Type is not accessible from the generated code",
        messageFormat: "Type '{0}' is not accessible from the code generated for context '{1}', or is less accessible than the context. Make it at least as accessible as the context, and not file-local.",
        category: "Meziantou.Framework.Toml.SourceGeneration",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor UnserializableMemberType = new(
        id: "MFTOML016",
        title: "Member type cannot be serialized",
        messageFormat: "Type '{0}' contains member '{1}' of type '{2}', which cannot be serialized because it is {3}. Ignore the member with [TomlIgnore].",
        category: "Meziantou.Framework.Toml.SourceGeneration",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor UnsupportedSerializableType = new(
        id: "MFTOML017",
        title: "Serializable type is not supported",
        messageFormat: "Type '{0}' cannot be registered with [TomlSerializable] because it is {1}",
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
        messageFormat: "Context '{0}' registers a derived type mapping for base type '{1}' without [TomlPolymorphic] or [TomlDerivedType]. Serializer options defaults will be used.",
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

    private static readonly DiagnosticDescriptor InaccessibleConstructor = new(
        id: "MFTOML013",
        title: "Deserialization constructor is not accessible",
        messageFormat: "The constructor of '{0}' annotated with [TomlConstructor] must be public or internal to be used by the generated code",
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
    private const string ContextOutputsTrackingName = "TomlContextOutputs";

    private const string TomlSerializableAttributeMetadataName = "Meziantou.Framework.Toml.Serialization.TomlSerializableAttribute";
    private const string TomlDerivedTypeMappingAttributeMetadataName = "Meziantou.Framework.Toml.Serialization.TomlDerivedTypeMappingAttribute";
    private const string TomlObjectCreationHandlingAttributeMetadataName = "Meziantou.Framework.Toml.Serialization.TomlObjectCreationHandlingAttribute";
    private const string TomlSourceGenerationOptionsAttributeMetadataName = "Meziantou.Framework.Toml.Serialization.TomlSourceGenerationOptionsAttribute";
    private const string TomlConverterMetadataName = "Meziantou.Framework.Toml.Serialization.TomlConverter";
    private const string TomlConverterFactoryMetadataName = "Meziantou.Framework.Toml.Serialization.TomlConverterFactory";
    private const string TomlConverterAttributeMetadataName = "Meziantou.Framework.Toml.Serialization.TomlConverterAttribute";
    private const string TomlSingleOrArrayAttributeMetadataName = "Meziantou.Framework.Toml.Serialization.TomlSingleOrArrayAttribute";
    private const string TomlOnSerializingMetadataName = "Meziantou.Framework.Toml.Serialization.ITomlOnSerializing";
    private const string TomlOnSerializedMetadataName = "Meziantou.Framework.Toml.Serialization.ITomlOnSerialized";
    private const string TomlOnDeserializingMetadataName = "Meziantou.Framework.Toml.Serialization.ITomlOnDeserializing";
    private const string TomlOnDeserializedMetadataName = "Meziantou.Framework.Toml.Serialization.ITomlOnDeserialized";
    private const string TomlStringEnumConverterMetadataName = "Meziantou.Framework.Toml.Serialization.TomlStringEnumConverter";
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
            Compilation compilation)
        {
            Compilation = compilation;
            ContextSymbol = contextSymbol;
            NamespaceName = namespaceName;
            TypeName = typeName;
            RootTypes = rootTypes;
            DerivedTypeMappings = derivedTypeMappings;
            Options = options;
            IsValid = isValid;
        }

        public Compilation Compilation { get; }
        public INamedTypeSymbol ContextSymbol { get; }
        public string NamespaceName { get; }
        public string TypeName { get; }
        public ImmutableArray<RootTypeModel> RootTypes { get; }
        public ImmutableArray<DerivedTypeMappingModel> DerivedTypeMappings { get; }
        public SourceGenOptions Options { get; }
        public bool IsValid { get; }

        // Under the updated memory safety rules, an extern member must be marked safe or unsafe, and older language
        // versions reject both
        public bool UsesUpdatedMemorySafetyRules => ContextSymbol.DeclaringSyntaxReferences.FirstOrDefault()?.SyntaxTree.Options.Features.ContainsKey("updated-memory-safety-rules") == true;
    }

    private sealed class RootTypeModel
    {
        public RootTypeModel(ITypeSymbol type, string? typeInfoPropertyName, Location? location)
        {
            Type = type;
            TypeInfoPropertyName = typeInfoPropertyName;
            Location = location;
        }

        public ITypeSymbol Type { get; }
        public string? TypeInfoPropertyName { get; }
        public Location? Location { get; }
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
    private const int DefaultMappingOrder = 2; // OrderThenDeclaration

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
        // Only the classes with [TomlSerializable] can be contexts. The transform emits the code, so the pipeline carries
        // equatable values and an edit that does not change the output does not add the source again.
        var tomlContexts = context.SyntaxProvider.ForAttributeWithMetadataName(
            TomlSerializableAttributeMetadataName,
            static (node, _) => node is ClassDeclarationSyntax,
            static (ctx, cancellationToken) => CreateContextOutput(ctx, cancellationToken));

        var outputs = tomlContexts.Collect()
            .Select(static (contexts, _) => DeduplicateContexts(contexts))
            .WithTrackingName(ContextOutputsTrackingName);

        context.RegisterSourceOutput(outputs, static (spc, outputs) =>
        {
            foreach (var output in outputs)
            {
                if (output.HintName is not null && output.Source is not null)
                {
                    spc.AddSource(output.HintName, output.Source);
                }
            }
        });

        // Diagnostics are reported at a location in the compilation's syntax trees, so '#pragma warning disable' applies to them.
        // A member diagnostic is the same for every context that includes its type, so it is reported once.
        var diagnostics = outputs.Select(static (outputs, _) => outputs.SelectMany(static output => output.Diagnostics).Distinct().ToImmutableEquatableArray());
        context.RegisterSourceOutput(diagnostics.Combine(context.CompilationProvider), static (spc, input) =>
        {
            if (input.Left.Length == 0)
            {
                return;
            }

            // Several trees may share a path (for instance an empty one); their diagnostics keep the path-only location
            var trees = new Dictionary<string, SyntaxTree?>(StringComparer.Ordinal);
            foreach (var tree in input.Right.SyntaxTrees)
            {
                trees[tree.FilePath] = trees.ContainsKey(tree.FilePath) ? null : tree;
            }

            foreach (var diagnostic in input.Left)
            {
                spc.ReportDiagnostic(diagnostic.ToDiagnostic(trees));
            }
        });
    }

    private static ImmutableEquatableArray<ContextOutput> DeduplicateContexts(ImmutableArray<ContextOutput?> contexts)
    {
        // A context is found once per attributed partial declaration
        var emitted = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<ContextOutput>();
        foreach (var output in contexts)
        {
            if (output is not null && emitted.Add(output.Key))
            {
                result.Add(output);
            }
        }

        return result.ToImmutableEquatableArray();
    }

    private static ContextOutput? CreateContextOutput(GeneratorAttributeSyntaxContext syntaxContext, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (syntaxContext.TargetNode is not ClassDeclarationSyntax classDeclaration || syntaxContext.TargetSymbol is not INamedTypeSymbol classSymbol)
        {
            return null;
        }

        var model = TryCreateContextModel(classSymbol, classDeclaration, syntaxContext.SemanticModel.Compilation);
        if (model is null)
        {
            return null;
        }

        var output = new GeneratorOutput();
        EmitContext(output, model);
        return new ContextOutput(classSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat), output.HintName, output.Source, output.Diagnostics.ToImmutableEquatableArray());
    }

    // typeof(G<string?>) keeps the annotation of the type argument, but the generated code names the type without it, so
    // the members are read from the unannotated type to have the types the compiler sees. Nullability checks come from
    // the original definition, so they do not change.
    private static ITypeSymbol WithoutNullableTypeArguments(ITypeSymbol type)
    {
        if (type is not INamedTypeSymbol { IsGenericType: true } named || named.ContainingType is { IsGenericType: true })
        {
            return type.WithNullableAnnotation(NullableAnnotation.NotAnnotated);
        }

        var typeArguments = named.TypeArguments.Select(WithoutNullableTypeArguments).ToImmutableArray();
        return named.OriginalDefinition.Construct(typeArguments, typeArguments.Select(static _ => NullableAnnotation.NotAnnotated).ToImmutableArray());
    }

    private static ContextModel? TryCreateContextModel(INamedTypeSymbol classSymbol, ClassDeclarationSyntax classDeclaration, Compilation compilation)
    {
        if (!DerivesFromTomlSerializerContext(classSymbol))
        {
            return null;
        }

        var roots = ImmutableArray.CreateBuilder<RootTypeModel>();
        var derivedTypeMappings = ImmutableArray.CreateBuilder<DerivedTypeMappingModel>();
        var options = new SourceGenOptions();

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

                roots.Add(new RootTypeModel(WithoutNullableTypeArguments(typeSymbol), GetTypeInfoPropertyNameOverride(attribute), attribute.ApplicationSyntaxReference?.GetSyntax().GetLocation()));
                continue;
            }

            if (TryCreateDerivedTypeMappingModel(attribute, out var derivedTypeMapping))
            {
                derivedTypeMappings.Add(derivedTypeMapping);
                continue;
            }

            if (IsTomlSourceGenerationOptionsAttribute(attribute))
            {
                ApplyTomlSourceGenerationOptionsAttribute(attribute, options);
            }
        }

        if (roots.Count == 0)
        {
            return null;
        }

        // A nested context requires its containing types to be partial too
        var isPartial = classDeclaration.Modifiers.Any(static modifier => modifier.IsKind(SyntaxKind.PartialKeyword)) &&
            classDeclaration.Ancestors().OfType<TypeDeclarationSyntax>().All(static declaration => declaration.Modifiers.Any(static modifier => modifier.IsKind(SyntaxKind.PartialKeyword)));
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
            compilation: compilation);
    }

    private static void EmitContext(GeneratorOutput context, ContextModel model)
    {
        if (model.RootTypes.Length == 0)
        {
            return;
        }

        if (!model.IsValid)
        {
            context.ReportDiagnostic(DiagnosticInfo.Create(ContextMustBePartial, model.ContextSymbol.Locations.FirstOrDefault(), model.ContextSymbol.ToDisplayString()));
            return;
        }

        // A context nested in a generic type is supported, as its declaration repeats the type parameters of the containing types
        if (model.ContextSymbol.TypeParameters.Length > 0)
        {
            context.ReportDiagnostic(DiagnosticInfo.Create(ContextMustNotBeGeneric, model.ContextSymbol.Locations.FirstOrDefault(), model.ContextSymbol.ToDisplayString()));
            return;
        }

        ValidateConverters(context, model);
        ValidateSourceGenerationOptions(context, model);
        if (!ValidateRootAttributes(context, model))
        {
            return;
        }

        var derivedTypeMappings = ValidateDerivedTypeMappings(context, model);
        if (!ValidateRootTypes(context, model, derivedTypeMappings))
        {
            return;
        }

        var expanded = ExpandTypeGraph(context, model, derivedTypeMappings);
        var ordered = expanded
            .OrderBy(static t => t.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat), StringComparer.Ordinal)
            .ToImmutableArray();

        // The generated code would not compile, so only the diagnostics are reported
        if (!ValidateTypeAccessibility(context, model, ordered) || context.Diagnostics.Any(static diagnostic => IsMemberTypeError(diagnostic.Descriptor)))
        {
            return;
        }

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

    // A diagnostic is reported on the member or the attribute when it is in source, so the IDE shows it there and
    // '#pragma warning disable' applies to it, and on the context otherwise
    private static Location? GetDiagnosticLocation(ContextModel model, ISymbol? symbol)
        => symbol?.Locations.FirstOrDefault(static location => location.IsInSource) ?? model.ContextSymbol.Locations.FirstOrDefault();

    private static Location? GetDiagnosticLocation(ContextModel model, AttributeData attribute)
        => attribute.ApplicationSyntaxReference?.GetSyntax().GetLocation() ?? model.ContextSymbol.Locations.FirstOrDefault();

    private static bool ValidateTypeAccessibility(GeneratorOutput context, ContextModel model, ImmutableArray<ITypeSymbol> types)
    {
        var isValid = true;
        var reported = new HashSet<ITypeSymbol>(SymbolEqualityComparer.Default);
        foreach (var type in types)
        {
            if ((FindInaccessibleType(model, type) ?? FindLessAccessibleType(model, type)) is { } inaccessibleType && reported.Add(inaccessibleType))
            {
                context.ReportDiagnostic(DiagnosticInfo.Create(InaccessibleType, model.ContextSymbol.Locations.FirstOrDefault(), inaccessibleType.ToDisplayString(), model.ContextSymbol.ToDisplayString()));
                isValid = false;
            }
        }

        return isValid;
    }

    // The generated code is in another file than the types, so a file-local type is never accessible from it
    private static ITypeSymbol? FindInaccessibleType(ContextModel model, ITypeSymbol type)
    {
        switch (type)
        {
            case IArrayTypeSymbol array:
                return FindInaccessibleType(model, array.ElementType);

            case INamedTypeSymbol named:
                for (var current = named; current is not null; current = current.ContainingType)
                {
                    if (current.IsFileLocal)
                    {
                        return current;
                    }
                }

                if (!model.Compilation.IsSymbolAccessibleWithin(named.OriginalDefinition, model.ContextSymbol))
                {
                    return named;
                }

                foreach (var typeArgument in named.TypeArguments)
                {
                    if (FindInaccessibleType(model, typeArgument) is { } inaccessibleTypeArgument)
                    {
                        return inaccessibleTypeArgument;
                    }
                }

                return null;

            default:
                return null;
        }
    }

    // The context exposes a public TomlTypeInfo<T> property for each type, so a type less accessible than the context, such
    // as a private nested type next to an internal nested context, would fail with CS0053
    private static ITypeSymbol? FindLessAccessibleType(ContextModel model, ITypeSymbol type)
    {
        switch (type)
        {
            case IArrayTypeSymbol array:
                return FindLessAccessibleType(model, array.ElementType);

            case INamedTypeSymbol named:
                var typeAccessibility = GetEffectiveAccessibility(named);
                var contextAccessibility = GetEffectiveAccessibility(model.ContextSymbol);
                if (typeAccessibility.InAssembly < contextAccessibility.InAssembly || typeAccessibility.OutsideAssembly < contextAccessibility.OutsideAssembly)
                {
                    return named;
                }

                foreach (var typeArgument in named.TypeArguments)
                {
                    if (FindLessAccessibleType(model, typeArgument) is { } lessAccessibleTypeArgument)
                    {
                        return lessAccessibleTypeArgument;
                    }
                }

                return null;

            default:
                return null;
        }
    }

    // Accessibility levels are not ordered: internal and protected are each accessible where the other is not. So the code in
    // the assembly and the code outside of it are compared separately: 2 is any code, 1 is derived types only, 0 is none.
    private static (int InAssembly, int OutsideAssembly) GetEffectiveAccessibility(INamedTypeSymbol type)
    {
        var inAssembly = 2;
        var outsideAssembly = 2;
        for (var current = type; current is not null; current = current.ContainingType)
        {
            var (currentInAssembly, currentOutsideAssembly) = current.DeclaredAccessibility switch
            {
                Accessibility.Public => (2, 2),
                Accessibility.ProtectedOrInternal => (2, 1),
                Accessibility.Internal => (2, 0),
                Accessibility.Protected => (1, 1),
                Accessibility.ProtectedAndInternal => (1, 0),
                _ => (0, 0),
            };

            inAssembly = Math.Min(inAssembly, currentInAssembly);
            outsideAssembly = Math.Min(outsideAssembly, currentOutsideAssembly);
        }

        return (inAssembly, outsideAssembly);
    }

    private static string? GetUnserializableTypeReason(ITypeSymbol type) => type switch
    {
        { IsRefLikeType: true } => "a ref struct",
        { TypeKind: TypeKind.Delegate } => "a delegate",
        { TypeKind: TypeKind.Pointer or TypeKind.FunctionPointer } => "a pointer",
        _ => null,
    };

    private static void EmitContext(GeneratorOutput context, ContextModel model, ImmutableArray<DerivedTypeMappingModel> derivedTypeMappings, ImmutableArray<ITypeSymbol> ordered)
    {
        var builder = new StringBuilder();
        builder.AppendLine("// <auto-generated/>");
        builder.AppendLine("#nullable enable");

        // The model can use obsolete and experimental types and members. The ones that are errors to use are accessed through
        // accessors instead.
        builder.Append("#pragma warning disable CS0612, CS0618");
        foreach (var diagnosticId in GetExperimentalDiagnosticIds(ordered))
        {
            builder.Append(", ").Append(diagnosticId);
        }

        builder.AppendLine();
        builder.AppendLine();

        if (!string.IsNullOrEmpty(model.NamespaceName))
        {
            builder.Append("namespace ").Append(model.NamespaceName).AppendLine(";");
            builder.AppendLine();
        }

        var containingTypes = new List<INamedTypeSymbol>();
        for (var containingType = model.ContextSymbol.ContainingType; containingType is not null; containingType = containingType.ContainingType)
        {
            containingTypes.Insert(0, containingType);
        }

        foreach (var containingType in containingTypes)
        {
            builder.Append("partial ").Append(GetTypeDeclarationKeyword(containingType)).Append(' ').Append(containingType.Name);
            if (containingType.TypeParameters.Length > 0)
            {
                builder.Append('<').Append(string.Join(", ", containingType.TypeParameters.Select(static parameter => parameter.Name))).Append('>');
            }

            builder.AppendLine();
            builder.AppendLine("{");
        }

        AppendGeneratedTypeAttributes(builder, string.Empty);
        builder.Append("partial class ").Append(model.TypeName).AppendLine();
        builder.AppendLine("{");

        builder.Append("    private ").Append(model.TypeName).AppendLine("(global::Meziantou.Framework.Toml.TomlSerializerOptions options, bool _generated) : base(options) { }");
        builder.AppendLine();

        builder.Append("    public static ").Append(model.TypeName).AppendLine(" Default { get; } = new(CreateDefaultOptions(), _generated: true);");
        builder.AppendLine();

        if (!model.Options.ConverterTypes.IsDefaultOrEmpty)
        {
            builder.AppendLine("    private static readonly global::System.Type[] s_sourceGenerationConverterTypes =");
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
        builder.AppendLine("    public override global::Meziantou.Framework.Toml.TomlTypeInfo? GetTypeInfo(global::System.Type type, global::Meziantou.Framework.Toml.TomlSerializerOptions options)");
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
        for (var i = 0; i < containingTypes.Count; i++)
        {
            builder.AppendLine("}");
        }

        context.AddSource(GetHintName(model.ContextSymbol), builder.ToString());
    }

    private static string GetTypeDeclarationKeyword(INamedTypeSymbol type)
    {
        return (type.IsRecord, type.TypeKind) switch
        {
            (true, TypeKind.Struct) => "record struct",
            (true, _) => "record",
            (_, TypeKind.Struct) => "struct",
            (_, TypeKind.Interface) => "interface",
            _ => "class",
        };
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
        builder.AppendLine("    private static global::Meziantou.Framework.Toml.TomlSerializerOptions CreateDefaultOptions()");
        builder.AppendLine("    {");
        builder.AppendLine("        var options = global::Meziantou.Framework.Toml.TomlSerializerOptions.Default;");

        if (!model.Options.ConverterTypes.IsDefaultOrEmpty)
        {
            builder.AppendLine("        var converters = new global::Meziantou.Framework.Toml.Serialization.TomlConverter[]");
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

        if (model.Options.RootValueKeyName is { } rootValueKeyName && !string.IsNullOrWhiteSpace(rootValueKeyName))
        {
            builder.Append("        options = options with { RootValueKeyName = \"").Append(EscapeStringLiteral(rootValueKeyName)).AppendLine("\" };");
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
        GeneratorOutput context,
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
        // Same precedence as the reflection resolver: the converter declared on the type wins
        var declaredConverter = GetDeclaredConverter(type, type, model);
        var usesStringEnumConverter = type.TypeKind == TypeKind.Enum && (declaredConverter is { Error: null, IsStringEnum: true } || HasOptionsStringEnumConverter(model.Options));
        ITypeSymbol? staticOptionsConverterType = null;
        var usesStaticOptionsConverter = !usesStringEnumConverter && TryGetStaticOptionsConverterType(model.Options, type, out staticOptionsConverterType);

        builder.Append("    private global::Meziantou.Framework.Toml.TomlTypeInfo<").Append(typeName).Append(">? _").Append(propertyName).AppendLine(";");
        builder.Append("    ").Append(propertyAccessibility).Append(" global::Meziantou.Framework.Toml.TomlTypeInfo<").Append(typeName).Append("> ").Append(propertyName).AppendLine();
        builder.Append("        => _").Append(propertyName).Append(" ??= Create").Append(propertyName).AppendLine("(Options);");
        builder.AppendLine();

        if (!string.Equals(publicPropertyName, propertyName, StringComparison.Ordinal))
        {
            builder.Append("    public global::Meziantou.Framework.Toml.TomlTypeInfo<").Append(typeName).Append("> ").Append(publicPropertyName).Append(" => ").Append(propertyName).AppendLine(";");
            builder.AppendLine();
        }

        builder.Append("    private global::Meziantou.Framework.Toml.TomlTypeInfo<").Append(typeName).Append("> Create").Append(propertyName).AppendLine("(global::Meziantou.Framework.Toml.TomlSerializerOptions options)");
        builder.AppendLine("    {");

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
            if (usesStringEnumConverter)
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
        builder.Append("    private sealed class __TomlTypeInfo_").Append(propertyName).Append(" : global::Meziantou.Framework.Toml.TomlTypeInfo<").Append(typeName).AppendLine(">");
        builder.AppendLine("    {");
        builder.Append("        private readonly ").Append(model.TypeName).AppendLine(" _context;");
        // The metadata is shared by the threads that use the same options, and some types are only resolved when read
        builder.AppendLine("        private readonly global::System.Collections.Concurrent.ConcurrentDictionary<global::System.Type, global::Meziantou.Framework.Toml.TomlTypeInfo>? _typeInfoCache;");
        builder.AppendLine();
        builder.Append("        public __TomlTypeInfo_").Append(propertyName).Append('(').Append(model.TypeName).AppendLine(" context, global::Meziantou.Framework.Toml.TomlSerializerOptions options) : base(options)");
        builder.AppendLine("        {");
        builder.AppendLine("            _context = context;");
        // The member types are resolved when first used: for options other than the context's, the context creates new metadata
        // on each call, so resolving them here would never end for a recursive type
        builder.AppendLine("            if (!global::System.Object.ReferenceEquals(options, context.Options))");
        builder.AppendLine("            {");
        builder.AppendLine("                _typeInfoCache = new global::System.Collections.Concurrent.ConcurrentDictionary<global::System.Type, global::Meziantou.Framework.Toml.TomlTypeInfo>();");
        builder.AppendLine("            }");
        builder.AppendLine("        }");
        builder.AppendLine();
        builder.AppendLine("        private global::Meziantou.Framework.Toml.TomlTypeInfo GetTypeInfo<__T>(global::Meziantou.Framework.Toml.TomlTypeInfo generatedTypeInfo)");
        builder.AppendLine("        {");
        builder.AppendLine("            var type = typeof(__T);");
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
        builder.AppendLine("        public override bool WritesTable => true;");
        builder.AppendLine();

        EmitMemberConverterTypeInfos(builder, poco);
        EmitNonPublicGetterAccessors(builder, poco);
        EmitNonPublicSetterAccessors(builder, model, type, poco);

        builder.Append("        public override void Write(global::Meziantou.Framework.Toml.Serialization.TomlWriter writer, ").Append(typeName).AppendLine(" value)");
        builder.AppendLine("        {");
        if (!type.IsValueType)
        {
            builder.AppendLine("            if (value is null) throw new global::Meziantou.Framework.Toml.TomlException(\"TOML does not support null values.\");");
        }
        if (callsOnSerializing)
        {
            builder.AppendLine("            ((global::Meziantou.Framework.Toml.Serialization.ITomlOnSerializing)value).OnTomlSerializing();");
        }
        builder.AppendLine("            writer.WriteStartTable();");
        builder.AppendLine("            AttachPropertiesMetadata(writer, value);");
        string? usedKeysVariable = null;
        if (poco.ExtensionData is { } extensionData)
        {
            var usedKeysComparer = GetEffectivePropertyNameCaseInsensitive(model.Options)
                ? "global::System.StringComparer.OrdinalIgnoreCase"
                : "global::System.StringComparer.Ordinal";
            usedKeysVariable = "__usedKeys";
            builder.Append("            var __extensionData = value.").Append(extensionData.Identifier).AppendLine(";");
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
            builder.AppendLine("                    if (__usedKeys is not null && __usedKeys.Contains(__key)) throw new global::Meziantou.Framework.Toml.TomlException($\"Extension data key '{__key}' conflicts with an existing member key.\");");
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
            builder.Append("        public override ").Append(readReturnType).AppendLine(" Read(global::Meziantou.Framework.Toml.Serialization.TomlReader reader)");
            builder.AppendLine("        {");
            builder.AppendLine("            if (reader.TokenType != global::Meziantou.Framework.Toml.Serialization.TomlTokenType.StartTable) throw reader.CreateException($\"Expected StartTable token but was {reader.TokenType}.\");");
            builder.Append("            throw CreateConfigurationException(\"").Append(EscapeStringLiteral(ctorError.ErrorMessage)).Append('"');
            if (ctorError.ErrorMessageSuffix is not null)
            {
                builder.Append(" + typeof(").Append(typeName).Append(").FullName + \"").Append(EscapeStringLiteral(ctorError.ErrorMessageSuffix)).Append('"');
            }

            builder.AppendLine(");");
            builder.AppendLine("        }");
        }
        else if (poco.RequiresGeneratedObjectInitializer)
        {
            var bufferedConstructor = constructor ?? new PocoConstructor(null, ImmutableArray<PocoConstructorParameter>.Empty, null, setsRequiredMembers: poco.ParameterlessConstructorSetsRequiredMembers);
            EmitPocoReadWithConstructor(builder, model, type, poco, bufferedConstructor, readReturnType, callsOnDeserializing, callsOnDeserialized, useObjectInitializerConstruction: true);
            EmitPocoReadIntoExisting(builder, typeName, poco, callsOnDeserializing, callsOnDeserialized, model.Options);
        }
        else if (constructor is { IsValid: true } ctor && !ctor.Parameters.IsDefaultOrEmpty)
        {
            EmitPocoReadWithConstructor(builder, model, type, poco, ctor, readReturnType, callsOnDeserializing, callsOnDeserialized, useObjectInitializerConstruction: false);
            EmitPocoReadIntoExisting(builder, typeName, poco, callsOnDeserializing, callsOnDeserialized, model.Options);
        }
        else
        {
            builder.Append("        public override ").Append(readReturnType).AppendLine(" Read(global::Meziantou.Framework.Toml.Serialization.TomlReader reader)");
            builder.AppendLine("        {");
            builder.AppendLine("            if (reader.TokenType != global::Meziantou.Framework.Toml.Serialization.TomlTokenType.StartTable) throw reader.CreateException($\"Expected StartTable token but was {reader.TokenType}.\");");
            builder.AppendLine("            var tableStartSpan = reader.CurrentSpan;");
            builder.AppendLine("            var __diagnosticCount = GetDeserializationDiagnosticCount(reader);");
            builder.Append("            var __propertiesMetadata = BeginPropertiesMetadata<").Append(typeName).AppendLine(">(reader);");

            // Like the reflection-based metadata, an exception thrown by the constructor is reported with the table
            builder.Append("            ").Append(typeName).AppendLine(" value;");
            builder.AppendLine("            try");
            builder.AppendLine("            {");
            builder.Append("                value = ").Append(poco.UsesConstructorAccessor ? "__CreateInstance()" : "new " + typeName + "()").AppendLine(";");
            builder.AppendLine("            }");
            builder.AppendLine("            catch (global::System.Exception ex)");
            builder.AppendLine("            {");
            builder.Append("                var __message = $\"Failed to create an instance of '{typeof(").Append(typeName).AppendLine(").FullName}'.\";");
            builder.AppendLine("                throw tableStartSpan is { } __span ? new global::Meziantou.Framework.Toml.TomlException(__span, __message, ex) : new global::Meziantou.Framework.Toml.TomlException(__message, ex);");
            builder.AppendLine("            }");
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
            builder.AppendLine("            while (reader.TokenType != global::Meziantou.Framework.Toml.Serialization.TomlTokenType.EndTable)");
            builder.AppendLine("            {");
            builder.AppendLine("                if (reader.TokenType != global::Meziantou.Framework.Toml.Serialization.TomlTokenType.PropertyName) throw reader.CreateException($\"Expected PropertyName token but was {reader.TokenType}.\");");
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
            builder.AppendLine("            ThrowIfDeserializationDiagnostics(reader, __diagnosticCount, tableStartSpan);");
            if (hasRequiredMembers)
            {
                if (poco.Members.Length <= 64)
                {
                    builder.AppendLine("            if (requiredMask != 0 && (seenMask & requiredMask) != requiredMask)");
                    builder.AppendLine("            {");
                    builder.AppendLine("                var span = tableStartSpan ?? endTableSpan;");
                    for (var i = 0; i < poco.Members.Length; i++)
                    {
                        if (!poco.Members[i].IsRequired)
                        {
                            continue;
                        }

                        var serializedName = EscapeInterpolatedStringLiteral(poco.Members[i].SerializedName);
                        builder.Append("                if ((seenMask & (1UL << ").Append(i.ToString(CultureInfo.InvariantCulture)).AppendLine(")) == 0)");
                        builder.AppendLine("                {");
                        builder.Append("                    throw CreateDeserializationException(reader, span, $\"Missing required TOML key '")
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
                    builder.AppendLine("                var span = tableStartSpan ?? endTableSpan;");
                    for (var i = 0; i < poco.Members.Length; i++)
                    {
                        if (!poco.Members[i].IsRequired)
                        {
                            continue;
                        }

                        var serializedName = EscapeInterpolatedStringLiteral(poco.Members[i].SerializedName);
                        builder.Append("                if (!seen[").Append(i.ToString(CultureInfo.InvariantCulture)).AppendLine("])");
                        builder.AppendLine("                {");
                        builder.Append("                    throw CreateDeserializationException(reader, span, $\"Missing required TOML key '")
                            .Append(serializedName)
                            .Append("' when deserializing '{typeof(")
                            .Append(typeName)
                            .Append(").FullName}'.\");");
                        builder.AppendLine("                }");
                    }
                    builder.AppendLine("            }");
                }
            }
            builder.AppendLine("            EndPropertiesMetadata(reader, __propertiesMetadata, value);");
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
            builder.Append("        private global::Meziantou.Framework.Toml.TomlTypeInfo<").Append(memberTypeName).Append(">? _").Append(name).AppendLine(";");
            builder.Append("        private global::Meziantou.Framework.Toml.TomlTypeInfo<").Append(memberTypeName).Append("> ").Append(name).Append(" => _").Append(name).Append(" ??= ")
                .Append(GetDeclaredConverterTypeInfoExpression(converter, member.Type, "Options")).AppendLine(";");
            builder.AppendLine();
        }
    }

    private static string GetDeclaredConverterTypeInfoExpression(DeclaredConverter converter, ITypeSymbol convertedType, string optionsExpression)
    {
        var typeName = convertedType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        if (converter.IsStringEnum)
        {
            return TryGetNullableUnderlyingType(convertedType, out var enumType)
                ? "CreateNullableStringEnumTypeInfo<" + enumType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) + ">(" + optionsExpression + ")"
                : "CreateStringEnumTypeInfo<" + typeName + ">(" + optionsExpression + ")";
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
            // The member of the declaring type only: a member of a base type can have the same name
            var bindingFlags = "global::System.Reflection.BindingFlags.Instance | global::System.Reflection.BindingFlags.Public | global::System.Reflection.BindingFlags.NonPublic | global::System.Reflection.BindingFlags.DeclaredOnly";
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

    private static void EmitNonPublicSetterAccessors(StringBuilder builder, ContextModel model, ITypeSymbol type, PocoShape poco)
    {
        if (poco.UsesConstructorAccessor)
        {
            builder.AppendLine("        [global::System.Runtime.CompilerServices.UnsafeAccessor(global::System.Runtime.CompilerServices.UnsafeAccessorKind.Constructor)]");
            builder.Append("        private static ").Append(model.UsesUpdatedMemorySafetyRules ? "safe " : "").Append("extern ").Append(type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)).AppendLine(" __CreateInstance();");
            builder.AppendLine();
        }

        if (poco.ExtensionData is { SetterAccessorName: { } extensionDataAccessorName, Symbol.ContainingType: { } extensionDataDeclaringType } extensionData)
        {
            EmitReflectionSetterAccessor(builder, extensionDataAccessorName, extensionDataDeclaringType, extensionData.MemberName, extensionData.MemberType, isField: false);
        }

        foreach (var member in poco.Members)
        {
            if (member.SetterAccessorName is not null && member.SetterAccessorIsInitAccessor)
            {
                builder.Append("        [global::System.Runtime.CompilerServices.UnsafeAccessor(global::System.Runtime.CompilerServices.UnsafeAccessorKind.Method, Name = \"set_").Append(EscapeStringLiteral(member.MemberName)).AppendLine("\")]");
                builder.Append("        private static ").Append(model.UsesUpdatedMemorySafetyRules ? "safe " : "").Append("extern void ").Append(member.SetterAccessorName).Append('(').Append(member.DeclaringType.IsValueType ? "ref " : "").Append(member.DeclaringType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat))
                    .Append(" __instance, ").Append(member.Type.ToDisplayString(FullyQualifiedNullableFormat)).AppendLine(" __value);");
                builder.AppendLine();
            }
            else if (member.SetterAccessorName is not null)
            {
                EmitReflectionSetterAccessor(builder, member.SetterAccessorName, member.DeclaringType, member.MemberName, member.Type, member.IsField);
            }

            if (member.InitSetterAccessorName is not null)
            {
                if (CanUseInitAccessor(model, member.DeclaringType))
                {
                    builder.Append("        [global::System.Runtime.CompilerServices.UnsafeAccessor(global::System.Runtime.CompilerServices.UnsafeAccessorKind.Method, Name = \"set_").Append(EscapeStringLiteral(member.MemberName)).AppendLine("\")]");
                    builder.Append("        private static ").Append(model.UsesUpdatedMemorySafetyRules ? "safe " : "").Append("extern void ").Append(member.InitSetterAccessorName).Append('(').Append(member.DeclaringType.IsValueType ? "ref " : "").Append(member.DeclaringType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat))
                        .Append(" __instance, ").Append(member.Type.ToDisplayString(FullyQualifiedNullableFormat)).AppendLine(" __value);");
                    builder.AppendLine();
                }
                else
                {
                    EmitReflectionSetterAccessor(builder, member.InitSetterAccessorName, member.DeclaringType, member.MemberName, member.Type, member.IsField);
                }
            }
        }
    }

    private static void EmitReflectionSetterAccessor(StringBuilder builder, string accessorName, ITypeSymbol declaringType, string memberName, ITypeSymbol memberType, bool isField)
    {
        var declaringTypeName = declaringType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        var memberTypeName = memberType.ToDisplayString(FullyQualifiedNullableFormat);
        // The member of the declaring type only: a member of a base type can have the same name
        var bindingFlags = "global::System.Reflection.BindingFlags.Instance | global::System.Reflection.BindingFlags.Public | global::System.Reflection.BindingFlags.NonPublic | global::System.Reflection.BindingFlags.DeclaredOnly";
        var memberLookup = "typeof(" + declaringTypeName + ")." + (isField ? "GetField" : "GetProperty") + "(\"" + EscapeStringLiteral(memberName) + "\", " + bindingFlags + ")!";
        var isValueType = declaringType.IsValueType;
        builder.Append("        private static void ").Append(accessorName).Append('(').Append(isValueType ? "ref " : "").Append(declaringTypeName).Append(" __instance, ").Append(memberTypeName).AppendLine(" __value)");
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

    // Like the reflection resolver, an extension data member without a setter cannot be initialized when it is null
    private static void EmitExtensionDataAssignment(StringBuilder builder, string indent, PocoExtensionData extensionData, string valueExpression)
    {
        if (!extensionData.CanSet)
        {
            var message = EscapeStringLiteral($"Extension data member '{extensionData.MemberName}' is null and cannot be initialized.");
            builder.Append(indent).Append("throw new global::Meziantou.Framework.Toml.TomlException(\"").Append(message).AppendLine("\");");
        }
        else if (extensionData.SetterAccessorName is { } accessorName)
        {
            builder.Append(indent).Append(accessorName).Append('(').Append(extensionData.Symbol?.ContainingType is { IsValueType: true } ? "ref value" : "value").Append(", ").Append(valueExpression).AppendLine(");");
        }
        else
        {
            builder.Append(indent).Append("value.").Append(extensionData.Identifier).Append(" = ").Append(valueExpression).AppendLine(";");
        }
    }

    private static string GetSetterAccessorCall(PocoMember member, string instanceExpression, string valueExpression)
        => member.SetterAccessorName + "(" + (member.DeclaringType.IsValueType ? "ref " : "") + instanceExpression + ", " + valueExpression + ")";

    private static string GetMemberReadExpression(PocoMember member, string instanceExpression)
    {
        return member.GetterAccessorName is null
            ? instanceExpression + "." + member.Identifier
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

        // A parameter of another type than its member cannot give the member its value
        string? GetLinkedParameterExpression(int memberIndex)
        {
            for (var i = 0; i < ctor.Parameters.Length; i++)
            {
                if (ctor.Parameters[i].LinkedMemberIndex == memberIndex && SymbolEqualityComparer.Default.Equals(ctor.Parameters[i].ParameterType, poco.Members[memberIndex].Type))
                {
                    return "__arg" + i.ToString(CultureInfo.InvariantCulture);
                }
            }

            return null;
        }

        builder.Append("        public override ").Append(readReturnType).AppendLine(" Read(global::Meziantou.Framework.Toml.Serialization.TomlReader reader)");
        builder.AppendLine("        {");
        builder.AppendLine("            if (reader.TokenType != global::Meziantou.Framework.Toml.Serialization.TomlTokenType.StartTable) throw reader.CreateException($\"Expected StartTable token but was {reader.TokenType}.\");");
        builder.AppendLine("            var tableStartSpan = reader.CurrentSpan;");
            builder.AppendLine("            var __diagnosticCount = GetDeserializationDiagnosticCount(reader);");
        builder.Append("            var __propertiesMetadata = BeginPropertiesMetadata<").Append(typeName).AppendLine(">(reader);");

        // Constructor argument locals.
        for (var i = 0; i < ctor.Parameters.Length; i++)
        {
            var parameter = ctor.Parameters[i];
            var parameterTypeName = parameter.ParameterType.ToDisplayString(FullyQualifiedNullableFormat);
            var defaultLiteral = GetDefaultLiteral(parameter.ParameterType);
            builder.Append("            ").Append(parameterTypeName).Append(" __arg").Append(i.ToString(CultureInfo.InvariantCulture)).Append(" = ").Append(defaultLiteral).AppendLine(";");
            builder.Append("            bool __argSeen").Append(i.ToString(CultureInfo.InvariantCulture)).AppendLine(" = false;");
        }

        // Deferred member assignment locals. A get-only [TomlSingleOrArray] member populates its collection after construction.
        for (var i = 0; i < poco.Members.Length; i++)
        {
            var member = poco.Members[i];
            if (!member.CanSet && !member.HasSingleOrArray)
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
        builder.AppendLine("            while (reader.TokenType != global::Meziantou.Framework.Toml.Serialization.TomlTokenType.EndTable)");
        builder.AppendLine("            {");
        builder.AppendLine("                if (reader.TokenType != global::Meziantou.Framework.Toml.Serialization.TomlTokenType.PropertyName) throw reader.CreateException($\"Expected PropertyName token but was {reader.TokenType}.\");");
        if (GetEffectivePropertyNameCaseInsensitive(model.Options))
        {
            builder.AppendLine("                {");
            builder.AppendLine("                    var name = reader.PropertyName!;");
            builder.AppendLine("                    reader.Read();");

        var comparison = "global::System.StringComparison.OrdinalIgnoreCase";
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
            EmitRecoverableRead(builder, "                        ", indent =>
            {
                if (parameter.LinkedMemberIndex >= 0 && parameter.LinkedMemberIndex < poco.Members.Length && poco.Members[parameter.LinkedMemberIndex].HasSingleOrArray && parameter.ConverterTypeInfoName is null)
                {
                    var linkedMember = poco.Members[parameter.LinkedMemberIndex];
                    EmitSingleOrArrayReadAssignment(builder, parameter.ParameterType, "__arg" + i.ToString(CultureInfo.InvariantCulture), indent, "Member '" + EscapeStringLiteral(linkedMember.MemberName) + "' on '" + EscapeStringLiteral(typeName) + "' uses [TomlSingleOrArray]");
                }
                else
                {
                    builder.Append(indent).Append("__arg").Append(i.ToString(CultureInfo.InvariantCulture)).Append(" = ").Append(GetConstructorParameterReadExpression(parameter, typeName)).AppendLine(";");
                }
            });

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

            EmitRecoverableRead(builder, "                        ", indent =>
            {
                if (member.HasSingleOrArray)
                {
                    EmitSingleOrArrayReadAssignment(builder, member.Type, "__memberValue" + i.ToString(CultureInfo.InvariantCulture), indent, "Member '" + EscapeStringLiteral(member.MemberName) + "' on '" + EscapeStringLiteral(typeName) + "' uses [TomlSingleOrArray]");
                }
                else
                {
                    builder.Append(indent).Append("__memberValue").Append(i.ToString(CultureInfo.InvariantCulture)).Append(" = ").Append(GetMemberReadExpression(member)).AppendLine(";");
                }
            });
            builder.Append("                        __memberSeen").Append(i.ToString(CultureInfo.InvariantCulture)).AppendLine(" = true;");
            builder.AppendLine("                        continue;");
            builder.AppendLine("                    }");
        }

        if (extensionData is not null)
        {
            builder.AppendLine("                    if (__extensionData is null) __extensionData = new();");
            builder.Append("                    __extensionData[name] = ").Append(GetTypeInfoReadExpression(extensionData.ValueType)).AppendLine(";");
            builder.AppendLine("                    continue;");
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

        // A type without members would produce an empty switch (warning CS1522)
        if (actionsByHash.Count == 0)
        {
            builder.AppendLine("                            default:");
            builder.AppendLine("                                break;");
        }

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
                    EmitRecoverableRead(builder, "                                    ", indent =>
                    {
                        if (parameter.LinkedMemberIndex >= 0 && parameter.LinkedMemberIndex < poco.Members.Length && poco.Members[parameter.LinkedMemberIndex].HasSingleOrArray && parameter.ConverterTypeInfoName is null)
                        {
                            var linkedMember = poco.Members[parameter.LinkedMemberIndex];
                            EmitSingleOrArrayReadAssignment(builder, parameter.ParameterType, "__arg" + action.Index.ToString(CultureInfo.InvariantCulture), indent, "Member '" + EscapeStringLiteral(linkedMember.MemberName) + "' on '" + EscapeStringLiteral(typeName) + "' uses [TomlSingleOrArray]");
                        }
                        else
                        {
                            builder.Append(indent).Append("__arg").Append(action.Index.ToString(CultureInfo.InvariantCulture)).Append(" = ").Append(GetConstructorParameterReadExpression(parameter, typeName)).AppendLine(";");
                        }
                    });

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
                        EmitRecoverableRead(builder, "                                    ", indent =>
                        {
                            if (member.HasSingleOrArray)
                            {
                                EmitSingleOrArrayReadAssignment(builder, member.Type, "__memberValue" + action.Index.ToString(CultureInfo.InvariantCulture), indent, "Member '" + EscapeStringLiteral(member.MemberName) + "' on '" + EscapeStringLiteral(typeName) + "' uses [TomlSingleOrArray]");
                            }
                            else
                            {
                                builder.Append(indent).Append("__memberValue").Append(action.Index.ToString(CultureInfo.InvariantCulture)).Append(" = ").Append(GetMemberReadExpression(member)).AppendLine(";");
                            }
                        });
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
        else
        {
            // Extension data captures the unmapped keys, so this code would be unreachable (CS0162)
            if (poco.DisallowUnmappedMembers)
            {
                builder.AppendLine("                    var name = reader.PropertyName!;");
            }

            builder.AppendLine("                    reader.Read();");
            EmitUnmappedMember(builder, "                    ", poco.DisallowUnmappedMembers, typeName);
        }
            builder.AppendLine("                }");
        }
        builder.AppendLine("            }");

        builder.AppendLine("            var endTableSpan = reader.CurrentSpan;");
        builder.AppendLine("            reader.Read();");
        builder.AppendLine("            ThrowIfDeserializationDiagnostics(reader, __diagnosticCount, tableStartSpan);");

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
                var keyName = EscapeInterpolatedStringLiteral(parameter.KeyName);
                builder.AppendLine("                var span = tableStartSpan ?? endTableSpan;");
                builder.Append("                throw CreateDeserializationException(reader, span, $\"Missing required constructor parameter '")
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
                builder.AppendLine("                var span = tableStartSpan ?? endTableSpan;");
                for (var i = 0; i < poco.Members.Length; i++)
                {
                    if (!poco.Members[i].IsRequired)
                    {
                        continue;
                    }

                    var serializedName = EscapeInterpolatedStringLiteral(poco.Members[i].SerializedName);
                    builder.Append("                if ((seenMask & (1UL << ").Append(i.ToString(CultureInfo.InvariantCulture)).AppendLine(")) == 0)");
                    builder.AppendLine("                {");
                    builder.Append("                    throw CreateDeserializationException(reader, span, $\"Missing required TOML key '")
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
                builder.AppendLine("                var span = tableStartSpan ?? endTableSpan;");
                for (var i = 0; i < poco.Members.Length; i++)
                {
                    if (!poco.Members[i].IsRequired)
                    {
                        continue;
                    }

                    var serializedName = EscapeInterpolatedStringLiteral(poco.Members[i].SerializedName);
                    builder.Append("                if (!seen[").Append(i.ToString(CultureInfo.InvariantCulture)).AppendLine("])");
                    builder.AppendLine("                {");
                    builder.Append("                    throw CreateDeserializationException(reader, span, $\"Missing required TOML key '")
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
        var initAccessors = new List<(string Name, string SetterName, ITypeSymbol DeclaringType, ITypeSymbol ValueType)>();
        if (useObjectInitializerConstruction && TryGetInitAccessors(model, poco, ctor, out var initAccessorNames, out var extensionDataInitAccessorName))
        {
            EmitSingleObjectInitializerConstruction(builder, poco, ctor, typeName, readNonNullableTypeName, extensionData, initAccessorNames, extensionDataInitAccessorName, GetLinkedParameterExpression, callsOnDeserializing, wrapConstructionErrors);
            foreach (var (index, name) in initAccessorNames)
            {
                var member = poco.Members[index];
                initAccessors.Add((name, "set_" + member.MemberName, member.DeclaringType, member.Type));
            }

            if (extensionDataInitAccessorName is not null && extensionData?.Symbol is { ContainingType: { } extensionDataDeclaringType })
            {
                initAccessors.Add((extensionDataInitAccessorName, "set_" + extensionData.MemberName, extensionDataDeclaringType, extensionData.MemberType));
            }
        }
        else if (useObjectInitializerConstruction)
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
                        builder.Append("                var __existing = value.").Append(member.Identifier).AppendLine(";");
                        builder.AppendLine("                if (__existing is null)");
                        builder.AppendLine("                {");
                        builder.Append("                    throw new global::Meziantou.Framework.Toml.TomlException($\"Member '").Append(EscapeStringLiteral(member.MemberName))
                            .Append("' on '{value.GetType().FullName}' uses [TomlSingleOrArray] but the existing collection is null or cannot be populated.\");")
                            .AppendLine();
                        builder.AppendLine("                }");
                        if (addCollectionExpression is null)
                        {
                            builder.Append("                throw new global::Meziantou.Framework.Toml.TomlException($\"Member '").Append(EscapeStringLiteral(member.MemberName))
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
                finalInitializerAssignments.Add(member.Identifier + " = __memberValue" + i.ToString(CultureInfo.InvariantCulture));

                // The template reads the defaults of the members; a [SetsRequiredMembers] constructor sets the required ones
                if (!member.IsCompilerRequired || ctor.SetsRequiredMembers)
                {
                    continue;
                }

                var templateValueExpression = "__memberValue" + i.ToString(CultureInfo.InvariantCulture);
                if (GetLinkedParameterExpression(i) is { } linkedParameterExpression)
                {
                    templateValueExpression = "__memberSeen" + i.ToString(CultureInfo.InvariantCulture) + " ? __memberValue" + i.ToString(CultureInfo.InvariantCulture) + " : " + linkedParameterExpression;
                }

                templateInitializerAssignments.Add(member.Identifier + " = " + templateValueExpression);
            }

            // A required member that is not serialized must still be set by the object initializer
            if (!ctor.SetsRequiredMembers)
            {
                foreach (var unserializedRequiredMember in poco.UnserializedRequiredMembers)
                {
                    templateInitializerAssignments.Add(unserializedRequiredMember + " = default!");
                    finalInitializerAssignments.Add(unserializedRequiredMember + " = default!");
                }
            }

            if (extensionData is { CanSet: true })
            {
                needsTemplate = true;
                finalInitializerAssignments.Add(extensionData.Identifier + " = __extensionDataValue");
                if (extensionData.IsCompilerRequired && !ctor.SetsRequiredMembers)
                {
                    templateInitializerAssignments.Add(extensionData.Identifier + " = " + extensionData.CreateExpression);
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
                    builder.Append("                __memberValue").Append(i.ToString(CultureInfo.InvariantCulture)).Append(" = __template.").Append(member.Identifier).AppendLine(";");
                    builder.AppendLine("            }");
                }

                if (extensionData is { CanSet: true })
                {
                    builder.Append("            var __extensionDataValue = __template.").Append(extensionData.Identifier).AppendLine(";");
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
                builder.Append("                var __target = value.").Append(extensionData.Identifier).AppendLine(";");
                builder.AppendLine("                if (__target is null)");
                builder.AppendLine("                {");
                builder.Append("                    throw new global::Meziantou.Framework.Toml.TomlException(\"").Append(nullInitializationMessage).AppendLine("\");");
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
                    if (member.HasSingleOrArray)
                    {
                        EmitSingleOrArrayPopulateExisting(builder, member, i);
                    }

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
                    builder.Append("                value.").Append(member.Identifier).Append(" = __memberValue").Append(i.ToString(CultureInfo.InvariantCulture)).AppendLine(";");
                }

                builder.AppendLine("            }");
            }

            if (extensionData is not null)
            {
                builder.AppendLine("            if (__extensionData is not null && __extensionData.Count != 0)");
                builder.AppendLine("            {");
                builder.Append("                var __target = value.").Append(extensionData.Identifier).AppendLine(";");
                builder.AppendLine("                if (__target is null)");
                builder.AppendLine("                {");
                builder.Append("                    __target = ").Append(extensionData.CreateExpression).AppendLine(";");
                EmitExtensionDataAssignment(builder, "                    ", extensionData, "__target");
                builder.AppendLine("                }");
                builder.AppendLine("                foreach (var __pair in __extensionData)");
                builder.AppendLine("                {");
                builder.AppendLine("                    __target[__pair.Key] = __pair.Value;");
                builder.AppendLine("                }");
                builder.AppendLine("            }");
            }
        }

        builder.AppendLine("            EndPropertiesMetadata(reader, __propertiesMetadata, value);");
        if (callsOnDeserialized)
        {
            builder.AppendLine("            ((global::Meziantou.Framework.Toml.Serialization.ITomlOnDeserialized)value).OnTomlDeserialized();");
        }

        builder.AppendLine("            return value;");
        builder.AppendLine("        }");

        foreach (var (name, setterName, declaringType, valueType) in initAccessors)
        {
            builder.AppendLine();
            builder.Append("        [global::System.Runtime.CompilerServices.UnsafeAccessor(global::System.Runtime.CompilerServices.UnsafeAccessorKind.Method, Name = \"").Append(EscapeStringLiteral(setterName)).AppendLine("\")]");
            builder.Append("        private static ").Append(model.UsesUpdatedMemorySafetyRules ? "safe " : "").Append("extern void ").Append(name).Append('(').Append(declaringType.IsValueType ? "ref " : "").Append(declaringType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat))
                .Append(" __instance, ").Append(valueType.ToDisplayString(FullyQualifiedNullableFormat)).AppendLine(" __value);");
        }
    }

    // The init-only members that are not set by the object initializer are set with an [UnsafeAccessor] to their init
    // accessor, so the constructor runs once. An accessor cannot be declared for a member of a generic type, which uses the
    // template instead.
    private static bool TryGetInitAccessors(ContextModel model, PocoShape poco, PocoConstructor ctor, out List<(int Index, string Name)> initAccessorNames, out string? extensionDataInitAccessorName)
    {
        initAccessorNames = [];
        extensionDataInitAccessorName = null;
        for (var i = 0; i < poco.Members.Length; i++)
        {
            var member = poco.Members[i];
            if (!member.CanSet || member.SetterAccessorName is not null || !member.IsInitOnly || IsSetByObjectInitializer(member.IsCompilerRequired, ctor))
            {
                continue;
            }

            if (!CanUseInitAccessor(model, member.DeclaringType))
            {
                return false;
            }

            initAccessorNames.Add((i, "__Init" + i.ToString(CultureInfo.InvariantCulture)));
        }

        if (poco.ExtensionData is { CanSet: true, IsInitOnly: true } extensionData && !IsSetByObjectInitializer(extensionData.IsCompilerRequired, ctor))
        {
            if (extensionData.Symbol?.ContainingType is not { } declaringType || !CanUseInitAccessor(model, declaringType))
            {
                return false;
            }

            extensionDataInitAccessorName = "__InitExtensionData";
        }

        return true;
    }

    private static bool IsSetByObjectInitializer(bool isCompilerRequired, PocoConstructor ctor) => isCompilerRequired && !ctor.SetsRequiredMembers;

    private static bool CanUseInitAccessor(ContextModel model, ITypeSymbol declaringType)
    {
        for (var current = declaringType as INamedTypeSymbol; current is not null; current = current.ContainingType)
        {
            if (current.IsGenericType)
            {
                return false;
            }
        }

        return FindInaccessibleType(model, declaringType) is null && FindLessAccessibleType(model, declaringType) is null;
    }

    // Only the members that must be set by an object initializer, the required members, are in it. The other members are
    // set after the construction when the document has them, so they keep the values set by the constructor otherwise.
    private static void EmitSingleObjectInitializerConstruction(
        StringBuilder builder,
        PocoShape poco,
        PocoConstructor ctor,
        string typeName,
        string readNonNullableTypeName,
        PocoExtensionData? extensionData,
        List<(int Index, string Name)> initAccessorNames,
        string? extensionDataInitAccessorName,
        Func<int, string?> getLinkedParameterExpression,
        bool callsOnDeserializing,
        bool wrapConstructionErrors)
    {
        var initializerAssignments = ImmutableArray.CreateBuilder<string>();
        for (var i = 0; i < poco.Members.Length; i++)
        {
            var member = poco.Members[i];
            if (!member.CanSet || member.SetterAccessorName is not null || !IsSetByObjectInitializer(member.IsCompilerRequired, ctor))
            {
                continue;
            }

            // A required member is missing only when the required modifier is not enforced, and then gets its default value
            var valueExpression = "__memberSeen" + i.ToString(CultureInfo.InvariantCulture) + " ? __memberValue" + i.ToString(CultureInfo.InvariantCulture) + " : " + (getLinkedParameterExpression(i) ?? GetDefaultLiteral(member.Type));
            initializerAssignments.Add(member.Identifier + " = " + valueExpression);
        }

        // A required member that is not serialized must still be set by the object initializer
        if (!ctor.SetsRequiredMembers)
        {
            foreach (var unserializedRequiredMember in poco.UnserializedRequiredMembers)
            {
                initializerAssignments.Add(unserializedRequiredMember + " = default!");
            }
        }

        var extensionDataInInitializer = extensionData is { CanSet: true } && IsSetByObjectInitializer(extensionData.IsCompilerRequired, ctor);
        if (extensionDataInInitializer)
        {
            builder.Append("            var __extensionDataValue = ").Append(extensionData!.CreateExpression).AppendLine(";");
            builder.AppendLine("            if (__extensionData is not null)");
            builder.AppendLine("            {");
            builder.AppendLine("                foreach (var __pair in __extensionData)");
            builder.AppendLine("                {");
            builder.AppendLine("                    __extensionDataValue[__pair.Key] = __pair.Value;");
            builder.AppendLine("                }");
            builder.AppendLine("            }");
            initializerAssignments.Add(extensionData.Identifier + " = __extensionDataValue");
        }

        builder.Append("            ").Append(readNonNullableTypeName).AppendLine(" value;");
        EmitPocoConstructionAssignment(builder, "value", typeName, ctor.Parameters, initializerAssignments.ToImmutable(), wrapConstructionErrors);

        if (callsOnDeserializing)
        {
            builder.AppendLine("            ((global::Meziantou.Framework.Toml.Serialization.ITomlOnDeserializing)value).OnTomlDeserializing();");
        }

        for (var i = 0; i < poco.Members.Length; i++)
        {
            var member = poco.Members[i];
            var memberValue = "__memberValue" + i.ToString(CultureInfo.InvariantCulture);
            var memberSeen = "__memberSeen" + i.ToString(CultureInfo.InvariantCulture);
            if (member.SetterAccessorName is not null)
            {
                builder.Append("            if (").Append(memberSeen).Append(") ").Append(GetSetterAccessorCall(member, "value", memberValue)).AppendLine(";");
            }
            else if (!member.CanSet)
            {
                if (member.HasSingleOrArray)
                {
                    EmitSingleOrArrayPopulateExisting(builder, member, i);
                }
            }
            else if (!IsSetByObjectInitializer(member.IsCompilerRequired, ctor))
            {
                var accessorName = initAccessorNames.FirstOrDefault(accessor => accessor.Index == i).Name;
                builder.Append("            if (").Append(memberSeen).Append(") ");
                if (accessorName is not null)
                {
                    builder.Append(accessorName).Append('(').Append(member.DeclaringType.IsValueType ? "ref value" : "value").Append(", ").Append(memberValue).AppendLine(");");
                }
                else
                {
                    builder.Append("value.").Append(member.Identifier).Append(" = ").Append(memberValue).AppendLine(";");
                }
            }
        }

        if (extensionData is null || extensionDataInInitializer)
        {
            return;
        }

        builder.AppendLine("            if (__extensionData is not null && __extensionData.Count != 0)");
        builder.AppendLine("            {");
        builder.Append("                var __target = value.").Append(extensionData.Identifier).AppendLine(";");
        builder.AppendLine("                if (__target is null)");
        builder.AppendLine("                {");
        if (extensionData.CanSet)
        {
            builder.Append("                    __target = ").Append(extensionData.CreateExpression).AppendLine(";");
            if (extensionDataInitAccessorName is not null)
            {
                builder.Append("                    ").Append(extensionDataInitAccessorName).Append('(').Append(extensionData.Symbol?.ContainingType is { IsValueType: true } ? "ref value" : "value").AppendLine(", __target);");
            }
            else
            {
                EmitExtensionDataAssignment(builder, "                    ", extensionData, "__target");
            }
        }
        else
        {
            var nullInitializationMessage = EscapeStringLiteral($"Extension data member '{extensionData.MemberName}' is null and cannot be initialized.");
            builder.Append("                    throw new global::Meziantou.Framework.Toml.TomlException(\"").Append(nullInitializationMessage).AppendLine("\");");
        }

        builder.AppendLine("                }");
        builder.AppendLine("                foreach (var __pair in __extensionData)");
        builder.AppendLine("                {");
        builder.AppendLine("                    __target[__pair.Key] = __pair.Value;");
        builder.AppendLine("                }");
        builder.AppendLine("            }");
    }

    private static void EmitSingleOrArrayPopulateExisting(StringBuilder builder, PocoMember member, int index)
    {
        var addCollectionExpression = GetSingleOrArrayAddCollectionExpression(member.Type, "__existing", "__memberValue" + index.ToString(CultureInfo.InvariantCulture) + "!");
        builder.Append("            if (__memberSeen").Append(index.ToString(CultureInfo.InvariantCulture)).AppendLine(")");
        builder.AppendLine("            {");
        builder.Append("                var __existing = value.").Append(member.Identifier).AppendLine(";");
        builder.AppendLine("                if (__existing is null)");
        builder.AppendLine("                {");
        builder.Append("                    throw new global::Meziantou.Framework.Toml.TomlException($\"Member '").Append(EscapeStringLiteral(member.MemberName))
            .Append("' on '{value.GetType().FullName}' uses [TomlSingleOrArray] but the existing collection is null or cannot be populated.\");")
            .AppendLine();
        builder.AppendLine("                }");
        if (addCollectionExpression is null)
        {
            builder.Append("                throw new global::Meziantou.Framework.Toml.TomlException($\"Member '").Append(EscapeStringLiteral(member.MemberName))
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

    private static void EmitPocoReadIntoExisting(
        StringBuilder builder,
        string typeName,
        PocoShape poco,
        bool callsOnDeserializing,
        bool callsOnDeserialized,
        SourceGenOptions options)
    {
        builder.AppendLine();
        builder.AppendLine("        public override object? ReadInto(global::Meziantou.Framework.Toml.Serialization.TomlReader reader, object? existingValue)");
        builder.AppendLine("        {");
        builder.Append("            if (existingValue is not ").Append(typeName).AppendLine(" value)");
        builder.AppendLine("            {");
        builder.AppendLine("                return Read(reader);");
        builder.AppendLine("            }");
        builder.AppendLine("            if (reader.TokenType != global::Meziantou.Framework.Toml.Serialization.TomlTokenType.StartTable) throw reader.CreateException($\"Expected StartTable token but was {reader.TokenType}.\");");
        builder.AppendLine("            var tableStartSpan = reader.CurrentSpan;");
            builder.AppendLine("            var __diagnosticCount = GetDeserializationDiagnosticCount(reader);");
        builder.Append("            var __propertiesMetadata = BeginPropertiesMetadata<").Append(typeName).AppendLine(">(reader);");
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
        builder.AppendLine("            while (reader.TokenType != global::Meziantou.Framework.Toml.Serialization.TomlTokenType.EndTable)");
        builder.AppendLine("            {");
        builder.AppendLine("                if (reader.TokenType != global::Meziantou.Framework.Toml.Serialization.TomlTokenType.PropertyName) throw reader.CreateException($\"Expected PropertyName token but was {reader.TokenType}.\");");
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
                builder.AppendLine("                var span = tableStartSpan ?? endTableSpan;");
                for (var i = 0; i < poco.Members.Length; i++)
                {
                    if (!poco.Members[i].IsRequired)
                    {
                        continue;
                    }

                    var serializedName = EscapeInterpolatedStringLiteral(poco.Members[i].SerializedName);
                    builder.Append("                if ((seenMask & (1UL << ").Append(i.ToString(CultureInfo.InvariantCulture)).AppendLine(")) == 0)");
                    builder.AppendLine("                {");
                    builder.Append("                    throw CreateDeserializationException(reader, span, $\"Missing required TOML key '")
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
                builder.AppendLine("                var span = tableStartSpan ?? endTableSpan;");
                for (var i = 0; i < poco.Members.Length; i++)
                {
                    if (!poco.Members[i].IsRequired)
                    {
                        continue;
                    }

                    var serializedName = EscapeInterpolatedStringLiteral(poco.Members[i].SerializedName);
                    builder.Append("                if (!seen[").Append(i.ToString(CultureInfo.InvariantCulture)).AppendLine("])");
                    builder.AppendLine("                {");
                    builder.Append("                    throw CreateDeserializationException(reader, span, $\"Missing required TOML key '")
                        .Append(serializedName)
                        .Append("' when deserializing '{typeof(")
                        .Append(typeName)
                        .Append(").FullName}'.\");");
                    builder.AppendLine("                }");
                }
                builder.AppendLine("            }");
            }
        }

        builder.AppendLine("            EndPropertiesMetadata(reader, __propertiesMetadata, value);");
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

                // The value was checked for null when it was read, but the local has the annotation of the parameter
                if (parameters[i].DisallowNull && !parameters[i].ParameterType.IsValueType)
                {
                    builder.Append('!');
                }
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
        builder.AppendLine("            catch (global::System.Exception ex)");
        builder.AppendLine("            {");
        builder.AppendLine("                var __message = $\"Failed to create an instance of '{typeof(" + typeName + ").FullName}'.\";");
        builder.AppendLine("                throw (tableStartSpan ?? endTableSpan) is { } __span ? new global::Meziantou.Framework.Toml.TomlException(__span, __message, ex) : new global::Meziantou.Framework.Toml.TomlException(__message, ex);");
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

        builder.Append(indent).AppendLine("if (reader.TokenType == global::Meziantou.Framework.Toml.Serialization.TomlTokenType.StartArray)");
        builder.Append(indent).AppendLine("{");
        builder.Append(indent).Append("    ").Append(targetExpression).Append(" = ").Append(GetTypeInfoReadExpression(collectionType)).AppendLine(";");
        builder.Append(indent).AppendLine("}");
        builder.Append(indent).AppendLine("else");
        builder.Append(indent).AppendLine("{");
        if (createExpression is null)
        {
            builder.Append(indent).Append("    throw new global::Meziantou.Framework.Toml.TomlException(\"").Append(errorPrefix)
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
        builder.Append(indent).Append("if (reader.TokenType == global::Meziantou.Framework.Toml.Serialization.TomlTokenType.StartTable && !reader.IsInlineContainer && ").Append(existingExpression).AppendLine(" is not null)");
        builder.Append(indent).AppendLine("{");
        builder.Append(indent).Append("    var __tableExtension = ").Append(GetTypeInfoAccess(type)).Append(".ReadInto(reader, ").Append(existingExpression).AppendLine(");");
        builder.Append(indent).Append("    ").Append(targetExpression).Append(" = (").Append(typeName).AppendLine(")__tableExtension!;");
        builder.Append(indent).AppendLine("    continue;");
        builder.Append(indent).AppendLine("}");
    }

    private static void EmitRepeatedTableExtensionIntoReadOnlyMember(StringBuilder builder, PocoMember member, string indent)
    {
        var memberAccess = "value." + member.Identifier;
        var memberTypeName = member.Type.ToDisplayString(FullyQualifiedNullableFormat);
        builder.Append(indent).Append("if (reader.TokenType == global::Meziantou.Framework.Toml.Serialization.TomlTokenType.StartTable && !reader.IsInlineContainer && ").Append(memberAccess).AppendLine(" is not null)");
        builder.Append(indent).AppendLine("{");
        builder.Append(indent).Append("    var __tableExtension = ").Append(GetMemberTypeInfoAccess(member)).Append(".ReadInto(reader, ").Append(memberAccess).AppendLine(");");
        builder.Append(indent).Append("    if (!object.ReferenceEquals(").Append(memberAccess).Append(", __tableExtension))").AppendLine();
        builder.Append(indent).AppendLine("    {");
        builder.Append(indent).Append("        throw new global::Meziantou.Framework.Toml.TomlException($\"Member '").Append(EscapeStringLiteral(member.MemberName))
            .Append("' on '{value.GetType().FullName}' cannot be extended by an additional TOML table definition because '")
            .Append(EscapeStringLiteral(memberTypeName))
            .AppendLine("' does not support in-place population.\");");
        builder.Append(indent).AppendLine("    }");
        builder.Append(indent).AppendLine("    continue;");
        builder.Append(indent).AppendLine("}");
    }

    // A member with a converter is read as a whole by its converter, and a member set by an accessor cannot be assigned
    private static bool CanEmitTableHeaderExtension(PocoMember member) => !member.Type.IsValueType && member.ConverterTypeInfoName is null && member.SetterAccessorName is null;

    private static bool CanEmitTableHeaderExtension(PocoConstructorParameter parameter) => !parameter.ParameterType.IsValueType && parameter.ConverterTypeInfoName is null;

    private static void EmitSingleOrArrayMemberRead(StringBuilder builder, PocoMember member, string indent, SourceGenOptions options, string memberAccess)
    {
        var memberTypeName = member.Type.ToDisplayString(FullyQualifiedNullableFormat);
        var populateCondition = GetSingleOrArrayPopulateConditionExpression(member, options);
        var errorPrefix = $"Member '{EscapeStringLiteral(member.MemberName)}' on '{{value.GetType().FullName}}' uses [TomlSingleOrArray]";
        var createExpression = GetSingleOrArrayCreateExpression(member.Type);
        var canPopulateExpression = GetSingleOrArrayCanPopulateExpression(member.Type, memberAccess + "!");
        var addSingleExpression = GetSingleOrArrayAddSingleExpression(member.Type, memberAccess + "!");

        builder.Append(indent).AppendLine("if (reader.TokenType == global::Meziantou.Framework.Toml.Serialization.TomlTokenType.StartArray)");
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
                builder.Append(indent).Append("                throw new global::Meziantou.Framework.Toml.TomlException($\"").Append(errorPrefix)
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
            builder.Append(indent).Append("            throw new global::Meziantou.Framework.Toml.TomlException($\"").Append(errorPrefix)
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
            builder.Append(indent).Append("        throw new global::Meziantou.Framework.Toml.TomlException($\"").Append(errorPrefix)
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
            builder.Append(indent).Append("    throw new global::Meziantou.Framework.Toml.TomlException($\"").Append(errorPrefix)
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
                builder.Append(indent).Append("            throw new global::Meziantou.Framework.Toml.TomlException($\"").Append(errorPrefix)
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
                builder.Append(indent).Append("        throw new global::Meziantou.Framework.Toml.TomlException($\"").Append(errorPrefix)
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
        var setterAccessorName = member.SetterAccessorName ?? member.InitSetterAccessorName;
        if (setterAccessorName is null)
        {
            EmitMemberRead(builder, member, index, indent, options, "value." + member.Identifier);
            return;
        }

        // The member is not accessible from the context, or is init-only: read it into a local, then set it with the generated
        // accessor
        var local = "__accessorValue" + index.ToString(CultureInfo.InvariantCulture);
        builder.Append(indent).AppendLine("{");
        builder.Append(indent).Append("    var ").Append(local).Append(" = ").Append(GetMemberReadExpression(member, "value")).AppendLine(";");
        EmitMemberRead(builder, member, index, indent + "    ", options, local);
        builder.Append(indent).Append("    ").Append(setterAccessorName).Append('(').Append(member.DeclaringType.IsValueType ? "ref " : "").Append("value, ").Append(local).AppendLine(");");
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
                builder.Append(indent).Append("    throw new global::Meziantou.Framework.Toml.TomlException($\"")
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
                    builder.Append(populateIndent).Append("    throw new global::Meziantou.Framework.Toml.TomlException($\"")
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
        builder.Append(indent).AppendLine("catch (global::Meziantou.Framework.Toml.TomlException __ex) when (TryAddDeserializationDiagnostic(reader, __readTokenType, __readSpan, __ex))");
        builder.Append(indent).AppendLine("{");
        builder.Append(indent).AppendLine("    continue;");
        builder.Append(indent).AppendLine("}");
    }

    // Like the reflection-based metadata, a value that cannot be converted is reported with the other errors of the
    // document. The block scopes the locals, which can be in a switch section.
    private static void EmitRecoverableRead(StringBuilder builder, string indent, Action<string> emitRead)
    {
        builder.Append(indent).AppendLine("{");
        builder.Append(indent).AppendLine("    var __readTokenType = reader.TokenType;");
        builder.Append(indent).AppendLine("    var __readSpan = reader.CurrentSpan;");
        builder.Append(indent).AppendLine("    try");
        builder.Append(indent).AppendLine("    {");
        emitRead(indent + "        ");
        builder.Append(indent).AppendLine("    }");
        builder.Append(indent).AppendLine("    catch (global::Meziantou.Framework.Toml.TomlException __ex) when (TryAddDeserializationDiagnostic(reader, __readTokenType, __readSpan, __ex))");
        builder.Append(indent).AppendLine("    {");
        builder.Append(indent).AppendLine("        continue;");
        builder.Append(indent).AppendLine("    }");
        builder.Append(indent).AppendLine("}");
    }

    private static void EmitMemberDispatch(StringBuilder builder, PocoShape poco, bool ignoreCase, SourceGenOptions options)
    {
        var useSeenMask = poco.Members.Length <= 64;
        var throwOnDuplicate = ShouldThrowOnDuplicate(options);
        if (ignoreCase)
        {
            var comparison = "global::System.StringComparison.OrdinalIgnoreCase";
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
                            if (CanEmitTableHeaderExtension(member) && member.InitSetterAccessorName is null)
                            {
                                EmitRepeatedTableExtensionIntoTarget(builder, member.Type, "value." + member.Identifier, "value." + member.Identifier, "                            ");
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
                            if (CanEmitTableHeaderExtension(member) && member.InitSetterAccessorName is null)
                            {
                                EmitRepeatedTableExtensionIntoTarget(builder, member.Type, "value." + member.Identifier, "value." + member.Identifier, "                            ");
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
                builder.Append("                    var __extensionData = value.").Append(extensionData.Identifier).AppendLine(";");
                builder.AppendLine("                    if (__extensionData is null)");
                builder.AppendLine("                    {");
                builder.Append("                        __extensionData = ").Append(extensionData.CreateExpression).AppendLine(";");
                EmitExtensionDataAssignment(builder, "                        ", extensionData, "__extensionData");
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

        // A type without members would produce an empty switch (warning CS1522)
        if (membersByHash.Count == 0)
        {
            builder.AppendLine("                            default:");
            builder.AppendLine("                                break;");
        }

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
                            if (CanEmitTableHeaderExtension(member) && member.InitSetterAccessorName is null)
                            {
                                EmitRepeatedTableExtensionIntoTarget(builder, member.Type, "value." + member.Identifier, "value." + member.Identifier, "                                        ");
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
                            if (CanEmitTableHeaderExtension(member) && member.InitSetterAccessorName is null)
                            {
                                EmitRepeatedTableExtensionIntoTarget(builder, member.Type, "value." + member.Identifier, "value." + member.Identifier, "                                        ");
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
            builder.Append("                        var __extensionData = value.").Append(extensionDataRead.Identifier).AppendLine(";");
            builder.AppendLine("                        if (__extensionData is null)");
            builder.AppendLine("                        {");
            builder.Append("                            __extensionData = ").Append(extensionDataRead.CreateExpression).AppendLine(";");
            EmitExtensionDataAssignment(builder, "                            ", extensionDataRead, "__extensionData");
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

        // The check applies before DefaultIgnoreCondition, but a member whose own ignore condition skips null values is skipped
        if (member.DisallowNullOnSerialize && member.WriteIgnore is not (WriteIgnoreKind.WhenWritingNull or WriteIgnoreKind.WhenWritingDefault))
        {
            builder.Append(openIndent).Append("if (").Append(localName).Append(" is null) throw new global::Meziantou.Framework.Toml.TomlException($\"The member '")
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

        if (canBeNull)
        {
            builder.Append(openIndent).Append("if (").Append(localName).Append(" is null) throw new global::Meziantou.Framework.Toml.TomlException($\"The member '")
                .Append(EscapeInterpolatedStringLiteral(member.MemberName))
                .Append("' on '{typeof(")
                .Append(member.OwnerTypeName)
                .AppendLine(").FullName}' is null, which TOML cannot represent. Use TomlIgnoreCondition.WhenWritingNull to skip it.\");");
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
        GeneratorOutput context,
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

            if (GetDeclaredConverter(current, current, model) is { } typeConverter)
            {
                if (typeConverter.Error is { } typeConverterError)
                {
                    context.ReportDiagnostic(DiagnosticInfo.Create(
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
                        context.ReportDiagnostic(DiagnosticInfo.Create(
                            UnsupportedDictionaryKeyType,
                            GetDiagnosticLocation(model, member.Symbol),
                            current.ToDisplayString(),
                            member.MemberName,
                            member.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)));
                        continue;
                    }

                    if (member.ConverterTypeInfoName is not null)
                    {
                        continue;
                    }

                    if (GetUnserializableTypeReason(member.Type) is { } reason)
                    {
                        context.ReportDiagnostic(DiagnosticInfo.Create(
                            UnserializableMemberType,
                            GetDiagnosticLocation(model, member.Symbol),
                            current.ToDisplayString(),
                            member.MemberName,
                            member.Type.ToDisplayString(),
                            reason));
                        continue;
                    }

                    if (!IsSupportedMemberType(member.Type, model.Options, derivedTypeMappings))
                    {
                        context.ReportDiagnostic(DiagnosticInfo.Create(
                            UnsupportedMemberType,
                            GetDiagnosticLocation(model, member.Symbol),
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

                        if (GetUnserializableTypeReason(parameter.ParameterType) is { } parameterReason)
                        {
                            context.ReportDiagnostic(DiagnosticInfo.Create(
                                UnserializableMemberType,
                                GetDiagnosticLocation(model, parameter.Symbol),
                                current.ToDisplayString(),
                                parameter.ParameterName,
                                parameter.ParameterType.ToDisplayString(),
                                parameterReason));
                            continue;
                        }

                        if (!IsSupportedMemberType(parameter.ParameterType, model.Options, derivedTypeMappings))
                        {
                            context.ReportDiagnostic(DiagnosticInfo.Create(
                                UnsupportedMemberType,
                                GetDiagnosticLocation(model, parameter.Symbol),
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
                        context.ReportDiagnostic(DiagnosticInfo.Create(
                            UnsupportedMemberType,
                            GetDiagnosticLocation(model, extensionData.Symbol),
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
                        context.ReportDiagnostic(DiagnosticInfo.Create(
                            InvalidPolymorphismConfiguration,
                            GetDiagnosticLocation(model, current),
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

    private static bool ValidateRootAttributes(GeneratorOutput context, ContextModel model)
    {
        var reservedIdentifiers = GetReservedIdentifiers(model);
        var typesByName = new Dictionary<string, ITypeSymbol>(StringComparer.Ordinal);
        foreach (var root in model.RootTypes)
        {
            if (root.TypeInfoPropertyName is not { } name)
            {
                continue;
            }

            string? error = null;
            if (typesByName.TryGetValue(name, out var otherType))
            {
                if (SymbolEqualityComparer.Default.Equals(otherType, root.Type))
                {
                    continue;
                }

                error = $"TomlSerializable TypeInfoPropertyName '{name}' is used for both '{otherType.ToDisplayString()}' and '{root.Type.ToDisplayString()}'.";
            }
            else if (!SyntaxFacts.IsValidIdentifier(name))
            {
                error = $"TomlSerializable TypeInfoPropertyName '{name}' must be a valid C# identifier.";
            }
            else if (IsReservedTypeInfoName(reservedIdentifiers, name))
            {
                error = $"TomlSerializable TypeInfoPropertyName '{name}' conflicts with a member of the context, or with a member generated for another TypeInfoPropertyName.";
            }

            if (error is not null)
            {
                context.ReportDiagnostic(DiagnosticInfo.Create(InvalidSourceGenerationOption, root.Location ?? model.ContextSymbol.Locations.FirstOrDefault(), model.ContextSymbol.ToDisplayString(), error));
                return false;
            }

            // The members generated for this name, such as _Foo and CreateFoo, cannot be used by another name
            typesByName[name] = root.Type;
            reservedIdentifiers.Add(name);
            reservedIdentifiers.Add("_" + name);
            reservedIdentifiers.Add("Create" + name);
            reservedIdentifiers.Add("__TomlTypeInfo_" + name);
        }

        return true;
    }

    // The generated code would reference metadata that is not generated for these member types
    private static bool IsMemberTypeError(DiagnosticDescriptor descriptor)
        => descriptor.Equals(UnserializableMemberType) || descriptor.Equals(UnsupportedMemberType) || descriptor.Equals(UnsupportedDictionaryKeyType);

    // A type the generated code cannot handle would produce code that does not compile
    private static bool ValidateRootTypes(GeneratorOutput context, ContextModel model, ImmutableArray<DerivedTypeMappingModel> derivedTypeMappings)
    {
        var isValid = true;
        foreach (var root in model.RootTypes)
        {
            if (GetUnsupportedRootTypeReason(root.Type, model.Options, derivedTypeMappings) is { } reason)
            {
                context.ReportDiagnostic(DiagnosticInfo.Create(UnsupportedSerializableType, root.Location ?? model.ContextSymbol.Locations.FirstOrDefault(), root.Type.ToDisplayString(), reason));
                isValid = false;
            }
        }

        return isValid;
    }

    private static string? GetUnsupportedRootTypeReason(ITypeSymbol type, SourceGenOptions options, ImmutableArray<DerivedTypeMappingModel> derivedTypeMappings)
    {
        if (GetUnserializableTypeReason(type) is { } reason)
        {
            return reason;
        }

        return type switch
        {
            INamedTypeSymbol named when named.IsUnboundGenericType || named.TypeArguments.Any(static argument => argument.TypeKind is TypeKind.TypeParameter or TypeKind.Error) => "an open generic type",
            IArrayTypeSymbol { IsSZArray: false } => "a multi-dimensional array",
            _ when IsSupportedMemberType(type, options, derivedTypeMappings) => null,
            INamedTypeSymbol { TypeKind: TypeKind.Interface } or INamedTypeSymbol { IsAbstract: true } => "abstract, and has no polymorphism configuration ([TomlPolymorphic], [TomlDerivedType], or a derived type mapping)",
            _ => "not supported by the generated code",
        };
    }

    private static bool HasPolymorphismAttributes(INamedTypeSymbol type)
    {
        foreach (var attr in type.GetAttributes())
        {
            var attrName = attr.AttributeClass?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            if (attrName == "global::Meziantou.Framework.Toml.Serialization.TomlPolymorphicAttribute" ||
                attrName == "global::Meziantou.Framework.Toml.Serialization.TomlDerivedTypeAttribute")
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

        // A value tuple has no properties to serialize, and the generated code cannot create it with its tuple syntax
        if (type.IsTupleType)
        {
            return false;
        }

        // A collection without collection metadata, such as Queue<T>, would be written as an object and read back empty
        if (type.SpecialType != SpecialType.System_String && (type.SpecialType == SpecialType.System_Collections_IEnumerable || type.AllInterfaces.Any(static i => i.SpecialType == SpecialType.System_Collections_IEnumerable)))
        {
            return false;
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

    // The generated source and diagnostics of a context, as equatable values for the incremental pipeline
    private sealed record ContextOutput(string Key, string? HintName, string? Source, ImmutableEquatableArray<DiagnosticInfo> Diagnostics);

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
            => new(descriptor, LocationInfo.Create(location), arguments.Select(static argument => argument?.ToString() ?? string.Empty).ToImmutableEquatableArray());

        public Diagnostic ToDiagnostic(Dictionary<string, SyntaxTree?> trees) => Diagnostic.Create(Descriptor, Location?.ToLocation(trees), [.. Arguments]);
    }

    // Collects the output of EmitContext
    private sealed class GeneratorOutput
    {
        public List<DiagnosticInfo> Diagnostics { get; } = [];

        public string? HintName { get; private set; }

        public string? Source { get; private set; }

        // The shape of a type is computed while expanding the type graph and again while emitting it, so the same diagnostic
        // can be reported twice
        public void ReportDiagnostic(DiagnosticInfo diagnostic)
        {
            if (!Diagnostics.Contains(diagnostic))
            {
                Diagnostics.Add(diagnostic);
            }
        }

        public void AddSource(string hintName, string source)
        {
            HintName = hintName;
            Source = source;
        }
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

        // The member name to use in generated code, escaped when it is a keyword
        public string Identifier => EscapeIdentifier(MemberName);
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
        public bool IsInitOnly { get; set; }
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

        // The converter declared on the member with [TomlConverter]
        public DeclaredConverter? Converter { get; set; }

        // The generated property returning the member's converter type info, when the member has a converter
        public string? ConverterTypeInfoName { get; set; }

        // The generated method setting a member whose setter is not accessible from the context ([TomlInclude])
        public string? SetterAccessorName { get; set; }

        // The setter method is an [UnsafeAccessor] to the init accessor, rather than reflection
        public bool SetterAccessorIsInitAccessor { get; set; }

        // The method setting an init member of an existing instance (ReadInto), when the construction sets it otherwise
        public string? InitSetterAccessorName { get; set; }

        // The property or the field, to report a diagnostic on it
        public ISymbol? Symbol { get; set; }
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

        // The member name to use in generated code, escaped when it is a keyword
        public string Identifier => EscapeIdentifier(MemberName);
        public ITypeSymbol MemberType { get; }
        public ITypeSymbol ValueType { get; }
        public string CreateExpression { get; }
        public bool CanSet { get; }
        public bool IsInitOnly { get; }
        public bool IsCompilerRequired { get; }

        // The member, to report a diagnostic on it
        public ISymbol? Symbol { get; set; }

        // The accessor that sets the member when its setter is not accessible from the generated code or is init-only
        public string? SetterAccessorName { get; set; }
    }

    private sealed class PocoConstructor
    {
        public PocoConstructor(IMethodSymbol? constructor, ImmutableArray<PocoConstructorParameter> parameters, string? errorMessage, bool setsRequiredMembers, string? errorMessageSuffix = null)
        {
            Constructor = constructor;
            Parameters = parameters;
            ErrorMessage = errorMessage;
            ErrorMessageSuffix = errorMessageSuffix;
            SetsRequiredMembers = setsRequiredMembers;
        }

        public IMethodSymbol? Constructor { get; }
        public ImmutableArray<PocoConstructorParameter> Parameters { get; }
        public string? ErrorMessage { get; }

        // When set, the message is ErrorMessage, the full name of the type, then this suffix, like the reflection resolver
        public string? ErrorMessageSuffix { get; }
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

        // The parameter, to report a diagnostic on it
        public IParameterSymbol? Symbol { get; init; }

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
        public bool RequiresGeneratedObjectInitializer { get; set; }
        public int? MappingOrder { get; }
        public int? DottedKeyHandling { get; }
        public bool DisallowUnmappedMembers { get; set; }
        public string TypeName { get; set; } = "";

        // The parameterless constructor has [SetsRequiredMembers], so the object initializer need not set required members
        public bool ParameterlessConstructorSetsRequiredMembers { get; set; }

        // The instance is created with an [UnsafeAccessor] to its parameterless constructor, which does not require the C#
        // required members to be set, so it is populated like any other instance
        public bool UsesConstructorAccessor { get; set; }

        // The identifiers of the C# required members that are not serialized, such as a member with [TomlIgnore]
        public ImmutableArray<string> UnserializedRequiredMembers { get; set; } = ImmutableArray<string>.Empty;
    }

    private static bool RequiresGeneratedObjectInitializer(
        ImmutableArray<PocoMember> members,
        PocoExtensionData? extensionData,
        PocoConstructor? constructor,
        bool parameterlessConstructorSetsRequiredMembers,
        ImmutableArray<string> unserializedRequiredMembers)
    {
        var hasInitOnlyMembers = members.Any(static member => member.IsInitOnly) || extensionData?.IsInitOnly == true;
        if (hasInitOnlyMembers)
        {
            return true;
        }

        var hasCompilerRequiredMembers = members.Any(static member => member.IsCompilerRequired) || extensionData?.IsCompilerRequired == true || !unserializedRequiredMembers.IsEmpty;
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

        // An explicit [TomlIgnore(Condition = Never)]: overrides DefaultIgnoreCondition
        Never = 8,
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
            1 => WriteIgnoreKind.WhenWritingNull,
            2 => WriteIgnoreKind.WhenWritingDefault,
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

    private static bool TryGetPocoShape(GeneratorOutput context, ContextModel model, ITypeSymbol type, out PocoShape shape)
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
        string? constructorErrorSuffix = null;
        var parameterlessConstructorSetsRequiredMembers = false;
        var parameterlessConstructorIsObsoleteError = false;
        if (named.TypeKind is TypeKind.Class or TypeKind.Struct)
        {
            var publicConstructors = named.InstanceConstructors
                .Where(static ctor => !ctor.IsStatic && ctor.DeclaredAccessibility == Accessibility.Public)
                .ToArray();

            // Like the reflection resolver, an annotated constructor can be non-public
            var annotated = named.InstanceConstructors
                .Where(static ctor =>
                    !ctor.IsStatic &&
                    HasAttribute(ctor, "Meziantou.Framework.Toml.Serialization.TomlConstructorAttribute"))
                .ToArray();

            if (annotated.Length > 1)
            {
                constructorError = "Multiple constructors on type '";
                constructorErrorSuffix = "' are annotated with [TomlConstructor].";
                selectedConstructor = annotated[0];
            }
            else if (annotated.Length == 1)
            {
                selectedConstructor = annotated[0];
                if (!IsAccessibleFromGeneratedContext(model, selectedConstructor, named))
                {
                    context.ReportDiagnostic(DiagnosticInfo.Create(InaccessibleConstructor, selectedConstructor.Locations.FirstOrDefault(), named.ToDisplayString()));
                    constructorError = "The constructor annotated for deserialization is not accessible from the generated code.";
                }
            }
            else if (named.TypeKind == TypeKind.Struct)
            {
                // Like System.Text.Json, a struct without an annotated constructor is created with its parameterless constructor
            }
            else if (publicConstructors.Length == 0)
            {
                constructorError = "No suitable constructor could be selected for type '";
                constructorErrorSuffix = "'.";
            }
            else
            {
                if (publicConstructors.FirstOrDefault(static ctor => ctor.Parameters.Length == 0) is { } parameterlessConstructor)
                {
                    parameterlessConstructorIsObsoleteError = IsObsoleteError(parameterlessConstructor);
                    selectedConstructor = null;
                    parameterlessConstructorSetsRequiredMembers = HasAttribute(parameterlessConstructor, SetsRequiredMembersAttributeMetadataName);
                }
                else if (publicConstructors.Length == 1)
                {
                    selectedConstructor = publicConstructors[0];
                }
                else
                {
                    constructorError = "No suitable constructor could be selected for type '";
                    constructorErrorSuffix = "'.";
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

            var hasInclude = HasAttribute(member, "Meziantou.Framework.Toml.Serialization.TomlIncludeAttribute");
            if (member.GetMethod.DeclaredAccessibility != Accessibility.Public && !hasInclude)
            {
                continue;
            }

            var getterAccessible = IsAccessibleFromGeneratedContext(model, member.GetMethod, named) && !IsObsoleteError(member) && !IsObsoleteError(member.GetMethod);
            var getterAccessorName = getterAccessible ? null : "__Get" + members.Count.ToString(CultureInfo.InvariantCulture);
            // Like the reflection resolver, a setter is used when it is public or the member has [TomlInclude]
            var setterAccessible = member.SetMethod is not null && IsAccessibleFromGeneratedContext(model, member.SetMethod, named) && !IsObsoleteError(member) && !IsObsoleteError(member.SetMethod);
            var canSet = member.SetMethod is not null && (member.SetMethod.DeclaredAccessibility == Accessibility.Public || hasInclude);
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
                    context.ReportDiagnostic(DiagnosticInfo.Create(
                        InvalidExtensionDataMember,
                        GetDiagnosticLocation(model, member),
                        type.ToDisplayString(),
                        member.Name,
                        "Multiple extension data members were found. Only a single member can be annotated with [TomlExtensionData]."));
                    continue;
                }

                if (!TryGetExtensionDataValueType(member.Type, out var extensionValueType))
                {
                    context.ReportDiagnostic(DiagnosticInfo.Create(
                        InvalidExtensionDataMember,
                        GetDiagnosticLocation(model, member),
                        type.ToDisplayString(),
                        member.Name,
                        "Extension data members must be dictionary-like with string keys (for example IDictionary<string, object> or TomlTable)."));
                    continue;
                }

                if (!TryGetExtensionDataCreateExpression(member.Type, extensionValueType, out var createExpression))
                {
                    context.ReportDiagnostic(DiagnosticInfo.Create(
                        InvalidExtensionDataMember,
                        GetDiagnosticLocation(model, member),
                        type.ToDisplayString(),
                        member.Name,
                        "Extension data members must be instantiable (public parameterless constructor) or use an interface type such as IDictionary<string, TValue>."));
                    continue;
                }

                extensionData = new PocoExtensionData(member.Name, member.Type, extensionValueType, createExpression, canSet, isInitOnly, isCompilerRequired)
                {
                    Symbol = member,
                    SetterAccessorName = canSet && (!setterAccessible || isInitOnly) ? "__SetExtensionData" : null,
                };
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
            if (HasEmptyTomlPropertyName(member))
            {
                context.ReportDiagnostic(DiagnosticInfo.Create(
                    InvalidAttributeUsage,
                    member.Locations.FirstOrDefault(),
                    type.ToDisplayString(),
                    member.Name,
                    "[TomlPropertyName] cannot be empty. Its constructor throws an ArgumentException."));
                continue;
            }

            var order = GetOrder(member);
            var required = IsRequired(member, honorRequiredModifier) && !ignore.IgnoreOnRead;
            var formatting = GetFormattingMetadata(member);
            if (formatting.StringStyle is not null && member.Type.SpecialType != SpecialType.System_String)
            {
                context.ReportDiagnostic(DiagnosticInfo.Create(
                    InvalidAttributeUsage,
                    member.Locations.FirstOrDefault(),
                    type.ToDisplayString(),
                    member.Name,
                    "[TomlStringStyle] can only be applied to string members."));
                continue;
            }

            var declaredConverter = GetDeclaredConverter(member, member.Type, model);
            if (declaredConverter?.Error is { } converterError)
            {
                context.ReportDiagnostic(DiagnosticInfo.Create(
                    InvalidConverterType,
                    member.Locations.FirstOrDefault(),
                    declaredConverter.ConverterType?.ToDisplayString() ?? "null",
                    converterError));
                continue;
            }

            // An init-only member is set in an object initializer, which replaces its value. To populate it like the
            // reflection resolver does, it is set after the construction with the generated accessor instead.
            var effectiveObjectCreationHandling = objectCreationHandling == ObjectCreationHandlingKind.Default ? GetEffectiveObjectCreationHandling(model.Options) : objectCreationHandling;
            if (isInitOnly && !isCompilerRequired && !member.Type.IsValueType && effectiveObjectCreationHandling == ObjectCreationHandlingKind.Populate)
            {
                isInitOnly = false;
                setterAccessorName ??= "__Set" + members.Count.ToString(CultureInfo.InvariantCulture);
            }

            members.Add(new PocoMember(member.Name, serializedName, member.Type, member.ContainingType, order, writeIgnore, ignore.IgnoreOnRead, objectCreationHandling, hasExplicitObjectCreationHandling, hasSingleOrArray, required, isCompilerRequired, canSet, isInitOnly, isField: false, getterAccessorName, formatting.TableArrayStyle, formatting.InlineTablePolicy, formatting.StringStyle, formatting.PreferLiteralWhenNoEscapes, formatting.AllowHexEscapes)
            {
                OwnerTypeName = ownerTypeName,
                DisallowNullOnSerialize = respectNullableAnnotations && DisallowsNull(member, member.OriginalDefinition.Type, MaybeNullAttributeMetadataName, NotNullAttributeMetadataName),
                DisallowNullOnDeserialize = respectNullableAnnotations && DisallowsNull(member, member.OriginalDefinition.Type, AllowNullAttributeMetadataName, DisallowNullAttributeMetadataName),
                Converter = declaredConverter,
                ConverterTypeInfoName = declaredConverter is null ? null : "MemberConverterTypeInfo" + members.Count.ToString(CultureInfo.InvariantCulture),
                SetterAccessorName = setterAccessorName,
                Symbol = member,
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
                    context.ReportDiagnostic(DiagnosticInfo.Create(
                        InvalidExtensionDataMember,
                        GetDiagnosticLocation(model, member),
                        type.ToDisplayString(),
                        member.Name,
                        "Multiple extension data members were found. Only a single member can be annotated with [TomlExtensionData]."));
                    continue;
                }

                if (!TryGetExtensionDataValueType(member.Type, out var extensionValueType))
                {
                    context.ReportDiagnostic(DiagnosticInfo.Create(
                        InvalidExtensionDataMember,
                        GetDiagnosticLocation(model, member),
                        type.ToDisplayString(),
                        member.Name,
                        "Extension data members must be dictionary-like with string keys (for example IDictionary<string, object> or TomlTable)."));
                    continue;
                }

                if (!TryGetExtensionDataCreateExpression(member.Type, extensionValueType, out var createExpression))
                {
                    context.ReportDiagnostic(DiagnosticInfo.Create(
                        InvalidExtensionDataMember,
                        GetDiagnosticLocation(model, member),
                        type.ToDisplayString(),
                        member.Name,
                        "Extension data members must be instantiable (public parameterless constructor) or use an interface type such as IDictionary<string, TValue>."));
                    continue;
                }

                extensionData = new PocoExtensionData(member.Name, member.Type, extensionValueType, createExpression, canSet: true, isInitOnly: false, isCompilerRequired: member.IsRequired) { Symbol = member };
                continue;
            }

            var hasInclude = HasAttribute(member, "Meziantou.Framework.Toml.Serialization.TomlIncludeAttribute");
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
            if (HasEmptyTomlPropertyName(member))
            {
                context.ReportDiagnostic(DiagnosticInfo.Create(
                    InvalidAttributeUsage,
                    member.Locations.FirstOrDefault(),
                    type.ToDisplayString(),
                    member.Name,
                    "[TomlPropertyName] cannot be empty. Its constructor throws an ArgumentException."));
                continue;
            }

            var order = GetOrder(member);
            var required = IsRequired(member, honorRequiredModifier) && !ignore.IgnoreOnRead;
            var fieldAccessible = IsAccessibleFromGeneratedContext(model, member, named) && !IsObsoleteError(member);
            var getterAccessorName = fieldAccessible ? null : "__Get" + members.Count.ToString(CultureInfo.InvariantCulture);
            var canSet = !member.IsReadOnly && (fieldAccessible || hasInclude);
            var setterAccessorName = canSet && !fieldAccessible ? "__Set" + members.Count.ToString(CultureInfo.InvariantCulture) : null;
            var fieldWriteIgnore = member.IsReadOnly && model.Options.IgnoreReadOnlyFields == true ? WriteIgnoreKind.WhenWriting : ignore.WriteIgnore;
            var formatting = GetFormattingMetadata(member);
            if (formatting.StringStyle is not null && member.Type.SpecialType != SpecialType.System_String)
            {
                context.ReportDiagnostic(DiagnosticInfo.Create(
                    InvalidAttributeUsage,
                    member.Locations.FirstOrDefault(),
                    type.ToDisplayString(),
                    member.Name,
                    "[TomlStringStyle] can only be applied to string members."));
                continue;
            }

            var declaredConverter = GetDeclaredConverter(member, member.Type, model);
            if (declaredConverter?.Error is { } converterError)
            {
                context.ReportDiagnostic(DiagnosticInfo.Create(
                    InvalidConverterType,
                    member.Locations.FirstOrDefault(),
                    declaredConverter.ConverterType?.ToDisplayString() ?? "null",
                    converterError));
                continue;
            }

            members.Add(new PocoMember(member.Name, serializedName, member.Type, member.ContainingType, order, fieldWriteIgnore, ignore.IgnoreOnRead, objectCreationHandling, hasExplicitObjectCreationHandling, hasSingleOrArray, required, member.IsRequired, canSet, isInitOnly: false, isField: true, getterAccessorName, formatting.TableArrayStyle, formatting.InlineTablePolicy, formatting.StringStyle, formatting.PreferLiteralWhenNoEscapes, formatting.AllowHexEscapes)
            {
                OwnerTypeName = ownerTypeName,
                DisallowNullOnSerialize = respectNullableAnnotations && DisallowsNull(member, member.OriginalDefinition.Type, MaybeNullAttributeMetadataName, NotNullAttributeMetadataName),
                DisallowNullOnDeserialize = respectNullableAnnotations && DisallowsNull(member, member.OriginalDefinition.Type, AllowNullAttributeMetadataName, DisallowNullAttributeMetadataName),
                Converter = declaredConverter,
                ConverterTypeInfoName = declaredConverter is null ? null : "MemberConverterTypeInfo" + members.Count.ToString(CultureInfo.InvariantCulture),
                SetterAccessorName = setterAccessorName,
                Symbol = member,
            });
        }

        // Declaration order, the same as the reflection resolver: the members of the base types first, then in each type
        // the fields then the properties, in declaration order
        var orderedMembers = members
            .Select(static (member, index) => (Member: member, Index: index))
            .OrderBy(static item => GetInheritanceDepth(item.Member.DeclaringType))
            .ThenBy(static item => item.Member.IsField ? 0 : 1)
            .ThenBy(static item => item.Index)
            .Select(static item => item.Member)
            .ToArray();
        members.Clear();
        members.AddRange(orderedMembers);

        // A field hiding a base property, or a property hiding a base field, hides it like a member of the same kind
        for (var i = members.Count - 1; i >= 0; i--)
        {
            var member = members[i];
            if (members.Any(other => !ReferenceEquals(other, member) && other.MemberName == member.MemberName && DerivesFrom(other.DeclaringType, member.DeclaringType)))
            {
                members.RemoveAt(i);
            }
        }

        // Reading would fill only one of the members, and writing would write the key twice
        var membersBySerializedName = new Dictionary<string, PocoMember>(GetEffectivePropertyNameCaseInsensitive(model.Options) ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        foreach (var member in members)
        {
            if (!membersBySerializedName.TryAdd(member.SerializedName, member))
            {
                context.ReportDiagnostic(DiagnosticInfo.Create(
                    InvalidAttributeUsage,
                    member.Symbol?.Locations.FirstOrDefault() ?? named.Locations.FirstOrDefault(),
                    type.ToDisplayString(),
                    member.MemberName,
                    $"The TOML key '{member.SerializedName}' is also used by the member '{membersBySerializedName[member.SerializedName].MemberName}'."));
            }
        }

        var unserializedRequiredMembers = GetUnserializedRequiredMembers(named, members, extensionData);

        PocoConstructor? constructorModel = null;
        if (constructorError is not null && selectedConstructor is null)
        {
            constructorModel = new PocoConstructor(null, ImmutableArray<PocoConstructorParameter>.Empty, constructorError, setsRequiredMembers: false, constructorErrorSuffix);
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

                // The instance is created after the members are read, so a get-only member cannot be populated; like the
                // reflection resolver, an explicit Populate on it is an error rather than a silently lost value
                var parameterNames = new HashSet<string>(selectedConstructor.Parameters.Select(static parameter => parameter.Name), StringComparer.OrdinalIgnoreCase);
                foreach (var member in membersSoFar)
                {
                    if (!member.CanSet && !member.HasSingleOrArray && member.HasExplicitObjectCreationHandling &&
                        member.ObjectCreationHandling == ObjectCreationHandlingKind.Populate && !parameterNames.Contains(member.MemberName))
                    {
                        context.ReportDiagnostic(DiagnosticInfo.Create(
                            InvalidAttributeUsage,
                            member.Symbol?.Locations.FirstOrDefault() ?? named.Locations.FirstOrDefault(),
                            type.ToDisplayString(),
                            member.MemberName,
                            "[TomlObjectCreationHandling(Populate)] cannot be used on a get-only member of a type created with a constructor that has parameters."));
                    }
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

                    var parameterDisallowNull = respectNullableAnnotations && DisallowsNull(parameter, parameter.OriginalDefinition.Type, AllowNullAttributeMetadataName, DisallowNullAttributeMetadataName);
                    parameters.Add(new PocoConstructorParameter(keyName, parameterName, parameter.Type, hasDefaultValue, defaultValueExpression, linkedMemberIndex, parameterDisallowNull)
                    {
                        Symbol = parameter,
                        ConverterTypeInfoName = linkedMemberIndex >= 0 ? membersSoFar[linkedMemberIndex].ConverterTypeInfoName : null,
                    });
                }

                constructorModel = new PocoConstructor(selectedConstructor, parameters.ToImmutable(), constructorError, setsRequiredMembers, constructorErrorSuffix);
                shape = new PocoShape(
                    membersSoFar,
                    extensionData,
                    constructorModel,
                    RequiresGeneratedObjectInitializer(membersSoFar, extensionData, constructorModel, parameterlessConstructorSetsRequiredMembers, unserializedRequiredMembers),
                    typeMappingOrder,
                    typeDottedKeyHandling)
                {
                    DisallowUnmappedMembers = disallowUnmappedMembers,
                    TypeName = ownerTypeName,
                    ParameterlessConstructorSetsRequiredMembers = parameterlessConstructorSetsRequiredMembers,
                    UnserializedRequiredMembers = unserializedRequiredMembers,
                };
                AssignInitSetterAccessorNames(membersSoFar);
                return true;
            }

            constructorModel = new PocoConstructor(selectedConstructor, ImmutableArray<PocoConstructorParameter>.Empty, constructorError, setsRequiredMembers, constructorErrorSuffix);
        }

        var finalMembers = members.ToImmutable();
        shape = new PocoShape(
            finalMembers,
            extensionData,
            constructorModel,
            RequiresGeneratedObjectInitializer(finalMembers, extensionData, constructorModel, parameterlessConstructorSetsRequiredMembers, unserializedRequiredMembers),
            typeMappingOrder,
            typeDottedKeyHandling)
        {
            DisallowUnmappedMembers = disallowUnmappedMembers,
            TypeName = ownerTypeName,
            ParameterlessConstructorSetsRequiredMembers = parameterlessConstructorSetsRequiredMembers,
            UnserializedRequiredMembers = unserializedRequiredMembers,
        };

        // An object initializer replaces the values of the members, so they cannot be populated, and it must set every
        // required member, even one that is not read. When possible, the instance is created without it, like the reflection
        // resolver does.
        if (shape.RequiresGeneratedObjectInitializer &&
            named.TypeKind == TypeKind.Class &&
            constructorError is null &&
            selectedConstructor is null or { Parameters.Length: 0 } &&
            extensionData is not { IsInitOnly: true } &&
            CanUseInitAccessor(model, named) &&
            finalMembers.All(member => !member.IsInitOnly || member.SetterAccessorName is not null || CanUseInitAccessor(model, member.DeclaringType)))
        {
            for (var i = 0; i < finalMembers.Length; i++)
            {
                var member = finalMembers[i];
                if (member.IsInitOnly && member.SetterAccessorName is null)
                {
                    // The other setter accessors are named after the index of the member when it was found, before the
                    // members were ordered, so this one needs its own prefix
                    member.SetterAccessorName = "__InitSet" + i.ToString(CultureInfo.InvariantCulture);
                    member.SetterAccessorIsInitAccessor = true;
                    member.IsInitOnly = false;
                }
            }

            shape.RequiresGeneratedObjectInitializer = false;
            shape.UsesConstructorAccessor = !(selectedConstructor is not null ? HasAttribute(selectedConstructor, SetsRequiredMembersAttributeMetadataName) : parameterlessConstructorSetsRequiredMembers);
        }

        AssignInitSetterAccessorNames(finalMembers);

        // A constructor that is an error to use can still be called through an accessor
        if (parameterlessConstructorIsObsoleteError && !shape.RequiresGeneratedObjectInitializer && named.TypeKind == TypeKind.Class && CanUseInitAccessor(model, named))
        {
            shape.UsesConstructorAccessor = true;
        }

        return true;
    }

    // An init member that the construction sets (an object initializer or an accessor) is set with an accessor when an existing
    // instance is populated
    private static void AssignInitSetterAccessorNames(ImmutableArray<PocoMember> members)
    {
        for (var i = 0; i < members.Length; i++)
        {
            var member = members[i];
            if (member.IsInitOnly && member.CanSet && member.SetterAccessorName is null)
            {
                member.InitSetterAccessorName = "__PopulateInit" + i.ToString(CultureInfo.InvariantCulture);
            }
        }
    }

    // The C# required members that are not serialized ([TomlIgnore], a non-public getter, a field without IncludeFields)
    private static ImmutableArray<string> GetUnserializedRequiredMembers(INamedTypeSymbol type, ImmutableArray<PocoMember>.Builder members, PocoExtensionData? extensionData)
    {
        var serializedNames = new HashSet<string>(members.Select(static member => member.MemberName), StringComparer.Ordinal);
        if (extensionData is not null)
        {
            serializedNames.Add(extensionData.MemberName);
        }

        var seenNames = new HashSet<string>(StringComparer.Ordinal);
        var result = ImmutableArray.CreateBuilder<string>();
        for (var current = type; current is not null; current = current.BaseType)
        {
            foreach (var member in current.GetMembers())
            {
                var isRequired = member switch
                {
                    IPropertySymbol { IsStatic: false } property => property.IsRequired,
                    IFieldSymbol { IsStatic: false } field => field.IsRequired,
                    _ => false,
                };

                if (isRequired && seenNames.Add(member.Name) && !serializedNames.Contains(member.Name))
                {
                    result.Add(EscapeIdentifier(member.Name));
                }
            }
        }

        return result.ToImmutable();
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

                if (hiddenPropertyNames.Contains(member.Name))
                {
                    continue;
                }

                // Only a member that could be serialized hides the base members: a private 'new' member does not, like in
                // System.Text.Json
                if (member.GetMethod?.DeclaredAccessibility == Accessibility.Public ||
                    member.SetMethod?.DeclaredAccessibility == Accessibility.Public ||
                    HasAttribute(member, "Meziantou.Framework.Toml.Serialization.TomlIncludeAttribute"))
                {
                    hiddenPropertyNames.Add(member.Name);
                }

                yield return member;
            }
        }
    }

    private static int GetInheritanceDepth(ITypeSymbol type)
    {
        var depth = 0;
        for (var current = type.BaseType; current is not null; current = current.BaseType)
        {
            depth++;
        }

        return depth;
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

    // An internal or protected internal member of another assembly is only accessible with InternalsVisibleTo. The generated
    // code accesses members through a value of the model type, so a protected member is not accessible from a context
    // nested in a derived type either.
    private static bool IsAccessibleFromGeneratedContext(ContextModel model, ISymbol member, ITypeSymbol throughType)
    {
        return model.Compilation.IsSymbolAccessibleWithin(member, model.ContextSymbol, throughType);
    }

    private static bool TryGetPolymorphicShape(
        GeneratorOutput context,
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
        int? tomlUnknownHandling = null;

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
                            context.ReportDiagnostic(DiagnosticInfo.Create(
                                InvalidPolymorphismConfiguration,
                                GetDiagnosticLocation(model, attr),
                                type.ToDisplayString(),
                                "Only one default derived type (no discriminator) can be registered."));
                        }
                        continue;
                    }

                    if (!ValidateDerivedType(defaultType, discriminator: null, GetDiagnosticLocation(model, attr)))
                    {
                        continue;
                    }

                    if (!derivedTypeSet.Add(defaultType))
                    {
                        if (reportDiagnostics)
                        {
                            context.ReportDiagnostic(DiagnosticInfo.Create(
                                InvalidPolymorphismConfiguration,
                                GetDiagnosticLocation(model, attr),
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
                            context.ReportDiagnostic(DiagnosticInfo.Create(
                                InvalidPolymorphismConfiguration,
                                GetDiagnosticLocation(model, attr),
                                type.ToDisplayString(),
                            "TomlDerivedTypeAttribute must specify a derived type and a non-empty discriminator."));
                        }
                        continue;
                    }

                    AddStrictDerivedType(derivedType, discriminator, GetDiagnosticLocation(model, attr));
                }
                else
                {
                    if (reportDiagnostics)
                    {
                        context.ReportDiagnostic(DiagnosticInfo.Create(
                            InvalidPolymorphismConfiguration,
                            GetDiagnosticLocation(model, attr),
                            type.ToDisplayString(),
                            "TomlDerivedTypeAttribute must specify a derived type and an optional discriminator."));
                    }
                    continue;
                }
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
                TryAddLowerPrecedenceDefaultDerivedType(mapping.DerivedType, model.ContextSymbol.Locations.FirstOrDefault());
            }
            else
            {
                TryAddLowerPrecedenceDerivedType(mapping.DerivedType, mapping.Discriminator, model.ContextSymbol.Locations.FirstOrDefault());
            }
        }

        if (derived.Count == 0 && defaultDerivedType is null)
        {
            return false;
        }

        // Priority chain: TomlPolymorphicAttribute → options (null = use options)
        shape = new PolymorphicShape(tomlDiscriminatorPropertyName, derived.ToImmutable(), defaultDerivedType, tomlUnknownHandling);
        return true;

        void AddStrictDerivedType(ITypeSymbol derivedType, string discriminator, Location? location)
        {
            if (!discriminatorSet.Add(discriminator))
            {
                if (reportDiagnostics)
                {
                    context.ReportDiagnostic(DiagnosticInfo.Create(
                        InvalidPolymorphismConfiguration,
                        location,
                        type.ToDisplayString(),
                        $"Discriminator '{discriminator}' is registered more than once."));
                }
                return;
            }

            if (!derivedTypeSet.Add(derivedType))
            {
                if (reportDiagnostics)
                {
                    context.ReportDiagnostic(DiagnosticInfo.Create(
                        InvalidPolymorphismConfiguration,
                        location,
                        type.ToDisplayString(),
                        $"Derived type '{derivedType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}' is registered more than once."));
                }
                return;
            }

            if (!ValidateDerivedType(derivedType, discriminator, location))
            {
                return;
            }

            derived.Add(new PolymorphicDerivedType(derivedType, discriminator));
        }

        void TryAddLowerPrecedenceDefaultDerivedType(ITypeSymbol derivedType, Location? location)
        {
            if (!ValidateDerivedType(derivedType, discriminator: null, location))
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

        void TryAddLowerPrecedenceDerivedType(ITypeSymbol derivedType, string discriminator, Location? location)
        {
            if (!ValidateDerivedType(derivedType, discriminator, location))
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

        bool ValidateDerivedType(ITypeSymbol derivedType, string? discriminator, Location? location)
        {
            if (discriminator is not null && discriminator.Length == 0)
            {
                if (reportDiagnostics)
                {
                    context.ReportDiagnostic(DiagnosticInfo.Create(
                        InvalidPolymorphismConfiguration,
                        location,
                        type.ToDisplayString(),
                        "Derived type discriminators cannot be empty."));
                }

                return false;
            }

            if (!IsAssignableTo(derivedType, type))
            {
                if (reportDiagnostics)
                {
                    context.ReportDiagnostic(DiagnosticInfo.Create(
                        InvalidPolymorphismConfiguration,
                        location,
                        type.ToDisplayString(),
                        $"Derived type '{derivedType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}' is not assignable to base type '{type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}'."));
                }

                return false;
            }

            if (derivedType is INamedTypeSymbol derivedNamed && (derivedNamed.TypeKind != TypeKind.Class || derivedNamed.IsAbstract))
            {
                if (reportDiagnostics)
                {
                    context.ReportDiagnostic(DiagnosticInfo.Create(
                        InvalidPolymorphismConfiguration,
                        location,
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
               HasAttribute(member, "Meziantou.Framework.Toml.Serialization.TomlRequiredAttribute");
    }

    private static bool IsExtensionData(ISymbol member)
    {
        return HasAttribute(member, "Meziantou.Framework.Toml.Serialization.TomlExtensionDataAttribute");
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

        // The value of a nullable parameter is the value of its underlying type, which converts to the nullable type
        if (TryGetNullableUnderlyingType(type, out var nullableUnderlyingType))
        {
            return TryGetDefaultValueExpression(nullableUnderlyingType, value, out expression);
        }

        if (type.TypeKind == TypeKind.Enum)
        {
            // The value is in parentheses: (T)-1 would be a subtraction
            var enumTypeName = type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            string? literal = value switch
            {
                sbyte or byte or short or ushort or int => Convert.ToInt64(value, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture),
                uint u32 => u32.ToString(CultureInfo.InvariantCulture) + "U",
                long i64 => i64.ToString(CultureInfo.InvariantCulture) + "L",
                ulong u64 => u64.ToString(CultureInfo.InvariantCulture) + "UL",
                _ => null,
            };
            if (literal is null)
            {
                return false;
            }

            expression = $"({enumTypeName})({literal})";
            return true;
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

            // A HashSet<T> implements both interfaces
            if (constructedFrom is "global::System.Collections.Generic.ISet<T>" or "global::System.Collections.Generic.IReadOnlySet<T>")
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

    // The converter declared with [TomlConverter], resolved like the reflection resolver does
    // Without a model, only the shape of the converter is checked, which is enough to know whether there is one
    private static DeclaredConverter? GetDeclaredConverter(ISymbol symbol, ITypeSymbol convertedType, ContextModel? model = null)
    {
        if (!TryGetAttribute(symbol, TomlConverterAttributeMetadataName, out var attribute))
        {
            return null;
        }

        var converterType = attribute.ConstructorArguments.Length == 1 ? attribute.ConstructorArguments[0].Value as ITypeSymbol : null;

        // The string enum converter has its own metadata, which also converts a T? member
        var enumType = TryGetNullableUnderlyingType(convertedType, out var underlyingType) ? underlyingType : convertedType;
        if (enumType.TypeKind == TypeKind.Enum && converterType?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) == "global::" + TomlStringEnumConverterMetadataName)
        {
            return new DeclaredConverter(converterType, isStringEnum: true, error: null);
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
        else if (model is null ? !IsTypeAccessibleFromGeneratedContext(named) : FindInaccessibleType(model, named) is not null)
        {
            error = "Converters must be accessible from the generated context (public, or internal to the same assembly).";
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

    // TomlStringEnumConverter is not a TomlConverter<T>, so it is not matched to a type like the other converters of the options
    private static bool HasOptionsStringEnumConverter(SourceGenOptions options)
        => !options.ConverterTypes.IsDefaultOrEmpty &&
           options.ConverterTypes.Any(static converterType => converterType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) == "global::" + TomlStringEnumConverterMetadataName);

    // [Obsolete(message, error: true)]: the member cannot be used directly, even with the warning disabled
    private static bool IsObsoleteError(ISymbol? symbol)
        => symbol is not null &&
           TryGetAttribute(symbol, "System.ObsoleteAttribute", out var attribute) &&
           attribute.ConstructorArguments is [_, { Value: true }];

    private static ImmutableArray<string> GetExperimentalDiagnosticIds(ImmutableArray<ITypeSymbol> types)
    {
        var ids = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var type in types)
        {
            for (var current = type.OriginalDefinition as INamedTypeSymbol; current is not null; current = current.BaseType)
            {
                Add(current);
                for (var containing = current.ContainingType; containing is not null; containing = containing.ContainingType)
                {
                    Add(containing);
                }

                foreach (var member in current.GetMembers())
                {
                    Add(member);
                }
            }
        }

        return ids.ToImmutableArray();

        void Add(ISymbol symbol)
        {
            if (TryGetAttribute(symbol, "System.Diagnostics.CodeAnalysis.ExperimentalAttribute", out var attribute) &&
                attribute.ConstructorArguments is [{ Value: string diagnosticId }] &&
                SyntaxFacts.IsValidIdentifier(diagnosticId))
            {
                ids.Add(diagnosticId);
            }
        }
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

    private static bool HasEmptyTomlPropertyName(ISymbol member)
        => TryGetAttribute(member, "Meziantou.Framework.Toml.Serialization.TomlPropertyNameAttribute", out var attribute) &&
            attribute.ConstructorArguments is [{ Value: string { Length: 0 } }];

    private static string GetSerializedName(ISymbol member, string memberName, string? namingPolicyExpression)
    {
        if (TryGetAttribute(member, "Meziantou.Framework.Toml.Serialization.TomlPropertyNameAttribute", out var tomlAttr) &&
            tomlAttr.ConstructorArguments.Length == 1 &&
            tomlAttr.ConstructorArguments[0].Value is string tomlName)
        {
            return tomlName;
        }

        if (namingPolicyExpression is not null && TryConvertKnownName(memberName, namingPolicyExpression, out var converted))
        {
            return converted;
        }

        return memberName;
    }

    private static ObjectCreationHandlingKind GetObjectCreationHandling(ISymbol symbol)
    {
        if (TryGetAttribute(symbol, TomlObjectCreationHandlingAttributeMetadataName, out var attr))
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
                0 => new IgnoreBehavior(ignoreAlways: false, ignoreOnRead: false, writeIgnore: WriteIgnoreKind.Never),
                1 => new IgnoreBehavior(ignoreAlways: false, ignoreOnRead: false, writeIgnore: WriteIgnoreKind.WhenWritingNull),
                2 => new IgnoreBehavior(ignoreAlways: false, ignoreOnRead: false, writeIgnore: WriteIgnoreKind.WhenWritingDefault),
                4 => new IgnoreBehavior(ignoreAlways: false, ignoreOnRead: false, writeIgnore: WriteIgnoreKind.WhenWriting),
                5 => new IgnoreBehavior(ignoreAlways: false, ignoreOnRead: true, writeIgnore: WriteIgnoreKind.None),
                _ => new IgnoreBehavior(ignoreAlways: true, ignoreOnRead: false, writeIgnore: WriteIgnoreKind.None),
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
        var usedIdentifiers = GetReservedIdentifiers(model);

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

        bool IsAvailable(string name) => !IsReservedTypeInfoName(usedIdentifiers, name);

        void Assign(ITypeSymbol type, string name)
        {
            names[type] = name;
            usedIdentifiers.Add(name);
            usedIdentifiers.Add("_" + name);
            usedIdentifiers.Add("Create" + name);
            usedIdentifiers.Add("__TomlTypeInfo_" + name);
        }
    }

    // The members generated for the context, the members inherited from TomlSerializerContext and object, and the members
    // declared by the user
    private static HashSet<string> GetReservedIdentifiers(ContextModel model)
    {
        var identifiers = new HashSet<string>(StringComparer.Ordinal)
        {
            model.TypeName,
            "Default",
            "Options",
            "GetTypeInfo",
            "CreateDefaultOptions",
            "s_sourceGenerationConverterTypes",
        };

        for (var current = model.ContextSymbol; current is not null; current = current.BaseType)
        {
            foreach (var member in current.GetMembers())
            {
                identifiers.Add(member.Name);
            }
        }

        return identifiers;
    }

    // The metadata of a type uses a property, a field, a factory method, and a nested class named after it
    private static bool IsReservedTypeInfoName(HashSet<string> reservedIdentifiers, string name)
        => reservedIdentifiers.Contains(name) ||
           reservedIdentifiers.Contains("_" + name) ||
           reservedIdentifiers.Contains("Create" + name) ||
           reservedIdentifiers.Contains("__TomlTypeInfo_" + name);

    private static string GetTypeInfoAccess(ITypeSymbol type)
        => "GetTypeInfo<" + type.ToDisplayString(FullyQualifiedNullableFormat) + ">(_context." + GetTypeInfoPropertyName(type) + ")";

    private static string GetTypeInfoReadExpression(ITypeSymbol type)
        => "(" + type.ToDisplayString(FullyQualifiedNullableFormat) + ")" + GetTypeInfoAccess(type) + ".ReadAsObject(reader)!";

    private const string AllowNullAttributeMetadataName = "System.Diagnostics.CodeAnalysis.AllowNullAttribute";
    private const string MaybeNullAttributeMetadataName = "System.Diagnostics.CodeAnalysis.MaybeNullAttribute";
    private const string NotNullAttributeMetadataName = "System.Diagnostics.CodeAnalysis.NotNullAttribute";
    private const string DisallowNullAttributeMetadataName = "System.Diagnostics.CodeAnalysis.DisallowNullAttribute";

    // Whether null is rejected, like NullabilityInfoContext in the reflection resolver: the declared type of the member
    // decides (a type parameter is nullable unless it has a class constraint), and the attributes override it
    private static bool DisallowsNull(ISymbol symbol, ITypeSymbol declaredType, string allowAttributeMetadataName, string disallowAttributeMetadataName)
    {
        if (declaredType.IsValueType)
        {
            return false;
        }

        if (HasAttribute(symbol, disallowAttributeMetadataName))
        {
            return true;
        }

        if (HasAttribute(symbol, allowAttributeMetadataName))
        {
            return false;
        }

        if (declaredType is ITypeParameterSymbol typeParameter)
        {
            return typeParameter.HasReferenceTypeConstraint && typeParameter.ReferenceTypeConstraintNullableAnnotation != NullableAnnotation.Annotated;
        }

        return IsNonNullableReferenceType(declaredType);
    }

    // Only reference types annotated as non-nullable are enforced, like the reflection resolver
    private static bool IsNonNullableReferenceType(ITypeSymbol type)
        => type.IsReferenceType && type.NullableAnnotation == NullableAnnotation.NotAnnotated;

    private static string EscapeInterpolatedStringLiteral(string value)
        => EscapeStringLiteral(value).Replace("{", "{{", StringComparison.Ordinal).Replace("}", "}}", StringComparison.Ordinal);

    // The type of the cast before '?? throw': it accepts null whatever the annotation of the declared type, for example a
    // [DisallowNull] string? member
    private static string GetNullableTypeDisplay(ITypeSymbol type)
    {
        if (type.IsValueType)
        {
            var display = type.ToDisplayString(FullyQualifiedNullableFormat);
            return type.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T ? display : display + "?";
        }

        return type.WithNullableAnnotation(NullableAnnotation.Annotated).ToDisplayString(FullyQualifiedNullableFormat);
    }

    // Reads a member value, rejecting null when the member is declared as non-nullable
    private static string GetMemberTypeInfoAccess(PocoMember member)
        => member.ConverterTypeInfoName ?? GetTypeInfoAccess(member.Type);

    private static string GetMemberReadExpression(PocoMember member)
    {
        if (!member.DisallowNullOnDeserialize)
        {
            return "(" + member.Type.ToDisplayString(FullyQualifiedNullableFormat) + ")" + GetMemberTypeInfoAccess(member) + ".ReadAsObject(reader)!";
        }

        return "((" + GetNullableTypeDisplay(member.Type) + ")" + GetMemberTypeInfoAccess(member) + ".ReadAsObject(reader) ?? throw reader.CreateException($\"The TOML key '" +
            EscapeInterpolatedStringLiteral(member.SerializedName) + "' cannot be null because '{typeof(" + member.OwnerTypeName + ").FullName}' declares it as non-nullable.\"))";
    }

    private static string GetConstructorParameterReadExpression(PocoConstructorParameter parameter, string typeName)
    {
        var typeInfoAccess = parameter.ConverterTypeInfoName ?? GetTypeInfoAccess(parameter.ParameterType);
        if (!parameter.DisallowNull)
        {
            return "(" + parameter.ParameterType.ToDisplayString(FullyQualifiedNullableFormat) + ")" + typeInfoAccess + ".ReadAsObject(reader)!";
        }

        return "((" + GetNullableTypeDisplay(parameter.ParameterType) + ")" + typeInfoAccess + ".ReadAsObject(reader) ?? throw reader.CreateException($\"The constructor parameter '" +
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

    // [TomlUnmappedMemberHandling] applies to the declaring type only, and takes precedence over the options
    private static int GetUnmappedMemberHandling(INamedTypeSymbol type, SourceGenOptions options)
    {
        foreach (var attribute in type.GetAttributes())
        {
            if (attribute.AttributeClass?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) == "global::Meziantou.Framework.Toml.Serialization.TomlUnmappedMemberHandlingAttribute" &&
                attribute.ConstructorArguments.Length == 1 &&
                attribute.ConstructorArguments[0].Value is int value)
            {
                return value;
            }
        }

        return options.UnmappedMemberHandling ?? 0;
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

    private static string EscapeIdentifier(string name)
        => SyntaxFacts.GetKeywordKind(name) != SyntaxKind.None ? "@" + name : name;

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

        // A keyword cannot be used as an identifier, and the name is also used as a prefix and a suffix of other names
        if ((!char.IsLetter(builder[0]) && builder[0] != '_') || SyntaxFacts.GetKeywordKind(builder.ToString()) != SyntaxKind.None)
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


    private static bool IsTomlSourceGenerationOptionsAttribute(AttributeData attribute)
        => attribute.AttributeClass?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) == "global::" + TomlSourceGenerationOptionsAttributeMetadataName;

    private static ImmutableArray<DerivedTypeMappingModel> ValidateDerivedTypeMappings(GeneratorOutput context, ContextModel model)
    {
        var builder = ImmutableArray.CreateBuilder<DerivedTypeMappingModel>(model.DerivedTypeMappings.Length);
        var warnedBaseTypes = new HashSet<ITypeSymbol>(SymbolEqualityComparer.Default);

        foreach (var mapping in model.DerivedTypeMappings)
        {
            if (mapping.Discriminator is { Length: 0 })
            {
                context.ReportDiagnostic(DiagnosticInfo.Create(
                    InvalidDerivedTypeMapping,
                    mapping.Location ?? model.ContextSymbol.Locations.FirstOrDefault(),
                    model.ContextSymbol.ToDisplayString(),
                    $"Derived type mapping from '{mapping.BaseType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)}' to '{mapping.DerivedType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)}' must use a non-empty discriminator."));
                continue;
            }

            if (!IsAssignableTo(mapping.DerivedType, mapping.BaseType))
            {
                context.ReportDiagnostic(DiagnosticInfo.Create(
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
                context.ReportDiagnostic(DiagnosticInfo.Create(
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
                context.ReportDiagnostic(DiagnosticInfo.Create(
                    MissingPolymorphicConfigurationOnDerivedTypeMappingBase,
                    mapping.Location ?? model.ContextSymbol.Locations.FirstOrDefault(),
                    model.ContextSymbol.ToDisplayString(),
                    mapping.BaseType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)));
            }

            builder.Add(mapping);
        }

        return builder.ToImmutable();
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

    private static void ValidateConverters(GeneratorOutput context, ContextModel model)
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
                context.ReportDiagnostic(DiagnosticInfo.Create(InvalidConverterType, model.ContextSymbol.Locations.FirstOrDefault(), type.ToDisplayString(), "Converters must be classes."));
                continue;
            }

            if (named.IsAbstract)
            {
                context.ReportDiagnostic(DiagnosticInfo.Create(InvalidConverterType, model.ContextSymbol.Locations.FirstOrDefault(), type.ToDisplayString(), "Converters must not be abstract."));
                continue;
            }

            if (!DerivesFrom(named, "global::" + TomlConverterMetadataName))
            {
                context.ReportDiagnostic(DiagnosticInfo.Create(InvalidConverterType, model.ContextSymbol.Locations.FirstOrDefault(), type.ToDisplayString(), $"Converters must derive from {TomlConverterMetadataName}."));
                continue;
            }

            if (!named.Constructors.Any(static c => c.Parameters.Length == 0 && c.DeclaredAccessibility == Accessibility.Public))
            {
                context.ReportDiagnostic(DiagnosticInfo.Create(InvalidConverterType, model.ContextSymbol.Locations.FirstOrDefault(), type.ToDisplayString(), "Converters must have a public parameterless constructor."));
                continue;
            }

            if (DerivesFrom(named, "global::" + TomlConverterFactoryMetadataName))
            {
                context.ReportDiagnostic(DiagnosticInfo.Create(UnusedConverterFactory, model.ContextSymbol.Locations.FirstOrDefault(), type.ToDisplayString()));
            }
        }
    }

    private static void ValidateSourceGenerationOptions(GeneratorOutput context, ContextModel model)
    {
        if (model.Options.IndentSize is not null && model.Options.IndentSize.Value < 1)
        {
            context.ReportDiagnostic(DiagnosticInfo.Create(
                InvalidSourceGenerationOption,
                model.ContextSymbol.Locations.FirstOrDefault(),
                model.ContextSymbol.ToDisplayString(),
                "IndentSize must be at least 1."));
            model.Options.IndentSize = null;
        }

        ValidateEnumOption(context, model, "PreferredObjectCreationHandling", model.Options.PreferredObjectCreationHandling, TryGetObjectCreationHandlingExpression, v => model.Options.PreferredObjectCreationHandling = v);
        ValidateEnumOption(context, model, "NewLine", model.Options.NewLine, TryGetTomlNewLineKindExpression, v => model.Options.NewLine = v);
        ValidateEnumOption(context, model, "DefaultIgnoreCondition", model.Options.DefaultIgnoreCondition, TryGetTomlIgnoreConditionExpression, v => model.Options.DefaultIgnoreCondition = v);

        // Same rule as TomlSerializerOptions.DefaultIgnoreCondition: Never, WhenWritingNull, or WhenWritingDefault
        if (model.Options.DefaultIgnoreCondition is > 2)
        {
            context.ReportDiagnostic(DiagnosticInfo.Create(
                InvalidSourceGenerationOption,
                model.ContextSymbol.Locations.FirstOrDefault(),
                model.ContextSymbol.ToDisplayString(),
                "DefaultIgnoreCondition must be Never, WhenWritingNull, or WhenWritingDefault."));
            model.Options.DefaultIgnoreCondition = null;
        }
        ValidateEnumOption(context, model, "DuplicateKeyHandling", model.Options.DuplicateKeyHandling, TryGetTomlDuplicateKeyHandlingExpression, v => model.Options.DuplicateKeyHandling = v);
        ValidateEnumOption(context, model, "UnmappedMemberHandling", model.Options.UnmappedMemberHandling, TryGetTomlUnmappedMemberHandlingExpression, v => model.Options.UnmappedMemberHandling = v);
        if (model.Options.MaxDepth is not null && model.Options.MaxDepth.Value < 0)
        {
            context.ReportDiagnostic(DiagnosticInfo.Create(
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
            context.ReportDiagnostic(DiagnosticInfo.Create(
                InvalidSourceGenerationOption,
                model.ContextSymbol.Locations.FirstOrDefault(),
                model.ContextSymbol.ToDisplayString(),
                "RootValueKeyName cannot be empty or whitespace."));
            model.Options.RootValueKeyName = null;
        }
    }

    private delegate bool TryGetEnumExpression(int value, out string expression);

    private static void ValidateEnumOption(
        GeneratorOutput context,
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

        context.ReportDiagnostic(DiagnosticInfo.Create(
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

    private static bool DerivesFrom(ITypeSymbol type, ITypeSymbol baseType)
    {
        for (var current = type.BaseType; current is not null; current = current.BaseType)
        {
            if (SymbolEqualityComparer.Default.Equals(current, baseType))
            {
                return true;
            }
        }

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

    // The content of a regular string literal: also escapes control characters, newlines and U+2028/U+2029
    private static string EscapeStringLiteral(string value)
    {
        var literal = SymbolDisplay.FormatLiteral(value, quote: true);
        return literal.Substring(1, literal.Length - 2);
    }

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

