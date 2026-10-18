using System.Text;
using System.Xml;
using Meziantou.Framework.WebPlatform.Dom;

namespace Meziantou.Framework.WebPlatform.Xml;

/// <summary>
/// Builds a DOM tree from an XML document (https://html.spec.whatwg.org/multipage/xhtml.html#parsing-xhtml-documents),
/// using <see cref="XmlReader"/> for the XML 1.0 and Namespaces in XML well-formedness rules.
/// </summary>
internal sealed class XmlTreeBuilder
{
    private readonly Document _document;
    private readonly StringBuilder _text = new();
    private readonly Stack<Node> _openNodes = new();
    private readonly bool _fragment;
    private Node _current;

    private XmlTreeBuilder(Document document, Node root, bool fragment)
    {
        _document = document;
        _current = root;
        _fragment = fragment;
    }

    private static XmlReaderSettings CreateSettings(string input) => new()
    {
        DtdProcessing = DtdProcessing.Parse,
        XmlResolver = new XhtmlEntityResolver(input),

        // Protection against entity expansion attacks ("billion laughs")
        MaxCharactersFromEntities = 10_000_000,
        IgnoreComments = false,
        IgnoreProcessingInstructions = false,
        IgnoreWhitespace = false,
        CheckCharacters = true,
        ValidationType = ValidationType.None,
        ConformanceLevel = ConformanceLevel.Document,
    };

    /// <summary>Parses <paramref name="input"/> into <paramref name="root"/>. Returns the well-formedness error, if any.</summary>
    /// <param name="fragment">Whether the parser is created by the XML fragment parsing algorithm: the scripts never run.</param>
    public static XmlException? Parse(Document document, Node root, string input, bool fragment = false)
    {
        var builder = new XmlTreeBuilder(document, root, fragment);
        try
        {
            using var reader = CreateReader(input);
            builder.Run(reader);
            return null;
        }
        catch (XmlException ex)
        {
            return ex;
        }
    }

    /// <summary>Creates the reader of an XML document. Lone surrogates cannot be encoded: they are replaced with U+FFFD, as when the string is converted to bytes.</summary>
    private static XmlReader CreateReader(string input)
    {
        input = Infra.InfraString.ToScalarValueString(input);
        return XmlReader.Create(new StringReader(input), CreateSettings(input));
    }

    /// <summary>Creates a tree builder whose parsing can pause at a parser-blocking script (see <see cref="Html.Parsing.XmlScriptingParser"/>).</summary>
    public static XmlTreeBuilder CreateResumable(Document document, string input) => new(document, document, fragment: false) { _reader = CreateReader(input) };

    private XmlReader? _reader;
    private bool _pausedForScript;

    /// <summary>
    /// Runs the tree construction of a builder created by <see cref="CreateResumable"/> until the end of the input, or until a
    /// parser-blocking script is pending. Returns true when the input is consumed.
    /// </summary>
    /// <exception cref="XmlException">The document is not well-formed.</exception>
    public bool RunUntilPause()
    {
        var reader = _reader!;
        try
        {
            if (Run(reader))
            {
                reader.Dispose();
                return true;
            }

            return false;
        }
        catch (XmlException)
        {
            reader.Dispose();
            throw;
        }
    }

    /// <summary>Returns true when the input is consumed, false when the parser pauses for a parser-blocking script.</summary>
    private bool Run(XmlReader reader)
    {
        while (reader.Read())
        {
            switch (reader.NodeType)
            {
                case XmlNodeType.Element:
                    FlushText();
                    OnElement(reader);
                    break;

                case XmlNodeType.EndElement:
                    FlushText();
                    OnEndElement();
                    break;

                case XmlNodeType.Text or XmlNodeType.Whitespace or XmlNodeType.SignificantWhitespace:
                    // Text outside of the document element (only whitespace is allowed there) is not represented
                    if (_current is not Document)
                    {
                        _text.Append(reader.Value);
                    }

                    break;

                case XmlNodeType.CDATA:
                    FlushText();
                    Append(new CDataSection(_document, reader.Value));
                    break;

                case XmlNodeType.Comment:
                    FlushText();
                    Append(new Comment(_document, reader.Value));
                    break;

                case XmlNodeType.ProcessingInstruction:
                    FlushText();
                    Append(new ProcessingInstruction(_document, reader.Name, reader.Value, validate: false) { CreatedByParser = !_fragment });
                    break;

                case XmlNodeType.DocumentType:
                    FlushText();
                    Append(new DocumentType(_document, reader.Name, reader.GetAttribute("PUBLIC") ?? "", reader.GetAttribute("SYSTEM") ?? ""));
                    break;
            }

            if (_pausedForScript)
            {
                _pausedForScript = false;
                return false;
            }
        }

        FlushText();
        return true;
    }

