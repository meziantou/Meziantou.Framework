namespace Meziantou.Framework.CodeDom
{
    public class CodeConstructorDeclaration : CodeMemberDeclaration
    {
        private CodeStatementCollection _statements;
        private CodeConstructorInitializer _initializer;

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

        public CodeConstructorInitializer Initializer
        {
            get { return _initializer; }
            set { _initializer = SetParent(value); }
        }

        public Modifiers Modifiers { get; set; }

        public CodeTypeDeclaration ParentType => Parent.GetSelfOrParentOfType<CodeTypeDeclaration>();
    }
}