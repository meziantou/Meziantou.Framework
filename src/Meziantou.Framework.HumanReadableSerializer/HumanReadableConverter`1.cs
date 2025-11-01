namespace Meziantou.Framework.HumanReadable;

/// <summary>Base class for strongly-typed converters.</summary>
/// <typeparam name="T">The type this converter handles.</typeparam>
public abstract class HumanReadableConverter<T> : HumanReadableConverter
{
    /// <summary>Determines whether this converter can convert the specified type.</summary>
    /// <param name="type">The type to check.</param>
    /// <returns><see langword="true"/> if the converter can convert the type; otherwise, <see langword="false"/>.</returns>
    public sealed override bool CanConvert(Type type) => typeof(T).IsAssignableFrom(type);

    /// <summary>Writes a value to the writer.</summary>
    /// <param name="writer">The writer to write to.</param>
    /// <param name="value">The value to write.</param>
    /// <param name="valueType">The type of the value.</param>
    /// <param name="options">The serialization options.</param>
    public sealed override void WriteValue(HumanReadableTextWriter writer, object? value, Type valueType, HumanReadableSerializerOptions options)
    {
        WriteValue(writer, (T?)value, options);
    }

    /// <summary>Writes a strongly-typed value to the writer.</summary>
    /// <param name="writer">The writer to write to.</param>
    /// <param name="value">The value to write.</param>
    /// <param name="options">The serialization options.</param>
    protected abstract void WriteValue(HumanReadableTextWriter writer, T? value, HumanReadableSerializerOptions options);
}
