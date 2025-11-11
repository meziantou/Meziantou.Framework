namespace Meziantou.Framework.CodeDom;

/// <summary>Represents a foreach iteration statement.</summary>
public class IterationStatement : Statement
{
    public Statement? Initialization
    {
        get;
        set => SetParent(ref field, value);
    }

    public Statement? IncrementStatement
    {
        get;
        set => SetParent(ref field, value);
    }

    public Expression? Condition
    {
        get;
        set => SetParent(ref field, value);
    }

    public StatementCollection? Body
    {
        get;
        set => SetParent(ref field, value);
    }
}
