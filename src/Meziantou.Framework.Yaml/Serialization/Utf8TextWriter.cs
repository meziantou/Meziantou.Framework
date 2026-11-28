using System.Buffers;
using System.Text;

namespace Meziantou.Framework.Yaml.Serialization;

/// <summary>
/// Encodes the text that the serializer writes to UTF-8, in pooled buffers, and writes it to a stream or keeps it for
/// <see cref="ToArray"/>. Unlike a <see cref="StreamWriter"/>, it allocates no buffer, and an instance is reused by the
/// calls of a thread.
/// </summary>
internal sealed class Utf8TextWriter : TextWriter
{
    // The characters buffered before they are encoded
    private const int CharBufferSize = 4096;

    // The text of the serializer is UTF-16 from .NET strings: a lone surrogate is reported instead of being replaced, like
    // the stream reader of the deserializer does for invalid bytes
    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    [ThreadStatic]
    private static Utf8TextWriter? s_cachedWriter;

    private readonly Encoder _encoder = StrictUtf8.GetEncoder();
    private Stream? _stream;
    private char[] _chars = [];
    private int _charCount;

    // The encoded bytes: a block written to the stream, or the whole text when there is no stream
    private byte[] _bytes = [];
    private int _byteCount;

    private Utf8TextWriter()
    {
    }

    public override Encoding Encoding => StrictUtf8;

    /// <summary>Gets a writer that writes the text to <paramref name="stream"/>. Give it back with <see cref="Return"/>.</summary>
    public static Utf8TextWriter Rent(Stream stream)
    {
        var writer = RentCore();
        writer._stream = stream;
        writer._bytes = ArrayPool<byte>.Shared.Rent(StrictUtf8.GetMaxByteCount(CharBufferSize));
        return writer;
    }

    /// <summary>Gets a writer that keeps the text for <see cref="ToArray"/>. Give it back with <see cref="Return"/>.</summary>
    public static Utf8TextWriter RentForBytes()
    {
        var writer = RentCore();
        writer._bytes = ArrayPool<byte>.Shared.Rent(StrictUtf8.GetMaxByteCount(CharBufferSize));
        return writer;
    }

    private static Utf8TextWriter RentCore()
    {
        // A converter can serialize while a value is serialized: the nested call gets a writer of its own
        var writer = s_cachedWriter ?? new Utf8TextWriter();
        s_cachedWriter = null;
        writer._chars = ArrayPool<char>.Shared.Rent(CharBufferSize);
        return writer;
    }

    /// <summary>Gives back the buffers of the writer, which must not be used afterward.</summary>
    public static void Return(Utf8TextWriter writer)
    {
        ArrayPool<char>.Shared.Return(writer._chars);
        ArrayPool<byte>.Shared.Return(writer._bytes);
        writer._chars = [];
        writer._bytes = [];
        writer._charCount = 0;
        writer._byteCount = 0;
        writer._stream = null;
        writer._encoder.Reset();
        s_cachedWriter = writer;
    }

    public override void Write(char value)
    {
        if (_charCount == _chars.Length)
        {
            EncodeChars(flush: false);
        }

        _chars[_charCount++] = value;
    }

    public override void Write(string? value) => Write(value.AsSpan());

    public override void Write(char[] buffer, int index, int count) => Write(buffer.AsSpan(index, count));

    public override void Write(ReadOnlySpan<char> buffer)
    {
        while (!buffer.IsEmpty)
        {
            if (_charCount == _chars.Length)
            {
                EncodeChars(flush: false);
            }

            var count = Math.Min(buffer.Length, _chars.Length - _charCount);
            buffer[..count].CopyTo(_chars.AsSpan(_charCount));
            _charCount += count;
            buffer = buffer[count..];
        }
    }

    /// <summary>Encodes the buffered text and, with a stream, writes it to the stream and flushes it.</summary>
    public override void Flush()
    {
        EncodeChars(flush: true);
        _stream?.Flush();
    }

    /// <summary>Gets the bytes of the text, which <see cref="Flush"/> encoded.</summary>
    public byte[] ToArray() => _bytes.AsSpan(0, _byteCount).ToArray();

    // A surrogate pair can be split between two blocks: the encoder keeps the high surrogate until the next block
    private void EncodeChars(bool flush)
    {
        var chars = _chars.AsSpan(0, _charCount);
        if (_stream is not null)
        {
            var count = _encoder.GetBytes(chars, _bytes, flush);
            _stream.Write(_bytes, 0, count);
        }
        else
        {
            var required = _byteCount + StrictUtf8.GetMaxByteCount(chars.Length);
            if (required > _bytes.Length)
            {
                var bytes = ArrayPool<byte>.Shared.Rent(Math.Max(required, _bytes.Length * 2));
                _bytes.AsSpan(0, _byteCount).CopyTo(bytes);
                ArrayPool<byte>.Shared.Return(_bytes);
                _bytes = bytes;
            }

            _byteCount += _encoder.GetBytes(chars, _bytes.AsSpan(_byteCount), flush);
        }

        _charCount = 0;
    }
}
