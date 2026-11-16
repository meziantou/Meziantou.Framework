using Meziantou.Framework.Language.InternalSyntax;
using Meziantou.Framework.Markdown.Parsing;
using System.Text;

namespace Meziantou.Framework.Markdown;

/// <summary>Generic attributes: <c>{#id .class name=value}</c>, which set the HTML attributes of the element of their <see cref="Target"/>.</summary>
public sealed class MarkdownGenericAttributesSyntax : MarkdownInlineSyntax
{
    internal MarkdownGenericAttributesSyntax(GreenNode green, SyntaxNode? parent, int position)
        : base(green, parent, position)
    {
    }

    private ParsedAttributes? _attributes;

    /// <summary>Gets the id, or <see langword="null"/> when there is none.</summary>
    public string? Id => Attributes.Id;

    /// <summary>Gets the classes.</summary>
    public IReadOnlyList<string> Classes => Attributes.Classes;

    /// <summary>Gets the other attributes, whose value is <see langword="null"/> when they are written without one, such as <c>{hidden}</c>.</summary>
    public IReadOnlyList<KeyValuePair<string, string?>> Properties => Attributes.Properties;

    /// <summary>Gets the node whose element the attributes are for, or <see langword="null"/> when they are for none.</summary>
    /// <remarks>
    /// As in Markdig: attributes after an inline that is not text, such as a link, a code span or an emphasis, are for it;
    /// attributes after text are for the block. The attributes of the first line of a heading or of a fenced block are for the
    /// block. A paragraph of attributes only gives its last ones to the next block, when there is one. Attributes after a line
    /// break are for none, except in a heading, whose line breaks Markdig reads as text.
    /// </remarks>
    public MarkdownSyntaxNode? Target
    {
        get
        {
            // The previous sibling, without the attributes before it, which are for the same element
            SyntaxNodeOrToken previous = default;
            foreach (var child in Parent?.ChildNodesAndTokens() ?? default)
            {
                if (child.AsNode() == this)
                {
                    break;
                }

                if (child.AsNode() is not MarkdownGenericAttributesSyntax)
                {
                    previous = child;
                }
            }

            var lastOfAttributesParagraph = Parent is MarkdownParagraphSyntax paragraph && paragraph.ChildNodesAndTokens().All(child => child.AsNode() is MarkdownGenericAttributesSyntax) && paragraph.ChildNodes().Last() == this;
            return GetTarget(previous, lastOfAttributesParagraph, GetNextBlock);
        }
    }

    internal ParsedAttributes Attributes => _attributes ??= Parse();

    /// <summary>Gets a value indicating whether the attributes are on the first line of a heading or a fenced block, which they are for.</summary>
    /// <remarks>Their classes come before the classes of the block, such as the language of a code block.</remarks>
    internal bool IsBlockLine => GetFirstChildToken().Value is 3;

    /// <summary>Gets the target from the sibling before the attributes, without the attributes before them.</summary>
    internal MarkdownSyntaxNode? GetTarget(SyntaxNodeOrToken previous, bool lastOfAttributesParagraph, Func<MarkdownParagraphSyntax, MarkdownBlockSyntax?> getNextBlock)
    {
        // Attributes after a delimiter that is text are for no element, and the ones after whitespace are for the block
        var parent = Parent;
        var mark = GetFirstChildToken().Value;
        if (parent is null || mark is 1)
        {
            return null;
        }

        if (IsBlockLine)
        {
            return parent;
        }

        // Markdig gives the attributes after a character of SmartyPants to it, which writes no attributes, except a dash, which it
        // finds later in the text
        if (previous.IsToken && previous.AsToken() is { RawKind: (int)SyntaxKind.SmartyPantToken, Value: not ((int)MarkdownSmartyPant.EnDash or (int)MarkdownSmartyPant.EmDash) } && mark is not 2)
        {
            return null;
        }

        // Markdig gives the attributes after an opening delimiter to the delimiter, which the emphasis replaces
        if (previous.IsToken && parent is MarkdownEmphasisSyntax or MarkdownStrikethroughSyntax or MarkdownEmphasisExtraSyntax && parent.ChildNodesAndTokens()[0] == previous)
        {
            return null;
        }

        // Markdig finds the abbreviations after the attributes, in the text
        if (mark is not 2 && previous.AsNode() is MarkdownInlineSyntax inline and not MarkdownAbbreviationSyntax)
        {
            return inline;
        }

        // Markdig reads the line endings of a heading as text. The block of a cell of a pipe table is the table, which has the
        // attributes of its paragraph.
        var block = FirstAncestorOrSelf<MarkdownSyntaxNode>(node => node is MarkdownBlockSyntax or MarkdownTableCellSyntax { Parent.Parent: MarkdownTableSyntax { IsPipeTable: false } });
        if (previous.IsToken && previous.AsToken().RawKind is (int)SyntaxKind.LineEndingToken or (int)SyntaxKind.HardLineBreakToken && block is not MarkdownHeadingSyntax)
        {
            return null;
        }

        if (lastOfAttributesParagraph && block is MarkdownParagraphSyntax paragraph && getNextBlock(paragraph) is { } next)
        {
            return next;
        }

        return block;
    }

