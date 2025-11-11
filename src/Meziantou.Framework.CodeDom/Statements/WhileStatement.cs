namespace Meziantou.Framework.CodeDom;

/// <summary>Represents a while loop statement.</summary>
public class WhileStatement : Statement
{
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
