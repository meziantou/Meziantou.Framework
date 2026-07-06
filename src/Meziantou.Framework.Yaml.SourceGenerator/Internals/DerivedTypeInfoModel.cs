using Microsoft.CodeAnalysis;

namespace Meziantou.Framework.Yaml.SourceGeneration;

internal readonly struct DerivedTypeInfoModel
{
    public DerivedTypeInfoModel(ITypeSymbol derivedType, string? discriminator, string? tag)
    {
        DerivedType = derivedType;
        Discriminator = discriminator;
        Tag = tag;
    }

    public ITypeSymbol DerivedType { get; }

    public string? Discriminator { get; }

    public string? Tag { get; }
}
