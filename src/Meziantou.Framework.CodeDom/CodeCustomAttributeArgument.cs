namespace Meziantou.Framework.CodeDom
{
    public class CodeCustomAttributeArgument : CodeObject
    {
        private CodeExpression _value;
        public string Name { get; set; }

        public CodeExpression Value
        {
            get { return _value; }
            set { _value = SetParent(value); }
        }
    }
}