namespace Meziantou.Framework.Json.Internals;

/// <summary>A number literal of a filter expression, with the text it was written with.</summary>
internal sealed class NumberLiteral
{
    public NumberLiteral(double value, string text)
    {
        Value = value;
        Utf8Text = Encoding.UTF8.GetBytes(text);
    }

    /// <summary>Gets the value rounded to the nearest <see cref="double"/>.</summary>
    public double Value { get; }

    /// <summary>Gets the literal as written, which keeps the digits that <see cref="Value"/> rounds away.</summary>
    public byte[] Utf8Text { get; }
}
