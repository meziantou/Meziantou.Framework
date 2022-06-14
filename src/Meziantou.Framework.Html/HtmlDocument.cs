#nullable disable
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Xml;

namespace Meziantou.Framework.Html;

[DebuggerDisplay("{Name}")]
public sealed class HtmlDocument : HtmlNode
{
    private HtmlOptions _options = new();
    private string _filePath;
    private HtmlElement _baseElement;
    private bool _baseElementSearched;
    private bool? _xhtml;
    private HtmlAttribute _namespaceXml;
    private Dictionary<string, string> _declaredNamespaces;
    private Dictionary<string, string> _declaredPrefixes;

    public event EventHandler<HtmlDocumentParseEventArgs> Parsing;
    public event EventHandler<HtmlDocumentParseEventArgs> Parsed;

    public HtmlDocument()
        : base(string.Empty, "#document", string.Empty, ownerDocument: null)
    {
    }

    public Encoding StreamEncoding { get; private set; }
    public Encoding DetectedEncoding { get; private set; }
    public new Uri BaseAddress { get; set; }
    public bool ReaderWasRestarted { get; private set; }
    public HtmlElement DocumentType { get; private set; }
    public HtmlElement HtmlElement { get; private set; }
    public HtmlElement BodyElement { get; private set; }
    public HtmlElement HeadElement { get; private set; }

    internal static void RemoveIntrinsicElement(HtmlDocument doc, HtmlElement element)
    {
        if (doc == null || element == null)
            return;

        if (element == doc.HtmlElement)
        {
            doc.HtmlElement = null;
            return;
        }

        if (element == doc.BodyElement)
        {
            doc.BodyElement = null;
            return;
        }

        if (element == doc.HeadElement)
        {
            doc.HeadElement = null;
            return;
        }

        if (element == doc.DocumentType)
        {
            doc.DocumentType = null;
            return;
        }
    }

    public string FilePath
    {
        get => _filePath;
        private set
        {
            _filePath = value;
            BaseAddress ??= (Utilities.IsRooted(value) ? new Uri(value) : new Uri(Path.GetFullPath(value)));
        }
    }

    public HtmlOptions Options
    {
        get => _options;
        set
        {
            _options = value ?? throw new ArgumentNullException(nameof(value));
            OnPropertyChanged();
        }
    }

    public void LoadHtml(string html)
    {
        if (html is null)
            throw new ArgumentNullException(nameof(html));

        Clear();
        using var reader = new StringReader(html);
        StreamEncoding = Utilities.GetDefaultEncoding(); // This is arguable, but it's better for saves
        InternalLoad(reader, firstPass: false);
    }

    public void Load(string filePath, Encoding encoding, bool detectEncodingFromByteOrderMarks, int bufferSize)
    {
        if (filePath is null)
            throw new ArgumentNullException(nameof(filePath));

        Clear();
        FilePath = filePath;
        using (var reader = Utilities.OpenReader(filePath, encoding, detectEncodingFromByteOrderMarks, bufferSize))
        {
            reader.Peek();
            StreamEncoding = reader.CurrentEncoding;
            if (InternalLoad(reader, firstPass: true))
                return;
        }

        var streamEncoding = DetectedEncoding;
        Restart();
        using (var reader = Utilities.OpenReader(filePath, streamEncoding, detectEncodingFromByteOrderMarks: false, bufferSize))
        {
            reader.Peek();
            StreamEncoding = reader.CurrentEncoding;
            InternalLoad(reader, firstPass: false);
        }
    }

    public void Load(string filePath, bool detectEncodingFromByteOrderMarks)
    {
        if (filePath is null)
            throw new ArgumentNullException(nameof(filePath));

        Clear();
        FilePath = filePath;
        if (detectEncodingFromByteOrderMarks)
        {
            using var reader = Utilities.OpenReader(filePath, detectEncodingFromByteOrderMarks: true);
            reader.Peek();
            StreamEncoding = reader.CurrentEncoding;
            if (InternalLoad(reader, firstPass: true))
                return;
        }
        else
        {
            // use ansi as the default encoding
            using var reader = Utilities.OpenReader(filePath, Utilities.GetDefaultEncoding(), detectEncodingFromByteOrderMarks: false);
            reader.Peek();
            StreamEncoding = reader.CurrentEncoding;
            if (InternalLoad(reader, firstPass: true))
                return;
        }

        var streamEncoding = DetectedEncoding;
        Restart();
        using (var reader = Utilities.OpenReader(filePath, streamEncoding))
        {
            reader.Peek();
            StreamEncoding = reader.CurrentEncoding;
            InternalLoad(reader, firstPass: false);
        }
    }

