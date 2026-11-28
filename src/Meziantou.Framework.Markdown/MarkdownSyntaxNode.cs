using System.Collections.Immutable;
using System.Runtime.InteropServices;
using System.Text;
using Meziantou.Framework.Language.InternalSyntax;
using Meziantou.Framework.Markdown.Parsing;
using SyntaxToken = Meziantou.Framework.Language.SyntaxToken;

namespace Meziantou.Framework.Markdown;

/// <summary>The base of every node of a Markdown syntax tree: a block, an inline, or a part of a table.</summary>
/// <remarks>
/// The tokens of a node are the text of its construct: the marker of a block quote, the backticks of a code span, the
/// brackets and the destination of a link. The properties of the derived types read them, and give the values they
/// stand for: the level of a heading, the destination of a link without its escapes.
/// </remarks>
public abstract class MarkdownSyntaxNode : SyntaxNode
{
    private SyntaxNode? _children;

    // Whether the node is in a cell of a table, where \| is a pipe: 0 when not computed yet, 1 when it is not, 2 when it is
    private int _inTableCell;

    private protected MarkdownSyntaxNode(GreenNode green, SyntaxNode? parent, int position)
        : base(green, parent, position)
    {
    }

    /// <summary>Gets the kind of the node.</summary>
    public SyntaxKind Kind() => (SyntaxKind)RawKind;

    /// <summary>Gets the parent of the node, or <see langword="null"/> for the document.</summary>
    public new MarkdownSyntaxNode? Parent => (MarkdownSyntaxNode?)base.Parent;

    /// <summary>Calls the method of <paramref name="visitor"/> that matches this node.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="visitor"/> is <see langword="null"/>.</exception>
    public abstract void Accept(MarkdownSyntaxVisitor visitor);

    /// <summary>Calls the method of <paramref name="visitor"/> that matches this node.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="visitor"/> is <see langword="null"/>.</exception>
    public abstract TResult? Accept<TResult>(MarkdownSyntaxVisitor<TResult> visitor);

    /// <summary>Gets the text of the node without its markup, as a reader sees it.</summary>
    /// <remarks>
    /// The escapes and the entities are decoded, a code span is its code, a link is its text, and raw HTML is left out.
    /// The blocks of a container are separated by a line break.
    /// </remarks>
    public virtual string GetPlainText()
    {
        var builder = new StringBuilder();
        var first = true;
        foreach (var child in ChildNodes())
        {
            if (child is MarkdownSyntaxNode node && node.GetPlainText() is { Length: > 0 } text)
            {
                if (!first)
                {
                    builder.Append('\n');
                }

                builder.Append(text);
                first = false;
            }
        }

        return builder.ToString();
    }

    // The only slot is the list of the children
    internal override SyntaxNode? GetNodeSlot(int index) => index == 0 ? GetRedAtZero(ref _children) : null;

    internal override SyntaxNode? GetCachedSlot(int index) => index == 0 ? _children : null;

    /// <summary>Gets the text of a token of this node: in a cell of a table of GFM, not of a pipe table, <c>\|</c> is a pipe.</summary>
    internal string GetTokenText(SyntaxToken token)
    {
        if (_inTableCell == 0)
        {
            _inTableCell = FirstAncestorOrSelf<MarkdownTableCellSyntax>() is { Parent.Parent: MarkdownTableSyntax { IsPipeTable: false } } ? 2 : 1;
        }

        return _inTableCell == 2 ? token.Text.Replace("\\|", "|", StringComparison.Ordinal) : token.Text;
    }

    /// <summary>Gets the first child token of <paramref name="kind"/>, or the default token when there is none.</summary>
    private protected SyntaxToken GetToken(SyntaxKind kind)
    {
        foreach (var child in ChildNodesAndTokens())
        {
            if (child.AsToken(out var token) && token.RawKind == (int)kind)
            {
                return token;
            }
        }

        return default;
    }

    /// <summary>Gets the first child token, or the default token when there is none.</summary>
    internal SyntaxToken GetFirstChildToken()
    {
        foreach (var child in ChildNodesAndTokens())
        {
            if (child.AsToken(out var token))
            {
                return token;
            }
        }

        return default;
    }

    /// <summary>Gets the last child token, or the default token when there is none.</summary>
    internal SyntaxToken GetLastChildToken()
    {
        foreach (var child in ChildNodesAndTokens().Reverse())
        {
            if (child.AsToken(out var token))
            {
                return token;
            }
        }

        return default;
    }

    private protected ImmutableArray<SyntaxToken> GetTokens(SyntaxKind kind) => GetTokens(kind, kind);

    // The tokens are counted first, so the array has their number and is not copied
    private protected ImmutableArray<SyntaxToken> GetTokens(SyntaxKind kind, SyntaxKind otherKind)
    {
        var count = 0;
        foreach (var child in ChildNodesAndTokens())
        {
            if (IsToken(child, kind, otherKind))
            {
                count++;
            }
        }

        if (count == 0)
        {
            return [];
        }

        var tokens = new SyntaxToken[count];
        var index = 0;
        foreach (var child in ChildNodesAndTokens())
        {
            if (IsToken(child, kind, otherKind))
            {
                tokens[index++] = child.AsToken();
            }
        }

        return ImmutableCollectionsMarshal.AsImmutableArray(tokens);

        static bool IsToken(SyntaxNodeOrToken child, SyntaxKind kind, SyntaxKind otherKind)
            => child.IsToken && (child.RawKind == (int)kind || child.RawKind == (int)otherKind);
    }

