namespace Meziantou.Framework.Yaml.Serialization;

/// <summary>
/// Specifies a custom <see cref="YamlConverter"/> to use when serializing or deserializing a member or type.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Enum | AttributeTargets.Interface | AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = false)]
public sealed class YamlConverterAttribute : YamlAttribute
{
    /// <summary>
    /// Initializes a new instance of the <see cref="YamlConverterAttribute"/> class.
    /// </summary>
    /// <param name="converterType">The converter type.</param>
    /// <exception cref="ArgumentNullException"><paramref name="converterType"/> is <see langword="null"/>.</exception>
    public YamlConverterAttribute([DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)] Type converterType)
    {
        ArgumentNullException.ThrowIfNull(converterType);
        ConverterType = converterType;
    }

    /// <summary>Gets the converter type.</summary>
    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)]
    public Type ConverterType { get; }
}
