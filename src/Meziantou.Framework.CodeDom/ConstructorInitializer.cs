using System.Collections.Generic;

namespace Meziantou.Framework.CodeDom
{
    public abstract class ConstructorInitializer : CodeObject, ICommentable
    {
        public ConstructorInitializer()
            : this((IEnumerable<Expression>)null)
        {
        }

        public ConstructorInitializer(params Expression[] codeExpressions)
            : this((IEnumerable<Expression>)codeExpressions)
        {
        }

        public ConstructorInitializer(IEnumerable<Expression> codeExpressions)
        {
            CommentsBefore = new CommentCollection(this);
            CommentsAfter = new CommentCollection(this);
            Arguments = new CodeObjectCollection<Expression>(this);

            if (codeExpressions != null)
            {
                foreach (var codeExpression in codeExpressions)
                {
                    Arguments.Add(codeExpression);
                }
            }
        }

        public CommentCollection CommentsBefore { get; }
        public CommentCollection CommentsAfter { get; }
        public CodeObjectCollection<Expression> Arguments { get; }
    }
}