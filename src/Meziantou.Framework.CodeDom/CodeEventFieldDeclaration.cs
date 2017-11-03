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
            get { return _type; }
            set { _type = SetParent(value); }
        }

        public CodeStatementCollection AddAccessor
        {
            get { return _addAccessor; }
            set { _addAccessor = SetParent(value); }
        }

        public CodeStatementCollection RemoveAccessor
        {
            get { return _removeAccessor; }
            set { _removeAccessor = SetParent(value); }
        }

        public CodeTypeReference PrivateImplementationType
        {
            get { return _privateImplementationType; }
            set { _privateImplementationType = SetParent(value); }
        }

        public Modifiers Modifiers { get; set; }
    }
}