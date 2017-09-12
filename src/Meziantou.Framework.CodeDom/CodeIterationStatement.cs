namespace Meziantou.Framework.CodeDom
{
    public class CodeIterationStatement : CodeStatement
    {
        private CodeStatement _initialization;
        private CodeExpression _condition;
        private CodeStatement _incrementStatement;
        private CodeStatementCollection _body;

        public CodeStatement Initialization
        {
            get { return _initialization; }
            set { _initialization = SetParent(value); }
        }

        public CodeStatement IncrementStatement
        {
            get { return _incrementStatement; }
            set { _incrementStatement = SetParent(value); }
        }

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
