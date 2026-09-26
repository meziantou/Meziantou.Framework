// Copyright (c) Alexandre Mutel. All rights reserved.
// Licensed under the BSD-Clause 2 license.
// See license.txt file in the project root for full license information.

using System;
using Tomlyn.Helpers;
using Tomlyn.Serialization;

namespace Tomlyn;

/// <summary>
/// Represents metadata and operations for a serializable type.
/// </summary>
public abstract class TomlTypeInfo
{
    /// <summary>
    /// Initializes a new instance of the <see cref="TomlTypeInfo"/> class.
    /// </summary>
    /// <param name="type">The represented CLR type.</param>
    /// <param name="options">The options associated with the metadata.</param>
    protected TomlTypeInfo(Type type, TomlSerializerOptions options)
    {
        ArgumentGuard.ThrowIfNull(type, nameof(type));
        ArgumentGuard.ThrowIfNull(options, nameof(options));

        Type = type;
        Options = options;
    }

    /// <summary>
    /// Gets the CLR type represented by this metadata.
    /// </summary>
    public Type Type { get; }

    /// <summary>
    /// Gets the options associated with this metadata instance.
    /// </summary>
    public TomlSerializerOptions Options { get; }

    /// <summary>
    /// Gets a value indicating whether this metadata writes values as TOML tables.
    /// </summary>
    public virtual bool WritesTable => false;

    /// <summary>
    /// Writes a value to an existing TOML writer.
    /// </summary>
    public abstract void Write(TomlWriter writer, object? value);

    /// <summary>
    /// Reads a value from an existing TOML reader.
    /// </summary>
    public abstract object? ReadAsObject(TomlReader reader);

    /// <summary>
    /// Reads a value from an existing TOML reader, populating an existing CLR instance when supported.
    /// </summary>
    /// <remarks>
    /// The return value is the effective deserialized value for the caller to use. Implementations can return the
    /// original populated instance or a replacement value. Returning the effective value is required for populate
    /// scenarios involving structs and nullable value types, where a modified copy must be reassigned by the caller.
    /// The default implementation ignores <paramref name="existingValue"/> and falls back to
    /// <see cref="ReadAsObject(TomlReader)"/>, which preserves replace semantics for metadata that does not support
    /// in-place population.
    /// </remarks>
    /// <param name="reader">The TOML reader positioned at the value to read.</param>
    /// <param name="existingValue">The existing CLR value to populate, if any.</param>
    /// <returns>The populated or replacement value.</returns>
    public virtual object? ReadInto(TomlReader reader, object? existingValue)
    {
        ArgumentGuard.ThrowIfNull(reader, nameof(reader));
        return ReadAsObject(reader);
    }
}

/// <summary>
/// Represents metadata and operations for a specific serializable type.
/// </summary>
/// <typeparam name="T">The represented CLR type.</typeparam>
public abstract class TomlTypeInfo<T> : TomlTypeInfo
{
    /// <summary>
    /// Initializes a new instance of the <see cref="TomlTypeInfo{T}"/> class.
    /// </summary>
    /// <param name="options">The options associated with the metadata.</param>
    protected TomlTypeInfo(TomlSerializerOptions options)
        : base(typeof(T), options)
    {
    }

    /// <summary>
    /// Writes a value to an existing TOML writer.
    /// </summary>
    public abstract void Write(TomlWriter writer, T value);

    /// <summary>
    /// Reads a value from an existing TOML reader.
    /// </summary>
    public abstract T? Read(TomlReader reader);

    /// <inheritdoc />
    public sealed override void Write(TomlWriter writer, object? value)
    {
        ArgumentGuard.ThrowIfNull(writer, nameof(writer));
        Write(writer, (T)value!);
    }

    /// <inheritdoc />
    public sealed override object? ReadAsObject(TomlReader reader)
    {
        ArgumentGuard.ThrowIfNull(reader, nameof(reader));
        return Read(reader);
    }
}
