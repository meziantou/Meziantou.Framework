#nullable disable
namespace Meziantou.Framework.CodeDom
{
    public class ConditionStatement : Statement
    {
        private Expression _condition;
        private StatementCollection _trueStatements;
        private StatementCollection _falseStatements;

        public Expression Condition
        {
            get => _condition;
            set => SetParent(ref _condition, value);
        }

        public StatementCollection TrueStatements
        {
            get => _trueStatements;
            set => SetParent(ref _trueStatements, value);
        }

        public StatementCollection FalseStatements
        {
            get => _falseStatements;
            set => SetParent(ref _falseStatements, value);
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
}
