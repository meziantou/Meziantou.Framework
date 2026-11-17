using Meziantou.Framework.Language.InternalSyntax;
using System.Text;

namespace Meziantou.Framework.Markdown;

/// <summary>An abbreviation in the text, which a definition of the document gives the meaning of.</summary>
public sealed class MarkdownAbbreviationSyntax : MarkdownInlineSyntax
{
    internal MarkdownAbbreviationSyntax(GreenNode green, SyntaxNode? parent, int position)
        : base(green, parent, position)
    {
    }

    /// <summary>Gets the abbreviation.</summary>
    public string Label => GetPlainText(ChildNodesAndTokens());

    /// <summary>Gets the definition of the abbreviation: the last one with its label, or <see langword="null"/> when an edit removed it.</summary>
    public MarkdownAbbreviationDefinitionSyntax? Definition => FirstAncestorOrSelf<MarkdownDocumentSyntax>()?.GetAbbreviationDefinition(Label);

    internal override void AppendPlainText(StringBuilder builder) => builder.Append(Label);

    public override void Accept(MarkdownSyntaxVisitor visitor)
    {
        ArgumentNullException.ThrowIfNull(visitor);

        visitor.VisitMarkdownAbbreviation(this);
    }

    public override TResult? Accept<TResult>(MarkdownSyntaxVisitor<TResult> visitor)
        where TResult : default
    {
        ArgumentNullException.ThrowIfNull(visitor);

        return visitor.VisitMarkdownAbbreviation(this);
    }
}
