namespace Meziantou.Framework.CodeDom
{
    public class CodeFieldDeclaration : CodeMemberDeclaration
    {
        private CodeExpression _initExpression;
        private CodeTypeReference _type;

        public CodeExpression InitExpression
        {
            get { return _initExpression; }
            set { _initExpression = SetParent(value); }
        }

        public CodeTypeReference Type
        {
            get { return _type; }
            set { _type = SetParent(value); }
        }

        public Modifiers Modifiers { get; set; }
    }
}