using Meziantou.Framework.Language.InternalSyntax;
using System.Text;

namespace Meziantou.Framework.Markdown;

/// <summary>A reference to a footnote: <c>[^label]</c>.</summary>
public sealed class MarkdownFootnoteReferenceSyntax : MarkdownInlineSyntax
{
    internal MarkdownFootnoteReferenceSyntax(GreenNode green, SyntaxNode? parent, int position)
        : base(green, parent, position)
    {
    }

    /// <summary>Gets the label that refers to the footnote, with its <c>^</c>.</summary>
    public string Label
    {
        get
        {
            if (GetToken(SyntaxKind.LinkLabelToken).RawKind != 0)
            {
                return GetTokensText(SyntaxKind.LinkLabelToken);
            }

            // The text between the brackets
            var builder = new StringBuilder();
            foreach (var child in ChildNodesAndTokens().Skip(1))
            {
                if (child.IsToken && child.AsToken().RawKind == (int)SyntaxKind.CloseBracketToken)
                {
                    break;
                }

                foreach (var token in child.IsToken ? [child.AsToken()] : child.AsNode()!.DescendantTokens())
                {
                    builder.Append(GetTokenText(token));
                }
            }

            return builder.ToString();
        }
    }

    /// <summary>Gets the footnote, or <see langword="null"/> when an edit removed it.</summary>
    public MarkdownFootnoteDefinitionSyntax? Definition => FirstAncestorOrSelf<MarkdownDocumentSyntax>()?.GetFootnoteDefinition(Label);

    internal override void AppendPlainText(StringBuilder builder)
    {
    }

    public override void Accept(MarkdownSyntaxVisitor visitor)
    {
        ArgumentNullException.ThrowIfNull(visitor);

        visitor.VisitMarkdownFootnoteReference(this);
    }

    public override TResult? Accept<TResult>(MarkdownSyntaxVisitor<TResult> visitor)
        where TResult : default
    {
        ArgumentNullException.ThrowIfNull(visitor);

        return visitor.VisitMarkdownFootnoteReference(this);
    }
}
