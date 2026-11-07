using Meziantou.Framework.WebPlatform.Encodings;
using Meziantou.Framework.WebPlatform.Infra;

namespace Meziantou.Framework.WebPlatform.Html.Parsing;

internal enum EncodingConfidence : byte
{
    Tentative,
    Certain,
    Irrelevant,
}

/// <summary>Determining the character encoding (https://html.spec.whatwg.org/multipage/parsing.html#determining-the-character-encoding).</summary>
internal static class EncodingSniffer
{
    private const int PrescanLength = 1024;
    private const int PrescanChunkLength = 4096;

    /// <summary>
    /// The steps of the encoding sniffing algorithm that only depend on the bytes: the byte order mark, the transport
    /// layer, and the prescan. Returns null when they do not determine the encoding: the caller continues with the
    /// encoding of the container document, then the detection of the encoding (<see cref="EncodingDetector"/>), then the
    /// default encoding (with the confidence tentative).
    /// <paramref name="lateDeclaration"/> is the encoding and the end of the <c>meta</c> element of the head that the prescan
    /// finds after the first 1024 bytes, after bytes that have the same interpretation in all the encodings (see <see cref="CanChangeEncodingOnTheFly"/>):
    /// it changes the encoding when the parser inserts it, so it is ignored when the parser does not reach it in the head
    /// (e.g. after a <c>&lt;plaintext&gt;</c> written by a script, as expected by WPT html/syntax/speculative-parsing).
    /// </summary>
    public static (WebEncodingId? Encoding, EncodingConfidence Confidence) Sniff(ReadOnlySpan<byte> bytes, string? transportEncodingLabel, out (WebEncodingId Encoding, int End)? lateDeclaration)
    {
        TrySniff(bytes, isComplete: true, transportEncodingLabel, out var result, out lateDeclaration);
        return result;
    }

    /// <summary>
    /// <see cref="Sniff"/> for the first bytes of a document whose bytes arrive over time
    /// (https://html.spec.whatwg.org/multipage/parsing.html#encoding-sniffing-algorithm: "the user agent may wait for more
    /// bytes of the resource to be available"). Returns false when the bytes received so far do not determine the result
    /// of the steps: the byte order mark is incomplete, or the prescan has not found a <c>meta</c> element and has not
    /// reached its end (the first 1024 bytes and the end of the head, see <see cref="Prescan"/>). When
    /// <paramref name="isComplete"/> is true, the bytes are the whole document and the method returns true.
    /// </summary>
    public static bool TrySniff(ReadOnlySpan<byte> bytes, bool isComplete, string? transportEncodingLabel, out (WebEncodingId? Encoding, EncodingConfidence Confidence) result, out (WebEncodingId Encoding, int End)? lateDeclaration)
    {
        lateDeclaration = null;
        result = (null, EncodingConfidence.Tentative);
        if (WebEncodingRegistry.SniffBom(bytes, out var bomEncoding) > 0)
        {
            result = (bomEncoding, EncodingConfidence.Certain);
            return true;
        }

        // The first bytes of a byte order mark
        if (!isComplete && bytes.Length < 3 && (((ReadOnlySpan<byte>)[0xEF, 0xBB, 0xBF]).StartsWith(bytes) || ((ReadOnlySpan<byte>)[0xFE, 0xFF]).StartsWith(bytes) || ((ReadOnlySpan<byte>)[0xFF, 0xFE]).StartsWith(bytes)))
            return false;

        if (transportEncodingLabel is not null && WebEncodingRegistry.TryGetEncoding(transportEncodingLabel, out var transportEncoding))
        {
            result = (transportEncoding, EncodingConfidence.Certain);
            return true;
        }

        var prescanned = Prescan(bytes, isComplete, out var isFinal, out var declarationStart, out var declarationEnd);
        if (!isFinal)
            return false;

        if (prescanned is { } encoding)
        {
            if (declarationStart >= PrescanLength && CanChangeEncodingOnTheFly(bytes[..declarationEnd]))
            {
                lateDeclaration = (encoding, declarationEnd);
                return true;
            }

            result = (encoding, EncodingConfidence.Tentative);
        }

        return true;
    }

