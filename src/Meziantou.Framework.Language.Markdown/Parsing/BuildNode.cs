namespace Meziantou.Framework.Language.Markdown.Parsing;

internal sealed class BuildNode(SyntaxKind kind) : BuildElement
{
    public SyntaxKind Kind { get; } = kind;

    public List<BuildElement> Children { get; } = [];

    public void AddToken(SyntaxKind kind, int start, int end, int value = 0)
    {
        if (end > start)
        {
            Children.Add(new BuildToken(kind, start, end, value));
        }
    }

    // A line of a code block is a token even when it is empty: it is still a line of the code
    public void AddLineToken(SyntaxKind kind, int start, int end, int value) => Children.Add(new BuildToken(kind, start, end, value));
}
