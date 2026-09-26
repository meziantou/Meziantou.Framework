// Copyright (c) Alexandre Mutel. All rights reserved.
// Licensed under the BSD-Clause 2 license.
// See license.txt file in the project root for full license information.

using System;
using Tomlyn.Helpers;

namespace Tomlyn.Serialization.Internal;

internal sealed class TomlNullableTypeInfo<T> : TomlTypeInfo<T?>
    where T : struct
{
    private readonly TomlTypeInfo<T> _inner;

    public TomlNullableTypeInfo(TomlSerializerOptions options, TomlTypeInfo inner)
        : base(options)
    {
        ArgumentGuard.ThrowIfNull(inner, nameof(inner));

        _inner = inner as TomlTypeInfo<T>
            ?? throw new TomlException($"The provided TOML metadata for type '{inner.Type.FullName}' cannot be used as metadata for '{typeof(T).FullName}'.");
    }

    public override void Write(TomlWriter writer, T? value)
    {
        ArgumentGuard.ThrowIfNull(writer, nameof(writer));

        if (!value.HasValue)
        {
            throw new TomlException("TOML does not support null values.");
        }

        _inner.Write(writer, value.Value);
    }

    public override T? Read(TomlReader reader)
    {
        ArgumentGuard.ThrowIfNull(reader, nameof(reader));
        return _inner.Read(reader);
    }

    public override object? ReadInto(TomlReader reader, object? existingValue)
    {
        ArgumentGuard.ThrowIfNull(reader, nameof(reader));

        if (existingValue is T value)
        {
            return _inner.ReadInto(reader, value);
        }

        return Read(reader);
    }
}
