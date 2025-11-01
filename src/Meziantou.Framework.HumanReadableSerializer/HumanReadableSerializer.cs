namespace Meziantou.Framework.HumanReadable;

/// <summary>
/// Provides methods to serialize objects into a human-readable text format.
/// </summary>
public static class HumanReadableSerializer
{
    /// <summary>
    /// Serializes the specified value to a human-readable string representation.
    /// </summary>
    /// <param name="value">The value to serialize.</param>
    /// <param name="options">The serializer options to use, or <see langword="null"/> to use the default options.</param>
    /// <returns>A human-readable string representation of the value.</returns>
    public static string Serialize(object? value, HumanReadableSerializerOptions? options = null)
    {
        options ??= new HumanReadableSerializerOptions();
        var writer = new HumanReadableTextWriter(options);
        Serialize(writer, value, value?.GetType() ?? typeof(object), options);
        return writer.ToString();
    }

    /// <summary>
    /// Serializes the specified value to a human-readable string representation using the specified type.
    /// </summary>
    /// <param name="value">The value to serialize.</param>
    /// <param name="type">The type to use for serialization.</param>
    /// <param name="options">The serializer options to use, or <see langword="null"/> to use the default options.</param>
    /// <returns>A human-readable string representation of the value.</returns>
    /// <exception cref="ArgumentException">Thrown when the value cannot be assigned to the specified type.</exception>
    public static string Serialize(object? value, Type type, HumanReadableSerializerOptions? options = null)
    {
        if (value is not null && !type.IsAssignableFrom(value.GetType()))
            throw new ArgumentException($"The provided value cannot be assigned to type '{type.AssemblyQualifiedName}'", nameof(value));

        options ??= new HumanReadableSerializerOptions();
        var writer = new HumanReadableTextWriter(options);
        Serialize(writer, value, type, options);
        return writer.ToString();
    }

    /// <summary>
    /// Serializes the specified value to the provided writer using the specified type and options.
    /// </summary>
    /// <param name="writer">The writer to write the serialized output to.</param>
    /// <param name="value">The value to serialize.</param>
    /// <param name="type">The type to use for serialization.</param>
    /// <param name="options">The serializer options to use.</param>
    public static void Serialize(HumanReadableTextWriter writer, object? value, Type type, HumanReadableSerializerOptions options)
    {
        using (options.BeginScope())
        {
            var converter = options.GetConverter(type);
            converter.WriteValue(writer, value, type, options);
        }
    }

    /// <summary>
    /// Serializes the specified value to the provided writer using the specified options.
    /// </summary>
    /// <typeparam name="T">The type of the value to serialize.</typeparam>
    /// <param name="writer">The writer to write the serialized output to.</param>
    /// <param name="value">The value to serialize.</param>
    /// <param name="options">The serializer options to use.</param>
    public static void Serialize<T>(HumanReadableTextWriter writer, T? value, HumanReadableSerializerOptions options)
    {
        Serialize(writer, value, value?.GetType() ?? typeof(T), options);
    }
}
