namespace Meziantou.Framework.CodeDom
{
    public class CodeFieldDeclaration : CodeMemberDeclaration
    {
        private CodeExpression _initExpression;
        private CodeTypeReference _type;

        public CodeFieldDeclaration()
            : this(null, null)
        {
        }

        public CodeFieldDeclaration(string name, CodeTypeReference type)
            : this(name, type, Modifiers.None)
        {
        }

        public CodeFieldDeclaration(string name, CodeTypeReference type, Modifiers modifiers)
            : this(name, type, modifiers, null)
        {
        }

        public CodeFieldDeclaration(string name, CodeTypeReference type, Modifiers modifiers, CodeExpression initExpression)
            : base(name)
        {
            Modifiers = modifiers;
            Type = type;
            InitExpression = initExpression;
        }

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