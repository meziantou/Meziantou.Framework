using System.Text;
using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.Infra;

namespace Meziantou.Framework.WebPlatform.Xml;

/// <summary>The XML serialization algorithm (https://w3c.github.io/DOM-Parsing/#dfn-xml-serialization).</summary>
internal sealed class XmlSerialization
{
    /// <summary>Thrown when the serialization would not be well-formed; converted to an "InvalidStateError" DOMException.</summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1032:Implement standard exception constructors", Justification = "Internal control-flow exception")]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1064:Exceptions should be public", Justification = "Internal control-flow exception")]
    private sealed class NotWellFormedException(string message) : Exception(message);

    private readonly bool _requireWellFormed;
    private readonly StringBuilder _output = new();
    private int _prefixIndex = 1;

    private XmlSerialization(bool requireWellFormed)
    {
        _requireWellFormed = requireWellFormed;
    }

    /// <summary>https://w3c.github.io/DOM-Parsing/#dfn-concept-serialize-xml</summary>
    public static string Serialize(Node node, bool requireWellFormed)
    {
        var serializer = new XmlSerialization(requireWellFormed);
        var prefixMap = new NamespacePrefixMap();
        prefixMap.Add(Namespaces.Xml, "xml");
        try
        {
            serializer.SerializeNode(node, null, prefixMap);
        }
        catch (NotWellFormedException ex)
        {
            throw DomException.InvalidState(ex.Message);
        }

        return serializer._output.ToString();
    }

    /// <summary>Serializes the children of a node (the fragment serializing algorithm for XML documents).</summary>
    public static string SerializeChildren(Node node, bool requireWellFormed)
    {
        var serializer = new XmlSerialization(requireWellFormed);
        var prefixMap = new NamespacePrefixMap();
        prefixMap.Add(Namespaces.Xml, "xml");
        try
        {
            var children = node is HtmlTemplateElement template ? template.Content : node;
            for (var child = children.FirstChild; child is not null; child = child.NextSibling)
            {
                serializer.SerializeNode(child, null, prefixMap);
            }
        }
        catch (NotWellFormedException ex)
        {
            throw DomException.InvalidState(ex.Message);
        }

        return serializer._output.ToString();
    }

    private static void Fail(string message) => throw new NotWellFormedException(message);

    private void SerializeNode(Node node, string? ns, NamespacePrefixMap prefixMap)
    {
        switch (node)
        {
            case Element element:
                SerializeElement(element, ns, prefixMap);
                break;

            case Document document:
                if (_requireWellFormed && document.DocumentElement is null)
                    Fail("The document has no document element");

                for (var child = document.FirstChild; child is not null; child = child.NextSibling)
                {
                    SerializeNode(child, ns, prefixMap);
                }

                break;

            case Comment comment:
                if (_requireWellFormed && (!IsXmlChars(comment.Data) || comment.Data.Contains("--", StringComparison.Ordinal) || comment.Data.EndsWith('-', StringComparison.Ordinal)))
                    Fail("The comment is not well-formed");

                _output.Append("<!--").Append(comment.Data).Append("-->");
                break;

            case CDataSection cdata:
                _output.Append("<![CDATA[").Append(cdata.Data).Append("]]>");
                break;

            case Text text:
                if (_requireWellFormed && !IsXmlChars(text.Data))
                    Fail("The text contains characters that are not allowed in XML");

                AppendEscapedText(text.Data);
                break;

            case DocumentFragment fragment:
                for (var child = fragment.FirstChild; child is not null; child = child.NextSibling)
                {
                    SerializeNode(child, ns, prefixMap);
                }

                break;

            case DocumentType doctype:
                SerializeDocumentType(doctype);
                break;

            case ProcessingInstruction pi:
                if (_requireWellFormed && (pi.Target.Contains(':', StringComparison.Ordinal) || InfraAscii.EqualsIgnoreAsciiCase(pi.Target, "xml")))
                    Fail("The processing instruction target is not well-formed");

                if (_requireWellFormed && (!IsXmlChars(pi.Data) || pi.Data.Contains("?>", StringComparison.Ordinal)))
                    Fail("The processing instruction data is not well-formed");

                _output.Append("<?").Append(pi.Target).Append(' ').Append(pi.Data).Append("?>");
                break;

            case Attr:
                break;

            default:
                throw new TypeErrorException("Only Nodes and Attrs can be serialized");
        }
    }

    private void AppendEscapedText(string data)
    {
        foreach (var c in data)
        {
            switch (c)
            {
                case '&':
                    _output.Append("&amp;");
                    break;
                case '<':
                    _output.Append("&lt;");
                    break;
                case '>':
                    _output.Append("&gt;");
                    break;
                default:
                    _output.Append(c);
                    break;
            }
        }
    }

