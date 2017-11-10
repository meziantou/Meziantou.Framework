namespace Meziantou.Framework.CodeDom
{
    public class ThrowStatement : Statement
    {
        private Expression _expression;

        public ThrowStatement()
        {
        }

        public ThrowStatement(Expression expression)
        {
            Expression = expression;
        }

        public Expression Expression
        {
            get { return _expression; }
            set { SetParent(ref _expression, value); }
        }
    }
}