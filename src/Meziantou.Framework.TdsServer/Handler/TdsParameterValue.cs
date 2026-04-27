using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml.Linq;

namespace Meziantou.Framework.Tds.Handler;

/// <summary>Represents a decoded query parameter value with helper conversion methods.</summary>
public sealed class TdsParameterValue
{
    /// <summary>Initializes a new instance of <see cref="TdsParameterValue"/>.</summary>
    public TdsParameterValue(object? rawValue)
    {
        RawValue = rawValue;
    }

    /// <summary>Gets the raw value decoded from the protocol payload.</summary>
    public object? RawValue { get; }

    /// <summary>Gets a value indicating whether the parameter value is null.</summary>
    public bool IsNull => RawValue is null;

    /// <summary>Converts the value to <see cref="string"/> when possible.</summary>
    public string? AsString()
    {
        return RawValue switch
        {
            null => null,
            string text => text,
            byte[] bytes => Convert.ToBase64String(bytes),
            _ => Convert.ToString(RawValue, CultureInfo.InvariantCulture),
        };
    }

    /// <summary>Converts the value to <see cref="int"/> when possible.</summary>
    public int? AsInt32()
    {
        return RawValue switch
        {
            null => null,
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
        return RawValue switch
        {
            null => null,
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
        return RawValue switch
        {
            null => null,
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
            _ => null,
        };
    }

    /// <summary>Converts the value to <see cref="double"/> when possible.</summary>
    public double? AsDouble()
    {
        return RawValue switch
        {
            null => null,
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
        return RawValue switch
        {
            null => null,
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
        return RawValue switch
        {
            null => null,
            byte[] value => value,
            _ => null,
        };
    }

    /// <summary>Parses the value as JSON when possible.</summary>
    public JsonObject? AsJson()
    {
        return RawValue switch
        {
            null => null,
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
