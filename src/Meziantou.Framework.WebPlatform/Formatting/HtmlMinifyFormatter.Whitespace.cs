using System.Text;
using Meziantou.Framework.WebPlatform.Css.Cascade;
using Meziantou.Framework.WebPlatform.Css.Om;
using Meziantou.Framework.WebPlatform.Css.Syntax;
using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.Infra;

namespace Meziantou.Framework.WebPlatform.Formatting;

public sealed partial class HtmlMinifyFormatter
{
    /// <summary>
    /// Computes the minified value of the text nodes. The nodes are split into the sequences of inline content that are
    /// laid out together (the inline formatting contexts, delimited by the block-level elements): in a sequence, a
    /// collapsible space is not rendered when it follows another collapsible space, or when it is at the start or the end
    /// of a line (https://drafts.csswg.org/css-text-4/#white-space-phase-2).
    /// </summary>
    private sealed class WhitespaceAnalyzer
    {
        /// <summary>The text of a pseudo-element whose content is not a string (e.g. <c>counter()</c>): non-whitespace content.</summary>
        private const string OpaqueContent = "￼";

        private readonly HtmlMinifyFormatter _formatter;
        private readonly StyleEngine _styleEngine;
        private readonly Dictionary<Element, ElementInfo> _elements = new(ReferenceEqualityComparer.Instance);
        private readonly Dictionary<(Element Element, bool After), string?> _pseudoTexts = [];
        private readonly Dictionary<Text, string> _texts;

        /// <summary>The lists of the contexts that were processed, reused by the next contexts.</summary>
        private readonly Stack<List<Atom>> _freeContexts = new();

        /// <summary>The array of <see cref="ProcessContext"/>, reused by the next contexts.</summary>
        private bool?[] _onlyWhitespaceAfter = [];

        public WhitespaceAnalyzer(HtmlMinifyFormatter formatter, Document document, Dictionary<Text, string> texts)
        {
            _formatter = formatter;
            _texts = texts;
            _styleEngine = new StyleEngine(document, MediaEnvironment.Default with { MatchAllMediaQueries = true, ScriptingEnabled = document.ScriptingEnabled });
        }

        private enum DisplayKind
        {
            /// <summary>display: none: not rendered, transparent for the surrounding content.</summary>
            Hidden,

            /// <summary>A non-replaced inline box (or display: contents): its content is part of the surrounding content.</summary>
            Inline,

            /// <summary>An atomic inline (inline-block, replaced element...): non-whitespace content for the surrounding content.</summary>
            Atomic,

            /// <summary>A block-level box: the surrounding lines end before and start after it.</summary>
            Block,

            /// <summary>A forced line break (br).</summary>
            LineBreak,

            /// <summary>An SVG or MathML element: laid out by its own rules, its text is not modified.</summary>
            Foreign,
        }

        private enum AtomKind
        {
            /// <summary>The start or the end of a line.</summary>
            Boundary,

            /// <summary>Content that is not collapsible whitespace (or unknown content, which is assumed not to be).</summary>
            Content,

            /// <summary>A text node whose whitespace is collapsible.</summary>
            Text,

            /// <summary>The content of a ::before or ::after pseudo-element.</summary>
            Pseudo,
        }

        private readonly record struct Atom(AtomKind Kind, Node? Node = null, bool After = false);

        private sealed class ElementInfo(DisplayKind display, string whiteSpaceCollapse)
        {
            public DisplayKind Display { get; } = display;

            public string WhiteSpaceCollapse { get; } = whiteSpaceCollapse;
        }

