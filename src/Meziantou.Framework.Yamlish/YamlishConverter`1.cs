namespace Meziantou.Framework.Yamlish;

/// <summary>Provides a strongly typed converter for reading and writing Yamlish nodes.</summary>
/// <typeparam name="T">The type handled by the converter.</typeparam>
public abstract class YamlishConverter<T> : YamlishConverter
{
    /// <inheritdoc />
    public sealed override bool CanConvert(Type typeToConvert) => typeToConvert == typeof(T);

    /// <summary>Reads a value from a Yamlish node.</summary>
    /// <param name="node">The node to read.</param>
    /// <param name="options">The serializer options.</param>
    /// <returns>The converted value.</returns>
    public abstract T? Read(YamlishNode node, YamlishSerializerOptions options);

    /// <summary>Writes a value as a Yamlish node.</summary>
    /// <param name="value">The value to write.</param>
    /// <param name="options">The serializer options.</param>
    /// <returns>The Yamlish node that represents the value.</returns>
    public abstract YamlishNode Write(T value, YamlishSerializerOptions options);

    /// <inheritdoc />
    public sealed override object? Read(YamlishNode node, Type typeToConvert, YamlishSerializerOptions options)
    {
        return Read(node, options);
    }

    /// <inheritdoc />
    public sealed override YamlishNode Write(object value, Type typeToConvert, YamlishSerializerOptions options)
    {
        return Write((T)value, options);
    }
}
