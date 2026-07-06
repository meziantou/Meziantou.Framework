using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Meziantou.Framework.Yaml.SourceGeneration;

/// <summary>
/// Generates YAML serialization metadata for types annotated with YAML source-generation attributes.
/// </summary>
[Generator]
public sealed partial class YamlSerializerContextGenerator : IIncrementalGenerator
{
    private static readonly string ThrowHelperContent = GetThrowHelperContent();
    private static readonly SymbolDisplayFormat FullyQualifiedNullableFormat = SymbolDisplayFormat.FullyQualifiedFormat.WithMiscellaneousOptions(
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
        messageFormat: "Type '{0}' contains member '{1}' of unsupported type '{2}'. Add [YamlSerializable(typeof({2}))] to the context or change the member type.",
        category: "Meziantou.Framework.Yaml.SourceGeneration",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor UnsupportedExtensionDataMember = new(
        id: "MFY003",
        title: "Unsupported extension data member",
        messageFormat: "Type '{0}' contains extension data member '{1}' of unsupported type '{2}'. Extension data members must be 'IDictionary<string, object>', 'IDictionary<string, Meziantou.Framework.Yaml.Model.YamlNode>', or 'Meziantou.Framework.Yaml.Model.YamlMapping'.",
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

    internal static readonly ImmutableArray<DiagnosticDescriptor> SupportedDiagnosticDescriptors = ImmutableArray.Create(
        ContextMustBePartial,
        UnsupportedMemberType,
        UnsupportedExtensionDataMember,
        MultipleExtensionDataMembers,
        InvalidSourceGenerationOption,
        InvalidConverterType,
        InvalidDerivedTypeMapping,
        MissingYamlPolymorphicOnDerivedTypeMappingBase);

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
            derivedTypeMappings);

        var indexByType = new Dictionary<ITypeSymbol, int>(resolvedTypes.Length, SymbolEqualityComparer.Default);
        for (var i = 0; i < resolvedTypes.Length; i++)
        {
            indexByType[resolvedTypes[i]] = i;
        }

        ValidateSourceGenerationOptions(diagnostics, compilation, model);

        // Validate that member types are generated as well (or are known scalars).
        for (var i = 0; i < resolvedTypes.Length; i++)
        {
            if (resolvedTypes[i] is not INamedTypeSymbol named || (named.TypeKind != TypeKind.Class && named.TypeKind != TypeKind.Struct))
            {
                continue;
            }

            if (IsYamlNodeType(named))
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

                if (IsKnownScalar(memberType) || IsYamlNodeType(memberType))
                {
                    continue;
                }

                // Skip if the member itself has [YamlConverter(typeof(...))] — the converter handles serialization.
                if (GetYamlConverterAttributeTypeName(member) is not null)
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
                    if (IsKnownScalar(arrayElementType) || IsYamlNodeType(arrayElementType) || indexByType.ContainsKey(arrayElementType) ||
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

                    if (IsKnownScalar(dictionaryValueType) || IsYamlNodeType(dictionaryValueType) || indexByType.ContainsKey(dictionaryValueType) ||
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

                if (!indexByType.ContainsKey(memberType))
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

            if (named.DeclaredAccessibility is Accessibility.Private or Accessibility.Protected or Accessibility.ProtectedAndInternal)
            {
                diagnostics.Add(Diagnostic.Create(
                    InvalidConverterType,
                    location,
                    named.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                    "Converter types must be accessible to the generated context (public or internal)."));
                continue;
            }

            if (!DerivesFrom(named, yamlConverterSymbol))
            {
                diagnostics.Add(Diagnostic.Create(
                    InvalidConverterType,
                    location,
                    named.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                    $"Converter types must derive from '{yamlConverterSymbol.ToDisplayString()}'."));
                continue;
            }

            if (!named.InstanceConstructors.Any(static ctor => ctor.Parameters.Length == 0 && ctor.DeclaredAccessibility is Accessibility.Public or Accessibility.Internal))
            {
                diagnostics.Add(Diagnostic.Create(
                    InvalidConverterType,
                    location,
                    named.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                    "Converter types must provide a public or internal parameterless constructor."));
            }
        }
    }

    private static bool DerivesFrom(INamedTypeSymbol symbol, INamedTypeSymbol baseType)
    {
        for (var current = symbol; current is not null; current = current.BaseType)
        {
            if (SymbolEqualityComparer.Default.Equals(current, baseType))
            {
                return true;
            }
        }

        return false;
    }

    private static ImmutableArray<ITypeSymbol> ExpandSerializableTypes(
        ImmutableArray<ITypeSymbol> roots,
        ImmutableArray<DerivedTypeMappingModel> contextMappings)
    {
        // Always include explicitly declared root types. Additionally include polymorphic derived types
        // so generated polymorphism dispatch can call into their serializers without requiring explicit roots.
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
            if (type is not INamedTypeSymbol named)
            {
                continue;
            }

            foreach (var derived in GetPolymorphicDerivedTypes(named, contextMappings))
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
                    if (IsKnownScalar(unionCase.Type) || IsYamlNodeType(unionCase.Type))
                    {
                        continue;
                    }

                    if (seen.Add(unionCase.Type))
                    {
                        builder.Add(unionCase.Type);
                        queue.Enqueue(unionCase.Type);
                    }
                }
            }
        }

