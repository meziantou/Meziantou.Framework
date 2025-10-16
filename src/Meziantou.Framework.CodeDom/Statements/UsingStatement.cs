namespace Meziantou.Framework.CodeDom;

public class UsingStatement : Statement
{
    public Statement? Statement
    {
        get;
        set => SetParent(ref field, value);
    }

    public StatementCollection? Body { get; set; }
}
