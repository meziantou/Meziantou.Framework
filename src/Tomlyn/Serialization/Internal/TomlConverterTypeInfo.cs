using System;
using Tomlyn.Helpers;

namespace Tomlyn.Serialization.Internal;

internal sealed class TomlConverterTypeInfo<T> : TomlTypeInfo<T>
{
    private readonly TomlConverter<T> _converter;
    private readonly bool _writesTable;

    public TomlConverterTypeInfo(TomlSerializerOptions options, TomlConverter<T> converter, bool writesTable = false)
        : base(options)
    {
        _converter = converter ?? throw new ArgumentNullException(nameof(converter));
        _writesTable = writesTable;
    }

    public override bool WritesTable => _writesTable;

    public override void Write(TomlWriter writer, T value)
    {
        ArgumentGuard.ThrowIfNull(writer, nameof(writer));
        _converter.Write(writer, value);
    }

    public override T? Read(TomlReader reader)
    {
        ArgumentGuard.ThrowIfNull(reader, nameof(reader));
        return TomlConverterHelper.Read(reader, _converter);
    }
}
