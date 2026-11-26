using System.Buffers;
using System.Text.Unicode;

namespace Meziantou.Framework.Json.Internals;

/// <summary>A primitive value that is not part of the document: a literal, or the result of a function.</summary>
internal readonly struct ScalarValue
{
    private ScalarValue(JsonPathNodeKind kind, string? stringValue, double numberValue, bool booleanValue, byte[]? utf8Text = null)
    {
        Kind = kind;
        StringValue = stringValue;
        NumberValue = numberValue;
        BooleanValue = booleanValue;
        Utf8Text = utf8Text;
    }

    public JsonPathNodeKind Kind { get; }

    public string? StringValue { get; }

    public double NumberValue { get; }

    /// <summary>
    /// Gets the UTF-8 text of a literal: the text of a number, or <see langword="null"/> when <see cref="NumberValue"/> is
    /// exact; the value of a string, or <see langword="null"/> when the string is not a literal.
    /// </summary>
    public byte[]? Utf8Text { get; }

    public bool BooleanValue { get; }

    public static ScalarValue Null() => new(JsonPathNodeKind.Null, stringValue: null, numberValue: 0, booleanValue: false);

    public static ScalarValue FromBoolean(bool value) => new(JsonPathNodeKind.Boolean, stringValue: null, numberValue: 0, booleanValue: value);

    public static ScalarValue FromNumber(double value) => new(JsonPathNodeKind.Number, stringValue: null, numberValue: value, booleanValue: false);

    public static ScalarValue FromNumber(NumberLiteral value) => new(JsonPathNodeKind.Number, stringValue: null, numberValue: value.Value, booleanValue: false, value.Utf8Text);

    public static ScalarValue FromString(string value) => new(JsonPathNodeKind.String, value, numberValue: 0, booleanValue: false);

    /// <summary>Converts a literal of a filter expression.</summary>
    /// <param name="value">The literal: <see langword="null"/>, a <see cref="bool"/>, a <see cref="string"/> or a <see cref="NumberLiteral"/>.</param>
    /// <param name="result">The value of the literal.</param>
    /// <returns><see langword="false"/> when <paramref name="value"/> is not a literal.</returns>
    public static bool TryFromLiteral(object? value, out ScalarValue result)
    {
        switch (value)
        {
            case null:
                result = Null();
                return true;
            case bool boolean:
                result = FromBoolean(boolean);
                return true;
            case string text:
                result = new(JsonPathNodeKind.String, text, numberValue: 0, booleanValue: false, GetUtf8(text));
                return true;
            case NumberLiteral number:
                result = FromNumber(number);
                return true;
            default:
                result = default;
                return false;
        }
    }

    /// <summary>Gets the UTF-8 form of a string, or <see langword="null"/> when it has an unpaired surrogate and has no UTF-8 form.</summary>
    private static byte[]? GetUtf8(string text)
    {
        var buffer = new byte[Encoding.UTF8.GetMaxByteCount(text.Length)];
        if (Utf8.FromUtf16(text, buffer, out _, out var written, replaceInvalidSequences: false) is not OperationStatus.Done)
            return null;

        return buffer.AsSpan(0, written).ToArray();
    }
}
