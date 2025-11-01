namespace Meziantou.Framework.CodeDom;

/// <summary>Represents an object instantiation expression using the new keyword.</summary>
/// <example>
/// <code>
/// var newObj = new NewObjectExpression(typeof(StringBuilder));
/// var newObjWithArgs = new NewObjectExpression(typeof(StringBuilder), new LiteralExpression("initial value"));
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
