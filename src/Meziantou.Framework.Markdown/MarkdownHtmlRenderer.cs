using System.Buffers;
using System.Collections.Frozen;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using Meziantou.Framework.Markdown.Parsing;
using GreenToken = Meziantou.Framework.Language.InternalSyntax.SyntaxToken;

namespace Meziantou.Framework.Markdown;

/// <summary>Renders a Markdown syntax tree as HTML, as the CommonMark reference implementation does.</summary>
public static class MarkdownHtmlRenderer
{
    // The titles of the alerts of GitHub, with their icons (Octicons, MIT license), as Meziantou.Framework.Markdown 1.x writes them
    private static readonly Dictionary<string, string> AlertTitles = new(StringComparer.OrdinalIgnoreCase)
    {
        ["NOTE"] = "<p class=\"markdown-alert-title\"><svg viewBox=\"0 0 16 16\" version=\"1.1\" width=\"16\" height=\"16\" aria-hidden=\"true\"><path d=\"M0 8a8 8 0 1 1 16 0A8 8 0 0 1 0 8Zm8-6.5a6.5 6.5 0 1 0 0 13 6.5 6.5 0 0 0 0-13ZM6.5 7.75A.75.75 0 0 1 7.25 7h1a.75.75 0 0 1 .75.75v2.75h.25a.75.75 0 0 1 0 1.5h-2a.75.75 0 0 1 0-1.5h.25v-2h-.25a.75.75 0 0 1-.75-.75ZM8 6a1 1 0 1 1 0-2 1 1 0 0 1 0 2Z\"></path></svg>Note</p>",
        ["TIP"] = "<p class=\"markdown-alert-title\"><svg viewBox=\"0 0 16 16\" version=\"1.1\" width=\"16\" height=\"16\" aria-hidden=\"true\"><path d=\"M8 1.5c-2.363 0-4 1.69-4 3.75 0 .984.424 1.625.984 2.304l.214.253c.223.264.47.556.673.848.284.411.537.896.621 1.49a.75.75 0 0 1-1.484.211c-.04-.282-.163-.547-.37-.847a8.456 8.456 0 0 0-.542-.68c-.084-.1-.173-.205-.268-.32C3.201 7.75 2.5 6.766 2.5 5.25 2.5 2.31 4.863 0 8 0s5.5 2.31 5.5 5.25c0 1.516-.701 2.5-1.328 3.259-.095.115-.184.22-.268.319-.207.245-.383.453-.541.681-.208.3-.33.565-.37.847a.751.751 0 0 1-1.485-.212c.084-.593.337-1.078.621-1.489.203-.292.45-.584.673-.848.075-.088.147-.173.213-.253.561-.679.985-1.32.985-2.304 0-2.06-1.637-3.75-4-3.75ZM5.75 12h4.5a.75.75 0 0 1 0 1.5h-4.5a.75.75 0 0 1 0-1.5ZM6 15.25a.75.75 0 0 1 .75-.75h2.5a.75.75 0 0 1 0 1.5h-2.5a.75.75 0 0 1-.75-.75Z\"></path></svg>Tip</p>",
        ["IMPORTANT"] = "<p class=\"markdown-alert-title\"><svg viewBox=\"0 0 16 16\" version=\"1.1\" width=\"16\" height=\"16\" aria-hidden=\"true\"><path d=\"M0 1.75C0 .784.784 0 1.75 0h12.5C15.216 0 16 .784 16 1.75v9.5A1.75 1.75 0 0 1 14.25 13H8.06l-2.573 2.573A1.458 1.458 0 0 1 3 14.543V13H1.75A1.75 1.75 0 0 1 0 11.25Zm1.75-.25a.25.25 0 0 0-.25.25v9.5c0 .138.112.25.25.25h2a.75.75 0 0 1 .75.75v2.19l2.72-2.72a.749.749 0 0 1 .53-.22h6.5a.25.25 0 0 0 .25-.25v-9.5a.25.25 0 0 0-.25-.25Zm7 2.25v2.5a.75.75 0 0 1-1.5 0v-2.5a.75.75 0 0 1 1.5 0ZM9 9a1 1 0 1 1-2 0 1 1 0 0 1 2 0Z\"></path></svg>Important</p>",
        ["WARNING"] = "<p class=\"markdown-alert-title\"><svg viewBox=\"0 0 16 16\" version=\"1.1\" width=\"16\" height=\"16\" aria-hidden=\"true\"><path d=\"M6.457 1.047c.659-1.234 2.427-1.234 3.086 0l6.082 11.378A1.75 1.75 0 0 1 14.082 15H1.918a1.75 1.75 0 0 1-1.543-2.575Zm1.763.707a.25.25 0 0 0-.44 0L1.698 13.132a.25.25 0 0 0 .22.368h12.164a.25.25 0 0 0 .22-.368Zm.53 3.996v2.5a.75.75 0 0 1-1.5 0v-2.5a.75.75 0 0 1 1.5 0ZM9 11a1 1 0 1 1-2 0 1 1 0 0 1 2 0Z\"></path></svg>Warning</p>",
        ["CAUTION"] = "<p class=\"markdown-alert-title\"><svg viewBox=\"0 0 16 16\" version=\"1.1\" width=\"16\" height=\"16\" aria-hidden=\"true\"><path d=\"M4.47.22A.749.749 0 0 1 5 0h6c.199 0 .389.079.53.22l4.25 4.25c.141.14.22.331.22.53v6a.749.749 0 0 1-.22.53l-4.25 4.25A.749.749 0 0 1 11 16H5a.749.749 0 0 1-.53-.22L.22 11.53A.749.749 0 0 1 0 11V5c0-.199.079-.389.22-.53Zm.84 1.28L1.5 5.31v5.38l3.81 3.81h5.38l3.81-3.81V5.31L10.69 1.5ZM8 4a.75.75 0 0 1 .75.75v3.5a.75.75 0 0 1-1.5 0v-3.5A.75.75 0 0 1 8 4Zm0 8a1 1 0 1 1 0-2 1 1 0 0 1 0 2Z\"></path></svg>Caution</p>",
    };

    private static readonly SearchValues<char> EscapedCharacters = SearchValues.Create("&<>\"\0");

    // The characters that code escapes: plain text only replaces \0
    private static readonly SearchValues<char> SoftEscapedCharacters = SearchValues.Create("&<\0");
    private static readonly SearchValues<char> PlainTextSoftEscapedCharacters = SearchValues.Create("\0");

    // The characters a URL keeps as they are: a % is kept only before two hexadecimal digits, so it is not one of them
    private static readonly SearchValues<char> UriSafeCharacters = SearchValues.Create("abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789;/?:@&=+$,-_.!~*'()#");

    /// <summary>Renders the document as HTML.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="tree"/> is <see langword="null"/>.</exception>
    public static string ToHtml(MarkdownSyntaxTree tree) => ToHtml(tree, options: null);

    /// <summary>Renders the document as HTML.</summary>
    /// <param name="tree">The document.</param>
    /// <param name="options">How to write the HTML, or <see langword="null"/> for <see cref="MarkdownHtmlRendererOptions.Default"/>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="tree"/> is <see langword="null"/>.</exception>
    public static string ToHtml(MarkdownSyntaxTree tree, MarkdownHtmlRendererOptions? options) => Render(tree, options, plainText: false);

    // The HTML, or the plain text: the text of the HTML, without its tags and its HTML, and not escaped
    internal static string Render(MarkdownSyntaxTree tree, MarkdownHtmlRendererOptions? options, bool plainText)
    {
        ArgumentNullException.ThrowIfNull(tree);

        using var writer = Write(tree, options, plainText);
        return writer.ToString();
    }

    /// <summary>
    /// Gets a value indicating whether two documents render as the same HTML, with the default options. A line ending of the HTML
    /// is the same as another one: the line endings of the code change with the line ending of the document.
    /// </summary>
    /// <remarks>The HTML is compared in the buffers that it is written to, without creating the strings.</remarks>
    internal static bool RenderAsTheSameHtml(MarkdownSyntaxTree tree, MarkdownSyntaxTree other)
    {
        using var writer = Write(tree, options: null, plainText: false);
        using var otherWriter = Write(other, options: null, plainText: false);
        return EqualsIgnoringLineEndings(writer.Written, otherWriter.Written);
    }

    // Whether two texts are the same, \r\n and \r being \n
    internal static bool EqualsIgnoringLineEndings(ReadOnlySpan<char> text, ReadOnlySpan<char> other)
    {
        while (true)
        {
            var common = text.CommonPrefixLength(other);
            var afterCarriageReturn = common > 0 && text[common - 1] == '\r';
            text = text[common..];
            other = other[common..];

            // The \r that ends the common text and the \n after it on one side are a single line ending
            if (afterCarriageReturn)
            {
                var textLineFeed = text is ['\n', ..];
                var otherLineFeed = other is ['\n', ..];
                text = textLineFeed ? text[1..] : text;
                other = otherLineFeed ? other[1..] : other;
                if (textLineFeed || otherLineFeed)
                {
                    continue;
                }
            }

            if (text.IsEmpty || other.IsEmpty)
            {
                return text.IsEmpty && other.IsEmpty;
            }

            if (!TrySkipLineEnding(ref text) || !TrySkipLineEnding(ref other))
            {
                return false;
            }
        }

        static bool TrySkipLineEnding(ref ReadOnlySpan<char> text)
        {
            switch (text)
            {
                case ['\r', '\n', ..]:
                    text = text[2..];
                    return true;
                case ['\r' or '\n', ..]:
                    text = text[1..];
                    return true;
                default:
                    return false;
            }
        }
    }

    // Writes the HTML, or the plain text, into a writer that the caller disposes
    private static Writer Write(MarkdownSyntaxTree tree, MarkdownHtmlRendererOptions? options, bool plainText)
    {
        var writer = new Writer(tree.GetText().Length, tree.Options, options ?? MarkdownHtmlRendererOptions.Default) { PlainText = plainText };
        writer.CollectAllAttributes(tree);

        if (options?.PragmaLines is true && !plainText)
        {
            writer.CollectPragmaLines(tree);
        }

        writer.WriteBlocks(tree.GetRoot().Blocks);
        if (tree.Options.Extensions.HasFlag(MarkdownExtensions.Footnotes))
        {
            writer.WriteFootnotes(tree.GetRoot());
        }

        if (plainText)
        {
            writer.EndLine();
        }

        return writer;
    }

    internal sealed class Writer(int sourceLength, MarkdownParseOptions options, MarkdownHtmlRendererOptions rendererOptions) : IDisposable
    {
        // The node renderers of the options, and the writer they write with, created for the first node they are offered
        private readonly IReadOnlyList<MarkdownHtmlNodeRenderer> _nodeRenderers = rendererOptions.NodeRenderers;
        private readonly bool _hasNodeRenderers = rendererOptions.NodeRenderers.Count > 0;
        private MarkdownHtmlWriter? _nodeWriter;

        // The node that MarkdownHtmlWriter.WriteDefault writes: it is not offered to the node renderers again. And the last node
        // that it wrote, whose state of the blocks around it is the one of the built-in rendering.
        private MarkdownSyntaxNode? _defaultNode;
        private MarkdownSyntaxNode? _lastDefaultNode;
        private int _lastDefaultEnd;

        public MarkdownHtmlRendererOptions RendererOptions => rendererOptions;

        // The generic attributes of the nodes, merged as Markdig merges them, and the classes of the attributes of the first line of a
        // block, which come before the classes of the block
        // The empty cells added to the short rows of the pipe tables of the document, at most MaximumAutocompletedCells as in Markdig
        private const long MaximumAutocompletedCells = 0x80000;
        private long _autocompletedCells;

