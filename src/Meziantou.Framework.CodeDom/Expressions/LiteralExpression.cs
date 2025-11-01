namespace Meziantou.Framework.CodeDom;

/// <summary>Represents a literal value expression (numbers, strings, booleans, null, etc.).</summary>
/// <example>
/// <code>
/// var intLiteral = new LiteralExpression(42);
/// var stringLiteral = new LiteralExpression("Hello");
/// var boolLiteral = new LiteralExpression(true);
/// var nullLiteral = new LiteralExpression(null);
/// </code>
/// </example>
public class LiteralExpression : Expression
{
    public LiteralExpression()
    {
    }

    /// <summary>Initializes a new instance of the <see cref="LiteralExpression"/> class with the specified value.</summary>
    /// <param name="value">The literal value.</param>
    public LiteralExpression(object? value)
    {
        Value = value;
    }

    public object? Value { get; set; }
}
