using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.Infra;

namespace Meziantou.Framework.WebPlatform.Formatting;

public sealed partial class HtmlMinifyFormatter
{
    private sealed class MinifyWriter(HtmlMinifyFormatter formatter, TextWriter output) : HtmlMarkupWriter(output)
    {
        private readonly HtmlMinifyFormatter _formatter = formatter;

        /// <summary>The minified value of the text nodes whose value changes (an empty value removes the node).</summary>
        private readonly Dictionary<Text, string> _texts = new(ReferenceEqualityComparer.Instance);

        /// <summary>Whether the last attribute written is an unquoted value that ends with a solidus.</summary>
        private bool _lastAttributeEndsWithSolidus;

        public override void Write(Node node)
        {
            ArgumentNullException.ThrowIfNull(node);
            if (_formatter.CollapseWhitespace || _formatter.TrimTrailingWhitespaceInPreformattedText)
            {
                var document = node as Document ?? node.NodeDocument;
                new WhitespaceAnalyzer(_formatter, document, _texts).Analyze(node);
            }

            base.Write(node);
        }

        protected override bool ShouldWrite(Node node) => !IsRemoved(node);

        protected override void WriteStartTag(Element element)
        {
            if (CanOmitStartTag(element))
                return;

            Output.Write('<');
            Output.Write(GetSerializedTagName(element));
            _lastAttributeEndsWithSolidus = false;
            WriteAttributes(element);

            // Not needed by the HTML parser (the solidus is part of the value), but a few tools read "/>" as a self-closing tag
            if (_lastAttributeEndsWithSolidus)
            {
                Output.Write(' ');
            }

            Output.Write('>');
        }

        protected override void WriteEndTag(Element element)
        {
            if (CanOmitEndTag(element))
                return;

            base.WriteEndTag(element);
        }

        protected override void WriteAttribute(Element element, string serializedName, string value)
        {
            _lastAttributeEndsWithSolidus = false;
            if (_formatter.RemoveScriptTypeAttribute && serializedName == "type" && element.IsHtml(TagId.Script) && IsClassicScriptType(element, value))
                return;

            if (_formatter.CollapseWhitespace && serializedName == "class")
            {
                value = InfraString.StripAndCollapseAsciiWhitespace(value);
            }

            if (!_formatter.RemoveAttributeQuotes)
            {
                base.WriteAttribute(element, serializedName, value);
                return;
            }

            Output.Write(' ');
            Output.Write(serializedName);
            if (value.Length == 0)
                return;

            Output.Write('=');
            if (CanBeUnquoted(value))
            {
                WriteMinimallyEscaped(value, quote: null, isText: false);
                _lastAttributeEndsWithSolidus = value[^1] == '/';
                return;
            }

            // Prefer the quote that does not need to be escaped
            var quote = value.Contains('"', StringComparison.Ordinal) && !value.Contains('\'', StringComparison.Ordinal) ? '\'' : '"';
            Output.Write(quote);
            WriteMinimallyEscaped(value, quote, isText: false);
            Output.Write(quote);
        }

        protected override void WriteText(Text text)
        {
            if (text.ParentNode is Element parent && HasRawTextContent(parent))
            {
                WriteRawText(text);
                return;
            }

            WriteMinimallyEscaped(GetText(text), quote: null, isText: true);
        }

        /// <summary>
        /// Whether removing the type attribute keeps the script a classic script
        /// (https://html.spec.whatwg.org/multipage/scripting.html#prepare-the-script-element): the type is empty or a
        /// JavaScript MIME type, and there is no language attribute that would be used instead.
        /// </summary>
        private static bool IsClassicScriptType(Element element, string type)
        {
            if (element.GetAttribute("language") is { Length: > 0 })
                return false;

            return type.Length == 0 || HtmlScriptElement.IsJavaScriptMimeTypeEssenceMatch(InfraAscii.StripLeadingAndTrailingAsciiWhitespace(type).ToString());
        }

