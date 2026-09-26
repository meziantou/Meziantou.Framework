using System;

namespace Tomlyn.Serialization;

/// <summary>
/// Converts between TOML tokens and a CLR type.
/// </summary>
public abstract class TomlConverter
{
    /// <summary>
    /// Determines whether this converter can handle <paramref name="typeToConvert"/>.
    /// </summary>
    public abstract bool CanConvert(Type typeToConvert);

    /// <summary>
    /// Reads a value from TOML.
    /// </summary>
    public abstract object? Read(TomlReader reader, Type typeToConvert);

    /// <summary>
    /// Writes a value to TOML.
    /// </summary>
    public abstract void Write(TomlWriter writer, object? value);
}

/// <summary>
/// Converts between TOML and a specific CLR type.
/// </summary>
/// <typeparam name="T">The CLR type handled by this converter.</typeparam>
public abstract class TomlConverter<T> : TomlConverter
{
    /// <inheritdoc />
    public sealed override bool CanConvert(Type typeToConvert) => typeToConvert == typeof(T);

    /// <inheritdoc />
    public sealed override object? Read(TomlReader reader, Type typeToConvert) => Read(reader);

    /// <inheritdoc />
    public sealed override void Write(TomlWriter writer, object? value) => Write(writer, (T)value!);

    /// <summary>
    /// Reads a value from TOML.
    /// </summary>
    public abstract T? Read(TomlReader reader);

    /// <summary>
    /// Writes a value to TOML.
    /// </summary>
    public abstract void Write(TomlWriter writer, T value);
}