    private void OnElement(XmlReader reader)
    {
        var ns = Namespaces.Intern(reader.NamespaceURI.Length == 0 ? null : reader.NamespaceURI);
        var prefix = reader.Prefix.Length == 0 ? null : reader.Prefix;
        var localName = reader.LocalName;
        var isEmpty = reader.IsEmptyElement;

        // https://html.spec.whatwg.org/multipage/parsing.html#create-an-element-for-the-token
        string? isValue = null;
        var attributes = new List<(string LocalName, string? Namespace, string? Prefix, string Value)>();
        if (reader.MoveToFirstAttribute())
        {
            do
            {
                var attributeNamespace = Namespaces.Intern(reader.NamespaceURI.Length == 0 ? null : reader.NamespaceURI);
                var attributePrefix = reader.Prefix.Length == 0 ? null : reader.Prefix;
                attributes.Add((reader.LocalName, attributeNamespace, attributePrefix, reader.Value));
                if (attributeNamespace is null && reader.LocalName == "is")
                {
                    isValue = reader.Value;
                }
            }
            while (reader.MoveToNextAttribute());

            reader.MoveToElement();
        }

        // Custom element constructors run synchronously, except for fragments
        var registry = CustomElementRegistry.LookUp(_current);
        var willExecuteScript = !_fragment && CustomElementRegistry.LookUpDefinition(registry, ns, localName, isValue) is not null;
        var scope = default(Html.CEReactionsScope);
        if (willExecuteScript)
        {
            _current.NodeDocument.EventLoop?.PerformMicrotaskCheckpointBeforeCustomElementConstruction();
            scope = Html.CEReactionsScope.EnterAndPush();
        }

        Element element;
        try
        {
            element = Html.ElementFactory.CreateElement(_document, localName, ns, prefix, isValue, willExecuteScript, registry);
            element.EnsureAttributeCapacity(attributes.Count);
            foreach (var (attributeLocalName, attributeNamespace, attributePrefix, value) in attributes)
            {
                element.AppendAttribute(attributeLocalName, attributeNamespace, attributePrefix, value);
            }
        }
        finally
        {
            scope.Dispose();
        }

        // https://html.spec.whatwg.org/multipage/xhtml.html#parsing-xhtml-documents: the script elements are parser-inserted
        // and not force-async. The scripts of a fragment never run.
        if (element is HtmlScriptElement script)
        {
            script.ParserDocument = _document;
            script.ForceAsync = false;
            script.AlreadyStarted = _fragment;
        }
        else if (element is SvgScriptElement svgScript)
        {
            svgScript.IsParserInserted = true;
            if (_fragment)
            {
                svgScript.MarkAsAlreadyProcessed();
            }
        }
        else if (element is HtmlLinkElement link)
        {
            // https://html.spec.whatwg.org/multipage/semantics.html#contributes-a-script-blocking-style-sheet: the style sheets of
            // the style and link elements created by the parser of a document block its scripts
            link.CreatedByParser = !_fragment;
        }
        else if (element is HtmlStyleElement style)
        {
            style.CreatedByParser = !_fragment;
        }

        // The style block of a style element is updated when it is popped (see Element.OnStyleElementStackOfOpenElementsChanged)
        if (element is HtmlStyleElement or SvgStyleElement)
        {
            element.OnStyleElementStackOfOpenElementsChanged(onStack: true);
        }

        // https://html.spec.whatwg.org/multipage/media.html#blocked-on-parser
        (element as HtmlMediaElement)?.OnCreatedByParser();
        Append(element);
        if (isEmpty)
        {
            OnElementPopped(element);
        }
        else
        {
            if (element is HtmlObjectElement obj)
            {
                obj.IsOnStackOfOpenElements = true;
            }

            _openNodes.Push(_current);
            _current = element;
        }
    }

    private void OnEndElement()
    {
        // The stack of open elements is used rather than the parent of the element, which a script may have removed
        var element = (Element)_current;
        _current = _openNodes.Pop();
        OnElementPopped(element);
    }

    private void OnElementPopped(Element element)
    {
        // https://html.spec.whatwg.org/multipage/iframe-embed-object.html#the-object-element: what an object element represents
        // is determined when it is popped off the stack of open elements
        if (element is HtmlObjectElement obj)
        {
            obj.OnPoppedOffStackOfOpenElements();
        }
        else if (element is HtmlMediaElement media)
        {
            media.OnPoppedOffStackOfOpenElements();
        }
        else if (element is HtmlStyleElement or SvgStyleElement)
        {
            element.OnStyleElementStackOfOpenElementsChanged(onStack: false);
        }

        // https://html.spec.whatwg.org/multipage/xhtml.html#parsing-xhtml-documents: when the end tag of a script element is
        // parsed, a microtask checkpoint is performed and the script is prepared. It runs when the browsing context of the
        // document has a script engine.
        if (element.LocalName == "script" && (element.IsHtmlNamespace || ReferenceEquals(element.NamespaceUri, Namespaces.Svg)))
        {
            if (!_fragment && _document.BrowsingContext is { Options.ScriptEngine: not null } browsingContext && !browsingContext.EventLoop.IsScriptRunning)
            {
                browsingContext.EventLoop.PerformMicrotaskCheckpoint();
            }

            if (element is HtmlScriptElement script)
            {
                // https://w3c.github.io/trusted-types/dist/spec/#setting-slot-values-from-parser
                script.ScriptText = script.GetChildTextContent();
                script.Prepare();

                // The parser pauses until the pending parsing-blocking script is executed (see XmlScriptingParser)
                _pausedForScript = _document.PendingParsingBlockingScript is not null;
            }
            else if (element is SvgScriptElement svgScript)
            {
                // https://w3c.github.io/trusted-types/dist/spec/#setting-slot-values-from-parser
                svgScript.ScriptText = svgScript.GetChildTextContent();
                if (!_fragment)
                {
                    svgScript.Process();
                }
            }
        }
    }

    /// <summary>Appends a node to the current node, or to the template contents when the current node is a template.</summary>
    private void Append(Node node)
    {
        var parent = _current is HtmlTemplateElement template ? template.Content : _current;
        parent.AppendNode(node);
    }

    private void FlushText()
    {
        if (_text.Length == 0)
            return;

        Append(new Text(_document, _text.ToString()));
        _text.Clear();
    }
}
