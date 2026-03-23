namespace Meziantou.Framework.Json.Internals;

internal readonly struct JsonPathToken
{
    public JsonPathToken(JsonPathTokenKind kind, int position)
    {
        Kind = kind;
        Position = position;
    }

    public JsonPathToken(JsonPathTokenKind kind, int position, string? stringValue)
    {
        Kind = kind;
        Position = position;
        StringValue = stringValue;
    }

    public JsonPathToken(JsonPathTokenKind kind, int position, double numberValue, bool isIntegerLiteral)
    {
        Kind = kind;
        Position = position;
        NumberValue = numberValue;
        IsIntegerLiteral = isIntegerLiteral;
    }

    public JsonPathTokenKind Kind { get; }

    public int Position { get; }

    public string? StringValue { get; }

    public double NumberValue { get; }

    /// <summary>
    /// Indicates whether this number literal was lexed as a pure integer
    /// (no decimal point, no exponent, no leading negative zero).
    /// </summary>
    public bool IsIntegerLiteral { get; }
}
