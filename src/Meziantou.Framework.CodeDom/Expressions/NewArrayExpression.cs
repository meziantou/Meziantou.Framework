namespace Meziantou.Framework.CodeDom;

/// <summary>Represents an array creation expression (new T[]).</summary>
public class NewArrayExpression : Expression
{
    public NewArrayExpression()
    {
        Arguments = new CodeObjectCollection<Expression>(this);
    }

    public NewArrayExpression(TypeReference? type, params Expression[] arguments)
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
