using System.Xml;
using System.Xml.Linq;

namespace Meziantou.Framework.DependencyScanning.Internals;

internal static class XmlUtilities
{
    private static readonly XmlReaderSettings? XmlSettings = new() { CloseInput = false, Async = true, };

    public static Task<XDocument> LoadDocumentWithoutClosingStreamAsync(Stream stream, CancellationToken cancellationToken)
    {
        return LoadDocumentWithoutClosingStreamAsync(stream, LoadOptions.SetLineInfo, cancellationToken);
    }

    public static async Task<XDocument> LoadDocumentWithoutClosingStreamAsync(Stream stream, LoadOptions loadOptions, CancellationToken cancellationToken)
    {
        using var xmlReader = XmlReader.Create(stream, XmlSettings);
        return await XDocument.LoadAsync(xmlReader, loadOptions, cancellationToken).ConfigureAwait(false);
    }

    public static Task<XDocument?> TryLoadDocumentWithoutClosingStream(Stream stream, CancellationToken cancellationToken)
    {
        return TryLoadDocumentWithoutClosingStream(stream, LoadOptions.SetLineInfo, cancellationToken);
    }

    public static async Task<XDocument?> TryLoadDocumentWithoutClosingStream(Stream stream, LoadOptions loadOptions, CancellationToken cancellationToken)
    {
        try
        {
            using var xmlReader = XmlReader.Create(stream, XmlSettings);
            return await XDocument.LoadAsync(xmlReader, loadOptions, cancellationToken).ConfigureAwait(false);
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
}
