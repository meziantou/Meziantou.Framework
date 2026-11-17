using Meziantou.Framework.Compression.Internals.Deflate64;
using Meziantou.Framework.Compression.Internals.IO;

namespace Meziantou.Framework.Compression.Internals.Zip;

/// <summary>
/// The deflate or Deflate64 decoder of a streamed entry of the forward-only reader: when the data end, the bytes the decoder
/// read after them go back to the input, which is then at the data descriptor. Disposing the stream of the entry does not
/// release the decoder: the reader decodes the rest of the data to find the descriptor, then calls <see cref="Release"/>.
/// </summary>
internal sealed class ZipDeflateEndStream : ReadOnlyStreamBase
{
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "CA2213:Disposable fields should be disposed", Justification = "The reader owns the input")]
    private readonly PushbackStream _input;

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "CA2213:Disposable fields should be disposed", Justification = "Released by Release, once the reader found the end of the data")]
    private readonly Deflate64DecoderStream _decoder;
    private bool _ended;

    public ZipDeflateEndStream(PushbackStream input, bool deflate64)
    {
        _input = input;
        _decoder = new Deflate64DecoderStream(new NonClosingStream(input), deflate64, leaveOpen: false);
    }

    public override int Read(Span<byte> buffer)
    {
        if (_ended || buffer.IsEmpty)
            return 0;

        var read = _decoder.Read(buffer);
        if (read is 0)
        {
            if (!_decoder.IsCompleted)
                throw new InvalidDataException("The deflate data of the zip entry are truncated.");

            _ended = true;
            _input.Unread(_decoder.GetUnconsumedInput().Span);
        }

        return read;
    }

    /// <summary>Decodes and discards the rest of the data.</summary>
    public void SkipToEnd()
    {
        var buffer = new byte[16 * 1024];
        while (Read(buffer) > 0)
        {
        }
    }

    /// <summary>Releases the decoder (called by the reader, not by the stream of the entry).</summary>
    public void Release() => _decoder.Dispose();
}
