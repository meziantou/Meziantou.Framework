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

    /// <summary>Returns this definition with the destination <paramref name="destination"/>, or itself when it has this destination.</summary>
    /// <param name="destination">The destination, which is escaped: it is the <see cref="Destination"/> of the definition.</param>
    /// <remarks>The definition is in no tree: replace this one with it, with <see cref="SyntaxNodeExtensions.ReplaceNode{TRoot}(TRoot, SyntaxNode, SyntaxNode)"/>.</remarks>
    /// <exception cref="ArgumentNullException"><paramref name="destination"/> is <see langword="null"/>.</exception>
    public MarkdownLinkReferenceDefinitionSyntax WithDestination(string destination)
    {
        ArgumentNullException.ThrowIfNull(destination);

        if (Destination == destination)
            return this;

        return MarkdownLinkSyntax.WithDestination(this, destination, start: 0);
    }

    /// <summary>Returns this definition with the title <paramref name="title"/>, or itself when it has this title.</summary>
    /// <param name="title">The title, which is escaped: it is the <see cref="Title"/> of the definition. <see langword="null"/> removes the title.</param>
    /// <remarks>The definition is in no tree: replace this one with it, with <see cref="SyntaxNodeExtensions.ReplaceNode{TRoot}(TRoot, SyntaxNode, SyntaxNode)"/>.</remarks>
    public MarkdownLinkReferenceDefinitionSyntax WithTitle(string? title)
    {
        if (Title == title)
            return this;

        return MarkdownLinkSyntax.WithTitle(this, title, start: 0);
    }

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
