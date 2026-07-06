namespace Meziantou.Framework.Yaml.Serialization;

/// <summary>Specifies the emitted order for a serialized member.</summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
public sealed class YamlPropertyOrderAttribute : YamlAttribute
{
    /// <summary>
    /// Initializes a new instance of the <see cref="YamlPropertyOrderAttribute"/> class.
    /// </summary>
    /// <param name="order">The order value.</param>
    public YamlPropertyOrderAttribute(int order)
    {
        Order = order;
    }

    /// <summary>Gets the order value.</summary>
    public int Order { get; }
}

