namespace Meziantou.Framework.CodeDom;

public class TryCatchFinallyStatement : Statement
{
    public StatementCollection? Try
    {
        get;
        set => SetParent(ref field, value);
    }

    public CatchClauseCollection? Catch
    {
        get;
        set => SetParent(ref field, value);
    }

    public StatementCollection? Finally
    {
        get;
        set => SetParent(ref field, value);
    }
}
