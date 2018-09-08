namespace Meziantou.Framework.CodeDom
{
    public class ConstructorDeclaration : MemberDeclaration
    {
        private StatementCollection _statements;
        private ConstructorInitializer _initializer;

        public ConstructorDeclaration()
        {
            Arguments = new CodeObjectCollection<MethodArgumentDeclaration>(this);
            Statements = new StatementCollection(this);
        }

        public CodeObjectCollection<MethodArgumentDeclaration> Arguments { get; }

        public StatementCollection Statements
        {
            get => _statements;
            set => SetParent(ref _statements, value);
        }

        public ConstructorInitializer Initializer
        {
            get => _initializer;
            set => SetParent(ref _initializer, value);
        }

        public Modifiers Modifiers { get; set; }

        public TypeDeclaration ParentType => Parent.GetSelfOrParentOfType<TypeDeclaration>();
    }
}