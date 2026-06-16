namespace Meziantou.Framework.Yamlish.Internals;

internal sealed class YamlishPolymorphismInfo
{
    public YamlishPolymorphismInfo(string typeDiscriminatorPropertyName, YamlishDerivedTypeInfo[] derivedTypes)
    {
        TypeDiscriminatorPropertyName = typeDiscriminatorPropertyName;
        DerivedTypes = derivedTypes;
    }

    public string TypeDiscriminatorPropertyName { get; }

    public YamlishDerivedTypeInfo[] DerivedTypes { get; }

    public YamlishDerivedTypeInfo? GetDerivedType(Type type)
    {
        return DerivedTypes.FirstOrDefault(derivedType => derivedType.Type == type);
    }

    public YamlishDerivedTypeInfo? GetDerivedType(string typeDiscriminator)
    {
        return DerivedTypes.FirstOrDefault(derivedType => string.Equals(derivedType.TypeDiscriminator, typeDiscriminator, StringComparison.Ordinal));
    }
}
