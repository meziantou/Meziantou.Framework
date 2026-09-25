using System.Xml;
using System.Xml.Linq;
using Meziantou.Framework.Language.Xml;

namespace Meziantou.Framework.DependencyScanning.Internals;

internal static class XmlUtilities
{
    /// <summary>The maximum nesting depth of a document. Loading a document is quadratic in its depth, and no file the scanners read nests deeper than a few dozen levels.</summary>
    internal const int MaxDepth = 256;

    /// <summary>The maximum number of characters of a document, so that a huge file cannot exhaust the memory or the time of a scan.</summary>
    internal const long MaxCharactersInDocument = 16 * 1024 * 1024;

    private static readonly XmlReaderSettings DefaultXmlSettings = CreateXmlReaderSettings(MaxCharactersInDocument);

    private static XmlReaderSettings CreateXmlReaderSettings(long maxCharactersInDocument) => new()
    {
        CloseInput = false,
        Async = true,

        // A DOCTYPE is skipped rather than rejected, as MSBuild does. Its entities are never expanded.
        DtdProcessing = DtdProcessing.Ignore,
        XmlResolver = null,
        MaxCharactersInDocument = maxCharactersInDocument,
    };

    public static Task<XDocument> LoadDocumentWithoutClosingStreamAsync(Stream stream, CancellationToken cancellationToken)
    {
        return LoadDocumentWithoutClosingStreamAsync(stream, LoadOptions.SetLineInfo, cancellationToken);
    }

    /// <summary>Loads an XML document from the current position of <paramref name="stream"/>, which is left open.</summary>
    /// <remarks>
    /// The encoding is the one <see cref="StreamUtilities.GetXmlEncoding"/> resolves, so a file whose declared encoding
    /// does not match its bytes is read like MSBuild reads it. Invalid bytes are replaced.
    /// </remarks>
    /// <exception cref="XmlException">The document is not valid, nests deeper than <see cref="MaxDepth"/>, or is larger than <see cref="MaxCharactersInDocument"/>.</exception>
    public static Task<XDocument> LoadDocumentWithoutClosingStreamAsync(Stream stream, LoadOptions loadOptions, CancellationToken cancellationToken)
    {
        return LoadDocumentAsync(stream, loadOptions, MaxDepth, MaxCharactersInDocument, cancellationToken);
    }

