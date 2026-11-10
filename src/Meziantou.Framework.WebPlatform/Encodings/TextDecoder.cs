using Meziantou.Framework.WebPlatform.Encodings;

namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>Represents the options of the <c>TextDecoder</c> constructor.</summary>
/// <seealso href="https://encoding.spec.whatwg.org/#textdecoderoptions"/>
[IdlInterface("TextDecoderOptions", "https://encoding.spec.whatwg.org/#textdecoderoptions")]
public sealed class TextDecoderOptions
{
    /// <summary>
    /// Gets or sets a value indicating whether decoding throws on invalid data instead of using replacement characters.
    /// </summary>
    /// <seealso href="https://encoding.spec.whatwg.org/#dom-textdecoderoptions-fatal"/>
    [IdlMember("fatal", "https://encoding.spec.whatwg.org/#dom-textdecoderoptions-fatal")]
    public bool Fatal { get; init; }

    /// <summary>Gets or sets a value indicating whether the byte order mark is kept in the decoded output.</summary>
    /// <seealso href="https://encoding.spec.whatwg.org/#dom-textdecoderoptions-ignorebom"/>
    [IdlMember("ignoreBOM", "https://encoding.spec.whatwg.org/#dom-textdecoderoptions-ignorebom")]
    public bool IgnoreBom { get; init; }
}

/// <summary>Represents the options of <c>TextDecoder.decode()</c>.</summary>
/// <seealso href="https://encoding.spec.whatwg.org/#textdecodeoptions"/>
[IdlInterface("TextDecodeOptions", "https://encoding.spec.whatwg.org/#textdecodeoptions")]
public sealed class TextDecodeOptions
{
    /// <summary>Gets or sets a value indicating whether more chunks of data will follow in later calls.</summary>
    /// <seealso href="https://encoding.spec.whatwg.org/#dom-textdecodeoptions-stream"/>
    [IdlMember("stream", "https://encoding.spec.whatwg.org/#dom-textdecodeoptions-stream")]
    public bool Stream { get; init; }
}

/// <summary>https://encoding.spec.whatwg.org/#interface-textdecoder</summary>
/// <seealso href="https://encoding.spec.whatwg.org/#textdecoder"/>
[IdlInterface("TextDecoder", "https://encoding.spec.whatwg.org/#textdecoder")]
public sealed class TextDecoder
{
    private readonly TextDecoderCore _decoder;

    /// <summary>Creates a decoder for the encoding identified by the given label.</summary>
    /// <seealso href="https://encoding.spec.whatwg.org/#dom-textdecoder"/>
    [IdlMember("constructor", "https://encoding.spec.whatwg.org/#dom-textdecoder")]
    public TextDecoder(string label = "utf-8", TextDecoderOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(label);
        _decoder = TextDecoderCore.TryCreate(label, options?.Fatal ?? false, options?.IgnoreBom ?? false)
            ?? throw new RangeErrorException($"The encoding label '{label}' is not supported");
    }

    /// <summary>Gets the encoding's name, ASCII lowercased.</summary>
    /// <seealso href="https://encoding.spec.whatwg.org/#dom-textdecoder-encoding"/>
    [IdlMember("encoding", "https://encoding.spec.whatwg.org/#dom-textdecoder-encoding")]
    public string Encoding => _decoder.Encoding;

    /// <summary>
    /// Gets a value indicating whether decoding throws on invalid data instead of using replacement characters.
    /// </summary>
    /// <seealso href="https://encoding.spec.whatwg.org/#dom-textdecoder-fatal"/>
    [IdlMember("fatal", "https://encoding.spec.whatwg.org/#dom-textdecoder-fatal")]
    public bool Fatal => _decoder.Fatal;

    /// <summary>Gets a value indicating whether the byte order mark is kept in the decoded output.</summary>
    /// <seealso href="https://encoding.spec.whatwg.org/#dom-textdecoder-ignorebom"/>
    [IdlMember("ignoreBOM", "https://encoding.spec.whatwg.org/#dom-textdecoder-ignorebom")]
    public bool IgnoreBom => _decoder.IgnoreBom;

    /// <summary>Decodes the bytes (https://encoding.spec.whatwg.org/#dom-textdecoder-decode): a missing input is an empty input.</summary>
    /// <seealso href="https://encoding.spec.whatwg.org/#dom-textdecoder-decode"/>
    [IdlMember("decode", "https://encoding.spec.whatwg.org/#dom-textdecoder-decode")]
    public string Decode(ReadOnlySpan<byte> input = default, TextDecodeOptions? options = null)
    {
        if (!_decoder.TryDecode(input, options?.Stream ?? false, out var result))
            throw new TypeErrorException("The encoded data is not valid");

        return result;
    }
}

/// <summary>Represents the result of <c>TextEncoder.encodeInto()</c>.</summary>
/// <seealso href="https://encoding.spec.whatwg.org/#dictdef-textencoderencodeintoresult"/>
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
    /// <seealso href="https://encoding.spec.whatwg.org/#dom-textencoderencodeintoresult-read"/>
    [IdlMember("read", "https://encoding.spec.whatwg.org/#dom-textencoderencodeintoresult-read")]
    public ulong Read { get; }

    /// <summary>Gets the number of bytes that were written to the destination.</summary>
    /// <seealso href="https://encoding.spec.whatwg.org/#dom-textencoderencodeintoresult-written"/>
    [IdlMember("written", "https://encoding.spec.whatwg.org/#dom-textencoderencodeintoresult-written")]
    public ulong Written { get; }
}

/// <summary>https://encoding.spec.whatwg.org/#interface-textencoder</summary>
/// <seealso href="https://encoding.spec.whatwg.org/#textencoder"/>
[IdlInterface("TextEncoder", "https://encoding.spec.whatwg.org/#textencoder")]
public sealed class TextEncoder
{
    /// <summary>Creates a UTF-8 encoder.</summary>
    /// <seealso href="https://encoding.spec.whatwg.org/#dom-textencoder"/>
    [IdlMember("constructor", "https://encoding.spec.whatwg.org/#dom-textencoder")]
    public TextEncoder()
    {
    }

    /// <summary>Gets the name of the encoding, which is always <c>utf-8</c>.</summary>
    /// <seealso href="https://encoding.spec.whatwg.org/#dom-textencoder-encoding"/>
    [IdlMember("encoding", "https://encoding.spec.whatwg.org/#dom-textencoder-encoding")]
    public string Encoding => "utf-8";

    /// <summary>Returns the UTF-8 bytes of the given string.</summary>
    /// <seealso href="https://encoding.spec.whatwg.org/#dom-textencoder-encode"/>
    [IdlMember("encode", "https://encoding.spec.whatwg.org/#dom-textencoder-encode")]
    public byte[] Encode(string input = "")
    {
        ArgumentNullException.ThrowIfNull(input);

        return TextEncoderCore.Encode(input);
    }

    /// <summary>
    /// Encodes the given string as UTF-8 into the destination and returns the number of units read and bytes written.
    /// </summary>
    /// <seealso href="https://encoding.spec.whatwg.org/#dom-textencoder-encodeinto"/>
    [IdlMember("encodeInto", "https://encoding.spec.whatwg.org/#dom-textencoder-encodeinto")]
    public TextEncoderEncodeIntoResult EncodeInto(string source, Span<byte> destination)
    {
        ArgumentNullException.ThrowIfNull(source);

        TextEncoderCore.EncodeInto(source, destination, out var read, out var written);
        return new TextEncoderEncodeIntoResult(read, written);
    }
}
