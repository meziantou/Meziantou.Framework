using System.Xml;

namespace Meziantou.Framework.Geo.Internals;

internal static class XmlStreamReader
{
    /// <summary>Creates a reader on an XML document and reads its first node, the XML declaration when there is one.</summary>
    /// <exception cref="FormatException">The start of the document is not valid XML.</exception>
    public static XmlReader Open(Stream stream, XmlReaderSettings settings, string errorMessage)
    {
        // Creating the reader reads the start of the document to detect its encoding, and reading the XML declaration
        // switches to the encoding it declares: both can fail
        XmlReader? reader = null;
        try
        {
            reader = XmlReader.Create(stream, settings);
            reader.Read();
            return reader;
        }
        catch (XmlException ex)
        {
            reader?.Dispose();
            throw new FormatException(errorMessage + ex.Message, ex);
        }
#if !NET11_0_OR_GREATER
        // .NET 10 decodes the declaration of a document without a byte order mark one byte per character, then counts the
        // bytes it read in the declared encoding when switching to it: a byte above 0x7F counts as several bytes, which
        // can point after the bytes read and throw an ArgumentOutOfRangeException. Such a byte is not valid in a declaration.
        catch (ArgumentOutOfRangeException ex)
        {
            reader?.Dispose();
            throw new FormatException(errorMessage + "The XML declaration contains a character that is not valid.", ex);
        }
#endif
    }
}
