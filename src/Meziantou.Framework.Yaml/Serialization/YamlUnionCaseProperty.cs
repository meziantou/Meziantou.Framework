namespace Meziantou.Framework.Yaml.Serialization;

/// <summary>Describes a mapping key declared by a union case that serializes as a YAML mapping.</summary>
public sealed class YamlUnionCaseProperty
{
    /// <summary>Initializes a new instance of the <see cref="YamlUnionCaseProperty"/> class.</summary>
    /// <param name="name">The mapping key, after the naming policy has been applied.</param>
    /// <param name="isRequired">Whether the payload must contain the key.</param>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> is <see langword="null"/>.</exception>
    public YamlUnionCaseProperty(string name, bool isRequired)
    {
        ArgumentNullException.ThrowIfNull(name);

        Name = name;
        IsRequired = isRequired;
    }

    /// <summary>Gets the mapping key, after the naming policy has been applied.</summary>
    public string Name { get; }

    /// <summary>Gets a value indicating whether the payload must contain the key.</summary>
    public bool IsRequired { get; }
}
