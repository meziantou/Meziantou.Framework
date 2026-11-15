using Meziantou.Framework.Language.InternalSyntax;
using SyntaxToken = Meziantou.Framework.Language.SyntaxToken;

namespace Meziantou.Framework.Markdown;

/// <summary>A link reference definition: <c>[label]: destination "title"</c>.</summary>
public sealed class MarkdownLinkReferenceDefinitionSyntax : MarkdownBlockSyntax
{
    internal MarkdownLinkReferenceDefinitionSyntax(GreenNode green, SyntaxNode? parent, int position)
        : base(green, parent, position)
    {
    }

    /// <summary>Gets the label, as it is written: the text between the brackets, with its escapes.</summary>
    public string Label => GetTokensText(SyntaxKind.LinkLabelToken);

    /// <summary>Gets the destination, without its angle brackets, escapes and entities.</summary>
    public string Destination => MarkdownLinkSyntax.ReadDestination(this, GetToken(SyntaxKind.LinkDestinationToken));

    /// <summary>Gets the title, without its quotes, escapes and entities, or <see langword="null"/> when there is none.</summary>
    public string? Title => MarkdownLinkSyntax.ReadTitle(GetTokensText(SyntaxKind.LinkTitleToken));

    /// <summary>Gets the token of the destination.</summary>
    public SyntaxToken DestinationToken => GetToken(SyntaxKind.LinkDestinationToken);

    public override string GetPlainText() => "";

    public override void Accept(MarkdownSyntaxVisitor visitor)
    {
        ArgumentNullException.ThrowIfNull(visitor);

        visitor.VisitMarkdownLinkReferenceDefinition(this);
    }

    public override TResult? Accept<TResult>(MarkdownSyntaxVisitor<TResult> visitor)
        where TResult : default
    {
        ArgumentNullException.ThrowIfNull(visitor);

        return visitor.VisitMarkdownLinkReferenceDefinition(this);
    }
}
