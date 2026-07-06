using Meziantou.Framework.Yaml.Serialization;

namespace Meziantou.Framework.Yaml;

/// <summary>Represents metadata and operations for a serializable type.</summary>
public abstract class YamlTypeInfo
{
    /// <summary>
    /// Initializes a new instance of the <see cref="YamlTypeInfo"/> class.
    /// </summary>
    /// <param name="type">The represented CLR type.</param>
    /// <param name="options">The options associated with the metadata.</param>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="type"/> or <paramref name="options"/> is <see langword="null"/>.
    /// </exception>
    protected YamlTypeInfo(Type type, YamlSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(type);
        ArgumentNullException.ThrowIfNull(options);

        Type = type;
        Options = options;
    }

    /// <summary>Gets the CLR type represented by this metadata.</summary>
    public Type Type { get; }

    /// <summary>Gets the options associated with this metadata instance.</summary>
    public YamlSerializerOptions Options { get; }

    /// <summary>Writes a value to an existing YAML writer.</summary>
    /// <param name="writer">The writer that receives the value payload.</param>
    /// <param name="value">The value to serialize.</param>
    /// <exception cref="ArgumentNullException"><paramref name="writer"/> is <see langword="null"/>.</exception>
    public abstract void Write(YamlWriter writer, object? value);

    /// <summary>Reads a value from an existing YAML reader.</summary>
    /// <param name="reader">The reader positioned on the value token.</param>
    /// <returns>The deserialized value.</returns>
    public abstract object? ReadAsObject(YamlReader reader);
}

/// <summary>Represents metadata and operations for a specific serializable type.</summary>
/// <typeparam name="T">The represented CLR type.</typeparam>
public abstract class YamlTypeInfo<T> : YamlTypeInfo
{
    /// <summary>
    /// Initializes a new instance of the <see cref="YamlTypeInfo{T}"/> class.
    /// </summary>
    /// <param name="options">The options associated with the metadata.</param>
    protected YamlTypeInfo(YamlSerializerOptions options) : base(typeof(T), options)
    {
    }

    /// <summary>Writes a value to an existing YAML writer.</summary>
    /// <param name="writer">The writer that receives the value payload.</param>
    /// <param name="value">The value to serialize.</param>
    /// <exception cref="ArgumentNullException"><paramref name="writer"/> is <see langword="null"/>.</exception>
    public abstract void Write(YamlWriter writer, T value);

    /// <summary>Reads a value from an existing YAML reader.</summary>
    /// <param name="reader">The reader positioned on the value token.</param>
    /// <returns>The deserialized value.</returns>
    public abstract T? Read(YamlReader reader);

    /// <inheritdoc />
    public override void Write(YamlWriter writer, object? value)
    {
        ArgumentNullException.ThrowIfNull(writer);
        Write(writer, (T)value!);
    }

    /// <inheritdoc />
    public override object? ReadAsObject(YamlReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);
        return Read(reader);
    }
}

