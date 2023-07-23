using System.Globalization;
using System.Xml.Linq;
using System.Xml.XPath;
using Meziantou.Framework.DependencyScanning.Internals;

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
    int ILocationLineInfo.LinePosition => _lineInfo.LinePosition;

    protected internal override async Task UpdateCoreAsync(string? oldValue, string newValue, CancellationToken cancellationToken)
    {
        var stream = FileSystem.OpenReadWrite(FilePath);
        try
        {
            var doc = await XmlUtilities.LoadDocumentWithoutClosingStreamAsync(stream, LoadOptions.PreserveWhitespace, cancellationToken).ConfigureAwait(false);
            var element = doc.XPathSelectElement(XPath);
            if (element == null)
                throw new DependencyScannerException("Dependency not found. File was probably modified since last scan.");

            if (AttributeName != null)
            {
                var attributeName = XName.Get(AttributeName);
                var value = UpdateTextValue(element.Attribute(attributeName)?.Value, oldValue, newValue);
                element.SetAttributeValue(attributeName, value);
            }
            else
            {
                var value = UpdateTextValue(element.Value, oldValue, newValue);
                element.SetValue(value);
            }

            stream.SetLength(0);
            await XmlUtilities.SaveDocumentWithoutClosingStream(stream, doc, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            await stream.DisposeAsync().ConfigureAwait(false);
        }
    }

    public override string ToString()
    {
        if (AttributeName == null)
        {
            return string.Create(CultureInfo.InvariantCulture, $"{FilePath}:{XPath}:{_lineInfo}");
        }

        return string.Create(CultureInfo.InvariantCulture, $"{FilePath}:{XPath}/@{AttributeName}:{_lineInfo}");
    }

    private string UpdateTextValue(string? currentValue, string? oldValue, string newValue)
    {
        if (StartPosition < 0)
        {
            if (oldValue != null && currentValue != oldValue)
                throw new DependencyScannerException($"Expected value '{oldValue}' does not match the current value '{currentValue}'. The file was probably modified since last scan.");

            return newValue;
        }

        if (oldValue != null)
        {
            var slicedCurrentValue = currentValue.AsSpan().Slice(StartPosition, Length);
            if(!slicedCurrentValue.Equals(oldValue, StringComparison.Ordinal))
                throw new DependencyScannerException($"Expected value '{oldValue}' does not match the current value '{slicedCurrentValue.ToString()}'. The file was probably modified since last scan.");
        }

        return currentValue
            .Remove(StartPosition, Length)
            .Insert(StartPosition, newValue);


    }
}
