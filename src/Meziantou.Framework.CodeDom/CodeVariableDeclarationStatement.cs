namespace Meziantou.Framework.CodeDom
{
    public class CodeVariableDeclarationStatement : CodeStatement
    {
        private CodeExpression _initExpression;
        private CodeTypeReference _type;

        public CodeVariableDeclarationStatement()
        {
        }

        public CodeVariableDeclarationStatement(CodeTypeReference type, string name, CodeExpression initExpression = null)
        {
            Type = type;
            Name = name;
            InitExpression = initExpression;
        }

        public string Name { get; set; }

        public CodeTypeReference Type
        {
            get { return _type; }
            set { SetParent(ref _type, value); }
        }

        public CodeExpression InitExpression
        {
            get { return _initExpression; }
            set { SetParent(ref _initExpression, value); }
        }
    }
}