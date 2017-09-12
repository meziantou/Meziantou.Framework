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
            set { _type = SetParent(value); }
        }

        public CodeExpression InitExpression
        {
            get { return _initExpression; }
            set { _initExpression = SetParent(value); }
        }
    }
}