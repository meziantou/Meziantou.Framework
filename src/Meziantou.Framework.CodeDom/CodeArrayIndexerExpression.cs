namespace Meziantou.Framework.CodeDom
{
    public class CodeArrayIndexerExpression : CodeExpression
    {
        private CodeExpression _arrayExpression;

        public CodeArrayIndexerExpression(CodeExpression array, params CodeExpression[] indices)
        {
            Indices = new CodeObjectCollection<CodeExpression>(this);

            ArrayExpression = array;
            if (indices != null)
            {
                foreach (var index in indices)
                {
                    Indices.Add(index);
                }
            }
        }

        public CodeExpression ArrayExpression
        {
            get => _arrayExpression;
            set => SetParent(ref _arrayExpression, value);
        }

        public CodeObjectCollection<CodeExpression> Indices { get; }
    }
}