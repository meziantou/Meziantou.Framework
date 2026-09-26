using System;
using Meziantou.Framework.Toml.Helpers;

namespace Meziantou.Framework.Toml.Serialization.Internal;

internal sealed class TomlUntypedConverterTypeInfo<T> : TomlTypeInfo<T>
{
    private readonly TomlConverter _converter;
    private readonly Type _typeToConvert;
    private readonly bool _writesTable;

    public TomlUntypedConverterTypeInfo(TomlSerializerOptions options, TomlConverter converter, bool writesTable = false)
        : base(options)
    {
        _converter = converter ?? throw new ArgumentNullException(nameof(converter));
        _typeToConvert = typeof(T);
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
        return (T)TomlConverterHelper.Read(reader, _converter, _typeToConvert)!;
    }
}
