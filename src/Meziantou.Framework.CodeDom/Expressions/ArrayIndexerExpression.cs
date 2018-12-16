namespace Meziantou.Framework.CodeDom
{
    public class ArrayIndexerExpression : Expression
    {
        private Expression _arrayExpression;

        public ArrayIndexerExpression()
            : this(array: null)
        {
        }

        public ArrayIndexerExpression(Expression array, params Expression[] indices)
        {
            Indices = new CodeObjectCollection<Expression>(this);

            ArrayExpression = array;
            if (indices != null)
            {
                foreach (var index in indices)
                {
                    Indices.Add(index);
                }
            }
        }

        public Expression ArrayExpression
        {
            get => _arrayExpression;
            set => SetParent(ref _arrayExpression, value);
        }

        public CodeObjectCollection<Expression> Indices { get; }
    }
}
