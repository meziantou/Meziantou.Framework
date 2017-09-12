namespace Meziantou.Framework.CodeDom
{
    public class CodeNewObjectExpression : CodeExpression
    {
        private CodeTypeReference _type;

        public CodeNewObjectExpression(CodeTypeReference type, params CodeExpression[] arguments)
        {
            Arguments = new CodeObjectCollection<CodeExpression>(this);

            Type = type;

            if (arguments != null)
            {
                foreach (var argument in arguments)
                {
                    Arguments.Add(argument);
                }
            }
        }


        public CodeTypeReference Type
        {
            get { return _type; }
            set { _type = SetParent(value); }
        }

        public CodeObjectCollection<CodeExpression> Arguments { get; }

    }
}