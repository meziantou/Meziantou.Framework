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
            get => _initialization;
            set => SetParent(ref _initialization, value);
        }

        public CodeStatement IncrementStatement
        {
            get => _incrementStatement;
            set => SetParent(ref _incrementStatement, value);
        }

        public CodeExpression Condition
        {
            get => _condition;
            set => SetParent(ref _condition, value);
        }

        public CodeStatementCollection Body
        {
            get => _body;
            set => SetParent(ref _body, value);
        }
    }
}
