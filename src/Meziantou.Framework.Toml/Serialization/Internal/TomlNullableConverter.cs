using System;

namespace Meziantou.Framework.Toml.Serialization.Internal;

// Like System.Text.Json, a converter for T also converts a T? member. TOML has no null, so a T? value is always a T.
internal sealed class TomlNullableConverter : TomlConverter
{
    private readonly TomlConverter _converter;
    private readonly Type _nullableType;
    private readonly Type _underlyingType;

    public TomlNullableConverter(TomlConverter converter, Type nullableType, Type underlyingType)
    {
        _converter = converter;
        _nullableType = nullableType;
        _underlyingType = underlyingType;
    }

    public override bool CanConvert(Type typeToConvert) => typeToConvert == _nullableType;

    // The errors of a user converter get its name, not the name of this converter
    public override object? Read(TomlReader reader, Type typeToConvert) => TomlConverterHelper.Read(reader, _converter, _underlyingType);

    public override void Write(TomlWriter writer, object? value)
    {
        if (value is null)
        {
            throw new TomlException("TOML does not support null values.");
        }

        _converter.Write(writer, value);
    }
}
