namespace Meziantou.Framework.CodeDom
{
    public class EventFieldDeclaration : MemberDeclaration
    {
        private TypeReference _type;
        private StatementCollection _removeAccessor;
        private StatementCollection _addAccessor;
        private TypeReference _privateImplementationType;

        public EventFieldDeclaration()
          : this(null, null)
        {
        }

        public EventFieldDeclaration(string name, TypeReference type)
            : this(name, type, Modifiers.None)
        {
        }

        public EventFieldDeclaration(string name, TypeReference type, Modifiers modifiers)
            : base(name)
        {
            Modifiers = modifiers;
            Type = type;
        }

        public TypeReference Type
        {
            get => _type;
            set => SetParent(ref _type, value);
        }

        public StatementCollection AddAccessor
        {
            get => _addAccessor;
            set => SetParent(ref _addAccessor, value);
        }

        public StatementCollection RemoveAccessor
        {
            get => _removeAccessor;
            set => SetParent(ref _removeAccessor, value);
        }

        public TypeReference PrivateImplementationType
        {
            get => _privateImplementationType;
            set => SetParent(ref _privateImplementationType, value);
        }

        public Modifiers Modifiers { get; set; }
    }
}