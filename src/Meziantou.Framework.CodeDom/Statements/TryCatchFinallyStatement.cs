namespace Meziantou.Framework.CodeDom;

public class TryCatchFinallyStatement : Statement
{
    private StatementCollection? _try;
    private CatchClauseCollection? _catch;
    private StatementCollection? _finally;

    public StatementCollection? Try
    {
        get => _try;
        set => SetParent(ref _try, value);
    }

    public CatchClauseCollection? Catch
    {
        get => _catch;
        set => SetParent(ref _catch, value);
    }

    public StatementCollection? Finally
    {
        get => _finally;
        set => SetParent(ref _finally, value);
    }
}
