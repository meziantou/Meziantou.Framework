using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Linq;

namespace Meziantou.Framework.DependencyScanning.Internals
{
    internal static class XmlUtilities
    {
        private static readonly XmlReaderSettings? s_settings = new XmlReaderSettings { CloseInput = false, Async = true, };

        public static Task<XDocument> LoadDocumentWithoutClosingStreamAsync(Stream stream, CancellationToken cancellationToken)
        {
            return LoadDocumentWithoutClosingStreamAsync(stream, LoadOptions.SetLineInfo, cancellationToken);
        }

        public static async Task<XDocument> LoadDocumentWithoutClosingStreamAsync(Stream stream, LoadOptions loadOptions, CancellationToken cancellationToken)
        {
            using var xmlReader = XmlReader.Create(stream, s_settings);
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

                using var xmlReader = XmlReader.Create(stream, s_settings);
                return await XDocument.LoadAsync(xmlReader, loadOptions, cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                return null;
            }
        }

        public static async Task SaveDocumentWithoutClosingStream(Stream stream, XDocument document, CancellationToken cancellationToken)
        {
            var settings = new XmlWriterSettings
            {
                OmitXmlDeclaration = document.Declaration == null,
                CloseOutput = false,
                Async = true,
            };
            using var xmlWriter = XmlWriter.Create(stream, settings);
            await document.SaveAsync(xmlWriter, cancellationToken).ConfigureAwait(false);
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
            while (current != null);

            return query;
        }

        public static int GetElementIndex(XNode element)
        {
            var index = 0;
            while (element.PreviousNode != null)
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
}
