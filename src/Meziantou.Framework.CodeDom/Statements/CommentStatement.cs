namespace Meziantou.Framework.CodeDom;

/// <summary>Represents a statement that contains only a comment.</summary>
public class CommentStatement : Statement
{
    public CommentStatement()
    {
    }

    public CommentStatement(string? content)
    {
        Content = content;
    }

    public string? Content { get; set; }
}