        private Dictionary<SyntaxNode, ParsedAttributes>? _attributes;
        private Dictionary<SyntaxNode, List<string>>? _blockLineClasses;
        private Dictionary<MarkdownParagraphSyntax, MarkdownBlockSyntax>? _nextBlocks;

        // The HTML is longer than the Markdown: the buffer starts at up to twice its length, so that it is rarely copied. The
        // buffers are rented, so that writing allocates only the string at the end
        private readonly PooledStringBuilder _output = new(sourceLength + Math.Min(sourceLength, 1 << 20) + 16);

        // The text that is not content, which plain text does not write: the tags, the attributes and the HTML
        private readonly PooledStringBuilder _discardedMarkup = new(256);

        /// <summary>Gets or sets a value indicating whether the writer writes plain text: the text of the document without HTML, as the plain text of Markdig.</summary>
        public bool PlainText { get; init; }

        private PooledStringBuilder Markup
        {
            get
            {
                if (!PlainText)
                {
                    return _output;
                }

                if (_discardedMarkup.Length > 4096)
                {
                    _discardedMarkup.Clear();
                }

                return _discardedMarkup;
            }
        }
        private readonly bool _tagFilter = options.Extensions.HasFlag(MarkdownExtensions.TagFilter);
        private readonly bool _hardLineBreaks = options.Extensions.HasFlag(MarkdownExtensions.HardLineBreaks);
        private readonly bool _diagrams = options.Extensions.HasFlag(MarkdownExtensions.Diagrams);
        private readonly bool _autoIdentifiers = options.Extensions.HasFlag(MarkdownExtensions.AutoIdentifiers);
        private readonly bool _mediaLinks = options.Extensions.HasFlag(MarkdownExtensions.MediaLinks);
        private readonly bool _genericAttributes = options.Extensions.HasFlag(MarkdownExtensions.GenericAttributes);

        // Whether the next block is the first of a custom container or of a definition
        private bool _firstInContainer;
        private bool _paragraphOnNewLineInDefinition;

        // In a cell of a table, a pipe escaped by a backslash is a pipe, even in a code span or a link destination
        private bool _inTableCell;

        // The text tokens whose whitespace is trimmed at the start and at the end of a cell of a pipe table
        private (SyntaxToken Start, SyntaxToken End) _trimmedCellTokens;
        private static readonly char[] PipeTableWhitespace = [' ', '\t', '\n', '\f', '\r', '\u00A0', '\u1680', '\u2000', '\u2001', '\u2002', '\u2003', '\u2004', '\u2005', '\u2006', '\u2007', '\u2008', '\u2009', '\u200A', '\u202F', '\u205F', '\u3000'];

        // The checkbox of a task list item, which its first paragraph starts with
        private string? _pendingCheckbox;

        // The footnotes and their numbers, and the links back to the references of a footnote, which its last paragraph ends with
        private MarkdownFootnotes? _footnotes;
        private MarkdownParagraphSyntax? _backLinksParagraph;
        private string? _backLinks;

        public override string ToString() => _output.ToString();

        public ReadOnlySpan<char> Written => _output.AsSpan();

        public void Dispose()
        {
            _output.Dispose();
            _discardedMarkup.Dispose();
        }

        // The classes and the properties that Bootstrap and the direction of the text add after the generic attributes
        private Dictionary<SyntaxNode, (List<string> Classes, List<KeyValuePair<string, string?>> Properties)>? _extraAttributes;

        // The attributes of the nodes of a document: the generic attributes, and the ones of Bootstrap and of the direction of the
        // text. The content of a grid cell is a document of its own, which its writer reads.
        public void CollectAllAttributes(MarkdownSyntaxTree tree)
        {
            // Finding the targets of the attributes walks every node and token: skip it for the documents that have none
            if (tree.GetRoot().HasGenericAttributes)
            {
                CollectAttributes(tree.GetRoot());
            }

            if (tree.Options.Extensions.HasFlag(MarkdownExtensions.Bootstrap) || tree.Options.Extensions.HasFlag(MarkdownExtensions.Globalization))
            {
                CollectExtraAttributes(tree.GetRoot());
            }
        }

        public void CollectExtraAttributes(MarkdownDocumentSyntax document)
        {
            _extraAttributes = [];
            var bootstrap = options.Extensions.HasFlag(MarkdownExtensions.Bootstrap);
            var globalization = options.Extensions.HasFlag(MarkdownExtensions.Globalization);
            var directions = new Dictionary<SyntaxNode, bool>();
            foreach (var node in document.DescendantNodesAndSelf())
            {
                if (bootstrap)
                {
                    switch (node)
                    {
                        case MarkdownLinkSyntax { IsImage: true }:
                            AddClass(node, "img-fluid");
                            break;
                        case MarkdownTableSyntax or MarkdownGridTableSyntax:
                            AddClass(node, "table");
                            break;
                        case MarkdownAlertSyntax alert:
                            AddClass(node, "alert");
                            if (alert.AlertKind.Length <= 16)
                            {
                                AddClass(node, alert.AlertKind.ToUpperInvariant() switch
                                {
                                    "NOTE" => "alert-primary",
                                    "TIP" => "alert-success",
                                    "IMPORTANT" => "alert-info",
                                    "WARNING" => "alert-warning",
                                    "CAUTION" => "alert-danger",
                                    _ => "alert-dark",
                                });
                            }

                            AddProperty(node, "role", "alert");
                            if (alert.DescendantNodes().OfType<MarkdownParagraphSyntax>().LastOrDefault() is { } last)
                            {
                                AddClass(last, "mb-0");
                            }

                            break;
                        case MarkdownBlockQuoteSyntax:
                            AddClass(node, "blockquote");
                            break;
                        case MarkdownFigureSyntax:
                            AddClass(node, "figure");
                            break;
                        case MarkdownFigureCaptionSyntax:
                            AddClass(node, "figure-caption");
                            break;
                    }
                }

                if (globalization && node is not (MarkdownTableRowSyntax or MarkdownTableCellSyntax or MarkdownListItemSyntax) && IsRightToLeft(node, directions))
                {
                    AddProperty(node, "dir", "rtl");
                    if (node is MarkdownTableSyntax)
                    {
                        AddProperty(node, "align", "right");
                    }
                }
            }

            void AddClass(SyntaxNode node, string cssClass) => Get(node).Classes.Add(cssClass);

            void AddProperty(SyntaxNode node, string name, string value)
            {
                var properties = Get(node).Properties;
                if (!properties.Exists(property => string.Equals(property.Key, name, StringComparison.Ordinal)))
                {
                    properties.Add(new(name, value));
                }
            }

            (List<string> Classes, List<KeyValuePair<string, string?>> Properties) Get(SyntaxNode node)
            {
                if (!_extraAttributes.TryGetValue(node, out var extra))
                {
                    extra = ([], []);
                    _extraAttributes[node] = extra;
                }

                return extra;
            }
        }

        // As in Markdig, the direction of a node is the one of its first child, down to text, whose first strong character
        // gives it. The nodes of the chain of first children have the same direction.
        private bool IsRightToLeft(SyntaxNode node, Dictionary<SyntaxNode, bool> directions)
        {
            var chain = new List<SyntaxNode>();
            bool result;
            SyntaxNodeOrToken current = node;
            while (true)
            {
                if (current.AsNode() is { } currentNode)
                {
                    if (directions.TryGetValue(currentNode, out result))
                    {
                        break;
                    }

                    chain.Add(currentNode);
                    if (currentNode is MarkdownCodeSpanSyntax or MarkdownAutolinkSyntax { IsExtended: false } or MarkdownRawHtmlSyntax or MarkdownMathInlineSyntax or MarkdownFootnoteReferenceSyntax or MarkdownAbbreviationSyntax or MarkdownCodeBlockSyntax or MarkdownHtmlBlockSyntax or MarkdownThematicBreakSyntax or MarkdownMathBlockSyntax or MarkdownGridTableSyntax or MarkdownLinkReferenceDefinitionSyntax or MarkdownJiraLinkSyntax)
                    {
                        result = false;
                        break;
                    }

                    var first = GetFirstContent(currentNode);
                    if (first.RawKind == 0)
                    {
                        result = false;
                        break;
                    }

                    current = first;
                    continue;
                }

                var token = current.AsToken();
                result = (SyntaxKind)token.RawKind switch
                {
                    SyntaxKind.TextToken or SyntaxKind.EscapedCharacterToken => StartsWithRightToLeft(token),
                    SyntaxKind.EmojiToken or SyntaxKind.ExtendedAutolinkToken => TextDirection.StartsWithRightToLeft(token.Text),
                    _ => false,
                };
                break;
            }

            foreach (var item in chain)
            {
                directions[item] = result;
            }

            return result;
        }

        // Markdig merges the literal of a construct that it does not parse, such as ], ! or a backslash, with the text that follows
        // it, but not an emphasis delimiter that can open or close, nor the text before an escaped character: when the text has no
        // character with a direction, the direction is the one of the text that follows it.
        private bool StartsWithRightToLeft(SyntaxToken token)
        {
            var direction = TextDirection.GetDirection((SyntaxKind)token.RawKind is SyntaxKind.EscapedCharacterToken ? token.Text.AsSpan(1) : token.Text);
            if (direction is not null || token.Parent is not { } parent)
            {
                return direction ?? false;
            }

            var siblings = new List<SyntaxNodeOrToken>(parent.ChildNodesAndTokens());
            var index = siblings.FindIndex(child => child.IsToken && child.SpanStart == token.SpanStart && child.RawKind == token.RawKind);
            if (index < 0 || IsDelimiterRun(siblings, index))
            {
                return false;
            }

            for (var i = index + 1; i < siblings.Count && siblings[i].IsToken && (SyntaxKind)siblings[i].RawKind is SyntaxKind.TextToken && siblings[i].SpanStart == siblings[i - 1].Span.End && !IsDelimiterRun(siblings, i); i++)
            {
                direction = TextDirection.GetDirection(siblings[i].AsToken().Text);
                if (direction is not null)
                {
                    return direction.Value;
                }
            }

            return false;
        }

        // Whether a text token is a run of emphasis delimiters that can open or close, from the characters around it (inserted and
        // marked text need two delimiters), or a pipe, which the pipe tables of Markdig replace by a literal when it is not in a table
        private bool IsDelimiterRun(List<SyntaxNodeOrToken> siblings, int index)
        {
            var token = siblings[index];
            if (options.Extensions.HasFlag(MarkdownExtensions.PipeTables) && (SyntaxKind)token.RawKind is SyntaxKind.TextToken && token.AsToken().Text is ['|', ..])
            {
                return true;
            }

            if ((SyntaxKind)token.RawKind is not SyntaxKind.TextToken || token.AsToken().Text is not [var delimiter and ('*' or '_' or '~' or '^' or '+' or '='), ..] text || text.AsSpan().ContainsAnyExcept(delimiter) || (delimiter is '+' or '=' && text.Length < 2))
            {
                return false;
            }

            var previous = index > 0 && siblings[index - 1] is { IsToken: true } before && before.Span.End == token.SpanStart && before.AsToken().Text is [.., var last] ? last : ' ';
            var next = index + 1 < siblings.Count && siblings[index + 1] is { IsToken: true } after && after.SpanStart == token.Span.End && after.AsToken().Text is [var first, ..] ? first : ' ';
            var previousIsWhitespace = char.IsWhiteSpace(previous);
            var nextIsWhitespace = char.IsWhiteSpace(next);
            var previousIsPunctuation = char.IsPunctuation(previous) || char.IsSymbol(previous);
            var nextIsPunctuation = char.IsPunctuation(next) || char.IsSymbol(next);
            var leftFlanking = !nextIsWhitespace && (!nextIsPunctuation || previousIsWhitespace || previousIsPunctuation);
            var rightFlanking = !previousIsWhitespace && (!previousIsPunctuation || nextIsWhitespace || nextIsPunctuation);
            return delimiter == '_'
                ? (leftFlanking && (!rightFlanking || previousIsPunctuation)) || (rightFlanking && (!leftFlanking || nextIsPunctuation))
                : leftFlanking || rightFlanking;
        }

