using System.Net;
using System.Text;
using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.Infra;
using Meziantou.Framework.WebPlatform.Xml;

namespace Meziantou.Framework.WebPlatform.Xslt;

/// <summary>The output methods (https://www.w3.org/TR/xslt-10/#output).</summary>
internal enum XsltOutputMethod
{
    Xml,
    Html,
    Text,
}

/// <summary>
/// Converts a result tree (built by <see cref="XsltExecution"/>) to the DOM or to markup, according to its output method, as
/// the browsers do: with the html output method, the elements in no namespace are HTML elements; with the text output method,
/// the result is the text of the tree.
/// </summary>
internal static class XsltOutput
{
    /// <summary>The namespace of the element that wraps a result tree that is not a well-formed document (as in Firefox).</summary>
    private const string TransformiixNamespace = "http://www.mozilla.org/TransforMiix";

    /// <summary>
    /// The output method of a result tree: the method of xsl:output, or html when the first element of the tree is an
    /// <c>html</c> element in no namespace preceded only by whitespace (https://www.w3.org/TR/xslt-10/#output), else xml.
    /// </summary>
    public static XsltOutputMethod GetMethod(XsltOutputSettings settings, DocumentFragment result)
    {
        switch (settings.Method)
        {
            case "html":
                return XsltOutputMethod.Html;
            case "text":
                return XsltOutputMethod.Text;
            case not null:
                return XsltOutputMethod.Xml;
        }

        for (var child = result.FirstChild; child is not null; child = child.NextSibling)
        {
            switch (child)
            {
                case Element element:
                    return element.NamespaceUri is null && InfraAscii.EqualsIgnoreAsciiCase(element.LocalName, "html") ? XsltOutputMethod.Html : XsltOutputMethod.Xml;
                case Text text when !IsWhitespace(text.Data):
                    return XsltOutputMethod.Xml;
            }
        }

        return XsltOutputMethod.Xml;
    }

    /// <summary>
    /// The result of transformToFragment(): the nodes of the result tree created in the output document. Without an output
    /// method, the elements of an HTML output document are created as HTML elements (as in Firefox).
    /// </summary>
    public static DocumentFragment ToFragment(XsltOutputSettings settings, DocumentFragment result, Document output)
    {
        var method = settings.Method is null ? (output.IsHtmlDocument ? XsltOutputMethod.Html : XsltOutputMethod.Xml) : GetMethod(settings, result);
        var fragment = output.CreateDocumentFragment();
        if (method == XsltOutputMethod.Text)
        {
            var text = result.TextContent ?? "";
            if (text.Length > 0)
            {
                fragment.AppendChild(output.CreateTextNode(text));
            }

            return fragment;
        }

        CopyChildren(result, fragment, output, html: method == XsltOutputMethod.Html);
        return fragment;
    }

    /// <summary>
    /// The result of transformToDocument(): a new document that has no browsing context, whose scripts never run (they are
    /// marked as already started, https://html.spec.whatwg.org/multipage/scripting.html#scriptTagXSLT).
    /// </summary>
    public static Document ToDocument(XsltOutputSettings settings, DocumentFragment result, Document? sourceDocument)
    {
        Document document;
        switch (GetMethod(settings, result))
        {
            case XsltOutputMethod.Html:
                document = new HtmlDocumentParser(new HtmlParserOptions { ScriptingEnabled = false, AllowDeclarativeShadowRoots = false }).Parse(Serialize(settings, result, XsltOutputMethod.Html));
                break;

            case XsltOutputMethod.Text:
                document = new HtmlDocumentParser(new HtmlParserOptions { ScriptingEnabled = false, AllowDeclarativeShadowRoots = false }).Parse("<pre>\n" + WebUtility.HtmlEncode(result.TextContent ?? ""));
                break;

            default:
                document = new Document(isHtmlDocument: false, settings.MediaType ?? "application/xml");
                var root = NeedsWrapper(result) ? CreateWrapper(document) : (Node)document;
                if (ReferenceEquals(root, document) && (settings.DoctypeSystem is not null || settings.DoctypePublic is not null) && FindFirstElement(result) is { } firstElement)
                {
                    document.AppendChild(new DocumentType(document, firstElement.TagName, settings.DoctypePublic ?? "", settings.DoctypeSystem ?? ""));
                }

                CopyChildren(result, root, document, html: false);
                break;
        }

        MarkScriptsAlreadyStarted(document);
        if (sourceDocument is not null)
        {
            document.UrlRecord = sourceDocument.UrlRecord;
            document.OriginDocument = Document.GetOriginDocument(sourceDocument);
        }

        return document;
    }

