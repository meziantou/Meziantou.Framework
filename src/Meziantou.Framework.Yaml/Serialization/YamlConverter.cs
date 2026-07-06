namespace Meziantou.Framework.Yaml.Serialization;

/// <summary>Converts between YAML tokens and a CLR type.</summary>
public abstract class YamlConverter
{
    /// <summary>
    /// Determines whether this converter can handle <paramref name="typeToConvert"/>.
    /// </summary>
    public abstract bool CanConvert(Type typeToConvert);

    /// <summary>Reads a value from YAML.</summary>
    public abstract object? Read(YamlReader reader, Type typeToConvert);

    /// <summary>Determines whether this converter can populate an existing instance instead of creating a replacement.</summary>
    /// <remarks>
    /// The default implementation returns <see langword="false"/>.
    /// </remarks>
    public virtual bool CanPopulate(Type typeToConvert)
    {
        ArgumentNullException.ThrowIfNull(typeToConvert);
        return false;
    }

    /// <summary>Populates an existing value instance from YAML.</summary>
    /// <remarks>
    /// Converters that support population should override both <see cref="CanPopulate(Type)"/> and this method.
    /// The default implementation throws <see cref="NotSupportedException"/>.
    /// </remarks>
    public virtual object? Populate(YamlReader reader, Type typeToConvert, object existingValue)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(typeToConvert);
        ArgumentNullException.ThrowIfNull(existingValue);
        throw new NotSupportedException($"Converter '{GetType()}' does not support populating '{typeToConvert}'.");
    }

    /// <summary>Writes a value to YAML.</summary>
    public abstract void Write(YamlWriter writer, object? value);
}

/// <summary>Converts between YAML and a specific CLR type.</summary>
/// <typeparam name="T">The CLR type handled by this converter.</typeparam>
public abstract class YamlConverter<T> : YamlConverter
{
    /// <inheritdoc />
    public sealed override bool CanConvert(Type typeToConvert) => typeToConvert == typeof(T);

    /// <inheritdoc />
    public sealed override object? Read(YamlReader reader, Type typeToConvert)
    {
        return Read(reader);
    }

    /// <inheritdoc />
    public sealed override void Write(YamlWriter writer, object? value)
    {
        Write(writer, (T)value!);
    }

    /// <summary>Reads a value from YAML.</summary>
    public abstract T? Read(YamlReader reader);

    /// <summary>Writes a value to YAML.</summary>
    public abstract void Write(YamlWriter writer, T value);
}