    public void Load(string filePath, Encoding encoding)
    {
        if (filePath is null)
            throw new ArgumentNullException(nameof(filePath));

        Clear();
        FilePath = filePath;
        using (var reader = Utilities.OpenReader(filePath, encoding))
        {
            reader.Peek();
            StreamEncoding = reader.CurrentEncoding;
            if (InternalLoad(reader, firstPass: true))
                return;
        }

        var streamEncoding = DetectedEncoding;
        Restart();
        using (var reader = Utilities.OpenReader(filePath, streamEncoding))
        {
            reader.Peek();
            StreamEncoding = reader.CurrentEncoding;
            InternalLoad(reader, firstPass: false);
        }
    }

    public void Load(string filePath, Encoding encoding, bool detectEncodingFromByteOrderMarks)
    {
        if (filePath is null)
            throw new ArgumentNullException(nameof(filePath));

        Clear();
        FilePath = filePath;
        using (var reader = Utilities.OpenReader(filePath, encoding, detectEncodingFromByteOrderMarks))
        {
            reader.Peek();
            StreamEncoding = reader.CurrentEncoding;
            if (InternalLoad(reader, firstPass: true))
                return;
        }

        var streamEncoding = DetectedEncoding;
        Restart();
        using (var reader = Utilities.OpenReader(filePath, streamEncoding))
        {
            reader.Peek();
            StreamEncoding = reader.CurrentEncoding;
            InternalLoad(reader, firstPass: false);
        }
    }

    public void Load(string filePath)
    {
        if (filePath is null)
            throw new ArgumentNullException(nameof(filePath));

        Clear();
        FilePath = filePath;
        using (var reader = Utilities.OpenReader(filePath, Utilities.GetDefaultEncoding()))
        {
            reader.Peek();
            StreamEncoding = reader.CurrentEncoding;
            if (InternalLoad(reader, firstPass: true))
                return;
        }

        var streamEncoding = DetectedEncoding;
        Restart();
        using (var reader = Utilities.OpenReader(filePath, streamEncoding))
        {
            reader.Peek();
            StreamEncoding = reader.CurrentEncoding;
            InternalLoad(reader, firstPass: false);
        }
    }

    public void Load(Stream stream, bool detectEncodingFromByteOrderMarks)
    {
        if (stream is null)
            throw new ArgumentNullException(nameof(stream));

        Clear();
        if (detectEncodingFromByteOrderMarks)
        {
            using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, bufferSize: 1024, leaveOpen: true);
            reader.Peek();
            StreamEncoding = reader.CurrentEncoding;
            if (InternalLoad(reader, firstPass: true))
                return;
        }
        else
        {
            using var reader = new StreamReader(stream, Utilities.GetDefaultEncoding(), detectEncodingFromByteOrderMarks: false, bufferSize: 1024, leaveOpen: true);
            reader.Peek();
            StreamEncoding = reader.CurrentEncoding;
            if (InternalLoad(reader, firstPass: true))
                return;
        }

