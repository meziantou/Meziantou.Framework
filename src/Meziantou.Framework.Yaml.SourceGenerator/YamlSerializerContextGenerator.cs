using System.Collections.Immutable;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using Meziantou.Framework.Roslyn;

namespace Meziantou.Framework.Yaml.SourceGeneration;

/// <summary>
/// Generates YAML serialization metadata for types annotated with YAML source-generation attributes.
/// </summary>
[Generator]
public sealed partial class YamlSerializerContextGenerator : IIncrementalGenerator
{
    private const string GeneratedCodeTool = "Meziantou.Framework.Yaml.SourceGenerator";
    private static readonly string GeneratedCodeVersion = typeof(YamlSerializerContextGenerator).Assembly.GetName().Version?.ToString() ?? "0.0.0.0";

    private static readonly string ThrowHelperContent = GetEmbeddedSource("Meziantou.Framework.Yaml.Serialization.YamlThrowHelper.cs");
    private static readonly string MergeKeyContent = GetEmbeddedSource("Meziantou.Framework.Yaml.Serialization.Converters.YamlMergeKey.cs");
    internal static readonly SymbolDisplayFormat FullyQualifiedNullableFormat = SymbolDisplayFormat.FullyQualifiedFormat.WithMiscellaneousOptions(
        SymbolDisplayFormat.FullyQualifiedFormat.MiscellaneousOptions | SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier);

