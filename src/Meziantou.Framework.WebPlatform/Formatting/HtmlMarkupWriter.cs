using Meziantou.Framework.WebPlatform.Css.Om;
using Meziantou.Framework.WebPlatform.Dom;

namespace Meziantou.Framework.WebPlatform.Formatting;

/// <summary>
/// Writes the markup of a node. The default implementation is the HTML fragment serialization algorithm
/// (https://html.spec.whatwg.org/multipage/parsing.html#serialising-html-fragments); a derived class customizes the output
/// by overriding the method of each kind of node.
/// </summary>
/// <remarks>
/// The tree is walked iteratively, so deep trees do not overflow the stack. An instance writes one node: create a new
/// writer for each call (see <see cref="HtmlFormatter.CreateWriter"/>).
/// </remarks>
public class HtmlMarkupWriter
{
    /// <summary>Creates a writer that emits HTML markup to the given output.</summary>
    public HtmlMarkupWriter(TextWriter output)
    {
        ArgumentNullException.ThrowIfNull(output);
        Output = output;
    }

    /// <summary>The writer the markup is written to.</summary>
    protected TextWriter Output { get; }

    /// <summary>The node passed to <see cref="Write"/>.</summary>
    protected Node? Root { get; private set; }

    /// <summary>
    /// The number of elements, between the root and the node being written, whose start tag is written: 0 for the root
    /// element and for the children of a root document; the children of an element are at the depth of the element plus 1.
    /// </summary>
    protected int Depth { get; private set; }

    /// <summary>
    /// The formatter of the CSS text of the <c>style</c> elements (see <see cref="WriteRawText"/>), or null to write it as
    /// is. <see cref="HtmlFormatter.Format(Node, TextWriter)"/> sets it to <see cref="HtmlFormatter.StyleElementFormatter"/>
    /// when it is null.
    /// </summary>
    public CssFormatter? StyleElementFormatter { get; set; }

    /// <summary>
    /// The formatter of the value of the <c>style</c> attributes (see <see cref="WriteAttributes"/>), or null to write it as
    /// is. <see cref="HtmlFormatter.Format(Node, TextWriter)"/> sets it to <see cref="HtmlFormatter.StyleAttributeFormatter"/>
    /// when it is null.
    /// </summary>
    public CssFormatter? StyleAttributeFormatter { get; set; }

    /// <summary>The style element whose formatted text is in <see cref="_formattedStyleSheet"/>.</summary>
    private HtmlStyleElement? _formattedStyleElement;

    private string? _formattedStyleSheet;

    /// <summary>Whether the shadow roots whose serializable flag is set are serialized as declarative shadow roots.</summary>
    internal bool SerializableShadowRoots { get; init; }

    /// <summary>The shadow roots that are serialized as declarative shadow roots.</summary>
    internal IReadOnlyCollection<ShadowRoot>? ShadowRoots { get; init; }

    /// <summary>
    /// Writes <paramref name="node"/>: an element with its start tag, its descendants and its end tag (as the outerHTML
    /// getter), the children of a document or a document fragment.
    /// </summary>
    public virtual void Write(Node node)
    {
        ArgumentNullException.ThrowIfNull(node);
        Root = node;
        List<Frame> frames = [new Frame(node, isSingleNode: true, endElement: null)];
        Run(frames);
    }

    /// <summary>Writes the children of <paramref name="node"/>, as the innerHTML getter.</summary>
    internal void WriteChildren(Node node)
    {
        Root = node;
        if (node is Element element && HtmlFormatter.IsVoidElement(element))
            return;

        var frames = new List<Frame>();
        PushChildren(node, frames, endElement: null);
        Run(frames);
    }

    /// <summary>Returns whether <paramref name="node"/> and its descendants are written. Returns true by default.</summary>
    protected virtual bool ShouldWrite(Node node) => true;

    /// <summary>Writes the start tag of the element: <c>&lt;</c>, the tag name, the attributes (<see cref="WriteAttributes"/>) and <c>&gt;</c>.</summary>
    protected virtual void WriteStartTag(Element element)
    {
        ArgumentNullException.ThrowIfNull(element);
        Output.Write('<');
        Output.Write(HtmlFormatter.GetSerializedTagName(element));
        WriteAttributes(element);
        Output.Write('>');
    }

