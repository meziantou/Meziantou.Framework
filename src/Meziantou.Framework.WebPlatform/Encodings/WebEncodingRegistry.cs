using System.Collections.Frozen;
using Meziantou.Framework.WebPlatform.Infra;

namespace Meziantou.Framework.WebPlatform.Encodings;

/// <summary>Names, labels and factories of the encodings defined by the WHATWG Encoding Standard.</summary>
internal static partial class WebEncodingRegistry
{
    // Nested class so the map is initialized after the generated label table (static field initializers of partial classes are not ordered across files).
    private static class LabelMap
    {
        public static readonly FrozenDictionary<string, WebEncodingId>.AlternateLookup<ReadOnlySpan<char>> Lookup = Create().GetAlternateLookup<ReadOnlySpan<char>>();

        private static FrozenDictionary<string, WebEncodingId> Create()
        {
            var dictionary = new Dictionary<string, WebEncodingId>(Labels.Length, StringComparer.Ordinal);
            foreach (var (label, encoding) in Labels)
            {
                dictionary.Add(label, encoding);
            }

            return dictionary.ToFrozenDictionary(StringComparer.Ordinal);
        }
    }

    /// <summary>Gets the canonical name of an encoding (e.g. "UTF-8", "windows-1252").</summary>
    public static string GetName(WebEncodingId encoding) => Names[(int)encoding];

    /// <summary>
    /// Implements "get an encoding": https://encoding.spec.whatwg.org/#concept-encoding-get
    /// </summary>
    public static bool TryGetEncoding(ReadOnlySpan<char> label, out WebEncodingId encoding)
    {
        label = InfraAscii.StripLeadingAndTrailingAsciiWhitespace(label);
        if (label.Length is 0 or > MaxLabelLength)
        {
            encoding = default;
            return false;
        }

        Span<char> lowercase = stackalloc char[MaxLabelLength];
        var length = System.Text.Ascii.ToLower(label, lowercase, out var written);
        if (length != System.Buffers.OperationStatus.Done)
        {
            // Non-ASCII characters are never part of a label. Lowercase the ASCII ones only so the lookup fails as expected.
            for (var i = 0; i < label.Length; i++)
            {
                lowercase[i] = InfraAscii.ToAsciiLower(label[i]);
            }

            written = label.Length;
        }

        return LabelMap.Lookup.TryGetValue(lowercase[..written], out encoding);
    }

    /// <summary>
    /// Implements "get an output encoding": https://encoding.spec.whatwg.org/#get-an-output-encoding
    /// </summary>
    public static WebEncodingId GetOutputEncoding(WebEncodingId encoding) => encoding switch
    {
        WebEncodingId.Replacement or WebEncodingId.Utf16BE or WebEncodingId.Utf16LE => WebEncodingId.Utf8,
        _ => encoding,
    };

    /// <summary>
    /// Implements "BOM sniff": https://encoding.spec.whatwg.org/#bom-sniff
    /// Returns the length of the BOM, or 0 when there is none.
    /// </summary>
    public static int SniffBom(ReadOnlySpan<byte> bytes, out WebEncodingId encoding)
    {
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
        {
            encoding = WebEncodingId.Utf8;
            return 3;
        }

        if (bytes.Length >= 2)
        {
            if (bytes[0] == 0xFE && bytes[1] == 0xFF)
            {
                encoding = WebEncodingId.Utf16BE;
                return 2;
            }

            if (bytes[0] == 0xFF && bytes[1] == 0xFE)
            {
                encoding = WebEncodingId.Utf16LE;
                return 2;
            }
        }

        encoding = default;
        return 0;
    }

    /// <summary>Creates a new decoder instance for an encoding.</summary>
    public static WebDecoder CreateDecoder(WebEncodingId encoding, bool fatal = false) => encoding switch
    {
        WebEncodingId.Utf8 => new Utf8Decoder(fatal),
        WebEncodingId.Gbk or WebEncodingId.Gb18030 => new Gb18030Decoder(fatal),
        WebEncodingId.Big5 => new Big5Decoder(fatal),
        WebEncodingId.EucJP => new EucJPDecoder(fatal),
        WebEncodingId.Iso2022JP => new Iso2022JPDecoder(fatal),
        WebEncodingId.ShiftJis => new ShiftJisDecoder(fatal),
        WebEncodingId.EucKR => new EucKRDecoder(fatal),
        WebEncodingId.Replacement => new ReplacementDecoder(fatal),
        WebEncodingId.Utf16BE => new Utf16Decoder(fatal, bigEndian: true),
        WebEncodingId.Utf16LE => new Utf16Decoder(fatal, bigEndian: false),
        WebEncodingId.XUserDefined => new XUserDefinedDecoder(fatal),
        _ => new SingleByteDecoder(fatal, EncodingIndexes.GetSingleByteIndexOffset(encoding)),
    };

    /// <summary>
    /// Implements "get an encoder": https://encoding.spec.whatwg.org/#get-an-encoder
    /// </summary>
    public static WebEncoder CreateEncoder(WebEncodingId encoding) => encoding switch
    {
        WebEncodingId.Utf8 => new Utf8Encoder(),
        WebEncodingId.Gbk => new Gb18030Encoder(isGbk: true),
        WebEncodingId.Gb18030 => new Gb18030Encoder(isGbk: false),
        WebEncodingId.Big5 => new Big5Encoder(),
        WebEncodingId.EucJP => new EucJPEncoder(),
        WebEncodingId.Iso2022JP => new Iso2022JPEncoder(),
        WebEncodingId.ShiftJis => new ShiftJisEncoder(),
        WebEncodingId.EucKR => new EucKREncoder(),
        WebEncodingId.XUserDefined => new XUserDefinedEncoder(),
        WebEncodingId.Replacement or WebEncodingId.Utf16BE or WebEncodingId.Utf16LE => throw new ArgumentException("The encoding has no encoder", nameof(encoding)),
        _ => new SingleByteEncoder(EncodingIndexes.GetSingleByteIndexOffset(encoding)),
    };
}