    /// <summary>
    /// Finds the target of every attributes of the document, in one pass over the children of each node. Returns the block after
    /// each paragraph of attributes only, which its last attributes are for.
    /// </summary>
    /// <remarks>
    /// The attributes are given in the order Markdig applies them: the ones of the first line of the blocks, which it reads with the
    /// blocks, then the others in the order of the text.
    /// </remarks>
    internal static Dictionary<MarkdownParagraphSyntax, MarkdownBlockSyntax> FindTargets(MarkdownDocumentSyntax document, Action<MarkdownGenericAttributesSyntax, MarkdownSyntaxNode> onTarget)
    {
        var nextBlocks = new Dictionary<MarkdownParagraphSyntax, MarkdownBlockSyntax>();
        foreach (var container in document.DescendantNodesAndSelf(MarkdownDocumentSyntax.IsBlockContainer))
        {
            MarkdownParagraphSyntax? attributesParagraph = null;
            foreach (var child in container.ChildNodes())
            {
                // Markdig removes the definitions of abbreviations and footnotes from their container
                if (child is not MarkdownBlockSyntax block || child is MarkdownAbbreviationDefinitionSyntax or MarkdownFootnoteDefinitionSyntax)
                {
                    continue;
                }

                if (attributesParagraph is not null)
                {
                    nextBlocks[attributesParagraph] = block;
                }

                attributesParagraph = block is MarkdownParagraphSyntax paragraph && IsAttributesOnly(paragraph) ? paragraph : null;
            }
        }

        List<(MarkdownGenericAttributesSyntax Attributes, MarkdownSyntaxNode Target)>? inlineTargets = null;
        foreach (var node in document.DescendantNodesAndSelf())
        {
            SyntaxNodeOrToken previous = default;
            var attributesParagraph = node is MarkdownParagraphSyntax paragraph && nextBlocks.ContainsKey(paragraph);
            var children = node.ChildNodesAndTokens();
            var count = children.Count;
            var index = 0;
            foreach (var child in children)
            {
                index++;
                if (child.AsNode() is not MarkdownGenericAttributesSyntax attributes)
                {
                    previous = child;
                    continue;
                }

                if (attributes.GetTarget(previous, attributesParagraph && index == count, nextBlocks.GetValueOrDefault) is { } target)
                {
                    if (attributes.IsBlockLine)
                    {
                        onTarget(attributes, target);
                    }
                    else
                    {
                        (inlineTargets ??= []).Add((attributes, target));
                    }
                }
            }
        }

        foreach (var (attributes, target) in inlineTargets ?? [])
        {
            onTarget(attributes, target);
        }

        return nextBlocks;
    }

    private static bool IsAttributesOnly(MarkdownParagraphSyntax paragraph)
    {
        foreach (var child in paragraph.ChildNodesAndTokens())
        {
            if (child.AsNode() is not MarkdownGenericAttributesSyntax)
            {
                return false;
            }
        }

        return true;
    }

    // The block after the block in its parent, found by its position: the children of a node are in the order of the text
    internal static MarkdownBlockSyntax? GetNextBlock(MarkdownBlockSyntax block)
    {
        if (block.Parent is not { } parent)
        {
            return null;
        }

        var children = parent.ChildNodesAndTokens();
        var low = 0;
        var high = children.Count - 1;
        while (low < high)
        {
            var middle = low + ((high - low) / 2);
            if (children[middle].FullSpan.End <= block.FullSpan.Start)
            {
                low = middle + 1;
            }
            else
            {
                high = middle;
            }
        }

        for (var i = low; i < children.Count; i++)
        {
            if (children[i].AsNode() is MarkdownBlockSyntax next && next != block && next.FullSpan.Start >= block.FullSpan.End)
            {
                return next;
            }
        }

        return null;
    }

    internal override void AppendPlainText(StringBuilder builder)
    {
    }

    private ParsedAttributes Parse()
    {
        var text = new StringBuilder();
        foreach (var child in ChildNodesAndTokens())
        {
            if (!child.AsToken(out var token))
            {
                continue;
            }

            if (token.RawKind == (int)SyntaxKind.AttributesToken)
            {
                text.Append(token.Text);
            }
            else if (token.RawKind == (int)SyntaxKind.LineEndingToken)
            {
                text.Append('\n');
            }
        }

        var value = text.ToString();
        return AttributesScanner.TryParse(value, 0, value.Length, out var attributes) >= 0 ? attributes! : new ParsedAttributes(null, [], []);
    }

    public override void Accept(MarkdownSyntaxVisitor visitor)
    {
        ArgumentNullException.ThrowIfNull(visitor);

        visitor.VisitMarkdownGenericAttributes(this);
    }

    public override TResult? Accept<TResult>(MarkdownSyntaxVisitor<TResult> visitor)
        where TResult : default
    {
        ArgumentNullException.ThrowIfNull(visitor);

        return visitor.VisitMarkdownGenericAttributes(this);
    }
}
