using System.Xml;
using System.Xml.Linq;
using Meziantou.Framework.Language.Xml;

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
}
