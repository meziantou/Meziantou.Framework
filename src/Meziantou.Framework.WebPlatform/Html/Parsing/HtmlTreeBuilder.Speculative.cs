using Meziantou.Framework.WebPlatform.Dom;

namespace Meziantou.Framework.WebPlatform.Html.Parsing;

// https://html.spec.whatwg.org/multipage/parsing.html#speculative-html-parsing
internal sealed partial class HtmlTreeBuilder
{
    /// <summary>Called when a speculative parser inserts an element (see <see cref="CreateSpeculativeParser"/>).</summary>
    private Action<Element>? _speculativeElementInserted;

    /// <summary>
    /// Creates a speculative HTML parser with the same state as this parser
    /// (https://html.spec.whatwg.org/multipage/parsing.html#start-the-speculative-html-parser): it parses the characters that
    /// are not consumed yet into <paramref name="speculativeDocument"/>, an isomorphic representation of the document whose
    /// elements are shallow copies (the speculative mock elements) of the elements of the stack of open elements, of the
    /// list of active formatting elements, of the head and form element pointers, and of their ancestors. The state of this
    /// parser and its document are not affected. <paramref name="elementInserted"/> is called when the speculative parser
    /// inserts an element (the element is not created by the parser when it is inserted in the contents of a template).
    /// </summary>
    public HtmlTreeBuilder CreateSpeculativeParser(Document speculativeDocument, Action<Element> elementInserted)
    {
        var input = _tokenizer.RemainingInput;
        var tokenizer = new HtmlTokenizer(input, errorHandler: null, new NameTable())
        {
            State = _tokenizer.State,
            LastStartTagName = _tokenizer.LastStartTagName,
            EofInserted = _tokenizer.EofInserted,
        };

        var parser = new HtmlTreeBuilder(speculativeDocument, tokenizer, errorHandler: null, ScriptingMode)
        {
            AllowDeclarativeShadowRoots = AllowDeclarativeShadowRoots,
            _speculativeElementInserted = elementInserted,
            _mode = _mode,
            _originalMode = _originalMode,
            _framesetOk = _framesetOk,
            _fosterParenting = _fosterParenting,
            _ignoreNextLineFeed = _ignoreNextLineFeed,
            _pendingTableCharactersContainNonWhitespace = _pendingTableCharactersContainNonWhitespace,
        };

        parser._pendingTableCharacters.Append(_pendingTableCharacters);
        parser._templateModes.AddRange(_templateModes);

        var copies = new Dictionary<Node, Node?>(ReferenceEqualityComparer.Instance) { [_document] = speculativeDocument };
        foreach (var element in _openElements)
        {
            parser.PushOpenElement((Element)Copy(element)!);
        }

        foreach (var entry in _activeFormatting)
        {
            parser._activeFormatting.Add(entry.IsMarker ? entry : entry with { Element = (Element)Copy(entry.Element!)! });
        }

        parser._headElement = _headElement is null ? null : (Element)Copy(_headElement)!;
        parser._formElement = _formElement is null ? null : (Element)Copy(_formElement)!;

        // The base element of the document is processed for the subsequent speculative fetches
        for (var node = _document.FirstChild; node is not null; node = node.GetNextInTreeOrder(_document))
        {
            if (node is Element element && element.IsHtml(TagId.Base) && element.HasAttribute("href"))
            {
                Copy(element);
                break;
            }
        }

        return parser;

        // Returns the copy of a node, which is inserted into the copy of its parent (or of the host of its shadow root, or of
        // the template whose contents it is)
        Node? Copy(Node node)
        {
            if (copies.TryGetValue(node, out var copy))
                return copy;

            switch (node)
            {
                case ShadowRoot shadowRoot:
                {
                    if (Copy(shadowRoot.Host) is not Element host)
                    {
                        copy = null;
                        break;
                    }

                    var shadowRootCopy = host.AttachShadowRoot(shadowRoot.Mode, shadowRoot.Clonable, shadowRoot.Serializable, shadowRoot.DelegatesFocus, shadowRoot.SlotAssignment, registry: null);
                    shadowRootCopy.Declarative = shadowRoot.Declarative;
                    copy = shadowRootCopy;
                    break;
                }

                case DocumentFragment { Host: HtmlTemplateElement template } fragment when ReferenceEquals(template.TemplateContents, fragment):
                    copy = (Copy(template) as HtmlTemplateElement)?.TemplateContents;
                    break;

                case Element element:
                {
                    // The elements of the contents of a template are created in the document of the contents
                    var parentCopy = element.ParentNode is { } parent ? Copy(parent) : null;
                    var elementCopy = ElementFactory.CreateElement(parentCopy?.NodeDocument ?? speculativeDocument, element.LocalName, element.NamespaceUri, element.Prefix, isValue: null, synchronousCustomElements: false);
                    for (var i = 0; i < element.AttributeCount; i++)
                    {
                        elementCopy.AppendAttribute(element.GetAttributeLocalName(i), element.GetAttributeNamespace(i), element.GetAttributePrefix(i), element.GetAttributeValue(i));
                    }

                    copies[element] = elementCopy;
                    parentCopy?.InsertBeforeCore(elementCopy, null);

                    // The template of a declarative shadow root is not inserted: its contents are the shadow root
                    if (element is HtmlTemplateElement { TemplateContents: ShadowRoot templateShadowRoot } && Copy(templateShadowRoot) is ShadowRoot shadowRootCopy)
                    {
                        ((HtmlTemplateElement)elementCopy).TemplateContents = shadowRootCopy;
                    }

                    return elementCopy;
                }

                default:
                    copy = null;
                    break;
            }

            copies[node] = copy;
            return copy;
        }
    }
}
