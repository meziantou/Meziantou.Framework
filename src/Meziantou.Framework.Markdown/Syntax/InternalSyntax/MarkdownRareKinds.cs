namespace Meziantou.Framework.Markdown.Syntax.InternalSyntax;

/// <summary>The kinds of the nodes that most documents have none of, which a green node knows it has below it.</summary>
[Flags]
internal enum MarkdownRareKinds : byte
{
    None = 0,
    GenericAttributes = 1 << 0,
    GridTable = 1 << 1,
}
