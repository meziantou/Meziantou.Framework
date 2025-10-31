namespace Meziantou.Framework.CodeDom;

/// <summary>Represents a collection of comments.</summary>
public class CommentCollection : CodeObjectCollection<Comment>
{
    private readonly CommentType _defaultCommentType;

    public CommentCollection(CodeObject parent)
        : base(parent)
    {
    }

    public CommentCollection(CodeObject parent, CommentType defaultCommentType)
        : base(parent)
    {
        _defaultCommentType = defaultCommentType;
    }

    public void Add(string? text)
    {
        Add(new Comment(text, _defaultCommentType));
    }

    public void Add(string? text, CommentType type)
    {
        Add(new Comment(text, type));
    }
}
