namespace Meziantou.Framework.Markdown.Parsing;

internal sealed class BuildNode(SyntaxKind kind, int capacity = 0)
{
    public SyntaxKind Kind { get; } = kind;

    public List<BuildElement> Children { get; } = new(capacity);

    public void AddToken(SyntaxKind kind, int start, int end, int value = 0)
    {
        if (end > start)
        {
            Children.Add(BuildElement.Token(kind, start, end, value));
        }
    }

    // A line of a code block is a token even when it is empty: it is still a line of the code
    public void AddLineToken(SyntaxKind kind, int start, int end, int value) => Children.Add(BuildElement.Token(kind, start, end, value));
}
