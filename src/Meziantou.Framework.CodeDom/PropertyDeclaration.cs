namespace Meziantou.Framework.CodeDom
{
    public class PropertyMemberDeclaration : CodeObject
    {
        private StatementCollection _statements;

        public PropertyMemberDeclaration()
            : this(null)
        {
        }

        public PropertyMemberDeclaration(StatementCollection statements)
        {
            Statements = statements ?? new StatementCollection();
            CustomAttributes = new CodeObjectCollection<CustomAttribute>(this);
        }

        public Modifiers Modifiers { get; set; }
        public CodeObjectCollection<CustomAttribute> CustomAttributes { get; }

        public StatementCollection Statements
        {
            get => _statements;
            set => SetParent(ref _statements, value);
        }

        public static implicit operator PropertyMemberDeclaration(StatementCollection statements)
        {
            return new PropertyMemberDeclaration(statements);
        }

        public static implicit operator PropertyMemberDeclaration(Statement statement)
        {
            return new PropertyMemberDeclaration
            {
                Statements = statement
            };
        }
    }

    public class PropertyDeclaration : MemberDeclaration
    {
        private PropertyMemberDeclaration _setter;
        private PropertyMemberDeclaration _getter;
        private TypeReference _type;
        private TypeReference _privateImplementationType;

        public PropertyDeclaration()
            : this(null, null)
        {
        }

        public PropertyDeclaration(string name, TypeReference type)
        {
            Name = name;
            Type = type;
        }

        public Modifiers Modifiers { get; set; }

        public TypeReference Type
        {
            get => _type;
            set => SetParent(ref _type, value);
        }

        public PropertyMemberDeclaration Getter
        {
            get => _getter;
            set => SetParent(ref _getter, value);
        }

        public PropertyMemberDeclaration Setter
        {
            get => _setter;
            set => SetParent(ref _setter, value);
        }

        public TypeReference PrivateImplementationType
        {
            get => _privateImplementationType;
            set => SetParent(ref _privateImplementationType, value);
        }
    }
}