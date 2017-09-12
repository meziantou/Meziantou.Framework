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
            get { return _arrayExpression; }
            set
            {
                _arrayExpression = SetParent(value);
            }
        }

        public CodeObjectCollection<CodeExpression> Indices { get; }
    }
}