    internal static async Task<XDocument> LoadDocumentAsync(Stream stream, LoadOptions loadOptions, int maxDepth, long maxCharactersInDocument, CancellationToken cancellationToken)
    {
        MemoryStream? bufferedStream = null;
        try
        {
            var input = stream;
            if (!input.CanSeek)
            {
                bufferedStream = new MemoryStream();
                await input.CopyToAsync(bufferedStream, cancellationToken).ConfigureAwait(false);
                bufferedStream.Position = 0;
                input = bufferedStream;
            }

            // The encoding is detected from the start of the file, then the whole file is read through a TextReader, so
            // the XML reader does not apply the declared encoding itself.
            var start = input.Position;
            var header = new byte[1024];
            var headerLength = await StreamUtilities.ReadUntilCountOrEndAsync(input, header, cancellationToken).ConfigureAwait(false);
            input.Seek(start, SeekOrigin.Begin);
            var (encoding, _) = StreamUtilities.GetXmlEncoding(header.AsSpan(0, headerLength), strict: false);

            using var textReader = new StreamReader(input, encoding, detectEncodingFromByteOrderMarks: true, bufferSize: -1, leaveOpen: true);
            var settings = maxCharactersInDocument == MaxCharactersInDocument ? DefaultXmlSettings : CreateXmlReaderSettings(maxCharactersInDocument);
            using var xmlReader = new DepthLimitingXmlReader(XmlReader.Create(textReader, settings), maxDepth);
            return await XDocument.LoadAsync(xmlReader, loadOptions, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            if (bufferedStream is not null)
            {
                await bufferedStream.DisposeAsync().ConfigureAwait(false);
            }
        }
    }

    public static Task<XDocument?> TryLoadDocumentWithoutClosingStream(Stream stream, CancellationToken cancellationToken)
    {
        return TryLoadDocumentWithoutClosingStream(stream, LoadOptions.SetLineInfo, cancellationToken);
    }

    /// <summary>Loads an XML document like <see cref="LoadDocumentWithoutClosingStreamAsync(Stream, LoadOptions, CancellationToken)"/>, or returns <see langword="null"/> when it is not valid, too deep, or too large.</summary>
    public static async Task<XDocument?> TryLoadDocumentWithoutClosingStream(Stream stream, LoadOptions loadOptions, CancellationToken cancellationToken)
    {
        try
        {
            return await LoadDocumentWithoutClosingStreamAsync(stream, loadOptions, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // An invalid document is not an error: the file is simply not a document this scanner can read
            return null;
        }
    }

    public static string CreateXPath(XElement element)
    {
        var current = element;
        var query = "";
        do
        {
            var index = GetElementIndex(current) + 1;
            query = "/*[" + index.ToString(CultureInfo.InvariantCulture) + "]" + query;
            current = current.Parent;
        }
        while (current is not null);

        return query;
    }

    public static int GetElementIndex(XNode element)
    {
        var index = 0;
        while (element.PreviousNode is not null)
        {
            if (element.PreviousNode.NodeType == XmlNodeType.Element)
            {
                index++;
            }

            element = element.PreviousNode;
        }

        return index;
    }

    /// <summary>Gets the encoding name stated by the XML declaration at the start of <paramref name="bytes"/>, if any.</summary>
    /// <remarks>Only an ASCII-compatible declaration is recognized; a file in UTF-16 or UTF-32 is identified by its BOM.</remarks>
    public static string? GetDeclaredEncodingName(ReadOnlySpan<byte> bytes)
    {
        if (bytes is not [(byte)'<', (byte)'?', (byte)'x', (byte)'m', (byte)'l', (byte)' ' or (byte)'\t' or (byte)'\r' or (byte)'\n', ..])
            return null;

        var end = bytes[..Math.Min(bytes.Length, 1024)].IndexOf("?>"u8);
        if (end < 0)
            return null;

        var declarationText = Encoding.Latin1.GetString(bytes[..(end + 2)]);
        var root = XmlSyntaxTree.ParseText(declarationText).GetRoot();
        return root.Nodes is [XmlDeclarationSyntax declaration, ..] ? declaration.Encoding : null;
    }

    /// <summary>
    /// Decodes XML character data the way <see cref="XmlReader"/> exposes it, which is what <see cref="XElement.Value"/>
    /// and <see cref="XAttribute.Value"/> return, and records where each decoded character comes from in <paramref name="text"/>.
    /// </summary>
    /// <param name="text">The character data as written in the file: the content of an element, or an attribute value without its quotes.</param>
    /// <param name="isAttribute">Whether <paramref name="text"/> is an attribute value, whose whitespace characters are normalized to spaces.</param>
    /// <param name="value">The decoded value.</param>
    /// <param name="sourceOffsets">
    /// For each character of <paramref name="value"/>, the offset in <paramref name="text"/> where the markup that produced
    /// it starts, or -1 when the character is not the first one that markup produced. The extra last item is the length of
    /// <paramref name="text"/>.
    /// </param>
    /// <returns><see langword="false"/> when <paramref name="text"/> holds a reference that cannot be resolved without a DTD, or a malformed one.</returns>
    public static bool TryDecodeCharacterData(string text, bool isAttribute, [NotNullWhen(true)] out string? value, [NotNullWhen(true)] out int[]? sourceOffsets)
    {
        var builder = new StringBuilder(text.Length);
        var offsets = new List<int>(text.Length + 1);
        var index = 0;
        while (index < text.Length)
        {
            switch (text[index])
            {
                case '&':
                    var nameLength = text.AsSpan(index + 1).IndexOf(';');
                    if (nameLength < 0 || !TryResolveReference(text.AsSpan(index + 1, nameLength), out var replacement))
                    {
                        value = null;
                        sourceOffsets = null;
                        return false;
                    }

                    Append(replacement, index);
                    index += nameLength + 2;
                    break;

                // Line breaks are normalized to a line feed (XML 1.0 §2.11), and in an attribute value every whitespace
                // character becomes a space (§3.3.3). A character reference is not normalized.
                case '\r':
                    Append(isAttribute ? " " : "\n", index);
                    index += index + 1 < text.Length && text[index + 1] == '\n' ? 2 : 1;
                    break;

                case '\n' or '\t' when isAttribute:
                    Append(" ", index);
                    index++;
                    break;

                default:
                    offsets.Add(index);
                    builder.Append(text[index]);
                    index++;
                    break;
            }
        }

        offsets.Add(text.Length);
        value = builder.ToString();
        sourceOffsets = [.. offsets];
        return true;

        void Append(string decoded, int sourceOffset)
        {
            builder.Append(decoded);
            offsets.Add(sourceOffset);
            for (var i = 1; i < decoded.Length; i++)
            {
                offsets.Add(-1);
            }
        }
    }

    /// <summary>Escapes <paramref name="value"/> so that XML reads it back unchanged as element content or as an attribute value.</summary>
    /// <param name="value">The value to escape.</param>
    /// <param name="isAttribute">Whether the value is written in an attribute.</param>
    /// <param name="quote">The quote character delimiting the attribute value.</param>
    public static string EscapeCharacterData(string value, bool isAttribute, char quote)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var c in value)
        {
            var replacement = c switch
            {
                '&' => "&amp;",
                '<' => "&lt;",
                '>' => "&gt;",
                '\r' => "&#xD;",
                '"' when isAttribute && quote == '"' => "&quot;",
                '\'' when isAttribute && quote == '\'' => "&apos;",
                '\n' when isAttribute => "&#xA;",
                '\t' when isAttribute => "&#x9;",
                _ => null,
            };

            if (replacement is null)
            {
                builder.Append(c);
            }
            else
            {
                builder.Append(replacement);
            }
        }

        return builder.ToString();
    }