    // The caches are ImmutableArray fields, whose default is "not computed yet": two threads may both compute the
    // list, they compute the same one, and writing the field is atomic, as the array is its only field
    private protected ImmutableArray<SyntaxToken> GetTokens(ref ImmutableArray<SyntaxToken> field, SyntaxKind kind) => GetTokens(ref field, kind, kind);

    private protected ImmutableArray<SyntaxToken> GetTokens(ref ImmutableArray<SyntaxToken> field, SyntaxKind kind, SyntaxKind otherKind)
    {
        if (field.IsDefault)
        {
            field = GetTokens(kind, otherKind);
        }

        return field;
    }

    private protected ImmutableArray<T> GetNodes<T>(ref ImmutableArray<T> field)
        where T : MarkdownSyntaxNode
    {
        if (field.IsDefault)
        {
            // The children are enumerated as a ChildSyntaxList, which allocates no enumerator: the first pass counts the nodes, so
            // that the array has their exact size, and creates them, so that the second pass reads them from the cache
            var children = ChildNodesAndTokens();
            var count = 0;
            foreach (var child in children)
            {
                if (child.AsNode() is T)
                {
                    count++;
                }
            }

            if (count == 0)
            {
                field = [];
            }
            else
            {
                var nodes = new T[count];
                var index = 0;
                foreach (var child in children)
                {
                    if (child.AsNode() is T node)
                    {
                        nodes[index++] = node;
                    }
                }

                field = ImmutableCollectionsMarshal.AsImmutableArray(nodes);
            }
        }

        return field;
    }

    /// <summary>Gets the text of the tokens of <paramref name="kind"/>, with the line breaks between them.</summary>
    private protected string GetTokensText(SyntaxKind kind)
    {
        // Most nodes have one token of the kind, or none: the builder is made for the second part only
        string? first = null;
        StringBuilder? builder = null;
        var previousIsKind = false;
        foreach (var child in ChildNodesAndTokens())
        {
            if (!child.AsToken(out var token))
            {
                continue;
            }

            if (token.RawKind == (int)kind)
            {
                Append(GetTokenText(token));
                previousIsKind = true;
            }
            else
            {
                if (token.RawKind == (int)SyntaxKind.LineEndingToken && previousIsKind)
                {
                    Append("\n");
                }

                previousIsKind = false;
            }
        }

        return builder?.ToString() ?? first ?? "";

        void Append(string text)
        {
            if (first is null)
            {
                first = text;
                return;
            }

            builder ??= new StringBuilder(first);
            builder.Append(text);
        }
    }

    /// <summary>Gets the plain text of children: the inlines of a block or of an inline.</summary>
    private protected string GetPlainText(IEnumerable<SyntaxNodeOrToken> children)
    {
        var builder = new StringBuilder();
        AppendPlainText(children, builder);
        return builder.ToString();
    }

    internal void AppendPlainText(IEnumerable<SyntaxNodeOrToken> children, StringBuilder builder)
    {
        foreach (var child in children)
        {
            AppendPlainText(child, builder);
        }
    }

    internal void AppendPlainText(SyntaxNodeOrToken child, StringBuilder builder)
    {
        if (child.AsNode() is MarkdownInlineSyntax inline)
        {
            inline.AppendPlainText(builder);
            return;
        }

        var token = child.AsToken();
        switch ((SyntaxKind)token.RawKind)
        {
            case SyntaxKind.TextToken or SyntaxKind.OpenBracketToken or SyntaxKind.ExclamationOpenBracketToken or SyntaxKind.CloseBracketToken or SyntaxKind.ExtendedAutolinkToken or SyntaxKind.ExtensionTextToken:
                builder.Append(GetTokenText(token));
                break;
            case SyntaxKind.EscapedCharacterToken:
                builder.Append(GetTokenText(token).AsSpan(1));
                break;
            case SyntaxKind.EntityToken:
                builder.Append(HtmlEntities.Decode(token.Text));
                break;
            case SyntaxKind.EmojiToken:
                var mapping = FirstAncestorOrSelf<MarkdownDocumentSyntax>()?.Options.EmojiMapping ?? MarkdownEmojiMapping.Default;
                builder.Append(mapping.TryGetEmoji(token.Text, out var emoji) ? emoji : token.Text);
                break;
            case SyntaxKind.SmartyPantToken:
                builder.Append(((MarkdownSmartyPant)(int)token.Value!) switch
                {
                    MarkdownSmartyPant.LeftQuote => '\u2018',
                    MarkdownSmartyPant.RightQuote => '\u2019',
                    MarkdownSmartyPant.LeftDoubleQuote => '\u201C',
                    MarkdownSmartyPant.RightDoubleQuote => '\u201D',
                    MarkdownSmartyPant.LeftAngleQuote => '\u00AB',
                    MarkdownSmartyPant.RightAngleQuote => '\u00BB',
                    MarkdownSmartyPant.Ellipsis => '\u2026',
                    MarkdownSmartyPant.EnDash => '\u2013',
                    _ => '\u2014',
                });
                break;
            case SyntaxKind.LineEndingToken or SyntaxKind.HardLineBreakToken:
                builder.Append('\n');
                break;
        }
    }
}
