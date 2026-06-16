namespace Meziantou.Framework.Yamlish;

/// <summary>Declares a derived type that can be used when serializing or deserializing polymorphic values.</summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Interface | AttributeTargets.Struct, AllowMultiple = true, Inherited = false)]
public sealed class YamlishDerivedTypeAttribute : YamlishAttribute
{
    /// <summary>Initializes a new instance of the <see cref="YamlishDerivedTypeAttribute" /> class.</summary>
    /// <param name="derivedType">The derived type.</param>
    public YamlishDerivedTypeAttribute(Type derivedType)
    {
        ArgumentNullException.ThrowIfNull(derivedType);
        DerivedType = derivedType;
        TypeDiscriminator = derivedType.Name;
    }

    /// <summary>Initializes a new instance of the <see cref="YamlishDerivedTypeAttribute" /> class.</summary>
    /// <param name="derivedType">The derived type.</param>
    /// <param name="typeDiscriminator">The discriminator value used to identify the derived type.</param>
    public YamlishDerivedTypeAttribute(Type derivedType, string typeDiscriminator)
    {
        ArgumentNullException.ThrowIfNull(derivedType);
        ArgumentException.ThrowIfNullOrEmpty(typeDiscriminator);
        DerivedType = derivedType;
        TypeDiscriminator = typeDiscriminator;
    }

    /// <summary>Initializes a new instance of the <see cref="YamlishDerivedTypeAttribute" /> class.</summary>
    /// <param name="derivedType">The derived type.</param>
    /// <param name="typeDiscriminator">The discriminator value used to identify the derived type.</param>
    public YamlishDerivedTypeAttribute(Type derivedType, int typeDiscriminator)
    {
        ArgumentNullException.ThrowIfNull(derivedType);
        DerivedType = derivedType;
        TypeDiscriminator = typeDiscriminator.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>Gets the derived type.</summary>
    public Type DerivedType { get; }

    /// <summary>Gets the discriminator value used to identify the derived type.</summary>
    public string TypeDiscriminator { get; }
}