        // The first child that is content: not a marker, a delimiter, a bracket or generic attributes
        private static SyntaxNodeOrToken GetFirstContent(SyntaxNode node)
        {
            var children = node.ChildNodesAndTokens();
            for (var i = 0; i < children.Count; i++)
            {
                var child = children[i];
                if (child.AsNode() is { } childNode)
                {
                    if (childNode is not MarkdownGenericAttributesSyntax && !(node is MarkdownTableSyntax && childNode is MarkdownTableRowSyntax { IsDelimiter: true }))
                    {
                        return childNode;
                    }

                    continue;
                }

                if (node is MarkdownBlockSyntax and not (MarkdownParagraphSyntax or MarkdownHeadingSyntax or MarkdownDefinitionTermSyntax or MarkdownFigureCaptionSyntax))
                {
                    // The tokens of a container block are its markers
                    continue;
                }

                if ((SyntaxKind)child.RawKind is SyntaxKind.TextToken or SyntaxKind.EscapedCharacterToken or SyntaxKind.EmojiToken or SyntaxKind.EntityToken or SyntaxKind.SmartyPantToken or SyntaxKind.LineEndingToken or SyntaxKind.HardLineBreakToken or SyntaxKind.ExtendedAutolinkToken)
                {
                    return child;
                }

                // Markdig replaces a bracket that a ] closes without a link by a literal, which has no direction, but keeps the
                // content of a bracket that nothing closes in it
                if ((SyntaxKind)child.RawKind is SyntaxKind.OpenBracketToken or SyntaxKind.ExclamationOpenBracketToken && IsClosed(children, i))
                {
                    return child;
                }
            }

            return default;

            static bool IsClosed(ChildSyntaxList children, int index)
            {
                for (var i = index + 1; i < children.Count; i++)
                {
                    if (children[i] is { IsToken: true } token && (SyntaxKind)token.RawKind is SyntaxKind.TextToken && token.AsToken().Text is [']', ..])
                    {
                        return true;
                    }
                }

                return false;
            }
        }

        // The ids of the pragma lines, which go to the first block of each line, the outermost one, as Markdig gives them before it
        // renders the document, and the blocks of a line whose id is set, which an anchor with the id precedes
        private Dictionary<SyntaxNode, string>? _pragmaIds;
        private Dictionary<SyntaxNode, string>? _pragmaAnchors;
        private Dictionary<object, string>? _pragmaGridParts;

        public void CollectPragmaLines(MarkdownSyntaxTree tree)
        {
            _pragmaIds = [];
            _pragmaAnchors = [];
            var text = tree.GetText();
            var lines = new HashSet<int>();
            Visit(tree.GetRoot());

            void Visit(SyntaxNode container)
            {
                foreach (var child in container.ChildNodes())
                {
                    if (child is MarkdownGridTableSyntax grid)
                    {
                        Add(grid);
                        var firstLine = text.GetLine(grid.SpanStart).LineNumber;
                        foreach (var row in grid.Rows)
                        {
                            AddGridPart(row, row.LineIndex);
                            foreach (var cell in row.Cells)
                            {
                                AddGridPart(cell, cell.LineIndex);
                            }
                        }

                        void AddGridPart(object part, int lineIndex)
                        {
                            if (lineIndex >= 0 && lines.Add(firstLine + lineIndex))
                            {
                                (_pragmaGridParts ??= [])[part] = "pragma-line-" + (firstLine + lineIndex).ToString(CultureInfo.InvariantCulture);
                            }
                        }

                        continue;
                    }

                    if (child is MarkdownTableSyntax table)
                    {
                        Add(table);
                        foreach (var row in table.ChildNodes().OfType<MarkdownTableRowSyntax>())
                        {
                            if (!row.IsDelimiter)
                            {
                                Add(row);
                            }
                        }

                        continue;
                    }

                    if (child is MarkdownBlockSyntax and not (MarkdownHtmlBlockSyntax or MarkdownLinkReferenceDefinitionSyntax or MarkdownFootnoteDefinitionSyntax or MarkdownFrontMatterSyntax or MarkdownAbbreviationDefinitionSyntax))
                    {
                        if (child is not MarkdownParagraphSyntax paragraph || _attributes is null || !IsAttributesParagraph(paragraph))
                        {
                            Add(child);
                        }
                    }

                    if (MarkdownDocumentSyntax.IsBlockContainer(child))
                    {
                        Visit(child);
                    }
                }
            }

            void Add(SyntaxNode block)
            {
                var line = MarkdigLines.GetLine(block);
                if (!lines.Add(line))
                {
                    return;
                }

                var id = "pragma-line-" + line.ToString(CultureInfo.InvariantCulture);
                if (GetId(block) is null)
                {
                    _pragmaIds[block] = id;
                }
                else
                {
                    _pragmaAnchors[block] = id;
                }
            }
        }

        private string? GetId(SyntaxNode node)
        {
            if (_attributes is not null && _attributes.TryGetValue(node, out var attributes) && attributes.Id is not null)
            {
                return attributes.Id;
            }

            return _autoIdentifiers && node is MarkdownHeadingSyntax heading ? heading.Identifier : null;
        }

        // Markdig writes the anchor of a heading that has inlines at their start, and before the other blocks
        private static bool HasPragmaAnchorInside(MarkdownBlockSyntax block) => block is MarkdownHeadingSyntax heading && heading.Inlines.Any(inline => inline.AsNode() is not MarkdownGenericAttributesSyntax);

        // The anchor of the pragma line of a block whose id is set, which goes before it, or at the start of a heading
        private void AppendPragmaAnchor(SyntaxNode block)
        {
            if (_pragmaAnchors is not null && _pragmaAnchors.TryGetValue(block, out var id))
            {
                Markup.Append("<a id=\"").Append(id).Append("\"></a>");
            }
        }

        // The id of the last attributes wins, their classes are added, and a property is only added when there is none with its name
        public void CollectAttributes(MarkdownDocumentSyntax document)
        {
            _attributes = [];
            _nextBlocks = MarkdownGenericAttributesSyntax.FindTargets(document, Merge);
        }

        private void Merge(MarkdownGenericAttributesSyntax attributes, SyntaxNode target)
        {
            {
                if (!_attributes!.TryGetValue(target, out var merged))
                {
                    merged = new ParsedAttributes(null, [], []);
                }

                // A property is added when the element has none with its name from other attributes: the ones of the same attributes are all kept
                var properties = merged.Properties;
                var existingCount = properties.Count;
                foreach (var property in attributes.Properties)
                {
                    if (rendererOptions.AttributeFilter(property.Key) && properties.FindIndex(0, existingCount, existing => string.Equals(existing.Key, property.Key, StringComparison.Ordinal)) < 0)
                    {
                        properties.Add(property);
                    }
                }

                if (attributes.IsBlockLine)
                {
                    _blockLineClasses ??= [];
                    _blockLineClasses[target] = [.. attributes.Classes];
                }
                else
                {
                    merged.Classes.AddRange(attributes.Classes);
                }

                _attributes![target] = merged with { Id = attributes.Id ?? merged.Id };
            }
        }

        // The generic attributes of the element of the node: its id, the classes of the element and the ones of the attributes, and
        // the other attributes
        private void AppendAttributes(SyntaxNode node, string? leadingClasses = null, string? trailingClasses = null, string? id = null)
        {
            ParsedAttributes? attributes = null;
            _ = _attributes?.TryGetValue(node, out attributes);
            AppendAttributes(node, attributes, leadingClasses, trailingClasses, id);
        }

        // A class of the class attribute, after a space unless it is the first one
        private void AppendClass(string? cssClass, ref bool first)
        {
            if (cssClass is null)
            {
                return;
            }

            if (!first)
            {
                Markup.Append(' ');
            }

            AppendAttributeEscaped(cssClass);
            first = false;
        }

        private void AppendAttributes(SyntaxNode node, ParsedAttributes? attributes, string? leadingClasses = null, string? trailingClasses = null, string? id = null)
        {
            id ??= attributes?.Id;
            if (id is null && _pragmaIds is not null)
            {
                id = _pragmaIds.GetValueOrDefault(node);
            }

            if (id is not null)
            {
                Markup.Append(" id=\"");
                AppendAttributeEscaped(id);
                Markup.Append('"');
            }

            List<string>? blockLineClasses = null;
            _ = _blockLineClasses?.TryGetValue(node, out blockLineClasses);
            (List<string> Classes, List<KeyValuePair<string, string?>> Properties) extra = default;
            _ = _extraAttributes?.TryGetValue(node, out extra);
            var hasClasses = attributes is { Classes.Count: > 0 } || blockLineClasses is { Count: > 0 } || extra.Classes is { Count: > 0 };
            if (hasClasses || leadingClasses is not null || trailingClasses is not null)
            {
                Markup.Append(" class=\"");
                var first = true;
                AppendClass(leadingClasses, ref first);
                if (blockLineClasses is not null)
                {
                    foreach (var cssClass in blockLineClasses)
                    {
                        AppendClass(cssClass, ref first);
                    }
                }

                AppendClass(trailingClasses, ref first);
                if (attributes is not null)
                {
                    foreach (var cssClass in attributes.Classes)
                    {
                        AppendClass(cssClass, ref first);
                    }
                }

                if (extra.Classes is not null)
                {
                    foreach (var cssClass in extra.Classes)
                    {
                        AppendClass(cssClass, ref first);
                    }
                }

                Markup.Append('"');
            }

            foreach (var property in attributes is null ? [] : CollectionsMarshal.AsSpan(attributes.Properties))
            {
                Markup.Append(' ').Append(property.Key).Append("=\"");
                AppendAttributeEscaped(property.Value ?? "");
                Markup.Append('"');
            }

            // The attributes of Bootstrap and of the direction of the text, unless the element has them
            foreach (var property in extra.Properties is null ? [] : CollectionsMarshal.AsSpan(extra.Properties))
            {
                if (attributes is null || !attributes.Properties.Exists(existing => string.Equals(existing.Key, property.Key, StringComparison.Ordinal)))
                {
                    Markup.Append(' ').Append(property.Key).Append("=\"");
                    AppendAttributeEscaped(property.Value ?? "");
                    Markup.Append('"');
                }
            }
        }

        // Before a block that Markdig starts on a new line only when it is not the first block of a custom container, which is
        // written just after the <div>, or of a definition, after the <dd>: a paragraph, a heading, a thematic break, HTML. In a
        // definition, Markdig starts a paragraph on a new line when the definition list is not the first block of its container.
        private void CrUnlessFirstInContainer(bool paragraph = false)
        {
            if (_firstInContainer)
            {
                _firstInContainer = false;
                if (!(paragraph && _paragraphOnNewLineInDefinition))
                {
                    return;
                }
            }

            Cr();
        }

        // Plain text ends with a line ending, as the last block of Markdig ends its line
        public void EndLine() => Cr();

        private void Cr()
        {
            _firstInContainer = false;
            if (_output.Length > 0 && _output.LastChar != '\n')
            {
                _output.Append('\n');
            }
        }

