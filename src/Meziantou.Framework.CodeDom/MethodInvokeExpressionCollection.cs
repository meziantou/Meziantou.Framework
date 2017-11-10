namespace Meziantou.Framework.CodeDom
{
    public class MethodInvokeExpressionCollection : CodeObjectCollection<MethodInvokeArgumentExpression>
    {
        public MethodInvokeExpressionCollection(CodeObject parent) : base(parent)
        {
        }

        public void Add(Expression expression)
        {
            if (expression == null) throw new System.ArgumentNullException(nameof(expression));

            Add(new MethodInvokeArgumentExpression(expression));
        }
    }
}