        /// <summary>https://html.spec.whatwg.org/multipage/syntax.html#unquoted</summary>
        private static bool CanBeUnquoted(string value)
        {
            foreach (var c in value)
            {
                if (InfraAscii.IsAsciiWhitespace(c) || c is '"' or '\'' or '=' or '<' or '>' or '`')
                    return false;
            }

            return true;
        }

        /// <summary>
        /// Writes the value with only the escaping needed to parse it back: <c>&lt;</c> in text, the quote of an attribute
        /// value, and <c>&amp;</c> when it could start a character reference (followed by an alphanumeric or a number sign,
        /// or at the end of a text, which may be followed by another text node).
        /// </summary>
        private void WriteMinimallyEscaped(string value, char? quote, bool isText)
        {
            var span = value.AsSpan();
            var start = 0;
            for (var i = 0; i < span.Length; i++)
            {
                var c = span[i];
                string? replacement;
                if (c == '&')
                {
                    var mustEscape = i + 1 < span.Length ? char.IsAsciiLetterOrDigit(span[i + 1]) || span[i + 1] == '#' : isText;
                    replacement = mustEscape ? "&amp;" : null;
                }
                else if (c == '<' && isText)
                {
                    replacement = "&lt;";
                }
                else if (c == '"' && quote == '"')
                {
                    replacement = "&quot;";
                }
                else
                {
                    replacement = null;
                }

                if (replacement is not null)
                {
                    Output.Write(span[start..i]);
                    Output.Write(replacement);
                    start = i + 1;
                }
            }

            Output.Write(span[start..]);
        }

        private string GetText(Text text) => _texts.TryGetValue(text, out var value) ? value : text.Data;

        private bool IsRemoved(Node node) => node switch
        {
            Comment => _formatter.RemoveComments,
            Text text => GetText(text).Length == 0,
            _ => false,
        };

        private Node? GetFirstChild(Node node)
        {
            for (var child = node.FirstChild; child is not null; child = child.NextSibling)
            {
                if (!IsRemoved(child))
                    return child;
            }

            return null;
        }

        private Node? GetNextSibling(Node node)
        {
            for (var sibling = node.NextSibling; sibling is not null; sibling = sibling.NextSibling)
            {
                if (!IsRemoved(sibling))
                    return sibling;
            }

            return null;
        }

        private Node? GetPreviousSibling(Node node)
        {
            for (var sibling = node.PreviousSibling; sibling is not null; sibling = sibling.PreviousSibling)
            {
                if (!IsRemoved(sibling))
                    return sibling;
            }

            return null;
        }

        private bool StartsWithAsciiWhitespace(Node? node) => node is Text text && GetText(text) is { Length: > 0 } value && InfraAscii.IsAsciiWhitespace(value[0]);

        private static bool IsHtmlElement(Node? node, TagId tagId) => node is Element element && element.IsHtml(tagId);

        private static bool IsHtmlElement(Node? node, TagId tagId1, TagId tagId2) => node is Element { IsHtmlNamespace: true } element && (element.TagId == tagId1 || element.TagId == tagId2);

        private static bool IsHtmlElement(Node? node, TagId tagId1, TagId tagId2, TagId tagId3) => node is Element { IsHtmlNamespace: true } element && (element.TagId == tagId1 || element.TagId == tagId2 || element.TagId == tagId3);

