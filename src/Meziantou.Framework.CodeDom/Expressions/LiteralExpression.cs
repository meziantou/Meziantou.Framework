namespace Meziantou.Framework.CodeDom
{
    public class LiteralExpression : Expression
    {
        public LiteralExpression()
        {
        }

        public LiteralExpression(object? value)
        {
            Value = value;
        }

        public object? Value { get; set; }

        public static LiteralExpression Null() => new LiteralExpression(value: null);
    }
}
