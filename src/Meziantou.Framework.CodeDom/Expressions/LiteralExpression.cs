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

        public static LiteralExpression Null() => new(value: null);
        public static LiteralExpression True() => new(value: true);
        public static LiteralExpression False() => new(value: false);
    }
}
