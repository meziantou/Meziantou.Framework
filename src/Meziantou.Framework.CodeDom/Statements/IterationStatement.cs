namespace Meziantou.Framework.CodeDom;

public class IterationStatement : Statement
{
    private Statement? _initialization;
    private Expression? _condition;
    private Statement? _incrementStatement;
    private StatementCollection? _body;

    public Statement? Initialization
    {
        get => _initialization;
        set => SetParent(ref _initialization, value);
    }

    public Statement? IncrementStatement
    {
        get => _incrementStatement;
        set => SetParent(ref _incrementStatement, value);
    }

    public Expression? Condition
    {
        get => _condition;
        set => SetParent(ref _condition, value);
    }

    public StatementCollection? Body
    {
        get => _body;
        set => SetParent(ref _body, value);
    }
}