    /// <summary>https://w3c.github.io/DOM-Parsing/#dfn-xml-serializing-a-documenttype-node</summary>
    private void SerializeDocumentType(DocumentType doctype)
    {
        if (_requireWellFormed && !doctype.PublicId.All(IsPubidChar))
            Fail("The public identifier is not well-formed");

        if (_requireWellFormed && (!IsXmlChars(doctype.SystemId) || (doctype.SystemId.Contains('"', StringComparison.Ordinal) && doctype.SystemId.Contains('\'', StringComparison.Ordinal))))
            Fail("The system identifier is not well-formed");

        _output.Append("<!DOCTYPE ").Append(doctype.Name);
        if (doctype.PublicId.Length > 0)
        {
            _output.Append(" PUBLIC ");
            AppendId(doctype.PublicId);
        }

        if (doctype.SystemId.Length > 0)
        {
            if (doctype.PublicId.Length == 0)
            {
                _output.Append(" SYSTEM");
            }

            _output.Append(' ');
            AppendId(doctype.SystemId);
        }

        _output.Append('>');

        // Browsers always use QUOTATION MARK, even when the identifier contains one (checked by WPT domparsing/xml-serialization.xhtml)
        void AppendId(string id) => _output.Append('"').Append(id).Append('"');
    }

    private static bool IsPubidChar(char c) => c is ' ' or '\r' or '\n' || char.IsAsciiLetterOrDigit(c) || "-'()+,./:=?;!*#@$_%".Contains(c, StringComparison.Ordinal);

    private static bool IsXmlChars(string value)
    {
        foreach (var rune in value.EnumerateRunes())
        {
            // Lone surrogates are enumerated as U+FFFD by EnumerateRunes: check them explicitly
            if (!XmlNames.IsXmlChar(rune.Value))
                return false;
        }

        for (var i = 0; i < value.Length; i++)
        {
            if (char.IsHighSurrogate(value[i]) && i + 1 < value.Length && char.IsLowSurrogate(value[i + 1]))
            {
                i++;
            }
            else if (char.IsSurrogate(value[i]))
            {
                return false;
            }
        }

        return true;
    }

    private static readonly HashSet<string> VoidElements = new(StringComparer.Ordinal)
    {
        "area", "base", "basefont", "bgsound", "br", "col", "embed", "frame", "hr", "img", "input", "keygen", "link", "menuitem", "meta", "param", "source", "track", "wbr",
    };

    /// <summary>https://w3c.github.io/DOM-Parsing/#dfn-xml-serializing-an-element-node</summary>
    private void SerializeElement(Element node, string? namespaceParameter, NamespacePrefixMap prefixMap)
    {
        if (_requireWellFormed && (node.LocalName.Contains(':', StringComparison.Ordinal) || !XmlNames.IsName(node.LocalName)))
            Fail("The element name is not well-formed");

        _output.Append('<');
        string qualifiedName;
        var ignoreNamespaceDefinitionAttribute = false;
        var map = prefixMap.Clone();
        var localPrefixes = new Dictionary<string, string>(StringComparer.Ordinal);
        var localDefaultNamespace = RecordNamespaceInformation(node, map, localPrefixes);
        var inheritedNs = namespaceParameter;
        var ns = node.NamespaceUri;
        if (inheritedNs == ns)
        {
            if (localDefaultNamespace is not null)
            {
                ignoreNamespaceDefinitionAttribute = true;
            }

            qualifiedName = ns == Namespaces.Xml ? "xml:" + node.LocalName : node.LocalName;
            _output.Append(qualifiedName);
        }
        else
        {
            var prefix = node.Prefix;
            var candidatePrefix = map.RetrievePreferredPrefix(ns, prefix);
            if (prefix == "xmlns")
            {
                if (_requireWellFormed)
                    Fail("An element cannot have the xmlns prefix");

                candidatePrefix = prefix;
            }

            if (candidatePrefix is not null)
            {
                qualifiedName = candidatePrefix + ":" + node.LocalName;
                if (localDefaultNamespace is not null && localDefaultNamespace != Namespaces.Xml)
                {
                    inheritedNs = localDefaultNamespace.Length == 0 ? null : localDefaultNamespace;
                }

                _output.Append(qualifiedName);
            }
            else if (prefix is not null)
            {
                if (localPrefixes.ContainsKey(prefix))
                {
                    prefix = GeneratePrefix(map, ns);
                }

                map.Add(ns, prefix);
                qualifiedName = prefix + ":" + node.LocalName;
                _output.Append(qualifiedName).Append(" xmlns:").Append(prefix).Append("=\"");
                AppendAttributeValue(ns);
                _output.Append('"');
                if (localDefaultNamespace is not null)
                {
                    inheritedNs = localDefaultNamespace.Length == 0 ? null : localDefaultNamespace;
                }
            }
            else if (localDefaultNamespace is null || localDefaultNamespace != (ns ?? ""))
            {
                ignoreNamespaceDefinitionAttribute = true;
                qualifiedName = node.LocalName;
                inheritedNs = ns;
                _output.Append(qualifiedName).Append(" xmlns=\"");
                AppendAttributeValue(ns);
                _output.Append('"');
            }
            else
            {
                qualifiedName = node.LocalName;
                inheritedNs = ns;
                _output.Append(qualifiedName);
            }
        }

        SerializeAttributes(node, map, localPrefixes, ignoreNamespaceDefinitionAttribute);

        var isHtml = ns == Namespaces.Html;
        var isTemplate = isHtml && node.LocalName == "template";
        if (isHtml && node.FirstChild is null && !isTemplate && VoidElements.Contains(node.LocalName))
        {
            _output.Append(" />");
            return;
        }

        if (!isHtml && node.FirstChild is null)
        {
            _output.Append("/>");
            return;
        }

        _output.Append('>');
        var children = isTemplate ? ((HtmlTemplateElement)node).Content : (Node)node;
        for (var child = children.FirstChild; child is not null; child = child.NextSibling)
        {
            SerializeNode(child, inheritedNs, map);
        }

        _output.Append("</").Append(qualifiedName).Append('>');
    }

