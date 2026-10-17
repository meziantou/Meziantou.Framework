using System.Text;
using Meziantou.Framework.WebPlatform.Encodings;
using Meziantou.Framework.WebPlatform.Infra;

namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>Represents the options of the <c>TextDecoder</c> constructor.</summary>
[IdlInterface("TextDecoderOptions", "https://encoding.spec.whatwg.org/#textdecoderoptions")]
public sealed class TextDecoderOptions
{
    /// <summary>
    /// Gets or sets a value indicating whether decoding throws on invalid data instead of using replacement characters.
    /// </summary>
    [IdlMember("fatal", "https://encoding.spec.whatwg.org/#dom-textdecoderoptions-fatal")]
    public bool Fatal { get; init; }

    /// <summary>Gets or sets a value indicating whether the byte order mark is kept in the decoded output.</summary>
    [IdlMember("ignoreBOM", "https://encoding.spec.whatwg.org/#dom-textdecoderoptions-ignorebom")]
    public bool IgnoreBom { get; init; }
}

/// <summary>Represents the options of <c>TextDecoder.decode()</c>.</summary>
[IdlInterface("TextDecodeOptions", "https://encoding.spec.whatwg.org/#textdecodeoptions")]
public sealed class TextDecodeOptions
{
    /// <summary>Gets or sets a value indicating whether more chunks of data will follow in later calls.</summary>
    [IdlMember("stream", "https://encoding.spec.whatwg.org/#dom-textdecodeoptions-stream")]
    public bool Stream { get; init; }
}

/// <summary>https://encoding.spec.whatwg.org/#interface-textdecoder</summary>
[IdlInterface("TextDecoder", "https://encoding.spec.whatwg.org/#textdecoder")]
public sealed class TextDecoder
{
    private readonly WebEncodingId _encoding;
    private readonly WebDecoder _decoder;
    private bool _doNotFlush;
    private bool _bomSeen;

    /// <summary>Creates a decoder for the encoding identified by the given label.</summary>
    [IdlMember("constructor", "https://encoding.spec.whatwg.org/#dom-textdecoder")]
    public TextDecoder(string label = "utf-8", TextDecoderOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(label);
        if (!WebEncodingRegistry.TryGetEncoding(label, out var encoding) || encoding == WebEncodingId.Replacement)
            throw new RangeErrorException($"The encoding label '{label}' is not supported");

        _encoding = encoding;
        Fatal = options?.Fatal ?? false;
        IgnoreBom = options?.IgnoreBom ?? false;
        _decoder = WebEncodingRegistry.CreateDecoder(encoding, Fatal);
    }

    /// <summary>Gets the encoding's name, ASCII lowercased.</summary>
    [IdlMember("encoding", "https://encoding.spec.whatwg.org/#dom-textdecoder-encoding")]
    public string Encoding => InfraAscii.ToAsciiLowercase(WebEncodingRegistry.GetName(_encoding));

    /// <summary>
    /// Gets a value indicating whether decoding throws on invalid data instead of using replacement characters.
    /// </summary>
    [IdlMember("fatal", "https://encoding.spec.whatwg.org/#dom-textdecoder-fatal")]
    public bool Fatal { get; }

    /// <summary>Gets a value indicating whether the byte order mark is kept in the decoded output.</summary>
    [IdlMember("ignoreBOM", "https://encoding.spec.whatwg.org/#dom-textdecoder-ignorebom")]
    public bool IgnoreBom { get; }

    /// <summary>Decodes the bytes (https://encoding.spec.whatwg.org/#dom-textdecoder-decode): a missing input is an empty input.</summary>
    [IdlMember("decode", "https://encoding.spec.whatwg.org/#dom-textdecoder-decode")]
    public string Decode(ReadOnlySpan<byte> input = default, TextDecodeOptions? options = null)
    {
        if (!_doNotFlush)
        {
            _decoder.Reset();
            _bomSeen = false;
        }

        _doNotFlush = options?.Stream ?? false;

        var builder = new StringBuilder(input.Length);
        if (!_decoder.DecodeTo(input, builder, flush: !_doNotFlush))
        {
            // The do not flush flag is kept: in stream mode, the next call continues with the state of the decoder (e.g. the
            // mode of ISO-2022-JP, https://encoding.spec.whatwg.org/#dom-textdecoder-decode)
            throw new TypeErrorException("The encoded data is not valid");
        }

        return SerializeIOQueue(builder);
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

/// <summary>Represents the result of <c>TextEncoder.encodeInto()</c>.</summary>
[IdlInterface("TextEncoderEncodeIntoResult", "https://encoding.spec.whatwg.org/#dictdef-textencoderencodeintoresult")]
[System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Auto)]
public readonly struct TextEncoderEncodeIntoResult
{
    internal TextEncoderEncodeIntoResult(ulong read, ulong written)
    {
        Read = read;
        Written = written;
    }

    /// <summary>Gets the number of UTF-16 code units of the source that were read.</summary>
    [IdlMember("read", "https://encoding.spec.whatwg.org/#dom-textencoderencodeintoresult-read")]
    public ulong Read { get; }

    /// <summary>Gets the number of bytes that were written to the destination.</summary>
    [IdlMember("written", "https://encoding.spec.whatwg.org/#dom-textencoderencodeintoresult-written")]
    public ulong Written { get; }
}

/// <summary>https://encoding.spec.whatwg.org/#interface-textencoder</summary>
[IdlInterface("TextEncoder", "https://encoding.spec.whatwg.org/#textencoder")]
public sealed class TextEncoder
{
    /// <summary>Creates a UTF-8 encoder.</summary>
    [IdlMember("constructor", "https://encoding.spec.whatwg.org/#dom-textencoder")]
    public TextEncoder()
    {
    }

    /// <summary>Gets the name of the encoding, which is always <c>utf-8</c>.</summary>
    [IdlMember("encoding", "https://encoding.spec.whatwg.org/#dom-textencoder-encoding")]
    public string Encoding => "utf-8";

    /// <summary>Returns the UTF-8 bytes of the given string.</summary>
    [IdlMember("encode", "https://encoding.spec.whatwg.org/#dom-textencoder-encode")]
    public byte[] Encode(string input = "")
    {
        ArgumentNullException.ThrowIfNull(input);

        // Converting to scalar values replaces lone surrogates with U+FFFD, which is what the UTF-8 encoding of the BCL does.
        return System.Text.Encoding.UTF8.GetBytes(input);
    }

    /// <summary>
    /// Encodes the given string as UTF-8 into the destination and returns the number of units read and bytes written.
    /// </summary>
    [IdlMember("encodeInto", "https://encoding.spec.whatwg.org/#dom-textencoder-encodeinto")]
    public TextEncoderEncodeIntoResult EncodeInto(string source, Span<byte> destination)
    {
        ArgumentNullException.ThrowIfNull(source);

        ulong read = 0;
        var written = 0;
        foreach (var rune in source.EnumerateRunes())
        {
            // Lone surrogates are converted to U+FFFD by EnumerateRunes (USVString conversion).
            if (!rune.TryEncodeToUtf8(destination[written..], out var bytesWritten))
                break;

            written += bytesWritten;
            read += (ulong)(rune.Value >= 0x10000 ? 2 : 1);
        }

        return new TextEncoderEncodeIntoResult(read, (ulong)written);
    }
}
