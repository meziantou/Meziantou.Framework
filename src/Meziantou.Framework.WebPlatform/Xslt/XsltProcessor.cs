using System.Xml.XPath;
using Meziantou.Framework.WebPlatform.Scripting;
using Meziantou.Framework.WebPlatform.XPath;
using Meziantou.Framework.WebPlatform.Xslt;

namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>
/// Transforms nodes with an XSLT 1.0 stylesheet (https://dom.spec.whatwg.org/#interface-xsltprocessor,
/// https://www.w3.org/TR/xslt-10/), as the browsers do.
/// </summary>
/// <remarks>
/// <para>
/// The stylesheet is compiled when the first transformation runs, and its <c>xsl:include</c> and <c>xsl:import</c> elements
/// load their stylesheets synchronously with the fetch of the document of the current global object (the same-origin
/// requests of destination <c>xslt</c>, checked by its content security policies). As in Chromium, the <c>document()</c>
/// function returns an empty node-set, and the <c>disable-output-escaping</c> attributes are ignored.
/// </para>
/// <para>
/// The XPath expressions are evaluated by System.Xml.XPath over the DOM: the stylesheet is interpreted, so the transformations
/// work without dynamic code (with native AOT). A recursion deeper than 3000 templates is an error.
/// </para>
/// </remarks>
/// <seealso href="https://dom.spec.whatwg.org/#interface-xsltprocessor"/>
[IdlInterface("XSLTProcessor", "https://dom.spec.whatwg.org/#interface-xsltprocessor")]
public sealed class XsltProcessor
{
    // The associated document of the relevant global object of the processor, which fetches the included stylesheets
    private readonly Document _relevantDocument;
    private readonly Dictionary<XsltQName, (object? Value, object XPathValue)> _parameters = [];
    private Node? _stylesheet;
    private XsltStylesheet? _compiled;

    /// <summary>Creates a processor associated with the current global document.</summary>
    /// <seealso href="https://dom.spec.whatwg.org/#dom-xsltprocessor-xsltprocessor"/>
    [IdlMember("constructor", "https://dom.spec.whatwg.org/#dom-xsltprocessor-xsltprocessor")]
    public XsltProcessor()
    {
        _relevantDocument = Document.CurrentGlobalDocument;
    }

    /// <summary>The limits of the transformations (the fuzzing targets set lower limits).</summary>
    internal XsltLimits Limits { get; init; } = XsltLimits.Default;

    /// <summary>
    /// Imports a stylesheet: a document whose document element is an <c>xsl:stylesheet</c> or <c>xsl:transform</c> element
    /// (or a literal result element with an <c>xsl:version</c> attribute), or such an element. The stylesheet replaces the
    /// previous one; it is compiled by the next transformation.
    /// </summary>
    /// <exception cref="DomException">The node is not a document or an element (<c>NotSupportedError</c>).</exception>
    /// <seealso href="https://dom.spec.whatwg.org/#dom-xsltprocessor-importstylesheet"/>
    [IdlMember("importStylesheet", "https://dom.spec.whatwg.org/#dom-xsltprocessor-importstylesheet")]
    public void ImportStylesheet(Node style)
    {
        ArgumentNullException.ThrowIfNull(style);
        if (style is not (Document or Element))
            throw DomException.NotSupported("The stylesheet must be a document or an element");

        _stylesheet = style;
        _compiled = null;
    }

