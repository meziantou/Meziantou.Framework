namespace Meziantou.Framework.CodeDom
{
    public class ExpressionStatement : Statement
    {
        private Expression? _expression;

        public ExpressionStatement()
        {
        }

        public ExpressionStatement(Expression? expression)
        {
            Expression = expression;
        }

        public Expression? Expression
        {
            get => _expression;
            set => SetParent(ref _expression, value);
        }
    }
}