    private static bool TryResolveReference(ReadOnlySpan<char> name, [NotNullWhen(true)] out string? value)
    {
        value = name switch
        {
            "lt" => "<",
            "gt" => ">",
            "amp" => "&",
            "apos" => "'",
            "quot" => "\"",
            ['#', 'x', .. var hex] when hex.Length > 0 && int.TryParse(hex, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var codePoint) && IsXmlCharacter(codePoint) => char.ConvertFromUtf32(codePoint),
            ['#', .. var digits] when digits.Length > 0 && int.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out var codePoint) && IsXmlCharacter(codePoint) => char.ConvertFromUtf32(codePoint),
            _ => null,
        };

        return value is not null;
    }

    private static bool IsXmlCharacter(int value) => value switch
    {
        0x9 or 0xA or 0xD => true,
        >= 0x20 and <= 0xD7FF => true,
        >= 0xE000 and <= 0xFFFD => true,
        >= 0x10000 and <= 0x10FFFF => true,
        _ => false,
    };

    /// <summary>Stops reading a document that nests deeper than a limit, which <see cref="XmlReaderSettings"/> cannot do.</summary>
    private sealed class DepthLimitingXmlReader : XmlReader, IXmlLineInfo
    {
        private readonly XmlReader _reader;
        private readonly int _maxDepth;

        public DepthLimitingXmlReader(XmlReader reader, int maxDepth)
        {
            _reader = reader;
            _maxDepth = maxDepth;
        }

        public override int AttributeCount => _reader.AttributeCount;
        public override string BaseURI => _reader.BaseURI;
        public override int Depth => _reader.Depth;
        public override bool EOF => _reader.EOF;
        public override bool HasValue => _reader.HasValue;
        public override bool IsDefault => _reader.IsDefault;
        public override bool IsEmptyElement => _reader.IsEmptyElement;
        public override string LocalName => _reader.LocalName;
        public override string Name => _reader.Name;
        public override string NamespaceURI => _reader.NamespaceURI;
        public override XmlNameTable NameTable => _reader.NameTable;
        public override XmlNodeType NodeType => _reader.NodeType;
        public override string Prefix => _reader.Prefix;
        public override char QuoteChar => _reader.QuoteChar;
        public override ReadState ReadState => _reader.ReadState;
        public override XmlReaderSettings? Settings => _reader.Settings;
        public override string Value => _reader.Value;
        public override string XmlLang => _reader.XmlLang;
        public override XmlSpace XmlSpace => _reader.XmlSpace;

        int IXmlLineInfo.LineNumber => _reader is IXmlLineInfo lineInfo ? lineInfo.LineNumber : 0;
        int IXmlLineInfo.LinePosition => _reader is IXmlLineInfo lineInfo ? lineInfo.LinePosition : 0;
        bool IXmlLineInfo.HasLineInfo() => _reader is IXmlLineInfo lineInfo && lineInfo.HasLineInfo();

        public override string GetAttribute(int i) => _reader.GetAttribute(i);
        public override string? GetAttribute(string name) => _reader.GetAttribute(name);
        public override string? GetAttribute(string name, string? namespaceURI) => _reader.GetAttribute(name, namespaceURI);
        public override string? LookupNamespace(string prefix) => _reader.LookupNamespace(prefix);
        public override bool MoveToAttribute(string name) => _reader.MoveToAttribute(name);
        public override bool MoveToAttribute(string name, string? ns) => _reader.MoveToAttribute(name, ns);
        public override void MoveToAttribute(int i) => _reader.MoveToAttribute(i);
        public override bool MoveToElement() => _reader.MoveToElement();
        public override bool MoveToFirstAttribute() => _reader.MoveToFirstAttribute();
        public override bool MoveToNextAttribute() => _reader.MoveToNextAttribute();
        public override bool ReadAttributeValue() => _reader.ReadAttributeValue();
        public override void ResolveEntity() => _reader.ResolveEntity();
        public override Task<string> GetValueAsync() => _reader.GetValueAsync();

        public override bool Read()
        {
            var result = _reader.Read();
            EnsureDepth();
            return result;
        }

        public override async Task<bool> ReadAsync()
        {
            var result = await _reader.ReadAsync().ConfigureAwait(false);
            EnsureDepth();
            return result;
        }

        private void EnsureDepth()
        {
            if (_reader.Depth > _maxDepth)
                throw new XmlException($"The document nests deeper than {_maxDepth.ToString(CultureInfo.InvariantCulture)} levels.");
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _reader.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}
