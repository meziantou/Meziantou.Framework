namespace Meziantou.Framework.CodeDom
{
    public class NewArrayExpression : Expression
    {
        public NewArrayExpression()
        {
            Arguments = new CodeObjectCollection<Expression>(this);
        }

        public NewArrayExpression(TypeReference? type, params Expression[] arguments)
        {
            Arguments = new CodeObjectCollection<Expression>(this);

            Type = type;

            if (arguments != null)
            {
                foreach (var argument in arguments)
                {
                    Arguments.Add(argument);
                }
            }
        }

        public TypeReference? Type { get; set; }

        public CodeObjectCollection<Expression> Arguments { get; }
    }
}
