// Copyright (c) Alexandre Mutel. All rights reserved.
// Licensed under the BSD-Clause 2 license.
// See license.txt file in the project root for full license information.

using System;
using System.Text.Json.Serialization;
using Tomlyn.Helpers;

namespace Tomlyn.Serialization;

/// <summary>
/// Specifies a root type to include in a source-generated <see cref="TomlSerializerContext"/>.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public sealed class TomlSerializableAttribute : TomlAttribute
{
    /// <summary>
    /// Initializes a new instance of the <see cref="TomlSerializableAttribute"/> class.
    /// </summary>
    /// <param name="type">The root type to include in the generated context.</param>
    public TomlSerializableAttribute(Type type)
    {
        ArgumentGuard.ThrowIfNull(type, nameof(type));
        Type = type;
    }

    /// <summary>
    /// Gets the root type included in the generated context.
    /// </summary>
    public Type Type { get; }

    /// <summary>
    /// Gets or sets the generated <see cref="Tomlyn.TomlTypeInfo"/> property name for this root type.
    /// </summary>
    public string? TypeInfoPropertyName { get; set; }

    /// <summary>
    /// Gets or sets the requested source generation mode for this root type.
    /// </summary>
    /// <remarks>
    /// Tomlyn source generation always emits the metadata needed for TOML serialization and deserialization.
    /// </remarks>
    public JsonSourceGenerationMode GenerationMode { get; set; }
}
