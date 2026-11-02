using System.Buffers;

namespace Meziantou.Framework.Pdf.Parsing;

/// <summary>
/// Reads a stream to its end into one array of its exact length: the length of a seekable stream is known, and a
/// non-seekable stream is read in pooled chunks copied once to the array, so that the file is not copied twice.
/// </summary>
internal static class PdfStreamReader
{
    private const int ChunkSize = 1024 * 1024;

    public static byte[] ReadToEnd(Stream stream)
    {
        if (stream is MemoryStream memory && memory.TryGetBuffer(out var buffer))
        {
            // The buffer of the stream can change after the document is read: it is copied once
            var position = (int)Math.Min(memory.Position, memory.Length);
            var result = buffer.AsSpan(position, (int)memory.Length - position).ToArray();
            memory.Position = memory.Length;
            return result;
        }

        if (TryGetRemainingLength(stream) is { } length)
        {
            var result = new byte[length];
            var read = ReadAtMost(stream, result);
            if (read == length && !HasMore(stream))
                return result;

            // The stream was not as long as it said: read what it gives
            return ReadChunks(stream, result.AsSpan(0, read));
        }

        return ReadChunks(stream, []);
    }

    public static async Task<byte[]> ReadToEndAsync(Stream stream, CancellationToken cancellationToken)
    {
        if (stream is MemoryStream memory && memory.TryGetBuffer(out _))
            return ReadToEnd(stream);

        if (TryGetRemainingLength(stream) is { } length)
        {
            var result = new byte[length];
            var read = 0;
            while (read < length)
            {
                var count = await stream.ReadAsync(result.AsMemory(read), cancellationToken).ConfigureAwait(false);
                if (count <= 0)
                    break;

                read += count;
            }

            var probe = new byte[1];
            if (read == length && await stream.ReadAsync(probe, cancellationToken).ConfigureAwait(false) == 0)
                return result;

            var chunks = new ChunkList();
            try
            {
                chunks.Write(result.AsSpan(0, read));
                if (read == length)
                {
                    chunks.Write(probe);
                }

                await chunks.ReadFromAsync(stream, cancellationToken).ConfigureAwait(false);
                return chunks.ToArray();
            }
            finally
            {
                chunks.Dispose();
            }
        }

        var list = new ChunkList();
        try
        {
            await list.ReadFromAsync(stream, cancellationToken).ConfigureAwait(false);
            return list.ToArray();
        }
        finally
        {
            list.Dispose();
        }
    }

    private static int? TryGetRemainingLength(Stream stream)
    {
        if (!stream.CanSeek)
            return null;

        var remaining = stream.Length - stream.Position;
        if (remaining < 0)
            return 0;

        if (remaining > Array.MaxLength)
            throw new IOException("The stream is larger than an array: open it with " + nameof(PdfDocument) + "." + nameof(PdfDocument.OpenSeekable));

        return (int)remaining;
    }

    private static int ReadAtMost(Stream stream, Span<byte> buffer)
    {
        var total = 0;
        while (total < buffer.Length)
        {
            var read = stream.Read(buffer[total..]);
            if (read <= 0)
                break;

            total += read;
        }

        return total;
    }

    private static bool HasMore(Stream stream)
    {
        Span<byte> probe = stackalloc byte[1];
        if (stream.Read(probe) == 0)
            return false;

        stream.Seek(-1, SeekOrigin.Current);
        return true;
    }

    private static byte[] ReadChunks(Stream stream, ReadOnlySpan<byte> prefix)
    {
        var chunks = new ChunkList();
        try
        {
            chunks.Write(prefix);
            chunks.ReadFrom(stream);
            return chunks.ToArray();
        }
        finally
        {
            chunks.Dispose();
        }
    }

    /// <summary>The bytes read so far, in arrays of the pool.</summary>
    private sealed class ChunkList : IDisposable
    {
        private readonly List<byte[]> _chunks = [];
        private int _lastLength = ChunkSize;
        private long _length;

        public void Write(ReadOnlySpan<byte> data)
        {
            while (data.Length > 0)
            {
                var span = GetSpan();
                var count = Math.Min(span.Length, data.Length);
                data[..count].CopyTo(span);
                Advance(count);
                data = data[count..];
            }
        }

        public void ReadFrom(Stream stream)
        {
            while (true)
            {
                var read = stream.Read(GetSpan());
                if (read <= 0)
                    return;

                Advance(read);
            }
        }

        public async Task ReadFromAsync(Stream stream, CancellationToken cancellationToken)
        {
            while (true)
            {
                GetSpan();
                var read = await stream.ReadAsync(_chunks[^1].AsMemory(_lastLength, ChunkSize - _lastLength), cancellationToken).ConfigureAwait(false);
                if (read <= 0)
                    return;

                Advance(read);
            }
        }

        public byte[] ToArray()
        {
            if (_length > Array.MaxLength)
                throw new IOException("The stream is larger than an array: open it with " + nameof(PdfDocument) + "." + nameof(PdfDocument.OpenSeekable));

            var result = new byte[(int)_length];
            var position = 0;
            for (var i = 0; i < _chunks.Count; i++)
            {
                var count = i == _chunks.Count - 1 ? _lastLength : ChunkSize;
                _chunks[i].AsSpan(0, count).CopyTo(result.AsSpan(position));
                position += count;
            }

            return result;
        }

        public void Dispose()
        {
            foreach (var chunk in _chunks)
            {
                ArrayPool<byte>.Shared.Return(chunk);
            }

            _chunks.Clear();
        }

        private Span<byte> GetSpan()
        {
            if (_lastLength == ChunkSize)
            {
                _chunks.Add(ArrayPool<byte>.Shared.Rent(ChunkSize));
                _lastLength = 0;
            }

            return _chunks[^1].AsSpan(_lastLength, ChunkSize - _lastLength);
        }

        private void Advance(int count)
        {
            _lastLength += count;
            _length += count;
        }
    }
}
