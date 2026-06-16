namespace Meziantou.Framework.Yamlish;

/// <summary>Configures a type for polymorphic Yamlish serialization and deserialization.</summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Interface | AttributeTargets.Struct, Inherited = false)]
public sealed class YamlishPolymorphicAttribute : YamlishAttribute
{
    private string _typeDiscriminatorPropertyName = "$type";

    /// <summary>Gets or sets the mapping key used to store the type discriminator.</summary>
    public string TypeDiscriminatorPropertyName
    {
        get => _typeDiscriminatorPropertyName;
        set
        {
            ArgumentException.ThrowIfNullOrEmpty(value);
            _typeDiscriminatorPropertyName = value;
        }
    }
}