        /// <summary>https://html.spec.whatwg.org/multipage/syntax.html#optional-tags</summary>
        private bool CanOmitStartTag(Element element)
        {
            // A start tag must never be omitted if it has any attributes. The root is written as is: the content around it
            // is not written.
            if (!_formatter.OmitOptionalTags || ReferenceEquals(element, Root) || !element.IsHtmlNamespace || element.AttributeCount > 0 || element.IsValue is not null)
                return false;

            if (element.TagId is not (TagId.Html or TagId.Head or TagId.Body or TagId.Colgroup or TagId.Tbody) || _formatter.IsPreservedStartTag(element.LocalName))
                return false;

            var first = GetFirstChild(element);
            switch (element.TagId)
            {
                case TagId.Html:
                    // An html element's start tag may be omitted if the first thing inside the html element is not a comment.
                    return first is not Comment;

                case TagId.Head:
                    // A head element's start tag may be omitted if the element is empty, or if the first thing inside the
                    // head element is an element.
                    return first is null or Element;

                case TagId.Body:
                    // A body element's start tag may be omitted if the element is empty, or if the first thing inside the
                    // body element is not ASCII whitespace or a comment, except if the first thing inside the body element
                    // is a meta, noscript, link, script, style, or template element.
                    if (first is null)
                        return true;

                    if (first is Comment || StartsWithAsciiWhitespace(first))
                        return false;

                    return first is not Element { IsHtmlNamespace: true, TagId: TagId.Meta or TagId.Noscript or TagId.Link or TagId.Script or TagId.Style or TagId.Template };

                case TagId.Colgroup:
                    // A colgroup element's start tag may be omitted if the first thing inside the colgroup element is a col
                    // element, and if the element is not immediately preceded by another colgroup element whose end tag has
                    // been omitted. (It can't be omitted if the element is empty.)
                    return IsHtmlElement(first, TagId.Col) && !(GetPreviousSibling(element) is Element previousColgroup && previousColgroup.IsHtml(TagId.Colgroup) && CanOmitEndTag(previousColgroup));

                case TagId.Tbody:
                    // A tbody element's start tag may be omitted if the first thing inside the tbody element is a tr element,
                    // and if the element is not immediately preceded by a tbody, thead, or tfoot element whose end tag has
                    // been omitted. (It can't be omitted if the element is empty.)
                    return IsHtmlElement(first, TagId.Tr) && !(GetPreviousSibling(element) is Element previous && IsHtmlElement(previous, TagId.Tbody, TagId.Thead, TagId.Tfoot) && CanOmitEndTag(previous));

                default:
                    return false;
            }
        }