        var streamEncoding = DetectedEncoding;
        Restart();
        using (var reader = new StreamReader(stream, streamEncoding, detectEncodingFromByteOrderMarks: false, bufferSize: 1024, leaveOpen: true))
        {
            reader.Peek();
            StreamEncoding = reader.CurrentEncoding;
            InternalLoad(reader, firstPass: false);
        }
    }

    public void Load(Stream stream, Encoding encoding)
    {
        if (stream is null)
            throw new ArgumentNullException(nameof(stream));

        Clear();
        using (var reader = new StreamReader(stream, encoding, detectEncodingFromByteOrderMarks: true, bufferSize: 1024, leaveOpen: true))
        {
            reader.Peek();
            StreamEncoding = reader.CurrentEncoding;
            if (InternalLoad(reader, firstPass: true))
                return;
        }

        var streamEncoding = DetectedEncoding;
        Restart();
        using (var reader = new StreamReader(stream, streamEncoding, detectEncodingFromByteOrderMarks: false, bufferSize: 1024, leaveOpen: true))
        {
            reader.Peek();
            StreamEncoding = reader.CurrentEncoding;
            InternalLoad(reader, firstPass: false);
        }
    }

    public void Load(Stream stream, Encoding encoding, bool detectEncodingFromByteOrderMarks)
    {
        if (stream is null)
            throw new ArgumentNullException(nameof(stream));

        Clear();
        using (var reader = new StreamReader(stream, encoding, detectEncodingFromByteOrderMarks, bufferSize: 1024, leaveOpen: true))
        {
            reader.Peek();
            StreamEncoding = reader.CurrentEncoding;
            if (InternalLoad(reader, firstPass: true))
                return;
        }

        var streamEncoding = DetectedEncoding;
        Restart();
        using (var reader = new StreamReader(stream, streamEncoding, detectEncodingFromByteOrderMarks: false, bufferSize: 1024, leaveOpen: true))
        {
            reader.Peek();
            StreamEncoding = reader.CurrentEncoding;
            InternalLoad(reader, firstPass: false);
        }
    }

    public void Load(Stream stream, Encoding encoding, bool detectEncodingFromByteOrderMarks, int bufferSize)
    {
        if (stream is null)
            throw new ArgumentNullException(nameof(stream));

        Clear();
        using (var reader = new StreamReader(stream, encoding, detectEncodingFromByteOrderMarks, bufferSize, leaveOpen: true))
        {
            reader.Peek();
            StreamEncoding = reader.CurrentEncoding;
            if (InternalLoad(reader, firstPass: true))
                return;
        }

        var streamEncoding = DetectedEncoding;
        Restart();
        using (var reader = new StreamReader(stream, streamEncoding, detectEncodingFromByteOrderMarks: false, bufferSize, leaveOpen: true))
        {
            reader.Peek();
            StreamEncoding = reader.CurrentEncoding;
            InternalLoad(reader, firstPass: false);
        }
    }

    public void Load(Stream stream)
    {
        if (stream is null)
            throw new ArgumentNullException(nameof(stream));

        Clear();
        using (var reader = new StreamReader(stream, Utilities.GetDefaultEncoding(), detectEncodingFromByteOrderMarks: true, bufferSize: 1024, leaveOpen: true))
        {
            reader.Peek();
            StreamEncoding = reader.CurrentEncoding;
            if (InternalLoad(reader, firstPass: true))
                return;
        }

        var streamEncoding = DetectedEncoding;
        Restart();
        using (var reader = new StreamReader(stream, streamEncoding, detectEncodingFromByteOrderMarks: false, bufferSize: 1024, leaveOpen: true))
        {
            reader.Peek();
            StreamEncoding = reader.CurrentEncoding;
            InternalLoad(reader, firstPass: false);
        }
    }

    public void Load(TextReader reader)
    {
        if (reader is null)
            throw new ArgumentNullException(nameof(reader));

        Clear();
        InternalLoad(reader, firstPass: false);
    }

    [SuppressMessage("Style", "IDE0016:Use 'throw' expression", Justification = "It would change the behavior")]
    [SuppressMessage("Design", "CA1054:URI-like parameters should not be strings", Justification = "Breaking change")]
    public void AddNamespace(string prefix, string uri)
    {
        if (prefix == null)
        {
            _declaredPrefixes?.Remove(prefix);
        }
        else
        {
            if (uri == null)
                throw new ArgumentNullException(nameof(uri));

            _declaredPrefixes ??= new Dictionary<string, string>(StringComparer.Ordinal);
            _declaredPrefixes[prefix] = uri;
        }

        if (uri == null)
        {
            _declaredNamespaces?.Remove(uri);
        }
        else
        {
            if (prefix == null)
                throw new ArgumentNullException(nameof(prefix));

            _declaredNamespaces ??= new Dictionary<string, string>(StringComparer.InvariantCulture);
            _declaredNamespaces[uri] = prefix;
        }
    }

    public override string GetNamespaceOfPrefix(string prefix)
    {
        if (_declaredPrefixes == null)
            return string.Empty;

        if (_declaredPrefixes.TryGetValue(prefix, out var namespaceURI))
            return namespaceURI;

        return string.Empty;
    }

    public override string GetPrefixOfNamespace(string namespaceURI)
    {
        if (_declaredNamespaces == null)
            return string.Empty;

        if (_declaredNamespaces.TryGetValue(namespaceURI, out var prefix))
            return prefix;

        return string.Empty;
    }

    protected override void GetNamespaceAttributes(IDictionary<string, string> namespaces)
    {
        base.GetNamespaceAttributes(namespaces);
        if (_declaredPrefixes != null)
        {
            foreach (var kv in _declaredPrefixes)
            {
                namespaces[kv.Key] = kv.Value;
            }
        }

        if (_declaredNamespaces != null)
        {
            foreach (var kv in _declaredNamespaces)
            {
                namespaces[kv.Value] = kv.Key;
            }
        }
    }

    public IReadOnlyDictionary<string, string> DeclaredNamespaces => _declaredNamespaces ?? new Dictionary<string, string>(StringComparer.Ordinal);

    public IReadOnlyDictionary<string, string> DeclaredPrefixes => _declaredPrefixes ?? new Dictionary<string, string>(StringComparer.Ordinal);

    private HtmlAttribute CreateAttribute(string name)
    {
        ParseName(name, out var prefix, out var localName);
        return CreateAttribute(prefix, localName, namespaceURI: null);
    }

    [SuppressMessage("Design", "CA1054:URI-like parameters should not be strings", Justification = "Breaking change")]
    public HtmlAttribute CreateAttribute(string prefix, string localName, string namespaceURI)
    {
        if (prefix is null)
            throw new ArgumentNullException(nameof(prefix));

        if (localName is null)
            throw new ArgumentNullException(nameof(localName));

        if (prefix.Contains(':', StringComparison.Ordinal))
            throw new ArgumentException("Prefix must not contain ':'", nameof(prefix));

        return new HtmlAttribute(prefix, localName, namespaceURI, this);
    }

    public HtmlText CreateText()
    {
        return new HtmlText(this);
    }

    public HtmlText CreateText(string value)
    {
        var htmlText = CreateText();
        htmlText.Value = value;
        return htmlText;
    }

    public HtmlElement CreateElement(string name)
    {
        if (name is null)
            throw new ArgumentNullException(nameof(name));

        ParseName(name, out var prefix, out var localName);
        return CreateElement(prefix, localName, namespaceURI: null);
    }

    [SuppressMessage("Design", "CA1054:URI-like parameters should not be strings", Justification = "Breaking change")]
    public HtmlElement CreateElement(string prefix, string localName, string namespaceURI)
    {
        if (prefix is null)
            throw new ArgumentNullException(nameof(prefix));

        if (localName is null)
            throw new ArgumentNullException(nameof(localName));

        if (prefix.Contains(':', StringComparison.Ordinal))
            throw new ArgumentException("Prefix must not contain ':'", nameof(prefix));

        return new HtmlElement(prefix, localName, namespaceURI, this);
    }

    [SuppressMessage("Performance", "CA1822:Mark members as static", Justification = "By design")]
    public HtmlDocument CreateDocument()
    {
        return new HtmlDocument();
    }

    public HtmlComment CreateComment()
    {
        return new HtmlComment(this);
    }

    private void Clear()
    {
        Attributes.RemoveAll();
        ChildNodes.RemoveAll();
        ClearCaches();
        _filePath = null;
        ClearErrors();
        _baseElement = null;
        _baseElementSearched = false;
        _xhtml = null;
        HtmlElement = null;
        BodyElement = null;
        HeadElement = null;
        BaseAddress = null;
        DocumentType = null;
        DetectedEncoding = null;
        StreamEncoding = null;
        ReaderWasRestarted = false;
    }

    private void Restart()
    {
        Clear();
        ReaderWasRestarted = true;
    }

    // see http://stackoverflow.com/questions/4696499/meta-charset-utf-8-vs-meta-http-equiv-content-type
    private static string GetEncodingName(HtmlElement meta)
    {
        var name = Utilities.Nullify(meta.GetAttributeValue("charset"), trim: true);
        if (name != null)
            return name;

        var ct = meta.GetAttributeValue("http-equiv");
        if (ct == null || !ct.EqualsIgnoreCase("content-type"))
            return null;

        return Utilities.GetAttributeFromHeader(Utilities.Nullify(meta.GetAttributeValue("content"), trim: true), "charset");
    }

    private bool DetectEncoding(HtmlReader reader, HtmlElement element, bool firstPass)
    {
        if (DetectedEncoding != null)
            return true;

        if (element == null || !element.Name.EqualsIgnoreCase("meta"))
            return true;

        var encodingName = GetEncodingName(element);
        if (encodingName == null)
            return true;

        if (Options.ReaderThrowsOnUnknownDetectedEncoding)
        {
            DetectedEncoding = Encoding.GetEncoding(encodingName);
        }
        else
        {
            try
            {
                DetectedEncoding = Encoding.GetEncoding(encodingName);
            }
            catch
            {
                return true;
            }
        }

        // update stream encoding
        if (reader.TextReader is StreamReader sr)
        {
            StreamEncoding = sr.CurrentEncoding;
        }

        if (DetectedEncoding != null && StreamEncoding != null && !string.Equals(DetectedEncoding.EncodingName, StreamEncoding.EncodingName, StringComparison.Ordinal))
        {
            if (firstPass && Options.ReaderRestartsOnEncodingDetected && reader.IsRestartable)
            {
                if (reader.Restart())
                    return false;
            }

            AddError(new HtmlError(reader.State.Line, reader.State.Column, reader.State.Offset, HtmlErrorType.EncodingMismatch));
            if (Options.ReaderThrowsOnEncodingMismatch)
                throw new HtmlException(string.Format(CultureInfo.CurrentCulture, "HTML0004: Html encoding mismatch error. There seems to be mismatch between the stream (HTTP, File, etc.) encoding '{0}' and the declared (HTML META) encoding '{1}'.", StreamEncoding.EncodingName, DetectedEncoding.EncodingName));
        }
        return true;
    }

    private void OnParsing(HtmlDocumentParseEventArgs e)
    {
        Parsing?.Invoke(this, e);
    }

    private void OnParsed(HtmlDocumentParseEventArgs e)
    {
        Parsed?.Invoke(this, e);
    }

    private bool OnParsing(HtmlReader reader, ref HtmlNode currentNode, ref HtmlAttribute currentAttribute, out bool cont)
    {
        var e = new HtmlDocumentParseEventArgs(reader)
        {
            DetectedEncoding = DetectedEncoding,
            CurrentNode = currentNode,
            CurrentAttribute = currentAttribute,
        };

        OnParsing(e);
        DetectedEncoding = e.DetectedEncoding;
        currentNode = e.CurrentNode;
        currentAttribute = e.CurrentAttribute;
        cont = e.Continue;
        return !e.Cancel;
    }

    private bool OnParsed(HtmlReader reader, ref HtmlNode currentNode, ref HtmlAttribute currentAttribute)
    {
        var e = new HtmlDocumentParseEventArgs(reader)
        {
            DetectedEncoding = DetectedEncoding,
            CurrentNode = currentNode,
            CurrentAttribute = currentAttribute,
        };

        OnParsed(e);
        DetectedEncoding = e.DetectedEncoding;
        currentNode = e.CurrentNode;
        currentAttribute = e.CurrentAttribute;
        return !e.Cancel;
    }

    private HtmlReader CreateReader(TextReader reader)
    {
        return new HtmlReader(reader, Options);
    }

    private bool InternalLoad(TextReader reader, bool firstPass)
    {
        HtmlNode current = this;
        HtmlAttribute currentAtt = null;
        var htmlReader = CreateReader(reader);
        while (htmlReader.Read())
        {
            if (!OnParsing(htmlReader, ref current, ref currentAtt, out var mustContinue))
                break;

            if (mustContinue)
                continue;

            HtmlElement element;
            HtmlError error;
            switch (htmlReader.State.FragmentType)
            {
                case HtmlFragmentType.CDataText:
                case HtmlFragmentType.Text:
                    var text = CreateText();
                    text.StreamOrder = htmlReader.Offset;
                    text.IsCData = htmlReader.State.FragmentType == HtmlFragmentType.CDataText;
                    text.Value = htmlReader.State.Value;
                    if (current != null)
                    {
                        current.ChildNodes.Add(text);
                    }
                    break;

                case HtmlFragmentType.TagOpen:
                    string elementName;
                    bool processingInstruction;
                    if (htmlReader.State.Value.StartsWith('?'))
                    {
                        elementName = htmlReader.State.Value[1..];
                        processingInstruction = true;
                    }
                    else
                    {
                        elementName = htmlReader.State.Value;
                        processingInstruction = false;
                    }

                    element = CreateElement(elementName);
                    element.StreamOrder = htmlReader.Offset;

                    if (DocumentType == null && element.IsDocumentType)
                    {
                        DocumentType = element;
                    }
                    else if (elementName.EqualsIgnoreCase("html"))
                    {
                        HtmlElement = element;
                    }
                    else if (elementName.EqualsIgnoreCase("body"))
                    {
                        BodyElement = element;
                    }
                    else if (elementName.EqualsIgnoreCase("head"))
                    {
                        HeadElement = element;
                    }
                    else
                    {
                        element.IsProcessingInstruction = processingInstruction;
                    }

                    if (current != null)
                    {
                        current.ChildNodes.Add(element);
                    }

                    current = element;
                    break;

                case HtmlFragmentType.TagEnd:
                    element = current as HtmlElement;
                    if (!DetectEncoding(htmlReader, element, firstPass))
                        return false;

                    if (element != null && (element.Name.StartsWith('!') || element.IsProcessingInstruction))
                    {
                        element.IsEmpty = true;
                        if (current?.ParentNode != null)
                        {
                            current = current.ParentNode;
                        }
                    }
                    else
                    {
                        if (element != null)
                        {
                            var canHaveChild = (htmlReader.Options.GetElementReadOptions(element.Name) & HtmlElementReadOptions.NoChild) != HtmlElementReadOptions.NoChild;
                            if (canHaveChild)
                            {
                                current = element;
                            }
                            else if (current.ParentNode != null)
                            {
                                current = current.ParentNode;
                            }
                        }
                    }
                    break;

                case HtmlFragmentType.TagEndClose:
                case HtmlFragmentType.TagClose:
                    element = current as HtmlElement;
                    if (!DetectEncoding(htmlReader, element, firstPass))
                        return false;

                    if (element != null)
                    {
                        if (htmlReader.State.FragmentType == HtmlFragmentType.TagClose)
                        {
                            element.IsEmpty = false;
                        }

                        var parent = element.GetParentToClose(0, htmlReader.State.Value);
                        if (parent != null)
                        {
                            parent.IsClosed = true;
                            if (parent.ParentNode != null)
                            {
                                current = parent.ParentNode;
                            }

                            // check children closure
                            foreach (var childElement in parent.ChildNodes.OfType<HtmlElement>())
                            {
                                if (!childElement.IsClosed)
                                {
                                    if ((htmlReader.Options.GetElementReadOptions(childElement.Name) & HtmlElementReadOptions.AutoClosed) != HtmlElementReadOptions.AutoClosed)
                                    {
                                        error = new HtmlError(htmlReader.State, HtmlErrorType.TagNotClosed);
                                        AddError(error);
                                        error.Node = childElement;
                                        childElement.AddError(error);
                                    }
                                    else
                                    {
                                        childElement.IsClosed = true;
                                    }
                                }
                            }
                            break;
                        }
                    }

                    error = new HtmlError(htmlReader.State, HtmlErrorType.TagNotOpened);
                    AddError(error);

                    // add a text node to keep the maximum compatibility
                    text = CreateText();
                    text.StreamOrder = htmlReader.Offset;
                    error.Node = text;
                    text.Value = "</" + htmlReader.State.Value + ">";
                    text.AddError(error);
                    if (current != null)
                    {
                        current.ChildNodes.Add(text);
                    }
                    break;

                case HtmlFragmentType.AttName:
                    if (string.Equals(htmlReader.State.Value, "?", StringComparison.Ordinal))
                        break;

                    var att = CreateAttribute(htmlReader.State.Value);
                    att.StreamOrder = htmlReader.Offset;
                    att.NameQuoteChar = htmlReader.State.QuoteChar;

                    var existingAtt = current?.Attributes[att.Name];
                    if (existingAtt != null)
                    {
                        error = new HtmlError(htmlReader.State, HtmlErrorType.DuplicateAttribute);
                        AddError(error);
                    }

                    if (current != null)
                    {
                        current.Attributes.AddNoCheck(att);
                    }
                    currentAtt = att;
                    break;

                case HtmlFragmentType.AttValue:
                    if (currentAtt == null)
                        break;

                    currentAtt.Value = HtmlAttribute.UnescapeText(htmlReader.State.Value, htmlReader.State.QuoteChar);
                    currentAtt.QuoteChar = htmlReader.State.QuoteChar;

                    if (currentAtt.Name.EqualsIgnoreCase(XmlnsPrefix))
                    {
                        element = current as HtmlElement;
                        if (element != null && !Options.EmptyNamespaces.Contains(currentAtt.Value, StringComparer.Ordinal))
                        {
                            element.NamespaceURI = currentAtt.Value;
                        }
                    }
                    break;

                case HtmlFragmentType.Comment:
                    var comment = CreateComment();
                    comment.StreamOrder = htmlReader.Offset;
                    comment.Value = htmlReader.State.Value;
                    if (current != null)
                    {
                        current.ChildNodes.Add(comment);
                    }
                    break;
            }

            if (!OnParsed(htmlReader, ref current, ref currentAtt))
                break;
        }

        if (htmlReader.FirstEncodingErrorOffset >= 0)
        {
            AddError(new HtmlError(htmlReader.State.Line, htmlReader.State.Column, htmlReader.State.Offset, HtmlErrorType.EncodingError));
            if (DetectedEncoding == null)
            {
                if (htmlReader.Options.ReaderThrowsOnEncodingMismatch)
                    throw new HtmlException(string.Format(CultureInfo.CurrentCulture, "HTML0003: Html text encoding error. There seems to be a mismatch between the encoding '{0}', used to read the input Html text, or to open the input Html file, and the real detected text encoding, which cannot be determined at that time. If you do not want to see this exception thrown, please configure the ThrowOnEncodingError HtmlReader option. Offset of the first detected text encoding mismatch is {1}.", StreamEncoding.EncodingName, htmlReader.FirstEncodingErrorOffset));
            }
        }
        return true;
    }

    public override string InnerHtml
    {
        get => base.InnerHtml;
        set
        {
            if (!string.Equals(value, base.InnerHtml, StringComparison.Ordinal))
            {
                RemoveAll();
                if (value != null)
                {
                    var doc = CreateDocument();
                    doc.LoadHtml(value);
                    foreach (var node in doc.ChildNodes)
                    {
                        ChildNodes.AddNoCheck(node);
                    }
                }
                ClearCaches();
                OnPropertyChanged();
            }
        }
    }

    public override HtmlNodeType NodeType => HtmlNodeType.Document;

    public override string Name
    {
        get => base.Name;
        set
        {
            // do nothing
        }
    }

    public HtmlElement BaseElement
    {
        get
        {
            if (_baseElement == null && !_baseElementSearched)
            {
                _baseElement = SelectSingleNode("//base") as HtmlElement;
                _baseElementSearched = true;
            }
            return _baseElement;
        }
        set
        {
            _baseElement = value;
            _baseElementSearched = false;
        }
    }

    public void Save(TextWriter writer)
    {
        if (writer is null)
            throw new ArgumentNullException(nameof(writer));

        WriteTo(writer);
    }

    public void Save(XmlWriter writer)
    {
        if (writer is null)
            throw new ArgumentNullException(nameof(writer));

        WriteTo(writer);
    }

    public void Save(string filePath)
    {
        if (filePath is null)
            throw new ArgumentNullException(nameof(filePath));

        if (Path.GetExtension(filePath).EqualsIgnoreCase(".xml"))
        {
            var xmlWriterSettings = new XmlWriterSettings
            {
                Encoding = Encoding.UTF8,
            };

            using var fs = File.OpenWrite(filePath);
            using var writer = XmlWriter.Create(fs, xmlWriterSettings);
            Save(writer);

            return;
        }

        if (StreamEncoding != null)
        {
            using var writer = Utilities.OpenWriter(filePath, append: false, StreamEncoding);
            Save(writer);
        }
        else
        {
            using var writer = Utilities.OpenWriter(filePath);
            Save(writer);
        }
    }

    public void Save(string filePath, Encoding encoding)
    {
        if (filePath is null)
            throw new ArgumentNullException(nameof(filePath));

        if (Path.GetExtension(filePath).EqualsIgnoreCase(".xml"))
        {
            encoding ??= Encoding.UTF8;

            var xmlWriterSettings = new XmlWriterSettings
            {
                Encoding = encoding,
            };

            using var fs = File.OpenWrite(filePath);
            using var writer = XmlWriter.Create(fs, xmlWriterSettings);
            Save(writer);

            return;
        }

        using (var writer = Utilities.OpenWriter(filePath, append: false, encoding))
        {
            Save(writer);
        }
    }

    public void Save(Stream outStream)
    {
        if (outStream is null)
            throw new ArgumentNullException(nameof(outStream));

        if (StreamEncoding != null)
        {
            using var writer = new StreamWriter(outStream, StreamEncoding);
            Save(writer);
        }
        else
        {
            using var writer = new StreamWriter(outStream);
            Save(writer);
        }
    }

    public void Save(Stream outStream, Encoding encoding)
    {
        if (outStream is null)
            throw new ArgumentNullException(nameof(outStream));

        using var writer = new StreamWriter(outStream, encoding);
        Save(writer);
    }

    public override void WriteTo(TextWriter writer)
    {
        if (writer is null)
            throw new ArgumentNullException(nameof(writer));

        WriteContentTo(writer);
    }

    public override void WriteContentTo(TextWriter writer)
    {
        if (writer is null)
            throw new ArgumentNullException(nameof(writer));

        foreach (var node in ChildNodes)
        {
            node.WriteTo(writer);
        }
    }

    public override void WriteTo(XmlWriter writer)
    {
        if (writer is null)
            throw new ArgumentNullException(nameof(writer));

        WriteContentTo(writer);
    }

    public void WriteDocType(XmlWriter writer)
    {
        if (writer is null)
            throw new ArgumentNullException(nameof(writer));

        if (DocumentType == null)
            return;

        var name = DocumentType.Attributes.Count > 0 ? DocumentType.Attributes[0].Name : "html";
        string pubid = null;
        var att = DocumentType.Attributes["public"];
        if (att?.NextSibling != null)
        {
            pubid = att.NextSibling.Name;
        }

        string sysid = null;
        if (att?.NextSibling != null && att.NextSibling.NextSibling != null)
        {
            sysid = att.NextSibling.NextSibling.Name;
        }
        writer.WriteDocType(name, pubid, sysid, subset: null);
    }

    public bool IsXhtml
    {
        get
        {
            if (_xhtml.HasValue)
                return _xhtml.Value;

            return HtmlElement?.Attributes.GetNamespacePrefixIfDefined(XhtmlNamespaceURI) != null;
        }
        set => _xhtml = value;
    }

    public bool IsValidXmlDocument
    {
        get
        {
            foreach (var node in ChildNodes)
            {
                switch (node.NodeType)
                {
                    case HtmlNodeType.Comment:
                    case HtmlNodeType.ProcessingInstruction:
                        break;

                    case HtmlNodeType.Text:
                        if (!((HtmlText)node).IsWhitespace)
                            return false;
                        break;

                    case HtmlNodeType.DocumentType:
                        if (node != DocumentType)
                            return false;
                        break;

                    case HtmlNodeType.Element:
                        if (!node.Name.EqualsIgnoreCase("html") || node != HtmlElement)
                            return false;

                        break;

                    default:
                        return false;
                }
            }
            return true;
        }
    }

    public override void WriteContentTo(XmlWriter writer)
    {
        if (writer is null)
            throw new ArgumentNullException(nameof(writer));

        if (!IsValidXmlDocument)
        {
            var oneElementWritten = false;
            foreach (var node in ChildNodes)
            {
                if (node == HtmlElement)
                {
                    oneElementWritten = true;
                }

                if (node == HtmlElement || node == DocumentType ||
                    node.NodeType == HtmlNodeType.Comment ||
                    node.NodeType == HtmlNodeType.ProcessingInstruction ||
                    (node.NodeType == HtmlNodeType.Text && ((HtmlText)node).IsWhitespace))
                {
                    node.WriteTo(writer);
                }
            }

            if (!oneElementWritten)
            {
                foreach (var node in ChildNodes)
                {
                    if (node.NodeType == HtmlNodeType.Element)
                    {
                        node.WriteTo(writer);
                        break;
                    }
                }
            }
            return;
        }

        foreach (var node in ChildNodes)
        {
            node.WriteTo(writer);
        }
    }

    internal HtmlAttribute NamespaceXml
    {
        get
        {
            if (_namespaceXml == null)
            {
                _namespaceXml = CreateAttribute(XmlnsPrefix, XmlPrefix, XmlnsNamespaceURI);
                _namespaceXml.Value = XmlNamespaceURI;
            }
            return _namespaceXml;
        }
    }

    public HtmlNode ImportNode(HtmlNode node)
    {
        return ImportNode(node, HtmlCloneOptions.All);
    }

    [SuppressMessage("Performance", "CA1822:Mark members as static", Justification = "By design")]
    public HtmlNode ImportNode(HtmlNode node, HtmlCloneOptions cloneOptions)
    {
        if (node is null)
            throw new ArgumentNullException(nameof(node));

        return node.Clone(cloneOptions);
    }

    protected override void AddNamespacesInScope(XmlNamespaceScope scope, IDictionary<string, string> dictionary)
    {
        if (dictionary is null)
            throw new ArgumentNullException(nameof(dictionary));

        if (scope != XmlNamespaceScope.Local)
        {
            if (_declaredNamespaces != null)
            {
                foreach (var kv in _declaredNamespaces)
                {
                    dictionary[kv.Value] = kv.Key;
                }
            }

            if (_declaredPrefixes != null)
            {
                foreach (var kv in _declaredPrefixes)
                {
                    dictionary[kv.Key] = kv.Value;
                }
            }
        }

        base.AddNamespacesInScope(scope, dictionary);
    }

    [SuppressMessage("Design", "CA1055:URI-like return values should not be strings", Justification = "Breaking change")]
    public string MakeAbsoluteUrl(string url)
    {
        if (url is null)
            throw new ArgumentNullException(nameof(url));

        return MakeAbsoluteUrl(new Uri(url, UriKind.RelativeOrAbsolute)).ToString();
    }

    public Uri MakeAbsoluteUrl(Uri uri)
    {
        if (uri is null)
            throw new ArgumentNullException(nameof(uri));

        if (uri.IsAbsoluteUri)
            return uri;

        var baseAddress = BaseAddress;
        var baseElement = BaseElement;
        if (baseElement != null)
        {
            var href = Utilities.Nullify(baseElement.GetAttributeValue("href"), trim: true);
            if (href != null)
            {
                var address = new Uri(href, UriKind.RelativeOrAbsolute);
                if (address.IsAbsoluteUri)
                {
                    baseAddress = address;
                }
            }
        }

        if (baseAddress == null)
            throw new HtmlException("HTML0002: Cannot determine document's base address.");

        return new Uri(baseAddress, uri);
    }

    public string GetTitle()
    {
        return SelectSingleNode("//title")?.InnerText;
    }
}
