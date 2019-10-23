namespace Meziantou.Framework.CodeDom
{
    public class NewObjectExpression : Expression
    {
        private TypeReference? _type;

        public NewObjectExpression()
        {
            Arguments = new CodeObjectCollection<Expression>(this);
        }

        public NewObjectExpression(TypeReference? type, params Expression[] arguments)
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
            set => SetParent(ref _type, value);
        }

        public CodeObjectCollection<Expression> Arguments { get; }
    }
}
