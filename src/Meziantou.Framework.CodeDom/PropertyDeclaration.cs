namespace Meziantou.Framework.CodeDom
{
    public class PropertyDeclaration : MemberDeclaration
    {
        private StatementCollection _setter;
        private StatementCollection _getter;
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

        public StatementCollection Getter
        {
            get => _getter;
            set => SetParent(ref _getter, value);
        }

        public StatementCollection Setter
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