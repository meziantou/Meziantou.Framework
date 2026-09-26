using System;
using System.Collections.Generic;
using System.Globalization;
using Tomlyn.Model;
using Tomlyn.Syntax;
using Tomlyn.Text;

namespace Tomlyn.Serialization.Converters;

internal sealed class TomlEnumConverter : TomlConverter
{
    public static TomlEnumConverter Instance { get; } = new();

    public override bool CanConvert(Type typeToConvert) => typeToConvert.IsEnum;

    public override object? Read(TomlReader reader, Type typeToConvert)
    {
        if (!typeToConvert.IsEnum)
        {
            throw new InvalidOperationException($"Type '{typeToConvert.FullName}' is not an enum.");
        }

        if (reader.TokenType == TomlTokenType.Integer)
        {
            var raw = reader.GetInt64();
            reader.Read();
            return Enum.ToObject(typeToConvert, raw);
        }

        if (reader.TokenType == TomlTokenType.String)
        {
            var name = reader.GetString();
            try
            {
                var parsed = Enum.Parse(typeToConvert, name, ignoreCase: true);
                reader.Read();
                return parsed;
            }
            catch (ArgumentException)
            {
                throw reader.CreateException($"Invalid enum name `{name}` for type '{typeToConvert.FullName}'.");
            }
        }

        throw reader.CreateException($"Expected {TomlTokenType.Integer} or {TomlTokenType.String} token but was {reader.TokenType}.");
    }

    public override void Write(TomlWriter writer, object? value)
    {
        if (value is null)
        {
            throw new TomlException("TOML does not support null values.");
        }

        var type = value.GetType();
        if (!type.IsEnum)
        {
            throw new TomlException($"Expected an enum value but was '{type.FullName}'.");
        }

        var underlying = Enum.GetUnderlyingType(type);
        try
        {
            if (underlying == typeof(ulong) || underlying == typeof(uint) || underlying == typeof(ushort) || underlying == typeof(byte) ||
                underlying == typeof(nuint))
            {
                var raw = Convert.ToUInt64(value, CultureInfo.InvariantCulture);
                if (raw > long.MaxValue)
                {
                    throw new TomlException($"TOML integers are limited to signed 64-bit. Value {raw} cannot be written.");
                }

                writer.WriteIntegerValue(unchecked((long)raw));
                return;
            }

            var signed = Convert.ToInt64(value, CultureInfo.InvariantCulture);
            writer.WriteIntegerValue(signed);
        }
        catch (Exception ex) when (ex is InvalidCastException or OverflowException or FormatException)
        {
            throw new TomlException($"Enum value '{value}' cannot be written as a TOML integer.", ex);
        }
    }
}
