namespace Meziantou.Framework.CodeDom
{
    public class CodeLiteralExpression : CodeExpression
    {
        public CodeLiteralExpression()
        {
        }

        public CodeLiteralExpression(object value)
        {
            Value = value;
        }

        public object Value { get; set; }
    }
}