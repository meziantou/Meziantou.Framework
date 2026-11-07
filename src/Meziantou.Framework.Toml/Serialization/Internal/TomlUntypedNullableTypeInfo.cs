using System;

namespace Meziantou.Framework.Toml.Serialization.Internal;

internal sealed class TomlUntypedNullableTypeInfo : TomlTypeInfo
{
    private readonly TomlTypeInfo _inner;

    public TomlUntypedNullableTypeInfo(Type nullableType, TomlSerializerOptions options, TomlTypeInfo inner)
        : base(nullableType, options)
    {
        ArgumentNullException.ThrowIfNull(inner);
        _inner = inner;
    }

    public override void Write(TomlWriter writer, object? value)
    {
        ArgumentNullException.ThrowIfNull(writer);

        if (value is null)
        {
            throw new TomlException("TOML does not support null values.");
        }

        _inner.Write(writer, value);
    }

    public override object? ReadAsObject(TomlReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);
        return _inner.ReadAsObject(reader);
    }

    public override object? ReadInto(TomlReader reader, object? existingValue)
    {
        ArgumentNullException.ThrowIfNull(reader);

        if (existingValue is not null)
        {
            return _inner.ReadInto(reader, existingValue);
        }

        return ReadAsObject(reader);
    }
}
