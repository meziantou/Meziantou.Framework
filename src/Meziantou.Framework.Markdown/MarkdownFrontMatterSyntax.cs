using Meziantou.Framework.Language.InternalSyntax;
using Meziantou.Framework.Markdown.Parsing;
using SyntaxToken = Meziantou.Framework.Language.SyntaxToken;

namespace Meziantou.Framework.Markdown;

/// <summary>A YAML front matter at the start of the document, between <c>---</c> and <c>---</c> or <c>...</c>.</summary>
public sealed class MarkdownFrontMatterSyntax : MarkdownBlockSyntax
{
    internal MarkdownFrontMatterSyntax(GreenNode green, SyntaxNode? parent, int position)
        : base(green, parent, position)
    {
    }

    /// <summary>Gets the <c>---</c> that opens the front matter.</summary>
    public SyntaxToken OpeningFenceToken => GetFirstChildToken();

    /// <summary>Gets the <c>---</c> or <c>...</c> that closes the front matter.</summary>
    public SyntaxToken ClosingFenceToken => GetLastChildToken();

    /// <summary>Gets the tokens of the lines of the front matter, with their line endings.</summary>
    public IReadOnlyList<SyntaxToken> LineTokens => GetTokens(SyntaxKind.FrontMatterLineToken);

    /// <summary>Gets the YAML: the lines between the fences, each one ended by <c>\n</c>.</summary>
    public string Yaml => string.Concat(LineTokens.Select(token => MarkdownText.TrimLineEnding(token.Text).ToString() + "\n"));

    public override string GetPlainText() => "";

    public override void Accept(MarkdownSyntaxVisitor visitor)
    {
        ArgumentNullException.ThrowIfNull(visitor);

        visitor.VisitMarkdownFrontMatter(this);
    }

    public override TResult? Accept<TResult>(MarkdownSyntaxVisitor<TResult> visitor)
        where TResult : default
    {
        ArgumentNullException.ThrowIfNull(visitor);

        return visitor.VisitMarkdownFrontMatter(this);
    }
}