    /// <summary>
    /// Serializes a result tree (https://www.w3.org/TR/xslt-10/#output), as the result of the transformation of a document
    /// that is loaded in a browsing context: the browsers parse the serialization as a new document.
    /// </summary>
    public static string Serialize(XsltOutputSettings settings, DocumentFragment result, XsltOutputMethod method)
    {
        switch (method)
        {
            case XsltOutputMethod.Text:
                return result.TextContent ?? "";

            case XsltOutputMethod.Html:
            {
                var htmlDocument = new Document(isHtmlDocument: true, "text/html");
                var fragment = htmlDocument.CreateDocumentFragment();
                CopyChildren(result, fragment, htmlDocument, html: true);
                var builder = new StringBuilder();
                if (settings.DoctypePublic is not null || settings.DoctypeSystem is not null)
                {
                    builder.Append("<!DOCTYPE html");
                    if (settings.DoctypePublic is { } publicId)
                    {
                        builder.Append(" PUBLIC \"").Append(publicId).Append('"');
                        if (settings.DoctypeSystem is { } systemId)
                        {
                            builder.Append(" \"").Append(systemId).Append('"');
                        }
                    }
                    else
                    {
                        builder.Append(" SYSTEM \"").Append(settings.DoctypeSystem).Append('"');
                    }

                    builder.Append(">\n");
                }

                builder.Append(Html.HtmlSerializer.SerializeChildren(fragment));
                return builder.ToString();
            }

            default:
            {
                if (NeedsWrapper(result))
                {
                    var wrapperDocument = new Document(isHtmlDocument: false, "application/xml");
                    var wrapper = CreateWrapper(wrapperDocument);
                    CopyChildren(result, wrapper, wrapperDocument, html: false);
                    return XmlSerialization.Serialize(wrapper, requireWellFormed: false);
                }

                var builder = new StringBuilder();
                if (settings.DoctypeSystem is not null && FindFirstElement(result) is { } element)
                {
                    builder.Append("<!DOCTYPE ").Append(element.TagName);
                    if (settings.DoctypePublic is { } publicId)
                    {
                        builder.Append(" PUBLIC \"").Append(publicId).Append("\" \"");
                    }
                    else
                    {
                        builder.Append(" SYSTEM \"");
                    }

                    builder.Append(settings.DoctypeSystem).Append("\">\n");
                }

                builder.Append(XmlSerialization.SerializeChildren(result, requireWellFormed: false));
                return builder.ToString();
            }
        }
    }

    private static Element? FindFirstElement(Node node)
    {
        for (var child = node.FirstChild; child is not null; child = child.NextSibling)
        {
            if (child is Element element)
                return element;
        }

        return null;
    }

    /// <summary>Whether a result tree is not a well-formed document: it has no element, several elements, or text that is not whitespace.</summary>
    private static bool NeedsWrapper(DocumentFragment result)
    {
        var elements = 0;
        for (var child = result.FirstChild; child is not null; child = child.NextSibling)
        {
            if (child is Element)
            {
                elements++;
            }
            else if (child is Text text && !IsWhitespace(text.Data))
            {
                return true;
            }
        }

        return elements != 1;
    }