    /// <summary>
    /// Calls <see cref="WriteAttribute"/> for each attribute of the element, preceded by the <c>is</c> value of the
    /// element when it has one but no <c>is</c> attribute. The value of the <c>style</c> attribute is formatted by
    /// <see cref="StyleAttributeFormatter"/> when it is set.
    /// </summary>
    protected void WriteAttributes(Element element)
    {
        ArgumentNullException.ThrowIfNull(element);
        if (element.IsValue is { } isValue && !element.HasAttributeWithLocalName("is"))
        {
            WriteAttribute(element, "is", isValue);
        }

        for (var i = 0; i < element.AttributeCount; i++)
        {
            var value = element.GetAttributeValue(i);
            if (StyleAttributeFormatter is { } formatter && element.GetAttributeNamespace(i) is null && element.GetAttributeLocalName(i) == "style")
            {
                var declarations = new List<CssDeclarationEntry>();
                CssStyleDeclaration.ParseDeclarationBlock(value, declarations, atRule: null);
                value = formatter.Format(new CssStyleProperties(parentRule: null, declarations));
            }

            WriteAttribute(element, HtmlFormatter.GetSerializedAttributeName(element, i), value);
        }
    }

    /// <summary>Writes an attribute of the element: a space, then <c>name="value"</c> with the value escaped.</summary>
    /// <param name="element">The element of the attribute.</param>
    /// <param name="serializedName">The name of the attribute, prefixed as defined by <see cref="HtmlFormatter.GetSerializedAttributeName(Attr)"/>.</param>
    /// <param name="value">The value of the attribute.</param>
    protected virtual void WriteAttribute(Element element, string serializedName, string value)
    {
        Output.Write(' ');
        Output.Write(serializedName);
        Output.Write("=\"");
        HtmlFormatter.WriteEscaped(Output, value, attributeMode: true);
        Output.Write('"');
    }

    /// <summary>Writes the end tag of the element. It is not called for the void elements (<see cref="HtmlFormatter.IsVoidElement"/>).</summary>
    protected virtual void WriteEndTag(Element element)
    {
        ArgumentNullException.ThrowIfNull(element);
        Output.Write("</");
        Output.Write(HtmlFormatter.GetSerializedTagName(element));
        Output.Write('>');
    }

    /// <summary>Writes a text node: with <see cref="WriteRawText"/> when its parent has raw text content (<see cref="HtmlFormatter.HasRawTextContent"/>), escaped otherwise.</summary>
    protected virtual void WriteText(Text text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (text.ParentNode is Element parent && HtmlFormatter.HasRawTextContent(parent))
        {
            WriteRawText(text);
        }
        else
        {
            HtmlFormatter.WriteEscaped(Output, text.Data, attributeMode: false);
        }
    }

    /// <summary>
    /// Writes a text node whose parent has raw text content (<see cref="HtmlFormatter.HasRawTextContent"/>): as is, or for a
    /// <c>style</c> element, the text of its style sheet formatted by <see cref="StyleElementFormatter"/> in place of its first
    /// text node (its other text nodes are not written).
    /// </summary>
    protected virtual void WriteRawText(Text text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (GetFormattedStyleSheet(text) is { } css)
        {
            if (text.PreviousSibling is null)
            {
                Output.Write(css);
            }

            return;
        }

        Output.Write(text.Data);
    }

    /// <summary>
    /// Returns the text of the style sheet of the style element that contains <paramref name="text"/>, formatted by
    /// <see cref="StyleElementFormatter"/>; null when the text is written as is: there is no formatter, the parent is not a
    /// CSS style element, it has children that are not text nodes, or the formatted text would end the element.
    /// </summary>
    internal string? GetFormattedStyleSheet(Text text)
    {
        if (StyleElementFormatter is not { } formatter || text.ParentNode is not HtmlStyleElement style)
            return null;

        if (style == _formattedStyleElement)
            return _formattedStyleSheet;

        string? result = null;
        if (style.IsCssStyleBlock() && HasOnlyTextChildren(style))
        {
            var css = formatter.Format(new CssStyleSheet(style.GetChildTextContent()));

            // The HTML parser ends the raw text at "</style": e.g. the string "\3c/style>" is serialized as "</style>"
            if (!css.Contains("</style", StringComparison.OrdinalIgnoreCase))
            {
                result = css;
            }
        }

        _formattedStyleElement = style;
        _formattedStyleSheet = result;
        return result;

        static bool HasOnlyTextChildren(Element element)
        {
            for (var child = element.FirstChild; child is not null; child = child.NextSibling)
            {
                if (child is not Text)
                    return false;
            }

            return true;
        }
    }

    /// <summary>Writes <c>&lt;!--data--&gt;</c>.</summary>
    protected virtual void WriteComment(Comment comment)
    {
        ArgumentNullException.ThrowIfNull(comment);
        Output.Write("<!--");
        Output.Write(comment.Data);
        Output.Write("-->");
    }

    /// <summary>Writes <c>&lt;!DOCTYPE name&gt;</c>: as defined by the serialization algorithm, the public and system identifiers are not written.</summary>
    protected virtual void WriteDocumentType(DocumentType documentType)
    {
        ArgumentNullException.ThrowIfNull(documentType);
        Output.Write("<!DOCTYPE ");
        Output.Write(documentType.Name);
        Output.Write('>');
    }

