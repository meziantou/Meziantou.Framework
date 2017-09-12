namespace Meziantou.Framework.CodeDom
{
    public class CodeUnaryExpression : CodeExpression
    {
        private CodeExpression _rightExpression;
        private CodeExpression _leftExpression;

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
            get { return _leftExpression; }
            set { _leftExpression = SetParent(value); }
        }
    }
}