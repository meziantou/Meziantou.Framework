namespace Meziantou.Framework.CodeDom
{
    public class CodeCustomAttributeArgument : CodeObject
    {
        private CodeExpression _value;

        public CodeCustomAttributeArgument()
        {
        }

        public CodeCustomAttributeArgument(CodeExpression value)
        {
            Value = value;
        }

        public CodeCustomAttributeArgument(string propertyName, CodeExpression value)
        {
            PropertyName = propertyName;
            Value = value;
        }

        public string PropertyName { get; set; }

        public CodeExpression Value
        {
            get { return _value; }
            set { _value = SetParent(value); }
        }
    }
}