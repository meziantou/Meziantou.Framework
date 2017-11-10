namespace Meziantou.Framework.CodeDom
{
    public class StatementCollection : CodeObjectCollection<Statement>
    {
        public StatementCollection()
        {
        }

        public StatementCollection(CodeObject parent) : base(parent)
        {
        }

        public static implicit operator StatementCollection(Statement codeStatement)
        {
            var collection = new StatementCollection();
            collection.Add(codeStatement);
            return collection;
        }

        public static implicit operator StatementCollection(Expression codeExpression)
        {
            var collection = new StatementCollection();
            collection.Add(codeExpression);
            return collection;
        }
    }
}