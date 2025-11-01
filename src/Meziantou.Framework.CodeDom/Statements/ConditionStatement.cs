namespace Meziantou.Framework.CodeDom;

/// <summary>Represents a conditional statement (if-else).</summary>
public class ConditionStatement : Statement
{
    public Expression? Condition
    {
        get;
        set => SetParent(ref field, value);
    }

    public StatementCollection? TrueStatements
    {
        get;
        set => SetParent(ref field, value);
    }

    public StatementCollection? FalseStatements
    {
        get;
        set => SetParent(ref field, value);
    }

    public static ConditionStatement CreateIfNotNull(Expression leftExpression)
    {
        var condition = new ConditionStatement
        {
            Condition = new BinaryExpression(BinaryOperator.NotEquals, leftExpression, new LiteralExpression(value: null)),
        };
        return condition;
    }
}
