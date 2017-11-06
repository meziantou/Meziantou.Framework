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
            get => _statements;
            set => SetParent(ref _statements, value);
        }

        public CodeConstructorInitializer Initializer
        {
            get => _initializer;
            set => SetParent(ref _initializer, value);
        }

        public Modifiers Modifiers { get; set; }

        public CodeTypeDeclaration ParentType => Parent.GetSelfOrParentOfType<CodeTypeDeclaration>();
    }
}