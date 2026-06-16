namespace Meziantou.Framework.Yamlish;

/// <summary>Provides a factory that creates Yamlish converters for supported types.</summary>
public abstract class YamlishConverterFactory : YamlishConverter
{
    /// <summary>Creates a converter for the specified type.</summary>
    /// <param name="typeToConvert">The type to convert.</param>
    /// <param name="options">The serializer options.</param>
    /// <returns>The converter for <paramref name="typeToConvert" />, or <see langword="null" /> if no converter can be created.</returns>
    public abstract YamlishConverter? CreateConverter(Type typeToConvert, YamlishSerializerOptions options);

    /// <inheritdoc />
    public sealed override object? Read(YamlishNode node, Type typeToConvert, YamlishSerializerOptions options) => throw new InvalidOperationException();

    /// <inheritdoc />
    public sealed override YamlishNode Write(object value, Type typeToConvert, YamlishSerializerOptions options) => throw new InvalidOperationException();
}
