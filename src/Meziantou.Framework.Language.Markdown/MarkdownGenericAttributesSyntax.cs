using Meziantou.Framework.Language.InternalSyntax;
using Meziantou.Framework.Language.Markdown.Parsing;
using System.Text;

namespace Meziantou.Framework.Language.Markdown;

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
    /// break are for none.
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
    internal bool IsBlockLine => ChildTokens().FirstOrDefault().Value is 3;

    /// <summary>Gets the target from the sibling before the attributes, without the attributes before them.</summary>
    internal MarkdownSyntaxNode? GetTarget(SyntaxNodeOrToken previous, bool lastOfAttributesParagraph, Func<MarkdownParagraphSyntax, MarkdownBlockSyntax?> getNextBlock)
    {
        // Attributes after a delimiter that is text are for no element, and the ones after whitespace are for the block
        var parent = Parent;
        var mark = ChildTokens().FirstOrDefault().Value;
        if (parent is null || mark is 1)
        {
            return null;
        }

        if (IsBlockLine)
        {
            return parent;
        }

        if (mark is not 2 && previous.AsNode() is MarkdownInlineSyntax inline)
        {
            return inline;
        }

        if (previous.IsToken && previous.AsToken().RawKind is (int)SyntaxKind.LineEndingToken or (int)SyntaxKind.HardLineBreakToken)
        {
            return null;
        }

        var block = FirstAncestorOrSelf<MarkdownSyntaxNode>(node => node is MarkdownBlockSyntax or MarkdownTableCellSyntax);
        if (lastOfAttributesParagraph && block is MarkdownParagraphSyntax paragraph && getNextBlock(paragraph) is { } next)
        {
            return next;
        }

        return block;
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
        foreach (var token in ChildTokens())
        {
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
