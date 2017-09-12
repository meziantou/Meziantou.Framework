namespace Meziantou.Framework.CodeDom
{
    public class CodeEnumerationMember : CodeMemberDeclaration
    {
        private CodeExpression _value;

        public CodeExpression Value
        {
            get => _value;
            set => _value = SetParent(value);
        }
    }
}