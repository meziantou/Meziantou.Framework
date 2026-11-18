using System.Text;
using Meziantou.Framework.Language.InternalSyntax;
using Meziantou.Framework.Markdown.Parsing;
using SyntaxToken = Meziantou.Framework.Language.SyntaxToken;

namespace Meziantou.Framework.Markdown;

/// <summary>An ATX heading (<c># Title</c>) or a setext heading (a paragraph underlined by <c>=</c> or <c>-</c>).</summary>
public sealed class MarkdownHeadingSyntax : MarkdownBlockSyntax
{
    internal MarkdownHeadingSyntax(GreenNode green, SyntaxNode? parent, int position)
        : base(green, parent, position)
    {
    }

    /// <summary>Gets a value indicating whether the heading is underlined rather than opened by <c>#</c>.</summary>
    public bool IsSetext => Kind() == SyntaxKind.SetextHeading;

    /// <summary>Gets the level of the heading, from 1 to 6: a setext heading is of level 1 or 2.</summary>
    public int Level
    {
        get
        {
            if (IsSetext)
            {
                return UnderlineToken.Text[0] == '=' ? 1 : 2;
            }

            return MarkerToken.Text.Length;
        }
    }

    /// <summary>Gets the <c>#</c> that open an ATX heading, or the default token for a setext heading.</summary>
    public SyntaxToken MarkerToken => GetToken(SyntaxKind.AtxHeadingMarkerToken);

    /// <summary>Gets the optional <c>#</c> that close an ATX heading, or the default token when there are none.</summary>
    public SyntaxToken ClosingSequenceToken => GetToken(SyntaxKind.AtxHeadingClosingSequenceToken);

    /// <summary>Gets the <c>=</c> or <c>-</c> that underline a setext heading, or the default token for an ATX heading.</summary>
    public SyntaxToken UnderlineToken => GetToken(SyntaxKind.SetextHeadingUnderlineToken);

    /// <summary>Gets the inlines of the heading, without its markers.</summary>
    public IEnumerable<SyntaxNodeOrToken> Inlines => ChildNodesAndTokens().Where(child => !child.IsToken || child.AsToken().RawKind is not ((int)SyntaxKind.AtxHeadingMarkerToken or (int)SyntaxKind.AtxHeadingClosingSequenceToken or (int)SyntaxKind.SetextHeadingUnderlineToken));

    /// <summary>Gets the identifier of the heading, which the HTML renderer writes as its <c>id</c>, or <see langword="null"/> when it has none.</summary>
    /// <remarks>
    /// It is the id of its generic attributes, or, with <see cref="MarkdownExtensions.AutoIdentifiers"/>, an identifier made from its
    /// text: <c>## Getting started</c> is <c>getting-started</c>. The identifiers made from the same text are made unique by a
    /// suffix: the second <c>## Getting started</c> is <c>getting-started-1</c>.
    /// </remarks>
    public string? Identifier => FirstAncestorOrSelf<MarkdownDocumentSyntax>()?.GetHeadingIdentifier(this);

    /// <summary>Returns this heading with the level <paramref name="level"/>, or itself when it has this level.</summary>
    /// <remarks>
    /// The number of <c>#</c> of an ATX heading changes, and the underline of a setext heading is made of <c>=</c> for the
    /// level 1 and of <c>-</c> for the level 2. The heading is in no tree: replace this one with it, with
    /// <see cref="SyntaxNodeExtensions.ReplaceNode{TRoot}(TRoot, SyntaxNode, SyntaxNode)"/>.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="level"/> is not from 1 to 6, or from 1 to 2 for a setext heading.</exception>
    public MarkdownHeadingSyntax WithLevel(int level)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(level, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(level, IsSetext ? 2 : 6);

        if (level == Level)
            return this;

        var children = MarkdownNodeEditor.GetChildren(this);
        if (IsSetext)
        {
            var index = MarkdownNodeEditor.IndexOfToken(children, SyntaxKind.SetextHeadingUnderlineToken);
            children[index] = MarkdownNodeEditor.WithText(children[index]!, new string(level == 1 ? '=' : '-', UnderlineToken.Text.Length));
        }
        else
        {
            var index = MarkdownNodeEditor.IndexOfToken(children, SyntaxKind.AtxHeadingMarkerToken);
            children[index] = MarkdownNodeEditor.WithText(children[index]!, new string('#', level));
        }

        return MarkdownNodeEditor.WithChildren(this, children);
    }

