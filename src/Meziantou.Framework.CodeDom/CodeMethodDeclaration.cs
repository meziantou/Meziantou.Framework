namespace Meziantou.Framework.CodeDom
{
    public class CodeMethodDeclaration : CodeMemberDeclaration, IParametrableType
    {
        private CodeTypeReference _returnType;

        public CodeTypeReference ReturnType
        {
            get { return _returnType; }
            set { _returnType = SetParent(value); }
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