        return builder.ToImmutable();
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
                string.Equals(constructed, "global::System.Collections.Generic.ISet<T>", StringComparison.Ordinal))
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
        }

        elementType = null!;
        kind = default;
        return false;
    }

    private static bool TryGetDictionaryValueType(ITypeSymbol type, out ITypeSymbol valueType)
    {
        if (type is INamedTypeSymbol named
            && named.IsGenericType
            && string.Equals(named.ConstructedFrom.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat), "global::System.Collections.Generic.Dictionary<TKey, TValue>", StringComparison.Ordinal)
            && named.TypeArguments.Length == 2
            && named.TypeArguments[0].SpecialType == SpecialType.System_String)
        {
            valueType = named.TypeArguments[1];
            return true;
        }

        valueType = null!;
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
        }

        keyType = null!;
        valueType = null!;
        kind = default;
        return false;
    }

    private static string GetDictionaryTypePrefix(ITypeSymbol type)
    {
        if (type is INamedTypeSymbol named && named.IsGenericType)
        {
            var constructed = named.ConstructedFrom.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            if (string.Equals(constructed, "global::System.Collections.Generic.OrderedDictionary<TKey, TValue>", StringComparison.Ordinal))
            {
                return "global::System.Collections.Generic.OrderedDictionary";
            }
        }
        return "global::System.Collections.Generic.Dictionary";
    }

    private static bool IsSupportedDictionaryKeyType(ITypeSymbol type)
    {
        if (type.SpecialType == SpecialType.System_String)
        {
            return true;
        }

        if (type is INamedTypeSymbol enumType && enumType.TypeKind == TypeKind.Enum)
        {
            return true;
        }

        return IsKnownScalar(type);
    }

    private static bool TryGetCSharpUnionCases(INamedTypeSymbol type, out ImmutableArray<CSharpUnionCaseModel> cases)
    {
        cases = ImmutableArray<CSharpUnionCaseModel>.Empty;

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

        var builder = ImmutableArray.CreateBuilder<CSharpUnionCaseModel>();
        foreach (var constructor in type.InstanceConstructors)
        {
            if (constructor.DeclaredAccessibility != Accessibility.Public)
            {
                continue;
            }

            if (constructor.Parameters.Length != 1)
            {
                if (constructor.IsImplicitlyDeclared && constructor.Parameters.Length == 0)
                {
                    continue;
                }

                return false;
            }

            var parameter = constructor.Parameters[0];
            var caseType = parameter.Type;
            var runtimeType = GetCSharpUnionRuntimeType(caseType);
            builder.Add(new CSharpUnionCaseModel(
                caseType,
                runtimeType,
                GetCSharpUnionCaseKind(runtimeType),
                IsCSharpUnionNullableCase(parameter)));
        }

        if (builder.Count == 0)
        {
            return false;
        }

        cases = builder.ToImmutable();
        return true;
    }

    private static bool IsCSharpUnionDeclaration(INamedTypeSymbol type)
    {
        foreach (var syntaxReference in type.DeclaringSyntaxReferences)
        {
#pragma warning disable RSEXPERIMENTAL006 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.
            if (syntaxReference.GetSyntax().IsKind(SyntaxKind.UnionDeclaration))
            {
                return true;
            }
#pragma warning restore RSEXPERIMENTAL006 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.
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
        if (type.SpecialType == SpecialType.System_Object || IsYamlNodeType(type))
        {
            return CSharpUnionCaseKind.Any;
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

        return type is INamedTypeSymbol systemType &&
               string.Equals(systemType.ContainingNamespace?.ToDisplayString(), "System", StringComparison.Ordinal) &&
               (string.Equals(systemType.Name, "Half", StringComparison.Ordinal) ||
                string.Equals(systemType.Name, "Int128", StringComparison.Ordinal) ||
                string.Equals(systemType.Name, "UInt128", StringComparison.Ordinal));
    }

    private static bool IsCSharpUnionStringLikeSystemType(ITypeSymbol type)
        => type is INamedTypeSymbol systemType &&
           string.Equals(systemType.ContainingNamespace?.ToDisplayString(), "System", StringComparison.Ordinal) &&
           (string.Equals(systemType.Name, "DateTime", StringComparison.Ordinal) ||
            string.Equals(systemType.Name, "DateTimeOffset", StringComparison.Ordinal) ||
            string.Equals(systemType.Name, "Guid", StringComparison.Ordinal) ||
            string.Equals(systemType.Name, "TimeSpan", StringComparison.Ordinal) ||
            string.Equals(systemType.Name, "DateOnly", StringComparison.Ordinal) ||
            string.Equals(systemType.Name, "TimeOnly", StringComparison.Ordinal));

    private static ImmutableArray<CSharpUnionCaseModel> SortCSharpUnionCasesForWriting(ImmutableArray<CSharpUnionCaseModel> cases)
    {
        var builder = cases.ToBuilder();
        builder.Sort((left, right) =>
        {
            if (SymbolEqualityComparer.Default.Equals(left.RuntimeType, right.RuntimeType))
            {
                return 0;
            }

            if (IsAssignableTo(right.RuntimeType, left.RuntimeType))
            {
                return 1;
            }

            if (IsAssignableTo(left.RuntimeType, right.RuntimeType))
            {
                return -1;
            }

            return 0;
        });
        return builder.ToImmutable();
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

    private static CSharpUnionCaseModel? GetSingleCSharpUnionCase(
        ImmutableArray<CSharpUnionCaseModel> cases,
        CSharpUnionCaseKind kind,
        out bool ambiguous)
    {
        CSharpUnionCaseModel? match = null;
        var matchCount = 0;
        for (var i = 0; i < cases.Length; i++)
        {
            var unionCase = cases[i];
            if (unionCase.Kind == kind || unionCase.Kind == CSharpUnionCaseKind.Any)
            {
                match ??= unionCase;
                matchCount++;
            }
        }

        ambiguous = matchCount > 1;
        return matchCount == 1 ? match : null;
    }

    private static string GetCSharpUnionKindDescription(CSharpUnionCaseKind kind)
        => kind switch
        {
            CSharpUnionCaseKind.Boolean => "boolean",
            CSharpUnionCaseKind.Number => "number",
            CSharpUnionCaseKind.String => "scalar string",
            CSharpUnionCaseKind.Sequence => "sequence",
            CSharpUnionCaseKind.Mapping => "mapping",
            _ => "untyped",
        };

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

        if (attributed is not null)
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

    private static bool IsConstructorAccessibleFromGeneratedContext(IMethodSymbol constructor)
        => constructor.DeclaredAccessibility is Accessibility.Public or Accessibility.Internal or Accessibility.ProtectedOrInternal;

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
            return "'" + (ch == '\'' ? "\\'" : ch.ToString()) + "'";
        }

        if (parameter.Type is INamedTypeSymbol enumType && enumType.TypeKind == TypeKind.Enum)
        {
            var enumTypeName = enumType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            var numeric = Convert.ToInt64(value, CultureInfo.InvariantCulture);
            return $"({enumTypeName}){numeric}";
        }

        // Numeric primitives and other literals.
        return Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) ?? "default";
    }

    private static MemberModel CreateMemberModel(ISymbol member, YamlNamingPolicy? propertyNamingPolicy)
    {
        var (nameForRead, nameForWrite) = GetSerializedMemberNameExpressions(member, propertyNamingPolicy);
        var type = GetMemberType(member) ?? throw new InvalidOperationException("Member type could not be determined.");
        var accessExpression = member is IPropertySymbol prop ? "value." + prop.Name : "value." + member.Name;
        Func<string, string> assign = member is IPropertySymbol propAssign
            ? rhs => "instance." + propAssign.Name + " = " + rhs
            : rhs => "instance." + member.Name + " = " + rhs;
        var ignoreConditionExpression = GetIgnoreConditionExpression();
        var converterTypeName = GetYamlConverterAttributeTypeName(member);
        var objectCreationHandling = GetObjectCreationHandling(member);
        var (blockSequenceMappingStyle, blockSequenceSequenceStyle) = GetBlockSequenceItemStyles(member);
        var isRequiredKeyword = member is IPropertySymbol { IsRequired: true } or IFieldSymbol { IsRequired: true };
        var isRequired = isRequiredKeyword || HasAttribute(member, "Meziantou.Framework.Yaml.Serialization.YamlRequiredAttribute");
        var isInitOnly = member is IPropertySymbol property && IsInitOnlyProperty(property);
        var hasIncludeAttribute = HasAttribute(member, "Meziantou.Framework.Yaml.Serialization.YamlIncludeAttribute");
        var requiresIncludeFields = member is IFieldSymbol { DeclaredAccessibility: Accessibility.Public } && !hasIncludeAttribute;
        var isReadOnlyProperty = member is IPropertySymbol && !IsWritableMember(member);
        var isReadOnlyField = member is IFieldSymbol { IsReadOnly: true };
        var disallowNull = IsNonNullableReferenceType(type);
        var numberHandling = converterTypeName is null ? GetNumberHandlingValue(member, type) : null;
        var enumCustomNames = converterTypeName is null ? GetEnumCustomNames(type) : null;
        return new MemberModel(member, type, nameForRead, nameForWrite, accessExpression, assign, ignoreConditionExpression, converterTypeName, objectCreationHandling, blockSequenceMappingStyle, blockSequenceSequenceStyle, isRequired, isInitOnly, isRequiredKeyword, requiresIncludeFields, disallowNull, disallowNull, isReadOnlyProperty, isReadOnlyField, numberHandling, enumCustomNames);
    }

    private static (string ForRead, string ForWrite) GetSerializedMemberNameExpressions(ISymbol member, YamlNamingPolicy? propertyNamingPolicy)
    {
        foreach (var attribute in member.GetAttributes())
        {
            if (attribute.AttributeClass is null)
            {
                continue;
            }

            if (string.Equals(attribute.AttributeClass.ToDisplayString(), "Meziantou.Framework.Yaml.Serialization.YamlPropertyNameAttribute", StringComparison.Ordinal))
            {
                if (attribute.ConstructorArguments.Length == 1 && attribute.ConstructorArguments[0].Value is string yamlName)
                {
                    var nameLiteral = ToLiteral(yamlName);
                    return (nameLiteral, nameLiteral);
                }
            }
        }

        var name = ApplyNamingPolicy(member.Name, propertyNamingPolicy);
        var resolvedLiteral = ToLiteral(name);
        return (resolvedLiteral, resolvedLiteral);
    }

    private static string ApplyNamingPolicy(string name, YamlNamingPolicy? policy)
    {
        return policy?.ConvertName(name) ?? name;
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
            _ => null,
        };
    }

    private static string GetIgnoreConditionExpression()
    {
        // Member-level ignore overrides options default. YAML ignore is treated as Always (handled by member filtering).
        return "options.DefaultIgnoreCondition";
    }

    private static int? GetNumberHandlingValue(ISymbol member, ITypeSymbol memberType)
    {
        if (!IsSupportedNumberHandlingType(memberType))
        {
            return null;
        }

        var value = TryGetNumberHandlingFromAttributes(member.GetAttributes());
        if (value is null && member.ContainingType is not null)
        {
            value = TryGetNumberHandlingFromAttributes(member.ContainingType.GetAttributes());
        }

        if (value is null || value.Value == 0)
        {
            return null;
        }

        return value;
    }

    private static int? TryGetNumberHandlingFromAttributes(ImmutableArray<AttributeData> attributes)
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
                return false;
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

        builder.Append(indent).Append("    default: writer.WriteScalar(").Append(valueExpression).AppendLine(".ToString()); break;");
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
                .Append("global::System.String.Equals(").Append(textExpression).Append(", ").Append(ToLiteral(scalar)).AppendLine(", global::System.StringComparison.Ordinal))");
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

    private static string? GetYamlConverterAttributeTypeName(ISymbol member)
    {
        foreach (var attribute in member.GetAttributes())
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

            return converterType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        }

        return null;
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
        if (GetYamlConverterAttributeTypeName(unwrappedType) is not null)
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

    private static ImmutableArray<ISymbol> GetSerializableMembers(INamedTypeSymbol type)
    {
        // Arrays/collections/dictionaries are handled by dedicated generated code paths, not as object graphs.
        if (TryGetArrayElementType(type, out _) ||
            TryGetSequenceElementType(type, out _, out _) ||
            TryGetDictionaryTypes(type, out _, out _, out _))
        {
            return ImmutableArray<ISymbol>.Empty;
        }

        // Include base members for parity with reflection/STJ behavior, but prefer the most-derived
        // member when a derived type hides/overrides a base member with the same CLR name.
        var members = new List<ISymbol>();
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
            foreach (var member in current.GetMembers())
            {
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

                    if (HasAttribute(property, "Meziantou.Framework.Yaml.Serialization.YamlIgnoreAttribute"))
                    {
                        continue;
                    }

                    if (indexByClrName.TryGetValue(property.Name, out var existingIndex))
                    {
                        members[existingIndex] = property;
                    }
                    else
                    {
                        indexByClrName.Add(property.Name, members.Count);
                        members.Add(property);
                    }

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

                    if (HasAttribute(field, "Meziantou.Framework.Yaml.Serialization.YamlIgnoreAttribute"))
                    {
                        continue;
                    }

                    if (indexByClrName.TryGetValue(field.Name, out var existingIndex))
                    {
                        members[existingIndex] = field;
                    }
                    else
                    {
                        indexByClrName.Add(field.Name, members.Count);
                        members.Add(field);
                    }
                }
            }
        }

        return members.ToImmutableArray();
    }

    private static ImmutableArray<ISymbol> GetExtensionDataMembers(INamedTypeSymbol type)
    {
        var matches = ImmutableArray.CreateBuilder<ISymbol>();

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
                if (member is not IPropertySymbol and not IFieldSymbol)
                {
                    continue;
                }

                if (HasAttribute(member, "Meziantou.Framework.Yaml.Serialization.YamlExtensionDataAttribute"))
                {
                    matches.Add(member);
                }
            }
        }

        return matches.ToImmutable();
    }

    private static bool IsSupportedExtensionDataMemberType(ITypeSymbol type)
    {
        if (string.Equals(type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat), "global::Meziantou.Framework.Yaml.Model.YamlMapping", StringComparison.Ordinal))
        {
            return true;
        }

        if (TryGetDictionaryValueType(type, out var valueType))
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

    private static ExtensionDataMemberModel? TryCreateExtensionDataMemberModel(INamedTypeSymbol type)
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
            kind = ExtensionDataKind.Dictionary;
            _ = TryGetDictionaryValueType(memberType, out dictionaryValueType);
        }

        var accessExpression = "instance." + symbol.Name;
        Func<string, string>? assignExpression = null;
        var canAssign = false;
        var isInitOnly = false;

        if (symbol is IPropertySymbol property)
        {
            isInitOnly = IsInitOnlyProperty(property);
            if (property.SetMethod is not null)
            {
                canAssign = !isInitOnly;
                if (canAssign)
                {
                    assignExpression = rhs => "instance." + property.Name + " = " + rhs;
                }
            }
        }
        else if (symbol is IFieldSymbol field)
        {
            canAssign = !field.IsConst && !field.IsReadOnly;
            if (canAssign)
            {
                assignExpression = rhs => "instance." + field.Name + " = " + rhs;
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

    private static bool HasAttribute(ISymbol symbol, string metadataName)
    {
        foreach (var attribute in symbol.GetAttributes())
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

        model = new SerializableTypeModel(typeSymbol, GetTypeInfoPropertyNameOverride(attribute));
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

        for (var i = 0; i < model.DerivedTypeMappings.Length; i++)
        {
            var mapping = model.DerivedTypeMappings[i];
            var baseType = mapping.BaseType;
            var derivedType = mapping.DerivedType;

            if (!IsAssignableTo(derivedType, baseType))
            {
                diagnostics.Add(Diagnostic.Create(
                    InvalidDerivedTypeMapping,
                    mapping.Location ?? model.ContextSymbol.Locations.FirstOrDefault(),
                    derivedType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat),
                    baseType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)));
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
        ImmutableArray<DerivedTypeMappingModel> contextMappings)
    {
        if (TryGetPolymorphismInfo(baseType, contextMappings, out var info))
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
        out PolymorphismInfoModel info)
    {
        string? discriminatorPropertyNameOverride = null;
        int? discriminatorStyleOverrideValue = null;
        int? unknownOverrideValue = null;
        int? yamlUnknownOverrideValue = null;

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
            if (derivedArg.Kind != TypedConstantKind.Type || derivedArg.Value is not ITypeSymbol derivedType)
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

            var isDefaultMapping = mapping.Discriminator is null && mapping.Tag is null;
            if (!CanAddLowerPrecedenceMapping(
                mapping.DerivedType,
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
                defaultDerivedType ??= mapping.DerivedType;
            }

            derivedTypes.Add(new DerivedTypeInfoModel(mapping.DerivedType, mapping.Discriminator, mapping.Tag));
            if (mapping.Discriminator is not null)
            {
                seenDiscriminators.Add(mapping.Discriminator);
            }

            if (mapping.Tag is not null)
            {
                seenTags.Add(mapping.Tag);
            }
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

    private static string GetUnmappedMemberHandlingExpression(ITypeSymbol typeSymbol)
    {
        if (typeSymbol is INamedTypeSymbol namedType)
        {
            var overrideValue = TryGetUnmappedMemberHandlingOverride(namedType);
            if (!string.IsNullOrEmpty(overrideValue))
            {
                return "global::Meziantou.Framework.Yaml.YamlUnmappedMemberHandling." + overrideValue;
            }
        }

        return "options.RejectUnmatchedProperties ? global::Meziantou.Framework.Yaml.YamlUnmappedMemberHandling.Disallow : options.UnmappedMemberHandling";
    }

    private static string GetPreferredObjectCreationHandlingExpression(ITypeSymbol typeSymbol)
    {
        if (typeSymbol is INamedTypeSymbol namedType)
        {
            var overrideValue = TryGetObjectCreationHandlingOverride(namedType);
            if (!string.IsNullOrEmpty(overrideValue))
            {
                return "global::Meziantou.Framework.Yaml.YamlObjectCreationHandling." + overrideValue;
            }
        }

        return "options.PreferredObjectCreationHandling";
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
        foreach (var attribute in member.GetAttributes())
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
        foreach (var attribute in member.GetAttributes())
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

    private static string ToLiteral(string value)
        => "@\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
}
