namespace Meziantou.Framework.CodeDom
{
    public class CodeUnaryExpression : CodeExpression
    {
        private CodeExpression _expression;

        public CodeUnaryExpression()
        {
        }

        public CodeUnaryExpression(UnaryOperator op, CodeExpression expression)
        {
            Operator = op;
            Expression = Expression;
        }

        public UnaryOperator Operator { get; set; }

        public CodeExpression Expression
        {
            get { return _expression; }
            set { _expression = SetParent(value); }
        }
    }
}