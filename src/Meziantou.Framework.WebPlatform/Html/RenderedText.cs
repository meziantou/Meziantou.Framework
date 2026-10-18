using System.Globalization;
using System.Text;
using Meziantou.Framework.WebPlatform.Css.Cascade;
using Meziantou.Framework.WebPlatform.Css.Layout;
using Meziantou.Framework.WebPlatform.Dom;

namespace Meziantou.Framework.WebPlatform.Html;

/// <summary>
/// The rendered text collection steps of the innerText getter (https://html.spec.whatwg.org/multipage/dom.html#rendered-text-collection-steps),
/// computed from the cascade without layout: the boxes are given by the computed value of display (block-level boxes,
/// table cells and rows, atomic inlines), and the text by the white space processing of CSS Text
/// (https://drafts.csswg.org/css-text/#white-space-processing) and text-transform. There is no line wrapping: the lines
/// end at the block boundaries and at the forced line breaks.
/// </summary>
internal sealed class RenderedText
{
    private readonly StyleEngine _engine;
    private readonly List<object> _results = [];
    private readonly Stack<LineState> _lines = new();

    /// <summary>The range whose text is collected (the nodes outside of it are ignored), or null to collect all the text.</summary>
    private readonly Dom.Range? _range;

    private RenderedText(StyleEngine engine, Dom.Range? range = null)
    {
        _engine = engine;
        _range = range;
    }

    /// <summary>
    /// Returns the rendered text of the content of a range (e.g. the selection copied to the clipboard): the innerText of the
    /// element that contains the range, restricted to the nodes and the characters of the range. Returns null when the
    /// element is not being rendered.
    /// </summary>
    public static string? GetRenderedText(Dom.Range range)
    {
        var container = range.CommonAncestorContainer;
        var element = container as Element ?? container.ParentElement ?? (container as Document)?.DocumentElement;
        if (element is null || !HasRenderedText(element))
            return null;

        if (IsReplaced(element, element.NodeDocument.GetStyleEngine().GetStyle(element)))
            return "";

        var collector = new RenderedText(element.NodeDocument.GetStyleEngine(), range);
        var style = collector._engine.GetStyle(element);
        collector._lines.Push(collector.CreateLineState(element, style));
        collector.CollectChildren(element, style, GetChildrenMode(element, ChildrenMode.Normal));
        return collector.GetResult();
    }

    /// <summary>Returns the innerText of the element, or null when the element is not being rendered (its descendant text content is used).</summary>
    public static string? GetInnerText(Element element)
    {
        if (!HasRenderedText(element))
            return null;

        // https://drafts.csswg.org/css-contain-2/#cv-notes: the skipped contents do not contribute to innerText (the element is
        // being rendered, as its boxes are generated), the ones of the element included
        if (ContentRelevancy.IsInSkippedContents(element) || ContentRelevancy.SkipsContents(element, element.NodeDocument.GetStyleEngine().GetStyle(element)))
            return "";

        // The contents of the replaced elements are not rendered
        if (IsReplaced(element, element.NodeDocument.GetStyleEngine().GetStyle(element)))
            return "";

        var collector = new RenderedText(element.NodeDocument.GetStyleEngine());
        var style = collector._engine.GetStyle(element);
        collector._lines.Push(collector.CreateLineState(element, style));
        collector.Line.InWord = collector.IsAfterWordCharacter(element, style);
        collector.CollectChildren(element, style, GetChildrenMode(element, ChildrenMode.Normal));
        return collector.GetResult();
    }

