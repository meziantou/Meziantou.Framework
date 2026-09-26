using System;
using System.Collections.Generic;
using System.Globalization;
using Meziantou.Framework.Toml.Model;
using Meziantou.Framework.Toml.Syntax;
using Meziantou.Framework.Toml.Text;

namespace Meziantou.Framework.Toml.Serialization.Converters;

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
            if (!FitsUnderlyingType(typeToConvert, raw))
            {
                throw reader.CreateException($"The value {raw} is out of the range of the enum '{typeToConvert.FullName}'.");
            }

            reader.Read();
            return Enum.ToObject(typeToConvert, raw);
        }

        if (reader.TokenType == TomlTokenType.String)
        {
            // The exact name wins, so members that differ only by case are read as written
            var name = reader.GetString();
            if (!Enum.TryParse(typeToConvert, name, ignoreCase: false, out var parsed) && !Enum.TryParse(typeToConvert, name, ignoreCase: true, out parsed))
            {
                throw reader.CreateException($"Invalid enum name `{name.ToPrintableInputText()}` for type '{typeToConvert.FullName}'.");
            }

            reader.Read();
            return parsed;
        }

        throw reader.CreateException($"Expected {TomlTokenType.Integer} or {TomlTokenType.String} token but was {reader.TokenType}.");
    }

    // Enum.ToObject silently truncates a value that does not fit in the underlying type
    private static bool FitsUnderlyingType(Type enumType, long value)
    {
        return Type.GetTypeCode(Enum.GetUnderlyingType(enumType)) switch
        {
            TypeCode.SByte => value is >= sbyte.MinValue and <= sbyte.MaxValue,
            TypeCode.Byte => value is >= byte.MinValue and <= byte.MaxValue,
            TypeCode.Int16 => value is >= short.MinValue and <= short.MaxValue,
            TypeCode.UInt16 => value is >= ushort.MinValue and <= ushort.MaxValue,
            TypeCode.Int32 => value is >= int.MinValue and <= int.MaxValue,
            TypeCode.UInt32 => value is >= uint.MinValue and <= uint.MaxValue,
            TypeCode.UInt64 => value >= 0,
            _ => true,
        };
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
