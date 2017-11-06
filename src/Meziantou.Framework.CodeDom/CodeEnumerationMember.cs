namespace Meziantou.Framework.CodeDom
{
    public class CodeEnumerationMember : CodeMemberDeclaration
    {
        private CodeExpression _value;

        public CodeEnumerationMember()
        {
        }

        public CodeEnumerationMember(string name)
        {
            Name = name;
        }

        public CodeEnumerationMember(string name, CodeExpression value)
        {
            Name = name;
            Value = value;
        }

        public CodeExpression Value
        {
            get => _value;
            set => SetParent(ref _value, value);
        }
    }
}