    /// <summary>
    /// Whether the bytes are the start of an XML declaration that has not ended yet (see <see cref="GetXmlEncoding"/>): as
    /// in the browsers, a document whose bytes arrive over time waits for its end, however long it takes.
    /// </summary>
    public static bool IsIncompleteXmlDeclaration(ReadOnlySpan<byte> bytes) => bytes.Length < 5 ? "<?xml"u8.StartsWith(bytes) : bytes.StartsWith("<?xml"u8) && bytes.IndexOf((byte)'>') < 0;

    /// <summary>
    /// Whether the bytes have the same interpretation in all the ASCII-compatible encodings, one character per byte, so the
    /// parser can change the encoding of the rest of the input when it inserts a <c>meta</c> element
    /// (https://html.spec.whatwg.org/multipage/parsing.html#change-the-encoding, step 5): they are ASCII bytes, except the
    /// ones of the escape sequences of ISO-2022-JP. Such bytes say nothing about the encoding either: they are not given
    /// to the <see cref="EncodingDetector"/>.
    /// </summary>
    public static bool CanChangeEncodingOnTheFly(ReadOnlySpan<byte> bytes) => !bytes.ContainsAnyExceptInRange((byte)0x00, (byte)0x7F) && bytes.IndexOfAny((byte)0x0E, (byte)0x0F, (byte)0x1B) < 0;

    /// <summary>
    /// Prescans a byte stream to determine its encoding, as proposed by https://github.com/whatwg/html/issues/6962 and
    /// implemented by Blink and WebKit (the "prescan a byte stream" algorithm of the HTML Standard, which only looks at the
    /// first 1024 bytes, does not match the browsers): the HTML tokenizer tokenizes the bytes (decoded as an ASCII-compatible
    /// encoding), so that the <c>meta</c> elements in comments, raw text elements (<c>script</c>, <c>style</c>, <c>title</c>,
    /// <c>noscript</c>...) and bogus comments are ignored, and the character references of the attributes are decoded. The
    /// scan continues while the parser would be in the head (only the elements allowed in the head have been seen), and stops
    /// after the first 1024 bytes once another element is seen. When no <c>meta</c> element declares an encoding, the XML
    /// declaration is used. <paramref name="declarationStart"/> and <paramref name="declarationEnd"/> are the positions of the
    /// <c>meta</c> element in the input (0 for the XML declaration). When <paramref name="isComplete"/> is false (the rest of
    /// the input arrives later), <paramref name="isFinal"/> is false when the scan needs more bytes, else true.
    /// </summary>
    private static WebEncodingId? Prescan(ReadOnlySpan<byte> input, bool isComplete, out bool isFinal, out int declarationStart, out int declarationEnd)
    {
        isFinal = true;
        declarationStart = 0;
        declarationEnd = 0;
        if (input.StartsWith("<\0?\0x\0"u8))
            return WebEncodingId.Utf16LE;

        if (input.StartsWith("\0<\0?\0x"u8))
            return WebEncodingId.Utf16BE;

        if (!isComplete && input.Length < 6 && ("<\0?\0x\0"u8.StartsWith(input) || "\0<\0?\0x"u8.StartsWith(input)))
        {
            isFinal = false;
            return null;
        }

        // The bytes are given to the tokenizer by chunks, as the scan usually stops after the head
        var decoded = Math.Min(input.Length, PrescanChunkLength);
        var tokenizer = new HtmlTokenizer(IsomorphicDecode(input[..decoded]), errorHandler: null, new NameTable())
        {
            EofInserted = isComplete && decoded == input.Length,
        };

        var inHead = true;
        while (true)
        {
            var tokenType = tokenizer.NextToken();
            if (tokenType == HtmlTokenType.None)
            {
                // The rest of the input has not arrived yet
                if (decoded == input.Length)
                {
                    isFinal = false;
                    return null;
                }

                var length = Math.Min(input.Length - decoded, Math.Max(decoded, PrescanChunkLength));
                tokenizer.Insert(positionFromEnd: 0, IsomorphicDecode(input.Slice(decoded, length)));
                decoded += length;
                if (isComplete && decoded == input.Length)
                {
                    tokenizer.EofInserted = true;
                }

                continue;
            }

            if (tokenType == HtmlTokenType.EndOfFile)
                break;

            if (tokenType is HtmlTokenType.StartTag or HtmlTokenType.EndTag)
            {
                // The scan stops at the first token after the first 1024 bytes once the parser would not be in the head. The
                // tags are the only tokens whose start matters (the meta elements).
                if (!inHead && tokenizer.TagStartPosition >= PrescanLength)
                    break;

                var tag = tokenizer.Tag!;
                var isEndTag = tokenType == HtmlTokenType.EndTag;
                if (!isEndTag)
                {
                    // The tokenizer state the tree builder would set (scripting is enabled, so noscript is raw text)
                    switch (tag.TagId)
                    {
                        case TagId.Title or TagId.Textarea:
                            tokenizer.State = TokenizerState.Rcdata;
                            break;

                        case TagId.Style or TagId.Xmp or TagId.Iframe or TagId.Noembed or TagId.Noframes or TagId.Noscript:
                            tokenizer.State = TokenizerState.Rawtext;
                            break;

                        case TagId.Script:
                            tokenizer.State = TokenizerState.ScriptData;
                            break;

                        case TagId.Plaintext:
                            tokenizer.State = TokenizerState.Plaintext;
                            break;

                        case TagId.Meta:
                            if (GetMetaEncoding(tag) is { } encoding)
                            {
                                declarationStart = tokenizer.TagStartPosition;
                                declarationEnd = decoded - tokenizer.PositionFromEnd;
                                return encoding;
                            }

                            break;
                    }
                }

                if (tag.TagId is not (TagId.Script or TagId.Noscript or TagId.Style or TagId.Link or TagId.Meta or TagId.Object or TagId.Title or TagId.Base) &&
                    (isEndTag || tag.TagId is not (TagId.Html or TagId.Head)))
                {
                    inHead = false;
                }
            }

            if (!inHead && decoded - tokenizer.PositionFromEnd >= PrescanLength && !tokenizer.HasPendingToken)
                break;
        }

        return GetXmlEncoding(input);

        static string IsomorphicDecode(ReadOnlySpan<byte> bytes)
        {
            return string.Create(bytes.Length, bytes, static (span, bytes) =>
            {
                for (var i = 0; i < bytes.Length; i++)
                {
                    span[i] = (char)bytes[i];
                }
            });
        }
    }

