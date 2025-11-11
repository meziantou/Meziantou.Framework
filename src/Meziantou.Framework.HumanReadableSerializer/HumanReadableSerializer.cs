namespace Meziantou.Framework.HumanReadable;

/// <summary>Serializes objects to a human-readable text format.</summary>
/// <example>
/// <code>
/// var obj = new { Name = "John Doe", Age = 32 };
/// var output = HumanReadableSerializer.Serialize(obj);
/// // Output:
/// // Name: John Doe
/// // Age: 32
/// </code>
/// </example>
public static class HumanReadableSerializer
{
    /// <summary>Serializes the specified value to a human-readable string.</summary>
    /// <param name="value">The value to serialize.</param>
    /// <param name="options">Options to control the serialization behavior.</param>
    /// <returns>A human-readable string representation of the value.</returns>
    public static string Serialize(object? value, HumanReadableSerializerOptions? options = null)
    {
        options ??= new HumanReadableSerializerOptions();
        var writer = new HumanReadableTextWriter(options);
        Serialize(writer, value, value?.GetType() ?? typeof(object), options);
        return writer.ToString();
    }

    /// <summary>Serializes the specified value as the specified type to a human-readable string.</summary>
    /// <param name="value">The value to serialize.</param>
    /// <param name="type">The type to use for serialization.</param>
    /// <param name="options">Options to control the serialization behavior.</param>
    /// <returns>A human-readable string representation of the value.</returns>
    public static string Serialize(object? value, Type type, HumanReadableSerializerOptions? options = null)
    {
        if (value is not null && !type.IsAssignableFrom(value.GetType()))
            throw new ArgumentException($"The provided value cannot be assigned to type '{type.AssemblyQualifiedName}'", nameof(value));

        options ??= new HumanReadableSerializerOptions();
        var writer = new HumanReadableTextWriter(options);
        Serialize(writer, value, type, options);
        return writer.ToString();
    }

    /// <summary>Serializes the specified value to the provided writer.</summary>
    /// <param name="writer">The writer to serialize to.</param>
    /// <param name="value">The value to serialize.</param>
    /// <param name="type">The type to use for serialization.</param>
    /// <param name="options">Options to control the serialization behavior.</param>
    public static void Serialize(HumanReadableTextWriter writer, object? value, Type type, HumanReadableSerializerOptions options)
    {
        using (options.BeginScope())
        {
            var converter = options.GetConverter(type);
            converter.WriteValue(writer, value, type, options);
        }
    }

    /// <summary>Serializes the specified value to the provided writer.</summary>
    /// <typeparam name="T">The type of the value to serialize.</typeparam>
    /// <param name="writer">The writer to serialize to.</param>
    /// <param name="value">The value to serialize.</param>
    /// <param name="options">Options to control the serialization behavior.</param>
    public static void Serialize<T>(HumanReadableTextWriter writer, T? value, HumanReadableSerializerOptions options)
    {
        Serialize(writer, value, value?.GetType() ?? typeof(T), options);
    }
}