    private static readonly DiagnosticDescriptor ContextMustBePartial = new(
        id: "MFY001",
        title: "Yaml serializer context must be partial",
        messageFormat: "Type '{0}' derives from Meziantou.Framework.Yaml.Serialization.YamlSerializerContext and must be declared partial to support source generation",
        category: "Meziantou.Framework.Yaml.SourceGeneration",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor UnsupportedMemberType = new(
        id: "MFY002",
        title: "Unsupported member type",
        messageFormat: "Type '{0}' contains member '{1}' of unsupported type '{2}'. Use a supported scalar, collection, dictionary with a supported key, concrete generated type, or converter.",
        category: "Meziantou.Framework.Yaml.SourceGeneration",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor UnsupportedExtensionDataMember = new(
        id: "MFY003",
        title: "Unsupported extension data member",
        messageFormat: "Type '{0}' contains extension data member '{1}' of unsupported type '{2}'. Extension data members must be 'Dictionary<string, TValue>', 'IDictionary<string, TValue>', 'IReadOnlyDictionary<string, TValue>' where TValue is 'object' or 'Meziantou.Framework.Yaml.Model.YamlNode', or 'Meziantou.Framework.Yaml.Model.YamlMapping'.",
        category: "Meziantou.Framework.Yaml.SourceGeneration",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor MultipleExtensionDataMembers = new(
        id: "MFY004",
        title: "Multiple extension data members",
        messageFormat: "Type '{0}' contains multiple extension data members. Only one member can be annotated with [YamlExtensionData].",
        category: "Meziantou.Framework.Yaml.SourceGeneration",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor InvalidSourceGenerationOption = new(
        id: "MFY005",
        title: "Invalid source generation option",
        messageFormat: "Invalid source generation option on context '{0}': {1}",
        category: "Meziantou.Framework.Yaml.SourceGeneration",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor InvalidConverterType = new(
        id: "MFY006",
        title: "Invalid converter type",
        messageFormat: "Converter type '{0}' is invalid: {1}",
        category: "Meziantou.Framework.Yaml.SourceGeneration",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor UnsupportedSerializableType = new(
        id: "MFY007",
        title: "Unsupported serializable type",
        messageFormat: "Type '{0}' is not supported: {1}",
        category: "Meziantou.Framework.Yaml.SourceGeneration",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor InvalidDerivedTypeMapping = new(
        id: "MFY020",
        title: "Invalid derived type mapping",
        messageFormat: "Type '{0}' is not assignable to base type '{1}' in [YamlDerivedTypeMapping]",
        category: "Meziantou.Framework.Yaml.SourceGeneration",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor MissingYamlPolymorphicOnDerivedTypeMappingBase = new(
        id: "MFY021",
        title: "Derived type mapping base type has no polymorphic configuration",
        messageFormat: "Base type '{0}' in [YamlDerivedTypeMapping] has no [YamlPolymorphic] attribute; serializer-level defaults will be used",
        category: "Meziantou.Framework.Yaml.SourceGeneration",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor UnresolvedOpenGenericDerivedType = new(
        id: "MFY022",
        title: "Open generic derived type cannot be resolved",
        messageFormat: "Open generic derived type '{0}' cannot be resolved for base type '{1}' and is ignored. Ensure the type arguments of the derived type can be inferred from the base type.",
        category: "Meziantou.Framework.Yaml.SourceGeneration",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor InferClosedTypePolymorphismOnNonClosedType = new(
        id: "MFY023",
        title: "Closed type polymorphism inference requires a closed type",
        messageFormat: "Type '{0}' enables [YamlPolymorphic(InferClosedTypePolymorphism = true)] but is not a closed type, so no derived type can be inferred. Declare the type 'closed' or register its derived types using [YamlDerivedType].",
        category: "Meziantou.Framework.Yaml.SourceGeneration",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor InferClosedTypePolymorphismWithExplicitDerivedTypes = new(
        id: "MFY024",
        title: "Closed type polymorphism inference is replaced by explicit derived types",
        messageFormat: "Type '{0}' enables [YamlPolymorphic(InferClosedTypePolymorphism = true)] but also declares [YamlDerivedType]. Explicit registrations replace inference, so only the derived types declared explicitly are serialized polymorphically.",
        category: "Meziantou.Framework.Yaml.SourceGeneration",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor IgnoredInferredDerivedType = new(
        id: "MFY025",
        title: "Inferred derived type is ignored",
        messageFormat: "Derived type '{0}' of the closed type '{1}' is ignored: {2}",
        category: "Meziantou.Framework.Yaml.SourceGeneration",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor DuplicateDerivedTypeRegistration = new(
        id: "MFY026",
        title: "Duplicate derived type registration",
        messageFormat: "Type '{0}' has conflicting derived type registrations: {1}",
        category: "Meziantou.Framework.Yaml.SourceGeneration",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    internal static readonly ImmutableArray<DiagnosticDescriptor> SupportedDiagnosticDescriptors = ImmutableArray.Create(
        ContextMustBePartial,
        UnsupportedMemberType,
        UnsupportedExtensionDataMember,
        MultipleExtensionDataMembers,
        InvalidSourceGenerationOption,
        InvalidConverterType,
        UnsupportedSerializableType,
        InvalidDerivedTypeMapping,
        MissingYamlPolymorphicOnDerivedTypeMappingBase,
        UnresolvedOpenGenericDerivedType,
        InferClosedTypePolymorphismOnNonClosedType,
        InferClosedTypePolymorphismWithExplicitDerivedTypes,
        IgnoredInferredDerivedType,
        DuplicateDerivedTypeRegistration);

    internal sealed class ContextValidationResult
    {
        public ContextValidationResult(
            ImmutableArray<Diagnostic> diagnostics,
            ImmutableArray<DerivedTypeMappingModel> derivedTypeMappings,
            ImmutableArray<ITypeSymbol> resolvedTypes,
            Dictionary<ITypeSymbol, int> indexByType)
        {
            Diagnostics = diagnostics;
            DerivedTypeMappings = derivedTypeMappings;
            ResolvedTypes = resolvedTypes;
            IndexByType = indexByType;
        }

        public ImmutableArray<Diagnostic> Diagnostics { get; }

        public ImmutableArray<DerivedTypeMappingModel> DerivedTypeMappings { get; }

        public ImmutableArray<ITypeSymbol> ResolvedTypes { get; }

        public Dictionary<ITypeSymbol, int> IndexByType { get; }

        public bool HasErrors => Diagnostics.Any(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
    }

    internal sealed class DerivedTypeMappingModel
    {
        public DerivedTypeMappingModel(
            ITypeSymbol baseType,
            ITypeSymbol derivedType,
            string? discriminator,
            string? tag,
            Location? location)
        {
            BaseType = baseType;
            DerivedType = derivedType;
            Discriminator = discriminator;
            Tag = tag;
            Location = location;
        }

        public ITypeSymbol BaseType { get; }

        public ITypeSymbol DerivedType { get; }

        public string? Discriminator { get; }

        public string? Tag { get; }

        public Location? Location { get; }
    }

    /// <inheritdoc />
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var candidateContexts = context.SyntaxProvider.CreateSyntaxProvider(
                static (node, _) => node is ClassDeclarationSyntax classDeclaration && classDeclaration.AttributeLists.Count > 0,
                static (syntaxContext, _) => TryCreateContextModel(syntaxContext))
            .Where(static model => model is not null)
            .Select(static (model, _) => model!);

        var compilationAndModels = context.CompilationProvider.Combine(candidateContexts.Collect());
        context.RegisterSourceOutput(compilationAndModels, static (spc, input) =>
        {
            var compilation = input.Left;
            var models = input.Right;

            var byMetadataName = new Dictionary<string, ContextModel>(models.Length, StringComparer.Ordinal);
            foreach (var model in models)
            {
                byMetadataName[model.ContextSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)] = model;
            }

            var emittedContext = false;
            foreach (var model in byMetadataName.Values)
            {
                emittedContext |= EmitContext(spc, compilation, model);
            }

            if (!emittedContext)
                return;

            spc.AddSource($"YamlThrowHelper.g.cs", SourceText.From(ThrowHelperContent, Encoding.UTF8));
            spc.AddSource("YamlMergeKey.g.cs", SourceText.From(MergeKeyContent, Encoding.UTF8));
            spc.AddSource("Microsoft.CodeAnalysis.EmbeddedAttribute", SourceText.From(""""
                // <auto-generated/>
                #pragma warning disable
                namespace Microsoft.CodeAnalysis
                {
                    internal sealed partial class EmbeddedAttribute : global::System.Attribute
                    {
                    }
                }
                """", encoding: Encoding.UTF8));
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

        if (!DerivesFromYamlSerializerContext(classSymbol))
        {
            return null;
        }

        var serializableTypes = ImmutableArray.CreateBuilder<SerializableTypeModel>();
        var derivedTypeMappings = ImmutableArray.CreateBuilder<DerivedTypeMappingModel>();
        var yamlSourceGenerationOptions = new SourceGenerationOptionsModel();
        foreach (var attribute in classSymbol.GetAttributes())
        {
            if (TryCreateSerializableTypeModel(attribute, out var serializableType))
            {
                serializableTypes.Add(serializableType);
                continue;
            }

            if (TryCreateDerivedTypeMappingModel(attribute, out var derivedTypeMapping))
            {
                derivedTypeMappings.Add(derivedTypeMapping);
                continue;
            }

            if (IsYamlSourceGenerationOptionsAttribute(attribute))
            {
                ApplyYamlSourceGenerationOptionsAttribute(attribute, yamlSourceGenerationOptions);
            }
        }

        if (serializableTypes.Count == 0)
        {
            return null;
        }

        var sourceGenerationOptions = new SourceGenerationOptionsModel();
        sourceGenerationOptions.ApplyFrom(yamlSourceGenerationOptions);

        var isPartial = classDeclaration.Modifiers.Any(static modifier => modifier.IsKind(SyntaxKind.PartialKeyword));
        var containingNamespace = classSymbol.ContainingNamespace;
        var namespaceName = containingNamespace is { IsGlobalNamespace: false } ? containingNamespace.ToDisplayString() : string.Empty;
        var typeName = classSymbol.Name;

        return new ContextModel(
            classSymbol,
            namespaceName,
            typeName,
            serializableTypes.ToImmutable(),
            derivedTypeMappings.ToImmutable(),
            sourceGenerationOptions,
            isValid: isPartial);
    }

    internal static object? TryCreateContextModel(SemanticModel semanticModel, ClassDeclarationSyntax classDeclaration)
    {
        if (semanticModel.GetDeclaredSymbol(classDeclaration) is not INamedTypeSymbol classSymbol)
        {
            return null;
        }

        if (!DerivesFromYamlSerializerContext(classSymbol))
        {
            return null;
        }

        var serializableTypes = ImmutableArray.CreateBuilder<SerializableTypeModel>();
        var derivedTypeMappings = ImmutableArray.CreateBuilder<DerivedTypeMappingModel>();
        var yamlSourceGenerationOptions = new SourceGenerationOptionsModel();
        foreach (var attribute in classSymbol.GetAttributes())
        {
            if (TryCreateSerializableTypeModel(attribute, out var serializableType))
            {
                serializableTypes.Add(serializableType);
                continue;
            }

            if (TryCreateDerivedTypeMappingModel(attribute, out var derivedTypeMapping))
            {
                derivedTypeMappings.Add(derivedTypeMapping);
                continue;
            }

            if (IsYamlSourceGenerationOptionsAttribute(attribute))
            {
                ApplyYamlSourceGenerationOptionsAttribute(attribute, yamlSourceGenerationOptions);
            }
        }

        if (serializableTypes.Count == 0)
        {
            return null;
        }

        var sourceGenerationOptions = new SourceGenerationOptionsModel();
        sourceGenerationOptions.ApplyFrom(yamlSourceGenerationOptions);

        var isPartial = classDeclaration.Modifiers.Any(static modifier => modifier.IsKind(SyntaxKind.PartialKeyword));
        var containingNamespace = classSymbol.ContainingNamespace;
        var namespaceName = containingNamespace is { IsGlobalNamespace: false } ? containingNamespace.ToDisplayString() : string.Empty;
        var typeName = classSymbol.Name;

        return new ContextModel(
            classSymbol,
            namespaceName,
            typeName,
            serializableTypes.ToImmutable(),
            derivedTypeMappings.ToImmutable(),
            sourceGenerationOptions,
            isValid: isPartial);
    }

    internal static ContextValidationResult ValidateContext(Compilation compilation, object? contextModel)
    {
        if (contextModel is not ContextModel model)
        {
            return new ContextValidationResult(
                ImmutableArray<Diagnostic>.Empty,
                ImmutableArray<DerivedTypeMappingModel>.Empty,
                ImmutableArray<ITypeSymbol>.Empty,
                new Dictionary<ITypeSymbol, int>(SymbolEqualityComparer.Default));
        }

        var diagnostics = ImmutableArray.CreateBuilder<Diagnostic>();
        if (!model.IsValid)
        {
            diagnostics.Add(Diagnostic.Create(ContextMustBePartial, model.ContextSymbol.Locations.FirstOrDefault(), model.ContextSymbol.ToDisplayString()));
            return new ContextValidationResult(
                diagnostics.ToImmutable(),
                ImmutableArray<DerivedTypeMappingModel>.Empty,
                ImmutableArray<ITypeSymbol>.Empty,
                new Dictionary<ITypeSymbol, int>(SymbolEqualityComparer.Default));
        }

        var derivedTypeMappings = ValidateDerivedTypeMappings(model, diagnostics);
        var resolvedTypes = ExpandSerializableTypes(
            model.SerializableTypes.Select(static item => item.TypeSymbol).ToImmutableArray(),
            derivedTypeMappings,
            model.SourceGenerationOptions,
            compilation);

        var indexByType = new Dictionary<ITypeSymbol, int>(resolvedTypes.Length, SymbolEqualityComparer.Default);
        for (var i = 0; i < resolvedTypes.Length; i++)
        {
            indexByType[resolvedTypes[i]] = i;
        }

        ValidateSourceGenerationOptions(diagnostics, compilation, model);
        ValidateResolvedTypes(diagnostics, model, resolvedTypes, indexByType, compilation);

        // Validate that member types are generated as well (or are known scalars).
        var validatedPolymorphicTypes = new HashSet<ITypeSymbol>(SymbolEqualityComparer.Default);
        for (var i = 0; i < resolvedTypes.Length; i++)
        {
            if (resolvedTypes[i] is INamedTypeSymbol { TypeKind: TypeKind.Class or TypeKind.Interface } polymorphicCandidate && validatedPolymorphicTypes.Add(polymorphicCandidate.OriginalDefinition))
            {
                ValidateDerivedTypeRegistrations(diagnostics, polymorphicCandidate);
            }

            if (resolvedTypes[i] is not INamedTypeSymbol named || (named.TypeKind != TypeKind.Class && named.TypeKind != TypeKind.Struct))
            {
                continue;
            }

            ValidateYamlConverterAttribute(diagnostics, named, named);
            ValidateDerivedTypeAttributes(diagnostics, named);
            ValidateClosedTypePolymorphism(diagnostics, named, derivedTypeMappings, model.SourceGenerationOptions);

            // An unsupported type is reported as a whole, not through the members it would be written with.
            if (IsYamlNodeType(named) || IsKnownScalar(named) || GetUnsupportedTypeReason(named) is not null)
            {
                continue;
            }

            if (TryGetCSharpUnionCases(named, out _))
            {
                continue;
            }

            var extensionDataMembers = GetExtensionDataMembers(named);
            if (extensionDataMembers.Length > 1)
            {
                diagnostics.Add(Diagnostic.Create(
                    MultipleExtensionDataMembers,
                    named.Locations.FirstOrDefault(),
                    named.ToDisplayString()));
            }

            ISymbol? extensionDataMember = extensionDataMembers.Length == 1 ? extensionDataMembers[0] : null;
            if (extensionDataMember is not null)
            {
                var extensionType = GetMemberType(extensionDataMember);
                if (extensionType is null || !IsSupportedExtensionDataMemberType(extensionType))
                {
                    diagnostics.Add(Diagnostic.Create(
                        UnsupportedExtensionDataMember,
                        extensionDataMember.Locations.FirstOrDefault(),
                        named.ToDisplayString(),
                        extensionDataMember.Name,
                        extensionType?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) ?? "<unknown>"));
                }
            }

            foreach (var member in GetSerializableMembers(named))
            {
                if (extensionDataMember is not null && SymbolEqualityComparer.Default.Equals(member, extensionDataMember))
                {
                    continue;
                }

                var memberType = GetMemberType(member);
                if (memberType is null)
                {
                    continue;
                }

                ValidateYamlConverterAttribute(diagnostics, member, memberType);

                if (IsKnownScalar(memberType) || IsYamlNodeType(memberType) || IsUntypedObject(memberType))
                {
                    continue;
                }

                // Skip if the member itself has [YamlConverter(typeof(...))] — the converter handles serialization.
                if (HasYamlConverterAttribute(member))
                {
                    continue;
                }

                // Skip if the member type is handled by a converter (type-level attribute or context-level converter).
                if (IsTypeHandledByConverter(memberType, model.SourceGenerationOptions.ConverterTypes, compilation))
                {
                    continue;
                }

                if (TryGetArrayElementType(memberType, out var arrayElementType) ||
                    TryGetSequenceElementType(memberType, out arrayElementType, out _))
                {
                    if (IsKnownScalar(arrayElementType) || IsYamlNodeType(arrayElementType) || IsUntypedObject(arrayElementType) || indexByType.ContainsKey(GetNullableUnderlyingType(arrayElementType)) ||
                        IsTypeHandledByConverter(arrayElementType, model.SourceGenerationOptions.ConverterTypes, compilation))
                    {
                        continue;
                    }

                    diagnostics.Add(Diagnostic.Create(
                        UnsupportedMemberType,
                        member.Locations.FirstOrDefault(),
                        named.ToDisplayString(),
                        member.Name,
                        arrayElementType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)));
                    continue;
                }

                if (TryGetDictionaryTypes(memberType, out var dictionaryKeyType, out var dictionaryValueType, out _))
                {
                    if (!IsSupportedDictionaryKeyType(dictionaryKeyType))
                    {
                        diagnostics.Add(Diagnostic.Create(
                            UnsupportedMemberType,
                            member.Locations.FirstOrDefault(),
                            named.ToDisplayString(),
                            member.Name,
                            memberType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)));
                        continue;
                    }

                    if (IsKnownScalar(dictionaryValueType) || IsYamlNodeType(dictionaryValueType) || IsUntypedObject(dictionaryValueType) || indexByType.ContainsKey(GetNullableUnderlyingType(dictionaryValueType)) ||
                        IsTypeHandledByConverter(dictionaryValueType, model.SourceGenerationOptions.ConverterTypes, compilation))
                    {
                        continue;
                    }

                    diagnostics.Add(Diagnostic.Create(
                        UnsupportedMemberType,
                        member.Locations.FirstOrDefault(),
                        named.ToDisplayString(),
                        member.Name,
                        dictionaryValueType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)));
                    continue;
                }

                if (!indexByType.ContainsKey(GetNullableUnderlyingType(memberType)))
                {
                    diagnostics.Add(Diagnostic.Create(
                        UnsupportedMemberType,
                        member.Locations.FirstOrDefault(),
                        named.ToDisplayString(),
                        member.Name,
                        memberType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)));
                }
            }
        }

        return new ContextValidationResult(diagnostics.ToImmutable(), derivedTypeMappings, resolvedTypes, indexByType);
    }

    /// <summary>
    /// Reports the types the generated serializer cannot read back: a collection of the base class library none of
    /// the collection shapes handles, a delegate or reflection type, an asynchronous sequence, a dictionary whose key
    /// type cannot be written as a scalar, and a collection of such an element. The reflection-based serializer throws
    /// a <see cref="NotSupportedException"/> for the same types.
    /// </summary>
    private static void ValidateResolvedTypes(
        ImmutableArray<Diagnostic>.Builder diagnostics,
        ContextModel model,
        ImmutableArray<ITypeSymbol> resolvedTypes,
        Dictionary<ITypeSymbol, int> indexByType,
        Compilation compilation)
    {
        var contextLocation = model.ContextSymbol.Locations.FirstOrDefault();
        foreach (var type in resolvedTypes)
        {
            Location? location = null;
            foreach (var serializableType in model.SerializableTypes)
            {
                if (SymbolEqualityComparer.Default.Equals(serializableType.TypeSymbol, type))
                {
                    location = serializableType.Location;
                    break;
                }
            }

            location ??= contextLocation;
            if (IsTypeHandledByConverter(type, model.SourceGenerationOptions.ConverterTypes, compilation) || GetYamlConverterAttributeType(type) is not null)
            {
                continue;
            }

            var reason = GetUnsupportedTypeReason(type);
            if (reason is null && IsEnumerableWithoutMembers(type))
            {
                reason = "it is enumerable, but it is not a supported collection and has no serializable member, so its elements would be lost. Implement ICollection<T> with a public parameterless constructor, use a supported collection, or register a converter.";
            }

            if (reason is null && TryGetDictionaryTypes(type, out var keyType, out var valueType, out _))
            {
                if (!IsSupportedDictionaryKeyType(keyType))
                {
                    reason = $"the dictionary key type '{keyType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}' cannot be written as a scalar. Use a string, an enum, a scalar type such as int or Guid, or object.";
                }
                else
                {
                    reason = GetUnsupportedElementTypeReason(valueType, indexByType, model, compilation);
                }
            }
            else if (reason is null && (TryGetArrayElementType(type, out var elementType) || TryGetSequenceElementType(type, out elementType, out _)))
            {
                reason = GetUnsupportedElementTypeReason(elementType, indexByType, model, compilation);
            }
            else if (reason is null && TryGetKeyValuePairTypes(type, out keyType, out valueType))
            {
                reason = GetUnsupportedElementTypeReason(keyType, indexByType, model, compilation) ?? GetUnsupportedElementTypeReason(valueType, indexByType, model, compilation);
            }

            if (reason is not null)
            {
                diagnostics.Add(Diagnostic.Create(
                    UnsupportedSerializableType,
                    location,
                    type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                    reason));
            }
        }
    }

    /// <summary>
    /// Gets a value indicating whether <paramref name="type"/> is an enumerable type serialized as an object that has no
    /// member, which would be written as an empty mapping. The reflection-based serializer rejects the same types.
    /// </summary>
    private static bool IsEnumerableWithoutMembers(ITypeSymbol type)
    {
        if (type is not INamedTypeSymbol { TypeKind: TypeKind.Class or TypeKind.Struct } named ||
            IsKnownScalar(named) ||
            IsYamlNodeType(named) ||
            TryGetSequenceElementType(named, out _, out _) ||
            TryGetDictionaryTypes(named, out _, out _, out _) ||
            TryGetKeyValuePairTypes(named, out _, out _) ||
            TryGetCSharpUnionCases(named, out _) ||
            !named.AllInterfaces.Any(static interfaceType => string.Equals(interfaceType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat), "global::System.Collections.IEnumerable", StringComparison.Ordinal)))
        {
            return false;
        }

        return GetSerializableMembers(named).Length == 0 && GetExtensionDataMembers(named).Length == 0;
    }

    private static string? GetUnsupportedElementTypeReason(ITypeSymbol elementType, Dictionary<ITypeSymbol, int> indexByType, ContextModel model, Compilation compilation)
    {
        if (IsKnownScalar(elementType) || IsYamlNodeType(elementType) || IsUntypedObject(elementType) ||
            indexByType.ContainsKey(GetNullableUnderlyingType(elementType)) ||
            IsTypeHandledByConverter(elementType, model.SourceGenerationOptions.ConverterTypes, compilation))
        {
            return null;
        }

        var elementReason = GetUnsupportedTypeReason(GetNullableUnderlyingType(elementType));
        return elementReason is null ? null : $"its element type '{elementType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}' is not supported: {elementReason}";
    }

    /// <summary>
    /// Gets why a type that none of the built-in shapes handles cannot be serialized as an object made of its members,
    /// or <see langword="null"/> when it can. This mirrors the reflection-based serializer.
    /// </summary>
    internal static string? GetUnsupportedTypeReason(ITypeSymbol type)
    {
        if (type is not INamedTypeSymbol named ||
            type.SpecialType == SpecialType.System_String ||
            IsKnownScalar(type) ||
            IsYamlNodeType(type) ||
            TryGetSequenceElementType(type, out _, out _) ||
            TryGetDictionaryTypes(type, out _, out _, out _) ||
            TryGetKeyValuePairTypes(type, out _, out _))
        {
            return null;
        }

        if (named.TypeKind == TypeKind.Delegate || InheritsFrom(named, "global::System.Delegate"))
        {
            return "delegates cannot be serialized.";
        }

        if (InheritsFrom(named, "global::System.Reflection.MemberInfo"))
        {
            return "reflection types cannot be serialized.";
        }

        if (IsAsyncEnumerableInterface(named) || named.AllInterfaces.Any(IsAsyncEnumerableInterface))
        {
            return "asynchronous sequences cannot be serialized synchronously. Materialize the sequence into a List<T> first.";
        }

        if ((IsNonGenericEnumerableInterface(named) || named.AllInterfaces.Any(IsNonGenericEnumerableInterface)) && IsDeclaredInSystemNamespace(named))
        {
            return "this collection type is not supported. Use a supported collection such as List<T>, Dictionary<TKey, TValue>, or an array.";
        }

        return null;

        static bool IsAsyncEnumerableInterface(INamedTypeSymbol symbol)
            => string.Equals(symbol.ConstructedFrom.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat), "global::System.Collections.Generic.IAsyncEnumerable<T>", StringComparison.Ordinal);

        static bool IsNonGenericEnumerableInterface(INamedTypeSymbol symbol)
            => string.Equals(symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat), "global::System.Collections.IEnumerable", StringComparison.Ordinal);
    }

    private static bool InheritsFrom(INamedTypeSymbol type, string fullyQualifiedBaseTypeName)
    {
        for (var current = type; current is not null; current = current.BaseType)
        {
            if (string.Equals(current.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat), fullyQualifiedBaseTypeName, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsDeclaredInSystemNamespace(INamedTypeSymbol type)
    {
        // Every type derives from System.Object, so only the types a collection can derive from are considered.
        for (var current = type; current is not null && current.SpecialType is not (SpecialType.System_Object or SpecialType.System_ValueType); current = current.BaseType)
        {
            var namespaceName = current.ContainingNamespace?.ToDisplayString();
            if (namespaceName is not null && (string.Equals(namespaceName, "System", StringComparison.Ordinal) || namespaceName.StartsWith("System.", StringComparison.Ordinal)))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Reports a diagnostic when <c>[YamlConverter]</c> declares an open generic converter type that cannot be closed
    /// over the generic arguments of the annotated type or member type.
    /// </summary>
    private static void ValidateYamlConverterAttribute(ImmutableArray<Diagnostic>.Builder diagnostics, ISymbol symbol, ITypeSymbol typeToConvert)
    {
        var converterType = GetYamlConverterAttributeType(symbol);
        if (converterType is not INamedTypeSymbol namedConverterType || !IsOpenGenericConverterType(namedConverterType))
        {
            return;
        }

        if (!TryConstructOpenGenericConverterType(namedConverterType, typeToConvert, out _))
        {
            diagnostics.Add(Diagnostic.Create(
                InvalidConverterType,
                symbol.Locations.FirstOrDefault(),
                namedConverterType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                $"the open generic converter type is not compatible with type '{typeToConvert.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}'. Ensure that the total number of generic type parameters on the converter matches the number on the target type."));
        }
    }

    /// <summary>
    /// Reports the <c>[YamlDerivedType]</c> registrations of a type that conflict with an earlier one: the same derived
    /// type, discriminator, or tag registered twice, or two default derived types. The reflection-based serializer
    /// rejects them when the type is first used.
    /// </summary>
    private static void ValidateDerivedTypeRegistrations(ImmutableArray<Diagnostic>.Builder diagnostics, INamedTypeSymbol baseType)
    {
        var registrations = new DerivedTypeRegistrationSet();
        foreach (var attribute in baseType.GetAttributes())
        {
            if (!string.Equals(attribute.AttributeClass?.ToDisplayString(), "Meziantou.Framework.Yaml.Serialization.YamlDerivedTypeAttribute", StringComparison.Ordinal))
            {
                continue;
            }

            if (attribute.ConstructorArguments.Length < 1 ||
                attribute.ConstructorArguments[0].Kind != TypedConstantKind.Type ||
                attribute.ConstructorArguments[0].Value is not ITypeSymbol declaredDerivedType ||
                !TryResolveDerivedType(baseType, declaredDerivedType, out var derivedType))
            {
                continue;
            }

            string? discriminator = null;
            if (attribute.ConstructorArguments.Length >= 2)
            {
                discriminator = attribute.ConstructorArguments[1].Value switch
                {
                    string s => s,
                    int i => i.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    _ => null,
                };
            }

            string? tag = null;
            foreach (var pair in attribute.NamedArguments)
            {
                if (string.Equals(pair.Key, "Tag", StringComparison.Ordinal) && pair.Value.Value is string tagValue)
                {
                    tag = tagValue;
                }
            }

            if (registrations.TryAdd(derivedType, discriminator, tag) is { } conflict)
            {
                diagnostics.Add(Diagnostic.Create(
                    DuplicateDerivedTypeRegistration,
                    attribute.ApplicationSyntaxReference?.GetSyntax().GetLocation() ?? baseType.Locations.FirstOrDefault(),
                    baseType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat),
                    conflict));
            }
        }
    }

    /// <summary>Tracks the derived type registrations of a polymorphic type from a single source, to detect the conflicting ones.</summary>
    private sealed class DerivedTypeRegistrationSet
    {
        private readonly HashSet<ITypeSymbol> _derivedTypes = new(SymbolEqualityComparer.Default);
        private readonly Dictionary<string, ITypeSymbol> _discriminators = new(StringComparer.Ordinal);
        private readonly Dictionary<string, ITypeSymbol> _tags = new(StringComparer.Ordinal);
        private ITypeSymbol? _defaultDerivedType;

        /// <summary>Registers a derived type, or describes why it conflicts with an earlier registration.</summary>
        /// <returns><see langword="null"/> when the registration is added; otherwise the description of the conflict.</returns>
        public string? TryAdd(ITypeSymbol derivedType, string? discriminator, string? tag)
        {
            var derivedTypeName = derivedType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);
            if (_derivedTypes.Contains(derivedType))
            {
                return $"the derived type '{derivedTypeName}' is registered more than once";
            }

            if (discriminator is not null && _discriminators.TryGetValue(discriminator, out var discriminatorType))
            {
                return $"the discriminator '{discriminator}' is registered for both '{discriminatorType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)}' and '{derivedTypeName}'";
            }

            if (tag is not null && _tags.TryGetValue(tag, out var tagType))
            {
                return $"the tag '{tag}' is registered for both '{tagType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)}' and '{derivedTypeName}'";
            }

            if (discriminator is null && tag is null && _defaultDerivedType is not null)
            {
                return $"both '{_defaultDerivedType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)}' and '{derivedTypeName}' are registered as the default derived type, without a discriminator or a tag";
            }

            _derivedTypes.Add(derivedType);
            if (discriminator is not null)
            {
                _discriminators.Add(discriminator, derivedType);
            }

            if (tag is not null)
            {
                _tags.Add(tag, derivedType);
            }

            if (discriminator is null && tag is null)
            {
                _defaultDerivedType = derivedType;
            }

            return null;
        }
    }

    /// <summary>
    /// Reports a diagnostic for each <c>[YamlDerivedType]</c> declaring an open generic derived type that cannot be
    /// closed over the type arguments of the base type.
    /// </summary>
    private static void ValidateDerivedTypeAttributes(ImmutableArray<Diagnostic>.Builder diagnostics, INamedTypeSymbol baseType)
    {
        foreach (var attribute in baseType.GetAttributes())
        {
            if (!string.Equals(attribute.AttributeClass?.ToDisplayString(), "Meziantou.Framework.Yaml.Serialization.YamlDerivedTypeAttribute", StringComparison.Ordinal))
            {
                continue;
            }

            if (attribute.ConstructorArguments.Length < 1)
            {
                continue;
            }

            var derivedArgument = attribute.ConstructorArguments[0];
            if (derivedArgument.Kind != TypedConstantKind.Type ||
                derivedArgument.Value is not INamedTypeSymbol derivedType ||
                GetAllTypeParameters(derivedType).Length == 0 ||
                !IsOpenGenericType(derivedType))
            {
                continue;
            }

            if (!TryResolveDerivedType(baseType, derivedType, out _))
            {
                diagnostics.Add(Diagnostic.Create(
                    UnresolvedOpenGenericDerivedType,
                    attribute.ApplicationSyntaxReference?.GetSyntax().GetLocation() ?? baseType.Locations.FirstOrDefault(),
                    derivedType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat),
                    baseType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)));
            }
        }
    }

    /// <summary>
    /// Reports the misuses of <c>[YamlPolymorphic(InferClosedTypePolymorphism = true)]</c>: a type that is not
    /// declared <c>closed</c>, from which nothing can ever be inferred, and a type that also declares explicit
    /// registrations, which replace inference. When inference does apply, reports each derived type it has to ignore.
    /// </summary>
    private static void ValidateClosedTypePolymorphism(
        ImmutableArray<Diagnostic>.Builder diagnostics,
        INamedTypeSymbol baseType,
        ImmutableArray<DerivedTypeMappingModel> contextMappings,
        SourceGenerationOptionsModel sourceGenerationOptions)
    {
        AttributeData? polymorphicAttribute = null;
        var hasExplicitRegistrations = false;
        foreach (var attribute in baseType.GetAttributes())
        {
            var attributeName = attribute.AttributeClass?.ToDisplayString();
            if (string.Equals(attributeName, "Meziantou.Framework.Yaml.Serialization.YamlPolymorphicAttribute", StringComparison.Ordinal))
            {
                polymorphicAttribute = attribute;
            }
            else if (string.Equals(attributeName, "Meziantou.Framework.Yaml.Serialization.YamlDerivedTypeAttribute", StringComparison.Ordinal))
            {
                hasExplicitRegistrations = true;
            }
        }

        for (var i = 0; i < contextMappings.Length && !hasExplicitRegistrations; i++)
        {
            hasExplicitRegistrations = SymbolEqualityComparer.Default.Equals(contextMappings[i].BaseType, baseType);
        }

        bool? inferOverride = null;
        if (polymorphicAttribute is not null)
        {
            foreach (var pair in polymorphicAttribute.NamedArguments)
            {
                if (string.Equals(pair.Key, "InferClosedTypePolymorphism", StringComparison.Ordinal) && pair.Value.Value is bool inferValue)
                {
                    inferOverride = inferValue;
                }
            }
        }

        if (inferOverride is true)
        {
            var location = polymorphicAttribute!.ApplicationSyntaxReference?.GetSyntax().GetLocation() ?? baseType.Locations.FirstOrDefault();
            if (!ClosedTypeSymbolHelper.IsClosedType(baseType))
            {
                diagnostics.Add(Diagnostic.Create(
                    InferClosedTypePolymorphismOnNonClosedType,
                    location,
                    baseType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)));
                return;
            }

            if (hasExplicitRegistrations)
            {
                diagnostics.Add(Diagnostic.Create(
                    InferClosedTypePolymorphismWithExplicitDerivedTypes,
                    location,
                    baseType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)));
                return;
            }
        }

        if (!hasExplicitRegistrations && InfersClosedTypePolymorphism(baseType, inferOverride, sourceGenerationOptions))
        {
            InferClosedTypeDerivedTypes(baseType, diagnostics);
        }
    }

    /// <summary>
    /// Determines whether the derived types of <paramref name="baseType"/> are inferred from its closed hierarchy.
    /// </summary>
    private static bool InfersClosedTypePolymorphism(
        INamedTypeSymbol baseType,
        bool? declarationOverride,
        SourceGenerationOptionsModel sourceGenerationOptions)
        => (declarationOverride ?? sourceGenerationOptions.InferClosedTypePolymorphism ?? false) && ClosedTypeSymbolHelper.IsClosedType(baseType);

    /// <summary>
    /// Registers every descendant of a closed hierarchy, using its name, without the generic arity suffix, as its
    /// discriminator. A derived type that is itself closed brings its own hierarchy along, since it is known at
    /// compile time as well. Derived types are ordered by discriminator, base types first, so the generated metadata
    /// is deterministic.
    /// </summary>
    private static ImmutableArray<DerivedTypeInfoModel> InferClosedTypeDerivedTypes(
        INamedTypeSymbol baseType,
        ImmutableArray<Diagnostic>.Builder? diagnostics)
    {
        var derivedTypes = ImmutableArray.CreateBuilder<DerivedTypeInfoModel>();
        var seenDiscriminators = new HashSet<string>(StringComparer.Ordinal);
        var location = baseType.Locations.FirstOrDefault();

        // Each level is resolved against the type declaring it so an open generic derived type unifies with its own
        // base type. Only a newly registered type is expanded, and a type can only be registered once, so the
        // traversal always terminates.
        var pendingHierarchies = new Queue<INamedTypeSymbol>();
        pendingHierarchies.Enqueue(baseType);

        while (pendingHierarchies.Count > 0)
        {
            var declaringType = pendingHierarchies.Dequeue();
            foreach (var closedDerivedType in ClosedTypeSymbolHelper.GetClosedDerivedTypes(declaringType).OrderBy(static type => type.Name, StringComparer.Ordinal))
            {
                string? reason = null;
                if (!TryResolveDerivedType(declaringType, closedDerivedType, out var derivedType) || !IsAssignableTo(derivedType, baseType))
                {
                    reason = $"it cannot be resolved for the base type '{baseType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)}'";
                }
                else if (!ClosedTypeSymbolHelper.IsAtLeastAsVisibleAs(derivedType, baseType))
                {
                    reason = "it is less visible than the base type";
                }
                else if (!seenDiscriminators.Add(ClosedTypeSymbolHelper.GetInferredDiscriminator(derivedType)))
                {
                    reason = $"another derived type already uses the discriminator '{ClosedTypeSymbolHelper.GetInferredDiscriminator(derivedType)}'";
                }

                if (reason is not null)
                {
                    diagnostics?.Add(Diagnostic.Create(
                        IgnoredInferredDerivedType,
                        location,
                        closedDerivedType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat),
                        baseType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat),
                        reason));
                    continue;
                }

                derivedTypes.Add(new DerivedTypeInfoModel(derivedType!, ClosedTypeSymbolHelper.GetInferredDiscriminator(derivedType!), tag: null));

                if (derivedType is INamedTypeSymbol namedDerivedType && ClosedTypeSymbolHelper.IsClosedType(namedDerivedType))
                {
                    pendingHierarchies.Enqueue(namedDerivedType);
                }
            }
        }

        return derivedTypes.ToImmutable();
    }

    private static void ValidateSourceGenerationOptions(ImmutableArray<Diagnostic>.Builder diagnostics, Compilation compilation, ContextModel model)
    {
        var location = model.ContextSymbol.Locations.FirstOrDefault();
        var options = model.SourceGenerationOptions;

        if (options.IndentSize is < 1)
        {
            diagnostics.Add(Diagnostic.Create(
                InvalidSourceGenerationOption,
                location,
                model.ContextSymbol.ToDisplayString(),
                $"{nameof(options.IndentSize)} must be at least 1."));
        }

        if (string.Equals(options.DiscriminatorStyle, "Unspecified", StringComparison.Ordinal))
        {
            diagnostics.Add(Diagnostic.Create(
                InvalidSourceGenerationOption,
                location,
                model.ContextSymbol.ToDisplayString(),
                $"{nameof(options.DiscriminatorStyle)} cannot be Unspecified."));
        }

        if (options.ConverterTypes.IsDefaultOrEmpty)
        {
            return;
        }

        var yamlConverterSymbol = compilation.GetTypeByMetadataName("Meziantou.Framework.Yaml.Serialization.YamlConverter");
        if (yamlConverterSymbol is null)
        {
            return;
        }

        for (var i = 0; i < options.ConverterTypes.Length; i++)
        {
            var converterType = options.ConverterTypes[i];
            if (converterType is not INamedTypeSymbol named)
            {
                diagnostics.Add(Diagnostic.Create(
                    InvalidConverterType,
                    location,
                    converterType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                    "Converter types must be named types."));
                continue;
            }

            if (named.TypeKind != TypeKind.Class && named.TypeKind != TypeKind.Struct)
            {
                diagnostics.Add(Diagnostic.Create(
                    InvalidConverterType,
                    location,
                    named.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                    "Converter types must be classes or structs."));
                continue;
            }

            if (named.IsAbstract)
            {
                diagnostics.Add(Diagnostic.Create(
                    InvalidConverterType,
                    location,
                    named.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                    "Converter types cannot be abstract."));
                continue;
            }

            if (named.IsUnboundGenericType || named.TypeArguments.Any(static arg => arg.TypeKind == TypeKind.TypeParameter))
            {
                diagnostics.Add(Diagnostic.Create(
                    InvalidConverterType,
                    location,
                    named.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                    "Converter types cannot be open generic types."));
                continue;
            }

            // The generated code lives in the context type, so a nested converter can be private and still usable.
            if (!compilation.IsSymbolAccessibleWithin(named, model.ContextSymbol))
            {
                diagnostics.Add(Diagnostic.Create(
                    InvalidConverterType,
                    location,
                    named.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                    "Converter types must be accessible from the generated context."));
                continue;
            }

            if (!named.IsOrInheritsFrom(yamlConverterSymbol))
            {
                diagnostics.Add(Diagnostic.Create(
                    InvalidConverterType,
                    location,
                    named.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                    $"Converter types must derive from '{yamlConverterSymbol.ToDisplayString()}'."));
                continue;
            }

            if (!named.InstanceConstructors.Any(ctor => ctor.Parameters.Length == 0 && compilation.IsSymbolAccessibleWithin(ctor, model.ContextSymbol)))
            {
                diagnostics.Add(Diagnostic.Create(
                    InvalidConverterType,
                    location,
                    named.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                    "Converter types must provide a parameterless constructor accessible from the generated context."));
            }
        }
    }

    private static ImmutableArray<ITypeSymbol> ExpandSerializableTypes(
        ImmutableArray<ITypeSymbol> roots,
        ImmutableArray<DerivedTypeMappingModel> contextMappings,
        SourceGenerationOptionsModel sourceGenerationOptions,
        Compilation compilation)
    {
        // Always include explicitly declared root types. Additionally include polymorphic derived types and
        // statically discoverable member/element/value types so generated serializers can call into their
        // serializers without requiring explicit roots.
        var builder = ImmutableArray.CreateBuilder<ITypeSymbol>();
        var seen = new HashSet<ITypeSymbol>(SymbolEqualityComparer.Default);
        var queue = new Queue<ITypeSymbol>();

        for (var i = 0; i < roots.Length; i++)
        {
            var root = roots[i];
            if (seen.Add(root))
            {
                builder.Add(root);
                queue.Enqueue(root);
            }
        }

        for (var i = 0; i < contextMappings.Length; i++)
        {
            var mapping = contextMappings[i];
            if (seen.Add(mapping.BaseType))
            {
                builder.Add(mapping.BaseType);
                queue.Enqueue(mapping.BaseType);
            }

            if (seen.Add(mapping.DerivedType))
            {
                builder.Add(mapping.DerivedType);
                queue.Enqueue(mapping.DerivedType);
            }
        }

        while (queue.Count != 0)
        {
            var type = queue.Dequeue();

            foreach (var dependency in GetTransitiveSerializableDependencies(type, contextMappings, sourceGenerationOptions, compilation))
            {
                if (seen.Add(dependency))
                {
                    builder.Add(dependency);
                    queue.Enqueue(dependency);
                }
            }

            if (type is not INamedTypeSymbol named)
            {
                continue;
            }

            foreach (var derived in GetPolymorphicDerivedTypes(named, contextMappings, sourceGenerationOptions))
            {
                if (seen.Add(derived))
                {
                    builder.Add(derived);
                    queue.Enqueue(derived);
                }
            }

            if (TryGetCSharpUnionCases(named, out var unionCases))
            {
                foreach (var unionCase in unionCases)
                {
                    // A 'T?' case is read and written through the metadata of 'T': the union already carries its own
                    // null state, so there is no separate contract to generate for the nullable wrapper.
                    if (IsKnownScalar(unionCase.RuntimeType) || IsYamlNodeType(unionCase.RuntimeType) || IsUntypedObject(unionCase.RuntimeType))
                    {
                        continue;
                    }

                    if (seen.Add(unionCase.RuntimeType))
                    {
                        builder.Add(unionCase.RuntimeType);
                        queue.Enqueue(unionCase.RuntimeType);
                    }
                }
            }
        }

        return builder.ToImmutable();
    }

    private static IEnumerable<ITypeSymbol> GetTransitiveSerializableDependencies(
        ITypeSymbol type,
        ImmutableArray<DerivedTypeMappingModel> contextMappings,
        SourceGenerationOptionsModel sourceGenerationOptions,
        Compilation compilation)
    {
        if (TryGetArrayElementType(type, out var arrayElementType))
        {
            if (ShouldGenerateTransitiveType(GetNullableUnderlyingType(arrayElementType), contextMappings, sourceGenerationOptions, compilation))
            {
                yield return GetNullableUnderlyingType(arrayElementType);
            }

            yield break;
        }

        if (TryGetSequenceElementType(type, out var sequenceElementType, out _))
        {
            if (ShouldGenerateTransitiveType(GetNullableUnderlyingType(sequenceElementType), contextMappings, sourceGenerationOptions, compilation))
            {
                yield return GetNullableUnderlyingType(sequenceElementType);
            }

            yield break;
        }

        if (TryGetDictionaryTypes(type, out var dictionaryKeyType, out var dictionaryValueType, out _))
        {
            if (IsSupportedDictionaryKeyType(dictionaryKeyType) &&
                ShouldGenerateTransitiveType(GetNullableUnderlyingType(dictionaryValueType), contextMappings, sourceGenerationOptions, compilation))
            {
                yield return GetNullableUnderlyingType(dictionaryValueType);
            }

            yield break;
        }

        if (TryGetKeyValuePairTypes(type, out var pairKeyType, out var pairValueType))
        {
            if (ShouldGenerateTransitiveType(GetNullableUnderlyingType(pairKeyType), contextMappings, sourceGenerationOptions, compilation))
            {
                yield return GetNullableUnderlyingType(pairKeyType);
            }

            if (ShouldGenerateTransitiveType(GetNullableUnderlyingType(pairValueType), contextMappings, sourceGenerationOptions, compilation))
            {
                yield return GetNullableUnderlyingType(pairValueType);
            }

            yield break;
        }

        if (type is not INamedTypeSymbol named ||
            (named.TypeKind != TypeKind.Class && named.TypeKind != TypeKind.Struct) ||
            IsYamlNodeType(named) ||
            IsKnownScalar(named))
        {
            yield break;
        }

        // A constructor parameter is read by the generated reader of its type, unlike a member, whose collection is read
        // inline, so a collection parameter needs its own reader.
        if (named.TypeKind is TypeKind.Class or TypeKind.Struct && !named.IsAbstract &&
            TrySelectDeserializationConstructor(named, out var constructor, out _) && constructor is not null)
        {
            foreach (var parameter in constructor.Parameters)
            {
                var parameterType = GetNullableUnderlyingType(parameter.Type);
                if ((parameterType is IArrayTypeSymbol || TryGetSequenceElementType(parameterType, out _, out _) || TryGetDictionaryTypes(parameterType, out _, out _, out _)) &&
                    ShouldGenerateTransitiveType(parameterType, contextMappings, sourceGenerationOptions, compilation))
                {
                    yield return parameterType;
                }
            }
        }

        var extensionDataMembers = GetExtensionDataMembers(named);
        foreach (var member in GetSerializableMembers(named))
        {
            if (extensionDataMembers.Any(extensionDataMember => SymbolEqualityComparer.Default.Equals(member, extensionDataMember)))
            {
                continue;
            }

            if (HasYamlConverterAttribute(member))
            {
                continue;
            }

            var memberType = GetMemberType(member);
            if (memberType is null)
            {
                continue;
            }

            foreach (var dependency in GetTransitiveSerializableMemberDependencies(memberType, contextMappings, sourceGenerationOptions, compilation))
            {
                yield return dependency;
            }
        }
    }

    private static IEnumerable<ITypeSymbol> GetTransitiveSerializableMemberDependencies(
        ITypeSymbol memberType,
        ImmutableArray<DerivedTypeMappingModel> contextMappings,
        SourceGenerationOptionsModel sourceGenerationOptions,
        Compilation compilation)
    {
        if (TryGetArrayElementType(memberType, out var arrayElementType))
        {
            if (ShouldGenerateTransitiveType(GetNullableUnderlyingType(arrayElementType), contextMappings, sourceGenerationOptions, compilation))
            {
                yield return GetNullableUnderlyingType(arrayElementType);
            }

            yield break;
        }

        if (TryGetSequenceElementType(memberType, out var sequenceElementType, out _))
        {
            if (ShouldGenerateTransitiveType(GetNullableUnderlyingType(sequenceElementType), contextMappings, sourceGenerationOptions, compilation))
            {
                yield return GetNullableUnderlyingType(sequenceElementType);
            }

            yield break;
        }

        if (TryGetDictionaryTypes(memberType, out var dictionaryKeyType, out var dictionaryValueType, out _))
        {
            if (IsSupportedDictionaryKeyType(dictionaryKeyType) &&
                ShouldGenerateTransitiveType(GetNullableUnderlyingType(dictionaryValueType), contextMappings, sourceGenerationOptions, compilation))
            {
                yield return GetNullableUnderlyingType(dictionaryValueType);
            }

            yield break;
        }

        // The underlying type of a nullable value type is generated so a nullable struct or union can be serialized.
        if (memberType is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } nullableMemberType)
        {
            memberType = nullableMemberType.TypeArguments[0];
        }

        if (ShouldGenerateTransitiveType(memberType, contextMappings, sourceGenerationOptions, compilation))
        {
            yield return memberType;
        }
    }

    private static ITypeSymbol GetNullableUnderlyingType(ITypeSymbol type)
        => type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } nullableType ? nullableType.TypeArguments[0] : type;

    private static bool ShouldGenerateTransitiveType(
        ITypeSymbol type,
        ImmutableArray<DerivedTypeMappingModel> contextMappings,
        SourceGenerationOptionsModel sourceGenerationOptions,
        Compilation compilation)
    {
        if (IsKnownScalar(type) ||
            IsYamlNodeType(type) ||
            IsUntypedObject(type) ||
            IsTypeHandledByConverter(type, sourceGenerationOptions.ConverterTypes, compilation))
        {
            return false;
        }

        if (type is IArrayTypeSymbol)
        {
            return true;
        }

        if (type is not INamedTypeSymbol named)
        {
            return false;
        }

        // A collection used as an element, a dictionary value, or a constructor parameter is read by its own generated
        // reader, including a collection interface such as IEnumerable<T>.
        if (named.TypeArguments.All(static typeArgument => typeArgument.TypeKind != TypeKind.TypeParameter) &&
            (TryGetSequenceElementType(named, out _, out _) || TryGetDictionaryTypes(named, out _, out _, out _)))
        {
            return true;
        }

        if (named.TypeKind == TypeKind.Interface)
        {
            return TryGetPolymorphismInfo(named, contextMappings, sourceGenerationOptions, out var polymorphism) &&
                polymorphism.DerivedTypes.Length != 0;
        }

        // An unsupported type is not generated, so the member using it is reported.
        if (GetUnsupportedTypeReason(named) is not null)
        {
            return false;
        }

        if (named.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T ||
            named.SpecialType == SpecialType.System_Object ||
            named.TypeKind is TypeKind.Delegate or TypeKind.TypeParameter ||
            named.IsUnboundGenericType ||
            named.TypeArguments.Any(static typeArgument => typeArgument.TypeKind == TypeKind.TypeParameter))
        {
            return false;
        }

        return named.TypeKind is TypeKind.Class or TypeKind.Struct or TypeKind.Enum;
    }

    private static bool TryGetArrayElementType(ITypeSymbol type, out ITypeSymbol elementType)
    {
        if (type is IArrayTypeSymbol arrayType && arrayType.Rank == 1)
        {
            elementType = arrayType.ElementType;
            return true;
        }

        elementType = null!;
        return false;
    }

    private static bool TryGetListElementType(ITypeSymbol type, out ITypeSymbol elementType)
    {
        if (type is INamedTypeSymbol named
            && named.IsGenericType
            && string.Equals(named.ConstructedFrom.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat), "global::System.Collections.Generic.List<T>", StringComparison.Ordinal)
            && named.TypeArguments.Length == 1)
        {
            elementType = named.TypeArguments[0];
            return true;
        }

        elementType = null!;
        return false;
    }

    private static bool TryGetSequenceElementType(ITypeSymbol type, out ITypeSymbol elementType, out SequenceKind kind)
    {
        if (TryGetListElementType(type, out elementType))
        {
            kind = SequenceKind.List;
            return true;
        }

        if (type is INamedTypeSymbol named
            && named.IsGenericType
            && named.TypeArguments.Length == 1)
        {
            var constructed = named.ConstructedFrom.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            if (string.Equals(constructed, "global::System.Collections.Generic.IEnumerable<T>", StringComparison.Ordinal) ||
                string.Equals(constructed, "global::System.Collections.Generic.IReadOnlyList<T>", StringComparison.Ordinal) ||
                string.Equals(constructed, "global::System.Collections.Generic.IReadOnlyCollection<T>", StringComparison.Ordinal) ||
                string.Equals(constructed, "global::System.Collections.Generic.IList<T>", StringComparison.Ordinal) ||
                string.Equals(constructed, "global::System.Collections.Generic.ICollection<T>", StringComparison.Ordinal))
            {
                elementType = named.TypeArguments[0];
                kind = SequenceKind.Enumerable;
                return true;
            }

            if (string.Equals(constructed, "global::System.Collections.Generic.HashSet<T>", StringComparison.Ordinal) ||
                string.Equals(constructed, "global::System.Collections.Generic.ISet<T>", StringComparison.Ordinal) ||
                string.Equals(constructed, "global::System.Collections.Generic.IReadOnlySet<T>", StringComparison.Ordinal))
            {
                elementType = named.TypeArguments[0];
                kind = SequenceKind.Set;
                return true;
            }

            if (string.Equals(constructed, "global::System.Collections.Immutable.ImmutableArray<T>", StringComparison.Ordinal))
            {
                elementType = named.TypeArguments[0];
                kind = SequenceKind.ImmutableArray;
                return true;
            }

            if (string.Equals(constructed, "global::System.Collections.Immutable.ImmutableList<T>", StringComparison.Ordinal))
            {
                elementType = named.TypeArguments[0];
                kind = SequenceKind.ImmutableList;
                return true;
            }

            if (string.Equals(constructed, "global::System.Collections.Immutable.ImmutableHashSet<T>", StringComparison.Ordinal))
            {
                elementType = named.TypeArguments[0];
                kind = SequenceKind.ImmutableHashSet;
                return true;
            }

            SequenceKind? constructedKind = constructed switch
            {
                "global::System.Collections.Generic.Queue<T>" => SequenceKind.Queue,
                "global::System.Collections.Generic.Stack<T>" => SequenceKind.Stack,
                "global::System.Collections.Concurrent.ConcurrentQueue<T>" => SequenceKind.ConcurrentQueue,
                "global::System.Collections.Concurrent.ConcurrentStack<T>" => SequenceKind.ConcurrentStack,
                "global::System.Collections.Concurrent.ConcurrentBag<T>" => SequenceKind.ConcurrentBag,
                "global::System.Collections.Frozen.FrozenSet<T>" => SequenceKind.FrozenSet,
                "global::System.ArraySegment<T>" => SequenceKind.ArraySegment,
                _ => null,
            };

            if (constructedKind is { } sequenceKind)
            {
                elementType = named.TypeArguments[0];
                kind = sequenceKind;
                return true;
            }
        }

        if (TryGetMutableCollectionElementType(type, out elementType))
        {
            kind = SequenceKind.MutableCollection;
            return true;
        }

        elementType = null!;
        kind = default;
        return false;
    }

    private static bool TryGetMutableCollectionElementType(ITypeSymbol type, out ITypeSymbol elementType)
    {
        elementType = null!;

        if (type is not INamedTypeSymbol named ||
            named.TypeKind != TypeKind.Class ||
            named.IsAbstract ||
            named.InstanceConstructors.All(static constructor => constructor.Parameters.Length != 0 || constructor.DeclaredAccessibility != Accessibility.Public))
        {
            return false;
        }

        ITypeSymbol? matchedElementType = null;
        foreach (var interfaceType in named.AllInterfaces)
        {
            var constructed = interfaceType.ConstructedFrom.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            if (string.Equals(constructed, "global::System.Collections.IDictionary", StringComparison.Ordinal) ||
                string.Equals(constructed, "global::System.Collections.Generic.IDictionary<TKey, TValue>", StringComparison.Ordinal) ||
                string.Equals(constructed, "global::System.Collections.Generic.IReadOnlyDictionary<TKey, TValue>", StringComparison.Ordinal))
            {
                return false;
            }

            if (!string.Equals(constructed, "global::System.Collections.Generic.ICollection<T>", StringComparison.Ordinal) ||
                interfaceType.TypeArguments.Length != 1)
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

    private static bool TryGetDictionaryValueType(ITypeSymbol type, out ITypeSymbol valueType, out bool isReadOnly)
    {
        if (type is INamedTypeSymbol named
            && named.IsGenericType
            && named.TypeArguments.Length == 2
            && named.TypeArguments[0].SpecialType == SpecialType.System_String)
        {
            var definition = named.ConstructedFrom.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            switch (definition)
            {
                case "global::System.Collections.Generic.Dictionary<TKey, TValue>":
                case "global::System.Collections.Generic.IDictionary<TKey, TValue>":
                    valueType = named.TypeArguments[1];
                    isReadOnly = false;
                    return true;

                case "global::System.Collections.Generic.IReadOnlyDictionary<TKey, TValue>":
                    valueType = named.TypeArguments[1];
                    isReadOnly = true;
                    return true;
            }
        }

        valueType = null!;
        isReadOnly = false;
        return false;
    }

    private static bool TryGetDictionaryTypes(ITypeSymbol type, out ITypeSymbol keyType, out ITypeSymbol valueType, out DictionaryKind kind)
    {
        if (type is INamedTypeSymbol named
            && named.IsGenericType
            && named.TypeArguments.Length == 2)
        {
            var constructed = named.ConstructedFrom.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            if (string.Equals(constructed, "global::System.Collections.Generic.Dictionary<TKey, TValue>", StringComparison.Ordinal))
            {
                keyType = named.TypeArguments[0];
                valueType = named.TypeArguments[1];
                kind = DictionaryKind.Dictionary;
                return true;
            }

            if (string.Equals(constructed, "global::System.Collections.Generic.IDictionary<TKey, TValue>", StringComparison.Ordinal))
            {
                keyType = named.TypeArguments[0];
                valueType = named.TypeArguments[1];
                kind = DictionaryKind.IDictionary;
                return true;
            }

            if (string.Equals(constructed, "global::System.Collections.Generic.IReadOnlyDictionary<TKey, TValue>", StringComparison.Ordinal))
            {
                keyType = named.TypeArguments[0];
                valueType = named.TypeArguments[1];
                kind = DictionaryKind.IReadOnlyDictionary;
                return true;
            }

            if (string.Equals(constructed, "global::System.Collections.Generic.OrderedDictionary<TKey, TValue>", StringComparison.Ordinal))
            {
                keyType = named.TypeArguments[0];
                valueType = named.TypeArguments[1];
                kind = DictionaryKind.OrderedDictionary;
                return true;
            }

            if (string.Equals(constructed, "global::System.Collections.Frozen.FrozenDictionary<TKey, TValue>", StringComparison.Ordinal))
            {
                keyType = named.TypeArguments[0];
                valueType = named.TypeArguments[1];
                kind = DictionaryKind.FrozenDictionary;
                return true;
            }
        }

        if (TryGetMutableDictionaryTypes(type, out keyType, out valueType))
        {
            kind = DictionaryKind.MutableDictionary;
            return true;
        }

        keyType = null!;
        valueType = null!;
        kind = default;
        return false;
    }

    /// <summary>
    /// Matches the dictionaries the reflection-based serializer creates with their parameterless constructor, such as
    /// <c>SortedDictionary&lt;TKey, TValue&gt;</c> or <c>ConcurrentDictionary&lt;TKey, TValue&gt;</c>.
    /// </summary>
    private static bool TryGetMutableDictionaryTypes(ITypeSymbol type, out ITypeSymbol keyType, out ITypeSymbol valueType)
    {
        keyType = null!;
        valueType = null!;

        if (type is not INamedTypeSymbol named ||
            named.TypeKind != TypeKind.Class ||
            named.IsAbstract ||
            IsYamlNodeType(named) ||
            !named.InstanceConstructors.Any(static constructor => constructor.Parameters.Length == 0 && constructor.DeclaredAccessibility == Accessibility.Public))
        {
            return false;
        }

        INamedTypeSymbol? dictionaryInterface = null;
        foreach (var interfaceType in named.AllInterfaces)
        {
            if (!string.Equals(interfaceType.ConstructedFrom.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat), "global::System.Collections.Generic.IDictionary<TKey, TValue>", StringComparison.Ordinal))
            {
                continue;
            }

            if (dictionaryInterface is not null && !SymbolEqualityComparer.Default.Equals(dictionaryInterface, interfaceType))
            {
                return false;
            }

            dictionaryInterface = interfaceType;
        }

        if (dictionaryInterface is null)
        {
            return false;
        }

        keyType = dictionaryInterface.TypeArguments[0];
        valueType = dictionaryInterface.TypeArguments[1];
        return true;
    }

    /// <summary>Gets the statement declaring the <c>dictionary</c> local a generated reader fills.</summary>
    private static string GetDictionaryVariableDeclaration(ITypeSymbol dictionaryType, string keyTypeName, string valueTypeName, string comparerExpression)
    {
        _ = TryGetDictionaryTypes(dictionaryType, out _, out _, out var kind);
        dictionaryType = dictionaryType.WithNullableAnnotation(NullableAnnotation.NotAnnotated);
        return kind switch
        {
            // The indexer and ContainsKey may be implemented explicitly, so the dictionary is used through the interface.
            DictionaryKind.MutableDictionary => "global::System.Collections.Generic.IDictionary<" + keyTypeName + ", " + valueTypeName + "> dictionary = new " + dictionaryType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) + "()",
            DictionaryKind.OrderedDictionary => "var dictionary = new global::System.Collections.Generic.OrderedDictionary<" + keyTypeName + ", " + valueTypeName + ">(" + comparerExpression + ")",
            _ => "var dictionary = new global::System.Collections.Generic.Dictionary<" + keyTypeName + ", " + valueTypeName + ">(" + comparerExpression + ")",
        };
    }

    /// <summary>Gets the expression converting the <c>dictionary</c> local to the dictionary type.</summary>
    private static string GetDictionaryResultExpression(ITypeSymbol dictionaryType)
    {
        _ = TryGetDictionaryTypes(dictionaryType, out _, out _, out var kind);
        return kind switch
        {
            DictionaryKind.MutableDictionary => "((" + dictionaryType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) + ")dictionary)",
            DictionaryKind.FrozenDictionary => "global::System.Collections.Frozen.FrozenDictionary.ToFrozenDictionary(dictionary, dictionary.Comparer)",
            _ => "dictionary",
        };
    }

    /// <summary>Matches <c>KeyValuePair&lt;TKey, TValue&gt;</c>, which is serialized as a mapping with a <c>Key</c> and a <c>Value</c> entry.</summary>
    private static bool TryGetKeyValuePairTypes(ITypeSymbol type, out ITypeSymbol keyType, out ITypeSymbol valueType)
    {
        if (type is INamedTypeSymbol { IsGenericType: true, TypeArguments.Length: 2 } named &&
            string.Equals(named.ConstructedFrom.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat), "global::System.Collections.Generic.KeyValuePair<TKey, TValue>", StringComparison.Ordinal))
        {
            keyType = named.TypeArguments[0];
            valueType = named.TypeArguments[1];
            return true;
        }

        keyType = null!;
        valueType = null!;
        return false;
    }

    private static bool IsSupportedDictionaryKeyType(ITypeSymbol type)
    {
        // An object key is read by the untyped converter and written like the reflection-based serializer writes it.
        if (type.SpecialType is SpecialType.System_String or SpecialType.System_Object)
        {
            return true;
        }

        if (type is INamedTypeSymbol enumType && enumType.TypeKind == TypeKind.Enum)
        {
            return true;
        }

        return IsKnownScalar(type);
    }

    private static bool TryGetCSharpUnionCases(INamedTypeSymbol type, out ImmutableArray<CSharpUnionCaseModel> cases, SourceGenerationOptionsModel? sourceGenerationOptions = null)
    {
        cases = ImmutableArray<CSharpUnionCaseModel>.Empty;

        if (!TryGetCSharpUnionCaseParameters(type, out var parameters))
        {
            return false;
        }

        var numberHandling = GetCSharpUnionNumberHandling(type);
        var visitedUnions = new HashSet<ITypeSymbol>(SymbolEqualityComparer.Default) { type };
        var builder = ImmutableArray.CreateBuilder<CSharpUnionCaseModel>(parameters.Count);
        foreach (var parameter in parameters)
        {
            var caseType = parameter.Type;
            var runtimeType = GetCSharpUnionRuntimeType(caseType);
            var exactKinds = CSharpUnionCaseKind.None;
            var fallbackKinds = CSharpUnionCaseKind.None;
            var converterTypes = new List<ITypeSymbol>();

            // The number handling of this union is applied by the exact match of the case, so it is not passed here.
            AddCSharpUnionCaseKinds(runtimeType, numberHandling: null, visitedUnions, sourceGenerationOptions, ref exactKinds, ref fallbackKinds, converterTypes);
            builder.Add(new CSharpUnionCaseModel(
                caseType,
                runtimeType,
                exactKinds,
                fallbackKinds,
                converterTypes.ToImmutableArray(),
                IsCSharpUnionNullableCase(parameter),
                IsSupportedNumberHandlingType(caseType) ? numberHandling : null));
        }

        cases = builder.MoveToImmutable();
        return true;
    }

    private static bool TryGetCSharpUnionCaseParameters(INamedTypeSymbol type, out List<IParameterSymbol> parameters)
    {
        parameters = [];
        if (!IsCSharpUnionDeclaration(type))
        {
            return false;
        }

        var hasValueProperty = false;
        foreach (var property in type.GetMembers("Value").OfType<IPropertySymbol>())
        {
            if (!property.IsStatic &&
                property.Type.SpecialType == SpecialType.System_Object &&
                property.GetMethod is { DeclaredAccessibility: Accessibility.Public } &&
                property.SetMethod is null)
            {
                hasValueProperty = true;
                break;
            }
        }

        if (!hasValueProperty)
        {
            return false;
        }

        foreach (var constructor in type.InstanceConstructors)
        {
            // Only single-parameter constructors declare cases. Like the reflection-based converter, other constructors,
            // such as a user-declared 'U(int a, int b) : this(a + b)', are ignored rather than disqualifying the union.
            if (constructor.DeclaredAccessibility == Accessibility.Public && constructor.Parameters.Length == 1)
            {
                parameters.Add(constructor.Parameters[0]);
            }
        }

        return parameters.Count > 0;
    }

    private static int? GetCSharpUnionNumberHandling(INamedTypeSymbol type)
    {
        var numberHandling = TryGetNumberHandlingFromAttributes(type.GetAttributes());
        return numberHandling is 0 ? null : numberHandling;
    }

    // Mirrors YamlCSharpUnionConverter.AddCaseKinds: computes the YAML kinds a case reads. The exact kinds are the kinds
    // the case type is represented by. The fallback kinds are the other scalar kinds the case can still read, such as a
    // number for a string case; they are only considered when no case matches the kind exactly. A nested union matches
    // the kinds of its own cases. A case whose type is a union being computed, such as 'union U(bool, U?)', never
    // matches. The converter types are the types whose runtime custom converter lets the case read any kind.
    private static void AddCSharpUnionCaseKinds(
        ITypeSymbol runtimeType,
        int? numberHandling,
        HashSet<ITypeSymbol> visitedUnions,
        SourceGenerationOptionsModel? sourceGenerationOptions,
        ref CSharpUnionCaseKind exactKinds,
        ref CSharpUnionCaseKind fallbackKinds,
        List<ITypeSymbol> converterTypes)
    {
        if (visitedUnions.Contains(runtimeType))
        {
            return;
        }

        if (!converterTypes.Contains(runtimeType, SymbolEqualityComparer.Default))
        {
            converterTypes.Add(runtimeType);
        }

        // A type-level converter, or a converter declared on the generation options, can represent the type by any kind.
        if (HasYamlConverterAttribute(runtimeType) ||
            (sourceGenerationOptions is not null && TryGetStaticOptionsConverterType(sourceGenerationOptions, runtimeType, out _)))
        {
            fallbackKinds |= CSharpUnionCaseKind.All;
        }

        // The number handling declared on a nested union lets its numeric cases read some string scalars, such as "42".
        if (numberHandling is { } nestedNumberHandling && CanCSharpUnionNumberHandlingReadStringScalars(runtimeType, nestedNumberHandling))
        {
            fallbackKinds |= CSharpUnionCaseKind.String;
        }

        if (runtimeType is INamedTypeSymbol namedType && TryGetCSharpUnionCaseParameters(namedType, out var parameters))
        {
            var unionNumberHandling = GetCSharpUnionNumberHandling(namedType);
            visitedUnions.Add(runtimeType);
            foreach (var parameter in parameters)
            {
                var caseNumberHandling = IsSupportedNumberHandlingType(parameter.Type) ? unionNumberHandling : null;
                AddCSharpUnionCaseKinds(GetCSharpUnionRuntimeType(parameter.Type), caseNumberHandling, visitedUnions, sourceGenerationOptions, ref exactKinds, ref fallbackKinds, converterTypes);
            }

            visitedUnions.Remove(runtimeType);
            return;
        }

        exactKinds |= GetCSharpUnionCaseKind(runtimeType);
        fallbackKinds |= GetCSharpUnionCaseFallbackKinds(runtimeType);
    }

    // SyntaxKind.UnionDeclaration was introduced in Roslyn 5.6. The generator compiles against an older
    // Roslyn, so the value is used directly instead of naming the enum member. The generator still
    // detects unions when it runs inside a compiler that supports them.
    private const int UnionDeclarationRawKind = 9082;

    private static bool IsCSharpUnionDeclaration(INamedTypeSymbol type)
    {
        // Like the reflection-based converter, a type marked with [Union] is a union even when it is not a union
        // declaration: a hand-written union type, or a union declared in a referenced assembly, which has no syntax.
        foreach (var attribute in type.GetAttributes())
        {
            if (attribute.AttributeClass is { Name: "UnionAttribute", ContainingNamespace: { } attributeNamespace } &&
                string.Equals(attributeNamespace.ToDisplayString(), "System.Runtime.CompilerServices", StringComparison.Ordinal))
            {
                return true;
            }
        }

        foreach (var syntaxReference in type.DeclaringSyntaxReferences)
        {
            if (syntaxReference.GetSyntax().IsKind((SyntaxKind)UnionDeclarationRawKind))
            {
                return true;
            }
        }

        return false;
    }

    private static ITypeSymbol GetCSharpUnionRuntimeType(ITypeSymbol type)
    {
        if (type is INamedTypeSymbol nullableType && nullableType.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T)
        {
            return nullableType.TypeArguments[0];
        }

        return type;
    }

    private static bool IsCSharpUnionNullableCase(IParameterSymbol parameter)
    {
        if (parameter.Type is INamedTypeSymbol nullableType && nullableType.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T)
        {
            return true;
        }

        if (parameter.Type.IsValueType)
        {
            return false;
        }

        return parameter.NullableAnnotation != NullableAnnotation.NotAnnotated;
    }

    private static CSharpUnionCaseKind GetCSharpUnionCaseKind(ITypeSymbol type)
    {
        if (type.SpecialType == SpecialType.System_Object)
        {
            return CSharpUnionCaseKind.All;
        }

        if (IsYamlNodeType(type))
        {
            return GetCSharpUnionYamlNodeKind(type);
        }

        if (type.SpecialType == SpecialType.System_Boolean)
        {
            return CSharpUnionCaseKind.Boolean;
        }

        if (IsCSharpUnionNumericCase(type))
        {
            return CSharpUnionCaseKind.Number;
        }

        if (type.SpecialType == SpecialType.System_String ||
            type.SpecialType == SpecialType.System_Char ||
            type is INamedTypeSymbol { TypeKind: TypeKind.Enum } ||
            IsCSharpUnionStringLikeSystemType(type))
        {
            return CSharpUnionCaseKind.String;
        }

        if (TryGetDictionaryTypes(type, out _, out _, out _))
        {
            return CSharpUnionCaseKind.Mapping;
        }

        if (TryGetArrayElementType(type, out _) ||
            TryGetSequenceElementType(type, out _, out _))
        {
            return CSharpUnionCaseKind.Sequence;
        }

        return CSharpUnionCaseKind.Mapping;
    }

    // Mirrors YamlCSharpUnionConverter.GetYamlNodeKind: a YAML model case reads the nodes of its own shape; the base node
    // types read any node.
    private static CSharpUnionCaseKind GetCSharpUnionYamlNodeKind(ITypeSymbol type)
    {
        for (var current = type; current is not null; current = current.BaseType)
        {
            switch (current.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat))
            {
                case "global::Meziantou.Framework.Yaml.Model.YamlSequence":
                    return CSharpUnionCaseKind.Sequence;
                case "global::Meziantou.Framework.Yaml.Model.YamlMapping":
                    return CSharpUnionCaseKind.Mapping;
                case "global::Meziantou.Framework.Yaml.Model.YamlValue":
                    return CSharpUnionCaseKind.Scalar;
                case "global::Meziantou.Framework.Yaml.Model.YamlContainer":
                    return CSharpUnionCaseKind.Sequence | CSharpUnionCaseKind.Mapping;
            }
        }

        return CSharpUnionCaseKind.All;
    }

