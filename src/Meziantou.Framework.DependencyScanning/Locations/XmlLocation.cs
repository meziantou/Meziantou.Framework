using System.Xml.Linq;
using System.Xml.XPath;
using Meziantou.Framework.DependencyScanning.Internals;

namespace Meziantou.Framework.DependencyScanning;

internal class XmlLocation : Location, ILocationLineInfo
{
    private readonly LineInfo _lineInfo;

    public XmlLocation(string filePath, XElement element)
        : this(filePath, element, attributeName: null)
    {
    }

    public XmlLocation(string filePath, XElement element, string? attributeName)
        : base(filePath)
    {
        XPath = XmlUtilities.CreateXPath(element);
        _lineInfo = LineInfo.FromXElement(element);
        AttributeName = attributeName;
    }

    public XmlLocation(string filePath, XElement element, int column, int length)
        : this(filePath, element, attributeName: null, column, length)
    {
    }

    public XmlLocation(string filePath, XElement element, string? attributeName, int column, int length)
        : base(filePath)
    {
        XPath = XmlUtilities.CreateXPath(element);
        _lineInfo = LineInfo.FromXElement(element);
        AttributeName = attributeName;
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

    protected internal override async Task UpdateAsync(Stream stream, string newVersion, CancellationToken cancellationToken)
    {
        var doc = await XmlUtilities.LoadDocumentWithoutClosingStreamAsync(stream, LoadOptions.PreserveWhitespace, cancellationToken).ConfigureAwait(false);
        foreach (var element in doc.XPathSelectElements(XPath))
        {
            if (AttributeName != null)
            {
                var attributeName = XName.Get(AttributeName);
                var value = UpdateTextValue(element.Attribute(attributeName)?.Value, newVersion);
                element.SetAttributeValue(attributeName, value);
            }
            else
            {
                var value = UpdateTextValue(element.Value, newVersion);
                element.SetValue(value);
            }

            stream.SetLength(0);
            await XmlUtilities.SaveDocumentWithoutClosingStream(stream, doc, cancellationToken).ConfigureAwait(false);
        }
    }

    public override string ToString()
    {
        if (AttributeName == null)
        {
            return FormattableString.Invariant($"{FilePath}:{XPath}:{_lineInfo}");
        }

        return FormattableString.Invariant($"{FilePath}:{XPath}/@{AttributeName}:{_lineInfo}");
    }

    private string UpdateTextValue(string? value, string version)
    {
        if (value == null || StartPosition < 0)
            return version;

        return value
            .Remove(StartPosition, Length)
            .Insert(StartPosition, version);
    }
}
