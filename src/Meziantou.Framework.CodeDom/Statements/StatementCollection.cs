namespace Meziantou.Framework.CodeDom;

/// <summary>Represents a collection of statements.</summary>
public class StatementCollection : CodeObjectCollection<Statement>
{
    public StatementCollection()
    {
    }

    public StatementCollection(CodeObject parent) : base(parent)
    {
    }

    // Seems to help the compiler
    public new TCodeObject Add<TCodeObject>(TCodeObject item)
        where TCodeObject : Statement
    {
        return base.Add(item);
    }

    public Expression Add(Expression expression)
    {
        Add(new ExpressionStatement(expression));
        return expression;
    }

    public static implicit operator StatementCollection(Statement codeStatement) => [codeStatement];

    public static implicit operator StatementCollection(Expression codeExpression) => [new ExpressionStatement(codeExpression)];
}
