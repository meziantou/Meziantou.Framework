namespace Meziantou.Framework.CodeDom
{
    public class CodeBinaryExpression : CodeExpression
    {
        private CodeExpression _rightExpression;
        private CodeExpression _leftExpression;

        public CodeBinaryExpression()
        {
        }

        public CodeBinaryExpression(BinaryOperator op, CodeExpression leftExpression, CodeExpression rightExpression)
        {
            Operator = op;
            LeftExpression = leftExpression;
            RightExpression = rightExpression;
        }

        public BinaryOperator Operator { get; set; }

        public CodeExpression LeftExpression
        {
            get { return _leftExpression; }
            set { _leftExpression = SetParent(value); }
        }

        public CodeExpression RightExpression
        {
            get { return _rightExpression; }
            set { _rightExpression = SetParent(value); }
        }
    }
}