        // As Markdig: the terms, then the blocks of the definition in <dd>, where a paragraph alone is written without <p>
        private void WriteDefinitionList(MarkdownDefinitionListSyntax list)
        {
            Cr();
            Markup.Append("<dl");
            AppendAttributes(list);
            Markup.Append(">\n");
            var paragraphOnNewLine = !IsFirstBlockForMarkdig(list);
            foreach (var item in list.Items)
            {
                foreach (var term in item.Terms)
                {
                    Markup.Append("<dt");
                    AppendAttributes(term);
                    Markup.Append('>');
                    WriteInlines(term.Inlines);
                    Markup.Append("</dt>");
                    Cr();
                }

                // Markdig moves the definitions out of the definition
                var blocks = item.Blocks.Where(block => block is not (MarkdownLinkReferenceDefinitionSyntax or MarkdownAbbreviationDefinitionSyntax)).ToList();
                if (blocks.Count == 0 && item.BlankLineCount == 0)
                {
                    continue;
                }

                Markup.Append("<dd");
                AppendAttributes(item);
                Markup.Append('>');
                if (blocks is [MarkdownParagraphSyntax paragraph] && item.BlankLineCount == 0)
                {
                    WriteUnwrappedParagraph(paragraph);
                    Markup.Append("</dd>");
                    Cr();
                    continue;
                }

                _firstInContainer = true;
                _paragraphOnNewLineInDefinition = paragraphOnNewLine;
                WriteBlocks(blocks);
                _firstInContainer = false;
                _paragraphOnNewLineInDefinition = false;
                Cr();
                Markup.Append("</dd>\n");
            }

            Cr();
            Markup.Append("</dl>");
            Cr();
        }

        // Markdig writes the blocks of a definition itself, so they read whether the definition list is the first block of its container
        private static bool IsFirstBlockForMarkdig(MarkdownBlockSyntax block) => block.Parent switch
        {
            MarkdownDefinitionItemSyntax { Parent: MarkdownDefinitionListSyntax list } => IsFirstBlockForMarkdig(list),
            { } parent => parent.ChildNodes().FirstOrDefault(node => node is MarkdownBlockSyntax) == block,
            _ => true,
        };

        public void WriteBlocks(IReadOnlyList<MarkdownBlockSyntax> blocks)
        {
            foreach (var block in blocks)
            {
                WriteBlock(block);
            }
        }

        public void WriteBlock(MarkdownBlockSyntax block)
        {
            // WriteDefault writes the block that a node renderer is writing: its anchor is written, and the renderers declined it
            if (ReferenceEquals(_defaultNode, block))
            {
                _defaultNode = null;
            }
            else
            {
                if (_pragmaAnchors is not null && !HasPragmaAnchorInside(block) && _pragmaAnchors.ContainsKey(block))
                {
                    Cr();
                    AppendPragmaAnchor(block);
                    Markup.Append('\n');
                }

                if (_hasNodeRenderers && !(block is MarkdownParagraphSyntax attributes && _attributes is not null && IsAttributesParagraph(attributes)) && TryWriteBlockWithNodeRenderers(block))
                {
                    return;
                }
            }

            switch (block)
            {
                case MarkdownParagraphSyntax paragraph when _attributes is not null && IsAttributesParagraph(paragraph):
                    // Its attributes are for the next block
                    break;

                case MarkdownParagraphSyntax paragraph when ReferenceEquals(paragraph, _unwrappedParagraph):
                    WriteInlines(paragraph.Inlines);
                    break;

                case MarkdownParagraphSyntax paragraph:
                {
                    var parent = GetParagraphContainer(paragraph);
                    var tight = IsTight(parent);
                    // In a footer, Markdig does not start a heading, a thematic break or HTML on a new line after a paragraph
                    var inFooter = parent is MarkdownFooterSyntax;
                    if (tight)
                    {
                        _firstInContainer = false;
                    }
                    else
                    {
                        CrUnlessFirstInContainer(paragraph: true);
                        Markup.Append("<p");
                        AppendAttributes(paragraph);
                        Markup.Append('>');
                    }

                    if (_pendingCheckbox is not null)
                    {
                        _output.Append(_pendingCheckbox);
                        _pendingCheckbox = null;
                    }

                    WriteInlines(paragraph.Inlines, keepTrailingLineBreak: paragraph == _backLinksParagraph);
                    if (paragraph == _backLinksParagraph)
                    {
                        Markup.Append(_backLinks);
                        _backLinks = null;
                    }

                    if (inFooter)
                    {
                        _firstInContainer = true;
                    }

                    if (!tight)
                    {
                        Markup.Append("</p>");
                        Cr();
                    }

                    break;
                }

                case MarkdownHeadingSyntax heading:
                    CrUnlessFirstInContainer();
                    Markup.Append("<h").Append(heading.Level);
                    AppendAttributes(heading, id: _autoIdentifiers ? heading.Identifier : null);
                    Markup.Append('>');
                    if (HasPragmaAnchorInside(heading))
                    {
                        AppendPragmaAnchor(heading);
                    }

                    WriteInlines(heading.ChildNodesAndTokens(), keepTrailingLineBreak: true, skippedKind: SyntaxKind.AtxHeadingMarkerToken, otherSkippedKind: SyntaxKind.AtxHeadingClosingSequenceToken, lastSkippedKind: SyntaxKind.SetextHeadingUnderlineToken);
                    Markup.Append("</h").Append(heading.Level).Append('>');
                    Cr();
                    break;

                case MarkdownThematicBreakSyntax:
                    CrUnlessFirstInContainer();
                    Markup.Append("<hr");
                    AppendAttributes(block);
                    Markup.Append(" />");
                    Cr();
                    break;

                case MarkdownAlertSyntax alert:
                    Cr();
                    Markup.Append("<div");
                    AppendAttributes(alert, leadingClasses: "markdown-alert markdown-alert-" + alert.AlertKind.ToLowerInvariant());
                    Markup.Append(">\n");
                    if (AlertTitles.TryGetValue(alert.AlertKind, out var title))
                    {
                        Markup.Append(title).Append('\n');
                    }

                    WriteBlocks(alert.Blocks);
                    Cr();
                    Markup.Append("</div>");
                    Cr();
                    break;

                case MarkdownCustomContainerSyntax container:
                    // Markdig writes the first block just after the <div>, and the </div> just after the last block
                    Cr();
                    Markup.Append("<div");
                    AppendAttributes(container, trailingClasses: container.Name is { Length: > 0 } name ? name : null);
                    Markup.Append('>');
                    _firstInContainer = true;
                    WriteBlocks(container.Blocks);
                    _firstInContainer = false;
                    Markup.Append("</div>");
                    Cr();
                    break;

                case MarkdownDefinitionListSyntax list:
                    WriteDefinitionList(list);
                    break;

                case MarkdownFigureSyntax figure:
                    Cr();
                    Markup.Append("<figure");
                    AppendAttributes(figure);
                    Markup.Append(">\n");
                    // Markdig writes the </figure> just after the last block
                    WriteBlocks(figure.Blocks);
                    Markup.Append("</figure>");
                    Cr();
                    break;

                case MarkdownFigureCaptionSyntax caption:
                    Cr();
                    Markup.Append("<figcaption");
                    AppendAttributes(caption);
                    Markup.Append('>');
                    WriteInlines(caption.Inlines);
                    Markup.Append("</figcaption>");
                    Cr();
                    break;

                case MarkdownFooterSyntax footer:
                    // Markdig writes the first block just after the <footer>, and the </footer> just after the last block
                    Cr();
                    Markup.Append("<footer");
                    AppendAttributes(footer);
                    Markup.Append('>');
                    _firstInContainer = true;
                    WriteBlocks(footer.Blocks);
                    _firstInContainer = false;
                    Markup.Append("</footer>");
                    Cr();
                    break;

                case MarkdownBlockQuoteSyntax quote:
                    Cr();
                    Markup.Append("<blockquote");
                    AppendAttributes(quote);
                    Markup.Append('>');
                    Cr();
                    WriteBlocks(quote.Blocks);
                    Cr();
                    Markup.Append("</blockquote>");
                    Cr();
                    break;

                case MarkdownListSyntax list:
                    Cr();
                    if (list.StartNumber is { } start)
                    {
                        Markup.Append("<ol");
                        Markup.Append(list.OrderedStyle switch
                        {
                            MarkdownOrderedListStyle.LowerAlpha => " type=\"a\"",
                            MarkdownOrderedListStyle.UpperAlpha => " type=\"A\"",
                            MarkdownOrderedListStyle.LowerRoman => " type=\"i\"",
                            MarkdownOrderedListStyle.UpperRoman => " type=\"I\"",
                            _ => "",
                        });
                        if (start != 1)
                        {
                            Markup.Append(" start=\"").Append(start.ToString(CultureInfo.InvariantCulture)).Append('"');
                        }

                        AppendAttributes(list);
                        Markup.Append('>');
                    }
                    else
                    {
                        Markup.Append("<ul");
                        AppendAttributes(list);
                        Markup.Append('>');
                    }

                    Cr();
                    foreach (var item in list.Items)
                    {
                        WriteListItem(item);
                    }

                    Cr();
                    Markup.Append(list.IsOrdered ? "</ol>" : "</ul>");
                    Cr();
                    break;

                case MarkdownCodeBlockSyntax code:
                {
                    var language = code.Language;
                    if (_diagrams && code.IsFenced && language is "mermaid" or "nomnoml")
                    {
                        // A diagram is its code, where only < and & are escaped, in the element that its script reads
                        var tag = language == "mermaid" ? "pre" : "div";
                        Cr();
                        Markup.Append('<').Append(tag).Append(" class=\"").Append(language).Append("\">");
                        AppendCode(code, soft: true);
                        Markup.Append("</").Append(tag).Append('>');
                        Cr();
                        break;
                    }

                    Cr();
                    Markup.Append("<pre><code");
                    AppendAttributes(code, trailingClasses: language is not null ? "language-" + language : null);
                    Markup.Append('>');
                    AppendCode(code, soft: false);
                    Markup.Append("</code></pre>");
                    Cr();
                    break;
                }

                case MarkdownMathBlockSyntax math:
                    Cr();
                    Markup.Append("<div");
                    AppendAttributes(math, leadingClasses: "math");
                    Markup.Append(">\n\\[\n");
                    AppendEscaped(math.Math);
                    Markup.Append("\\]</div>");
                    Cr();
                    break;

                case MarkdownHtmlBlockSyntax html:
                    CrUnlessFirstInContainer();
                    AppendHtmlBlock(html);
                    Cr();
                    break;

                case MarkdownGridTableSyntax grid:
                    WriteGridTable(grid);
                    break;

                case MarkdownTableSyntax table:
                    WriteTable(table);
                    break;

                case MarkdownExtensionBlockSyntax extension:
                    WriteExtensionBlock(extension);
                    break;
            }
        }

        // The footnotes that are referred to, at the end of the document, each one with the links back to its references
        private bool TryWriteBlockWithNodeRenderers(MarkdownBlockSyntax block)
        {
            var start = _output.Length;
            _lastDefaultNode = null;
            if (!TryWriteWithNodeRenderers(block))
            {
                return false;
            }

            // A block that the node renderer wrote with WriteDefault, and nothing after, updated the state itself, and a block that
            // wrote nothing leaves the next block first in its container
            if (_output.Length != start && !(ReferenceEquals(_lastDefaultNode, block) && _lastDefaultEnd == _output.Length))
            {
                _firstInContainer = false;
            }

            if (block is MarkdownParagraphSyntax)
            {
                // The checkbox of a task list item starts its first paragraph: it starts what the node renderer wrote
                if (_pendingCheckbox is not null)
                {
                    if (start < _output.Length && _output.AsSpan()[start] == '\n')
                    {
                        start++;
                    }

                    _output.Insert(start, _pendingCheckbox);
                    _pendingCheckbox = null;
                }

                // The links back to the references of a footnote end its last paragraph, or a paragraph of their own, unless
                // the node renderer wrote the paragraph with WriteDefault, which wrote them
                if (block == _backLinksParagraph && _backLinks is not null)
                {
                    Cr();
                    Markup.Append("<p>").Append(_backLinks).Append("</p>");
                    Cr();
                    _backLinks = null;
                }
            }

            return true;
        }

