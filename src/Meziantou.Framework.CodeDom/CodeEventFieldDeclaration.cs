namespace Meziantou.Framework.CodeDom
{
    public class CodeEventFieldDeclaration : CodeMemberDeclaration
    {
        private CodeTypeReference _type;

        public CodeTypeReference Type
        {
            get { return _type; }
            set { _type = SetParent(value); }
        }

        public Modifiers Modifiers { get; set; }
    }
}