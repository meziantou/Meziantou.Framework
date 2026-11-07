using System.Text;
using System.Xml;

namespace Meziantou.Framework.WebPlatform.Xml;

internal static class XmlByteDecoder
{
    public static string Decode(ReadOnlySpan<byte> bytes)
    {
        var encoding = Encoding.UTF8;
        var hasSignature = false;
        var bomLength = 0;
        if (bytes.StartsWith<byte>([0x00, 0x00, 0xFE, 0xFF]))
        {
            encoding = new UTF32Encoding(bigEndian: true, byteOrderMark: true);
            bomLength = 4;
        }
        else if (bytes.StartsWith<byte>([0xFF, 0xFE, 0x00, 0x00]))
        {
            encoding = Encoding.UTF32;
            bomLength = 4;
        }
        else if (bytes.StartsWith<byte>([0xEF, 0xBB, 0xBF]))
        {
            bomLength = 3;
        }
        else if (bytes.StartsWith<byte>([0xFE, 0xFF]))
        {
            encoding = Encoding.BigEndianUnicode;
            bomLength = 2;
        }
        else if (bytes.StartsWith<byte>([0xFF, 0xFE]))
        {
            encoding = Encoding.Unicode;
            bomLength = 2;
        }
        else if (bytes.StartsWith<byte>([0x00, 0x00, 0x00, 0x3C]))
        {
            encoding = new UTF32Encoding(bigEndian: true, byteOrderMark: false);
            hasSignature = true;
        }
        else if (bytes.StartsWith<byte>([0x3C, 0x00, 0x00, 0x00]))
        {
            encoding = Encoding.UTF32;
            hasSignature = true;
        }
        else if (bytes.StartsWith<byte>([0x00, 0x3C, 0x00, 0x3F]))
        {
            encoding = Encoding.BigEndianUnicode;
            hasSignature = true;
        }
        else if (bytes.StartsWith<byte>([0x3C, 0x00, 0x3F, 0x00]))
        {
            encoding = Encoding.Unicode;
            hasSignature = true;
        }

        bytes = bytes[bomLength..];
        try
        {
            // Only decode the declaration before choosing the encoding: the rest may not be valid UTF-8.
            if (bytes.StartsWith(encoding.GetBytes("<?xml")))
            {
                var endMarker = encoding.GetBytes("?>");
                var end = bytes.IndexOf(endMarker);
                if (end >= 0)
                {
                    var declaration = GetStrictEncoding(encoding).GetString(bytes[..(end + endMarker.Length)]);
                    using var reader = XmlReader.Create(new StringReader(declaration + "<root/>"), new XmlReaderSettings { XmlResolver = null });
                    if (reader.Read() && reader.NodeType == XmlNodeType.XmlDeclaration && reader.GetAttribute("encoding") is { } label)
                    {
                        var declaredEncoding = Encoding.GetEncoding(label);
                        // The generic UTF-16/32 labels keep the byte order detected from the input.
                        var matches = declaredEncoding.CodePage == encoding.CodePage ||
                            (label.Equals("utf-16", StringComparison.OrdinalIgnoreCase) && encoding.CodePage is 1200 or 1201) ||
                            (label.Equals("utf-32", StringComparison.OrdinalIgnoreCase) && encoding.CodePage is 12000 or 12001);
                        if ((bomLength > 0 || hasSignature) && !matches)
                            throw new XmlException("The XML encoding declaration conflicts with the byte order mark or byte signature.");

                        if (!matches)
                        {
                            if (declaredEncoding.CodePage is 1200 or 1201 or 12000 or 12001)
                                throw new XmlException("The XML encoding declaration requires a matching byte signature.");

                            encoding = declaredEncoding;
                        }
                    }
                }
            }

            return GetStrictEncoding(encoding).GetString(bytes);
        }
        catch (DecoderFallbackException ex)
        {
            throw new XmlException("The XML input contains an invalid byte sequence for its encoding.", ex);
        }
        catch (ArgumentException ex)
        {
            throw new XmlException("The XML encoding declaration is not supported.", ex);
        }
        catch (NotSupportedException ex)
        {
            throw new XmlException("The XML encoding declaration is not supported.", ex);
        }
    }

    private static Encoding GetStrictEncoding(Encoding encoding) => Encoding.GetEncoding(encoding.CodePage, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
}
