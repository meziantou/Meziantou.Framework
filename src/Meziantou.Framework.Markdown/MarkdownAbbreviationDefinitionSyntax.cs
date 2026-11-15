using Meziantou.Framework.Language.InternalSyntax;
using Meziantou.Framework.Markdown.Parsing;

namespace Meziantou.Framework.Markdown;

/// <summary>The definition of an abbreviation: <c>*[HTML]: Hypertext Markup Language</c>.</summary>
public sealed class MarkdownAbbreviationDefinitionSyntax : MarkdownBlockSyntax
{
    internal MarkdownAbbreviationDefinitionSyntax(GreenNode green, SyntaxNode? parent, int position)
        : base(green, parent, position)
    {
    }

    /// <summary>Gets the abbreviation, without its escapes and the whitespace around it, and with one space for each run of whitespace.</summary>
    public string Label => Scanners.NormalizeAbbreviationLabel(GetToken(SyntaxKind.AbbreviationLabelToken).Text);

    /// <summary>Gets what the abbreviation stands for, which is the title of its <c>&lt;abbr&gt;</c>.</summary>
    public string Title => GetToken(SyntaxKind.AbbreviationTextToken) is { RawKind: not 0 } token ? token.Text : "";

    public override string GetPlainText() => "";

    public override void Accept(MarkdownSyntaxVisitor visitor)
    {
        ArgumentNullException.ThrowIfNull(visitor);

        visitor.VisitMarkdownAbbreviationDefinition(this);
    }

    public override TResult? Accept<TResult>(MarkdownSyntaxVisitor<TResult> visitor)
        where TResult : default
    {
        ArgumentNullException.ThrowIfNull(visitor);

        return visitor.VisitMarkdownAbbreviationDefinition(this);
    }
}
