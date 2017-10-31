namespace Meziantou.Framework.CodeDom
{
    public abstract class CodeEventHandlerStatement : CodeStatement
    {
        private CodeExpression _leftExpression;
        private CodeExpression _rightExpression;

        protected CodeEventHandlerStatement()
        {
        }

        protected CodeEventHandlerStatement(CodeExpression leftExpression, CodeExpression rightExpression)
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