    /// <summary>
    /// Whether the text of an inline box continues a word: the text before the box, in the inline boxes that contain it, ends
    /// with a word character, so the first letter of the box does not start a word for <c>text-transform: capitalize</c>
    /// (https://drafts.csswg.org/css-text-4/#valdef-text-transform-capitalize).
    /// </summary>
    private bool IsAfterWordCharacter(Element element, ComputedStyle style)
    {
        for (Node? node = element; node is Element current && IsInline(current, node == element ? style : null); node = node.ParentNode)
        {
            for (var sibling = node.PreviousSibling; sibling is not null; sibling = sibling.PreviousSibling)
            {
                if (GetLastCharacterIsWord(sibling) is { } result)
                    return result;
            }
        }

        return false;

        bool IsInline(Element current, ComputedStyle? currentStyle) => (currentStyle ?? _engine.GetStyle(current)).GetComputedValue("display") == "inline";

        // Whether the text of a node ends with a word character; null when the node has no text
        bool? GetLastCharacterIsWord(Node node)
        {
            switch (node)
            {
                case Text text:
                    return text.Data.Length == 0 ? null : TextTransformer.EndsWithWordCharacter(text.Data);

                case Element child:
                    if (!IsInline(child, null) || IsReplaced(child, _engine.GetStyle(child)))
                        return false;

                    for (var last = child.LastChild; last is not null; last = last.PreviousSibling)
                    {
                        if (GetLastCharacterIsWord(last) is { } result)
                            return result;
                    }

                    return null;

                default:
                    return null;
            }
        }
    }

    /// <summary>Whether the element is being rendered, or has display: contents (it has no box, but its contents are rendered), see <see cref="BoxGeneration"/>.</summary>
    private static bool HasRenderedText(Element element) => BoxGeneration.Get(element).Kind is BoxGenerationKind.Box or BoxGenerationKind.Contents;

    private static ChildrenMode GetChildrenMode(Element element, ChildrenMode parentMode)
    {
        if (element.IsHtml(TagId.Select) || (parentMode == ChildrenMode.Select && !element.IsHtml(TagId.Option)))
            return ChildrenMode.Select;

        if (element.IsHtml(TagId.Details) && !element.HasAttributeWithLocalName("open"))
            return ChildrenMode.ClosedDetails;

        if (ReferenceEquals(element.NamespaceUri, Namespaces.Svg))
            return element.LocalName is "text" or "tspan" or "textPath" or "a" ? ChildrenMode.Normal : ChildrenMode.Svg;

        return ChildrenMode.Normal;
    }

    /// <summary>https://html.spec.whatwg.org/multipage/dom.html#dom-innertext: steps 4 to 7 (the required line break counts).</summary>
    private string GetResult()
    {
        var items = _results.Where(item => item is not string { Length: 0 }).ToList();
        var start = 0;
        while (start < items.Count && items[start] is int)
        {
            start++;
        }

        var end = items.Count;
        while (end > start && items[end - 1] is int)
        {
            end--;
        }

        var builder = new StringBuilder();
        var pendingCount = 0;
        for (var i = start; i < end; i++)
        {
            if (items[i] is int count)
            {
                pendingCount = Math.Max(pendingCount, count);
                continue;
            }

            if (pendingCount > 0)
            {
                builder.Append('\n', pendingCount);
                pendingCount = 0;
            }

            builder.Append((string)items[i]);
        }

        return builder.ToString();
    }

    private enum ChildrenMode
    {
        Normal,

        /// <summary>The descendants of a select element: only the option elements render their text.</summary>
        Select,

        /// <summary>The children of a closed details element: only its summary is rendered.</summary>
        ClosedDetails,

        /// <summary>The children of an SVG element: only the text content elements render their text.</summary>
        Svg,
    }

    private void CollectChildren(Node parent, ComputedStyle parentStyle, ChildrenMode mode)
    {
        var summary = mode == ChildrenMode.ClosedDetails ? parent.ChildNodes.OfType<Element>().FirstOrDefault(e => e.IsHtml(TagId.Summary)) : null;
        for (var child = parent.FirstChild; child is not null; child = child.NextSibling)
        {
            if (_range is not null && !_range.IntersectsNode(child))
                continue;

            switch (child)
            {
                case Text text when mode == ChildrenMode.Normal && !IsTableWhitespace(text, parentStyle):
                    // The text of the inert nodes cannot be selected (https://html.spec.whatwg.org/multipage/interaction.html#inert):
                    // as in the browsers, it is not in the text of a selection
                    if (_range is not null && FocusModel.IsInert(text))
                        break;

                    CollectText(text, parentStyle);
                    break;

                case Element element:
                    // The SVG elements that are never rendered (definitions, paint servers, metadata)
                    if (BoxGeneration.IsNeverRenderedSvgElement(element))
                        break;

                    if (mode == ChildrenMode.ClosedDetails && !ReferenceEquals(element, summary))
                        break;

                    CollectElement(element, mode);
                    break;
            }
        }
    }

