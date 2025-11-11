namespace Meziantou.Framework.CodeDom;

/// <summary>Represents a try-catch-finally statement for exception handling.</summary>
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
