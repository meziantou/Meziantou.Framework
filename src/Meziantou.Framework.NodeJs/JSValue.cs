using System.Globalization;
using System.Numerics;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Meziantou.Framework.NodeJs.Internal;

namespace Meziantou.Framework.NodeJs;

/// <summary>A JavaScript value that JSON cannot represent, such as <c>undefined</c> or a <c>BigInt</c>, passed as an argument of a call.</summary>
/// <remarks>
/// <para>A <see cref="JSValue"/> converts implicitly to a <see cref="JsonNode"/>, so it can be used as an argument, or nested in a <see cref="JsonObject"/> or a <see cref="JsonArray"/> argument.</para>
/// <para>It cannot be serialized in any other way: the resulting <see cref="JsonNode"/> cannot be written as JSON, and a <see cref="JSValue"/> cannot be a member of an object serialized using reflection.</para>
/// </remarks>
/// <example>
/// <code>
/// await node.InvokeAsync("./module.mjs", "run", [JSValue.Undefined, JSValue.BigInt(long.MaxValue), new JsonObject { ["date"] = JSValue.Date(DateTimeOffset.UtcNow) }]);
/// </code>
/// </example>
[JsonConverter(typeof(NodeJsArgumentConverter<JSValue>))]
public sealed class JSValue
{
    private JSValue(JSValueKind kind, object? value)
    {
        Kind = kind;
        Value = value;
    }

    /// <summary>Gets the JavaScript <c>undefined</c> value.</summary>
    public static JSValue Undefined { get; } = new(JSValueKind.Undefined, value: null);

    internal JSValueKind Kind { get; }

    internal object? Value { get; }

    /// <summary>Creates a JavaScript <c>BigInt</c>.</summary>
    public static JSValue BigInt(BigInteger value) => new(JSValueKind.BigInt, value);

    /// <summary>Creates a JavaScript number. <see cref="double.NaN"/>, infinities, and negative zero are preserved.</summary>
    public static JSValue Number(double value) => new(JSValueKind.Number, value);

    /// <inheritdoc cref="Number(double)"/>
    public static JSValue Number(float value) => Number((double)value);

    /// <inheritdoc cref="Number(double)"/>
    public static JSValue Number(Half value) => Number((double)value);

    /// <summary>Creates a JavaScript number, which is the nearest double-precision value. Use <see cref="BigInt(BigInteger)"/> to preserve large integers exactly.</summary>
    public static JSValue Number(decimal value) => NumberLiteral(value.ToString(CultureInfo.InvariantCulture));

    /// <inheritdoc cref="Number(decimal)"/>
    public static JSValue Number(Int128 value) => NumberLiteral(value.ToString(CultureInfo.InvariantCulture));

    /// <inheritdoc cref="Number(decimal)"/>
    public static JSValue Number(UInt128 value) => NumberLiteral(value.ToString(CultureInfo.InvariantCulture));

    /// <inheritdoc cref="Number(decimal)"/>
    public static JSValue Number(long value) => NumberLiteral(value.ToString(CultureInfo.InvariantCulture));

    /// <inheritdoc cref="Number(decimal)"/>
    public static JSValue Number(ulong value) => NumberLiteral(value.ToString(CultureInfo.InvariantCulture));

    /// <summary>Creates a JavaScript number.</summary>
    public static JSValue Number(int value) => NumberLiteral(value.ToString(CultureInfo.InvariantCulture));

    /// <inheritdoc cref="Number(int)"/>
    public static JSValue Number(uint value) => NumberLiteral(value.ToString(CultureInfo.InvariantCulture));

    /// <inheritdoc cref="Number(int)"/>
    public static JSValue Number(short value) => NumberLiteral(value.ToString(CultureInfo.InvariantCulture));

    /// <inheritdoc cref="Number(int)"/>
    public static JSValue Number(ushort value) => NumberLiteral(value.ToString(CultureInfo.InvariantCulture));

    /// <inheritdoc cref="Number(int)"/>
    public static JSValue Number(sbyte value) => NumberLiteral(value.ToString(CultureInfo.InvariantCulture));

    /// <inheritdoc cref="Number(int)"/>
    public static JSValue Number(byte value) => NumberLiteral(value.ToString(CultureInfo.InvariantCulture));

    /// <summary>Creates a JavaScript <c>Date</c>. JavaScript dates have a precision of one millisecond, so the remaining ticks are truncated.</summary>
    public static JSValue Date(DateTimeOffset value) => new(JSValueKind.Date, value);

    /// <summary>Creates a JavaScript <c>Date</c>. JavaScript dates have a precision of one millisecond, so the remaining ticks are truncated.</summary>
    /// <param name="value">The date. A <see cref="DateTimeKind.Local"/> or <see cref="DateTimeKind.Unspecified"/> value is a local time, as for <see cref="DateTimeOffset(DateTime)"/>.</param>
    public static JSValue Date(DateTime value) => Date(new DateTimeOffset(value));

    /// <summary>Creates a JavaScript <c>Uint8Array</c> containing a copy of the data.</summary>
    /// <param name="value">The data. It is read when the call is sent, so it must not be modified before.</param>
    public static JSValue Uint8Array(ReadOnlyMemory<byte> value) => new(JSValueKind.Uint8Array, value);

    /// <summary>Converts the value to a <see cref="JsonNode"/>, to use it as an argument or in a <see cref="JsonObject"/> or <see cref="JsonArray"/> argument.</summary>
    public JsonNode ToJsonNode() => JsonValue.Create(this, NodeJsJsonSerializerContext.Default.JSValue)!;

    /// <summary>Converts the value to a <see cref="JsonNode"/>, to use it as an argument or in a <see cref="JsonObject"/> or <see cref="JsonArray"/> argument.</summary>
    public static implicit operator JsonNode?(JSValue? value) => value?.ToJsonNode();

    /// <summary>Returns the JavaScript representation of the value (e.g. <c>undefined</c>, <c>123n</c>, <c>NaN</c>).</summary>
    public override string ToString()
    {
        return Kind switch
        {
            JSValueKind.Undefined => "undefined",
            JSValueKind.BigInt => ((BigInteger)Value!).ToString(CultureInfo.InvariantCulture) + "n",
            JSValueKind.Number => FormatNumber((double)Value!),
            JSValueKind.NumberLiteral => (string)Value!,
            JSValueKind.Date => ((DateTimeOffset)Value!).UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture),
            JSValueKind.Uint8Array => "Uint8Array(" + ((ReadOnlyMemory<byte>)Value!).Length.ToString(CultureInfo.InvariantCulture) + ")",
            _ => Kind.ToString(),
        };
    }

    /// <summary>Gets the representation of a number that is not a finite JSON number (<c>NaN</c>, <c>Infinity</c>, <c>-Infinity</c>, or <c>-0</c>), or <see langword="null"/>.</summary>
    internal static string? GetSpecialNumberRepresentation(double value)
    {
        if (double.IsNaN(value))
            return "NaN";

        if (double.IsPositiveInfinity(value))
            return "Infinity";

        if (double.IsNegativeInfinity(value))
            return "-Infinity";

        if (value == 0 && double.IsNegative(value))
            return "-0";

        return null;
    }

    private static string FormatNumber(double value)
    {
        return GetSpecialNumberRepresentation(value) ?? value.ToString("R", CultureInfo.InvariantCulture);
    }

    private static JSValue NumberLiteral(string value) => new(JSValueKind.NumberLiteral, value);
}
