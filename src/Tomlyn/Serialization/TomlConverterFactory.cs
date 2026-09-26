// Copyright (c) Alexandre Mutel. All rights reserved.
// Licensed under the BSD-Clause 2 license.
// See license.txt file in the project root for full license information.

using System;
using Tomlyn;

namespace Tomlyn.Serialization;

/// <summary>
/// Produces <see cref="TomlConverter"/> instances for a family of types.
/// </summary>
public abstract class TomlConverterFactory : TomlConverter
{
    /// <summary>
    /// Creates a converter for <paramref name="typeToConvert"/>.
    /// </summary>
    public abstract TomlConverter CreateConverter(Type typeToConvert, TomlSerializerOptions options);

    /// <inheritdoc />
    public sealed override object? Read(TomlReader reader, Type typeToConvert)
        => throw new InvalidOperationException("TomlConverterFactory instances must be expanded by the converter resolution pipeline.");

    /// <inheritdoc />
    public sealed override void Write(TomlWriter writer, object? value)
        => throw new InvalidOperationException("TomlConverterFactory instances must be expanded by the converter resolution pipeline.");
}
