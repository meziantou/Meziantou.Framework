using System.Threading.Tasks;

namespace Meziantou.Framework.CodeDom
{
    public class CodeAwaitExpression : CodeExpression
    {
        private CodeExpression _expression;

        public CodeAwaitExpression()
        {
        }

        public CodeAwaitExpression(CodeExpression expression)
        {
            Expression = expression;
        }

        public CodeExpression Expression
        {
            get { return _expression; }
            set { SetParent(ref _expression, value); }
        }

        public CodeAwaitExpression ConfigureAwait(bool continueOnCapturedContext)
        {
            if (Expression == null)
                return this;

            var awaitExpression = Expression;
            Expression = null; // detach Expression
            Expression = new CodeMethodInvokeExpression(
                new CodeMemberReferenceExpression(awaitExpression, nameof(Task.ConfigureAwait)),
                new CodeLiteralExpression(continueOnCapturedContext));
            return this;
        }
    }
}
