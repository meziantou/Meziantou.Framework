using System.Globalization;
using System.Text;
using Meziantou.Framework.WebPlatform.Infra;
using Meziantou.Framework.WebPlatform.Urls;

namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>A text directive (https://wicg.github.io/scroll-to-text-fragment/#text-directive): <c>text=[prefix-,]start[,end][,-suffix]</c>.</summary>
internal sealed record TextDirective(string? Prefix, string Start, string? End, string? Suffix);

/// <summary>
/// URL Fragment Text Directives (https://wicg.github.io/scroll-to-text-fragment/): the fragment directive of a URL (the part
/// of its fragment after <c>:~:</c>) is removed from the URL of the document when it is navigated, and its text directives
/// are searched in the text of the document. The ranges found are the target text of the document, which the
/// <c>::target-text</c> pseudo-element styles (https://drafts.csswg.org/css-pseudo-4/#selectordef-target-text); the first one
/// is the indicated part of the document.
/// </summary>
/// <remarks>
/// The search is a simplification of "find a range from a text directive": the text nodes that are rendered (not in a
/// search-invisible subtree such as <c>display: none</c>, a form control or an embedded content element, visible, in a
/// <c>content-visibility: hidden</c> subtree only when it is <c>hidden=until-found</c>) are collected in the
/// shadow-including tree order, with their white space collapsed. A text is searched case-insensitively (the invariant
/// culture, not the collation of the locale of the document) in the text of a block: the text between two block
/// boundaries, which are the start and the end of every element that is not an inline box, whether it has visible text or
/// not. The word boundaries are the ones of UAX #29 (see <see cref="Unicode.WordBoundaries"/>: there is no dictionary, so
/// each ideograph is a word). The prefix, the end and the suffix are searched in the text that follows, also in the next
/// blocks.
/// </remarks>
internal static class TextDirectives
{
    /// <summary>https://wicg.github.io/scroll-to-text-fragment/#fragment-directive-delimiter</summary>
    private const string Delimiter = ":~:";

    /// <summary>
    /// https://wicg.github.io/scroll-to-text-fragment/#remove-the-fragment-directive: removes the fragment directive from the
    /// fragment of a URL and returns it, or returns null when the fragment has none.
    /// </summary>
    public static string? RemoveFragmentDirective(UrlRecord url)
    {
        if (url.Fragment is not { } fragment)
            return null;

        var position = fragment.IndexOf(Delimiter, StringComparison.Ordinal);
        if (position < 0)
            return null;

        // An empty fragment directive is null (the fragment still ends at the delimiter)
        url.Fragment = fragment[..position];
        return position + Delimiter.Length < fragment.Length ? fragment[(position + Delimiter.Length)..] : null;
    }

    /// <summary>Whether the fragment of a URL has a fragment directive delimiter.</summary>
    public static bool HasFragmentDirective(UrlRecord url) => url.Fragment is { } fragment && fragment.Contains(Delimiter, StringComparison.Ordinal);

    /// <summary>https://wicg.github.io/scroll-to-text-fragment/#parse-the-fragment-directive: the valid text directives of a fragment directive (none for null).</summary>
    public static List<TextDirective> Parse(string? fragmentDirective)
    {
        var result = new List<TextDirective>();
        if (fragmentDirective is null)
            return result;

        foreach (var directive in fragmentDirective.Split('&'))
        {
            if (directive.StartsWith("text=", StringComparison.Ordinal) && ParseTextDirective(directive["text=".Length..]) is { } textDirective)
            {
                result.Add(textDirective);
            }
        }

        return result;
    }

    /// <summary>https://wicg.github.io/scroll-to-text-fragment/#parse-a-text-directive</summary>
    private static TextDirective? ParseTextDirective(string value)
    {
        var tokens = new List<string>(value.Split(','));
        if (tokens.Count > 4)
            return null;

        string? prefix = null;
        string? suffix = null;
        if (tokens[0].EndsWith('-', StringComparison.Ordinal))
        {
            prefix = tokens[0][..^1];
            tokens.RemoveAt(0);
            if (!IsValidTerm(prefix) || tokens.Count == 0)
                return null;
        }

        if (tokens[^1].StartsWith('-', StringComparison.Ordinal))
        {
            suffix = tokens[^1][1..];
            tokens.RemoveAt(tokens.Count - 1);
            if (!IsValidTerm(suffix) || tokens.Count == 0)
                return null;
        }

        if (tokens.Count > 2 || !IsValidTerm(tokens[0]) || (tokens.Count == 2 && !IsValidTerm(tokens[1])))
            return null;

        return new TextDirective(Decode(prefix), Decode(tokens[0])!, tokens.Count == 2 ? Decode(tokens[1]) : null, Decode(suffix));

        // "If start is the empty string or contains any instances of U+002D (-), return null": as in the browsers, a term can
        // contain a "-" that is not percent-encoded (e.g. text=well-known)
        static bool IsValidTerm(string term) => term.Length > 0;

        // https://wicg.github.io/scroll-to-text-fragment/#percent-decode-a-text-directive-term: UTF-8 decode without BOM
        static string? Decode(string? term) => term is null ? null : new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetString(PercentEncoding.PercentDecode(term));
    }

    /// <summary>Returns the ranges of the text directives that are found in the document, in the order of the directives.</summary>
    public static List<Range> FindRanges(Document document, List<TextDirective> directives)
    {
        var result = new List<Range>();
        if (directives.Count == 0 || document.DocumentElement is null)
            return result;

        var text = SearchableText.Create(document);
        foreach (var directive in directives)
        {
            if (FindRange(text, directive) is var (start, end))
            {
                var (startNode, startOffset) = text.GetPosition(start, isEnd: false);
                var (endNode, endOffset) = text.GetPosition(end, isEnd: true);

                // A live range cannot start in a tree and end in another one (e.g. a match that ends in a shadow tree)
                if (!ReferenceEquals(startNode.GetRoot(), endNode.GetRoot()))
                    continue;

                var range = document.CreateRange();
                range.SetStart(startNode, (uint)startOffset);
                range.SetEnd(endNode, (uint)endOffset);
                result.Add(range);
            }
        }

        return result;
    }

    /// <summary>https://wicg.github.io/scroll-to-text-fragment/#find-a-range-from-a-text-directive: the positions of the match in the searchable text, or null.</summary>
    private static (int Start, int End)? FindRange(SearchableText text, TextDirective directive)
    {
        var searchStart = 0;
        while (searchStart < text.Length)
        {
            int matchStart;
            int matchEnd;

            // "mustEndAtWordBoundary: true if parsedValues's end is non-null or parsedValues's suffix is null"
            var startEndsAtWordBoundary = directive.End is not null || directive.Suffix is null;
            if (directive.Prefix is { } prefix)
            {
                if (text.Find(prefix, searchStart, wordStartBounded: true, wordEndBounded: false) is not var (prefixStart, prefixEnd))
                    return null;

                // The next candidate is searched after the start of this prefix
                searchStart = prefixStart + 1;
                var position = text.SkipWhiteSpace(prefixEnd);
                if (text.MatchAt(directive.Start, position, wordEndBounded: startEndsAtWordBoundary) is not { } end)
                    continue;

                (matchStart, matchEnd) = (position, end);
            }
            else
            {
                if (text.Find(directive.Start, searchStart, wordStartBounded: true, wordEndBounded: startEndsAtWordBoundary) is not var (start, end))
                    return null;

                (matchStart, matchEnd) = (start, end);
                searchStart = start + 1;
            }

            if (directive.End is { } endText)
            {
                // "mustEndAtWordBoundary: true if parsedValues's suffix is null"
                var rangeSearchStart = matchEnd;
                var found = false;
                while (text.Find(endText, rangeSearchStart, wordStartBounded: true, wordEndBounded: directive.Suffix is null) is var (endStart, endEnd))
                {
                    if (directive.Suffix is null || text.MatchAt(directive.Suffix, text.SkipWhiteSpace(endEnd), wordEndBounded: true) is not null)
                    {
                        matchEnd = endEnd;
                        found = true;
                        break;
                    }

                    rangeSearchStart = endStart + 1;
                }

                if (!found)
                {
                    // No end text after this start: the following starts have none either
                    if (directive.Suffix is null)
                        return null;

                    continue;
                }
            }
            else if (directive.Suffix is { } suffix && text.MatchAt(suffix, text.SkipWhiteSpace(matchEnd), wordEndBounded: true) is null)
            {
                continue;
            }

            return (matchStart, matchEnd);
        }

        return null;
    }

    /// <summary>
    /// The text of the document that the text directives are searched in: the data of the searchable text nodes, with the
    /// white space collapsed to a single space, and the block of each character (a match of a text does not span blocks).
    /// </summary>
    private sealed class SearchableText
    {
        private readonly StringBuilder _text = new();
        private readonly List<(Text Node, int Offset)> _positions = [];
        private readonly List<int> _blocks = [];
        private string? _string;

        public int Length => _text.Length;

        private string Value => _string ??= _text.ToString();

        // The block of the next character (incremented at each block boundary), and whether a boundary was crossed since
        // the last character
        private int _block;
        private bool _blockBoundary;
        private bool[]? _wordBoundaries;

        public static SearchableText Create(Document document)
        {
            var result = new SearchableText();
            var engine = document.GetStyleEngine();
            if (document.DocumentElement is { } root)
            {
                Visit(root);
            }

            return result;

            // https://wicg.github.io/scroll-to-text-fragment/#find-a-string-in-range: the nodes in shadow-including tree order
            void Visit(Element element)
            {
                // https://wicg.github.io/scroll-to-text-fragment/#search-invisible, #non-searchable-subtree. The contents of a select
                // element without multiple attribute are in its picker; an open select popover renders them, as a list box
                // does: they are searched (https://html.spec.whatwg.org/multipage/rendering.html#the-select-element-2)
                if (element.IsHtmlNamespace && (element.LocalName is "iframe" or "img" or "video" or "audio" or "canvas" or "object" or "embed" or "textarea" or "input" or "script" or "style" or "noscript" or "head" or "title" or "meter" or "progress"
                    || (element.LocalName == "select" && !element.HasAttributeWithLocalName("multiple") && !IsSelectPopoverShowing(element))))
                    return;

                var style = engine.GetStyle(element);
                var display = style.GetComputedValue("display");
                if (display == "none")
                    return;

                // The skipped contents are not rendered; the ones of a hidden=until-found element are revealed when they match
                if (style.GetComputedValue("content-visibility") == "hidden" && !InfraAscii.EqualsIgnoreAsciiCase(element.GetAttributeValue("hidden") ?? "", "until-found"))
                    return;

                // https://wicg.github.io/scroll-to-text-fragment/#has-block-level-display: an element that is not an inline box
                // ends the text of the block before it, and the text after it is another block
                var isBlock = display is not ("inline" or "contents");
                result._blockBoundary |= isBlock;
                var isVisible = style.GetComputedValue("visibility") == "visible";
                var shadowRoot = element.AttachedShadowRoot;
                if (shadowRoot is not null)
                {
                    VisitChildren(shadowRoot, isVisible, slottedOnly: false);
                }

                // The children of a shadow host are rendered when they are assigned to a slot, and the children of a slot (its
                // fallback content) when no node is assigned to it
                if (element is not HtmlSlotElement { AssignedNodesList.Count: > 0 })
                {
                    VisitChildren(element, isVisible, slottedOnly: shadowRoot is not null);
                }

                result._blockBoundary |= isBlock;
            }

            bool IsSelectPopoverShowing(Element element) => element is HtmlSelectElement { IsPickerOpen: true } select && engine.IsSelectPickerRendered(engine.GetStyle(select));

            void VisitChildren(Node parent, bool isVisible, bool slottedOnly)
            {
                for (var child = parent.FirstChild; child is not null; child = child.NextSibling)
                {
                    if (slottedOnly && child.Slottable?.AssignedSlot is null)
                        continue;

                    if (child is Element childElement)
                    {
                        Visit(childElement);
                    }
                    else if (child is Text text && isVisible)
                    {
                        // https://wicg.github.io/scroll-to-text-fragment/#visible-text-node
                        result.AppendText(text);
                    }
                }
            }
        }

        private void AppendText(Text text)
        {
            var data = text.Data;
            for (var i = 0; i < data.Length; i++)
            {
                // The white space (also the no-break spaces) is a single space, also across the nodes and the blocks
                if (char.IsWhiteSpace(data[i]))
                {
                    if (_text.Length > 0 && _text[^1] != ' ')
                    {
                        Append(' ', text, i, _blockBoundary ? -1 : _block);
                    }

                    continue;
                }

                if (_blockBoundary)
                {
                    // The text of another block does not continue the last word of this one
                    if (_text.Length > 0 && _text[^1] != ' ')
                    {
                        Append(' ', text, i, -1);
                    }

                    _block++;
                    _blockBoundary = false;
                }

                Append(data[i], text, i, _block);
            }
        }

        private void Append(char c, Text node, int offset, int block)
        {
            _text.Append(c);
            _positions.Add((node, offset));
            _blocks.Add(block);
            _string = null;
        }

        /// <summary>Returns the boundary point of a position of the text: before its character, or after the previous character for the end of a match.</summary>
        public (Text Node, int Offset) GetPosition(int position, bool isEnd)
        {
            if (isEnd)
            {
                var (node, offset) = _positions[position - 1];
                return (node, offset + 1);
            }

            return _positions[position];
        }

        public int SkipWhiteSpace(int position)
        {
            while (position < Length && _text[position] == ' ')
            {
                position++;
            }

            return position;
        }

        /// <summary>
        /// https://wicg.github.io/scroll-to-text-fragment/#find-a-string-in-range: the first match of a text at or after a
        /// position, case-insensitively, that starts and ends at a word boundary when required.
        /// </summary>
        public (int Start, int End)? Find(string query, int start, bool wordStartBounded, bool wordEndBounded)
        {
            query = Normalize(query);
            if (query.Length == 0)
                return null;

            var value = Value;
            for (var index = start; index < value.Length;)
            {
                index = CultureInfo.InvariantCulture.CompareInfo.IndexOf(value, query, index, CompareOptions.OrdinalIgnoreCase);
                if (index < 0)
                    return null;

                var end = index + query.Length;
                if ((!wordStartBounded || IsWordBoundary(index)) && (!wordEndBounded || IsWordBoundary(end)) && IsInOneBlock(index, end))
                    return (index, end);

                index++;
            }

            return null;
        }

        /// <summary>Returns the end of the match of a text at a position, or null.</summary>
        public int? MatchAt(string query, int position, bool wordEndBounded)
        {
            query = Normalize(query);
            var end = position + query.Length;
            if (query.Length == 0 || end > Length || string.Compare(Value, position, query, 0, query.Length, StringComparison.OrdinalIgnoreCase) != 0)
                return null;

            return (!wordEndBounded || IsWordBoundary(end)) && IsInOneBlock(position, end) ? end : null;
        }

        private static string Normalize(string query)
        {
            var builder = new StringBuilder(query.Length);
            foreach (var c in query)
            {
                if (!char.IsWhiteSpace(c))
                {
                    builder.Append(c);
                }
                else if (builder.Length == 0 || builder[^1] != ' ')
                {
                    builder.Append(' ');
                }
            }

            return builder.ToString();
        }

        private bool IsInOneBlock(int start, int end)
        {
            var block = -1;
            for (var i = start; i < end; i++)
            {
                // The space added between two blocks belongs to no block
                if (_blocks[i] < 0)
                    return false;

                if (block >= 0 && _blocks[i] != block)
                    return false;

                block = _blocks[i];
            }

            return true;
        }

        /// <summary>
        /// https://wicg.github.io/scroll-to-text-fragment/#word-bounded: a word boundary of UAX #29 in the text of the block, or
        /// an edge of a block.
        /// </summary>
        private bool IsWordBoundary(int position)
        {
            if (position <= 0 || position >= Length || _blocks[position - 1] != _blocks[position] || _blocks[position] < 0)
                return true;

            return (_wordBoundaries ??= ComputeWordBoundaries())[position];
        }

        private bool[] ComputeWordBoundaries()
        {
            // The word boundaries of each block (the text between two block boundaries)
            var value = Value;
            var boundaries = new bool[value.Length];
            for (var start = 0; start < value.Length;)
            {
                var end = start + 1;
                while (end < value.Length && _blocks[end] == _blocks[start])
                {
                    end++;
                }

                Unicode.WordBoundaries.GetBoundaries(value.AsSpan(start, end - start), boundaries.AsSpan(start, end - start));
                start = end;
            }

            return boundaries;
        }
    }
}
