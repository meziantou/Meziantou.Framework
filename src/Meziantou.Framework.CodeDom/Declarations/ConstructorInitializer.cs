namespace Meziantou.Framework.CodeDom;

/// <summary>Base class for constructor initializers (this, base).</summary>
public abstract class ConstructorInitializer : CodeObject, ICommentable
{
    protected ConstructorInitializer()
        : this((IEnumerable<Expression>?)null)
    {
    }

    protected ConstructorInitializer(params Expression[] codeExpressions)
        : this((IEnumerable<Expression>)codeExpressions)
    {
    }

    protected ConstructorInitializer(IEnumerable<Expression>? codeExpressions)
    {
        CommentsBefore = new CommentCollection(this);
        CommentsAfter = new CommentCollection(this);
        Arguments = new CodeObjectCollection<Expression>(this);

        if (codeExpressions is not null)
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
