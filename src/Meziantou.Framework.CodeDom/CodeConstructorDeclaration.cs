namespace Meziantou.Framework.CodeDom
{
    public class CodeConstructorDeclaration : CodeMemberDeclaration
    {
        private CodeStatementCollection _statements;

        public CodeConstructorDeclaration()
        {
            Arguments = new CodeObjectCollection<CodeMethodArgumentDeclaration>(this);
        }

        public CodeObjectCollection<CodeMethodArgumentDeclaration> Arguments { get; }

        public CodeStatementCollection Statements
        {
            get { return _statements; }
            set { _statements = SetParent(value); }
        }

        public Modifiers Modifiers { get; set; }

        public CodeTypeDeclaration ParentType => Parent.GetSelfOrParentOfType<CodeTypeDeclaration>();
    }
}