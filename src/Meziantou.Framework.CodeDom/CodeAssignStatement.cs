namespace Meziantou.Framework.CodeDom
{
    public class CodeAssignStatement : CodeStatement
    {
        private CodeExpression _leftExpression;
        private CodeExpression _rightExpression;

        public CodeAssignStatement()
        {
        }

        public CodeAssignStatement(CodeExpression leftExpression, CodeExpression rightExpression)
        {
            LeftExpression = leftExpression;
            RightExpression = rightExpression;
        }

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