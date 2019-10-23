namespace Meziantou.Framework.CodeDom
{
    public class YieldReturnStatement : Statement
    {
        private Expression? _expression;

        public YieldReturnStatement()
        {
        }

        public YieldReturnStatement(Expression? expression)
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
