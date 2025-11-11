namespace Meziantou.Framework.HumanReadable;

/// <summary>Base class for converter factories that create converters for specific types.</summary>
public abstract class HumanReadableConverterFactory : HumanReadableConverter
{
    /// <summary>Creates a converter for the specified type.</summary>
    /// <param name="typeToConvert">The type to create a converter for.</param>
    /// <param name="options">The serialization options.</param>
    /// <returns>A converter for the type, or <see langword="null"/> if no converter could be created.</returns>
    public abstract HumanReadableConverter? CreateConverter(Type typeToConvert, HumanReadableSerializerOptions options);

    public sealed override void WriteValue(HumanReadableTextWriter writer, object? value, Type valueType, HumanReadableSerializerOptions options) => throw new InvalidOperationException();
}
