using System;
using Tomlyn.Helpers;

namespace Tomlyn.Serialization.Internal;

internal sealed class TomlUntypedNullableTypeInfo : TomlTypeInfo
{
    private readonly TomlTypeInfo _inner;

    public TomlUntypedNullableTypeInfo(Type nullableType, TomlSerializerOptions options, TomlTypeInfo inner)
        : base(nullableType, options)
    {
        ArgumentGuard.ThrowIfNull(inner, nameof(inner));
        _inner = inner;
    }

    public override void Write(TomlWriter writer, object? value)
    {
        ArgumentGuard.ThrowIfNull(writer, nameof(writer));

        if (value is null)
        {
            throw new TomlException("TOML does not support null values.");
        }

        _inner.Write(writer, value);
    }

    public override object? ReadAsObject(TomlReader reader)
    {
        ArgumentGuard.ThrowIfNull(reader, nameof(reader));
        return _inner.ReadAsObject(reader);
    }

    public override object? ReadInto(TomlReader reader, object? existingValue)
    {
        ArgumentGuard.ThrowIfNull(reader, nameof(reader));

        if (existingValue is not null)
        {
            return _inner.ReadInto(reader, existingValue);
        }

        return ReadAsObject(reader);
    }
}
