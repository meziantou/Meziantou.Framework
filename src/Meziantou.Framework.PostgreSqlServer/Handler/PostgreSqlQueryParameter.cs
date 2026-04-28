using System.Globalization;
using System.Text.Json.Nodes;

namespace Meziantou.Framework.PostgreSql.Handler;

/// <summary>Represents a decoded PostgreSQL bound parameter with helper conversion methods.</summary>
public sealed class PostgreSqlQueryParameter
{
    /// <summary>Gets or sets the parameter name.</summary>
    public required string Name { get; init; }

    /// <summary>Gets or sets the raw value decoded from the protocol payload.</summary>
    public required object? Value { get; init; }

    /// <summary>Gets or sets the declared PostgreSQL column type.</summary>
    public required PostgreSqlColumnType Type { get; init; }

    /// <summary>Gets a value indicating whether the parameter value is null.</summary>
    public bool IsNull => Value is null or DBNull;

    /// <summary>Converts the value to <see cref="string"/> when possible.</summary>
    public string? AsString()
    {
        return Value switch
        {
            null or DBNull => null,
            string text => text,
            byte[] bytes => Convert.ToHexString(bytes),
            _ => Convert.ToString(Value, CultureInfo.InvariantCulture),
        };
    }

    /// <summary>Converts the value to <see cref="int"/> when possible.</summary>
    public int? AsInt32()
    {
        return Value switch
        {
            null or DBNull => null,
            byte value => value,
            sbyte value => value,
            short value => value,
            ushort value => value,
            int value => value,
            uint value when value <= int.MaxValue => (int)value,
            long value when value >= int.MinValue && value <= int.MaxValue => (int)value,
            string text when int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) => value,
            _ => null,
        };
    }

    /// <summary>Converts the value to <see cref="long"/> when possible.</summary>
    public long? AsInt64()
    {
        return Value switch
        {
            null or DBNull => null,
            byte value => value,
            sbyte value => value,
            short value => value,
            ushort value => value,
            int value => value,
            uint value => value,
            long value => value,
            ulong value when value <= long.MaxValue => (long)value,
            string text when long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) => value,
            _ => null,
        };
    }

    /// <summary>Converts the value to <see cref="bool"/> when possible.</summary>
    public bool? AsBoolean()
    {
        return Value switch
        {
            null or DBNull => null,
            bool value => value,
            byte value => value != 0,
            sbyte value => value != 0,
            short value => value != 0,
            ushort value => value != 0,
            int value => value != 0,
            uint value => value != 0,
            long value => value != 0,
            ulong value => value != 0,
            string text when bool.TryParse(text, out var value) => value,
            "t" => true,
            "f" => false,
            _ => null,
        };
    }

    /// <summary>Converts the value to <see cref="double"/> when possible.</summary>
    public double? AsDouble()
    {
        return Value switch
        {
            null or DBNull => null,
            float value => value,
            double value => value,
            decimal value => (double)value,
            byte value => value,
            sbyte value => value,
            short value => value,
            ushort value => value,
            int value => value,
            uint value => value,
            long value => value,
            ulong value => value,
            string text when double.TryParse(text, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out var value) => value,
            _ => null,
        };
    }

    /// <summary>Converts the value to <see cref="decimal"/> when possible.</summary>
    public decimal? AsDecimal()
    {
        return Value switch
        {
            null or DBNull => null,
            decimal value => value,
            byte value => value,
            sbyte value => value,
            short value => value,
            ushort value => value,
            int value => value,
            uint value => value,
            long value => value,
            ulong value => value,
            float value => (decimal)value,
            double value => (decimal)value,
            string text when decimal.TryParse(text, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out var value) => value,
            _ => null,
        };
    }

    /// <summary>Returns the value as binary payload when possible.</summary>
    public byte[]? AsBinary()
    {
        return Value switch
        {
            null or DBNull => null,
            byte[] value => value,
            _ => null,
        };
    }

    /// <summary>Parses the value as JSON when possible.</summary>
    public JsonObject? AsJson()
    {
        return Value switch
        {
            null or DBNull => null,
            JsonObject jsonObject => jsonObject,
            JsonNode jsonNode => jsonNode as JsonObject ?? throw new FormatException("JSON value is not an object."),
            string text => ParseJsonObject(text),
            byte[] bytes => ParseJsonObject(bytes),
            _ => ParseJsonObject(AsString() ?? throw new FormatException("Parameter value cannot be converted to JSON.")),
        };
    }

    private static JsonObject ParseJsonObject(string value)
    {
        var jsonNode = JsonNode.Parse(value);
        return jsonNode as JsonObject ?? throw new FormatException("JSON value is not an object.");
    }

    private static JsonObject ParseJsonObject(byte[] value)
    {
        var jsonNode = JsonNode.Parse(value);
        return jsonNode as JsonObject ?? throw new FormatException("JSON value is not an object.");
    }
}