    /// <summary>The encoding declared by the attributes of a <c>meta</c> start tag (the "processing" steps of https://html.spec.whatwg.org/multipage/parsing.html#prescan-a-byte-stream-to-determine-its-encoding).</summary>
    private static WebEncodingId? GetMetaEncoding(HtmlTagToken tag)
    {
        var gotPragma = false;
        bool? needPragma = null;
        WebEncodingId? charset = null;
        var charsetIsFailure = false;

        // The tokenizer drops the duplicate attributes
        foreach (var attribute in tag.Attributes)
        {
            switch (attribute.Name)
            {
                case "http-equiv":
                    if (InfraAscii.EqualsIgnoreAsciiCase(attribute.Value, "content-type"))
                    {
                        gotPragma = true;
                    }

                    break;

                case "content":
                    var extracted = ExtractCharacterEncodingFromMeta(attribute.Value);
                    if (extracted is not null && charset is null && !charsetIsFailure && WebEncodingRegistry.TryGetEncoding(extracted, out var contentEncoding))
                    {
                        charset = contentEncoding;
                        needPragma = true;
                    }

                    break;

                case "charset":
                    if (WebEncodingRegistry.TryGetEncoding(attribute.Value, out var charsetEncoding))
                    {
                        charset = charsetEncoding;
                        charsetIsFailure = false;
                    }
                    else
                    {
                        charset = null;
                        charsetIsFailure = true;
                    }

                    needPragma = false;
                    break;
            }
        }

        if (needPragma is null || (needPragma == true && !gotPragma) || charsetIsFailure || charset is not { } result)
            return null;

        if (result is WebEncodingId.Utf16BE or WebEncodingId.Utf16LE)
            return WebEncodingId.Utf8;

        if (result == WebEncodingId.XUserDefined)
            return WebEncodingId.Windows1252;

        return result;
    }

