namespace Meziantou.Framework.CodeDom;

/// <summary>Represents a literal value expression (numbers, strings, booleans, null, etc.).</summary>
/// <example>
/// <code xmlns:ref="urn:meziantou:file-referencer" ref:source="../../../tests/Meziantou.Framework.CodeDom.Tests/CSharpCodeGeneratorTests.cs" ref:region="LiteralExpression_Example" ref:dedent="true">
/// var intLiteral = new LiteralExpression(42);
/// var stringLiteral = new LiteralExpression("Hello");
/// var boolLiteral = new LiteralExpression(true);
/// var nullLiteral = new LiteralExpression(null);
///
/// Assert.Equal("42", intLiteral.ToCsharpString());
/// Assert.Equal("\"Hello\"", stringLiteral.ToCsharpString());
/// Assert.Equal("true", boolLiteral.ToCsharpString());
/// Assert.Equal("null", nullLiteral.ToCsharpString());
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
