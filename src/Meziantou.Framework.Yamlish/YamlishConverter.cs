namespace Meziantou.Framework.Yamlish;

/// <summary>Provides a converter for reading and writing Yamlish nodes.</summary>
public abstract class YamlishConverter
{
    /// <summary>
    /// Gets a value that indicates whether <see langword="null" /> should be passed to the converter on serialization, and whether <see langword="null" /> should be passed on deserialization.
    /// </sumary>
    public virtual bool HandleNullValues => false;

    /// <summary>Determines whether this converter can convert the specified type.</summary>
    /// <param name="typeToConvert">The type to convert.</param>
    /// <returns><see langword="true" /> if this converter can convert <paramref name="typeToConvert" />; otherwise, <see langword="false" />.</returns>
    public abstract bool CanConvert(Type typeToConvert);

    /// <summary>Reads a value from a Yamlish node.</summary>
    /// <param name="node">The node to read.</param>
    /// <param name="typeToConvert">The type to convert the node to.</param>
    /// <param name="options">The serializer options.</param>
    /// <returns>The converted value.</returns>
    public abstract object? Read(YamlishNode node, Type typeToConvert, YamlishSerializerOptions options);

    /// <summary>Writes a value as a Yamlish node.</summary>
    /// <param name="value">The value to write.</param>
    /// <param name="typeToConvert">The declared type of the value.</param>
    /// <param name="options">The serializer options.</param>
    /// <returns>The Yamlish node that represents the value.</returns>
    public abstract YamlishNode Write(object value, Type typeToConvert, YamlishSerializerOptions options);
}
