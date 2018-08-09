namespace Meziantou.Framework.CodeDom
{
    public class OperatorDeclaration : MemberDeclaration
    {
        private TypeReference _returnType;

        public TypeReference ReturnType
        {
            get => _returnType;
            set => SetParent(ref _returnType, value);
        }

        public CodeObjectCollection<MethodArgumentDeclaration> Arguments { get; }
        public StatementCollection Statements { get; }
        public Modifiers Modifiers { get; set; }

        public OperatorDeclaration()
            : this(null)
        {
        }

        public OperatorDeclaration(string name)
        {
            Statements = new StatementCollection(this);
            Arguments = new CodeObjectCollection<MethodArgumentDeclaration>(this);
            Name = name;
        }
    }
}
