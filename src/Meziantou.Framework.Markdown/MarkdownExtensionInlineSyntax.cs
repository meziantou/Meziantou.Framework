using Meziantou.Framework.Language.InternalSyntax;
using System.Text;
using SyntaxToken = Meziantou.Framework.Language.SyntaxToken;

namespace Meziantou.Framework.Markdown;

/// <summary>
/// A construct of a <see cref="MarkdownInlineExtension"/> or of a <see cref="MarkdownDelimiterExtension"/>: markers, text
/// and inlines, in the order of the text.
/// </summary>
/// <remarks>
/// <see cref="MarkdownHtmlRenderer"/> writes the text, escaped, and the inlines, without the markers. A
/// <see cref="MarkdownHtmlNodeRenderer"/> writes other HTML, such as a link for a mention.
/// </remarks>
public sealed class MarkdownExtensionInlineSyntax : MarkdownInlineSyntax
{
    internal MarkdownExtensionInlineSyntax(GreenNode green, SyntaxNode? parent, int position)
        : base(green, parent, position)
    {
    }

    /// <summary>Gets the name of the extension that read the construct, which the first token of the construct has.</summary>
    public string Name => GetFirstChildToken().Value as string ?? "";

    /// <summary>Gets the markers, such as the <c>@</c> of a mention or the runs of a delimiter extension, in the order of the text.</summary>
    public IReadOnlyList<SyntaxToken> Markers => [.. ChildTokens().Where(token => token.RawKind == (int)SyntaxKind.ExtensionMarkerToken)];

    /// <summary>Gets the text tokens, in the order of the text. A text on several lines has a token for each line.</summary>
    public IReadOnlyList<SyntaxToken> Texts => [.. ChildTokens().Where(token => token.RawKind == (int)SyntaxKind.ExtensionTextToken)];

    /// <summary>Gets the text of the construct: its text tokens, and its line endings between them.</summary>
    public string Text
    {
        get
        {
            var builder = new StringBuilder();
            foreach (var token in ChildTokens())
            {
                if (token.RawKind is (int)SyntaxKind.ExtensionTextToken or (int)SyntaxKind.LineEndingToken)
                {
                    builder.Append(token.Text);
                }
            }

            return builder.ToString();
        }
    }

    /// <summary>Gets the content that the parser read as inlines: the children that are not markers.</summary>
    public IEnumerable<SyntaxNodeOrToken> Inlines => ChildNodesAndTokens().Where(child => !child.IsToken || child.AsToken().RawKind != (int)SyntaxKind.ExtensionMarkerToken);

    internal override void AppendPlainText(StringBuilder builder) => AppendPlainText(Inlines, builder);

    public override void Accept(MarkdownSyntaxVisitor visitor)
    {
        ArgumentNullException.ThrowIfNull(visitor);

        visitor.VisitMarkdownExtensionInline(this);
    }

    public override TResult? Accept<TResult>(MarkdownSyntaxVisitor<TResult> visitor)
        where TResult : default
    {
        ArgumentNullException.ThrowIfNull(visitor);

        return visitor.VisitMarkdownExtensionInline(this);
    }
}