        public void Analyze(Node root)
        {
            // The contexts are independent: each one is processed when it ends, and its list is reused
            var current = new List<Atom>();

            // Outside of a document, the content around the root is unknown: it may be inserted anywhere
            var unknownEdges = root is not Document;
            if (unknownEdges)
            {
                current.Add(new Atom(AtomKind.Content));
            }

            var stack = new Stack<(Node Node, bool Exit, List<Atom>? Saved, bool UnknownEdges)>();
            stack.Push((root, Exit: false, Saved: null, UnknownEdges: false));
            while (stack.Count > 0)
            {
                var (node, exit, saved, edges) = stack.Pop();
                if (exit)
                {
                    if (node is Element exitElement && GetInfo(exitElement).Display is DisplayKind.Inline or DisplayKind.Block or DisplayKind.Atomic)
                    {
                        current.Add(new Atom(AtomKind.Pseudo, exitElement, After: true));
                    }

                    if (edges)
                    {
                        current.Add(new Atom(AtomKind.Content));
                    }

                    if (saved is not null)
                    {
                        ProcessContext(current);
                        current.Clear();
                        _freeContexts.Push(current);
                        current = saved;
                        if (node is Element { } blockElement && GetInfo(blockElement).Display == DisplayKind.Block)
                        {
                            current.Add(new Atom(AtomKind.Boundary));
                        }
                    }

                    continue;
                }

                switch (node)
                {
                    case Text text:
                        AddText(current, text);
                        break;

                    case HtmlTemplateElement template:
                        // The contents of a template are inserted somewhere else: the content around them is unknown
                        current = StartContext(current, unknownEdges: true, out var templateSaved);
                        stack.Push((template, Exit: true, templateSaved, UnknownEdges: true));
                        PushChildren(stack, template.TemplateContents);
                        break;

                    case Element element:
                    {
                        var info = GetInfo(element);
                        var isVoid = IsVoidElement(element);
                        switch (info.Display)
                        {
                            case DisplayKind.Hidden:
                                if (!isVoid && element.FirstChild is not null)
                                {
                                    // The content may be displayed later (e.g. by a script): keep the whitespace at its
                                    // edges, except in the head, whose content is never rendered
                                    var hiddenEdges = !IsInHead(element);
                                    current = StartContext(current, hiddenEdges, out var hiddenSaved);
                                    stack.Push((element, Exit: true, hiddenSaved, hiddenEdges));
                                    PushChildren(stack, element);
                                }

                                break;

                            case DisplayKind.LineBreak:
                                current.Add(new Atom(AtomKind.Boundary));
                                break;

                            case DisplayKind.Inline:
                                if (!isVoid)
                                {
                                    current.Add(new Atom(AtomKind.Pseudo, element, After: false));
                                    stack.Push((element, Exit: true, Saved: null, UnknownEdges: false));
                                    PushChildren(stack, element);
                                }

                                break;

                            case DisplayKind.Atomic or DisplayKind.Foreign:
                                current.Add(new Atom(AtomKind.Content));
                                if (!isVoid)
                                {
                                    current = StartContext(current, unknownEdges: false, out var atomicSaved);
                                    if (info.Display == DisplayKind.Atomic)
                                    {
                                        current.Add(new Atom(AtomKind.Pseudo, element, After: false));
                                    }

                                    stack.Push((element, Exit: true, atomicSaved, UnknownEdges: false));
                                    PushChildren(stack, element);
                                }

                                break;

                            case DisplayKind.Block:
                                current.Add(new Atom(AtomKind.Boundary));
                                if (!isVoid)
                                {
                                    current = StartContext(current, unknownEdges: false, out var blockSaved);
                                    current.Add(new Atom(AtomKind.Pseudo, element, After: false));
                                    stack.Push((element, Exit: true, blockSaved, UnknownEdges: false));
                                    PushChildren(stack, element);
                                }

                                break;
                        }

                        break;
                    }

                    case Document or DocumentFragment:
                        PushChildren(stack, node);
                        break;
                }
            }

            if (unknownEdges)
            {
                current.Add(new Atom(AtomKind.Content));
            }

            ProcessContext(current);
        }

        private List<Atom> StartContext(List<Atom> current, bool unknownEdges, out List<Atom> saved)
        {
            saved = current;
            var context = _freeContexts.TryPop(out var free) ? free : [];
            if (unknownEdges)
            {
                context.Add(new Atom(AtomKind.Content));
            }

            return context;
        }

        private static void PushChildren(Stack<(Node Node, bool Exit, List<Atom>? Saved, bool UnknownEdges)> stack, Node parent)
        {
            for (var child = parent.LastChild; child is not null; child = child.PreviousSibling)
            {
                stack.Push((child, Exit: false, Saved: null, UnknownEdges: false));
            }
        }

        private void AddText(List<Atom> context, Text text)
        {
            var data = text.Data;
            if (data.Length == 0)
                return;

            // The text of a textarea is its value: it is never modified
            if (text.ParentNode is not Element { IsHtmlNamespace: true } parent || HasRawTextContent(parent) || parent.TagId == TagId.Textarea)
            {
                context.Add(new Atom(AtomKind.Content));
                return;
            }

            var whiteSpaceCollapse = GetInfo(parent).WhiteSpaceCollapse;
            if (whiteSpaceCollapse == "collapse")
            {
                if (_formatter.CollapseWhitespace)
                {
                    context.Add(new Atom(AtomKind.Text, text));
                }
                else
                {
                    // The whitespace is kept as is: a sequence of whitespace is still collapsed when rendered
                    context.Add(new Atom(AtomKind.Content));
                }

                return;
            }

            if (whiteSpaceCollapse == "preserve" && _formatter.TrimTrailingWhitespaceInPreformattedText)
            {
                var trimmed = TrimTrailingWhitespaceOfLines(data, trimEnd: IsAtEndOfPreformattedElement(text));
                if (!ReferenceEquals(trimmed, data))
                {
                    _texts[text] = trimmed;
                }

                if (trimmed.Length == 0)
                    return;
            }

            // Preserved whitespace is rendered: it is content
            context.Add(new Atom(AtomKind.Content));
        }

