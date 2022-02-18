namespace Meziantou.Framework.CodeDom;

public class ReturnStatement : Statement
{
    private Expression? _expression;

    public ReturnStatement()
    {
    }

    public ReturnStatement(Expression? expression)
    {
        Expression = expression;
    }

    public Expression? Expression
    {
        get => _expression;
        set => SetParent(ref _expression, value);
    }
}
