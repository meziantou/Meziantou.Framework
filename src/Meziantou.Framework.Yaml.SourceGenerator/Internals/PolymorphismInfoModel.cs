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
        ITypeSymbol? defaultDerivedType)
    {
        DiscriminatorPropertyNameOverride = discriminatorPropertyNameOverride;
        DiscriminatorStyleOverrideValue = discriminatorStyleOverrideValue;
        UnknownDerivedTypeHandlingOverrideValue = unknownDerivedTypeHandlingOverrideValue;
        DerivedTypes = derivedTypes;
        DefaultDerivedType = defaultDerivedType;
    }

    public string? DiscriminatorPropertyNameOverride { get; }

    public int? DiscriminatorStyleOverrideValue { get; }

    public int? UnknownDerivedTypeHandlingOverrideValue { get; }

    public ImmutableArray<DerivedTypeInfoModel> DerivedTypes { get; }

    public ITypeSymbol? DefaultDerivedType { get; }
}