    /// <summary>The white space between the table parts is not rendered (https://drafts.csswg.org/css-tables/#fixup-algorithm).</summary>
    private static bool IsTableWhitespace(Text text, ComputedStyle parentStyle)
    {
        var display = parentStyle.Layout.Display;
        return (display.IsTable || (display.IsInternalTableBox && display.Internal != DisplayInternal.TableCell))
            && text.Data.AsSpan().IndexOfAnyExcept(" \t\n\r\f") < 0;
    }

    private void CollectElement(Element element, ChildrenMode parentMode)
    {
        if (!IsInFlatTree(element))
            return;

        var style = _engine.GetStyle(element);
        var layout = style.Layout;
        var display = layout.Display;
        if (display.IsNone)
            return;

        var mode = GetChildrenMode(element, parentMode);
        if (display.IsContents)
        {
            CollectChildren(element, style, mode);
            return;
        }

        var visible = layout.Visibility == Visibility.Visible;
        if (element.IsHtml(TagId.Br))
        {
            if (visible)
            {
                ForcedLineBreak();
            }

            return;
        }

        // The replaced elements and the elements whose contents are not rendered are atomic inlines without text (the
        // select elements render their options, https://html.spec.whatwg.org/multipage/dom.html#rendered-text-collection-steps)
        if (IsReplaced(element, style))
        {
            if (!IsBlockLevel(display))
            {
                AtomicContent();
            }
            else if (visible)
            {
                RequiredLineBreak(1);
            }

            return;
        }

        var isParagraph = element.IsHtml(TagId.P);
        var isBlockLevel = IsBlockLevel(display);
        var isAtomicInline = !isBlockLevel && display.IsAtomicInline;
        var isCaption = display.Internal == DisplayInternal.TableCaption;
        var isLineContainer = isBlockLevel || isAtomicInline || isCaption || display.Internal is DisplayInternal.TableCell or DisplayInternal.TableRow;
        if (isAtomicInline)
        {
            AtomicContent();
        }

        if (visible && (isBlockLevel || isParagraph || isCaption))
        {
            RequiredLineBreak(isParagraph ? 2 : 1);
        }

        if (isLineContainer)
        {
            // The line boxes of a block container: the collapsible spaces at the start and end of its lines are removed
            _lines.Push(CreateLineState(element, style));
        }

        // https://drafts.csswg.org/css-contain-2/#cv-notes: "Skipped contents do not contribute to the result of innerText"
        var skipContents = ContentRelevancy.SkipsContents(element, style);
        var atomicInlineStart = isAtomicInline && _range is not null ? _results.Count : -1;
        if (!skipContents)
        {
            CollectChildren(element, style, mode);
        }

        // The text of a range (the text that is copied): as in the browsers, the block-level boxes at the start and at the end
        // of an atomic inline (e.g. the items of an inline flex container) do not break the line that contains the atomic
        // inline. innerText keeps their required line breaks, as specified.
        if (atomicInlineStart >= 0)
        {
            while (atomicInlineStart < _results.Count && _results[atomicInlineStart] is int)
            {
                _results.RemoveAt(atomicInlineStart);
            }

            while (_results.Count > atomicInlineStart && _results[^1] is int)
            {
                _results.RemoveAt(_results.Count - 1);
            }
        }

        if (isLineContainer)
        {
            _lines.Pop();
        }

        if (visible)
        {
            if (display.Internal == DisplayInternal.TableCell && !IsLast(element, DisplayInternal.TableCell))
            {
                Append("\t");
            }

            if (display.Internal == DisplayInternal.TableRow && !IsLastRow(element))
            {
                ForcedLineBreak();
            }

            if (isBlockLevel || isParagraph || isCaption)
            {
                RequiredLineBreak(isParagraph ? 2 : 1);
            }
        }
    }

