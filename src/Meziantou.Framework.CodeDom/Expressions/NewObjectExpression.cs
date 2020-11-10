namespace Meziantou.Framework.CodeDom
{
    public class NewObjectExpression : Expression
    {
        public NewObjectExpression()
        {
            Arguments = new CodeObjectCollection<Expression>(this);
        }

        public NewObjectExpression(TypeReference? type, params Expression[] arguments)
        {
            Arguments = new CodeObjectCollection<Expression>(this);
            Type = type;
            foreach (var argument in arguments)
            {
                Arguments.Add(argument);
            }
        }

        public TypeReference? Type { get; set; }

        public CodeObjectCollection<Expression> Arguments { get; }
    }
}
