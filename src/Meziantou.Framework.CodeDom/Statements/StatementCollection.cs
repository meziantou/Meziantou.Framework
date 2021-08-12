namespace Meziantou.Framework.CodeDom;

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
        return base.Add<TCodeObject>(item);
    }

    public Expression Add(Expression expression)
    {
        Add(new ExpressionStatement(expression));
        return expression;
    }

    public static implicit operator StatementCollection(Statement codeStatement) => new() { codeStatement };

    public static implicit operator StatementCollection(Expression codeExpression) => new() { new ExpressionStatement(codeExpression) };
}