    /// <summary>The block-level boxes (the table captions are handled separately); like the browsers, the math boxes break the lines too.</summary>
    private static bool IsBlockLevel(Display display) => (display.IsBlockLevel && display.Internal == DisplayInternal.None) || (display.GeneratesBox && display.Inside == DisplayInside.Math);

    /// <summary>
    /// The HTML elements whose contents are not rendered as text (replaced elements, form controls, see <see cref="BoxGeneration.IsReplaced"/>),
    /// except the select elements (their options are rendered); the text of the SVG text content elements is rendered.
    /// </summary>
    private static bool IsReplaced(Element element, ComputedStyle style) => element.IsHtmlNamespace && !element.IsHtml(TagId.Select) && BoxGeneration.IsReplaced(element, style);

    private static bool IsInFlatTree(Element element) => element.ParentNode is not Element { AttachedShadowRoot: not null } || element.Slottable?.AssignedSlot is not null;

    /// <summary>Returns whether the element is the last box of the display type in its parent (a cell in its row).</summary>
    private bool IsLast(Element element, DisplayInternal display)
    {
        for (var sibling = element.NextElementSibling; sibling is not null; sibling = sibling.NextElementSibling)
        {
            if (_engine.GetStyle(sibling).Layout.Display is { GeneratesBox: true } siblingDisplay && siblingDisplay.Internal == display)
                return false;
        }

        return true;
    }

    /// <summary>Returns whether the row is the last row of the nearest ancestor table (its row groups included).</summary>
    private bool IsLastRow(Element row)
    {
        Element? table = null;
        for (var ancestor = row.ParentElement; ancestor is not null; ancestor = ancestor.ParentElement)
        {
            if (_engine.GetStyle(ancestor).Layout.Display.IsTable)
            {
                table = ancestor;
                break;
            }
        }

        if (table is null)
            return IsLast(row, DisplayInternal.TableRow);

        Element? last = null;
        for (var node = table.FirstElementChild; node is not null;)
        {
            var display = _engine.GetStyle(node).Layout.Display;
            if (display.Internal == DisplayInternal.TableRow)
            {
                last = node;
            }

            // The rows of the row groups, not of the nested tables
            if (display.IsTableRowGroup && node.FirstElementChild is { } child)
            {
                node = child;
                continue;
            }

            while (node is not null && node.NextElementSibling is null && !ReferenceEquals(node.ParentElement, table))
            {
                node = node.ParentElement;
            }

            node = node?.NextElementSibling;
        }

        return ReferenceEquals(last, row);
    }

    // ----------------------------------------------------------------------------------------------------------------
    // White space processing (https://drafts.csswg.org/css-text/#white-space-processing)
    // ----------------------------------------------------------------------------------------------------------------

    private sealed class LineState
    {
        /// <summary>Whether no content was written on the current line (its collapsible spaces are removed).</summary>
        public bool AtLineStart { get; set; } = true;

        /// <summary>Whether a collapsible space is waiting for the next content of the line.</summary>
        public bool PendingSpace { get; set; }

        /// <summary>Whether the previous character is a letter or a digit (for text-transform: capitalize).</summary>
        public bool InWord { get; set; }

        /// <summary>Whether the current line is the first line of the block container (::first-line).</summary>
        public bool FirstLine { get; set; } = true;

        /// <summary>Whether the first letter of the block container was written (::first-letter).</summary>
        public bool FirstLetterDone { get; set; }

        /// <summary>The text-transform of the ::first-line pseudo-element of the block container, when it differs from the one of the block.</summary>
        public TextTransform? FirstLineTransform { get; init; }

        /// <summary>The text-transform of the ::first-letter pseudo-element of the block container, when it differs from the one of the block.</summary>
        public TextTransform? FirstLetterTransform { get; init; }
    }

    private LineState Line => _lines.Peek();

    /// <summary>The line state of a block container, with the text-transform of its ::first-line and ::first-letter pseudo-elements.</summary>
    private LineState CreateLineState(Element element, ComputedStyle style)
    {
        var transform = style.Layout.TextTransform;
        var firstLine = _engine.GetStyle(element, "first-line").Layout.TextTransform;
        var firstLetter = _engine.GetStyle(element, "first-letter").Layout.TextTransform;
        return new LineState
        {
            FirstLineTransform = firstLine != transform ? firstLine : null,
            FirstLetterTransform = firstLetter != transform ? firstLetter : null,
        };
    }

