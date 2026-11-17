namespace Meziantou.Framework.Markdown;

/// <summary>The lines where Markdig starts the blocks, which its pragma lines and <see cref="MarkdownDocumentSyntax.FindClosestLine(int)"/> use.</summary>
internal static class MarkdigLines
{
    // Where Markdig starts a block: a setext heading on the line of its underline, a definition on the line of its marker, and a
    // paragraph on the line of the link reference definitions that it starts with
public static int GetStart(SyntaxNode block)
    {
        switch (block)
        {
            case MarkdownHeadingSyntax { RawKind: (int)SyntaxKind.SetextHeading }:
                return block.GetLastToken().SpanStart;
            case MarkdownDefinitionItemSyntax item:
                return item.MarkerToken.SpanStart;
            case MarkdownParagraphSyntax when block.Parent is { } parent:
            {
                var start = block.SpanStart;
                var siblings = parent.ChildNodes().ToList();
                for (var i = siblings.IndexOf(block) - 1; i >= 0 && siblings[i] is MarkdownLinkReferenceDefinitionSyntax definition; i--)
                {
                    var text = block.SyntaxTree!.GetText().ToString();
                    if (text.AsSpan(definition.Span.End, start - definition.Span.End).Count('\n') > 1)
                    {
                        break;
                    }

                    start = definition.SpanStart;
                }

                return start;
            }

            default:
                return block.SpanStart;
        }
    }

    /// <summary>Gets the zero-based number of the line where Markdig starts the block.</summary>
    public static int GetLine(SyntaxNode block) => block.SyntaxTree!.GetText().GetLine(GetStart(block)).LineNumber;
}
