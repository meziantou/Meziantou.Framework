namespace Meziantou.Framework.CodeDom
{
    public class CodeWhileStatement : CodeStatement
    {
        private CodeExpression _condition;
        private CodeStatementCollection _body;

        public CodeExpression Condition
        {
            get { return _condition; }
            set { _condition = SetParent(value); }
        }

        public CodeStatementCollection Body
        {
            get => _body;
            set => _body = SetParent(value);
        }
    }
}
