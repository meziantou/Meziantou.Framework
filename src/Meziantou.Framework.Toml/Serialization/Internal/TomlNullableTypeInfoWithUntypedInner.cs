using System;

namespace Meziantou.Framework.Toml.Serialization.Internal;

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
        var value = _inner.ReadAsObject(reader);
        return value is null ? null : (T)value;
    }

    public override object? ReadInto(TomlReader reader, object? existingValue)
    {
        ArgumentNullException.ThrowIfNull(reader);
        return _inner.ReadInto(reader, existingValue);
    }
}
