using System.Buffers;
using System.Net;

namespace Meziantou.Framework.Http.Caching;

/// <summary>Reads a response body for storage without buffering an unbounded amount of it.</summary>
/// <remarks>
/// <see cref="HttpContent.ReadAsByteArrayAsync(CancellationToken)"/> materializes the whole body before its
/// size can be compared to the limit, so an origin that announces no <c>Content-Length</c> can push
/// arbitrarily many bytes into memory before the response is rejected. This reader stops once the limit is
/// exceeded, and in every case leaves the response body fully readable by the caller: the caching handler
/// must stay transparent even for the responses it declines to store.
/// </remarks>
internal static class BoundedContentReader
{
    private const int ChunkSize = 81920;

    /// <summary>Reads the body, or returns <see langword="null"/> when it exceeds <paramref name="maximumSize"/>.</summary>
    [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "Ownership of the content is transferred to the response message")]
    public static async Task<byte[]?> ReadAsync(HttpResponseMessage response, long maximumSize, CancellationToken cancellationToken)
    {
        var originalContent = response.Content;
        var stream = await originalContent.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);

        using var buffer = new MemoryStream();
        var chunk = ArrayPool<byte>.Shared.Rent(ChunkSize);
        try
        {
            while (buffer.Length <= maximumSize)
            {
                var read = await stream.ReadAsync(chunk.AsMemory(0, ChunkSize), cancellationToken).ConfigureAwait(false);
                if (read is 0)
                {
                    // The whole body fits. It has been drained from the original content, so the response is
                    // given an equivalent content holding the bytes that were just read.
                    var bytes = buffer.ToArray();
                    ReplaceContent(response, new BufferedContent(bytes), originalContent, disposeOriginal: true);
                    return bytes;
                }

                buffer.Write(chunk, 0, read);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(chunk);
        }

        // Over the limit: nothing is stored, and the caller gets the bytes already read followed by the rest
        // of the original stream. The original content owns the connection, so it is disposed with the
        // stream that replaces it rather than here.
        ReplaceContent(response, new StreamContent(new PrefixedStream(buffer.ToArray(), stream, originalContent)), originalContent, disposeOriginal: false);
        return null;
    }

    private static void ReplaceContent(HttpResponseMessage response, HttpContent newContent, HttpContent originalContent, bool disposeOriginal)
    {
        foreach (var header in originalContent.Headers)
        {
            newContent.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        response.Content = newContent;
        if (disposeOriginal)
        {
            originalContent.Dispose();
        }
    }

    /// <summary>Holds an already-read body without announcing a length the original response did not.</summary>
    /// <remarks>
    /// <see cref="ByteArrayContent"/> computes its length, which would add a <c>Content-Length</c> header to
    /// a response that carried none — a <c>204</c>, or any chunked response. The headers are copied from the
    /// content this one replaces instead, so the caller sees exactly what the origin sent.
    /// </remarks>
    private sealed class BufferedContent(byte[] content) : HttpContent
    {
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context)
        {
            return stream.WriteAsync(content).AsTask();
        }

        protected override Task<Stream> CreateContentReadStreamAsync()
        {
            return Task.FromResult<Stream>(new MemoryStream(content, writable: false));
        }

        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }
    }

    /// <summary>A read-only stream that yields a buffered prefix before continuing with the stream it was read from.</summary>
    private sealed class PrefixedStream(byte[] prefix, Stream inner, HttpContent owner) : Stream
    {
        private int _prefixPosition;

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

        public override int Read(Span<byte> buffer)
        {
            var fromPrefix = ReadPrefix(buffer);
            return fromPrefix > 0 ? fromPrefix : inner.Read(buffer);
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            return ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
        }

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            var fromPrefix = ReadPrefix(buffer.Span);
            return fromPrefix > 0 ? ValueTask.FromResult(fromPrefix) : inner.ReadAsync(buffer, cancellationToken);
        }

        private int ReadPrefix(Span<byte> buffer)
        {
            var remaining = prefix.Length - _prefixPosition;
            if (remaining is 0 || buffer.IsEmpty)
                return 0;

            var count = Math.Min(remaining, buffer.Length);
            prefix.AsSpan(_prefixPosition, count).CopyTo(buffer);
            _prefixPosition += count;
            return count;
        }

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                // Disposing the content that produced the stream is what releases the connection.
                owner.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}
