using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.Infra;

namespace Meziantou.Framework.WebPlatform.Formatting;

/// <summary>
/// Writes an indented HTML markup, to read or compare documents: each node is written on its own line, indented by its
/// depth, and the whitespace of the text is collapsed and trimmed.
/// </summary>
/// <remarks>
/// The whitespace is not significant in the output, so the document it represents may be rendered differently: use
/// <see cref="HtmlFormatter.Default"/> or <see cref="HtmlMinifyFormatter"/> to write a document that is served. The content of
/// the elements whose whitespace is significant (<c>pre</c>, <c>textarea</c>, <c>listing</c>, <c>script</c>,
/// <c>style</c>...) is written as is, except the CSS of the <c>style</c> elements when
/// <see cref="HtmlFormatter.StyleElementFormatter"/> is set: it is written on the lines that follow the start tag, indented
/// by the depth of its content. An element whose only content is a text is written on a single line.
/// </remarks>
public sealed class HtmlPrettyFormatter : HtmlFormatter
{
    /// <summary>The string written for each level of indentation. The default is two spaces.</summary>
    public string Indentation
    {
        get;
        init
        {
            ArgumentNullException.ThrowIfNull(value);
            field = value;
        }
    } = "  ";

    /// <summary>
    /// The maximum number of indentation levels: the content of the deeper elements is indented as the content at this depth.
    /// The default is 64, which bounds the size of the output of deeply nested input (the indentation of each line would
    /// grow with the depth).
    /// </summary>
    public int MaxIndentationDepth
    {
        get;
        init
        {
            ArgumentOutOfRangeException.ThrowIfNegative(value);
            field = value;
        }
    } = 64;

    /// <summary>The line separator. The default is <c>"\n"</c>.</summary>
    public string NewLine
    {
        get;
        init
        {
            ArgumentNullException.ThrowIfNull(value);
            field = value;
        }
    } = "\n";

    /// <summary>Creates a writer that emits indented HTML to the given output.</summary>
    protected override HtmlMarkupWriter CreateWriter(TextWriter output) => new PrettyWriter(this, output);

    private sealed class PrettyWriter(HtmlPrettyFormatter formatter, TextWriter output) : HtmlMarkupWriter(output)
    {
        private readonly HtmlPrettyFormatter _formatter = formatter;

        /// <summary>For each open element, whether its content is written on the line of its start tag.</summary>
        private readonly Stack<bool> _singleLineElements = new();

        /// <summary>The number of open elements in a whitespace-sensitive element (0 when the content is formatted).</summary>
        private int _verbatimDepth;

        private bool _isFirstLine = true;

        private bool IsVerbatim => _verbatimDepth > 0;

        private bool IsInSingleLineElement => _singleLineElements.Count > 0 && _singleLineElements.Peek();

        protected override bool ShouldWrite(Node node)
        {
            // The whitespace between the nodes is replaced by the line breaks and the indentation
            return IsVerbatim || node is not Text text || !InfraString.IsAsciiWhitespaceOnly(text.Data);
        }

        protected override void WriteStartTag(Element element)
        {
            if (IsVerbatim)
            {
                base.WriteStartTag(element);
                if (!IsVoidElement(element))
                {
                    _verbatimDepth++;
                }

                return;
            }

            StartLine();
            base.WriteStartTag(element);
            if (IsVoidElement(element))
                return;

            if (IsWhitespaceSensitive(element))
            {
                _verbatimDepth = 1;
            }
            else
            {
                _singleLineElements.Push(HasSingleLineContent(element));
            }
        }

        protected override void WriteEndTag(Element element)
        {
            if (IsVerbatim)
            {
                _verbatimDepth--;
                base.WriteEndTag(element);
                return;
            }

            if (!_singleLineElements.Pop())
            {
                StartLine();
            }

            base.WriteEndTag(element);
        }

        protected override void WriteText(Text text)
        {
            if (IsVerbatim)
            {
                base.WriteText(text);
                return;
            }

            if (!IsInSingleLineElement)
            {
                StartLine();
            }

            WriteEscaped(Output, InfraString.StripAndCollapseAsciiWhitespace(text.Data), attributeMode: false);
        }

        protected override void WriteRawText(Text text)
        {
            if (GetFormattedStyleSheet(text) is not { } css)
            {
                base.WriteRawText(text);
                return;
            }

            // The style sheet is written in place of the first text node, each line indented by the depth of the content
            if (text.PreviousSibling is not null || css.Length == 0)
                return;

            foreach (var line in css.ReplaceLineEndings("\n").Split('\n'))
            {
                WriteNewLine(Depth);
                Output.Write(line);
            }

            WriteNewLine(Depth - 1);
        }

        protected override void WriteComment(Comment comment)
        {
            StartLineIfFormatted();
            base.WriteComment(comment);
        }

        protected override void WriteDocumentType(DocumentType documentType)
        {
            StartLineIfFormatted();
            base.WriteDocumentType(documentType);
        }

        protected override void WriteProcessingInstruction(ProcessingInstruction processingInstruction)
        {
            StartLineIfFormatted();
            base.WriteProcessingInstruction(processingInstruction);
        }

        private void StartLineIfFormatted()
        {
            if (!IsVerbatim)
            {
                StartLine();
            }
        }

        private void StartLine()
        {
            if (_isFirstLine)
            {
                _isFirstLine = false;
                WriteIndentation(Depth);
            }
            else
            {
                WriteNewLine(Depth);
            }
        }

        private void WriteNewLine(int depth)
        {
            Output.Write(_formatter.NewLine);
            WriteIndentation(depth);
        }

        private void WriteIndentation(int depth)
        {
            if (_formatter.Indentation.Length > 0)
            {
                for (var i = Math.Min(depth, _formatter.MaxIndentationDepth); i > 0; i--)
                {
                    Output.Write(_formatter.Indentation);
                }
            }
        }

        /// <summary>Whether the content of the element is empty or a single text (ignoring the whitespace-only text).</summary>
        private static bool HasSingleLineContent(Element element)
        {
            var parent = element is HtmlTemplateElement template ? template.TemplateContents : (Node)element;
            var hasText = false;
            for (var child = parent.FirstChild; child is not null; child = child.NextSibling)
            {
                if (child is Text text)
                {
                    if (InfraString.IsAsciiWhitespaceOnly(text.Data))
                        continue;

                    if (hasText)
                        return false;

                    hasText = true;
                    continue;
                }

                return false;
            }

            return true;
        }

        /// <summary>The elements whose whitespace is significant: their content is written as is.</summary>
        private static bool IsWhitespaceSensitive(Element element)
        {
            if (element.IsHtmlNamespace)
                return element.TagId is TagId.Pre or TagId.Textarea or TagId.Listing || HasRawTextContent(element);

            // The style sheets and the scripts of the SVG and MathML elements
            return element.LocalName is "style" or "script";
        }
    }
}