    private static Element CreateWrapper(Document document)
    {
        var wrapper = Html.ElementFactory.CreateElement(document, "result", TransformiixNamespace, "transformiix", isValue: null, synchronousCustomElements: false);
        document.AppendChild(wrapper);
        return wrapper;
    }

    /// <summary>
    /// Copies the children of a node of a result tree into a node of another document, without recursion. With
    /// <paramref name="html"/>, the elements in no namespace are HTML elements, whose names (and the names of their
    /// attributes) are lowercased, and the namespace declarations are dropped.
    /// </summary>
    private static void CopyChildren(Node source, Node target, Document document, bool html)
    {
        var node = source.FirstChild;
        var parent = target;
        while (node is not null)
        {
            Node? copy = null;
            switch (node)
            {
                case Element element:
                    copy = CopyElement(element, document, html);
                    break;
                case Text text:
                    // The whitespace text nodes are not allowed as children of a document
                    if (parent is not Document || !IsWhitespace(text.Data))
                    {
                        copy = new Text(document, text.Data);
                    }

                    break;
                case Comment comment:
                    copy = new Comment(document, comment.Data);
                    break;
                case ProcessingInstruction instruction:
                    copy = new ProcessingInstruction(document, instruction.Target, instruction.Data, validate: false);
                    break;
            }

            if (copy is not null)
            {
                parent.AppendChild(copy);
            }

            // The children of an element are copied before its next sibling
            if (copy is Element && node.FirstChild is { } firstChild)
            {
                parent = copy;
                node = firstChild;
                continue;
            }

            while (node is not null && node.NextSibling is null)
            {
                node = node.ParentNode;
                if (node is null || ReferenceEquals(node, source))
                    return;

                parent = parent.ParentNode!;
            }

            node = node?.NextSibling;
        }
    }

    private static Element CopyElement(Element element, Document document, bool html)
    {
        var ns = element.NamespaceUri;
        var prefix = element.Prefix;
        var localName = element.LocalName;
        var isHtml = false;
        if (html && ns is null)
        {
            ns = Namespaces.Html;
            prefix = null;
            localName = InfraAscii.ToAsciiLowercase(localName);
            isHtml = true;
        }

        Element copy;
        try
        {
            copy = document.CreateElementNS(ns, prefix is null ? localName : prefix + ":" + localName);
        }
        catch (DomException)
        {
            // A name that the DOM does not allow (the XML names are stricter for most of the characters, but not for all)
            copy = Html.ElementFactory.CreateElement(document, localName, ns, prefix, isValue: null, synchronousCustomElements: false);
        }

        for (var i = 0; i < element.AttributeCount; i++)
        {
            var attributeNamespace = element.GetAttributeNamespace(i);
            var attributePrefix = element.GetAttributePrefix(i);
            var attributeLocalName = element.GetAttributeLocalName(i);
            if (html && (isHtml || ns == Namespaces.Html) && attributeNamespace == Namespaces.Xmlns)
                continue;

            if (isHtml && attributeNamespace is null)
            {
                attributeLocalName = InfraAscii.ToAsciiLowercase(attributeLocalName);
            }

            try
            {
                copy.SetAttributeNS(attributeNamespace, attributePrefix is null ? attributeLocalName : attributePrefix + ":" + attributeLocalName, element.GetAttributeValue(i));
            }
            catch (DomException)
            {
                // The attribute is ignored
            }
        }

        return copy;
    }

    private static void MarkScriptsAlreadyStarted(Document document)
    {
        for (var node = document.FirstChild; node is not null; node = node.GetNextInTreeOrder(document))
        {
            switch (node)
            {
                case HtmlScriptElement script:
                    script.AlreadyStarted = true;
                    break;
                case SvgScriptElement svgScript:
                    svgScript.Core.AlreadyStarted = true;
                    break;
            }
        }
    }

    private static bool IsWhitespace(string text)
    {
        foreach (var c in text)
        {
            if (c is not (' ' or '\t' or '\r' or '\n'))
                return false;
        }

        return true;
    }
}