    // Mirrors YamlCSharpUnionConverter.GetFallbackKinds: a string reads the text of any scalar, and a char or an enum reads
    // the text or the value of a number.
    private static CSharpUnionCaseKind GetCSharpUnionCaseFallbackKinds(ITypeSymbol type)
    {
        if (type.SpecialType == SpecialType.System_String)
        {
            return CSharpUnionCaseKind.Boolean | CSharpUnionCaseKind.Number;
        }

        if (type.SpecialType == SpecialType.System_Char || type is INamedTypeSymbol { TypeKind: TypeKind.Enum })
        {
            return CSharpUnionCaseKind.Number;
        }

        return CSharpUnionCaseKind.None;
    }

    /// <summary>
    /// Gets the name of the IEEE 754 floating-point type introduced in .NET 11 (<c>System.Numerics.BFloat16</c>,
    /// <c>Decimal32</c>, <c>Decimal64</c>, or <c>Decimal128</c>), or <see langword="null"/> when the type is not one of them.
    /// </summary>
    private static string? GetIeee754TypeName(ITypeSymbol type)
        => type is INamedTypeSymbol namedType &&
           string.Equals(namedType.ContainingNamespace?.ToDisplayString(), "System.Numerics", StringComparison.Ordinal) &&
           namedType.Name is "BFloat16" or "Decimal32" or "Decimal64" or "Decimal128"
            ? namedType.Name
            : null;