        // The paragraph that is the only block of a grid cell or of a definition, which is written without <p>: the node renderers
        // can write it, and its default rendering is its inlines
        private MarkdownParagraphSyntax? _unwrappedParagraph;

        private void WriteUnwrappedParagraph(MarkdownParagraphSyntax paragraph)
        {
            if (_hasNodeRenderers)
            {
                var previous = _unwrappedParagraph;
                _unwrappedParagraph = paragraph;
                try
                {
                    if (TryWriteWithNodeRenderers(paragraph))
                    {
                        return;
                    }
                }
                finally
                {
                    _unwrappedParagraph = previous;
                }
            }

            WriteInlines(paragraph.Inlines);
        }

        private bool TryWriteWithNodeRenderers(MarkdownSyntaxNode node)
        {
            var writer = _nodeWriter ??= new MarkdownHtmlWriter(this);
            foreach (var renderer in _nodeRenderers)
            {
                if (renderer.TryWrite(node, writer))
                {
                    return true;
                }
            }

            return false;
        }

        public void WriteDefault(MarkdownSyntaxNode node)
        {
            var previous = _defaultNode;
            _defaultNode = node;
            try
            {
                switch (node)
                {
                    case MarkdownBlockSyntax block:
                        WriteBlock(block);
                        break;
                    case MarkdownInlineSyntax inline:
                        WriteInline(inline);
                        break;
                }
            }
            finally
            {
                _defaultNode = previous;
                _lastDefaultNode = node;
                _lastDefaultEnd = _output.Length;
            }
        }

        public void WriteInlineSequence(IEnumerable<SyntaxNodeOrToken> inlines)
        {
            if (inlines is ChildSyntaxList list)
            {
                WriteInlines(list);
                return;
            }

            foreach (var child in inlines)
            {
                if (child.AsNode() is MarkdownInlineSyntax inline)
                {
                    if (inline is not MarkdownGenericAttributesSyntax)
                    {
                        WriteInline(inline);
                    }
                }
                else if (child.IsToken)
                {
                    WriteToken(child.AsToken());
                }
            }
        }

        public void AppendMarkup(ReadOnlySpan<char> html) => Markup.Append(html);

        public void AppendAttributeValue(ReadOnlySpan<char> value) => AppendAttributeEscaped(value);

        public void AppendUrl(string url) => AppendAttributeEscaped(NormalizeUrl(url));

        public void AppendNodeAttributes(MarkdownSyntaxNode node, string? classes) => AppendAttributes(node, trailingClasses: classes);

        public void EnsureNewLine() => Cr();

        // The container whose tightness the paragraphs of a block follow: the paragraphs of a tight list have no <p>, as the ones
        // of its custom containers in Markdig
        private static MarkdownSyntaxNode? GetParagraphContainer(MarkdownBlockSyntax block)
        {
            var parent = (MarkdownSyntaxNode?)block.Parent;
            while (parent is MarkdownCustomContainerSyntax or MarkdownDefinitionItemSyntax or MarkdownDefinitionListSyntax or MarkdownFigureSyntax)
            {
                parent = (MarkdownSyntaxNode?)parent.Parent;
            }

            return parent;
        }

        // The paragraphs of a footer have no <p> either
        private static bool IsTight(MarkdownSyntaxNode? paragraphContainer) => paragraphContainer is MarkdownListItemSyntax { Parent: MarkdownListSyntax { IsTight: true } } or MarkdownFooterSyntax;

        // Whether the trailing trivia of a child ends its line
        private static bool EndsLine(SyntaxNodeOrToken child)
        {
            foreach (var trivia in child.GetTrailingTrivia())
            {
                if (trivia.RawKind == (int)SyntaxKind.EndOfLineTrivia)
                {
                    return true;
                }
            }

            return false;
        }

        // The block of a syntax extension that no node renderer writes: its lines in <pre><code>, its blocks, or its text and its
        // inlines in <p>, without its markers and its info. The text of a line starts on a new line.
        private void WriteExtensionBlock(MarkdownExtensionBlockSyntax extension)
        {
            // As a code block, which starts on a new line even as the first block of a container
            if (extension.Lines.Count > 0)
            {
                Cr();
                Markup.Append("<pre><code");
                AppendAttributes(extension);
                Markup.Append('>');
                AppendEscaped(extension.Content);
                Markup.Append("</code></pre>");
                Cr();
                return;
            }

            if (extension.IsFenced)
            {
                WriteBlocks(extension.Blocks);
                return;
            }

            // As a paragraph: no <p> in a tight list
            var parent = GetParagraphContainer(extension);
            var tight = IsTight(parent);
            if (tight)
            {
                _firstInContainer = false;
            }
            else
            {
                CrUnlessFirstInContainer(paragraph: true);
                Markup.Append("<p");
                AppendAttributes(extension);
                Markup.Append('>');
            }

            SyntaxNodeOrToken previous = default;
            foreach (var child in extension.Inlines)
            {
                if (previous.RawKind != 0 && EndsLine(previous))
                {
                    _output.Append('\n');
                }

                if (child.AsNode() is MarkdownInlineSyntax inline)
                {
                    if (inline is not MarkdownGenericAttributesSyntax)
                    {
                        WriteInline(inline);
                    }
                }
                else
                {
                    WriteToken(child.AsToken());
                }

                previous = child;
            }

            if (parent is MarkdownFooterSyntax)
            {
                _firstInContainer = true;
            }

            if (!tight)
            {
                Markup.Append("</p>");
                Cr();
            }
        }

        public void WriteFootnotes(MarkdownDocumentSyntax document)
        {
            // A document with footnotes has the list of the footnotes, even when none is referred to, as 1.x writes it
            if (!document.Blocks.OfType<MarkdownFootnoteDefinitionSyntax>().Any())
            {
                return;
            }

            _footnotes = new MarkdownFootnotes(document);

            Cr();
            Markup.Append("<div class=\"footnotes\">\n<hr />\n<ol>\n");
            foreach (var footnote in _footnotes.Footnotes)
            {
                Markup.Append("<li id=\"fn:").Append(_footnotes.GetNumber(footnote)).Append("\">\n");
                var backLinks = new StringBuilder();
                foreach (var reference in _footnotes.GetReferences(footnote))
                {
                    backLinks.Append("<a href=\"#fnref:").Append(_footnotes.GetReferenceNumber(reference)).Append("\" class=\"footnote-back-ref\">&#8617;</a>");
                }

                // The links end the last paragraph, or a paragraph of their own
                _backLinks = backLinks.ToString();
                _backLinksParagraph = footnote.Blocks.Count > 0 ? footnote.Blocks[^1] as MarkdownParagraphSyntax : null;
                WriteBlocks(footnote.Blocks);
                if (_backLinksParagraph is null)
                {
                    Cr();
                    Markup.Append("<p>").Append(_backLinks).Append("</p>");
                }

                Cr();
                Markup.Append("</li>\n");
            }

            Markup.Append("</ol>\n</div>\n");
        }

        // A paragraph of attributes only, whose last attributes are for the next block, is not written
        private bool IsAttributesParagraph(MarkdownParagraphSyntax paragraph) => _nextBlocks!.ContainsKey(paragraph);

        private void WriteListItem(MarkdownListItemSyntax item)
        {
            Markup.Append("<li");
            AppendAttributes(item);
            Markup.Append('>');
            if (item.IsTask)
            {
                // The checkbox starts the first paragraph, or the item when it starts with another block
                var checkbox = (PlainText, item.IsChecked) switch
                {
                    (true, true) => "[x] ",
                    (true, false) => "[ ] ",
                    (false, true) => "<input checked=\"\" disabled=\"\" type=\"checkbox\"> ",
                    (false, false) => "<input disabled=\"\" type=\"checkbox\"> ",
                };
                if (item.Blocks.Count > 0 && item.Blocks[0] is MarkdownParagraphSyntax)
                {
                    _pendingCheckbox = checkbox;
                }
                else
                {
                    _output.Append(checkbox);
                }
            }

            WriteBlocks(item.Blocks);
            Markup.Append("</li>");
            Cr();
        }

        // A table: the cells of the header row, then the cells of the other rows, as many as the header row has
        private void WriteTable(MarkdownTableSyntax table)
        {
            if (table.Kind() == SyntaxKind.PipeTable)
            {
                WritePipeTable(table);
                return;
            }

            var alignments = table.Alignments.Select(alignment => alignment switch
            {
                MarkdownTableAlignment.Center => " align=\"center\"",
                MarkdownTableAlignment.Left => " align=\"left\"",
                MarkdownTableAlignment.Right => " align=\"right\"",
                _ => "",
            }).ToList();

            Cr();
            Markup.Append("<table");
            AppendAttributes(table);
            Markup.Append(">\n<thead>\n");
            WriteTableRow(table.HeaderRow!, "th", alignments);
            Markup.Append("</thead>\n");
            var rows = table.Rows;
            if (rows.Count > 0)
            {
                Markup.Append("<tbody>\n");
                foreach (var row in rows)
                {
                    WriteTableRow(row, "td", alignments);
                }

                Markup.Append("</tbody>\n");
            }

            Markup.Append("</table>\n");
        }

        // A pipe table of Markdig: as many columns as its longest row has cells, or as its first row with UseHeaderForColumnCount,
        // with the alignments of GFM. A column after the last column of the delimiter row has its alignment. A table without a
        // delimiter row has no <thead>.
        private void WritePipeTable(MarkdownTableSyntax table)
        {
            var pipeTableOptions = options.PipeTableOptions;
            var alignments = table.Alignments;
            var header = table.HeaderRow;
            var rows = table.Rows;
            int columnCount;
            if (pipeTableOptions.UseHeaderForColumnCount)
            {
                columnCount = (header ?? rows[0]).Cells.Count;
            }
            else
            {
                columnCount = header?.Cells.Count ?? 0;
                foreach (var row in rows)
                {
                    columnCount = Math.Max(columnCount, row.Cells.Count);
                }
            }

            // As Markdig, the empty cells that complete the short rows are limited in the document: the rows of a table that would
            // exceed the limit are not completed
            long missingCells = 0;
            foreach (var row in header is null ? rows : rows.Prepend(header))
            {
                missingCells += Math.Max(0, columnCount - row.Cells.Count);
            }

            var completeShortRows = missingCells <= MaximumAutocompletedCells - _autocompletedCells;
            if (completeShortRows)
            {
                _autocompletedCells += missingCells;
            }

            Cr();
            Markup.Append("<table");
            AppendAttributes(table);
            Markup.Append(">\n");
            if (pipeTableOptions.InferColumnWidthsFromSeparator)
            {
                WritePipeTableColumnWidths(table);
            }

            if (header is not null)
            {
                Markup.Append("<thead>\n");
                WritePipeTableRow(header, "th", GetCellCount(header), alignments);
                Markup.Append("</thead>\n");
            }

            if (rows.Count > 0)
            {
                Markup.Append("<tbody>\n");
                foreach (var row in rows)
                {
                    WritePipeTableRow(row, "td", GetCellCount(row), alignments);
                }

                Markup.Append("</tbody>\n");
            }

            Markup.Append("</table>\n");

            int GetCellCount(MarkdownTableRowSyntax row) => completeShortRows ? columnCount : Math.Min(columnCount, row.Cells.Count);
        }