    /// <summary>https://w3c.github.io/DOM-Parsing/#dfn-recording-the-namespace-information</summary>
    private static string? RecordNamespaceInformation(Element element, NamespacePrefixMap map, Dictionary<string, string> localPrefixes)
    {
        string? defaultNamespaceAttributeValue = null;
        for (var i = 0; i < element.AttributeCount; i++)
        {
            var prefix = element.GetAttributePrefix(i);
            var value = element.GetAttributeValue(i);

            // An attribute named "xmlns" without namespace (e.g. created by setAttribute()) is handled as a default
            // namespace declaration, as browsers do (checked by WPT domparsing/XMLSerializer-serializeToString.html)
            if (IsNullNamespaceXmlnsAttribute(element, i))
            {
                defaultNamespaceAttributeValue = value;
                continue;
            }

            if (element.GetAttributeNamespace(i) != Namespaces.Xmlns)
                continue;

            if (prefix is null)
            {
                defaultNamespaceAttributeValue = value;
                continue;
            }

            var prefixDefinition = element.GetAttributeLocalName(i);
            string? namespaceDefinition = value;
            if (namespaceDefinition == Namespaces.Xml)
                continue;

            if (namespaceDefinition.Length == 0)
            {
                namespaceDefinition = null;
            }

            if (map.IsFound(namespaceDefinition, prefixDefinition))
                continue;

            map.Add(namespaceDefinition, prefixDefinition);
            localPrefixes[prefixDefinition] = namespaceDefinition ?? "";
        }

        return defaultNamespaceAttributeValue;
    }

    private static bool IsNullNamespaceXmlnsAttribute(Element element, int index) =>
        element.GetAttributeNamespace(index) is null && element.GetAttributePrefix(index) is null && element.GetAttributeLocalName(index) == "xmlns";

    /// <summary>https://w3c.github.io/DOM-Parsing/#dfn-xml-serialization-of-the-attributes</summary>
    private void SerializeAttributes(Element element, NamespacePrefixMap map, Dictionary<string, string> localPrefixes, bool ignoreNamespaceDefinitionAttribute)
    {
        var localNameSet = new HashSet<(string?, string)>();
        for (var i = 0; i < element.AttributeCount; i++)
        {
            var attributeNamespace = element.GetAttributeNamespace(i);
            var localName = element.GetAttributeLocalName(i);
            var value = element.GetAttributeValue(i);
            if (_requireWellFormed && localNameSet.Contains((attributeNamespace, localName)))
                Fail("The element has two attributes with the same name and namespace");

            localNameSet.Add((attributeNamespace, localName));
            if (ignoreNamespaceDefinitionAttribute && IsNullNamespaceXmlnsAttribute(element, i))
                continue;

            string? candidatePrefix = null;
            if (attributeNamespace is not null)
            {
                var prefix = element.GetAttributePrefix(i);
                candidatePrefix = map.RetrievePreferredPrefix(attributeNamespace, prefix);
                if (attributeNamespace == Namespaces.Xmlns)
                {
                    if (value == Namespaces.Xml ||
                        (prefix is null && ignoreNamespaceDefinitionAttribute) ||
                        (prefix is not null && (!localPrefixes.TryGetValue(localName, out var localValue) || localValue != value) && map.IsFound(value.Length == 0 ? null : value, localName)))
                    {
                        continue;
                    }

                    if (_requireWellFormed && value == Namespaces.Xmlns)
                        Fail("The XMLNS namespace cannot be declared");

                    if (_requireWellFormed && value.Length == 0)
                        Fail("Namespace prefix declarations cannot be empty");

                    if (prefix == "xmlns")
                    {
                        candidatePrefix = "xmlns";
                    }
                }
                else if (candidatePrefix is null)
                {
                    candidatePrefix = prefix is not null && !localPrefixes.ContainsKey(prefix)
                        ? prefix
                        : GeneratePrefix(map, attributeNamespace);
                    map.Add(attributeNamespace, candidatePrefix);
                    localPrefixes[candidatePrefix] = attributeNamespace;
                    _output.Append(" xmlns:").Append(candidatePrefix).Append("=\"");
                    AppendAttributeValue(attributeNamespace);
                    _output.Append('"');
                }
            }

            _output.Append(' ');
            if (candidatePrefix is not null)
            {
                _output.Append(candidatePrefix).Append(':');
            }

            if (_requireWellFormed && (localName.Contains(':', StringComparison.Ordinal) || !XmlNames.IsName(localName) || (localName == "xmlns" && attributeNamespace is null)))
                Fail("The attribute name is not well-formed");

            _output.Append(localName).Append("=\"");
            AppendAttributeValue(value);
            _output.Append('"');
        }
    }

