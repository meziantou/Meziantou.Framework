using System.Threading.Tasks;

namespace Meziantou.Framework.CodeDom
{
    public class AwaitExpression : Expression
    {
        private Expression _expression;

        public AwaitExpression()
        {
        }

        public AwaitExpression(Expression expression)
        {
            Expression = expression;
        }

        public Expression Expression
        {
            get => _expression;
            set => SetParent(ref _expression, value);
        }

        public AwaitExpression ConfigureAwait(bool continueOnCapturedContext)
        {
            if (Expression == null)
                return this;

            var awaitExpression = Expression;
            Expression = null; // detach Expression
            Expression = new MethodInvokeExpression(
                new MemberReferenceExpression(awaitExpression, nameof(Task.ConfigureAwait)),
                new LiteralExpression(continueOnCapturedContext));
            return this;
        }
    }
}
