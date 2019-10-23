namespace Meziantou.Framework.CodeDom
{
    public class PropertyAccessorDeclaration : CodeObject
    {
        private StatementCollection? _statements;

        public PropertyAccessorDeclaration()
            : this(statements: null)
        {
        }

        public PropertyAccessorDeclaration(StatementCollection? statements)
        {
            Statements = statements ?? new StatementCollection();
            CustomAttributes = new CodeObjectCollection<CustomAttribute>(this);
        }

        public Modifiers Modifiers { get; set; }
        public CodeObjectCollection<CustomAttribute> CustomAttributes { get; }

        public StatementCollection? Statements
        {
            get => _statements;
            set => SetParent(ref _statements, value);
        }

        public static implicit operator PropertyAccessorDeclaration(StatementCollection statements) => new PropertyAccessorDeclaration(statements);

        public static implicit operator PropertyAccessorDeclaration(Statement statement) => new PropertyAccessorDeclaration { Statements = statement };
    }
}
