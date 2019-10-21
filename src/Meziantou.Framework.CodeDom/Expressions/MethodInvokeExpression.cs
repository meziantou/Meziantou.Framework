#nullable disable
namespace Meziantou.Framework.CodeDom
{
    public class MethodInvokeExpression : Expression
    {
        private Expression _method;

        public MethodInvokeExpression()
            : this(method: null)
        {
        }

        public MethodInvokeExpression(Expression method)
            : this(method, parameters: null)
        {
        }

        public MethodInvokeExpression(Expression method, params Expression[] arguments)
            : this(method, parameters: null, arguments)
        {
        }

        public MethodInvokeExpression(Expression method, TypeReference[] parameters, params Expression[] arguments)
        {
            Parameters = new CodeObjectCollection<TypeReference>();
            Arguments = new CodeObjectCollection<Expression>(this);
            Method = method;

            if (arguments != null)
            {
                Arguments.AddRange(arguments);
            }

            if (parameters != null)
            {
                Parameters.AddRange(parameters);
            }
        }

        public Expression Method
        {
            get => _method;
            set => SetParent(ref _method, value);
        }

        public CodeObjectCollection<Expression> Arguments { get; }

        public CodeObjectCollection<TypeReference> Parameters { get; }
    }
}
