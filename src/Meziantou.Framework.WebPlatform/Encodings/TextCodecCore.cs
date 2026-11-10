using System.Text;
using Meziantou.Framework.WebPlatform.Infra;

namespace Meziantou.Framework.WebPlatform.Encodings;

// The algorithms of the TextDecoder and TextEncoder classes (https://encoding.spec.whatwg.org/#api), without the bindings:
// the public classes of WebPlatform and the web APIs of Meziantou.Framework.JavaScriptEngine.WebApis (which links this
// file) wrap them. The failures are reported to the caller, which throws its own exception type.

/// <summary>The state of a <c>TextDecoder</c> object (https://encoding.spec.whatwg.org/#interface-textdecoder).</summary>
internal sealed class TextDecoderCore
{
    private readonly WebEncodingId _encoding;
    private readonly WebDecoder _decoder;
    private bool _doNotFlush;
    private bool _bomSeen;

    private TextDecoderCore(WebEncodingId encoding, bool fatal, bool ignoreBom)
    {
        _encoding = encoding;
        Fatal = fatal;
        IgnoreBom = ignoreBom;
        _decoder = WebEncodingRegistry.CreateDecoder(encoding, fatal);
    }

    /// <summary>Creates a decoder for the encoding identified by the label, or returns null when the label is not supported.</summary>
    public static TextDecoderCore? TryCreate(string label, bool fatal, bool ignoreBom)
    {
        if (!WebEncodingRegistry.TryGetEncoding(label, out var encoding) || encoding == WebEncodingId.Replacement)
            return null;

        return new TextDecoderCore(encoding, fatal, ignoreBom);
    }

    /// <summary>Gets the encoding's name, ASCII lowercased.</summary>
    public string Encoding => InfraAscii.ToAsciiLowercase(WebEncodingRegistry.GetName(_encoding));

    public bool Fatal { get; }

    public bool IgnoreBom { get; }

    /// <summary>
    /// Decodes the bytes (https://encoding.spec.whatwg.org/#dom-textdecoder-decode); returns false when the decoder is
    /// fatal and the data is not valid.
    /// </summary>
    public bool TryDecode(ReadOnlySpan<byte> input, bool stream, out string result)
    {
        if (!_doNotFlush)
        {
            _decoder.Reset();
            _bomSeen = false;
        }

        _doNotFlush = stream;

        var builder = new StringBuilder(input.Length);
        if (!_decoder.DecodeTo(input, builder, flush: !_doNotFlush))
        {
            // The do not flush flag is kept: in stream mode, the next call continues with the state of the decoder (e.g. the
            // mode of ISO-2022-JP, https://encoding.spec.whatwg.org/#dom-textdecoder-decode)
            result = "";
            return false;
        }

        result = SerializeIOQueue(builder);
        return true;
    }

    /// <summary>https://encoding.spec.whatwg.org/#concept-td-serialize</summary>
    private string SerializeIOQueue(StringBuilder output)
    {
        if (output.Length > 0 && !IgnoreBom && !_bomSeen && _encoding is WebEncodingId.Utf8 or WebEncodingId.Utf16BE or WebEncodingId.Utf16LE)
        {
            _bomSeen = true;
            if (output[0] == '\U0000FEFF')
            {
                output.Remove(0, 1);
            }
        }

        return output.ToString();
    }
}

/// <summary>The algorithms of the <c>TextEncoder</c> class (https://encoding.spec.whatwg.org/#interface-textencoder).</summary>
internal static class TextEncoderCore
{
    /// <summary>Returns the UTF-8 bytes of the string (https://encoding.spec.whatwg.org/#dom-textencoder-encode).</summary>
    public static byte[] Encode(string input)
    {
        // Converting to scalar values replaces lone surrogates with U+FFFD, which is what the UTF-8 encoding of the BCL does.
        return System.Text.Encoding.UTF8.GetBytes(input);
    }

    /// <summary>
    /// Encodes the string as UTF-8 into the destination (https://encoding.spec.whatwg.org/#dom-textencoder-encodeinto):
    /// <paramref name="read"/> is the number of UTF-16 code units read and <paramref name="written"/> the number of bytes written.
    /// </summary>
    public static void EncodeInto(string source, Span<byte> destination, out ulong read, out ulong written)
    {
        read = 0;
        var bytesWrittenTotal = 0;
        foreach (var rune in source.EnumerateRunes())
        {
            // Lone surrogates are converted to U+FFFD by EnumerateRunes (USVString conversion).
            if (!rune.TryEncodeToUtf8(destination[bytesWrittenTotal..], out var bytesWritten))
                break;

            bytesWrittenTotal += bytesWritten;
            read += (ulong)(rune.Value >= 0x10000 ? 2 : 1);
        }

        written = (ulong)bytesWrittenTotal;
    }
}
