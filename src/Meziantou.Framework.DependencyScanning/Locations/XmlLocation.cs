using System.Text;
using System.Xml.Linq;
using Meziantou.Framework.DependencyScanning.Internals;
using Meziantou.Framework.Language.Xml;

namespace Meziantou.Framework.DependencyScanning;

internal class XmlLocation : Location, ILocationLineInfo
{
    private readonly LineInfo _lineInfo;

    public XmlLocation(IFileSystem fileSystem, string filePath, XElement element)
        : this(fileSystem, filePath, element, attribute: null)
    {
    }

    public XmlLocation(IFileSystem fileSystem, string filePath, XElement element, XAttribute? attribute)
        : base(fileSystem, filePath)
    {
        XPath = XmlUtilities.CreateXPath(element);
        _lineInfo = LineInfo.FromXObject((XObject?)attribute ?? element);
        AttributeName = attribute?.Name.LocalName;
    }

    public XmlLocation(IFileSystem fileSystem, string filePath, XElement element, int column, int length)
        : this(fileSystem, filePath, element, attribute: null, column, length)
    {
    }

    public XmlLocation(IFileSystem fileSystem, string filePath, XElement element, XAttribute? attribute, int column, int length)
        : base(fileSystem, filePath)
    {
        XPath = XmlUtilities.CreateXPath(element);
        var lineInfo = LineInfo.FromXObject((XObject?)attribute ?? element);
        _lineInfo = column == 0 && lineInfo != default ? lineInfo : new LineInfo(lineInfo.LineNumber, lineInfo.LinePosition + column);
        AttributeName = attribute?.Name.LocalName;
        StartPosition = column;
        Length = length;
    }

    public string XPath { get; }
    public string? AttributeName { get; }

    public int StartPosition { get; set; } = -1;
    public int Length { get; } = -1;

    public override bool IsUpdatable => true;
    int ILocationLineInfo.LineNumber => _lineInfo.LineNumber;
    int ILocationLineInfo.LinePosition => _lineInfo.LinePosition + Math.Clamp(StartPosition, 0, int.MaxValue);

    protected internal override async Task UpdateCoreAsync(string? oldValue, string newValue, CancellationToken cancellationToken)
    {
        var stream = FileSystem.OpenReadWrite(FilePath);
        try
        {
            string content;
            Encoding encoding;
            using (var reader = await StreamUtilities.CreateReaderAsync(stream, cancellationToken).ConfigureAwait(false))
            {
                content = await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
                encoding = reader.CurrentEncoding;
            }

            var syntaxTree = XmlSyntaxTree.ParseText(content);
            var locationXPath = AttributeName is null ? XPath : $"{XPath}/@{AttributeName}";
            var updatedRoot = ReplaceValue(syntaxTree, locationXPath, oldValue, newValue);
            var updatedContent = updatedRoot.ToFullString();

            stream.SetLength(0);
            stream.Seek(0, SeekOrigin.Begin);

            await using var writer = StreamUtilities.CreateWriter(stream, encoding);
            await writer.WriteAsync(updatedContent.AsMemory(), cancellationToken).ConfigureAwait(false);
            await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            await stream.DisposeAsync().ConfigureAwait(false);
        }
    }

    public override string ToString()
    {
        if (AttributeName is null)
        {
            return string.Create(CultureInfo.InvariantCulture, $"{FilePath}:{XPath}:{_lineInfo}");
        }

        return string.Create(CultureInfo.InvariantCulture, $"{FilePath}:{XPath}/@{AttributeName}:{_lineInfo}");
    }

    private string UpdateTextValue(string? currentValue, string? oldValue, string newValue)
    {
        if (StartPosition < 0)
        {
            if (oldValue is not null && currentValue != oldValue)
                throw new DependencyScannerException($"Expected value '{oldValue}' does not match the current value '{currentValue}'. The file was probably modified since last scan.");

            return newValue;
        }

        if (oldValue is not null)
        {
            var slicedCurrentValue = currentValue.AsSpan().Slice(StartPosition, Length);
            if (!slicedCurrentValue.Equals(oldValue, StringComparison.Ordinal))
                throw new DependencyScannerException($"Expected value '{oldValue}' does not match the current value '{slicedCurrentValue}'. The file was probably modified since last scan.");
        }

        if (currentValue is null)
            throw new DependencyScannerException("Current value is null. The file was probably modified since last scan.");

        return currentValue
            .Remove(StartPosition, Length)
            .Insert(StartPosition, newValue);
    }

    private XmlDocumentSyntax ReplaceValue(XmlSyntaxTree syntaxTree, string locationXPath, string? oldValue, string newValue)
    {
        var node = syntaxTree.Root.SelectSingleSyntaxNode(locationXPath) ?? throw new DependencyScannerException("Dependency not found. File was probably modified since last scan.");
        return node switch
        {
            XmlAttributeSyntax attribute => ReplaceAttributeValue(syntaxTree.Root, attribute, oldValue, newValue),
            XmlElementSyntax element => ReplaceElementValue(syntaxTree.Root, element, oldValue, newValue),
            _ => throw new DependencyScannerException("Dependency not found. File was probably modified since last scan."),
        };
    }

    private XmlDocumentSyntax ReplaceAttributeValue(XmlDocumentSyntax document, XmlAttributeSyntax attribute, string? oldValue, string newValue)
    {
        var updatedValue = UpdateTextValue(attribute.Value, oldValue, newValue);
        return document.ReplaceNode(attribute, attribute.WithValue(updatedValue));
    }

    private XmlDocumentSyntax ReplaceElementValue(XmlDocumentSyntax document, XmlElementSyntax element, string? oldValue, string newValue)
    {
        if (element.IsSelfClosing)
            throw new DependencyScannerException("Cannot update value of a self-closing XML element.");

        var updatedValue = UpdateTextValue(element.GetInnerText(), oldValue, newValue);
        return document.ReplaceNode(element, element.WithInnerText(updatedValue));
    }
}
