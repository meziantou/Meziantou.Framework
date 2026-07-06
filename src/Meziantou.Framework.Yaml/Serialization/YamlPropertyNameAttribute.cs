namespace Meziantou.Framework.Yaml.Serialization;

/// <summary>Specifies the serialized YAML property name for a member.</summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
public sealed class YamlPropertyNameAttribute : YamlAttribute
{
    /// <summary>
    /// Initializes a new instance of the <see cref="YamlPropertyNameAttribute"/> class.
    /// </summary>
    /// <param name="name">The serialized member name.</param>
    /// <exception cref="ArgumentException"><paramref name="name"/> is empty.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> is <see langword="null"/>.</exception>
    public YamlPropertyNameAttribute(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        if (name.Length == 0)
        {
            throw new ArgumentException("Property name cannot be empty.", nameof(name));
        }

        Name = name;
    }

    /// <summary>Gets the serialized member name.</summary>
    public string Name { get; }
}

