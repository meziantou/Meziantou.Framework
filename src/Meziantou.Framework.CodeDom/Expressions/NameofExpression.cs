namespace Meziantou.Framework.CodeDom
{
    public class NameofExpression : Expression
    {
        private Expression? _expression;

        public NameofExpression()
        {
        }

        public NameofExpression(Expression? expression)
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
