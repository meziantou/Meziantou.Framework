namespace Meziantou.Framework.Yamlish;

[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
public sealed class YamlishPropertyNameAttribute : YamlishAttribute
{
    public YamlishPropertyNameAttribute(string name)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        Name = name;
    }

    public string Name { get; }
}