    private static bool IsCSharpUnionNumericCase(ITypeSymbol type)
    {
        if (type.SpecialType is SpecialType.System_Byte
            or SpecialType.System_SByte
            or SpecialType.System_Int16
            or SpecialType.System_UInt16
            or SpecialType.System_Int32
            or SpecialType.System_UInt32
            or SpecialType.System_Int64
            or SpecialType.System_UInt64
            or SpecialType.System_IntPtr
            or SpecialType.System_UIntPtr
            or SpecialType.System_Single
            or SpecialType.System_Double
            or SpecialType.System_Decimal)
        {
            return true;
        }

        if (GetIeee754TypeName(type) is not null || IsBigIntegerType(type))
        {
            return true;
        }

        return type is INamedTypeSymbol systemType &&
               string.Equals(systemType.ContainingNamespace?.ToDisplayString(), "System", StringComparison.Ordinal) &&
               (string.Equals(systemType.Name, "Half", StringComparison.Ordinal) ||
                string.Equals(systemType.Name, "Int128", StringComparison.Ordinal) ||
                string.Equals(systemType.Name, "UInt128", StringComparison.Ordinal));
    }

    private static bool IsCSharpUnionStringLikeSystemType(ITypeSymbol type)
        => IsUriType(type) ||
           IsCultureInfoType(type) ||
           IsVersionType(type) ||
           IsRuneType(type) ||
           (type is INamedTypeSymbol systemType &&
           string.Equals(systemType.ContainingNamespace?.ToDisplayString(), "System", StringComparison.Ordinal) &&
           (string.Equals(systemType.Name, "DateTime", StringComparison.Ordinal) ||
            string.Equals(systemType.Name, "DateTimeOffset", StringComparison.Ordinal) ||
            string.Equals(systemType.Name, "Guid", StringComparison.Ordinal) ||
            string.Equals(systemType.Name, "TimeSpan", StringComparison.Ordinal) ||
            string.Equals(systemType.Name, "DateOnly", StringComparison.Ordinal) ||
            string.Equals(systemType.Name, "TimeOnly", StringComparison.Ordinal)));

    private static ImmutableArray<CSharpUnionCaseModel> CollapseCSharpUnionNullableOverloads(ImmutableArray<CSharpUnionCaseModel> cases)
    {
        var builder = ImmutableArray.CreateBuilder<CSharpUnionCaseModel>(cases.Length);
        foreach (var unionCase in cases)
        {
            var replaced = false;
            for (var i = 0; i < builder.Count; i++)
            {
                if (!SymbolEqualityComparer.Default.Equals(builder[i].RuntimeType, unionCase.RuntimeType))
                {
                    continue;
                }

                // 'union Foo(int, int?)' declares two cases for the same underlying type. They are indistinguishable
                // when reading a non-null value, so keep the non-nullable overload as the canonical one. The nullable
                // overload is still used when reading a null scalar.
                if (builder[i].Type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T })
                {
                    builder[i] = unionCase;
                }

                replaced = true;
                break;
            }

            if (!replaced)
            {
                builder.Add(unionCase);
            }
        }

