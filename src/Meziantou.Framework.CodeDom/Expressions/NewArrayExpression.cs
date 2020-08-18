namespace Meziantou.Framework.CodeDom
{
    public class NewArrayExpression : Expression
    {
        private TypeReference? _type;

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

        public TypeReference? Type
        {
            get => _type;
            set => _type = value;
        }

        public CodeObjectCollection<Expression> Arguments { get; }
    }
}