    /// <summary>Writes <c>&lt;?target data?&gt;</c>.</summary>
    protected virtual void WriteProcessingInstruction(ProcessingInstruction processingInstruction)
    {
        ArgumentNullException.ThrowIfNull(processingInstruction);
        Output.Write("<?");
        Output.Write(processingInstruction.Target);
        Output.Write(' ');
        Output.Write(processingInstruction.Data);
        Output.Write("?>");
    }

    // A struct in a list used as a stack (the frame of the top of the stack is updated in place): there is no allocation per element
    private struct Frame(Node? next, bool isSingleNode, Element? endElement)
    {
        public Node? Next = next;

        public readonly bool IsSingleNode = isSingleNode;

        /// <summary>The element whose end tag is written after the children.</summary>
        public readonly Element? EndElement = endElement;

        /// <summary>Whether the frame writes the children of a declarative shadow root, followed by its end tag.</summary>
        public bool IsShadowRoot;
    }

    /// <summary>
    /// Writes the declarative shadow root of <paramref name="node"/> (if serialized) and pushes the frames that write its
    /// children, followed by the end tag of <paramref name="endElement"/>.
    /// </summary>
    private void PushChildren(Node node, List<Frame> frames, Element? endElement)
    {
        var childrenParent = node is HtmlTemplateElement template ? template.TemplateContents : node;
        frames.Add(new Frame(childrenParent.FirstChild, isSingleNode: false, endElement));

        if (node is Element { AttachedShadowRoot: { } shadow } && ((SerializableShadowRoots && shadow.Serializable) || (ShadowRoots is not null && ShadowRoots.Contains(shadow))))
        {
            Output.Write("<template shadowrootmode=\"");
            Output.Write(shadow.Mode == ShadowRootMode.Open ? "open" : "closed");
            Output.Write('"');
            if (shadow.DelegatesFocus)
            {
                Output.Write(" shadowrootdelegatesfocus=\"\"");
            }

            if (shadow.Serializable)
            {
                Output.Write(" shadowrootserializable=\"\"");
            }

            if (shadow.SlotAssignment == SlotAssignmentMode.Manual)
            {
                Output.Write(" shadowrootslotassignment=\"manual\"");
            }

            if (shadow.Clonable)
            {
                Output.Write(" shadowrootclonable=\"\"");
            }

            var documentRegistry = shadow.NodeDocument.CustomElementRegistryStorage;
            var shadowRegistry = shadow.CustomElementRegistryStorage;
            if (!(documentRegistry is null && shadowRegistry is null) && !(CustomElementRegistry.IsGlobal(documentRegistry) && CustomElementRegistry.IsGlobal(shadowRegistry)))
            {
                Output.Write(" shadowrootcustomelementregistry=\"\"");
            }

            Output.Write('>');
            frames.Add(new Frame(shadow.FirstChild, isSingleNode: false, endElement: null) { IsShadowRoot = true });
            Depth++;
        }
    }

    private void Run(List<Frame> frames)
    {
        while (frames.Count > 0)
        {
            // The reference is not used after a frame is pushed (the list may be reallocated)
            ref var frame = ref System.Runtime.InteropServices.CollectionsMarshal.AsSpan(frames)[^1];
            var current = frame.Next;
            if (current is null)
            {
                var endElement = frame.EndElement;
                var isShadowRoot = frame.IsShadowRoot;
                frames.RemoveAt(frames.Count - 1);
                if (endElement is not null)
                {
                    Depth--;
                    WriteEndTag(endElement);
                }
                else if (isShadowRoot)
                {
                    Depth--;
                    Output.Write("</template>");
                }

                continue;
            }

            frame.Next = frame.IsSingleNode ? null : current.NextSibling;
            if (current is not (Document or DocumentFragment) && !ShouldWrite(current))
                continue;

            switch (current)
            {
                case Element element:
                    WriteStartTag(element);
                    if (HtmlFormatter.IsVoidElement(element))
                        break;

                    Depth++;
                    PushChildren(element, frames, element);
                    break;

                case Text text:
                    WriteText(text);
                    break;

                case Comment comment:
                    WriteComment(comment);
                    break;

                case ProcessingInstruction processingInstruction:
                    WriteProcessingInstruction(processingInstruction);
                    break;

                case DocumentType documentType:
                    WriteDocumentType(documentType);
                    break;

                case DocumentFragment or Document:
                    // Only used when writing the root node: a document or a document fragment has no markup of its own
                    PushChildren(current, frames, endElement: null);
                    break;
            }
        }
    }
}