    /// <summary>
    /// The encoding that a <c>meta</c> element inserted in the head declares
    /// (https://html.spec.whatwg.org/multipage/parsing.html#parsing-main-inhead): its <c>charset</c> attribute, else the
    /// <c>content</c> attribute of a <c>http-equiv="Content-Type"</c> pragma. Returns the encoding given to "change the
    /// encoding" (UTF-16 is UTF-8, and x-user-defined is windows-1252), or null.
    /// </summary>
    public static WebEncodingId? GetMetaElementEncoding(Dom.Element meta)
    {
        if (meta.GetAttributeValue("charset") is not { } label || !WebEncodingRegistry.TryGetEncoding(label, out var encoding))
        {
            label = InfraAscii.EqualsIgnoreAsciiCase(meta.GetAttributeValue("http-equiv") ?? "", "content-type") && meta.GetAttributeValue("content") is { } content ? ExtractCharacterEncodingFromMeta(content) : null;
            if (label is null || !WebEncodingRegistry.TryGetEncoding(label, out encoding))
                return null;
        }

        return encoding switch
        {
            WebEncodingId.Utf16BE or WebEncodingId.Utf16LE => WebEncodingId.Utf8,
            WebEncodingId.XUserDefined => WebEncodingId.Windows1252,
            _ => encoding,
        };
    }

    /// <summary>https://html.spec.whatwg.org/multipage/parsing.html#concept-get-xml-encoding-when-sniffing</summary>
    private static WebEncodingId? GetXmlEncoding(ReadOnlySpan<byte> input)
    {
        if (!input.StartsWith("<?xml"u8))
            return null;

        var xmlDeclarationEnd = input.IndexOf((byte)'>');
        if (xmlDeclarationEnd < 0)
            return null;

        var encodingIndex = input[..xmlDeclarationEnd].IndexOf("encoding"u8);
        if (encodingIndex < 0)
            return null;

        var position = encodingIndex + "encoding".Length;
        while (position < input.Length && input[position] <= 0x20)
        {
            position++;
        }

        if (position >= input.Length || input[position] != '=')
            return null;

        position++;
        while (position < input.Length && input[position] <= 0x20)
        {
            position++;
        }

        if (position >= input.Length)
            return null;

        var quoteMark = input[position];
        if (quoteMark is not ((byte)'"' or (byte)'\''))
            return null;

        position++;
        var end = input[position..].IndexOf(quoteMark);
        if (end < 0)
            return null;

        var potentialEncoding = input.Slice(position, end);
        foreach (var b in potentialEncoding)
        {
            if (b <= 0x20)
                return null;
        }

        // Isomorphic decode
        var label = string.Create(potentialEncoding.Length, potentialEncoding.ToArray(), static (span, bytes) =>
        {
            for (var i = 0; i < bytes.Length; i++)
            {
                span[i] = (char)bytes[i];
            }
        });

        if (!WebEncodingRegistry.TryGetEncoding(label, out var encoding))
            return null;

        return encoding is WebEncodingId.Utf16BE or WebEncodingId.Utf16LE ? WebEncodingId.Utf8 : encoding;
    }

    /// <summary>
    /// The algorithm for extracting a character encoding from a meta element
    /// (https://html.spec.whatwg.org/multipage/urls-and-fetching.html#algorithm-for-extracting-a-character-encoding-from-a-meta-element).
    /// Returns the encoding label, or null.
    /// </summary>
    public static string? ExtractCharacterEncodingFromMeta(string value)
    {
        var position = 0;
        while (true)
        {
            var index = IndexOfIgnoreCase(value, "charset", position);
            if (index < 0)
                return null;

            position = index + "charset".Length;
            while (position < value.Length && InfraAscii.IsAsciiWhitespace(value[position]))
            {
                position++;
            }

            if (position >= value.Length || value[position] != '=')
                continue;

            position++;
            while (position < value.Length && InfraAscii.IsAsciiWhitespace(value[position]))
            {
                position++;
            }

            if (position >= value.Length)
                return null;

            var c = value[position];
            if (c is '"' or '\'')
            {
                var end = value.IndexOf(c, position + 1, StringComparison.Ordinal);
                if (end < 0)
                    return null;

                return value[(position + 1)..end];
            }

            var stop = position;
            while (stop < value.Length && !InfraAscii.IsAsciiWhitespace(value[stop]) && value[stop] != ';')
            {
                stop++;
            }

            return value[position..stop];
        }

        static int IndexOfIgnoreCase(string value, string search, int start)
        {
            for (var i = start; i + search.Length <= value.Length; i++)
            {
                if (InfraAscii.EqualsIgnoreAsciiCase(value.AsSpan(i, search.Length), search))
                    return i;
            }

            return -1;
        }
    }
}