        /// <summary>Computes the value of the collapsible text nodes of an inline formatting context, in order.</summary>
        /// <remarks>
        /// The content of the pseudo-elements is only computed when a space depends on it: when the previous non-empty
        /// content of a space is a pseudo-element, or when only whitespace and pseudo-elements follow a space.
        /// </remarks>
        private void ProcessContext(List<Atom> atoms)
        {
            // For each atom, whether the content after it, up to the end of the line, is only collapsible whitespace
            // (computed lazily from the original text: the minification never makes whitespace significant)
            if (_onlyWhitespaceAfter.Length < atoms.Count)
            {
                _onlyWhitespaceAfter = new bool?[Math.Max(atoms.Count, _onlyWhitespaceAfter.Length * 2)];
            }

            var onlyWhitespaceAfter = _onlyWhitespaceAfter;
            Array.Clear(onlyWhitespaceAfter, 0, atoms.Count);

            // Whether the previous rendered content ends with a collapsible space (or is the start of a line), ignoring the
            // pseudo-elements from the index pendingPseudoStart, which are resolved when needed
            var previousEndsWithSpace = true;
            var pendingPseudoStart = -1;
            for (var i = 0; i < atoms.Count; i++)
            {
                var atom = atoms[i];
                switch (atom.Kind)
                {
                    case AtomKind.Boundary:
                        previousEndsWithSpace = true;
                        pendingPseudoStart = -1;
                        break;

                    case AtomKind.Content:
                        previousEndsWithSpace = false;
                        pendingPseudoStart = -1;
                        break;

                    case AtomKind.Pseudo:
                        if (pendingPseudoStart < 0)
                        {
                            pendingPseudoStart = i;
                        }

                        break;

                    case AtomKind.Text:
                        var text = (Text)atom.Node!;
                        var value = InfraString.CollapseAsciiWhitespace(text.Data);
                        var start = 0;
                        if (value.StartsWith(' ', StringComparison.Ordinal))
                        {
                            if (pendingPseudoStart >= 0)
                            {
                                // The last non-empty pseudo-element is the previous content
                                for (var j = i - 1; j >= pendingPseudoStart; j--)
                                {
                                    if (atoms[j].Kind == AtomKind.Pseudo && GetPseudoText(atoms[j]) is { Length: > 0 } pseudoText)
                                    {
                                        previousEndsWithSpace = InfraAscii.IsAsciiWhitespace(pseudoText[^1]);
                                        break;
                                    }
                                }

                                pendingPseudoStart = -1;
                            }

                            if (previousEndsWithSpace)
                            {
                                start = 1;
                            }
                        }

                        var end = value.Length > start && value[^1] == ' ' && IsOnlyWhitespaceAfter(atoms, onlyWhitespaceAfter, i) ? value.Length - 1 : value.Length;
                        value = value[start..end];
                        if (!ReferenceEquals(value, text.Data))
                        {
                            _texts[text] = value;
                        }

                        if (value.Length > 0)
                        {
                            previousEndsWithSpace = value[^1] == ' ';
                            pendingPseudoStart = -1;
                        }

                        break;
                }
            }
        }

        /// <summary>Returns whether the atoms after <paramref name="index"/>, up to the end of the line, are only collapsible whitespace.</summary>
        private bool IsOnlyWhitespaceAfter(List<Atom> atoms, bool?[] memo, int index)
        {
            if (memo[index] is { } known)
                return known;

            // Every atom between index and the first atom that decides the result has the same result
            var result = true;
            var last = index + 1;
            for (; last < atoms.Count; last++)
            {
                var atom = atoms[last];
                if (atom.Kind == AtomKind.Boundary)
                    break;

                if (!IsWhitespaceOnlyAtom(atom))
                {
                    result = false;
                    break;
                }

                if (memo[last] is { } next)
                {
                    result = next;
                    break;
                }
            }

            for (var i = index; i < last; i++)
            {
                memo[i] = result;
            }

            return result;
        }

        private bool IsWhitespaceOnlyAtom(Atom atom) => atom.Kind switch
        {
            AtomKind.Text => InfraString.IsAsciiWhitespaceOnly(((Text)atom.Node!).Data),
            AtomKind.Pseudo => InfraString.IsAsciiWhitespaceOnly(GetPseudoText(atom)),
            _ => false,
        };

        private ElementInfo GetInfo(Element element)
        {
            if (_elements.TryGetValue(element, out var info))
                return info;

            info = ComputeInfo(element);
            _elements.Add(element, info);
            return info;
        }

