namespace Meziantou.Framework.Yamlish;

/// <summary>Specifies the Yamlish property name used for a field or property.</summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
public sealed class YamlishPropertyNameAttribute : YamlishAttribute
{
    /// <summary>Initializes a new instance of the <see cref="YamlishPropertyNameAttribute" /> class.</summary>
    /// <param name="name">The Yamlish property name.</param>
    public YamlishPropertyNameAttribute(string name)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        Name = name;
    }

    /// <summary>Gets the Yamlish property name.</summary>
    public string Name { get; }
}
