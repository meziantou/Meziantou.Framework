namespace Meziantou.Framework.CodeDom;

/// <summary>Represents a code comment that can be added to code objects.</summary>
public class Comment : CodeObject
{
    public Comment()
    {
    }

    /// <summary>Initializes a new instance of the <see cref="Comment"/> class with the specified text and type.</summary>
    /// <param name="text">The comment text.</param>
    /// <param name="type">The comment type (line or inline).</param>
    public Comment(string? text, CommentType type)
    {
        Text = text;
        Type = type;
    }

    public string? Text { get; set; }
    public CommentType Type { get; set; }
}