        // The width of each column of the delimiter row, in percent of the dashes of the row, computed as Markdig computes it. No
        // width is written when every column is 0% or 1%.
        private void WritePipeTableColumnWidths(MarkdownTableSyntax table)
        {
            var columns = table.GetPipeTableColumns();
            var totalDashCount = 0;
            foreach (var column in columns)
            {
                totalDashCount += column.DashCount;
            }

            if (totalDashCount == 0)
            {
                return;
            }

            var widths = new float[columns.Count];
            for (var i = 0; i < columns.Count; i++)
            {
                widths[i] = (float)columns[i].DashCount * 100 / totalDashCount;
            }

            if (widths.All(width => width is 0.0f or 1.0f))
            {
                return;
            }

            foreach (var width in widths)
            {
                var rounded = Math.Round(width * 100, MidpointRounding.ToEven) / 100;
                Markup.Append("<col style=\"width:").Append(rounded.ToString("0.##", CultureInfo.InvariantCulture)).Append("%\" />\n");
            }
        }

        // A grid table of Markdig: the widths of its columns, then its rows, the ones of its header in <thead>. A cell whose content
        // is a paragraph has no <p>.
        private void WriteGridTable(MarkdownGridTableSyntax table)
        {
            Cr();
            Markup.Append("<table");
            AppendAttributes(table);
            Markup.Append(">\n");
            var columns = table.Columns;
            if (columns.Any(column => column.Width is not (0.0f or 1.0f)))
            {
                foreach (var column in columns)
                {
                    var width = Math.Round(column.Width * 100, MidpointRounding.ToEven) / 100;
                    Markup.Append("<col style=\"width:").Append(width.ToString("0.##", CultureInfo.InvariantCulture)).Append("%\" />\n");
                }
            }

            var hasBody = false;
            var hasHeader = false;
            var isHeaderOpen = false;
            foreach (var row in table.Rows)
            {
                if (row.IsHeader)
                {
                    if (!hasHeader)
                    {
                        Markup.Append("<thead>\n");
                        isHeaderOpen = true;
                    }

                    hasHeader = true;
                }
                else if (!hasBody)
                {
                    if (isHeaderOpen)
                    {
                        Markup.Append("</thead>\n");
                        isHeaderOpen = false;
                    }

                    Markup.Append("<tbody>\n");
                    hasBody = true;
                }

                Markup.Append("<tr");
                if (_pragmaGridParts?.GetValueOrDefault(row) is { } rowId)
                {
                    Markup.Append(" id=\"").Append(rowId).Append('"');
                }

                Markup.Append(">\n");
                var tag = row.IsHeader ? "th" : "td";
                foreach (var cell in row.Cells)
                {
                    Markup.Append('<').Append(tag);
                    if (cell.ColumnSpan != 1)
                    {
                        Markup.Append(" colspan=\"").Append(cell.ColumnSpan.ToString(CultureInfo.InvariantCulture)).Append('"');
                    }

                    if (cell.RowSpan != 1)
                    {
                        Markup.Append(" rowspan=\"").Append(cell.RowSpan.ToString(CultureInfo.InvariantCulture)).Append('"');
                    }

                    if (_pragmaGridParts?.GetValueOrDefault(cell) is { } cellId)
                    {
                        Markup.Append(" id=\"").Append(cellId).Append('"');
                    }

                    Markup.Append(columns[Math.Min(cell.ColumnIndex, columns.Count - 1)].Alignment switch
                    {
                        MarkdownTableAlignment.Center => " align=\"center\"",
                        MarkdownTableAlignment.Left => " align=\"left\"",
                        MarkdownTableAlignment.Right => " align=\"right\"",
                        _ => "",
                    });
                    Markup.Append('>');
                    _output.Append(RenderCell(cell.Content));
                    AppendPlainTextCellSeparator();
                    Markup.Append("</").Append(tag).Append(">\n");
                }

                Markup.Append("</tr>\n");
            }

            if (hasBody)
            {
                Markup.Append("</tbody>\n");
            }
            else if (isHeaderOpen)
            {
                Markup.Append("</thead>\n");
            }

            Markup.Append("</table>\n");
        }

        private string RenderCell(MarkdownSyntaxTree content)
        {
            using var writer = new Writer(content.GetText().Length, content.Options, rendererOptions) { PlainText = PlainText };
            writer.CollectAllAttributes(content);
            if (content.GetRoot().Blocks is [MarkdownParagraphSyntax paragraph] && !(writer._attributes is not null && writer.IsAttributesParagraph(paragraph)))
            {
                writer.WriteUnwrappedParagraph(paragraph);
            }
            else
            {
                // As in Markdig, the blocks start on a new line after <td>, except a paragraph, a heading, a thematic break and HTML
                if (content.GetRoot().Blocks is [not (MarkdownParagraphSyntax or MarkdownHeadingSyntax or MarkdownThematicBreakSyntax or MarkdownHtmlBlockSyntax), ..])
                {
                    writer._output.Append('\n');
                }

                writer.WriteBlocks(content.GetRoot().Blocks);
            }

            return writer.ToString();
        }

        private void WritePipeTableRow(MarkdownTableRowSyntax row, string tag, int columnCount, IReadOnlyList<MarkdownTableAlignment> alignments)
        {
            Markup.Append("<tr");
            AppendAttributes(row);
            Markup.Append(">\n");
            var cells = row.Cells;
            for (var i = 0; i < columnCount; i++)
            {
                Markup.Append('<').Append(tag);
                if (alignments.Count > 0)
                {
                    Markup.Append(alignments[Math.Min(i, alignments.Count - 1)] switch
                    {
                        MarkdownTableAlignment.Center => " align=\"center\"",
                        MarkdownTableAlignment.Left => " align=\"left\"",
                        MarkdownTableAlignment.Right => " align=\"right\"",
                        _ => "",
                    });
                }

                Markup.Append('>');
                if (i < cells.Count)
                {
                    // As in Markdig, a cell starts after its last hard line break: the inlines before it are not written
                    var inlines = cells[i].Inlines;
                    var first = 0;
                    for (var j = 0; j < inlines.Count; j++)
                    {
                        if (inlines[j].RawKind == (int)SyntaxKind.HardLineBreakToken)
                        {
                            first = j + 1;
                        }
                    }

                    _trimmedCellTokens = GetTrimmedCellTokens(inlines);
                    WriteInlines(inlines, first);
                    _trimmedCellTokens = default;
                }

                AppendPlainTextCellSeparator();
                Markup.Append("</").Append(tag).Append(">\n");
            }

            Markup.Append("</tr>\n");
        }

        private void WriteTableRow(MarkdownTableRowSyntax row, string tag, List<string> alignments)
        {
            Markup.Append("<tr");
            AppendAttributes(row);
            Markup.Append(">\n");
            var cells = row.Cells;
            for (var i = 0; i < alignments.Count; i++)
            {
                Markup.Append('<').Append(tag).Append(alignments[i]).Append('>');
                if (i < cells.Count)
                {
                    _inTableCell = true;
                    WriteInlines(cells[i].Inlines);
                    _inTableCell = false;
                }

                AppendPlainTextCellSeparator();
                Markup.Append("</").Append(tag).Append(">\n");
            }

            Markup.Append("</tr>\n");
        }

        /// <summary>
        /// Writes the inlines from <paramref name="start"/> to <paramref name="end"/>, or to the last one, without the tokens of the
        /// kinds to skip: the delimiters that the Inlines property of a node leaves out. The children are enumerated as a
        /// <see cref="ChildSyntaxList"/>, which allocates nothing.
        /// </summary>
        private void WriteInlines(ChildSyntaxList inlines, int start = 0, int end = int.MaxValue, bool keepTrailingLineBreak = false, SyntaxKind skippedKind = SyntaxKind.None, SyntaxKind otherSkippedKind = SyntaxKind.None, SyntaxKind lastSkippedKind = SyntaxKind.None)
        {
            // As in Markdig, a line break that only attributes follow ends the text, and is not written, as a hard line break that
            // ends the text, such as the one before a pipe table. In a heading, Markdig reads the line endings as text, and the back
            // links of a footnote come after the line break.
            SyntaxToken pendingLineBreak = default;
            var index = -1;
            foreach (var child in inlines)
            {
                index++;
                if (index < start)
                {
                    continue;
                }

                if (index >= end)
                {
                    break;
                }

                if (child.AsNode() is MarkdownGenericAttributesSyntax || (child.IsToken && skippedKind != SyntaxKind.None && (SyntaxKind)child.RawKind is var kind && (kind == skippedKind || kind == otherSkippedKind || kind == lastSkippedKind)))
                {
                    continue;
                }

                if (pendingLineBreak.RawKind != 0)
                {
                    WriteToken(pendingLineBreak);
                    pendingLineBreak = default;
                }

                if (child.AsNode() is MarkdownInlineSyntax inline)
                {
                    WriteInline(inline);
                }
                else if (!keepTrailingLineBreak && child.AsToken().RawKind is (int)SyntaxKind.HardLineBreakToken || (_genericAttributes && !keepTrailingLineBreak && child.AsToken().RawKind == (int)SyntaxKind.LineEndingToken))
                {
                    pendingLineBreak = child.AsToken();
                }
                else
                {
                    WriteToken(child.AsToken());
                }
            }
        }

        private string GetText(SyntaxToken token)
        {
            var text = _inTableCell ? token.Text.Replace("\\|", "|", StringComparison.Ordinal) : token.Text;
            if (_trimmedCellTokens.Start == token)
            {
                text = text.TrimStart(PipeTableWhitespace);
            }

            if (_trimmedCellTokens.End == token)
            {
                text = text.TrimEnd(PipeTableWhitespace);
            }

            return text;
        }

        // As in Markdig, the text of a link at the start or at the end of a cell of a pipe table is trimmed, and the text after
        // the generic attributes that start the cell
        private static (SyntaxToken Start, SyntaxToken End) GetTrimmedCellTokens(ChildSyntaxList inlines)
        {
            SyntaxToken start = default;
            SyntaxToken end = default;
            var firstIndex = 0;
            while (firstIndex < inlines.Count && inlines[firstIndex].AsNode() is MarkdownGenericAttributesSyntax)
            {
                firstIndex++;
            }

            // The text after the generic attributes that start the cell is trimmed: Markdig does not add them to the inlines
            if (firstIndex > 0 && firstIndex < inlines.Count && inlines[firstIndex].AsToken() is { RawKind: (int)SyntaxKind.TextToken } textAfterAttributes)
            {
                start = textAfterAttributes;
            }
            else if (firstIndex < inlines.Count && inlines[firstIndex].AsNode() is MarkdownLinkSyntax { RawKind: (int)SyntaxKind.Link } first && first.ChildNodesAndTokens() is { Count: > 1 } firstChildren && firstChildren[1].AsToken() is { RawKind: (int)SyntaxKind.TextToken } firstText)
            {
                start = firstText;
            }

            if (inlines.LastOrDefault().AsNode() is MarkdownLinkSyntax { RawKind: (int)SyntaxKind.Link } last)
            {
                var children = last.ChildNodesAndTokens();
                for (var i = 1; i < children.Count; i++)
                {
                    if (children[i].RawKind == (int)SyntaxKind.CloseBracketToken)
                    {
                        if (children[i - 1].AsToken() is { RawKind: (int)SyntaxKind.TextToken } lastText)
                        {
                            end = lastText;
                        }

                        break;
                    }
                }
            }

            return (start, end);
        }

