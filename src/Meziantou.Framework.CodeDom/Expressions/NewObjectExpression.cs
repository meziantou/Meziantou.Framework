namespace Meziantou.Framework.CodeDom;

/// <summary>Represents an object instantiation expression using the new keyword.</summary>
/// <example>
/// <code xmlns:ref="urn:meziantou:file-referencer" ref:source="../../../tests/Meziantou.Framework.CodeDom.Tests/CSharpCodeGeneratorTests.cs" ref:region="NewObjectExpression_Example" ref:dedent="true">
/// var newObj = new NewObjectExpression(typeof(StringBuilder));
/// var newObjWithArgs = new NewObjectExpression(typeof(StringBuilder), new LiteralExpression("initial value"));
///
/// Assert.Equal("new global::System.Text.StringBuilder()", newObj.ToCsharpString());
/// Assert.Equal("new global::System.Text.StringBuilder(\"initial value\")", newObjWithArgs.ToCsharpString());
/// </code>
/// </example>
public class NewObjectExpression : Expression
{
    public NewObjectExpression()
    {
        Arguments = new CodeObjectCollection<Expression>(this);
    }

    /// <summary>Initializes a new instance of the <see cref="NewObjectExpression"/> class with the specified type and constructor arguments.</summary>
    /// <param name="type">The type to instantiate.</param>
    /// <param name="arguments">The constructor arguments.</param>
    public NewObjectExpression(TypeReference? type, params Expression[] arguments)
    {
        Arguments = new CodeObjectCollection<Expression>(this);
        Type = type;
        foreach (var argument in arguments)
        {
            Arguments.Add(argument);
        }
    }

    public TypeReference? Type { get; set; }

    public CodeObjectCollection<Expression> Arguments { get; }
}