    private void RequiredLineBreak(int count)
    {
        _results.Add(count);
        Line.FirstLine = false;
        Line.AtLineStart = true;
        Line.PendingSpace = false;
        Line.InWord = false;
    }

    private void ForcedLineBreak()
    {
        Append("\n");
        Line.FirstLine = false;
        Line.AtLineStart = true;
        Line.PendingSpace = false;
        Line.InWord = false;
    }

    /// <summary>An atomic inline: the pending collapsible space is written before it.</summary>
    private void AtomicContent()
    {
        FlushPendingSpace();
        Line.AtLineStart = false;
        Line.InWord = false;
    }

    private void FlushPendingSpace()
    {
        if (Line.PendingSpace)
        {
            Append(" ");
            Line.PendingSpace = false;
        }
    }

    private void Append(string value) => _results.Add(value);

    private void CollectText(Text text, ComputedStyle style)
    {
        var layout = style.Layout;
        if (layout.Visibility != Visibility.Visible)
            return;

        var data = text.Data;
        var prefix = "";
        if (_range is not null)
        {
            // The characters of the range; the characters before it in the text node are processed without being collected,
            // so that a collapsible space at the start of the range is kept when it follows rendered content
            var end = ReferenceEquals(_range.EndContainer, text) ? (int)Math.Min(_range.EndOffset, (uint)data.Length) : data.Length;
            var start = ReferenceEquals(_range.StartContainer, text) ? (int)Math.Min(_range.StartOffset, (uint)end) : 0;
            prefix = data[..start];
            data = data[start..end];
        }

        if (data.Length == 0)
            return;

        var collapse = layout.WhiteSpaceCollapse;
        var transform = TextTransformer.ResolveMathAuto(layout.TextTransform, text.Data);
        CaseLanguage? language = null;
        var line = Line;
        var builder = new StringBuilder();
        if (prefix.Length > 0)
        {
            Collect(prefix);
            builder.Clear();

            // The spaces before the range are not in the range
            line.PendingSpace = false;
        }

        Collect(data);

        // A collapsible space at the end of the range is rendered when content follows it on its line: it is in the text of
        // the range (e.g. the word and the space that are selected before another word)
        if (line.PendingSpace && _range is not null && ReferenceEquals(_range.EndContainer, text) && IsFollowedByContentOnLine(text, (int)Math.Min(_range.EndOffset, (uint)text.Data.Length)))
        {
            builder.Append(' ');
            line.PendingSpace = false;
        }

        if (builder.Length > 0)
        {
            Append(builder.ToString());
        }

        // Appends the character at an index of a text, transformed by text-transform; returns the number of characters of the
        // text that were transformed with it
        int AppendChar(ReadOnlySpan<char> source, int index)
        {
            var c = source[index];
            var consumed = 1;
            if (line.PendingSpace)
            {
                builder.Append(' ');
                line.PendingSpace = false;
            }

            // The ::first-line and ::first-letter of the block container
            var charTransform = transform;
            if (line.FirstLine && line.FirstLineTransform is { } firstLineTransform)
            {
                charTransform = firstLineTransform;
            }

            if (!line.FirstLetterDone && char.IsLetterOrDigit(c))
            {
                line.FirstLetterDone = true;
                if (line.FirstLetterTransform is { } firstLetterTransform)
                {
                    charTransform = firstLetterTransform;
                }
            }

            if (charTransform == TextTransform.None)
            {
                builder.Append(c);
                line.InWord = TextTransformer.IsWordCharacter(c);
            }
            else
            {
                language ??= TextTransformer.GetCaseLanguage(text.ParentElement);
                Span<char> buffer = stackalloc char[TextTransformer.MaxLength];
                var count = TextTransformer.Transform(source, index, charTransform, language.Value, wordStart: !line.InWord, buffer, out consumed);
                builder.Append(buffer[..count]);
                line.InWord = TextTransformer.IsWordCharacter(source, index);
            }

            line.AtLineStart = false;
            return consumed;
        }

        void Newline()
        {
            line.PendingSpace = false;
            builder.Append('\n');
            line.FirstLine = false;
            line.AtLineStart = true;
            line.InWord = false;
        }

        void Collect(string data)
        {
            switch (collapse)
            {
                case WhiteSpaceCollapse.Preserve or WhiteSpaceCollapse.BreakSpaces or WhiteSpaceCollapse.PreserveSpaces:
                    for (var i = 0; i < data.Length; i++)
                    {
                        var c = data[i];
                        if (c == '\n' && collapse != WhiteSpaceCollapse.PreserveSpaces)
                        {
                            Newline();
                        }
                        else if (c == '\n')
                        {
                            // preserve-spaces: the segment breaks are spaces
                            AppendChar(" ", 0);
                        }
                        else
                        {
                            i += AppendChar(data, i) - 1;
                        }
                    }

                    break;

                default:
                {
                    // The spaces and tabs around the segment breaks are removed, then the segment breaks are spaces (collapse,
                    // discard) or line breaks (preserve-breaks), and the sequences of spaces collapse. As in the browsers, the
                    // carriage returns are collapsible spaces (they are preserved with the other values).
                    var normalized = RemoveSpacesAroundSegmentBreaks(data.Replace('\r', ' '));
                    for (var i = 0; i < normalized.Length; i++)
                    {
                        var c = normalized[i];
                        if (c == '\n' && collapse == WhiteSpaceCollapse.PreserveBreaks)
                        {
                            Newline();
                        }
                        else if (c is ' ' or '\t' or '\n')
                        {
                            if (collapse == WhiteSpaceCollapse.Discard)
                                continue;

                            if (!line.AtLineStart)
                            {
                                line.PendingSpace = true;
                            }

                            line.InWord = false;
                        }
                        else
                        {
                            i += AppendChar(normalized, i) - 1;
                        }
                    }

                    break;
                }
            }
        }
    }

