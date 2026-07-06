namespace Meziantou.Framework.Yaml.Serialization;

/// <summary>
/// Declares a root type for a source-generated <see cref="YamlSerializerContext"/>.
/// </summary>
/// <remarks>
/// Apply this attribute to a <see cref="YamlSerializerContext"/>-derived partial class once per root type
/// that should have generated YAML metadata.
/// </remarks>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public sealed class YamlSerializableAttribute : YamlAttribute
{
    /// <summary>
    /// Initializes a new instance of the <see cref="YamlSerializableAttribute"/> class.
    /// </summary>
    /// <param name="type">The root type that should have generated YAML metadata.</param>
    /// <exception cref="ArgumentNullException"><paramref name="type"/> is <see langword="null"/>.</exception>
    public YamlSerializableAttribute(Type type)
    {
        ArgumentNullException.ThrowIfNull(type);
        Type = type;
    }

    /// <summary>Gets the root type that should have generated YAML metadata.</summary>
    public Type Type { get; }

    /// <summary>
    /// Gets or sets the generated <see cref="YamlTypeInfo"/> property name exposed on the context.
    /// </summary>
    public string? TypeInfoPropertyName { get; set; }
}
