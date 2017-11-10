namespace Meziantou.Framework.CodeDom
{
    public class MethodInvokeExpression : Expression
    {
        private Expression _method;

        public MethodInvokeExpression()
            : this(null, null)
        {
        }

        public MethodInvokeExpression(Expression method, params Expression[] arguments)
        {
            Arguments = new CodeObjectCollection<Expression>(this);

            Method = method;

            if (arguments != null)
            {
                foreach (var argument in arguments)
                {
                    Arguments.Add(argument);
                }
            }
        }

        public Expression Method
        {
            get => _method;
            set => SetParent(ref _method, value);
        }

        public CodeObjectCollection<Expression> Arguments { get; }
    }
}