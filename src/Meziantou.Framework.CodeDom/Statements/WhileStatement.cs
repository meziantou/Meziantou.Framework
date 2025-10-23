namespace Meziantou.Framework.CodeDom;

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