    public override string GetPlainText() => GetPlainText(Inlines);

    // The text that the identifier is made from: Markdig renders the inlines as text, without the ones of the extensions that its text
    // renderer cannot render, such as math and footnote references. The abbreviations are text then, as Markdig finds them later.
    internal string GetIdentifierText()
    {
        var builder = new StringBuilder();
        AppendIdentifierText(Inlines, builder);
        return builder.ToString();
    }

    private void AppendIdentifierText(IEnumerable<SyntaxNodeOrToken> children, StringBuilder builder)
    {
        foreach (var child in children)
        {
            switch (child.AsNode())
            {
                case MarkdownEmphasisSyntax emphasis:
                    AppendIdentifierText(emphasis.Inlines, builder);
                    break;
                case MarkdownStrikethroughSyntax strikethrough:
                    AppendIdentifierText(strikethrough.Inlines, builder);
                    break;
                case MarkdownEmphasisExtraSyntax extra:
                    AppendIdentifierText(extra.Inlines, builder);
                    break;
                case MarkdownExtensionInlineSyntax extension:
                    AppendIdentifierText(extension.Inlines, builder);
                    break;
                case MarkdownLinkSyntax link:
                    AppendIdentifierText(link.Text, builder);
                    break;
                case MarkdownCodeSpanSyntax or MarkdownAutolinkSyntax or MarkdownAbbreviationSyntax:
                    ((MarkdownInlineSyntax)child.AsNode()!).AppendPlainText(builder);
                    break;
                // Markdig does not write the characters of SmartyPants in the text of the identifier
                case null when child.AsToken().RawKind != (int)SyntaxKind.SmartyPantToken:
                    AppendPlainText([child], builder);
                    break;
            }
        }
    }

    // The text of the first line, which a reference link links to the heading with: Markdig reads it from the source, without the
    // generic attributes
    internal string GetFirstLineLabel(MarkdownParseOptions options)
    {
        var builder = new StringBuilder();
        foreach (var child in Inlines)
        {
            if (child.AsNode() is MarkdownGenericAttributesSyntax { IsBlockLine: true } || (child.IsToken && child.AsToken().RawKind == (int)SyntaxKind.AttributesTrailingTextToken))
            {
                continue;
            }

            // The trivia before the heading, such as a blank line, is not part of its first line
            var text = child.ToFullString();
            builder.Append(builder.Length == 0 ? text.AsSpan(child.Span.Start - child.FullSpan.Start) : text);
        }

        // Attributes can end with the line ending
        var firstLine = builder.ToString();
        var lineEnd = firstLine.AsSpan().IndexOfAny('\r', '\n');
        var label = lineEnd < 0 ? firstLine : firstLine[..lineEnd];
        return options.Extensions.HasFlag(MarkdownExtensions.GenericAttributes) ? HeadingIdentifier.GetLabelWithoutAttributes(label, 0, label.Length) : label;
    }

    public override void Accept(MarkdownSyntaxVisitor visitor)
    {
        ArgumentNullException.ThrowIfNull(visitor);

        visitor.VisitMarkdownHeading(this);
    }

    public override TResult? Accept<TResult>(MarkdownSyntaxVisitor<TResult> visitor)
        where TResult : default
    {
        ArgumentNullException.ThrowIfNull(visitor);

        return visitor.VisitMarkdownHeading(this);
    }
}