        private void WriteToken(SyntaxToken token)
        {
            switch ((SyntaxKind)token.RawKind)
            {
                case SyntaxKind.TextToken or SyntaxKind.OpenBracketToken or SyntaxKind.ExclamationOpenBracketToken or SyntaxKind.CloseBracketToken or SyntaxKind.ExtendedAutolinkToken or SyntaxKind.ExtensionTextToken:
                    AppendEscaped(GetText(token));
                    break;
                case SyntaxKind.EscapedCharacterToken:
                    AppendEscaped(GetText(token).AsSpan(1));
                    break;
                case SyntaxKind.EntityToken:
                    AppendEscaped(HtmlEntities.Decode(token.Text));
                    break;
                case SyntaxKind.EmojiToken:
                    AppendEscaped(options.EmojiMapping.TryGetEmoji(token.Text, out var emoji) ? emoji : token.Text);
                    break;
                case SyntaxKind.SmartyPantToken:
                    var pant = (MarkdownSmartyPant)(int)token.Value!;
                    var pantText = rendererOptions.SmartyPants.GetValueOrDefault(pant) ?? MarkdownHtmlRendererOptions.DefaultSmartyPants[pant];
                    _output.Append(PlainText ? HtmlEntities.Decode(pantText) : pantText);
                    break;
                case SyntaxKind.LineEndingToken when !_hardLineBreaks:
                    // As in Markdig, a line ending after a line ending, which only generic attributes separate, is not written
                    // Plain text does not start with a line ending, as Markdig ensures a line only after text
                    if (_output.Length == 0 ? !PlainText : _output.LastChar != '\n')
                    {
                        _output.Append('\n');
                    }

                    break;
                case SyntaxKind.HardLineBreakToken or SyntaxKind.LineEndingToken:
                    // As Markdig, plain text has a line break: the next text starts on a new line
                    Markup.Append("<br />");
                    if (!PlainText || _output.Length == 0 || _output.LastChar != '\n')
                    {
                        _output.Append('\n');
                    }
                    break;
            }
        }

        private void WriteInline(MarkdownInlineSyntax inline)
        {
            if (ReferenceEquals(_defaultNode, inline))
            {
                _defaultNode = null;
            }
            else if (_hasNodeRenderers && TryWriteWithNodeRenderers(inline))
            {
                return;
            }

            switch (inline)
            {
                case MarkdownEmphasisSyntax emphasis:
                {
                    var tag = emphasis.IsStrong ? "strong" : "em";
                    Markup.Append('<').Append(tag);
                    AppendAttributes(emphasis);
                    Markup.Append('>');
                    WriteInlines(emphasis.ChildNodesAndTokens(), skippedKind: SyntaxKind.EmphasisDelimiterToken);
                    Markup.Append("</").Append(tag).Append('>');
                    break;
                }

                case MarkdownStrikethroughSyntax strikethrough:
                    Markup.Append("<del");
                    AppendAttributes(strikethrough);
                    Markup.Append('>');
                    WriteInlines(strikethrough.ChildNodesAndTokens(), skippedKind: SyntaxKind.StrikethroughDelimiterToken);
                    Markup.Append("</del>");
                    break;

                case MarkdownEmphasisExtraSyntax extra:
                {
                    var tag = extra.Style switch
                    {
                        MarkdownEmphasisExtraStyle.Subscript => "sub",
                        MarkdownEmphasisExtraStyle.Superscript => "sup",
                        MarkdownEmphasisExtraStyle.Inserted => "ins",
                        MarkdownEmphasisExtraStyle.CustomContainer => "span",
                        MarkdownEmphasisExtraStyle.Citation => "cite",
                        _ => "mark",
                    };

                    Markup.Append('<').Append(tag);
                    AppendAttributes(extra);
                    Markup.Append('>');
                    // The inlines are between the two delimiters
                    var children = extra.ChildNodesAndTokens();
                    WriteInlines(children, start: 1, end: children.Count - 1);
                    Markup.Append("</").Append(tag).Append('>');
                    break;
                }

                case MarkdownCodeSpanSyntax code:
                    Markup.Append("<code");
                    AppendAttributes(code);
                    Markup.Append('>');
                    AppendEscaped(code.Code);
                    Markup.Append("</code>");
                    break;

                case MarkdownAutolinkSyntax autolink:
                    Markup.Append("<a href=\"");
                    AppendAttributeEscaped(NormalizeUrl(autolink.Destination));
                    Markup.Append('"');
                    if (autolink.IsMarkdigAutolink && options.AutolinkOptions.OpenInNewWindow)
                    {
                        // Markdig sets the target before the generic attributes
                        ParsedAttributes? autolinkAttributes = null;
                        _ = _attributes?.TryGetValue(autolink, out autolinkAttributes);
                        AppendAttributes(autolink, new ParsedAttributes(autolinkAttributes?.Id, autolinkAttributes?.Classes ?? [], [new("target", "_blank"), .. (autolinkAttributes?.Properties ?? []).Where(property => !string.Equals(property.Key, "target", StringComparison.Ordinal))]));
                    }
                    else
                    {
                        AppendAttributes(autolink);
                    }

                    if (autolink.IsExtended || !autolink.IsEmail)
                    {
                        AppendRel();
                    }

                    Markup.Append('>');
                    AppendEscaped(autolink.Text);
                    Markup.Append("</a>");
                    break;

                case MarkdownJiraLinkSyntax jira:
                {
                    Markup.Append("<a href=\"");
                    AppendAttributeEscaped(NormalizeUrl(jira.Destination));
                    Markup.Append('"');
                    ParsedAttributes? attributes = null;
                    _ = _attributes?.TryGetValue(jira, out attributes);
                    if (options.JiraLinks.OpenInNewWindow)
                    {
                        // Markdig sets the target before the generic attributes
                        attributes = new ParsedAttributes(attributes?.Id, attributes?.Classes ?? [], [new("target", "_blank"), .. (attributes?.Properties ?? []).Where(property => !string.Equals(property.Key, "target", StringComparison.Ordinal))]);
                    }

                    AppendAttributes(jira, attributes);
                    AppendRel();
                    Markup.Append('>');
                    AppendEscaped(jira.ReferenceToken.Text);
                    Markup.Append("</a>");
                    break;
                }

                case MarkdownExtensionInlineSyntax extension:
                    // The construct of a syntax extension that no node renderer writes: its text and its inlines, without its markers
                    WriteInlines(extension.ChildNodesAndTokens(), keepTrailingLineBreak: true, skippedKind: SyntaxKind.ExtensionMarkerToken);
                    break;

                case MarkdownRawHtmlSyntax html:
                    AppendHtml(html.Html);
                    break;

                case MarkdownAbbreviationSyntax abbreviation:
                    if (abbreviation.Definition is { } definition)
                    {
                        Markup.Append("<abbr title=\"");
                        AppendAttributeEscaped(definition.Title);
                        Markup.Append("\">");
                        AppendEscaped(abbreviation.Label);
                        Markup.Append("</abbr>");
                    }
                    else
                    {
                        AppendEscaped(abbreviation.Label);
                    }

                    break;

                case MarkdownFootnoteReferenceSyntax reference:
                {
                    _footnotes ??= new MarkdownFootnotes(reference.FirstAncestorOrSelf<MarkdownDocumentSyntax>()!);
                    if (reference.Definition is { } footnote && _footnotes.Footnotes.Contains(footnote))
                    {
                        var number = _footnotes.GetNumber(footnote);
                        Markup.Append("<a id=\"fnref:").Append(_footnotes.GetReferenceNumber(reference)).Append("\" href=\"#fn:").Append(number).Append("\" class=\"footnote-ref\"><sup>");
                        _output.Append(number);
                        Markup.Append("</sup></a>");
                    }

                    break;
                }

                case MarkdownMathInlineSyntax math:
                    Markup.Append("<span");
                    AppendAttributes(math, leadingClasses: "math");
                    Markup.Append(">\\(");
                    AppendEscaped(math.Math);
                    Markup.Append("\\)</span>");
                    break;

                case MarkdownLinkSyntax { IsImage: true } image when _mediaLinks && !PlainText && TryWriteMedia(image):
                    break;

                case MarkdownLinkSyntax { IsImage: true } image:
                    Markup.Append("<img src=\"");
                    AppendAttributeEscaped(NormalizeUrl(image.Destination));
                    Markup.Append('"');
                    AppendAttributes(image);
                    Markup.Append(" alt=\"");
                    WriteAltText(image.Text);
                    Markup.Append('"');
                    AppendTitle(image.Title);
                    Markup.Append(" />");
                    break;

                case MarkdownLinkSyntax link:
                    Markup.Append("<a href=\"");
                    AppendAttributeEscaped(NormalizeUrl(link.Destination));
                    Markup.Append('"');
                    AppendAttributes(link);
                    AppendTitle(link.Title);
                    AppendRel();
                    Markup.Append('>');
                    // The text is between the opening bracket and the closing one
                    WriteInlines(link.ChildNodesAndTokens(), start: 1, end: link.CloseIndex);
                    Markup.Append("</a>");
                    break;
            }
        }

        private void AppendRel()
        {
            if (!string.IsNullOrWhiteSpace(rendererOptions.LinkRel))
            {
                Markup.Append(" rel=\"").Append(rendererOptions.LinkRel).Append('"');
            }
        }

        // An image of a site such as YouTube is an <iframe>, and one of an audio or a video file is <audio> or <video>, with the
        // attributes of the image and the ones of the options, unless the image has them
        private bool TryWriteMedia(MarkdownLinkSyntax image)
        {
            var url = image.Destination;
            var isSchemaRelative = url.StartsWith("//", StringComparison.Ordinal);
            if (isSchemaRelative)
            {
                url = "https:" + url;
            }

            if (!Uri.TryCreate(url, UriKind.RelativeOrAbsolute, out var uri))
            {
                return false;
            }

            var mediaOptions = rendererOptions.MediaLinks;
            ParsedAttributes? imageAttributes = null;
            _ = _attributes?.TryGetValue(image, out imageAttributes);
            var attributes = new ParsedAttributes(imageAttributes?.Id, [.. imageAttributes?.Classes ?? []], [.. imageAttributes?.Properties ?? []]);
            if (uri.IsAbsoluteUri && IsHttpOrHttps(uri))
            {
                foreach (var host in mediaOptions.Hosts)
                {
                    if (!host.TryHandle(uri, isSchemaRelative, out var iframeUrl))
                    {
                        continue;
                    }

                    if (!Uri.TryCreate(iframeUrl, UriKind.Absolute, out var iframeUri) || !IsHttpOrHttps(iframeUri))
                    {
                        return false;
                    }

                    Markup.Append("<iframe src=\"");
                    AppendAttributeEscaped(NormalizeUrl(iframeUrl));
                    Markup.Append('"');
                    AddProperty(attributes, "width", mediaOptions.Width, ifNotEmpty: true);
                    AddProperty(attributes, "height", mediaOptions.Height, ifNotEmpty: true);
                    AddClass(attributes, mediaOptions.Class);
                    AddClass(attributes, host.Class);
                    AddProperty(attributes, "frameborder", "0");
                    if (host.AllowFullScreen)
                    {
                        AddProperty(attributes, "allowfullscreen", value: null);
                    }

                    AppendAttributes(image, attributes);
                    Markup.Append("></iframe>");
                    return true;
                }
            }

            var path = uri.IsAbsoluteUri ? uri.GetComponents(UriComponents.Path, UriFormat.Unescaped) : uri.ToString();
            var lastDot = path.AsSpan().LastIndexOf('.');
            if (lastDot < 0 || !TryGetMimeType(mediaOptions.ExtensionToMimeType, path.AsSpan(lastDot), out var mimeType))
            {
                return false;
            }

            var isAudio = mimeType.StartsWith("audio", StringComparison.Ordinal);
            var tag = isAudio ? "audio" : "video";
            Markup.Append('<').Append(tag);
            AddProperty(attributes, "width", mediaOptions.Width);
            if (!isAudio)
            {
                AddProperty(attributes, "height", mediaOptions.Height);
            }

            if (mediaOptions.AddControls)
            {
                AddProperty(attributes, "controls", value: null);
            }

            AddClass(attributes, mediaOptions.Class);
            AppendAttributes(image, attributes);
            Markup.Append("><source type=\"");
            AppendAttributeEscaped(mimeType);
            Markup.Append("\" src=\"");
            AppendAttributeEscaped(NormalizeUrl(image.Destination));
            Markup.Append("\"></source></").Append(tag).Append('>');
            return true;

            static bool IsHttpOrHttps(Uri uri) => uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps;

            static void AddClass(ParsedAttributes attributes, string? cssClass)
            {
                if (!string.IsNullOrEmpty(cssClass))
                {
                    attributes.Classes.Add(cssClass);
                }
            }

            static void AddProperty(ParsedAttributes attributes, string name, string? value, bool ifNotEmpty = false)
            {
                if ((!ifNotEmpty || !string.IsNullOrEmpty(value)) && !attributes.Properties.Exists(property => string.Equals(property.Key, name, StringComparison.Ordinal)))
                {
                    attributes.Properties.Add(new(name, value));
                }
            }
        }

