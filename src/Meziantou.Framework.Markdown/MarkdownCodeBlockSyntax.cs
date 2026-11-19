using Meziantou.Framework.Language.InternalSyntax;
using Meziantou.Framework.Markdown.Parsing;
using System.Text;
using GreenToken = Meziantou.Framework.Language.InternalSyntax.SyntaxToken;
using SyntaxToken = Meziantou.Framework.Language.SyntaxToken;

namespace Meziantou.Framework.Markdown;

/// <summary>A fenced code block (between <c>```</c> or <c>~~~</c>) or an indented code block.</summary>
public sealed class MarkdownCodeBlockSyntax : MarkdownBlockSyntax
{
    internal MarkdownCodeBlockSyntax(GreenNode green, SyntaxNode? parent, int position)
        : base(green, parent, position)
    {
    }

    /// <summary>Gets a value indicating whether the code is between fences rather than indented.</summary>
    public bool IsFenced => Kind() == SyntaxKind.FencedCodeBlock;

    /// <summary>Gets the opening fence, or the default token for an indented code block.</summary>
    public SyntaxToken OpeningFenceToken => GetLeadingToken(SyntaxKind.CodeFenceToken);

    /// <summary>Gets the closing fence, or the default token when there is none.</summary>
    public SyntaxToken ClosingFenceToken
    {
        get
        {
            var fences = GetTokens(SyntaxKind.CodeFenceToken);
            return fences.Count == 2 ? fences[1] : default;
        }
    }

    /// <summary>Gets the info string after the opening fence, or the default token when there is none.</summary>
    public SyntaxToken InfoToken => GetLeadingToken(SyntaxKind.InfoStringToken);

    /// <summary>Gets the info string, without its escapes and entities, or <see langword="null"/> when there is none.</summary>
    public string? Info => InfoToken is { RawKind: not 0 } info ? MarkdownText.Unescape(info.Text) : null;

    /// <summary>Gets the first word of the info string, which names the language of the code, or <see langword="null"/>.</summary>
    public string? Language
    {
        get
        {
            if (Info is not { } info)
            {
                return null;
            }

            var space = info.AsSpan().IndexOfAny(' ', '\t');
            var language = space < 0 ? info : info[..space];
            return language.Length == 0 ? null : language;
        }
    }

    // A token of the opening fence: the fence and the info string are before the lines of the code
    private SyntaxToken GetLeadingToken(SyntaxKind kind)
    {
        foreach (var child in ChildNodesAndTokens())
        {
            if (!child.AsToken(out var token))
            {
                continue;
            }

            if (token.RawKind == (int)kind)
            {
                return token;
            }

            if (token.RawKind == (int)SyntaxKind.CodeLineToken)
            {
                break;
            }
        }

        return default;
    }

    /// <summary>Adds the lines of the code, as <see cref="Code"/> has them, to <paramref name="lines"/>.</summary>
    internal void AddCodeLines(List<string> lines)
    {
        if (Green.GetSlot(0) is not { } children)
        {
            return;
        }

        var count = children.IsList ? children.SlotCount : 1;
        for (var i = 0; i < count; i++)
        {
            if ((children.IsList ? children.GetSlot(i) : children) is not GreenToken { RawKind: (int)SyntaxKind.CodeLineToken } token)
            {
                continue;
            }

            // The columns of a tab that a container took part of are spaces of the code
            var line = MarkdownText.TrimLineEnding(token.Text);
            lines.Add(token.GetValue() is int virtualSpaces ? new string(' ', virtualSpaces) + line.ToString() : line.Length == token.Text.Length ? token.Text : line.ToString());
        }
    }

    /// <summary>Gets the tokens of the lines of the code, with their line endings.</summary>
    public IReadOnlyList<SyntaxToken> LineTokens => GetTokens(SyntaxKind.CodeLineToken);

    /// <summary>Gets the code: its lines without the indentation of the block and the markers of its containers, each one ended by <c>\n</c>.</summary>
    public string Code
    {
        get
        {
            var builder = new StringBuilder();
            foreach (var child in ChildNodesAndTokens())
            {
                if (!child.AsToken(out var token))
                {
                    continue;
                }

                if (token.RawKind == (int)SyntaxKind.CodeLineToken)
                {
                    // The columns of a tab that a container took part of are spaces of the code
                    if (token.Value is int virtualSpaces)
                    {
                        builder.Append(' ', virtualSpaces);
                    }

                    builder.Append(MarkdownText.TrimLineEnding(token.Text)).Append('\n');
                }
            }

            return builder.ToString();
        }
    }

    /// <summary>Returns this fenced code block with the info string <paramref name="info"/>, or itself when it has this info string.</summary>
    /// <param name="info">
    /// The info string, which is escaped: it is the <see cref="Info"/> of the block, without the whitespace around it.
    /// <see langword="null"/> or a blank text removes the info string.
    /// </param>
    /// <remarks>The block is in no tree: replace this one with it, with <see cref="SyntaxNodeExtensions.ReplaceNode{TRoot}(TRoot, SyntaxNode, SyntaxNode)"/>.</remarks>
    /// <exception cref="ArgumentException"><paramref name="info"/> contains a line ending.</exception>
    /// <exception cref="InvalidOperationException">The block is an indented code block, which has no info string.</exception>
    public MarkdownCodeBlockSyntax WithInfo(string? info)
    {
        if (!IsFenced)
            throw new InvalidOperationException("An indented code block has no info string.");

        if (info is not null && info.AsSpan().ContainsAny('\r', '\n'))
            throw new ArgumentException("The info string cannot contain a line ending.", nameof(info));

        info = string.IsNullOrWhiteSpace(info) ? null : info.Trim();
        if (Info == info)
            return this;

        var children = MarkdownNodeEditor.GetChildren(this);
        var fence = MarkdownNodeEditor.IndexOfToken(children, SyntaxKind.CodeFenceToken);
        var index = InfoToken.RawKind == 0 ? -1 : MarkdownNodeEditor.IndexOfToken(children, SyntaxKind.InfoStringToken);
        if (info is null)
        {
            MarkdownNodeEditor.RemoveOnLine(children, index, index);
        }
        else if (index >= 0)
        {
            children[index] = MarkdownNodeEditor.WithText(children[index]!, EscapeInfo(info));
        }
        else
        {
            MarkdownNodeEditor.InsertOnLineAfter(children, fence, MarkdownNodeEditor.Token(SyntaxKind.InfoStringToken, EscapeInfo(info), leadingTrivia: null, trailingTrivia: null), space: false);
        }

        return MarkdownNodeEditor.WithChildren(this, children);
    }

    // The info string, escaped: a ~ that starts it would lengthen a fence of tildes, which is written as an entity
    private static string EscapeInfo(string info)
    {
        var escaped = MarkdownEscaper.EscapeInfoString(info);
        return escaped.StartsWith('~', StringComparison.Ordinal) ? string.Concat("&#126;", escaped.AsSpan(1)) : escaped;
    }

    public override string GetPlainText() => Code;

    public override void Accept(MarkdownSyntaxVisitor visitor)
    {
        ArgumentNullException.ThrowIfNull(visitor);

        visitor.VisitMarkdownCodeBlock(this);
    }

    public override TResult? Accept<TResult>(MarkdownSyntaxVisitor<TResult> visitor)
        where TResult : default
    {
        ArgumentNullException.ThrowIfNull(visitor);

        return visitor.VisitMarkdownCodeBlock(this);
    }
}