    /// <summary>
    /// Transforms a node, and returns the nodes of the result tree in a fragment owned by <paramref name="output"/>. With the
    /// <c>html</c> output method, or without output method when <paramref name="output"/> is an HTML document, the elements
    /// in no namespace are HTML elements. The scripts of the fragment run when it is inserted in a document.
    /// </summary>
    /// <exception cref="DomException">
    /// No stylesheet is imported (<c>InvalidStateError</c>), the source is not a node that XPath represents
    /// (<c>NotSupportedError</c>), or the stylesheet is not valid or the transformation fails (<c>OperationError</c>).
    /// </exception>
    /// <seealso href="https://dom.spec.whatwg.org/#dom-xsltprocessor-transformtofragment"/>
    [IdlMember("transformToFragment", "https://dom.spec.whatwg.org/#dom-xsltprocessor-transformtofragment")]
    public DocumentFragment TransformToFragment(Node source, Document output)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(output);
        var (stylesheet, result) = Transform(source);
        return XsltOutput.ToFragment(stylesheet.Output, result, output);
    }

    /// <summary>
    /// Transforms a node, and returns the result tree as a new document that has no browsing context: an HTML document for the
    /// <c>html</c> output method, an XML document for the <c>xml</c> output method, and an HTML document whose text is in a
    /// <c>pre</c> element for the <c>text</c> output method. Its scripts never run.
    /// </summary>
    /// <exception cref="DomException">
    /// No stylesheet is imported (<c>InvalidStateError</c>), the source is not a node that XPath represents
    /// (<c>NotSupportedError</c>), or the stylesheet is not valid or the transformation fails (<c>OperationError</c>).
    /// </exception>
    /// <seealso href="https://dom.spec.whatwg.org/#dom-xsltprocessor-transformtodocument"/>
    [IdlMember("transformToDocument", "https://dom.spec.whatwg.org/#dom-xsltprocessor-transformtodocument")]
    public Document TransformToDocument(Node source)
    {
        ArgumentNullException.ThrowIfNull(source);
        var (stylesheet, result) = Transform(source);
        return XsltOutput.ToDocument(stylesheet.Output, result, source.OwnerDocument ?? source as Document);
    }

    /// <summary>
    /// Sets the value of a global parameter of the stylesheet (an <c>xsl:param</c> element). A string, a number and a boolean
    /// are the XPath values; a node, or a list of nodes, is a node-set; any other value is converted to a string.
    /// </summary>
    /// <seealso href="https://dom.spec.whatwg.org/#dom-xsltprocessor-setparameter"/>
    [IdlMember("setParameter", "https://dom.spec.whatwg.org/#dom-xsltprocessor-setparameter")]
    public void SetParameter(string namespaceUri, string localName, object? value)
    {
        ArgumentNullException.ThrowIfNull(localName);
        _parameters[new XsltQName(namespaceUri ?? "", localName)] = (value, ToXPathValue(value));
    }

    /// <summary>Returns the value of a parameter set by <see cref="SetParameter"/>, or null.</summary>
    /// <seealso href="https://dom.spec.whatwg.org/#dom-xsltprocessor-getparameter"/>
    [IdlMember("getParameter", "https://dom.spec.whatwg.org/#dom-xsltprocessor-getparameter")]
    public object? GetParameter(string namespaceUri, string localName)
    {
        ArgumentNullException.ThrowIfNull(localName);
        return _parameters.TryGetValue(new XsltQName(namespaceUri ?? "", localName), out var parameter) ? parameter.Value : null;
    }

    /// <summary>Removes a parameter set by <see cref="SetParameter"/>.</summary>
    /// <seealso href="https://dom.spec.whatwg.org/#dom-xsltprocessor-removeparameter"/>
    [IdlMember("removeParameter", "https://dom.spec.whatwg.org/#dom-xsltprocessor-removeparameter")]
    public void RemoveParameter(string namespaceUri, string localName)
    {
        ArgumentNullException.ThrowIfNull(localName);
        _parameters.Remove(new XsltQName(namespaceUri ?? "", localName));
    }

    /// <summary>Removes all the parameters.</summary>
    /// <seealso href="https://dom.spec.whatwg.org/#dom-xsltprocessor-clearparameters"/>
    [IdlMember("clearParameters", "https://dom.spec.whatwg.org/#dom-xsltprocessor-clearparameters")]
    public void ClearParameters() => _parameters.Clear();

    /// <summary>Removes the stylesheet and all the parameters.</summary>
    /// <seealso href="https://dom.spec.whatwg.org/#dom-xsltprocessor-reset"/>
    [IdlMember("reset", "https://dom.spec.whatwg.org/#dom-xsltprocessor-reset")]
    public void Reset()
    {
        _stylesheet = null;
        _compiled = null;
        _parameters.Clear();
    }

    private (XsltStylesheet Stylesheet, DocumentFragment Result) Transform(Node source)
    {
        if (_stylesheet is null)
            throw DomException.InvalidState("No stylesheet is imported");

        if (source is not (Document or DocumentFragment or Element or Attr or Text or Comment or ProcessingInstruction))
            throw DomException.NotSupported("The source must be a document, a fragment, an element, an attribute, a text node, a comment or a processing instruction");

        try
        {
            var loader = new XsltDocumentLoader(_relevantDocument);
            _compiled ??= XsltCompiler.Compile(_stylesheet, loader);
            var parameters = new Dictionary<XsltQName, object>();
            foreach (var (name, (_, value)) in _parameters)
            {
                parameters[name] = value is List<Node> nodes ? XsltNodeSet.FromUnordered([.. nodes.Select(node => (XPathNavigator)new DomXPathNavigator(node))]) : value;
            }

            // As in Chromium, document() is disabled in the transformations of XSLTProcessor
            return (_compiled, XsltExecution.Transform(_compiled, source, parameters, loader, documentFunctionEnabled: false, Limits));
        }
        catch (XsltException ex)
        {
            throw new DomException(ex.Message, "OperationError");
        }
    }

    /// <summary>Converts the value of a parameter to an XPath value: a string, a number, a boolean, or a list of nodes.</summary>
    private static object ToXPathValue(object? value)
    {
        switch (value)
        {
            case null:
                return "";
            case string text:
                return text;
            case bool boolean:
                return boolean;
            case double or float or int or long or short or byte or uint or ulong or ushort or sbyte or decimal:
                return Convert.ToDouble(value, System.Globalization.CultureInfo.InvariantCulture);
            case Node node:
                return IsXPathNode(node) ? new List<Node> { node } : "";
        }

        var operations = GlobalScope.Current is { } global ? global.ScriptEngine?.GetScriptValueOperations(global) ?? DotNetScriptValueOperations.Instance : DotNetScriptValueOperations.Instance;
        if (operations.IsNullOrUndefined(value))
            return "";

        if (operations.IsBoolean(value))
            return operations.ToBoolean(value);

        if (operations.IsNumber(value))
            return operations.ToNumber(value);

        if (operations.IsObject(value) && operations.TryToSequence(value) is { } items)
        {
            var nodes = items.ConvertAll(operations.UnwrapPlatformObject);
            if (nodes.TrueForAll(item => item is Node))
                return nodes.Cast<Node>().Where(IsXPathNode).ToList();
        }

        return operations.ToDomString(value);
    }

    private static bool IsXPathNode(Node node) => node is Document or DocumentFragment or Element or Attr or Text or Comment or ProcessingInstruction;
}
