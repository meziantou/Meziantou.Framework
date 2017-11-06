namespace Meziantou.Framework.CodeDom
{
    public class CodeStatementCollection : CodeObjectCollection<CodeStatement>
    {
        public CodeStatementCollection()
        {
        }

        public CodeStatementCollection(CodeObject parent) : base(parent)
        {
        }

        public static implicit operator CodeStatementCollection(CodeStatement codeStatement)
        {
            var collection = new CodeStatementCollection();
            collection.Add(codeStatement);
            return collection;
        }

        public static implicit operator CodeStatementCollection(CodeExpression codeExpression)
        {
            var collection = new CodeStatementCollection();
            collection.Add(codeExpression);
            return collection;
        }
    }
}