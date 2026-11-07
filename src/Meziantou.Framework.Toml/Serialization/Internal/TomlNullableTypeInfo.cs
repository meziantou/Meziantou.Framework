using System;

namespace Meziantou.Framework.Toml.Serialization.Internal;

internal sealed class TomlNullableTypeInfo<T> : TomlTypeInfo<T?>
    where T : struct
{
    private readonly TomlTypeInfo<T> _inner;

    public TomlNullableTypeInfo(TomlSerializerOptions options, TomlTypeInfo inner)
        : base(options)
    {
        ArgumentNullException.ThrowIfNull(inner);

        _inner = inner as TomlTypeInfo<T>
            ?? throw new TomlException($"The provided TOML metadata for type '{inner.Type.FullName}' cannot be used as metadata for '{typeof(T).FullName}'.");
    }

    public override void Write(TomlWriter writer, T? value)
    {
        ArgumentNullException.ThrowIfNull(writer);

        if (!value.HasValue)
        {
            throw new TomlException("TOML does not support null values.");
        }

        _inner.Write(writer, value.Value);
    }

    public override T? Read(TomlReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);
        return _inner.Read(reader);
    }

    public override object? ReadInto(TomlReader reader, object? existingValue)
    {
        ArgumentNullException.ThrowIfNull(reader);

        if (existingValue is T value)
        {
            return _inner.ReadInto(reader, value);
        }

        return Read(reader);
    }
}