    /// <summary>
    /// https://w3c.github.io/DOM-Parsing/#dfn-generating-a-prefix. As specified (and implemented by browsers), the
    /// generated prefix can conflict with an existing declaration (https://github.com/w3c/DOM-Parsing/issues/44).
    /// </summary>
    private string GeneratePrefix(NamespacePrefixMap map, string? newNamespace)
    {
        var generated = "ns" + _prefixIndex.ToString(System.Globalization.CultureInfo.InvariantCulture);
        _prefixIndex++;
        map.Add(newNamespace, generated);
        return generated;
    }

    /// <summary>https://w3c.github.io/DOM-Parsing/#dfn-serializing-an-attribute-value</summary>
    private void AppendAttributeValue(string? value)
    {
        if (value is null)
            return;

        if (_requireWellFormed && !IsXmlChars(value))
            Fail("The attribute value contains characters that are not allowed in XML");

        AppendEscapedAttributeValue(_output, value);
    }

    /// <summary>
    /// Escapes an attribute value for XML markup: only the predefined XML entities and character references are
    /// emitted, and the whitespace characters are escaped so that attribute-value normalization preserves them.
    /// </summary>
    internal static void AppendEscapedAttributeValue(StringBuilder output, string value)
    {
        foreach (var c in value)
        {
            switch (c)
            {
                case '&':
                    output.Append("&amp;");
                    break;
                case '"':
                    output.Append("&quot;");
                    break;
                case '<':
                    output.Append("&lt;");
                    break;
                case '>':
                    output.Append("&gt;");
                    break;
                case '\t':
                    output.Append("&#9;");
                    break;
                case '\n':
                    output.Append("&#xA;");
                    break;
                case '\r':
                    output.Append("&#xD;");
                    break;
                default:
                    output.Append(c);
                    break;
            }
        }
    }

    /// <summary>https://w3c.github.io/DOM-Parsing/#dfn-namespace-prefix-map</summary>
    private sealed class NamespacePrefixMap
    {
        private readonly Dictionary<string, List<string>> _map;

        public NamespacePrefixMap()
        {
            _map = new(StringComparer.Ordinal);
        }

        private NamespacePrefixMap(Dictionary<string, List<string>> map)
        {
            _map = map;
        }

        // The null namespace is represented by a key that is not a valid namespace
        private static string Key(string? ns) => ns ?? "\0";

        public NamespacePrefixMap Clone()
        {
            var copy = new Dictionary<string, List<string>>(_map.Count, StringComparer.Ordinal);
            foreach (var (key, value) in _map)
            {
                copy[key] = [.. value];
            }

            return new NamespacePrefixMap(copy);
        }

        /// <summary>https://w3c.github.io/DOM-Parsing/#dfn-retrieving-a-preferred-prefix-string</summary>
        public string? RetrievePreferredPrefix(string? ns, string? preferredPrefix)
        {
            if (!_map.TryGetValue(Key(ns), out var candidates))
                return null;

            if (preferredPrefix is not null && candidates.Contains(preferredPrefix))
                return preferredPrefix;

            return candidates[^1];
        }

        /// <summary>https://w3c.github.io/DOM-Parsing/#dfn-found</summary>
        public bool IsFound(string? ns, string prefix) => _map.TryGetValue(Key(ns), out var candidates) && candidates.Contains(prefix);

        public void Add(string? ns, string prefix)
        {
            if (_map.TryGetValue(Key(ns), out var candidates))
            {
                candidates.Add(prefix);
            }
            else
            {
                _map[Key(ns)] = [prefix];
            }
        }


    }
}