        /// <summary>https://html.spec.whatwg.org/multipage/syntax.html#optional-tags</summary>
        private bool CanOmitEndTag(Element element)
        {
            if (!_formatter.OmitOptionalTags || ReferenceEquals(element, Root) || !element.IsHtmlNamespace)
                return false;

            var next = GetNextSibling(element);
            return element.TagId switch
            {
                // An html element's end tag may be omitted if the html element is not immediately followed by a comment.
                TagId.Html => next is not Comment,

                // A head element's end tag may be omitted if the head element is not immediately followed by ASCII whitespace or a comment.
                TagId.Head => next is not Comment && !StartsWithAsciiWhitespace(next),

                // A body element's end tag may be omitted if the body element is not immediately followed by a comment.
                TagId.Body => next is not Comment,

                // An li element's end tag may be omitted if the li element is immediately followed by another li element or
                // if there is no more content in the parent element.
                TagId.Li => next is null || IsHtmlElement(next, TagId.Li),

                // A dt element's end tag may be omitted if the dt element is immediately followed by another dt element or a dd element.
                TagId.Dt => IsHtmlElement(next, TagId.Dt, TagId.Dd),

                // A dd element's end tag may be omitted if the dd element is immediately followed by another dd element or
                // a dt element, or if there is no more content in the parent element.
                TagId.Dd => next is null || IsHtmlElement(next, TagId.Dt, TagId.Dd),

                TagId.P => CanOmitParagraphEndTag(element, next),

                // An rt/rp element's end tag may be omitted if the element is immediately followed by an rt or rp element,
                // or if there is no more content in the parent element.
                TagId.Rt or TagId.Rp => next is null || IsHtmlElement(next, TagId.Rt, TagId.Rp),

                // An optgroup element's end tag may be omitted if the optgroup element is immediately followed by another
                // optgroup element, if it is immediately followed by an hr element, or if there is no more content in the
                // parent element.
                TagId.Optgroup => next is null || IsHtmlElement(next, TagId.Optgroup, TagId.Hr),

                // An option element's end tag may be omitted if the option element is immediately followed by another option
                // element, if it is immediately followed by an optgroup element, if it is immediately followed by an hr
                // element, or if there is no more content in the parent element.
                TagId.Option => next is null || IsHtmlElement(next, TagId.Option, TagId.Optgroup, TagId.Hr),

                // A colgroup/caption element's end tag may be omitted if the element is not immediately followed by ASCII
                // whitespace or a comment.
                TagId.Colgroup or TagId.Caption => next is not Comment && !StartsWithAsciiWhitespace(next),

                // A thead element's end tag may be omitted if the thead element is immediately followed by a tbody or tfoot element.
                TagId.Thead => IsHtmlElement(next, TagId.Tbody, TagId.Tfoot),

                // A tbody element's end tag may be omitted if the tbody element is immediately followed by a tbody or tfoot
                // element, or if there is no more content in the parent element.
                TagId.Tbody => next is null || IsHtmlElement(next, TagId.Tbody, TagId.Tfoot),

                // A tfoot element's end tag may be omitted if there is no more content in the parent element.
                TagId.Tfoot => next is null,

                // A tr element's end tag may be omitted if the tr element is immediately followed by another tr element, or
                // if there is no more content in the parent element.
                TagId.Tr => next is null || IsHtmlElement(next, TagId.Tr),

                // A td/th element's end tag may be omitted if the element is immediately followed by a td or th element, or
                // if there is no more content in the parent element.
                TagId.Td or TagId.Th => next is null || IsHtmlElement(next, TagId.Td, TagId.Th),

                _ => false,
            };
        }

        /// <summary>
        /// A p element's end tag may be omitted if the p element is immediately followed by an address, article, aside,
        /// blockquote, details, dialog, div, dl, fieldset, figcaption, figure, footer, form, h1, h2, h3, h4, h5, h6, header,
        /// hgroup, hr, main, menu, nav, ol, p, pre, search, section, table, or ul element, or if there is no more content in
        /// the parent element and the parent element is an HTML element that is not an a, audio, del, ins, map, noscript, or
        /// video element, or an autonomous custom element.
        /// </summary>
        private static bool CanOmitParagraphEndTag(Element element, Node? next)
        {
            if (next is not null)
            {
                return next is Element { IsHtmlNamespace: true } nextElement && nextElement.TagId is
                    TagId.Address or TagId.Article or TagId.Aside or TagId.Blockquote or TagId.Details or TagId.Dialog or TagId.Div or
                    TagId.Dl or TagId.Fieldset or TagId.Figcaption or TagId.Figure or TagId.Footer or TagId.Form or TagId.H1 or TagId.H2 or
                    TagId.H3 or TagId.H4 or TagId.H5 or TagId.H6 or TagId.Header or TagId.Hgroup or TagId.Hr or TagId.Main or TagId.Menu or
                    TagId.Nav or TagId.Ol or TagId.P or TagId.Pre or TagId.Search or TagId.Section or TagId.Table or TagId.Ul;
            }

            // The parent of the content of a template is the template
            var parent = element.ParentNode switch
            {
                Element parentElement => parentElement,
                DocumentFragment { Host: HtmlTemplateElement template } fragment when ReferenceEquals(template.TemplateContents, fragment) => template,
                _ => null,
            };

            return parent is { IsHtmlNamespace: true } &&
                parent.TagId is not (TagId.A or TagId.Audio or TagId.Del or TagId.Ins or TagId.Map or TagId.Noscript or TagId.Video) &&
                !(parent.IsValue is null && CustomElements.IsValidCustomElementName(parent.LocalName));
        }
    }
}
