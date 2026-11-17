using Meziantou.Framework.Language.InternalSyntax;
using SyntaxToken = Meziantou.Framework.Language.SyntaxToken;

namespace Meziantou.Framework.Markdown;

/// <summary>An alert of GitHub: a block quote whose first line is a kind, such as <c>[!NOTE]</c>.</summary>
public sealed class MarkdownAlertSyntax : MarkdownBlockSyntax
{
    internal MarkdownAlertSyntax(GreenNode green, SyntaxNode? parent, int position)
        : base(green, parent, position)
    {
    }

    private IReadOnlyList<MarkdownBlockSyntax>? _blocks;

    /// <summary>Gets the <c>&gt;</c> of the first line. The markers of the next lines are trivia.</summary>
    public SyntaxToken MarkerToken => GetToken(SyntaxKind.BlockQuoteMarkerToken);

    /// <summary>Gets the <c>[!NOTE]</c> of the alert, which is the first token of its paragraph.</summary>
    public SyntaxToken KindToken
    {
        get
        {
            foreach (var paragraph in Blocks.OfType<MarkdownParagraphSyntax>())
            {
                if (paragraph.GetFirstChildToken() is { RawKind: (int)SyntaxKind.AlertKindToken } token)
                {
                    return token;
                }
            }

            return default;
        }
    }

    /// <summary>Gets the kind of the alert, as it is written: <c>NOTE</c>, <c>TIP</c>, <c>IMPORTANT</c>, <c>WARNING</c>, <c>CAUTION</c>, or other letters.</summary>
    public string AlertKind => KindToken is { RawKind: not 0 } token ? token.Text[2..^1] : "";

    /// <summary>Gets the blocks of the alert. The paragraph that starts with the kind has the rest of its lines.</summary>
    public IReadOnlyList<MarkdownBlockSyntax> Blocks => GetNodes(ref _blocks);

    public override void Accept(MarkdownSyntaxVisitor visitor)
    {
        ArgumentNullException.ThrowIfNull(visitor);

        visitor.VisitMarkdownAlert(this);
    }

    public override TResult? Accept<TResult>(MarkdownSyntaxVisitor<TResult> visitor)
        where TResult : default
    {
        ArgumentNullException.ThrowIfNull(visitor);

        return visitor.VisitMarkdownAlert(this);
    }
}
