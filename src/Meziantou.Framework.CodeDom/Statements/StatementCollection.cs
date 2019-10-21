#nullable disable
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

        public static implicit operator StatementCollection(Statement codeStatement) => new StatementCollection { codeStatement };

        public static implicit operator StatementCollection(Expression codeExpression) => new StatementCollection { codeExpression };
    }
}
