using Meziantou.Framework.HumanReadable.Converters;

namespace Meziantou.Framework.HumanReadable;

/// <summary>Base class for converters that convert objects to human-readable text.</summary>
public abstract class HumanReadableConverter
{
    /// <summary>Gets a value indicating whether this converter handles null values.</summary>
    public virtual bool HandleNull { get; }

    /// <summary>Determines whether this converter can convert the specified type.</summary>
    /// <param name="type">The type to check.</param>
    /// <returns><see langword="true"/> if the converter can convert the type; otherwise, <see langword="false"/>.</returns>
    public abstract bool CanConvert(Type type);

    /// <summary>Writes a value to the writer.</summary>
    /// <param name="writer">The writer to write to.</param>
    /// <param name="value">The value to write.</param>
    /// <param name="valueType">The type of the value.</param>
    /// <param name="options">The serialization options.</param>
    public abstract void WriteValue(HumanReadableTextWriter writer, object? value, Type valueType, HumanReadableSerializerOptions options);

    internal static HumanReadableConverter CreateFromAttribute(HumanReadableConverterAttribute converterAttribute, Type typeToConvert, HumanReadableSerializerOptions options)
    {
        converterAttribute.EnsureTypeIsValid();

        var converter = converterAttribute.ConverterInstance ?? (HumanReadableConverter)Activator.CreateInstance(converterAttribute.ConverterType)!;
        if (!converter.CanConvert(typeToConvert))
            throw new HumanReadableSerializerException($"The converter '{converter.GetType().FullName}' is not compatible with '{typeToConvert.FullName}'");

        if (converter is HumanReadableConverterFactory factory)
        {
            converter = factory.CreateConverter(typeToConvert, options)
                ?? throw new HumanReadableSerializerException($"The converter factory '{factory.GetType().FullName}' did not create a converter for '{typeToConvert.FullName}'");
        }

        if (!converter.HandleNull)
        {
            converter = new NullConverterWrapper(converter);
        }

        return converter;
    }
}
