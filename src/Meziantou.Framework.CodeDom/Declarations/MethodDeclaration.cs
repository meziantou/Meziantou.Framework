namespace Meziantou.Framework.CodeDom
{
    public class MethodDeclaration : MemberDeclaration, IParametrableType, IModifiers
    {
        private TypeReference _returnType;
        private TypeReference _privateImplementationType;

        public TypeReference ReturnType
        {
            get => _returnType;
            set => SetParent(ref _returnType, value);
        }

        public TypeReference PrivateImplementationType
        {
            get => _privateImplementationType;
            set => SetParent(ref _privateImplementationType, value);
        }

        public CodeObjectCollection<TypeParameter> Parameters { get; }
        public CodeObjectCollection<MethodArgumentDeclaration> Arguments { get; }
        public StatementCollection Statements { get; set; }
        public Modifiers Modifiers { get; set; }

        public MethodDeclaration()
            : this(name: null)
        {
        }

        public MethodDeclaration(string name)
        {
            Arguments = new CodeObjectCollection<MethodArgumentDeclaration>(this);
            Parameters = new CodeObjectCollection<TypeParameter>(this);
            Name = name;
        }
    }
}
