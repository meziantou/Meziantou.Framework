using Meziantou.Framework.Language.InternalSyntax;

namespace Meziantou.Framework.Markdown;

/// <summary>A block of a document: a paragraph, a heading, a list, a block quote, a code block...</summary>
public abstract class MarkdownBlockSyntax : MarkdownSyntaxNode
{
    private protected MarkdownBlockSyntax(GreenNode green, SyntaxNode? parent, int position)
        : base(green, parent, position)
    {
    }
}
