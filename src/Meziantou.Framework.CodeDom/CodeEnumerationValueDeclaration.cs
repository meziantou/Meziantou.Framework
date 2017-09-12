namespace Meziantou.Framework.CodeDom
{
    public class CodeEnumerationValueDeclaration : CodeMemberDeclaration
    {
        private CodeExpression _value;

        public CodeExpression Value
        {
            get { return _value; }
            set { _value = SetParent(value); }
        }
    }
}