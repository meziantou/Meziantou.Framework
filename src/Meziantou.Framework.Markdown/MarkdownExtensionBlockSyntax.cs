using System.Collections.Immutable;
using Meziantou.Framework.Language.InternalSyntax;
using Meziantou.Framework.Markdown.Parsing;
using System.Text;
using SyntaxToken = Meziantou.Framework.Language.SyntaxToken;

namespace Meziantou.Framework.Markdown;

/// <summary>
/// A block of a <see cref="MarkdownFencedBlockExtension"/> or of a <see cref="MarkdownParagraphExtension"/>: its markers, and
/// its lines, its blocks, or its text and inlines.
/// </summary>
/// <remarks>
/// <see cref="MarkdownHtmlRenderer"/> writes the lines escaped in a <c>&lt;pre&gt;&lt;code&gt;</c> element, the blocks, or the
/// text and the inlines in a <c>&lt;p&gt;</c> element, without the markers and the info. A <see cref="MarkdownHtmlNodeRenderer"/>
/// writes other HTML.
/// </remarks>
public sealed class MarkdownExtensionBlockSyntax : MarkdownBlockSyntax
{
    private ImmutableArray<MarkdownBlockSyntax> _blocks;
    private ImmutableArray<SyntaxToken> _markers;
    private ImmutableArray<SyntaxToken> _texts;
    private ImmutableArray<SyntaxToken> _lines;

    internal MarkdownExtensionBlockSyntax(GreenNode green, SyntaxNode? parent, int position)
        : base(green, parent, position)
    {
    }

    /// <summary>Gets the name of the extension that read the block, which the first token of the block has.</summary>
    public string Name => GetFirstChildToken().Value as string ?? "";

    /// <summary>Gets a value indicating whether the block is a block of a <see cref="MarkdownFencedBlockExtension"/>.</summary>
    public bool IsFenced => GetFirstChildToken().RawKind == (int)SyntaxKind.ExtensionFenceToken;

    /// <summary>Gets the fences of a fenced block, or the markers of the block of a paragraph, in the order of the text.</summary>
    public ImmutableArray<SyntaxToken> Markers => GetTokens(ref _markers, SyntaxKind.ExtensionFenceToken, SyntaxKind.ExtensionMarkerToken);

    /// <summary>Gets the text after the opening fence, without the whitespace around it, or an empty string.</summary>
    public string Info
    {
        get
        {
            if (!IsFenced)
            {
                return "";
            }

            if (GetToken(SyntaxKind.InfoStringToken) is { RawKind: not 0 } token)
            {
                return token.Text;
            }

            var builder = new StringBuilder();
            foreach (var child in Inlines)
            {
                builder.Append(child.ToFullString());
            }

            return builder.ToString().Trim(' ', '\t', '\r', '\n');
        }
    }

    /// <summary>Gets the lines between the fences of a block of <see cref="MarkdownFencedBlockContent.RawLines"/>, with their line endings.</summary>
    public ImmutableArray<SyntaxToken> Lines => GetTokens(ref _lines, SyntaxKind.ExtensionLineToken);

    /// <summary>Gets the text of the lines between the fences, each line ending with <c>\n</c>.</summary>
    public string Content
    {
        get
        {
            var builder = new StringBuilder();
            foreach (var token in ChildTokens())
            {
                if (token.RawKind == (int)SyntaxKind.ExtensionLineToken)
                {
                    // The columns of a tab that a container took part of are spaces of the line
                    if (token.Value is int virtualSpaces)
                    {
                        builder.Append(' ', virtualSpaces);
                    }

                    builder.Append(MarkdownText.TrimLineEnding(token.Text)).Append('\n');
                }
            }

            return builder.ToString();
        }
    }

    /// <summary>Gets the blocks between the fences of a block of <see cref="MarkdownFencedBlockContent.Blocks"/>.</summary>
    public ImmutableArray<MarkdownBlockSyntax> Blocks => GetNodes(ref _blocks);

    /// <summary>Gets the text tokens of the block of a paragraph, in the order of the text.</summary>
    public ImmutableArray<SyntaxToken> Texts => GetTokens(ref _texts, SyntaxKind.ExtensionTextToken);

    /// <summary>
    /// Gets the text and the inlines of the block of a paragraph, or the info of a fenced block that reads it as inlines: the
    /// children that are not markers, info, lines or blocks.
    /// </summary>
    public IEnumerable<SyntaxNodeOrToken> Inlines => ChildNodesAndTokens().Where(child => child.AsNode() is not MarkdownBlockSyntax && !(child.IsToken && child.AsToken().RawKind is (int)SyntaxKind.ExtensionFenceToken or (int)SyntaxKind.ExtensionMarkerToken or (int)SyntaxKind.ExtensionLineToken or (int)SyntaxKind.InfoStringToken));

    public override string GetPlainText() => IsFenced ? (Lines.Length > 0 ? Content : base.GetPlainText()) : GetPlainText(Inlines);

    public override void Accept(MarkdownSyntaxVisitor visitor)
    {
        ArgumentNullException.ThrowIfNull(visitor);

        visitor.VisitMarkdownExtensionBlock(this);
    }

    public override TResult? Accept<TResult>(MarkdownSyntaxVisitor<TResult> visitor)
        where TResult : default
    {
        ArgumentNullException.ThrowIfNull(visitor);

        return visitor.VisitMarkdownExtensionBlock(this);
    }
}
