namespace Meziantou.Framework.CodeDom;

public class Comment : CodeObject
{
    public Comment()
    {
    }

    public Comment(string? text, CommentType type)
    {
        Text = text;
        Type = type;
    }

    public string? Text { get; set; }
    public CommentType Type { get; set; }
}
