namespace Meziantou.Framework.CodeDom
{
    public class CodeEventFieldDeclaration : CodeMemberDeclaration
    {
        private CodeTypeReference _type;
        private CodeStatementCollection _removeAccessor;
        private CodeStatementCollection _addAccessor;
        private CodeTypeReference _privateImplementationType;

        public CodeEventFieldDeclaration()
          : this(null, null)
        {
        }

        public CodeEventFieldDeclaration(string name, CodeTypeReference type)
            : this(name, type, Modifiers.None)
        {
        }

        public CodeEventFieldDeclaration(string name, CodeTypeReference type, Modifiers modifiers)
            : base(name)
        {
            Modifiers = modifiers;
            Type = type;
        }

        public CodeTypeReference Type
        {
            get => _type;
            set => SetParent(ref _type, value);
        }

        public CodeStatementCollection AddAccessor
        {
            get => _addAccessor;
            set => SetParent(ref _addAccessor, value);
        }

        public CodeStatementCollection RemoveAccessor
        {
            get => _removeAccessor;
            set => SetParent(ref _removeAccessor, value);
        }

        public CodeTypeReference PrivateImplementationType
        {
            get => _privateImplementationType;
            set => SetParent(ref _privateImplementationType, value);
        }

        public Modifiers Modifiers { get; set; }
    }
}