namespace Meziantou.Framework.Yamlish;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Interface | AttributeTargets.Struct, Inherited = false)]
public sealed class YamlishPolymorphicAttribute : YamlishAttribute
{
    private string _typeDiscriminatorPropertyName = "$type";

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