    /// <summary>
    /// Whether content follows an offset of a Text node on its line: text that is not white space, a replaced element or an
    /// atomic inline, before the end of the line (a br element, a block-level box or the end of the block container).
    /// </summary>
    private bool IsFollowedByContentOnLine(Text text, int offset)
    {
        if (text.Data.AsSpan(offset).IndexOfAnyExcept(" \t\n\r\f") >= 0)
            return true;

        Node node = text;
        while (true)
        {
            // The next node after the subtree of the node: the inline boxes are left until the block container of the line
            Node? next;
            while ((next = node.NextSibling) is null)
            {
                if (node.ParentNode is not Element parent || !_engine.GetStyle(parent).Layout.Display.IsInlineBox)
                    return false;

                node = parent;
            }

            // The first item of the subtree of the node, if any
            node = next;
            while (true)
            {
                if (node is Text nextText)
                {
                    if (nextText.Data.AsSpan().IndexOfAnyExcept(" \t\n\r\f") >= 0)
                        return true;

                    break;
                }

                if (node is not Element element)
                    break;

                var style = _engine.GetStyle(element);
                var display = style.Layout.Display;
                if (display.IsNone)
                    break;

                if (element.IsHtml(TagId.Br) || !display.IsInlineLevel)
                    return false;

                if (!display.IsInlineBox || BoxGeneration.IsReplaced(element, style))
                    return true;

                if (element.FirstChild is not { } child)
                    break;

                node = child;
            }
        }
    }

    private static string RemoveSpacesAroundSegmentBreaks(string data)
    {
        if (!data.Contains('\n', StringComparison.Ordinal))
            return data;

        var builder = new StringBuilder(data.Length);
        for (var i = 0; i < data.Length; i++)
        {
            var c = data[i];
            if (c is ' ' or '\t')
            {
                // A space before a segment break is removed
                var next = i;
                while (next < data.Length && data[next] is ' ' or '\t')
                {
                    next++;
                }

                if (next < data.Length && data[next] == '\n')
                {
                    i = next - 1;
                    continue;
                }

                // A space after a segment break is removed
                if (builder.Length > 0 && builder[^1] == '\n')
                    continue;
            }

            builder.Append(c);
        }

        return builder.ToString();
    }

}
