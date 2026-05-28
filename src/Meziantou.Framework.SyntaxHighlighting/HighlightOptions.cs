namespace Meziantou.Framework.SyntaxHighlighting;

public sealed class HighlightOptions
{
    public string ClassPrefix { get; init; } = "hljs-";

    internal static HighlightOptions Default { get; } = new();
}
