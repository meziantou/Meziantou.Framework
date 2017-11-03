using System.Collections.Generic;

namespace Meziantou.Framework.CodeDom
{
    public abstract class CodeConstructorInitializer : CodeObject, ICommentable
    {
        public CodeConstructorInitializer()
            : this((IEnumerable<CodeExpression>)null)
        {
        }

        public CodeConstructorInitializer(params CodeExpression[] codeExpressions)
            : this((IEnumerable<CodeExpression>)codeExpressions)
        {
        }

        public CodeConstructorInitializer(IEnumerable<CodeExpression> codeExpressions)
        {
            CommentsBefore = new CodeCommentCollection(this);
            CommentsAfter = new CodeCommentCollection(this);
            Arguments = new CodeObjectCollection<CodeExpression>(this);

            if (codeExpressions != null)
            {
                foreach (var codeExpression in codeExpressions)
                {
                    Arguments.Add(codeExpression);
                }
            }
        }

        public CodeCommentCollection CommentsBefore { get; }
        public CodeCommentCollection CommentsAfter { get; }
        public CodeObjectCollection<CodeExpression> Arguments { get; }
    }
}