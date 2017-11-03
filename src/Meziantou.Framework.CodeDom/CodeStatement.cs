namespace Meziantou.Framework.CodeDom
{
    public class CodeStatement : CodeObject, ICommentable
    {
        public CodeCommentCollection CommentsBefore { get; }
        public CodeCommentCollection CommentsAfter { get; }

        public CodeStatement()
        {
            CommentsBefore = new CodeCommentCollection(this);
            CommentsAfter = new CodeCommentCollection(this);
        }

        public static implicit operator CodeStatement(CodeExpression expression)
        {
            return new CodeExpressionStatement(expression);
        }
    }
}