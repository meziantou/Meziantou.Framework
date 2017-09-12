namespace Meziantou.Framework.CodeDom
{
    public class CodeMethodInvokeExpression : CodeExpression
    {
        private CodeExpression _method;

        public CodeMethodInvokeExpression()
            : this(null, null)
        {
        }

        public CodeMethodInvokeExpression(CodeExpression method, params CodeExpression[] arguments)
        {
            Arguments = new CodeObjectCollection<CodeExpression>(this);

            Method = method;

            if (arguments != null)
            {
                foreach (var argument in arguments)
                {
                    Arguments.Add(argument);
                }
            }
        }

        public CodeExpression Method
        {
            get { return _method; }
            set { _method = SetParent(value); }
        }

        public CodeObjectCollection<CodeExpression> Arguments { get; }
    }
}