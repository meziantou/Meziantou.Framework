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
        public static LiteralExpression True() => new LiteralExpression(value: true);
        public static LiteralExpression False() => new LiteralExpression(value: false);
    }
}
