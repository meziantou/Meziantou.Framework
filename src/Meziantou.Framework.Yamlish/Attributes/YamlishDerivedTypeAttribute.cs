namespace Meziantou.Framework.Yamlish;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Interface | AttributeTargets.Struct, AllowMultiple = true, Inherited = false)]
public sealed class YamlishDerivedTypeAttribute : YamlishAttribute
{
    public YamlishDerivedTypeAttribute(Type derivedType)
    {
        ArgumentNullException.ThrowIfNull(derivedType);
        DerivedType = derivedType;
        TypeDiscriminator = derivedType.Name;
    }

    public YamlishDerivedTypeAttribute(Type derivedType, string typeDiscriminator)
    {
        ArgumentNullException.ThrowIfNull(derivedType);
        ArgumentException.ThrowIfNullOrEmpty(typeDiscriminator);
        DerivedType = derivedType;
        TypeDiscriminator = typeDiscriminator;
    }

    public YamlishDerivedTypeAttribute(Type derivedType, int typeDiscriminator)
    {
        ArgumentNullException.ThrowIfNull(derivedType);
        DerivedType = derivedType;
        TypeDiscriminator = typeDiscriminator.ToString(CultureInfo.InvariantCulture);
    }

    public Type DerivedType { get; }

    public string TypeDiscriminator { get; }
}
