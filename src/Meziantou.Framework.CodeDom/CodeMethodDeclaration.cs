namespace Meziantou.Framework.CodeDom
{
    public class CodeMethodDeclaration : CodeMemberDeclaration, IParametrableType
    {
        private CodeTypeReference _returnType;
        private CodeTypeReference _privateImplementationType;

        public CodeTypeReference ReturnType
        {
            get { return _returnType; }
            set { _returnType = SetParent(value); }
        }

        public CodeTypeReference PrivateImplementationType
        {
            get { return _privateImplementationType; }
            set { _privateImplementationType = SetParent(value); }
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