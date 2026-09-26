// Copyright (c) Alexandre Mutel. All rights reserved.
// Licensed under the BSD-Clause 2 license.
// See license.txt file in the project root for full license information.

using System;
using Tomlyn.Helpers;

namespace Tomlyn.Serialization.Internal;

internal sealed class TomlNullableTypeInfoWithUntypedInner<T> : TomlTypeInfo<T?>
    where T : struct
{
    private readonly TomlTypeInfo _inner;

    public TomlNullableTypeInfoWithUntypedInner(TomlSerializerOptions options, TomlTypeInfo inner)
        : base(options)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
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
        var value = _inner.ReadAsObject(reader);
        return value is null ? null : (T)value;
    }

    public override object? ReadInto(TomlReader reader, object? existingValue)
    {
        ArgumentGuard.ThrowIfNull(reader, nameof(reader));
        return _inner.ReadInto(reader, existingValue);
    }
}
