using System;
using System.Collections.Generic;
using System.Globalization;
using Tomlyn.Model;
using Tomlyn.Syntax;
using Tomlyn.Text;

namespace Tomlyn.Serialization.Converters;

internal sealed class TomlNIntConverter : TomlConverter<nint>
{
    public static TomlNIntConverter Instance { get; } = new();

    public override nint Read(TomlReader reader)
    {
        if (reader.TokenType != TomlTokenType.Integer)
        {
            throw reader.CreateException($"Expected {TomlTokenType.Integer} token but was {reader.TokenType}.");
        }

        var raw = reader.GetInt64();
        if (IntPtr.Size == 4 && (raw < int.MinValue || raw > int.MaxValue))
        {
            throw reader.CreateException($"TOML integer value {raw} is out of range.");
        }

        reader.Read();
        return unchecked((nint)raw);
    }

    public override void Write(TomlWriter writer, nint value) => writer.WriteIntegerValue(value);
}
