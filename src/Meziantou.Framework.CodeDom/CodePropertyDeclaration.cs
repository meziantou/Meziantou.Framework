namespace Meziantou.Framework.CodeDom
{
    public class CodePropertyDeclaration : CodeMemberDeclaration
    {
        private CodeStatementCollection _setter;
        private CodeStatementCollection _getter;
        private CodeTypeReference _type;
        private CodeTypeReference _privateImplementationType;

        public CodePropertyDeclaration()
            : this(null, null)
        {
        }

        public CodePropertyDeclaration(string name, CodeTypeReference type)
        {
            Name = name;
            Type = type;
        }

        public Modifiers Modifiers { get; set; }

        public CodeTypeReference Type
        {
            get { return _type; }
            set { _type = SetParent(value); }
        }

        public CodeStatementCollection Getter
        {
            get { return _getter; }
            set { _getter = SetParent(value); }
        }

        public CodeStatementCollection Setter
        {
            get { return _setter; }
            set { _setter = SetParent(value); }
        }

        public CodeTypeReference PrivateImplementationType
        {
            get { return _privateImplementationType; }
            set { _privateImplementationType = SetParent(value); }
        }
    }
}