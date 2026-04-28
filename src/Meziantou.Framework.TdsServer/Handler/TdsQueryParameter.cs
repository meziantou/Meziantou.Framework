using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml.Linq;

namespace Meziantou.Framework.Tds.Handler;

/// <summary>Represents a decoded RPC parameter.</summary>
public sealed class TdsQueryParameter
{
    /// <summary>Gets or sets the parameter name.</summary>
    public required string Name { get; init; }

    /// <summary>Gets or sets the raw value decoded from the protocol payload. SQL NULL values use <see cref="DBNull.Value"/>.</summary>
    public required object Value { get; init; }

    /// <summary>Gets or sets the decoded parameter column type.</summary>
    public required TdsColumnType Type { get; init; }

    /// <summary>Gets a value indicating whether the parameter value is null.</summary>
    public bool IsNull => Value is null or DBNull;

    /// <summary>Converts the value to <see cref="string"/> when possible.</summary>
    public string? AsString()
    {
        return Value switch
        {
            null or DBNull => null,
            string text => text,
            byte[] bytes => Convert.ToBase64String(bytes),
            _ => Convert.ToString(Value, CultureInfo.InvariantCulture),
        };
    }

    /// <summary>Converts the value to <see cref="int"/> when possible.</summary>
    public int? AsInt32()
    {
        return Value switch
        {
            null or DBNull => null,
            byte typedValue => typedValue,
            sbyte typedValue => typedValue,
            short typedValue => typedValue,
            ushort typedValue => typedValue,
            int typedValue => typedValue,
            uint typedValue when typedValue <= int.MaxValue => (int)typedValue,
            long typedValue when typedValue >= int.MinValue && typedValue <= int.MaxValue => (int)typedValue,
            string text when int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var typedValue) => typedValue,
            _ => null,
        };
    }

    /// <summary>Converts the value to <see cref="long"/> when possible.</summary>
    public long? AsInt64()
    {
        return Value switch
        {
            null or DBNull => null,
            byte typedValue => typedValue,
            sbyte typedValue => typedValue,
            short typedValue => typedValue,
            ushort typedValue => typedValue,
            int typedValue => typedValue,
            uint typedValue => typedValue,
            long typedValue => typedValue,
            ulong typedValue when typedValue <= long.MaxValue => (long)typedValue,
            string text when long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var typedValue) => typedValue,
            _ => null,
        };
    }

    /// <summary>Converts the value to <see cref="bool"/> when possible.</summary>
    public bool? AsBoolean()
    {
        return Value switch
        {
            null or DBNull => null,
            bool typedValue => typedValue,
            byte typedValue => typedValue != 0,
            sbyte typedValue => typedValue != 0,
            short typedValue => typedValue != 0,
            ushort typedValue => typedValue != 0,
            int typedValue => typedValue != 0,
            uint typedValue => typedValue != 0,
            long typedValue => typedValue != 0,
            ulong typedValue => typedValue != 0,
            string text when bool.TryParse(text, out var typedValue) => typedValue,
            _ => null,
        };
    }

    /// <summary>Converts the value to <see cref="double"/> when possible.</summary>
    public double? AsDouble()
    {
        return Value switch
        {
            null or DBNull => null,
            float typedValue => typedValue,
            double typedValue => typedValue,
            decimal typedValue => (double)typedValue,
            byte typedValue => typedValue,
            sbyte typedValue => typedValue,
            short typedValue => typedValue,
            ushort typedValue => typedValue,
            int typedValue => typedValue,
            uint typedValue => typedValue,
            long typedValue => typedValue,
            ulong typedValue => typedValue,
            string text when double.TryParse(text, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out var typedValue) => typedValue,
            _ => null,
        };
    }

    /// <summary>Converts the value to <see cref="decimal"/> when possible.</summary>
    public decimal? AsDecimal()
    {
        return Value switch
        {
            null or DBNull => null,
            decimal typedValue => typedValue,
            byte typedValue => typedValue,
            sbyte typedValue => typedValue,
            short typedValue => typedValue,
            ushort typedValue => typedValue,
            int typedValue => typedValue,
            uint typedValue => typedValue,
            long typedValue => typedValue,
            ulong typedValue => typedValue,
            float typedValue => (decimal)typedValue,
            double typedValue => (decimal)typedValue,
            string text when decimal.TryParse(text, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out var typedValue) => typedValue,
            _ => null,
        };
    }

    /// <summary>Returns the value as binary payload when possible.</summary>
    public byte[]? AsBinary()
    {
        return Value switch
        {
            null or DBNull => null,
            byte[] typedValue => typedValue,
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

    /// <summary>Parses the value as XML when possible.</summary>
    public XDocument? AsXml()
    {
        var text = AsString();
        return text is null ? null : XDocument.Parse(text, LoadOptions.None);
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