        private ElementInfo ComputeInfo(Element element)
        {
            if (!element.IsHtmlNamespace)
            {
                // The SVG and MathML elements are laid out by their own rules: their content is not modified
                return new ElementInfo(DisplayKind.Foreign, "preserve");
            }

            var style = _styleEngine.GetStyle(element);
            var display = style.GetComputedValue("display");
            var whiteSpaceCollapse = style.GetComputedValue("white-space-collapse");
            DisplayKind kind;
            if (display == "none")
            {
                kind = DisplayKind.Hidden;
            }
            else if (element.TagId == TagId.Br)
            {
                kind = DisplayKind.LineBreak;
            }
            else if (display is "inline" or "contents" or "inline flow")
            {
                kind = IsReplacedElement(element) ? DisplayKind.Atomic : DisplayKind.Inline;
            }
            else if (display.StartsWith("inline", StringComparison.Ordinal) || display.StartsWith("ruby", StringComparison.Ordinal))
            {
                kind = DisplayKind.Atomic;
            }
            else
            {
                kind = DisplayKind.Block;
            }

            return new ElementInfo(kind, whiteSpaceCollapse);
        }

        /// <summary>The elements whose content is not laid out as inline text (replaced elements and form controls).</summary>
        private static bool IsReplacedElement(Element element) =>
            element.TagId is TagId.Img or TagId.Input or TagId.Textarea or TagId.Select or TagId.Button or TagId.Iframe or TagId.Embed or
                TagId.Object or TagId.Video or TagId.Audio or TagId.Canvas or TagId.Meter or TagId.Progress;

        private static bool IsInHead(Element element)
        {
            for (Node? node = element; node is not null; node = node.ParentNode)
            {
                if (node is Element { IsHtmlNamespace: true, TagId: TagId.Head })
                    return true;
            }

            return false;
        }

        /// <summary>Returns the text of a ::before or ::after pseudo-element, null when it has no content.</summary>
        private string? GetPseudoText(Atom atom)
        {
            var element = (Element)atom.Node!;
            var key = (element, atom.After);
            if (_pseudoTexts.TryGetValue(key, out var result))
                return result;

            result = ComputePseudoText(element, atom.After);
            _pseudoTexts.Add(key, result);
            return result;
        }

        private string? ComputePseudoText(Element element, bool after)
        {
            var style = _styleEngine.GetStyle(element, after ? "after" : "before");

            // Most pseudo-elements have no content declaration: their content is normal (it is not inherited), and computing
            // it would compute the content of the ancestors
            if (!style.TryGetCascadedValue("content", out _))
                return null;

            var content = style.GetComputedValue("content");
            if (content is "normal" or "none" || style.GetComputedValue("display") == "none")
                return null;

            var values = CssParser.ParseListOfComponentValues(content);
            string? text = null;
            foreach (var value in values)
            {
                if (value is CssPreservedToken { Type: CssTokenType.Whitespace })
                    continue;

                if (text is null && value is CssPreservedToken { Type: CssTokenType.String } stringToken)
                {
                    text = stringToken.Token.Value;
                    continue;
                }

                // Not a single string (counter(), attr(), quotes...): unknown non-whitespace content
                return OpaqueContent;
            }

            return text;
        }

        private bool IsAtEndOfPreformattedElement(Text text)
        {
            Node node = text;
            while (true)
            {
                for (var next = node.NextSibling; next is not null; next = next.NextSibling)
                {
                    if (next is Element or Text { Data.Length: > 0 })
                        return false;
                }

                if (node.ParentNode is not Element parent || !parent.IsHtmlNamespace)
                    return false;

                if (parent.ParentNode is not Element grandParent || !grandParent.IsHtmlNamespace || GetInfo(grandParent).WhiteSpaceCollapse != "preserve")
                    return GetInfo(parent).Display == DisplayKind.Block;

                node = parent;
            }
        }

        private static string TrimTrailingWhitespaceOfLines(string value, bool trimEnd)
        {
            StringBuilder? result = null;
            var lineStart = 0;
            for (var i = 0; i <= value.Length; i++)
            {
                var isLineEnd = i == value.Length ? trimEnd : value[i] is '\n' or '\r';
                if (!isLineEnd)
                    continue;

                var lineEnd = i;
                while (lineEnd > lineStart && value[lineEnd - 1] is ' ' or '\t')
                {
                    lineEnd--;
                }

                if (lineEnd != i || result is not null)
                {
                    result ??= new StringBuilder(value.Length).Append(value, 0, lineStart);
                    result.Append(value, lineStart, lineEnd - lineStart);
                    if (i < value.Length)
                    {
                        result.Append(value[i]);
                    }
                }

                lineStart = i + 1;
            }

            if (result is null)
                return value;

            if (!trimEnd && lineStart < value.Length)
            {
                result.Append(value, lineStart, value.Length - lineStart);
            }

            return result.ToString();
        }
    }
}
