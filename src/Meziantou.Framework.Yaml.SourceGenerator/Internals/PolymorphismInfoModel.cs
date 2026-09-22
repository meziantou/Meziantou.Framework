using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace Meziantou.Framework.Yaml.SourceGeneration;

internal sealed class PolymorphismInfoModel
{
    public PolymorphismInfoModel(
        string? discriminatorPropertyNameOverride,
        int? discriminatorStyleOverrideValue,
        int? unknownDerivedTypeHandlingOverrideValue,
        ImmutableArray<DerivedTypeInfoModel> derivedTypes,
        ITypeSymbol? defaultDerivedType,
        bool infersDerivedTypesAtRuntime)
    {
        DiscriminatorPropertyNameOverride = discriminatorPropertyNameOverride;
        DiscriminatorStyleOverrideValue = discriminatorStyleOverrideValue;
        UnknownDerivedTypeHandlingOverrideValue = unknownDerivedTypeHandlingOverrideValue;
        DerivedTypes = derivedTypes;
        DefaultDerivedType = defaultDerivedType;
        InfersDerivedTypesAtRuntime = infersDerivedTypesAtRuntime;
    }

    public string? DiscriminatorPropertyNameOverride { get; }

    public int? DiscriminatorStyleOverrideValue { get; }

    public int? UnknownDerivedTypeHandlingOverrideValue { get; }

    public ImmutableArray<DerivedTypeInfoModel> DerivedTypes { get; }

    public ITypeSymbol? DefaultDerivedType { get; }

    /// <summary>
    /// Gets a value indicating whether <see cref="DerivedTypes"/> were inferred from a closed hierarchy that neither the
    /// declaration nor the source-generation options opt into, so they only apply when the runtime options enable
    /// <c>YamlPolymorphismOptions.InferClosedTypePolymorphism</c>.
    /// </summary>
    public bool InfersDerivedTypesAtRuntime { get; }
}
