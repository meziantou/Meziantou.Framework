namespace Meziantou.Framework.CodeDom
{
    public class CodeMethodDeclaration : CodeMemberDeclaration, IParametrableType
    {
        private CodeTypeReference _returnType;
        private CodeTypeReference _privateImplementationType;

        public CodeTypeReference ReturnType
        {
            get => _returnType;
            set => SetParent(ref _returnType, value);
        }

        public CodeTypeReference PrivateImplementationType
        {
            get => _privateImplementationType;
            set => SetParent(ref _privateImplementationType, value);
        }

        public CodeObjectCollection<CodeTypeParameter> Parameters { get; }
        public CodeObjectCollection<CodeMethodArgumentDeclaration> Arguments { get; }
        public CodeStatementCollection Statements { get; set; }
        public Modifiers Modifiers { get; set; }

        public CodeMethodDeclaration()
            : this(null)
        {
        }

        public CodeMethodDeclaration(string name)
        {
            Arguments = new CodeObjectCollection<CodeMethodArgumentDeclaration>(this);
            Parameters = new CodeObjectCollection<CodeTypeParameter>(this);
            Name = name;
        }
    }
}