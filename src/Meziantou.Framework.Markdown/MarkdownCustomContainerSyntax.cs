using Meziantou.Framework.Language.InternalSyntax;
using Meziantou.Framework.Markdown.Parsing;
using SyntaxToken = Meziantou.Framework.Language.SyntaxToken;

namespace Meziantou.Framework.Markdown;

/// <summary>A custom container: blocks between a fence of three colons or more, <c>:::name arguments</c>, and a fence as long.</summary>
/// <remarks>The HTML renderer writes it as a <c>&lt;div&gt;</c>, whose class is the <see cref="Name"/>.</remarks>
public sealed class MarkdownCustomContainerSyntax : MarkdownBlockSyntax
{
    internal MarkdownCustomContainerSyntax(GreenNode green, SyntaxNode? parent, int position)
        : base(green, parent, position)
    {
    }

    private IReadOnlyList<MarkdownBlockSyntax>? _blocks;

    /// <summary>Gets the colons that open the container.</summary>
    public SyntaxToken OpeningFenceToken => GetFirstChildToken();

    /// <summary>Gets the colons that close the container, or the default token when the container is not closed.</summary>
    public SyntaxToken ClosingFenceToken => ChildTokens().Skip(1).LastOrDefault(token => token.RawKind == (int)SyntaxKind.CustomContainerFenceToken);

    /// <summary>Gets the name after the opening fence, without its escapes and entities, or an empty string when there is none.</summary>
    public string Name => GetToken(SyntaxKind.InfoStringToken) is { RawKind: not 0 } token ? MarkdownText.Unescape(token.Text) : "";

    /// <summary>Gets the text after the name, without its escapes and entities, or an empty string when there is none.</summary>
    public string Arguments => GetToken(SyntaxKind.CustomContainerArgumentsToken) is { RawKind: not 0 } token ? MarkdownText.Unescape(token.Text) : "";

    /// <summary>Gets the blocks of the container.</summary>
    public IReadOnlyList<MarkdownBlockSyntax> Blocks => GetNodes(ref _blocks);

    public override void Accept(MarkdownSyntaxVisitor visitor)
    {
        ArgumentNullException.ThrowIfNull(visitor);

        visitor.VisitMarkdownCustomContainer(this);
    }

    public override TResult? Accept<TResult>(MarkdownSyntaxVisitor<TResult> visitor)
        where TResult : default
    {
        ArgumentNullException.ThrowIfNull(visitor);

        return visitor.VisitMarkdownCustomContainer(this);
    }
}
