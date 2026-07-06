namespace Meziantou.Framework.Yaml.Serialization;

/// <summary>
/// Produces <see cref="YamlConverter"/> instances for a family of types.
/// </summary>
public abstract class YamlConverterFactory : YamlConverter
{
    /// <summary>
    /// Creates a converter for <paramref name="typeToConvert"/>.
    /// </summary>
    public abstract YamlConverter CreateConverter(Type typeToConvert, YamlSerializerOptions options);

    /// <inheritdoc />
    public sealed override object? Read(YamlReader reader, Type typeToConvert)
        => throw new InvalidOperationException("YamlConverterFactory instances must be expanded by the converter resolution pipeline.");

    /// <inheritdoc />
    public sealed override void Write(YamlWriter writer, object? value)
        => throw new InvalidOperationException("YamlConverterFactory instances must be expanded by the converter resolution pipeline.");
}

