namespace Meziantou.Framework.CodeDom
{
    public class CodeEventFieldDeclaration : CodeMemberDeclaration
    {
        private CodeTypeReference _type;

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

        public Modifiers Modifiers { get; set; }
    }
}