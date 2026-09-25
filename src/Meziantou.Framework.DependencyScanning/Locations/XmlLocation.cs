using System.Xml.Linq;
using Meziantou.Framework.DependencyScanning.Internals;
using Meziantou.Framework.Language;
using Meziantou.Framework.Language.Xml;

namespace Meziantou.Framework.DependencyScanning;

/// <summary>The location of a value in an XML file: the value of an element or of an attribute, or a part of it.</summary>
/// <remarks>
/// The column and length are offsets in the value as <see cref="XElement.Value"/> or <see cref="XAttribute.Value"/>
/// exposes it: line breaks normalized, references resolved, and, for an attribute, whitespace normalized to spaces.
/// An update maps them back onto the text written in the file.
/// </remarks>
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
        _lineInfo = lineInfo == default ? default : new LineInfo(lineInfo.LineNumber, lineInfo.LinePosition + Math.Max(column, 0));
        AttributeName = attribute?.Name.LocalName;
        StartPosition = column;
        Length = length;
    }

    public string XPath { get; }
    public string? AttributeName { get; }

    public int StartPosition { get; set; } = -1;
    public int Length { get; private set; } = -1;

    public override bool IsUpdatable => CanWriteFile;

    private protected override bool TracksWrittenValue => true;
    int ILocationLineInfo.LineNumber => _lineInfo.LineNumber;
    int ILocationLineInfo.LinePosition => _lineInfo.LinePosition;

    protected internal override async Task UpdateCoreAsync(string? oldValue, string newValue, CancellationToken cancellationToken)
    {
        if (oldValue is null)
            throw new DependencyScannerException("The value expected at the location is unknown, so the file cannot be updated safely. Provide the expected value.");

        var stream = FileSystem.OpenReadWrite(FilePath);
        try
        {
            var file = await StreamUtilities.ReadForUpdateAsync(stream, isXml: true, cancellationToken).ConfigureAwait(false);
            var (updatedContent, valueStart) = ReplaceValue(file.Text, oldValue, newValue);
            await StreamUtilities.WriteForUpdateAsync(stream, file, updatedContent, cancellationToken).ConfigureAwait(false);

            if (StartPosition >= 0)
            {
                StartPosition = valueStart;
                Length = newValue.Length;
            }

            SetCurrentValue(newValue);
            NotifyValueReplaced(valueStart, oldValue.Length, newValue.Length);
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

    private (string Text, int ValueStart) ReplaceValue(string text, string oldValue, string newValue)
    {
        var root = XmlSyntaxTree.ParseText(text).GetRoot();
        var locationXPath = AttributeName is null ? XPath : $"{XPath}/@{AttributeName}";
        var node = root.SelectSingleSyntaxNode(locationXPath) ?? throw new DependencyScannerException("Dependency not found. File was probably modified since last scan.");
        var (sourceStart, sourceEnd, isAttribute, quote) = node switch
        {
            XmlAttributeSyntax attribute => (attribute.ValueToken.SpanStart, attribute.ValueToken.Span.End, true, attribute.StartQuoteToken.Text is ['\''] ? '\'' : '"'),
            XmlElementSyntax element => GetElementContentSpan(text, element),

            // A self-closing element has no content to carry a value.
            XmlEmptyElementSyntax => throw new DependencyScannerException("Cannot update value of a self-closing XML element."),
            _ => throw new DependencyScannerException("Dependency not found. File was probably modified since last scan."),
        };

        var sourceText = text[sourceStart..sourceEnd];
        if (!XmlUtilities.TryDecodeCharacterData(sourceText, isAttribute, out var currentValue, out var sourceOffsets))
            throw new DependencyScannerException($"The value '{sourceText}' contains an entity reference that cannot be resolved. The location cannot be mapped onto the file.");

        int start;
        if (StartPosition < 0)
        {
            if (!string.Equals(currentValue, oldValue, StringComparison.Ordinal))
                throw new DependencyScannerException($"Expected value '{oldValue}' does not match the current value '{currentValue}'. The file was probably modified since last scan.");

            start = 0;
        }
        else
        {
            start = FindOldValue(currentValue, oldValue);
        }

        var replaceStart = sourceOffsets[start];
        var replaceEnd = sourceOffsets[start + oldValue.Length];
        if (replaceStart < 0 || replaceEnd < 0)
            throw new DependencyScannerException($"The recorded location splits a character reference in '{sourceText}'. The location cannot be mapped onto the file.");

        replaceStart += sourceStart;
        replaceEnd += sourceStart;
        var escapedValue = XmlUtilities.EscapeCharacterData(newValue, isAttribute, quote);

        // A line feed written right after a carriage return would merge with it into a single line break
        if (!isAttribute && escapedValue is ['\n', ..] && replaceStart > 0 && text[replaceStart - 1] == '\r')
        {
            escapedValue = string.Concat("&#xA;", escapedValue.AsSpan(1));
        }

        return (string.Concat(text.AsSpan(0, replaceStart), escapedValue, text.AsSpan(replaceEnd)), start);
    }

    private protected override void OnSiblingValueReplaced(Location sibling, int start, int oldLength, int newLength)
    {
        if (sibling is XmlLocation other && StartPosition >= start + oldLength &&
            string.Equals(other.XPath, XPath, StringComparison.Ordinal) &&
            string.Equals(other.AttributeName, AttributeName, StringComparison.Ordinal) &&
            string.Equals(other.FilePath, FilePath, StringComparison.Ordinal))
        {
            StartPosition += newLength - oldLength;
        }
    }

    private int FindOldValue(string currentValue, string oldValue)
    {
        if (StartPosition <= currentValue.Length && oldValue.Length <= currentValue.Length - StartPosition &&
            currentValue.AsSpan(StartPosition, oldValue.Length).Equals(oldValue, StringComparison.Ordinal))
        {
            return StartPosition;
        }

        // Several locations can share one value, such as the name and the version of Sdk="Name/1.0". Updating one of
        // them moves the ones after it, so the value is searched again, but only where it can have moved to: anywhere
        // after the recorded start when the value grew, or at its end when the value shrank. Anything before that window
        // belongs to another part of the value, and the value is only replaced when it occurs exactly once in the window.
        if (oldValue.Length > 0 && oldValue.Length <= currentValue.Length)
        {
            var searchStart = Math.Max(0, Math.Min(StartPosition, currentValue.Length - oldValue.Length));
            var index = currentValue.IndexOf(oldValue, searchStart, StringComparison.Ordinal);
            if (index >= 0 && currentValue.IndexOf(oldValue, index + 1, StringComparison.Ordinal) < 0)
                return index;
        }

        throw new DependencyScannerException($"Expected value '{oldValue}' was not found at the recorded location in the current value '{currentValue}'. The file was probably modified since last scan.");
    }

    private static (int Start, int End, bool IsAttribute, char Quote) GetElementContentSpan(string text, XmlElementSyntax element)
    {
        foreach (var child in element.Content)
        {
            // The value of an element holding anything but text does not map onto a single run of text: comments and
            // processing instructions are not part of it, and a CDATA section or a child element has its own escaping.
            if (child is not XmlTextSyntax)
                throw new DependencyScannerException($"Cannot update the value of the XML element '{element.Name}' because it contains child elements, comments, CDATA sections or processing instructions.");
        }

        if (element.EndTag is not { } endTag)
            throw new DependencyScannerException($"Cannot update the value of the XML element '{element.Name}' because it has no end tag.");

        var start = element.StartTag.FullSpan.End;
        var end = endTag.FullSpan.Start;
        if (end < start || !text.AsSpan(start, end - start).Equals(element.GetInnerText(), StringComparison.Ordinal))
            throw new DependencyScannerException($"Cannot locate the content of the XML element '{element.Name}'.");

        return (start, end, false, '"');
    }
}