        return builder.ToImmutable();
    }

    // Mirrors YamlCSharpUnionConverter.SortCasesForWriting: every case comes before the cases its type derives from, so
    // the first 'is' check matching a value selects the most specific case. A comparison sort cannot do this because
    // unrelated types compare as equal, which is not a consistent ordering. Unrelated cases keep their declaration order.
    private static ImmutableArray<CSharpUnionCaseModel> SortCSharpUnionCasesForWriting(ImmutableArray<CSharpUnionCaseModel> cases)
    {
        var remaining = new List<CSharpUnionCaseModel>(cases);
        var builder = ImmutableArray.CreateBuilder<CSharpUnionCaseModel>(cases.Length);
        while (remaining.Count > 0)
        {
            var index = 0;
            for (var i = 0; i < remaining.Count; i++)
            {
                if (!HasMoreSpecificCSharpUnionCase(remaining, remaining[i]))
                {
                    index = i;
                    break;
                }
            }

            builder.Add(remaining[index]);
            remaining.RemoveAt(index);
        }

        return builder.MoveToImmutable();

        static bool HasMoreSpecificCSharpUnionCase(List<CSharpUnionCaseModel> cases, CSharpUnionCaseModel unionCase)
        {
            foreach (var other in cases)
            {
                if (!SymbolEqualityComparer.Default.Equals(other.RuntimeType, unionCase.RuntimeType) &&
                    IsCSharpUnionCaseTypeAssignableTo(other.RuntimeType, unionCase.RuntimeType))
                {
                    return true;
                }
            }

            return false;
        }
    }

    // Mirrors Type.IsAssignableFrom for the runtime types of union cases, including the variance of generic interfaces and
    // array covariance, such as 'string[]' or 'IEnumerable<string>' converting to 'IEnumerable<object>'.
    private static bool IsCSharpUnionCaseTypeAssignableTo(ITypeSymbol type, ITypeSymbol baseType)
    {
        // Every type, including interfaces and arrays, converts to object.
        if (baseType.SpecialType == SpecialType.System_Object || SymbolEqualityComparer.Default.Equals(type, baseType))
        {
            return true;
        }

        if (type is IArrayTypeSymbol arrayType &&
            baseType is IArrayTypeSymbol baseArrayType &&
            arrayType.Rank == baseArrayType.Rank &&
            arrayType.ElementType.IsReferenceType &&
            baseArrayType.ElementType.IsReferenceType &&
            IsCSharpUnionCaseTypeAssignableTo(arrayType.ElementType, baseArrayType.ElementType))
        {
            return true;
        }

        if (IsCSharpUnionCaseVariantConvertible(type, baseType))
        {
            return true;
        }

        for (var current = type.BaseType; current is not null; current = current.BaseType)
        {
            if (SymbolEqualityComparer.Default.Equals(current, baseType))
            {
                return true;
            }
        }

        foreach (var implementedInterface in type.AllInterfaces)
        {
            if (IsCSharpUnionCaseVariantConvertible(implementedInterface, baseType))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsCSharpUnionCaseVariantConvertible(ITypeSymbol type, ITypeSymbol baseType)
    {
        if (SymbolEqualityComparer.Default.Equals(type, baseType))
        {
            return true;
        }

        if (type is not INamedTypeSymbol { IsGenericType: true, TypeKind: TypeKind.Interface or TypeKind.Delegate } namedType ||
            baseType is not INamedTypeSymbol { IsGenericType: true } namedBaseType ||
            !SymbolEqualityComparer.Default.Equals(namedType.OriginalDefinition, namedBaseType.OriginalDefinition))
        {
            return false;
        }

        var typeParameters = namedType.OriginalDefinition.TypeParameters;
        for (var i = 0; i < typeParameters.Length; i++)
        {
            var argument = namedType.TypeArguments[i];
            var baseArgument = namedBaseType.TypeArguments[i];
            if (SymbolEqualityComparer.Default.Equals(argument, baseArgument))
            {
                continue;
            }

            // Variance only applies to reference type arguments.
            var isConvertible = argument.IsReferenceType && baseArgument.IsReferenceType && typeParameters[i].Variance switch
            {
                VarianceKind.Out => IsCSharpUnionCaseTypeAssignableTo(argument, baseArgument),
                VarianceKind.In => IsCSharpUnionCaseTypeAssignableTo(baseArgument, argument),
                _ => false,
            };

            if (!isConvertible)
            {
                return false;
            }
        }

        return true;
    }

    private static CSharpUnionCaseModel? GetFirstNullableCSharpUnionCase(ImmutableArray<CSharpUnionCaseModel> cases)
    {
        for (var i = 0; i < cases.Length; i++)
        {
            var unionCase = cases[i];
            if (unionCase.AcceptsNull)
            {
                return unionCase;
            }
        }

        return null;
    }

    // Whether the number handling declared on the union lets a numeric case read some string scalars, such as "42".
    // Whether a given scalar is readable is only known at runtime.
    private static bool CanCSharpUnionNumberHandlingReadStringScalars(ITypeSymbol type, int numberHandling)
    {
        const int AllowReadingFromString = 1;
        const int AllowNamedFloatingPointLiterals = 4;

        return (numberHandling & AllowReadingFromString) != 0 ||
               ((numberHandling & AllowNamedFloatingPointLiterals) != 0 && (type.SpecialType is SpecialType.System_Single or SpecialType.System_Double || GetIeee754TypeName(type) is not null));
    }

    // Mirrors YamlNumberHandlingConverter.CanReadStringScalar, which is internal to the runtime library.
    private static string? GetCSharpUnionCaseStringScalarCondition(CSharpUnionCaseModel unionCase)
    {
        const int AllowReadingFromString = 1;
        const int AllowNamedFloatingPointLiterals = 4;

        if (unionCase.NumberHandling is not { } numberHandling)
        {
            return null;
        }

        string? condition = null;
        if ((numberHandling & AllowNamedFloatingPointLiterals) != 0 &&
            (unionCase.RuntimeType.SpecialType is SpecialType.System_Single or SpecialType.System_Double || GetIeee754TypeName(unionCase.RuntimeType) is not null))
        {
            condition = "reader.ScalarValue is \"NaN\" or \"Infinity\" or \"+Infinity\" or \"-Infinity\"";
        }

        if ((numberHandling & AllowReadingFromString) != 0)
        {
            const string ParseCondition = "global::Meziantou.Framework.Yaml.Serialization.YamlScalar.TryParseInt64(reader, out _) || global::Meziantou.Framework.Yaml.Serialization.YamlScalar.TryParseDouble(reader, out _)";
            condition = condition is null ? ParseCondition : condition + " || " + ParseCondition;
        }

        return condition;
    }

    private static string GetCSharpUnionKindDescription(CSharpUnionCaseKind kind)
        => kind switch
        {
            CSharpUnionCaseKind.Boolean => "boolean",
            CSharpUnionCaseKind.Number => "number",
            CSharpUnionCaseKind.String => "scalar string",
            CSharpUnionCaseKind.Sequence => "sequence",
            _ => "mapping",
        };

    private static bool IsUriType(ITypeSymbol type)
        => type is INamedTypeSymbol named &&
           string.Equals(named.Name, "Uri", StringComparison.Ordinal) &&
           string.Equals(named.ContainingNamespace?.ToDisplayString(), "System", StringComparison.Ordinal);

    private static bool IsCultureInfoType(ITypeSymbol type)
        => type is INamedTypeSymbol named &&
           string.Equals(named.Name, "CultureInfo", StringComparison.Ordinal) &&
           string.Equals(named.ContainingNamespace?.ToDisplayString(), "System.Globalization", StringComparison.Ordinal);

    private static bool IsVersionType(ITypeSymbol type)
        => type is INamedTypeSymbol named &&
           string.Equals(named.Name, "Version", StringComparison.Ordinal) &&
           string.Equals(named.ContainingNamespace?.ToDisplayString(), "System", StringComparison.Ordinal);

    private static bool IsBigIntegerType(ITypeSymbol type)
        => type is INamedTypeSymbol named &&
           string.Equals(named.Name, "BigInteger", StringComparison.Ordinal) &&
           string.Equals(named.ContainingNamespace?.ToDisplayString(), "System.Numerics", StringComparison.Ordinal);

    private static bool IsRuneType(ITypeSymbol type)
        => type is INamedTypeSymbol named &&
           string.Equals(named.Name, "Rune", StringComparison.Ordinal) &&
           string.Equals(named.ContainingNamespace?.ToDisplayString(), "System.Text", StringComparison.Ordinal);

    private static bool IsKnownScalar(ITypeSymbol type)
    {
        if (type is INamedTypeSymbol nullableType && nullableType.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T)
        {
            return IsKnownScalar(nullableType.TypeArguments[0]);
        }

        if (type is INamedTypeSymbol named && named.TypeKind == TypeKind.Enum)
        {
            return true;
        }

        if (GetIeee754TypeName(type) is not null)
        {
            return true;
        }

        if (IsUriType(type) || IsCultureInfoType(type) || IsVersionType(type) || IsBigIntegerType(type) || IsRuneType(type))
        {
            return true;
        }

        if (type is INamedTypeSymbol systemType &&
            string.Equals(systemType.ContainingNamespace?.ToDisplayString(), "System", StringComparison.Ordinal))
        {
            // Common non-primitive scalars supported out of the box (mirrors STJ built-ins).
            if (string.Equals(systemType.Name, "DateTime", StringComparison.Ordinal) ||
                string.Equals(systemType.Name, "DateTimeOffset", StringComparison.Ordinal) ||
                string.Equals(systemType.Name, "Guid", StringComparison.Ordinal) ||
                string.Equals(systemType.Name, "TimeSpan", StringComparison.Ordinal) ||
                string.Equals(systemType.Name, "DateOnly", StringComparison.Ordinal) ||
                string.Equals(systemType.Name, "TimeOnly", StringComparison.Ordinal) ||
                string.Equals(systemType.Name, "Half", StringComparison.Ordinal) ||
                string.Equals(systemType.Name, "Int128", StringComparison.Ordinal) ||
                string.Equals(systemType.Name, "UInt128", StringComparison.Ordinal))
            {
                return true;
            }
        }

        return type.SpecialType is SpecialType.System_String
            or SpecialType.System_Boolean
            or SpecialType.System_Byte
            or SpecialType.System_SByte
            or SpecialType.System_Int16
            or SpecialType.System_UInt16
            or SpecialType.System_Int32
            or SpecialType.System_UInt32
            or SpecialType.System_Int64
            or SpecialType.System_UInt64
            or SpecialType.System_IntPtr
            or SpecialType.System_UIntPtr
            or SpecialType.System_Single
            or SpecialType.System_Double
            or SpecialType.System_Decimal
            or SpecialType.System_Char;
    }

    private static bool ImplementsAnyYamlLifecycleCallback(INamedTypeSymbol type)
    {
        foreach (var iface in type.AllInterfaces)
        {
            var name = iface.ToDisplayString();
            if (string.Equals(name, "Meziantou.Framework.Yaml.Serialization.IYamlOnDeserializing", StringComparison.Ordinal) ||
                string.Equals(name, "Meziantou.Framework.Yaml.Serialization.IYamlOnDeserialized", StringComparison.Ordinal) ||
                string.Equals(name, "Meziantou.Framework.Yaml.Serialization.IYamlOnSerializing", StringComparison.Ordinal) ||
                string.Equals(name, "Meziantou.Framework.Yaml.Serialization.IYamlOnSerialized", StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static bool TrySelectDeserializationConstructor(INamedTypeSymbol type, out IMethodSymbol? selectedConstructor, out string? notSupportedMessage)
    {
        selectedConstructor = null;
        notSupportedMessage = null;

        IMethodSymbol? attributed = null;
        foreach (var ctor in type.InstanceConstructors)
        {
            if (HasAttribute(ctor, "Meziantou.Framework.Yaml.Serialization.YamlConstructorAttribute"))
            {
                if (attributed is not null)
                {
                    notSupportedMessage = $"Type '{type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}' defines multiple constructors annotated with [YamlConstructor].";
                    return false;
                }

                attributed = ctor;
            }
        }

        // A value type can always be created without calling a constructor, so it only uses the one it opts into.
        if (attributed is not null || type.IsValueType)
        {
            selectedConstructor = attributed;
            return true;
        }

        foreach (var ctor in type.InstanceConstructors)
        {
            if (ctor.DeclaredAccessibility == Accessibility.Public && ctor.Parameters.Length == 0)
            {
                selectedConstructor = ctor;
                return true;
            }
        }

        var publicCtors = type.InstanceConstructors.Where(static ctor => ctor.DeclaredAccessibility == Accessibility.Public).ToArray();
        if (publicCtors.Length == 1)
        {
            selectedConstructor = publicCtors[0];
            return true;
        }

        if (publicCtors.Length == 0)
        {
            notSupportedMessage = $"Type '{type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}' does not have a public constructor. Use [YamlConstructor] to opt into a non-public constructor.";
            return false;
        }

        notSupportedMessage = $"Type '{type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}' defines multiple public constructors. Use [YamlConstructor] to select the constructor to use for deserialization.";
        return false;
    }

    private static string GetOptionalParameterDefaultValueExpression(IParameterSymbol parameter)
    {
        if (!parameter.HasExplicitDefaultValue)
        {
            throw new InvalidOperationException("Parameter does not define an explicit default value.");
        }

        var value = parameter.ExplicitDefaultValue;
        if (value is null)
        {
            return "default";
        }

        if (value is string str)
        {
            return ToLiteral(str);
        }

        if (value is bool boolean)
        {
            return boolean ? "true" : "false";
        }

        if (value is char ch)
        {
            return SymbolDisplay.FormatLiteral(ch, quote: true);
        }

        var enumType = parameter.Type switch
        {
            INamedTypeSymbol { TypeKind: TypeKind.Enum } type => type,
            INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T, TypeArguments: [INamedTypeSymbol { TypeKind: TypeKind.Enum } underlyingType] } => underlyingType,
            _ => null,
        };
        if (enumType is not null)
        {
            // The value is parenthesized: a cast of a negative literal is otherwise parsed as a subtraction.
            var enumTypeName = enumType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            return $"({enumTypeName})({GetNumericLiteral(value)})";
        }

        return GetNumericLiteral(value);
    }

    private static string GetNumericLiteral(object value)
    {
        // A literal needs the suffix of its type: a decimal or a float default value written without one is a double
        // literal, which does not convert implicitly.
        return value switch
        {
            decimal number => number.ToString(CultureInfo.InvariantCulture) + "m",
            float number when float.IsNaN(number) => "float.NaN",
            float number when float.IsPositiveInfinity(number) => "float.PositiveInfinity",
            float number when float.IsNegativeInfinity(number) => "float.NegativeInfinity",
            float number => number.ToString("R", CultureInfo.InvariantCulture) + "f",
            double number when double.IsNaN(number) => "double.NaN",
            double number when double.IsPositiveInfinity(number) => "double.PositiveInfinity",
            double number when double.IsNegativeInfinity(number) => "double.NegativeInfinity",
            double number => number.ToString("R", CultureInfo.InvariantCulture) + "d",
            _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? "default",
        };
    }

    private static MemberModel CreateMemberModel(ISymbol member, INamedTypeSymbol declaringType, YamlNamingPolicy? propertyNamingPolicy, UnsafeAccessorRegistry accessors)
    {
        var serializedName = GetSerializedMemberName(member, declaringType, propertyNamingPolicy);
        var nameForRead = ToLiteral(serializedName);
        var nameForWrite = nameForRead;
        var type = GetMemberType(member) ?? throw new InvalidOperationException("Member type could not be determined.");
        var (accessExpression, assign, usesAccessorForWrite) = CreateMemberAccessExpressions(member, declaringType, accessors);
        var memberIgnoreCondition = TryGetIgnoreCondition(member, out var rawCondition) ? rawCondition : GetDeclaredIgnoreCondition(declaringType);
        var converterTypeName = GetYamlConverterAttributeTypeName(member, type);
        var objectCreationHandling = GetObjectCreationHandling(member);
        var (blockSequenceMappingStyle, blockSequenceSequenceStyle) = GetBlockSequenceItemStyles(member);
        var stringStyle = GetStringStyle(member);
        var isRequiredKeyword = member is IPropertySymbol { IsRequired: true } or IFieldSymbol { IsRequired: true };
        var isRequired = isRequiredKeyword || HasAttribute(member, "Meziantou.Framework.Yaml.Serialization.YamlRequiredAttribute");
        var isIgnoredOnRead = memberIgnoreCondition == IgnoreWhenReading;
        var isInitOnly = member is IPropertySymbol property && IsInitOnlyProperty(property);
        var hasIncludeAttribute = HasAttribute(member, "Meziantou.Framework.Yaml.Serialization.YamlIncludeAttribute");
        var requiresIncludeFields = member is IFieldSymbol { DeclaredAccessibility: Accessibility.Public } && !hasIncludeAttribute;
        var isReadOnlyProperty = member is IPropertySymbol && !IsWritableMember(member);
        var isReadOnlyField = member is IFieldSymbol { IsReadOnly: true };
        var disallowNull = IsNonNullableReferenceType(type);
        var numberHandling = converterTypeName is null ? GetNumberHandlingValue(member, type, declaringType) : null;
        var enumCustomNames = converterTypeName is null ? GetEnumCustomNames(type) : null;
        var skipObjectInitializer = usesAccessorForWrite || RequiresConstructorAccessor(declaringType, accessors) || CreatesInstanceBeforeReadingMembers(declaringType);
        return new MemberModel(member, type, nameForRead, nameForWrite, accessExpression, assign, memberIgnoreCondition, converterTypeName, objectCreationHandling, blockSequenceMappingStyle, blockSequenceSequenceStyle, stringStyle, isRequired, isIgnoredOnRead, isInitOnly, isRequiredKeyword, requiresIncludeFields, disallowNull, disallowNull, isReadOnlyProperty, isReadOnlyField, skipObjectInitializer, numberHandling, enumCustomNames)
        {
            SerializedName = serializedName,
            Order = GetMemberOrder(member),
        };
    }

    /// <summary>
    /// Builds the read and write expressions for a member, falling back to <c>[UnsafeAccessor]</c> stubs when the
    /// member cannot be referenced directly from the generated context.
    /// </summary>
    private static (Func<string, string> Access, Func<string, string> Assign, bool UsesAccessorForWrite) CreateMemberAccessExpressions(
        ISymbol member,
        INamedTypeSymbol declaringType,
        UnsafeAccessorRegistry accessors)
    {
        if (member is IPropertySymbol property)
        {
            Func<string, string> access = property.GetMethod is { } getMethod && !accessors.IsAccessible(getMethod)
                ? receiver => accessors.GetPropertyReadExpression(property, getMethod, receiver)
                : receiver => receiver + "." + EscapeIdentifier(property.Name);

            // An init-only setter can only be called from an object initializer, which is not available when the
            // instance itself is created through an accessor, nor for a value type or a class created before its members
            // are read, whose members are assigned after it is created.
            if (property.SetMethod is { } setMethod &&
                (!accessors.IsAccessible(setMethod) ||
                 ((IsInitOnlyProperty(property) || property.IsRequired) && (declaringType.IsValueType || RequiresConstructorAccessor(declaringType, accessors))) ||
                 (IsInitOnlyProperty(property) && CreatesInstanceBeforeReadingMembers(declaringType))))
            {
                return (access, rhs => accessors.GetPropertyWriteExpression(property, setMethod, "instance", rhs), true);
            }

            return (access, rhs => "instance." + EscapeIdentifier(property.Name) + " = " + rhs, false);
        }

        if (member is IFieldSymbol field && !accessors.IsAccessible(field))
        {
            return (receiver => accessors.GetFieldExpression(field, receiver), rhs => accessors.GetFieldExpression(field, "instance") + " = " + rhs, true);
        }

        return (receiver => receiver + "." + EscapeIdentifier(member.Name), rhs => "instance." + EscapeIdentifier(member.Name) + " = " + rhs, false);
    }

    /// <summary>
    /// Indicates the deserialization constructor of <paramref name="type"/> can only be invoked through an <c>[UnsafeAccessor]</c> stub.
    /// </summary>
    private static bool RequiresConstructorAccessor(INamedTypeSymbol type, UnsafeAccessorRegistry accessors)
        => GetInaccessibleDeserializationConstructor(type, accessors) is not null;

    /// <summary>
    /// Indicates an instance of <paramref name="type"/> is created by its parameterless deserialization constructor before
    /// its members are read, as the reflection-based contract does. Init-only members are then assigned through accessors
    /// rather than an object initializer, so a populated member, an anchor, and <c>IYamlOnDeserializing</c> all observe the
    /// instance being read.
    /// </summary>
    internal static bool CreatesInstanceBeforeReadingMembers(INamedTypeSymbol type)
        => type is { TypeKind: TypeKind.Class, IsAbstract: false } &&
           TrySelectDeserializationConstructor(type, out var constructor, out _) &&
           constructor is { Parameters.Length: 0 };

    /// <summary>
    /// Indicates <paramref name="type"/> declares or inherits a <c>required</c> member that an object creation expression
    /// using <paramref name="constructor"/> must set, because the constructor is not annotated with <c>SetsRequiredMembers</c>.
    /// </summary>
    internal static bool HasRequiredMembersNotSetByConstructor(INamedTypeSymbol type, IMethodSymbol constructor)
    {
        if (constructor.GetAttributes().Any(static attribute => string.Equals(attribute.AttributeClass?.ToDisplayString(), "System.Diagnostics.CodeAnalysis.SetsRequiredMembersAttribute", StringComparison.Ordinal)))
        {
            return false;
        }

        for (var current = type; current is not null; current = current.BaseType)
        {
            if (current.GetMembers().Any(static member => member is IPropertySymbol { IsRequired: true } or IFieldSymbol { IsRequired: true }))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Gets the deserialization constructor of <paramref name="type"/> when the generated context cannot invoke it directly.
    /// </summary>
    private static IMethodSymbol? GetInaccessibleDeserializationConstructor(INamedTypeSymbol type, UnsafeAccessorRegistry accessors)
    {
        if (type.TypeKind is not (TypeKind.Class or TypeKind.Struct) || type.IsAbstract)
        {
            return null;
        }

        if (!TrySelectDeserializationConstructor(type, out var constructor, out _) || constructor is null)
        {
            return null;
        }

        return accessors.IsAccessible(constructor) ? null : constructor;
    }

    private static string GetSerializedMemberName(ISymbol member, INamedTypeSymbol declaringType, YamlNamingPolicy? propertyNamingPolicy)
    {
        foreach (var attribute in GetMemberAttributes(member))
        {
            if (attribute.AttributeClass is null)
            {
                continue;
            }

            if (string.Equals(attribute.AttributeClass.ToDisplayString(), "Meziantou.Framework.Yaml.Serialization.YamlPropertyNameAttribute", StringComparison.Ordinal))
            {
                if (attribute.ConstructorArguments.Length == 1 && attribute.ConstructorArguments[0].Value is string yamlName)
                {
                    return yamlName;
                }
            }
        }

        var declaredPolicy = TryGetDeclaredNamingPolicy(GetMemberAttributes(member)) ?? GetDeclaredNamingPolicy(declaringType);
        var name = declaredPolicy is not null
            ? ApplyNamingPolicy(member.Name, YamlNamingPolicy.GetPolicy(declaredPolicy.Value))
            : ApplyNamingPolicy(member.Name, propertyNamingPolicy);
        return name;
    }

    private static int GetMemberOrder(ISymbol member)
    {
        foreach (var attribute in GetMemberAttributes(member))
        {
            if (string.Equals(attribute.AttributeClass?.ToDisplayString(), "Meziantou.Framework.Yaml.Serialization.YamlPropertyOrderAttribute", StringComparison.Ordinal) &&
                attribute.ConstructorArguments.Length == 1 &&
                attribute.ConstructorArguments[0].Value is int order)
            {
                return order;
            }
        }

        return 0;
    }

    private static string ApplyNamingPolicy(string name, YamlNamingPolicy? policy)
    {
        return policy?.ConvertName(name) ?? name;
    }

    private static YamlKnownNamingPolicy? GetDeclaredNamingPolicy(INamedTypeSymbol? type)
    {
        for (var current = type; current is not null; current = current.BaseType)
        {
            var policy = TryGetDeclaredNamingPolicy(current.GetAttributes());
            if (policy is not null)
            {
                return policy;
            }
        }

        return null;
    }

    private static YamlKnownNamingPolicy? TryGetDeclaredNamingPolicy(IEnumerable<AttributeData> attributes)
    {
        foreach (var attribute in attributes)
        {
            if (attribute.AttributeClass is null)
            {
                continue;
            }

            if (!string.Equals(attribute.AttributeClass.ToDisplayString(), "Meziantou.Framework.Yaml.Serialization.YamlNamingPolicyAttribute", StringComparison.Ordinal))
            {
                continue;
            }

            if (attribute.ConstructorArguments.Length == 1 && attribute.ConstructorArguments[0].Value is int value)
            {
                return (YamlKnownNamingPolicy)value;
            }
        }

        return null;
    }

    private static YamlNamingPolicy? ResolveNamingPolicy(string? policyName)
    {
        if (string.IsNullOrEmpty(policyName) || string.Equals(policyName, "Unspecified", StringComparison.Ordinal))
        {
            return null;
        }

        return policyName switch
        {
            "CamelCase" => YamlNamingPolicy.CamelCase,
            "SnakeCaseLower" => YamlNamingPolicy.SnakeCaseLower,
            "SnakeCaseUpper" => YamlNamingPolicy.SnakeCaseUpper,
            "KebabCaseLower" => YamlNamingPolicy.KebabCaseLower,
            "KebabCaseUpper" => YamlNamingPolicy.KebabCaseUpper,
            "PascalCase" => YamlNamingPolicy.PascalCase,
            _ => null,
        };
    }

    private static int? GetNumberHandlingValue(ISymbol member, ITypeSymbol memberType, INamedTypeSymbol declaringType)
    {
        if (!IsSupportedNumberHandlingType(memberType))
        {
            return null;
        }

        // A type-level attribute applies to every member of the serialized type, including the inherited ones, and is
        // inherited from the base types of the serialized type.
        var value = TryGetNumberHandlingFromAttributes(GetMemberAttributes(member));
        for (var current = declaringType; value is null && current is not null; current = current.BaseType)
        {
            value = TryGetNumberHandlingFromAttributes(current.GetAttributes());
        }

        if (value is null || value.Value == 0)
        {
            return null;
        }

        return value;
    }

    private static int? TryGetNumberHandlingFromAttributes(IEnumerable<AttributeData> attributes)
    {
        foreach (var attribute in attributes)
        {
            if (string.Equals(attribute.AttributeClass?.ToDisplayString(), "Meziantou.Framework.Yaml.Serialization.YamlNumberHandlingAttribute", StringComparison.Ordinal))
            {
                return GetNumberHandlingArgument(attribute);
            }
        }

        return null;
    }

    private static int? GetNumberHandlingArgument(AttributeData attribute)
    {
        if (attribute.ConstructorArguments.Length >= 1 && attribute.ConstructorArguments[0].Value is int value)
        {
            return value;
        }

        foreach (var pair in attribute.NamedArguments)
        {
            if (string.Equals(pair.Key, "Handling", StringComparison.Ordinal) && pair.Value.Value is int namedValue)
            {
                return namedValue;
            }
        }

        return null;
    }

    private static bool IsSupportedNumberHandlingType(ITypeSymbol type)
    {
        var underlying = type;
        if (type is INamedTypeSymbol named &&
            named.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T &&
            named.TypeArguments.Length == 1)
        {
            underlying = named.TypeArguments[0];
        }

        switch (underlying.SpecialType)
        {
            case SpecialType.System_Byte:
            case SpecialType.System_SByte:
            case SpecialType.System_Int16:
            case SpecialType.System_UInt16:
            case SpecialType.System_Int32:
            case SpecialType.System_UInt32:
            case SpecialType.System_Int64:
            case SpecialType.System_UInt64:
            case SpecialType.System_Single:
            case SpecialType.System_Double:
            case SpecialType.System_Decimal:
            case SpecialType.System_IntPtr:
            case SpecialType.System_UIntPtr:
                return true;
            default:
                return GetIeee754TypeName(underlying) is not null;
        }
    }

    private static List<(string Member, string Scalar)>? GetEnumCustomNames(ITypeSymbol enumType)
    {
        if (enumType is not INamedTypeSymbol named || named.TypeKind != TypeKind.Enum)
        {
            return null;
        }

        var enumTypeName = named.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        List<(string Member, string Scalar)>? result = null;
        foreach (var member in named.GetMembers())
        {
            if (member is not IFieldSymbol { IsStatic: true, HasConstantValue: true } field)
            {
                continue;
            }

            var name = GetEnumMemberCustomName(field);
            if (name is null)
            {
                continue;
            }

            result ??= new List<(string, string)>();
            result.Add((enumTypeName + "." + field.Name, name));
        }

        return result;
    }

    private static string? GetEnumMemberCustomName(IFieldSymbol field)
    {
        foreach (var attribute in field.GetAttributes())
        {
            if (string.Equals(attribute.AttributeClass?.ToDisplayString(), "Meziantou.Framework.Yaml.Serialization.YamlEnumMemberNameAttribute", StringComparison.Ordinal) &&
                attribute.ConstructorArguments.Length == 1 &&
                attribute.ConstructorArguments[0].Value is string yamlName)
            {
                return yamlName;
            }
        }

        return null;
    }

    private static void EmitEnumWriteSwitch(StringBuilder builder, List<(string Member, string Scalar)> names, string valueExpression, string indent)
    {
        builder.Append(indent).Append("switch (").Append(valueExpression).AppendLine(")");
        builder.Append(indent).AppendLine("{");
        foreach (var (member, scalar) in names)
        {
            builder.Append(indent).Append("    case ").Append(member).Append(": writer.WriteString(").Append(ToLiteral(scalar)).AppendLine("); break;");
        }

        builder.Append(indent).Append("    default: writer.WriteScalar(FormatYamlEnumName(").Append(valueExpression).AppendLine(")); break;");
        builder.Append(indent).AppendLine("}");
    }

    private static void EmitEnumReadChain(
        StringBuilder builder,
        List<(string Member, string Scalar)> names,
        string textExpression,
        string indent,
        Func<string, string> emitAssign,
        Action emitOnMatched,
        Action emitFallback)
    {
        var first = true;
        foreach (var (member, scalar) in names)
        {
            builder.Append(indent).Append(first ? "if (" : "else if (")
                .Append("global::System.String.Equals(").Append(textExpression).Append(", ").Append(ToLiteral(scalar)).AppendLine(", global::System.StringComparison.OrdinalIgnoreCase))");
            builder.Append(indent).AppendLine("{");
            builder.Append(indent).Append("    ").Append(emitAssign(member)).AppendLine(";");
            emitOnMatched();
            builder.Append(indent).AppendLine("}");
            first = false;
        }

        builder.Append(indent).AppendLine("else");
        builder.Append(indent).AppendLine("{");
        emitFallback();
        builder.Append(indent).AppendLine("}");
    }

    private static ITypeSymbol? GetYamlConverterAttributeType(ISymbol member)
    {
        foreach (var attribute in GetMemberAttributes(member))
        {
            if (attribute.AttributeClass is null)
            {
                continue;
            }

            if (!string.Equals(attribute.AttributeClass.ToDisplayString(), "Meziantou.Framework.Yaml.Serialization.YamlConverterAttribute", StringComparison.Ordinal))
            {
                continue;
            }

            if (attribute.ConstructorArguments.Length != 1)
            {
                continue;
            }

            var argument = attribute.ConstructorArguments[0];
            if (argument.Kind != TypedConstantKind.Type || argument.Value is not ITypeSymbol converterType)
            {
                continue;
            }

            return converterType;
        }

        return null;
    }

    private static bool HasYamlConverterAttribute(ISymbol member)
        => GetYamlConverterAttributeType(member) is not null;

    /// <summary>
    /// Gets the fully qualified name of the converter declared by <c>[YamlConverter]</c> on <paramref name="member"/>.
    /// An open generic converter type is closed over the generic arguments of <paramref name="typeToConvert"/>;
    /// <see langword="null"/> is returned when the converter cannot be constructed for that type.
    /// </summary>
    private static string? GetYamlConverterAttributeTypeName(ISymbol member, ITypeSymbol typeToConvert)
    {
        var converterType = GetYamlConverterAttributeType(member);
        if (converterType is null)
        {
            return null;
        }

        if (!IsOpenGenericConverterType(converterType))
        {
            return converterType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        }

        return TryConstructOpenGenericConverterType((INamedTypeSymbol)converterType, typeToConvert, out var constructed)
            ? constructed.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
            : null;
    }

    private static bool IsOpenGenericConverterType(ITypeSymbol converterType)
        => converterType is INamedTypeSymbol named && GetAllTypeParameters(named).Length > 0 && IsOpenGenericType(named);

    /// <summary>
    /// Closes an open generic converter type over the generic arguments of <paramref name="typeToConvert"/>.
    /// Both arities count the type parameters of the containing types, so nested converters such as
    /// <c>Outer&lt;&gt;.Inner&lt;&gt;</c> are supported.
    /// </summary>
    private static bool TryConstructOpenGenericConverterType(INamedTypeSymbol converterType, ITypeSymbol typeToConvert, [NotNullWhen(true)] out INamedTypeSymbol? constructedConverterType)
    {
        constructedConverterType = null;
        if (typeToConvert is not INamedTypeSymbol namedTypeToConvert)
        {
            return false;
        }

        var typeArguments = GetAllTypeArguments(namedTypeToConvert);
        if (typeArguments.Length == 0 || typeArguments.Any(static argument => argument.TypeKind == TypeKind.TypeParameter))
        {
            return false;
        }

        if (GetAllTypeParameters(converterType).Length != typeArguments.Length)
        {
            return false;
        }

        return TryConstructGenericType(converterType, typeArguments, out constructedConverterType);
    }

    /// <summary>
    /// Constructs a closed generic type from its definition and the type arguments of the whole nesting hierarchy,
    /// outermost first.
    /// </summary>
    private static bool TryConstructGenericType(INamedTypeSymbol definition, IReadOnlyList<ITypeSymbol> typeArguments, [NotNullWhen(true)] out INamedTypeSymbol? constructedType)
    {
        constructedType = null;

        var definitions = new List<INamedTypeSymbol>();
        for (var current = definition.OriginalDefinition; current is not null; current = current.ContainingType)
        {
            definitions.Add(current);
        }

        definitions.Reverse();

        INamedTypeSymbol? constructed = null;
        var index = 0;
        foreach (var currentDefinition in definitions)
        {
            var arity = currentDefinition.TypeParameters.Length;
            var candidate = constructed is null
                ? currentDefinition
                : constructed.GetTypeMembers(currentDefinition.Name, arity).FirstOrDefault();
            if (candidate is null)
            {
                return false;
            }

            if (arity > 0)
            {
                if (index + arity > typeArguments.Count)
                {
                    return false;
                }

                candidate = candidate.Construct(typeArguments.Skip(index).Take(arity).ToArray());
                index += arity;
            }

            constructed = candidate;
        }

        if (constructed is null || index != typeArguments.Count)
        {
            return false;
        }

        constructedType = constructed;
        return true;
    }

    /// <summary>Gets the type parameters of a type definition, including the ones of its containing types, outermost first.</summary>
    private static ImmutableArray<ITypeParameterSymbol> GetAllTypeParameters(INamedTypeSymbol definition)
    {
        var builder = ImmutableArray.CreateBuilder<ITypeParameterSymbol>();
        AppendTypeParameters(definition.OriginalDefinition, builder);
        return builder.ToImmutable();

        static void AppendTypeParameters(INamedTypeSymbol definition, ImmutableArray<ITypeParameterSymbol>.Builder builder)
        {
            if (definition.ContainingType is not null)
            {
                AppendTypeParameters(definition.ContainingType, builder);
            }

            builder.AddRange(definition.TypeParameters);
        }
    }

    /// <summary>
    /// Closes an open generic derived type over the type arguments of <paramref name="baseType"/> by unifying the base
    /// type specification declared by the derived type with the closed base type. Types that are already closed are
    /// returned unchanged.
    /// </summary>
    private static bool TryResolveDerivedType(ITypeSymbol baseType, ITypeSymbol derivedType, [NotNullWhen(true)] out ITypeSymbol? resolvedDerivedType)
    {
        if (derivedType is not INamedTypeSymbol namedDerivedType || GetAllTypeParameters(namedDerivedType).Length == 0 || !IsOpenGenericType(namedDerivedType))
        {
            resolvedDerivedType = derivedType;
            return true;
        }

        var definition = namedDerivedType.OriginalDefinition;
        var parameters = GetAllTypeParameters(definition);

        INamedTypeSymbol? resolved = null;
        foreach (var candidate in EnumerateBaseTypes(definition))
        {
            var substitution = new Dictionary<ITypeParameterSymbol, ITypeSymbol>(SymbolEqualityComparer.Default);
            if (!TryUnify(candidate, baseType, substitution))
            {
                continue;
            }

            var arguments = new ITypeSymbol[parameters.Length];
            var isComplete = true;
            for (var i = 0; i < parameters.Length; i++)
            {
                if (!substitution.TryGetValue(parameters[i], out var argument))
                {
                    isComplete = false;
                    break;
                }

                arguments[i] = argument;
            }

            if (!isComplete || !SatisfiesConstraints(parameters, arguments) || !TryConstructGenericType(definition, arguments, out var constructed))
            {
                continue;
            }

            if (!IsAssignableTo(constructed, baseType))
            {
                continue;
            }

            if (resolved is null)
            {
                resolved = constructed;
            }
            else if (!SymbolEqualityComparer.Default.Equals(resolved, constructed))
            {
                // The derived type can be constructed in more than one way for that base type.
                resolvedDerivedType = null;
                return false;
            }
        }

        resolvedDerivedType = resolved;
        return resolved is not null;
    }

    private static bool IsOpenGenericType(INamedTypeSymbol type)
        => type.IsUnboundGenericType || GetAllTypeArguments(type).Any(static argument => argument.TypeKind == TypeKind.TypeParameter);

    private static IEnumerable<ITypeSymbol> EnumerateBaseTypes(INamedTypeSymbol type)
    {
        for (var current = type.BaseType; current is not null; current = current.BaseType)
        {
            yield return current;
        }

        foreach (var interfaceType in type.AllInterfaces)
        {
            yield return interfaceType;
        }
    }

    /// <summary>
    /// Matches a base type specification declared by an open generic derived type (such as <c>Base&lt;List&lt;T&gt;&gt;</c>)
    /// against the closed base type, recording the type arguments bound to each type parameter.
    /// </summary>
    private static bool TryUnify(ITypeSymbol specification, ITypeSymbol actual, Dictionary<ITypeParameterSymbol, ITypeSymbol> substitution)
    {
        if (specification is ITypeParameterSymbol parameter)
        {
            if (substitution.TryGetValue(parameter, out var existing))
            {
                return SymbolEqualityComparer.Default.Equals(existing, actual);
            }

            substitution[parameter] = actual;
            return true;
        }

        if (specification is IArrayTypeSymbol specificationArray)
        {
            return actual is IArrayTypeSymbol actualArray
                && specificationArray.Rank == actualArray.Rank
                && TryUnify(specificationArray.ElementType, actualArray.ElementType, substitution);
        }

        if (specification is INamedTypeSymbol { IsGenericType: true } specificationType)
        {
            if (actual is not INamedTypeSymbol { IsGenericType: true } actualType ||
                !SymbolEqualityComparer.Default.Equals(specificationType.OriginalDefinition, actualType.OriginalDefinition))
            {
                return false;
            }

            var specificationArguments = GetAllTypeArguments(specificationType);
            var actualArguments = GetAllTypeArguments(actualType);
            if (specificationArguments.Length != actualArguments.Length)
            {
                return false;
            }

            for (var i = 0; i < specificationArguments.Length; i++)
            {
                if (!TryUnify(specificationArguments[i], actualArguments[i], substitution))
                {
                    return false;
                }
            }

            return true;
        }

        return SymbolEqualityComparer.Default.Equals(specification, actual);
    }

    private static bool SatisfiesConstraints(ImmutableArray<ITypeParameterSymbol> parameters, ITypeSymbol[] arguments)
    {
        for (var i = 0; i < parameters.Length; i++)
        {
            var parameter = parameters[i];
            var argument = arguments[i];

            if (parameter.HasReferenceTypeConstraint && !argument.IsReferenceType)
            {
                return false;
            }

            if ((parameter.HasValueTypeConstraint || parameter.HasUnmanagedTypeConstraint) && !argument.IsValueType)
            {
                return false;
            }

            if (parameter.HasConstructorConstraint && argument is INamedTypeSymbol { TypeKind: TypeKind.Class } argumentClass &&
                !argumentClass.InstanceConstructors.Any(static constructor => constructor.Parameters.Length == 0 && constructor.DeclaredAccessibility == Accessibility.Public))
            {
                return false;
            }

            foreach (var constraintType in parameter.ConstraintTypes)
            {
                var substituted = SubstituteTypeParameters(constraintType, parameters, arguments);
                if (!IsAssignableTo(argument, substituted))
                {
                    return false;
                }
            }
        }

        return true;
    }

    private static ITypeSymbol SubstituteTypeParameters(ITypeSymbol type, ImmutableArray<ITypeParameterSymbol> parameters, ITypeSymbol[] arguments)
    {
        if (type is ITypeParameterSymbol parameter)
        {
            for (var i = 0; i < parameters.Length; i++)
            {
                if (SymbolEqualityComparer.Default.Equals(parameters[i], parameter))
                {
                    return arguments[i];
                }
            }
        }

        return type;
    }

    /// <summary>Gets the type arguments of a type, including the ones of its containing types, outermost first.</summary>
    private static ImmutableArray<ITypeSymbol> GetAllTypeArguments(INamedTypeSymbol type)
    {
        if (type.ContainingType is null)
        {
            return type.TypeArguments;
        }

        var builder = ImmutableArray.CreateBuilder<ITypeSymbol>();
        AppendTypeArguments(type, builder);
        return builder.ToImmutable();

        static void AppendTypeArguments(INamedTypeSymbol type, ImmutableArray<ITypeSymbol>.Builder builder)
        {
            if (type.ContainingType is not null)
            {
                AppendTypeArguments(type.ContainingType, builder);
            }

            builder.AddRange(type.TypeArguments);
        }
    }

    /// <summary>
    /// Checks whether the given type is handled by a converter — either via a [YamlConverter] attribute
    /// on the type itself, or via a context-level YamlConverter&lt;T&gt; registration.
    /// Nullable&lt;T&gt; value types are unwrapped before checking.
    /// </summary>
    private static bool IsTypeHandledByConverter(
        ITypeSymbol typeToCheck,
        ImmutableArray<ITypeSymbol> converterTypes,
        Compilation compilation)
    {
        // Unwrap Nullable<T> for value types.
        var unwrappedType = typeToCheck;
        if (typeToCheck is INamedTypeSymbol nullable && nullable.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T)
        {
            unwrappedType = nullable.TypeArguments[0];
        }

        // Check if the type itself has [YamlConverter(typeof(...))].
        if (HasYamlConverterAttribute(unwrappedType))
        {
            return true;
        }

        // Check if a context-level converter handles this type.
        if (!converterTypes.IsDefaultOrEmpty)
        {
            var yamlConverterOfT = compilation.GetTypeByMetadataName("Meziantou.Framework.Yaml.Serialization.YamlConverter`1");
            if (yamlConverterOfT is not null)
            {
                foreach (var converterType in converterTypes)
                {
                    // Walk up the base type chain looking for YamlConverter<T>.
                    for (var current = converterType as INamedTypeSymbol; current is not null; current = current.BaseType)
                    {
                        if (current.IsGenericType &&
                            SymbolEqualityComparer.Default.Equals(current.OriginalDefinition, yamlConverterOfT) &&
                            current.TypeArguments.Length == 1 &&
                            SymbolEqualityComparer.Default.Equals(current.TypeArguments[0], unwrappedType))
                        {
                            return true;
                        }
                    }
                }
            }
        }

        return false;
    }

    private static bool IsUntypedObject(ITypeSymbol type)
        => type.SpecialType == SpecialType.System_Object;

    private static ImmutableArray<ISymbol> GetSerializableMembers(INamedTypeSymbol type)
    {
        // Arrays/collections/dictionaries are handled by dedicated generated code paths, not as object graphs.
        if (TryGetArrayElementType(type, out _) ||
            TryGetSequenceElementType(type, out _, out _) ||
            TryGetDictionaryTypes(type, out _, out _, out _) ||
            TryGetKeyValuePairTypes(type, out _, out _))
        {
            return ImmutableArray<ISymbol>.Empty;
        }

        // Include base members for parity with reflection/STJ behavior, but prefer the most-derived
        // member when a derived type hides/overrides a base member with the same CLR name.
        // Members are listed from the base-most type down to the type itself and, within a type, properties come before
        // fields, each in declaration order. Reflection cannot observe how properties and fields are interleaved in the
        // source, so this is the order the reflection-based contract uses too.
        var declaredIgnoreCondition = GetDeclaredIgnoreCondition(type);
        var members = new List<ISymbol?>();
        var indexByClrName = new Dictionary<string, int>(StringComparer.Ordinal);

        var hierarchy = new Stack<INamedTypeSymbol>();
        for (INamedTypeSymbol? current = type; current is not null; current = current.BaseType)
        {
            if (current.SpecialType == SpecialType.System_Object)
            {
                break;
            }

            hierarchy.Push(current);
        }

        while (hierarchy.Count != 0)
        {
            var current = hierarchy.Pop();
            var currentMembers = current.GetMembers();
            foreach (var member in currentMembers.Where(static member => member is IPropertySymbol).Concat(currentMembers.Where(static member => member is IFieldSymbol)))
            {
                // Only instance members are serialized; static properties, static fields and constants are not part of the contract.
                if (member.IsStatic)
                {
                    continue;
                }

                if (member is IPropertySymbol property)
                {
                    if (property.IsIndexer)
                    {
                        continue;
                    }

                    var hasIncludeAttr = HasAttribute(property, "Meziantou.Framework.Yaml.Serialization.YamlIncludeAttribute");
                    var canRead = property.GetMethod is { DeclaredAccessibility: Accessibility.Public } || hasIncludeAttr;
                    if (!canRead)
                    {
                        continue;
                    }

                    AddSerializableMember(members, indexByClrName, property, IsIgnoredAlways(property, declaredIgnoreCondition));

                    continue;
                }

                if (member is IFieldSymbol field)
                {
                    var hasIncludeAttr = HasAttribute(field, "Meziantou.Framework.Yaml.Serialization.YamlIncludeAttribute");
                    var canRead = field.DeclaredAccessibility == Accessibility.Public || hasIncludeAttr;
                    if (!canRead)
                    {
                        continue;
                    }

                    AddSerializableMember(members, indexByClrName, field, IsIgnoredAlways(field, declaredIgnoreCondition));
                }
            }
        }

        return members.Where(static member => member is not null).Select(static member => member!).ToImmutableArray();
    }

    private static void AddSerializableMember(List<ISymbol?> members, Dictionary<string, int> indexByClrName, ISymbol member, bool isIgnored)
    {
        // An ignored member still hides the member it overrides or hides, so neither is part of the contract.
        var entry = isIgnored ? null : member;
        if (indexByClrName.TryGetValue(member.Name, out var existingIndex))
        {
            members[existingIndex] = entry;
        }
        else
        {
            indexByClrName.Add(member.Name, members.Count);
            members.Add(entry);
        }
    }

    private static ImmutableArray<ISymbol> GetExtensionDataMembers(INamedTypeSymbol type)
    {
        var matches = new List<ISymbol>();

        var hierarchy = new Stack<INamedTypeSymbol>();
        for (INamedTypeSymbol? current = type; current is not null; current = current.BaseType)
        {
            if (current.SpecialType == SpecialType.System_Object)
            {
                break;
            }

            hierarchy.Push(current);
        }

        while (hierarchy.Count != 0)
        {
            var current = hierarchy.Pop();
            foreach (var member in current.GetMembers())
            {
                if (member is not IPropertySymbol and not IFieldSymbol || member.IsStatic)
                {
                    continue;
                }

                if (HasAttribute(member, "Meziantou.Framework.Yaml.Serialization.YamlExtensionDataAttribute"))
                {
                    // A member overriding or hiding the extension data member of a base type replaces it.
                    var existingIndex = matches.FindIndex(existing => string.Equals(existing.Name, member.Name, StringComparison.Ordinal));
                    if (existingIndex >= 0)
                    {
                        matches[existingIndex] = member;
                    }
                    else
                    {
                        matches.Add(member);
                    }
                }
            }
        }

        return matches.ToImmutableArray();
    }

    private static bool IsSupportedExtensionDataMemberType(ITypeSymbol type)
    {
        if (string.Equals(type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat), "global::Meziantou.Framework.Yaml.Model.YamlMapping", StringComparison.Ordinal))
        {
            return true;
        }

        if (TryGetDictionaryValueType(type, out var valueType, out _))
        {
            if (valueType.SpecialType == SpecialType.System_Object)
            {
                return true;
            }

            if (IsYamlNodeType(valueType))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsYamlNodeType(ITypeSymbol type)
    {
        for (var current = type; current is not null; current = (current as INamedTypeSymbol)?.BaseType)
        {
            if (string.Equals(current.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat), "global::Meziantou.Framework.Yaml.Model.YamlNode", StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static ExtensionDataMemberModel? TryCreateExtensionDataMemberModel(INamedTypeSymbol type, UnsafeAccessorRegistry accessors)
    {
        var extensionDataMembers = GetExtensionDataMembers(type);
        if (extensionDataMembers.Length != 1)
        {
            return null;
        }

        var symbol = extensionDataMembers[0];
        var memberType = GetMemberType(symbol);
        if (memberType is null || !IsSupportedExtensionDataMemberType(memberType))
        {
            return null;
        }

        ExtensionDataKind kind;
        ITypeSymbol? dictionaryValueType = null;
        if (string.Equals(memberType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat), "global::Meziantou.Framework.Yaml.Model.YamlMapping", StringComparison.Ordinal))
        {
            kind = ExtensionDataKind.Mapping;
        }
        else
        {
            _ = TryGetDictionaryValueType(memberType, out dictionaryValueType, out var isReadOnly);
            kind = isReadOnly ? ExtensionDataKind.ReadOnlyDictionary : ExtensionDataKind.Dictionary;
        }

        var (accessExpression, assign, usesAccessorForWrite) = CreateMemberAccessExpressions(symbol, type, accessors);
        Func<string, string>? assignExpression = null;
        var canAssign = false;
        var isInitOnly = false;

        if (symbol is IPropertySymbol property)
        {
            // An init-only setter reached through an accessor behaves like a regular setter: it can be called after construction.
            isInitOnly = IsInitOnlyProperty(property) && !usesAccessorForWrite;
            if (property.SetMethod is not null)
            {
                canAssign = !isInitOnly;
                if (canAssign)
                {
                    assignExpression = assign;
                }
            }
        }
        else if (symbol is IFieldSymbol field)
        {
            canAssign = !field.IsConst && !field.IsReadOnly;
            if (canAssign)
            {
                assignExpression = assign;
            }
        }

        return new ExtensionDataMemberModel(symbol, memberType, kind, dictionaryValueType, accessExpression, assignExpression, canAssign, isInitOnly);
    }

    private static bool IsWritableMember(ISymbol member)
    {
        if (member is IPropertySymbol property)
        {
            if (property.SetMethod is null)
            {
                return false;
            }

            var hasIncludeAttr = HasAttribute(property, "Meziantou.Framework.Yaml.Serialization.YamlIncludeAttribute");
            return property.SetMethod.DeclaredAccessibility == Accessibility.Public || hasIncludeAttr;
        }

        if (member is IFieldSymbol field)
        {
            if (field.IsConst || field.IsReadOnly)
            {
                return false;
            }

            return true;
        }

        return false;
    }

    private static bool IsInitOnlyProperty(IPropertySymbol property)
    {
        if (property.SetMethod is not { } setMethod)
        {
            return false;
        }

        foreach (var modifier in setMethod.ReturnTypeCustomModifiers)
        {
            if (!modifier.IsOptional &&
                string.Equals(modifier.Modifier.ToDisplayString(), "System.Runtime.CompilerServices.IsExternalInit", StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static ITypeSymbol? GetMemberType(ISymbol member)
        => member switch
        {
            IPropertySymbol p => p.Type,
            IFieldSymbol f => f.Type,
            _ => null,
        };

    private static bool IsNonNullableReferenceType(ITypeSymbol type)
        => type.IsReferenceType && type.NullableAnnotation == NullableAnnotation.NotAnnotated;

    private static string GetDefaultMemberAssignmentExpression(MemberModel member)
        => IsNonNullableReferenceType(member.Type) ? "default!" : "default";

    private static string GetNonNullableValueExpression(ITypeSymbol type, string expression)
        => IsNonNullableReferenceType(type) ? expression + "!" : expression;

    private static string GetGeneratedTypeName(ITypeSymbol type)
        => type.ToDisplayString(FullyQualifiedNullableFormat);

    private const int IgnoreNever = 0;
    private const int IgnoreWhenWritingNull = 1;
    private const int IgnoreWhenWritingDefault = 2;
    private const int IgnoreAlways = 3;
    private const int IgnoreWhenWriting = 4;
    private const int IgnoreWhenReading = 5;

    private const int DiscriminatorStyleTag = 0;
    private const int DiscriminatorStyleProperty = 1;
    private const int DiscriminatorStyleBoth = 2;
    private const int UnknownDerivedTypeHandlingFail = 0;
    private const int UnknownDerivedTypeHandlingFallBackToBase = 1;

    private static bool IsIgnoredAlways(ISymbol symbol, int? declaringTypeCondition)
        => (TryGetIgnoreCondition(symbol, out var condition) ? condition : declaringTypeCondition) == IgnoreAlways;

    private static int? GetDeclaredIgnoreCondition(INamedTypeSymbol? type)
    {
        for (var current = type; current is not null; current = current.BaseType)
        {
            if (TryGetIgnoreCondition(current, out var condition))
            {
                return condition;
            }
        }

        return null;
    }

    private static bool TryGetIgnoreCondition(ISymbol symbol, out int condition)
    {
        condition = IgnoreNever;

        foreach (var attribute in GetMemberAttributes(symbol))
        {
            if (attribute.AttributeClass is null)
            {
                continue;
            }

            var attributeName = attribute.AttributeClass.ToDisplayString();
            if (!string.Equals(attributeName, "Meziantou.Framework.Yaml.Serialization.YamlIgnoreAttribute", StringComparison.Ordinal))
            {
                continue;
            }

            condition = IgnoreAlways;

            foreach (var pair in attribute.NamedArguments)
            {
                if (!string.Equals(pair.Key, "Condition", StringComparison.Ordinal))
                {
                    continue;
                }

                if (pair.Value.Value is not null)
                {
                    condition = Convert.ToInt32(pair.Value.Value, System.Globalization.CultureInfo.InvariantCulture);
                    break;
                }
            }

            return true;
        }

        return false;
    }

    /// <summary>
    /// Gets the attributes of <paramref name="symbol"/>. An overriding property also exposes the inheritable attributes
    /// declared on the properties it overrides, unless it declares an attribute of the same type, matching
    /// <see cref="Attribute.GetCustomAttributes(System.Reflection.MemberInfo, bool)"/> used by reflection-based serialization.
    /// </summary>
    private static IEnumerable<AttributeData> GetMemberAttributes(ISymbol symbol)
    {
        var attributes = symbol.GetAttributes();
        if (symbol is not IPropertySymbol { OverriddenProperty: not null } property)
        {
            return attributes;
        }

        var result = new List<AttributeData>(attributes);
        var declaredAttributeTypes = new HashSet<ITypeSymbol>(SymbolEqualityComparer.Default);
        foreach (var attribute in attributes)
        {
            if (attribute.AttributeClass is not null)
            {
                declaredAttributeTypes.Add(attribute.AttributeClass);
            }
        }

        for (var overridden = property.OverriddenProperty; overridden is not null; overridden = overridden.OverriddenProperty)
        {
            var overriddenAttributes = overridden.GetAttributes();
            foreach (var attribute in overriddenAttributes)
            {
                if (attribute.AttributeClass is null || declaredAttributeTypes.Contains(attribute.AttributeClass) || !IsInheritedAttribute(attribute.AttributeClass))
                {
                    continue;
                }

                result.Add(attribute);
            }

            foreach (var attribute in overriddenAttributes)
            {
                if (attribute.AttributeClass is not null)
                {
                    declaredAttributeTypes.Add(attribute.AttributeClass);
                }
            }
        }

        return result;
    }

    private static bool IsInheritedAttribute(INamedTypeSymbol attributeClass)
    {
        for (var current = attributeClass; current is not null; current = current.BaseType)
        {
            foreach (var attribute in current.GetAttributes())
            {
                if (!string.Equals(attribute.AttributeClass?.ToDisplayString(), "System.AttributeUsageAttribute", StringComparison.Ordinal))
                {
                    continue;
                }

                foreach (var pair in attribute.NamedArguments)
                {
                    if (string.Equals(pair.Key, "Inherited", StringComparison.Ordinal) && pair.Value.Value is bool inherited)
                    {
                        return inherited;
                    }
                }

                return true;
            }
        }

        return true;
    }

    private static bool HasAttribute(ISymbol symbol, string metadataName)
    {
        foreach (var attribute in GetMemberAttributes(symbol))
        {
            if (attribute.AttributeClass is null)
            {
                continue;
            }

            var attributeName = attribute.AttributeClass.ToDisplayString();
            if (string.Equals(attributeName, metadataName, StringComparison.Ordinal) ||
                string.Equals(attributeName, "global::" + metadataName, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static bool DerivesFromYamlSerializerContext(INamedTypeSymbol symbol)
    {
        for (var current = symbol; current is not null; current = current.BaseType)
        {
            if (string.Equals(current.ToDisplayString(), "Meziantou.Framework.Yaml.Serialization.YamlSerializerContext", StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryCreateSerializableTypeModel(AttributeData attribute, out SerializableTypeModel model)
    {
        model = null!;

        if (!IsYamlSerializableAttribute(attribute))
        {
            return false;
        }

        if (attribute.ConstructorArguments.Length != 1)
        {
            return false;
        }

        var argument = attribute.ConstructorArguments[0];
        if (argument.Kind != TypedConstantKind.Type || argument.Value is not ITypeSymbol typeSymbol)
        {
            return false;
        }

        model = new SerializableTypeModel(typeSymbol, GetTypeInfoPropertyNameOverride(attribute), attribute.ApplicationSyntaxReference?.GetSyntax().GetLocation());
        return true;
    }

    private static bool TryCreateDerivedTypeMappingModel(AttributeData attribute, out DerivedTypeMappingModel model)
    {
        model = null!;

        if (!string.Equals(attribute.AttributeClass?.ToDisplayString(), "Meziantou.Framework.Yaml.Serialization.YamlDerivedTypeMappingAttribute", StringComparison.Ordinal))
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
                int i => i.ToString(System.Globalization.CultureInfo.InvariantCulture),
                _ => null,
            };
        }

        string? tag = null;
        foreach (var pair in attribute.NamedArguments)
        {
            if (string.Equals(pair.Key, "Tag", StringComparison.Ordinal) && pair.Value.Value is string tagValue)
            {
                tag = tagValue;
            }
        }

        model = new DerivedTypeMappingModel(
            baseType,
            derivedType,
            discriminator,
            tag,
            attribute.ApplicationSyntaxReference?.GetSyntax().GetLocation());
        return true;
    }

    private static bool IsYamlSerializableAttribute(AttributeData attribute)
        => string.Equals(attribute.AttributeClass?.ToDisplayString(), "Meziantou.Framework.Yaml.Serialization.YamlSerializableAttribute", StringComparison.Ordinal);

    private static bool IsYamlSourceGenerationOptionsAttribute(AttributeData attribute)
        => string.Equals(attribute.AttributeClass?.ToDisplayString(), "Meziantou.Framework.Yaml.Serialization.YamlSourceGenerationOptionsAttribute", StringComparison.Ordinal);

    private static ImmutableArray<DerivedTypeMappingModel> ValidateDerivedTypeMappings(ContextModel model, ImmutableArray<Diagnostic>.Builder diagnostics)
    {
        var builder = ImmutableArray.CreateBuilder<DerivedTypeMappingModel>(model.DerivedTypeMappings.Length);
        var warnedBaseTypes = new HashSet<ITypeSymbol>(SymbolEqualityComparer.Default);
        var registrationsByBaseType = new Dictionary<ITypeSymbol, DerivedTypeRegistrationSet>(SymbolEqualityComparer.Default);

        for (var i = 0; i < model.DerivedTypeMappings.Length; i++)
        {
            var mapping = model.DerivedTypeMappings[i];
            var baseType = mapping.BaseType;
            var derivedType = mapping.DerivedType;

            if (!TryResolveDerivedType(baseType, derivedType, out var resolvedDerivedType) || !IsAssignableTo(resolvedDerivedType, baseType))
            {
                diagnostics.Add(Diagnostic.Create(
                    InvalidDerivedTypeMapping,
                    mapping.Location ?? model.ContextSymbol.Locations.FirstOrDefault(),
                    derivedType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat),
                    baseType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)));
                continue;
            }

            if (!registrationsByBaseType.TryGetValue(baseType, out var registrations))
            {
                registrations = new DerivedTypeRegistrationSet();
                registrationsByBaseType.Add(baseType, registrations);
            }

            if (registrations.TryAdd(resolvedDerivedType, mapping.Discriminator, mapping.Tag) is { } conflict)
            {
                diagnostics.Add(Diagnostic.Create(
                    DuplicateDerivedTypeRegistration,
                    mapping.Location ?? model.ContextSymbol.Locations.FirstOrDefault(),
                    baseType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat),
                    conflict));
                continue;
            }

            if (warnedBaseTypes.Add(baseType) && !HasYamlPolymorphicConfiguration(baseType))
            {
                diagnostics.Add(Diagnostic.Create(
                    MissingYamlPolymorphicOnDerivedTypeMappingBase,
                    mapping.Location ?? model.ContextSymbol.Locations.FirstOrDefault(),
                    baseType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)));
            }

            builder.Add(mapping);
        }

        return builder.ToImmutable();
    }

    private static bool HasYamlPolymorphicConfiguration(ITypeSymbol typeSymbol)
    {
        foreach (var attribute in typeSymbol.GetAttributes())
        {
            var attributeName = attribute.AttributeClass?.ToDisplayString();
            if (string.Equals(attributeName, "Meziantou.Framework.Yaml.Serialization.YamlPolymorphicAttribute", StringComparison.Ordinal) ||
                string.Equals(attributeName, "Meziantou.Framework.Yaml.Serialization.YamlDerivedTypeAttribute", StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsAssignableTo(ITypeSymbol typeSymbol, ITypeSymbol baseType)
    {
        if (SymbolEqualityComparer.Default.Equals(typeSymbol, baseType))
        {
            return true;
        }

        if (typeSymbol is not INamedTypeSymbol namedType)
        {
            return false;
        }

        for (var current = namedType.BaseType; current is not null; current = current.BaseType)
        {
            if (SymbolEqualityComparer.Default.Equals(current, baseType))
            {
                return true;
            }
        }

        foreach (var implementedInterface in namedType.AllInterfaces)
        {
            if (SymbolEqualityComparer.Default.Equals(implementedInterface, baseType))
            {
                return true;
            }
        }

        return false;
    }

    private static string? GetTypeInfoPropertyNameOverride(AttributeData attribute)
    {
        foreach (var namedArgument in attribute.NamedArguments)
        {
            if (string.Equals(namedArgument.Key, "TypeInfoPropertyName", StringComparison.Ordinal) &&
                namedArgument.Value.Value is string typeInfoPropertyName)
            {
                return typeInfoPropertyName;
            }
        }

        return null;
    }

    private static IEnumerable<ITypeSymbol> GetPolymorphicDerivedTypes(
        INamedTypeSymbol baseType,
        ImmutableArray<DerivedTypeMappingModel> contextMappings,
        SourceGenerationOptionsModel sourceGenerationOptions)
    {
        if (TryGetPolymorphismInfo(baseType, contextMappings, sourceGenerationOptions, out var info))
        {
            for (var i = 0; i < info.DerivedTypes.Length; i++)
            {
                yield return info.DerivedTypes[i].DerivedType;
            }
        }
    }

    private static bool TryGetPolymorphismInfo(
        INamedTypeSymbol baseType,
        ImmutableArray<DerivedTypeMappingModel> contextMappings,
        SourceGenerationOptionsModel sourceGenerationOptions,
        out PolymorphismInfoModel info)
    {
        string? discriminatorPropertyNameOverride = null;
        int? discriminatorStyleOverrideValue = null;
        int? unknownOverrideValue = null;
        int? yamlUnknownOverrideValue = null;
        bool? inferClosedTypePolymorphismOverride = null;

        var derivedTypes = ImmutableArray.CreateBuilder<DerivedTypeInfoModel>();
        var seenDerived = new HashSet<ITypeSymbol>(SymbolEqualityComparer.Default);
        var seenDiscriminators = new HashSet<string>(StringComparer.Ordinal);
        var seenTags = new HashSet<string>(StringComparer.Ordinal);

        foreach (var attribute in baseType.GetAttributes())
        {
            var attributeName = attribute.AttributeClass?.ToDisplayString();
            if (string.Equals(attributeName, "Meziantou.Framework.Yaml.Serialization.YamlPolymorphicAttribute", StringComparison.Ordinal))
            {
                foreach (var pair in attribute.NamedArguments)
                {
                    if (string.Equals(pair.Key, "TypeDiscriminatorPropertyName", StringComparison.Ordinal) && pair.Value.Value is string name)
                    {
                        discriminatorPropertyNameOverride = name;
                    }
                    else if (string.Equals(pair.Key, "DiscriminatorStyle", StringComparison.Ordinal) && pair.Value.Value is int styleValue)
                    {
                        discriminatorStyleOverrideValue = styleValue;
                    }
                    else if (string.Equals(pair.Key, "UnknownDerivedTypeHandling", StringComparison.Ordinal) && pair.Value.Value is int unknownValue)
                    {
                        yamlUnknownOverrideValue = unknownValue;
                    }
                    else if (string.Equals(pair.Key, "InferClosedTypePolymorphism", StringComparison.Ordinal) && pair.Value.Value is bool inferValue)
                    {
                        inferClosedTypePolymorphismOverride = inferValue;
                    }
                }
            }
        }

        // -1 is YamlUnknownDerivedTypeHandling.Unspecified
        if (yamlUnknownOverrideValue is not null && yamlUnknownOverrideValue.Value != -1)
        {
            unknownOverrideValue = yamlUnknownOverrideValue;
        }

        // YamlDerivedTypeAttribute(Type derivedType) or YamlDerivedTypeAttribute(Type derivedType, string|int discriminator) { string? Tag }
        ITypeSymbol? defaultDerivedType = null;
        foreach (var attribute in baseType.GetAttributes())
        {
            if (!string.Equals(attribute.AttributeClass?.ToDisplayString(), "Meziantou.Framework.Yaml.Serialization.YamlDerivedTypeAttribute", StringComparison.Ordinal))
            {
                continue;
            }

            if (attribute.ConstructorArguments.Length < 1)
            {
                continue;
            }

            var derivedArg = attribute.ConstructorArguments[0];
            if (derivedArg.Kind != TypedConstantKind.Type || derivedArg.Value is not ITypeSymbol declaredDerivedType)
            {
                continue;
            }

            if (!TryResolveDerivedType(baseType, declaredDerivedType, out var derivedType))
            {
                continue;
            }

            string? discriminator = null;
            if (attribute.ConstructorArguments.Length >= 2)
            {
                discriminator = attribute.ConstructorArguments[1].Value switch
                {
                    string s => s,
                    int i => i.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    _ => null,
                };
            }

            string? tag = null;
            foreach (var pair in attribute.NamedArguments)
            {
                if (string.Equals(pair.Key, "Tag", StringComparison.Ordinal) && pair.Value.Value is string tagValue)
                {
                    tag = tagValue;
                }
            }

            if (seenDerived.Add(derivedType))
            {
                if (discriminator is null && tag is null)
                {
                    defaultDerivedType = derivedType;
                }

                derivedTypes.Add(new DerivedTypeInfoModel(derivedType, discriminator, tag));
                if (discriminator is not null)
                {
                    seenDiscriminators.Add(discriminator);
                }

                if (tag is not null)
                {
                    seenTags.Add(tag);
                }
            }
        }

        for (var i = 0; i < contextMappings.Length; i++)
        {
            var mapping = contextMappings[i];
            if (!SymbolEqualityComparer.Default.Equals(mapping.BaseType, baseType))
            {
                continue;
            }

            if (!TryResolveDerivedType(baseType, mapping.DerivedType, out var mappedDerivedType))
            {
                continue;
            }

            var isDefaultMapping = mapping.Discriminator is null && mapping.Tag is null;
            if (!CanAddLowerPrecedenceMapping(
                mappedDerivedType,
                mapping.Discriminator,
                mapping.Tag,
                isDefaultMapping,
                defaultDerivedType,
                seenDerived,
                seenDiscriminators,
                seenTags))
            {
                continue;
            }

            if (isDefaultMapping)
            {
                defaultDerivedType ??= mappedDerivedType;
            }

            derivedTypes.Add(new DerivedTypeInfoModel(mappedDerivedType, mapping.Discriminator, mapping.Tag));
            if (mapping.Discriminator is not null)
            {
                seenDiscriminators.Add(mapping.Discriminator);
            }

            if (mapping.Tag is not null)
            {
                seenTags.Add(mapping.Tag);
            }
        }

        // A value set on the declaration overrides the source-generation option; the option applies when unset.
        // Explicit registrations replace inference, so the closed hierarchy is only enumerated when none was declared.
        if (derivedTypes.Count == 0 &&
            InfersClosedTypePolymorphism(baseType, inferClosedTypePolymorphismOverride, sourceGenerationOptions))
        {
            derivedTypes.AddRange(InferClosedTypeDerivedTypes(baseType, diagnostics: null));
        }

        if (derivedTypes.Count == 0 && discriminatorPropertyNameOverride is null && discriminatorStyleOverrideValue is null)
        {
            info = null!;
            return false;
        }

        info = new PolymorphismInfoModel(
            discriminatorPropertyNameOverride,
            discriminatorStyleOverrideValue,
            unknownOverrideValue,
            derivedTypes.ToImmutable(),
            defaultDerivedType);
        return true;
    }

    private static bool CanAddLowerPrecedenceMapping(
        ITypeSymbol derivedType,
        string? discriminator,
        string? tag,
        bool isDefaultMapping,
        ITypeSymbol? defaultDerivedType,
        HashSet<ITypeSymbol> seenDerived,
        HashSet<string> seenDiscriminators,
        HashSet<string> seenTags)
    {
        if (seenDerived.Contains(derivedType))
        {
            return false;
        }

        if (isDefaultMapping)
        {
            if (defaultDerivedType is not null)
            {
                return false;
            }
        }
        else if (discriminator is not null && seenDiscriminators.Contains(discriminator))
        {
            return false;
        }

        if (tag is not null && seenTags.Contains(tag))
        {
            return false;
        }

        return true;
    }

    private static ImmutableArray<string> CreateTypeInfoPropertyNames(ContextModel model, ImmutableArray<ITypeSymbol> types)
    {
        var names = ImmutableArray.CreateBuilder<string>(types.Length);
        var usedNames = new HashSet<string>(StringComparer.Ordinal);
        var requestedNames = new Dictionary<ITypeSymbol, string>(SymbolEqualityComparer.Default);

        for (var i = 0; i < model.SerializableTypes.Length; i++)
        {
            var requestedName = model.SerializableTypes[i].TypeInfoPropertyName;
            if (requestedName is null || string.IsNullOrWhiteSpace(requestedName))
            {
                continue;
            }

            if (!requestedNames.ContainsKey(model.SerializableTypes[i].TypeSymbol))
            {
                requestedNames.Add(model.SerializableTypes[i].TypeSymbol, requestedName);
            }
        }

        foreach (var member in model.ContextSymbol.GetMembers())
        {
            usedNames.Add(member.Name);
        }

        usedNames.Add("Default");
        usedNames.Add("Options");
        usedNames.Add("TypeInfo");
        usedNames.Add("GetTypeInfo");

        for (var i = 0; i < types.Length; i++)
        {
            var baseName = requestedNames.TryGetValue(types[i], out var requestedName)
                ? SanitizeTypeInfoPropertyName(requestedName)
                : SanitizeTypeInfoPropertyName(BuildTypeInfoPropertyBaseName(types[i]));
            if (string.IsNullOrEmpty(baseName))
            {
                baseName = "TypeInfo";
            }

            var candidate = baseName;
            var suffix = 1;
            while (!SyntaxFacts.IsValidIdentifier(candidate) || usedNames.Contains(candidate))
            {
                candidate = baseName + suffix.ToString(CultureInfo.InvariantCulture);
                suffix++;
            }

            usedNames.Add(candidate);
            names.Add(candidate);
        }

        return names.ToImmutable();
    }

    private static string BuildTypeInfoPropertyBaseName(ITypeSymbol typeSymbol)
    {
        if (typeSymbol is IArrayTypeSymbol arrayType)
        {
            var elementName = BuildTypeInfoPropertyBaseName(arrayType.ElementType);
            return arrayType.Rank == 1
                ? elementName + "Array"
                : elementName + arrayType.Rank.ToString(CultureInfo.InvariantCulture) + "DArray";
        }

        if (typeSymbol is INamedTypeSymbol namedType)
        {
            if (namedType.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T && namedType.TypeArguments.Length == 1)
            {
                return "Nullable" + BuildTypeInfoPropertyBaseName(namedType.TypeArguments[0]);
            }

            var name = new StringBuilder();
            AppendContainingTypeNames(name, namedType.ContainingType);
            name.Append(StripGenericArity(namedType.Name));
            if (namedType.IsGenericType)
            {
                for (var i = 0; i < namedType.TypeArguments.Length; i++)
                {
                    name.Append(BuildTypeInfoPropertyBaseName(namedType.TypeArguments[i]));
                }
            }

            return name.ToString();
        }

        if (!string.IsNullOrEmpty(typeSymbol.Name))
        {
            return typeSymbol.Name;
        }

        return typeSymbol.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);
    }

    private static void AppendContainingTypeNames(StringBuilder builder, INamedTypeSymbol? containingType)
    {
        if (containingType is null)
        {
            return;
        }

        AppendContainingTypeNames(builder, containingType.ContainingType);
        builder.Append(StripGenericArity(containingType.Name));
    }

    private static string StripGenericArity(string typeName)
    {
        var tickIndex = typeName.IndexOf('`', StringComparison.Ordinal);
        return tickIndex >= 0 ? typeName.Substring(0, tickIndex) : typeName;
    }

    private static string SanitizeTypeInfoPropertyName(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return "TypeInfo";
        }

        var builder = new StringBuilder(text.Length + 1);
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (char.IsLetterOrDigit(c) || c == '_')
            {
                builder.Append(c);
            }
        }

        if (builder.Length == 0)
        {
            return "TypeInfo";
        }

        if (!char.IsLetter(builder[0]) && builder[0] != '_')
        {
            builder.Insert(0, '_');
        }

        var candidate = builder.ToString();
        if (SyntaxFacts.GetKeywordKind(candidate) != SyntaxKind.None)
        {
            candidate += "Value";
        }

        return candidate;
    }

    private static void ApplyYamlSourceGenerationOptionsAttribute(AttributeData attribute, SourceGenerationOptionsModel model)
    {
        foreach (var argument in attribute.NamedArguments)
        {
            switch (argument.Key)
            {
                case "WriteIndented":
                    model.WriteIndented = argument.Value.Value as bool?;
                    break;
                case "IndentSize":
                    model.IndentSize = argument.Value.Value as int?;
                    break;
                case "IndentBlockSequences":
                    model.IndentBlockSequences = argument.Value.Value as bool?;
                    break;
                case "StringStyle":
                    model.StringStyle = NormalizeEnumName(argument.Value.ToCSharpString());
                    break;
                case "PropertyNameCaseInsensitive":
                    model.PropertyNameCaseInsensitive = argument.Value.Value as bool?;
                    break;
                case "IncludeFields":
                    model.IncludeFields = argument.Value.Value as bool?;
                    break;
                case "IgnoreReadOnlyFields":
                    model.IgnoreReadOnlyFields = argument.Value.Value as bool?;
                    break;
                case "IgnoreReadOnlyProperties":
                    model.IgnoreReadOnlyProperties = argument.Value.Value as bool?;
                    break;
                case "RejectUnmatchedProperties":
                    model.RejectUnmatchedProperties = argument.Value.Value as bool?;
                    break;
                case "RespectRequiredConstructorParameters":
                    model.RespectRequiredConstructorParameters = argument.Value.Value as bool?;
                    break;
                case "RespectNullableAnnotations":
                    model.RespectNullableAnnotations = argument.Value.Value as bool?;
                    break;
                case "DefaultIgnoreCondition":
                    model.DefaultIgnoreCondition = NormalizeEnumName(argument.Value.ToCSharpString());
                    break;
                case "PropertyNamingPolicy":
                    model.PropertyNamingPolicy = NormalizeEnumName(argument.Value.ToCSharpString());
                    break;
                case "DictionaryKeyPolicy":
                    model.DictionaryKeyPolicy = NormalizeEnumName(argument.Value.ToCSharpString());
                    break;
                case "MappingOrder":
                    model.MappingOrder = NormalizeEnumName(argument.Value.ToCSharpString());
                    break;
                case "BlockSequenceMappingStyle":
                    model.BlockSequenceMappingStyle = NormalizeEnumName(argument.Value.ToCSharpString());
                    break;
                case "BlockSequenceSequenceStyle":
                    model.BlockSequenceSequenceStyle = NormalizeEnumName(argument.Value.ToCSharpString());
                    break;
                case "Schema":
                    model.Schema = NormalizeEnumName(argument.Value.ToCSharpString());
                    break;
                case "UseSchema":
                    model.UseSchema = argument.Value.Value as bool?;
                    break;
                case "UnmappedMemberHandling":
                    model.UnmappedMemberHandling = NormalizeEnumName(argument.Value.ToCSharpString());
                    break;
                case "PreferredObjectCreationHandling":
                    model.PreferredObjectCreationHandling = NormalizeEnumName(argument.Value.ToCSharpString());
                    break;
                case "DuplicateKeyHandling":
                    model.DuplicateKeyHandling = NormalizeEnumName(argument.Value.ToCSharpString());
                    break;
                case "UnsafeAllowDeserializeFromTagTypeName":
                    model.UnsafeAllowDeserializeFromTagTypeName = argument.Value.Value as bool?;
                    break;
                case "ReferenceHandling":
                    model.ReferenceHandling = NormalizeEnumName(argument.Value.ToCSharpString());
                    break;
                case "SourceName":
                    model.SourceName = argument.Value.Value as string;
                    break;
                case "PreferPlainStyle":
                    model.PreferPlainStyle = argument.Value.Value as bool?;
                    break;
                case "PreferQuotedForAmbiguousScalars":
                    model.PreferQuotedForAmbiguousScalars = argument.Value.Value as bool?;
                    break;
                case "InferClosedTypePolymorphism":
                    model.InferClosedTypePolymorphism = argument.Value.Value as bool?;
                    break;
                case "DiscriminatorStyle":
                    model.DiscriminatorStyle = NormalizeEnumName(argument.Value.ToCSharpString());
                    break;
                case "TypeDiscriminatorPropertyName":
                    model.TypeDiscriminatorPropertyName = argument.Value.Value as string;
                    break;
                case "UnknownDerivedTypeHandling":
                    model.UnknownDerivedTypeHandling = NormalizeEnumName(argument.Value.ToCSharpString());
                    break;
                case "Converters":
                    if (argument.Value.Kind == TypedConstantKind.Array)
                    {
                        var builder = ImmutableArray.CreateBuilder<ITypeSymbol>();
                        foreach (var item in argument.Value.Values)
                        {
                            if (item.Kind == TypedConstantKind.Type && item.Value is ITypeSymbol converterType)
                            {
                                builder.Add(converterType);
                            }
                        }

                        model.ConverterTypes = builder.ToImmutable();
                    }
                    break;
            }
        }
    }

    private static string? NormalizeEnumName(object? value)
    {
        if (value is null)
        {
            return null;
        }

        var text = value.ToString();
        if (string.IsNullOrEmpty(text))
        {
            return text;
        }

        var lastDot = text.LastIndexOf('.', StringComparison.Ordinal);
        return lastDot >= 0 && lastDot < text.Length - 1 ? text.Substring(lastDot + 1) : text;
    }

    private static string? TryGetUnmappedMemberHandlingOverride(INamedTypeSymbol typeSymbol)
    {
        foreach (var attribute in typeSymbol.GetAttributes())
        {
            if (!string.Equals(attribute.AttributeClass?.ToDisplayString(), "Meziantou.Framework.Yaml.Serialization.YamlUnmappedMemberHandlingAttribute", StringComparison.Ordinal))
            {
                continue;
            }

            if (attribute.ConstructorArguments.Length != 0)
            {
                return NormalizeEnumName(attribute.ConstructorArguments[0].ToCSharpString());
            }

            foreach (var argument in attribute.NamedArguments)
            {
                if (string.Equals(argument.Key, "UnmappedMemberHandling", StringComparison.Ordinal))
                {
                    return NormalizeEnumName(argument.Value.ToCSharpString());
                }
            }
        }

        return null;
    }

    private static string? GetObjectCreationHandling(ISymbol member)
    {
        foreach (var attribute in GetMemberAttributes(member))
        {
            if (!string.Equals(attribute.AttributeClass?.ToDisplayString(), "Meziantou.Framework.Yaml.Serialization.YamlObjectCreationHandlingAttribute", StringComparison.Ordinal))
            {
                continue;
            }

            if (attribute.ConstructorArguments.Length != 0)
            {
                return NormalizeEnumName(attribute.ConstructorArguments[0].ToCSharpString());
            }

            foreach (var argument in attribute.NamedArguments)
            {
                if (string.Equals(argument.Key, "Handling", StringComparison.Ordinal))
                {
                    return NormalizeEnumName(argument.Value.ToCSharpString());
                }
            }
        }

        return null;
    }

    private static (string? MappingStyle, string? SequenceStyle) GetBlockSequenceItemStyles(ISymbol member)
    {
        foreach (var attribute in GetMemberAttributes(member))
        {
            if (!string.Equals(attribute.AttributeClass?.ToDisplayString(), "Meziantou.Framework.Yaml.Serialization.YamlBlockSequenceItemStyleAttribute", StringComparison.Ordinal))
            {
                continue;
            }

            string? mappingStyle = null;
            string? sequenceStyle = null;
            if (attribute.ConstructorArguments.Length == 1)
            {
                mappingStyle = NormalizeEnumName(attribute.ConstructorArguments[0].ToCSharpString());
            }

            foreach (var argument in attribute.NamedArguments)
            {
                if (string.Equals(argument.Key, "MappingStyle", StringComparison.Ordinal))
                {
                    mappingStyle = NormalizeEnumName(argument.Value.ToCSharpString());
                }
                else if (string.Equals(argument.Key, "SequenceStyle", StringComparison.Ordinal))
                {
                    sequenceStyle = NormalizeEnumName(argument.Value.ToCSharpString());
                }
            }

            return (mappingStyle, sequenceStyle);
        }

        return (null, null);
    }

    private static string? GetStringStyle(ISymbol member)
    {
        foreach (var attribute in GetMemberAttributes(member))
        {
            if (!string.Equals(attribute.AttributeClass?.ToDisplayString(), "Meziantou.Framework.Yaml.Serialization.YamlStringStyleAttribute", StringComparison.Ordinal))
            {
                continue;
            }

            if (attribute.ConstructorArguments.Length == 1)
            {
                return NormalizeEnumName(attribute.ConstructorArguments[0].ToCSharpString());
            }
        }

        return null;
    }

    private static string? TryGetObjectCreationHandlingOverride(INamedTypeSymbol typeSymbol)
    {
        foreach (var attribute in typeSymbol.GetAttributes())
        {
            if (!string.Equals(attribute.AttributeClass?.ToDisplayString(), "Meziantou.Framework.Yaml.Serialization.YamlObjectCreationHandlingAttribute", StringComparison.Ordinal))
            {
                continue;
            }

            if (attribute.ConstructorArguments.Length != 0)
            {
                return NormalizeEnumName(attribute.ConstructorArguments[0].ToCSharpString());
            }

            foreach (var argument in attribute.NamedArguments)
            {
                if (string.Equals(argument.Key, "Handling", StringComparison.Ordinal))
                {
                    return NormalizeEnumName(argument.Value.ToCSharpString());
                }
            }
        }

        return null;
    }

    internal static string ToLiteral(string value)
        => "@\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";

    /// <summary>Escapes an identifier that is a C# keyword (e.g. <c>class</c>) so it can be referenced from generated code.</summary>
    internal static string EscapeIdentifier(string name)
        => SyntaxFacts.GetKeywordKind(name) != SyntaxKind.None ? "@" + name : name;

    /// <summary>
    /// Converts a member name into a suffix usable in a generated local name. An explicit interface implementation has a
    /// name such as <c>Namespace.IInterface.Member</c>, which is not a valid identifier.
    /// </summary>
    internal static string GetIdentifierSuffix(string name)
    {
        var builder = new StringBuilder(name.Length);
        foreach (var c in name)
        {
            builder.Append(char.IsLetterOrDigit(c) || c == '_' ? c : '_');
        }

        return builder.ToString();
    }

    private static int? GetDefaultIgnoreCondition(SourceGenerationOptionsModel options)
        => options.DefaultIgnoreCondition switch
        {
            "Never" => IgnoreNever,
            "WhenWritingNull" => IgnoreWhenWritingNull,
            "WhenWritingDefault" => IgnoreWhenWritingDefault,
            "Always" => IgnoreAlways,
            "WhenWriting" => IgnoreWhenWriting,
            "WhenReading" => IgnoreWhenReading,
            _ => null,
        };

    private static int? GetDiscriminatorStyle(SourceGenerationOptionsModel options)
        => options.DiscriminatorStyle switch
        {
            "Tag" => DiscriminatorStyleTag,
            "Property" => DiscriminatorStyleProperty,
            "Both" => DiscriminatorStyleBoth,
            _ => null,
        };

    private static int? GetUnknownDerivedTypeHandling(SourceGenerationOptionsModel options)
        => options.UnknownDerivedTypeHandling switch
        {
            "Fail" => UnknownDerivedTypeHandlingFail,
            "FallBackToBase" => UnknownDerivedTypeHandlingFallBackToBase,
            _ => null,
        };

    private static string? GetUnmappedMemberHandling(SourceGenerationOptionsModel options)
        => options.UnmappedMemberHandling is { Length: > 0 } value ? value : null;

    private static string? GetPreferredObjectCreationHandling(SourceGenerationOptionsModel options)
        => options.PreferredObjectCreationHandling is { Length: > 0 } value ? value : null;

    private static string? GetDuplicateKeyHandling(SourceGenerationOptionsModel options)
        => options.DuplicateKeyHandling is { Length: > 0 } value ? value : null;

    private static bool? GetSortedMappingOrder(SourceGenerationOptionsModel options)
        => options.MappingOrder switch
        {
            "Sorted" => true,
            "Declaration" => false,
            _ => null,
        };

    private static string GetPropertyNameComparerExpression(SourceGenerationOptionsModel options)
        => options.PropertyNameCaseInsensitive switch
        {
            true => "global::System.StringComparer.OrdinalIgnoreCase",
            false => "global::System.StringComparer.Ordinal",
            _ => "options.PropertyNameCaseInsensitive ? global::System.StringComparer.OrdinalIgnoreCase : global::System.StringComparer.Ordinal",
        };

    private static string GetPropertyNameComparisonExpression(SourceGenerationOptionsModel options)
        => options.PropertyNameCaseInsensitive switch
        {
            true => "global::System.StringComparison.OrdinalIgnoreCase",
            false => "global::System.StringComparison.Ordinal",
            _ => "options.PropertyNameCaseInsensitive ? global::System.StringComparison.OrdinalIgnoreCase : global::System.StringComparison.Ordinal",
        };

    private static string GetMergeEnabledExpression(SourceGenerationOptionsModel options)
        => options.Schema switch
        {
            "Core" or "Extended" => "true",
            "Failsafe" or "Json" => "false",
            _ => "options.Schema is global::Meziantou.Framework.Yaml.YamlSchemaKind.Core or global::Meziantou.Framework.Yaml.YamlSchemaKind.Extended",
        };

    private static string GetDiscriminatorPropertyNameExpression(PolymorphismInfoModel polymorphism, SourceGenerationOptionsModel options)
    {
        if (polymorphism.DiscriminatorPropertyNameOverride is not null)
            return ToLiteral(polymorphism.DiscriminatorPropertyNameOverride);

        if (options.TypeDiscriminatorPropertyName is not null)
            return ToLiteral(options.TypeDiscriminatorPropertyName);

        return "options.PolymorphismOptions.TypeDiscriminatorPropertyName";
    }

    private static bool DiscriminatorStyleWritesTag(int style)
        => style is DiscriminatorStyleTag or DiscriminatorStyleBoth;

    private static bool DiscriminatorStyleWritesProperty(int style)
        => style is DiscriminatorStyleProperty or DiscriminatorStyleBoth;

    private static bool DiscriminatorStyleReadsTag(int style)
        => style is DiscriminatorStyleTag or DiscriminatorStyleBoth;

    private static bool DiscriminatorStyleReadsProperty(int style)
        => style is DiscriminatorStyleProperty or DiscriminatorStyleBoth;

    private static void EmitDuplicateKeyHandling(
        StringBuilder builder,
        string? duplicateKeyHandlingOverride,
        string duplicateKeyExpression,
        string lastWinsAssignment,
        string indent)
    {
        switch (duplicateKeyHandlingOverride)
        {
            case "Error":
                builder.Append(indent).Append("throw global::Meziantou.Framework.Yaml.Serialization.YamlThrowHelper.ThrowDuplicateMappingKey(reader, ").Append(duplicateKeyExpression).AppendLine(");");
                return;
            case "FirstWins":
                builder.Append(indent).AppendLine("// Keep the first value for a duplicate key.");
                return;
            case "LastWins":
                builder.Append(indent).Append(lastWinsAssignment).AppendLine(";");
                return;
        }

        builder.Append(indent).AppendLine("switch (options.DuplicateKeyHandling)");
        builder.Append(indent).AppendLine("{");
        builder.Append(indent).AppendLine("    case global::Meziantou.Framework.Yaml.YamlDuplicateKeyHandling.Error:");
        builder.Append(indent).Append("        throw global::Meziantou.Framework.Yaml.Serialization.YamlThrowHelper.ThrowDuplicateMappingKey(reader, ").Append(duplicateKeyExpression).AppendLine(");");
        builder.Append(indent).AppendLine("    case global::Meziantou.Framework.Yaml.YamlDuplicateKeyHandling.FirstWins:");
        builder.Append(indent).AppendLine("        break;");
        builder.Append(indent).AppendLine("    case global::Meziantou.Framework.Yaml.YamlDuplicateKeyHandling.LastWins:");
        builder.Append(indent).Append("        ").Append(lastWinsAssignment).AppendLine(";");
        builder.Append(indent).AppendLine("        break;");
        builder.Append(indent).AppendLine("}");
    }

    private static void EmitUnknownDerivedTypeFailure(
        StringBuilder builder,
        int? unknownDerivedTypeHandlingOverride,
        string throwExpression,
        string indent)
    {
        if (unknownDerivedTypeHandlingOverride.HasValue)
        {
            if (unknownDerivedTypeHandlingOverride.Value == UnknownDerivedTypeHandlingFail)
            {
                builder.Append(indent).Append("throw ").Append(throwExpression).AppendLine(";");
            }

            return;
        }

        builder.Append(indent).AppendLine("if (unknownDerivedTypeHandling == global::Meziantou.Framework.Yaml.YamlUnknownDerivedTypeHandling.Fail)");
        builder.Append(indent).AppendLine("{");
        builder.Append(indent).Append("    throw ").Append(throwExpression).AppendLine(";");
        builder.Append(indent).AppendLine("}");
    }

    private static void EmitHandleUnmatchedMember(
        StringBuilder builder,
        string typeName,
        string keyExpression,
        string? unmappedMemberHandlingOverride,
        string indent)
    {
        if (string.Equals(unmappedMemberHandlingOverride, "Disallow", StringComparison.Ordinal))
        {
            builder.Append(indent).Append("throw global::Meziantou.Framework.Yaml.Serialization.YamlThrowHelper.ThrowUnmappedMember(reader, typeof(").Append(typeName).Append("), ").Append(keyExpression).AppendLine(");");
            return;
        }

        if (unmappedMemberHandlingOverride is null)
        {
            builder.Append(indent).AppendLine("if (unmappedMemberHandling == global::Meziantou.Framework.Yaml.YamlUnmappedMemberHandling.Disallow)");
            builder.Append(indent).AppendLine("{");
            builder.Append(indent).Append("    throw global::Meziantou.Framework.Yaml.Serialization.YamlThrowHelper.ThrowUnmappedMember(reader, typeof(").Append(typeName).Append("), ").Append(keyExpression).AppendLine(");");
            builder.Append(indent).AppendLine("}");
        }

        builder.Append(indent).AppendLine("reader.Skip();");
    }

    private static void EmitMappingOrderBranch(
        StringBuilder builder,
        bool? sortedMappingOrderOverride,
        string indent,
        Action<string> emitSorted,
        Action<string> emitDeclaration)
    {
        if (sortedMappingOrderOverride.HasValue)
        {
            if (sortedMappingOrderOverride.Value)
                emitSorted(indent);
            else
                emitDeclaration(indent);
            return;
        }

        builder.Append(indent).AppendLine("if (options.MappingOrder == global::Meziantou.Framework.Yaml.YamlMappingOrderPolicy.Sorted)");
        builder.Append(indent).AppendLine("{");
        emitSorted(indent + "    ");
        builder.Append(indent).AppendLine("}");
        builder.Append(indent).AppendLine("else");
        builder.Append(indent).AppendLine("{");
        emitDeclaration(indent + "    ");
        builder.Append(indent).AppendLine("}");
    }

    private static ImmutableArray<ITypeSymbol> CollectRuntimeCustomConverterTypes(ImmutableArray<ITypeSymbol> types)
    {
        var builder = ImmutableArray.CreateBuilder<ITypeSymbol>();
        var seen = new HashSet<ITypeSymbol>(SymbolEqualityComparer.Default);

        foreach (var type in types)
        {
            AddRuntimeCustomConverterType(type, includeMembers: true, builder, seen);
        }

        return builder.ToImmutable();
    }

    private static void AddRuntimeCustomConverterType(
        ITypeSymbol type,
        bool includeMembers,
        ImmutableArray<ITypeSymbol>.Builder builder,
        HashSet<ITypeSymbol> seen)
    {
        if (!seen.Add(type))
        {
            return;
        }

        builder.Add(type);

        if (type is IArrayTypeSymbol arrayType)
        {
            AddRuntimeCustomConverterType(arrayType.ElementType, includeMembers: true, builder, seen);
            return;
        }

        if (type is not INamedTypeSymbol namedType)
        {
            return;
        }

        if (namedType.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T && namedType.TypeArguments.Length == 1)
        {
            AddRuntimeCustomConverterType(namedType.TypeArguments[0], includeMembers: includeMembers, builder, seen);
        }

        if (TryGetSequenceElementType(type, out var elementType, out _))
        {
            AddRuntimeCustomConverterType(elementType, includeMembers: true, builder, seen);
            return;
        }

        if (TryGetDictionaryTypes(type, out var keyType, out var valueType, out _))
        {
            AddRuntimeCustomConverterType(keyType, includeMembers: true, builder, seen);
            AddRuntimeCustomConverterType(valueType, includeMembers: true, builder, seen);
            return;
        }

        foreach (var typeArgument in namedType.TypeArguments)
        {
            AddRuntimeCustomConverterType(typeArgument, includeMembers: true, builder, seen);
        }

        // The cases of a union, including the cases of a nested union, are read through the runtime converters, and a
        // runtime converter for one of them lets the case read any YAML kind.
        if (TryGetCSharpUnionCaseParameters(namedType, out var unionCaseParameters))
        {
            foreach (var parameter in unionCaseParameters)
            {
                AddRuntimeCustomConverterType(GetCSharpUnionRuntimeType(parameter.Type), includeMembers: true, builder, seen);
            }

            return;
        }

        if (!includeMembers || IsKnownScalar(type) || IsYamlNodeType(type) || IsUntypedObject(type))
        {
            return;
        }

        foreach (var member in GetSerializableMembers(namedType))
        {
            var memberType = GetMemberType(member);
            if (memberType is not null)
            {
                AddRuntimeCustomConverterType(memberType, includeMembers: true, builder, seen);
            }
        }
    }
}
