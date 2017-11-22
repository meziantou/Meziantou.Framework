namespace Meziantou.Framework.CodeDom
{
    public class VariableDeclarationStatement : Statement
    {
        private Expression _initExpression;
        private TypeReference _type;

        public VariableDeclarationStatement()
        {
        }

        public VariableDeclarationStatement(TypeReference type, string name, Expression initExpression = null)
        {
            Type = type;
            Name = name;
            InitExpression = initExpression;
        }

        public string Name { get; set; }

        public TypeReference Type
        {
            get => _type;
            set => SetParent(ref _type, value);
        }

        public Expression InitExpression
        {
            get => _initExpression;
            set => SetParent(ref _initExpression, value);
        }
    }
}