        // The plain text of the description of an image, where Markdig writes the characters of SmartyPants as they are rendered,
        // and raw HTML as text, as cmark does
        private void WriteAltText(IEnumerable<SyntaxNodeOrToken> children)
        {
            foreach (var child in children)
            {
                if (child.IsToken && child.AsToken().RawKind == (int)SyntaxKind.SmartyPantToken)
                {
                    WriteToken(child.AsToken());
                    continue;
                }

                if (child.AsNode() is MarkdownRawHtmlSyntax html)
                {
                    // As Markdig, plain text has no HTML, even in the text of an image
                    if (!PlainText)
                    {
                        AppendEscaped(html.Html);
                    }

                    continue;
                }

                if (child.AsNode() is { } node && node.DescendantNodesAndTokens().Any(descendant => descendant.RawKind is (int)SyntaxKind.SmartyPantToken or (int)SyntaxKind.RawHtml))
                {
                    switch (node)
                    {
                        case MarkdownEmphasisSyntax emphasis:
                            WriteAltText(emphasis.Inlines);
                            continue;
                        case MarkdownStrikethroughSyntax strikethrough:
                            WriteAltText(strikethrough.Inlines);
                            continue;
                        case MarkdownEmphasisExtraSyntax extra:
                            WriteAltText(extra.Inlines);
                            continue;
                        case MarkdownExtensionInlineSyntax extension:
                            WriteAltText(extension.Inlines);
                            continue;
                        case MarkdownLinkSyntax link:
                            WriteAltText(link.Text);
                            continue;
                    }
                }

                var text = new StringBuilder();
                ((MarkdownSyntaxNode)(child.Parent ?? throw new InvalidOperationException())).AppendPlainText([child], text);
                AppendEscaped(text.ToString());
            }
        }

        // A frozen dictionary, such as the default one, looks the extension up without allocating it
        private static bool TryGetMimeType(IReadOnlyDictionary<string, string> extensionToMimeType, ReadOnlySpan<char> extension, [NotNullWhen(true)] out string? mimeType)
        {
            if (extensionToMimeType is FrozenDictionary<string, string> frozen && frozen.TryGetAlternateLookup<ReadOnlySpan<char>>(out var lookup))
            {
                return lookup.TryGetValue(extension, out mimeType);
            }

            return extensionToMimeType.TryGetValue(extension.ToString(), out mimeType);
        }

        private void AppendTitle(string? title)
        {
            if (!string.IsNullOrEmpty(title))
            {
                Markup.Append(" title=\"");
                AppendAttributeEscaped(title);
                Markup.Append('"');
            }
        }

        // Percent-encodes the characters that cannot be in a URL, as the reference implementation does
        private static bool IsSafeExceptNonAscii(string uri)
        {
            foreach (var c in uri)
            {
                if (c < 128 && !UriSafeCharacters.Contains(c))
                {
                    return false;
                }
            }

            return true;
        }

        // The URL of a link or of an image, made absolute with the base URL and rewritten as the options say, then percent-encoded
        private string NormalizeUrl(string uri)
        {
            // A URL that cannot be made absolute, such as http:B, is kept
            if (rendererOptions.BaseUrl is { } baseUrl && Uri.TryCreate(uri, UriKind.RelativeOrAbsolute, out var relative) && !relative.IsAbsoluteUri && Uri.TryCreate(baseUrl, relative, out var absolute))
            {
                uri = absolute.AbsoluteUri;
            }

            if (rendererOptions.LinkRewriter is { } rewriter)
            {
                uri = rewriter(uri);
            }

            return NormalizeUri(uri, rendererOptions.EscapeNonAsciiUrlCharacters);
        }

        private static string NormalizeUri(string uri, bool escapeNonAscii = true)
        {
            if (!uri.AsSpan().ContainsAnyExcept(UriSafeCharacters) || (!escapeNonAscii && IsSafeExceptNonAscii(uri)))
            {
                return uri;
            }

            var builder = new StringBuilder(uri.Length);
            Span<byte> bytes = stackalloc byte[4];
            for (var i = 0; i < uri.Length; i++)
            {
                var c = uri[i];
                if (c == '%' && i + 2 < uri.Length && char.IsAsciiHexDigit(uri[i + 1]) && char.IsAsciiHexDigit(uri[i + 2]))
                {
                    builder.Append(c);
                }
                else if (char.IsAsciiLetterOrDigit(c) || ";/?:@&=+$,-_.!~*'()#".Contains(c, StringComparison.Ordinal) || (!escapeNonAscii && c >= 128))
                {
                    builder.Append(c);
                }
                else
                {
                    // A lone surrogate is encoded as the replacement character
                    if (Rune.DecodeFromUtf16(uri.AsSpan(i), out var rune, out var consumed) != System.Buffers.OperationStatus.Done)
                    {
                        rune = Rune.ReplacementChar;
                        consumed = 1;
                    }

                    var length = rune.EncodeToUtf8(bytes);
                    for (var j = 0; j < length; j++)
                    {
                        builder.Append('%').Append(bytes[j].ToString("X2", CultureInfo.InvariantCulture));
                    }

                    i += consumed - 1;
                }
            }

            return builder.ToString();
        }

        // The lines of an HTML block, as MarkdownHtmlBlockSyntax.Html joins them, without building the string
        // Each line is written when the next one shows that a line ending follows it, which ends a tag for the tag filter
        private void AppendHtmlBlock(MarkdownHtmlBlockSyntax html)
        {
            string? previous = null;
            foreach (var child in html.ChildNodesAndTokens())
            {
                if (child.AsToken(out var token) && token.RawKind == (int)SyntaxKind.HtmlLineToken)
                {
                    if (previous is not null)
                    {
                        AppendHtml(MarkdownText.TrimLineEnding(previous), followedByLineEnding: true);
                        Markup.Append('\n');
                    }

                    previous = token.Text;
                }
            }

            if (previous is not null)
            {
                AppendHtml(MarkdownText.TrimLineEnding(previous));
            }
        }

        // Raw HTML, where the tag filter of GFM escapes the tags that change how the rest of the page is read
        private void AppendHtml(ReadOnlySpan<char> html, bool followedByLineEnding = false)
        {
            while (!html.IsEmpty)
            {
                var index = html.IndexOfAny(_tagFilter ? "<\0" : "\0");
                if (index < 0)
                {
                    Markup.Append(html);
                    return;
                }

                Markup.Append(html[..index]);
                if (html[index] == '\0')
                {
                    Markup.Append('\uFFFD');
                }
                else
                {
                    Markup.Append(IsDisallowedTag(html[index..], followedByLineEnding) ? "&lt;" : "<");
                }

                html = html[(index + 1)..];
            }
        }

        private static bool IsDisallowedTag(ReadOnlySpan<char> tag, bool followedByLineEnding)
        {
            var name = tag[1..];
            if (name.StartsWith('/'))
            {
                name = name[1..];
            }

            foreach (var disallowed in (ReadOnlySpan<string>)["title", "textarea", "style", "xmp", "iframe", "noembed", "noframes", "script", "plaintext"])
            {
                // A name that ends the text is followed by the line ending after it, when there is one
                if (followedByLineEnding && name.Length == disallowed.Length && name.Equals(disallowed, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                if (name.Length > disallowed.Length && name.StartsWith(disallowed, StringComparison.OrdinalIgnoreCase))
                {
                    var next = name[disallowed.Length..];
                    if (next[0] is ' ' or '\t' or '\n' or '\v' or '\f' or '\r' or '>' || next.StartsWith("/>", StringComparison.Ordinal))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        // The code of a code block, as MarkdownCodeBlockSyntax.Code reads it, without building the string
        // The lines are read from the green tokens: the renderer needs their text, not their position
        private void AppendCode(MarkdownCodeBlockSyntax code, bool soft)
        {
            if (code.Green.GetSlot(0) is not { } children)
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
                if (token.GetValue() is int virtualSpaces)
                {
                    _output.Append(' ', virtualSpaces);
                }

                var line = MarkdownText.TrimLineEnding(token.Text);
                if (soft)
                {
                    AppendSoftEscaped(line);
                }
                else
                {
                    AppendEscaped(line);
                }

                _output.Append('\n');
            }
        }

        private void AppendSoftEscaped(ReadOnlySpan<char> text)
        {
            // The runs without a character to escape are written at once
            var escaped = PlainText ? PlainTextSoftEscapedCharacters : SoftEscapedCharacters;
            while (!text.IsEmpty)
            {
                var index = text.IndexOfAny(escaped);
                if (index < 0)
                {
                    _output.Append(text);
                    return;
                }

                _output.Append(text[..index]);
                _ = text[index] switch
                {
                    '<' => _output.Append("&lt;"),
                    '&' => _output.Append("&amp;"),
                    _ => _output.Append('\uFFFD'),
                };
                text = text[(index + 1)..];
            }
        }

        // Plain text writes a space after each cell of a table, so that the text of a cell is not merged with the next one, as
        // Markdig writes it
        private void AppendPlainTextCellSeparator()
        {
            if (PlainText)
            {
                _output.Append(' ');
            }
        }

        // Text, which plain text writes as it is
        public void AppendEscaped(ReadOnlySpan<char> text)
        {
            while (!text.IsEmpty)
            {
                var index = text.IndexOfAny(EscapedCharacters);
                if (index < 0)
                {
                    _output.Append(text);
                    return;
                }

                _output.Append(text[..index]);
                _output.Append(text[index] switch
                {
                    _ when PlainText && text[index] != '\0' => text[index].ToString(),
                    '&' => "&amp;",
                    '<' => "&lt;",
                    '>' => "&gt;",
                    '"' => "&quot;",
                    _ => "\uFFFD",
                });
                text = text[(index + 1)..];
            }
        }

        // The value of an attribute, which plain text does not write
        private void AppendAttributeEscaped(ReadOnlySpan<char> text)
        {
            while (!text.IsEmpty)
            {
                var index = text.IndexOfAny(EscapedCharacters);
                if (index < 0)
                {
                    Markup.Append(text);
                    return;
                }

                Markup.Append(text[..index]);
                Markup.Append(text[index] switch
                {
                    '&' => "&amp;",
                    '<' => "&lt;",
                    '>' => "&gt;",
                    '"' => "&quot;",
                    _ => "\uFFFD",
                });
                text = text[(index + 1)..];
            }
        }
    }
}
