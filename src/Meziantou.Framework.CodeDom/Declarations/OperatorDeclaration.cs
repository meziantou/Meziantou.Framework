namespace Meziantou.Framework.CodeDom
{
    public class OperatorDeclaration : MemberDeclaration, IModifiers
    {
        private TypeReference? _returnType;

        public TypeReference? ReturnType
        {
            get => _returnType;
            set => _returnType = value;
        }

        public CodeObjectCollection<MethodArgumentDeclaration> Arguments { get; }
        public StatementCollection Statements { get; }
        public Modifiers Modifiers { get; set; }

        public OperatorDeclaration()
            : this(name: null)
        {
        }

        public OperatorDeclaration(string? name)
        {
            Statements = new StatementCollection(this);
            Arguments = new CodeObjectCollection<MethodArgumentDeclaration>(this);
            Name = name;
        }
    }
}
