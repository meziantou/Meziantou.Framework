using Microsoft.Win32.SafeHandles;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// A bounded, read-only, random-access input: the bytes of one encoded file, addressable by offset. It is the I/O half of
/// the container formats whose structure is a graph of file offsets instead of a byte stream: TIFF/BigTIFF image file
/// directories, ICO/CUR directories and the chunks of an animated cursor. The forward-only <see cref="ImageParser{TResult}"/> contract
/// cannot express them, because an offset may point anywhere in the file, including before the current position.
/// </summary>
/// <remarks>
/// <para>
/// An input never grows: <see cref="Length"/> is known when it is created. It holds no per-operation state, so an
/// <see cref="ImageCollection"/> can keep one input open and give every operation its own limits, accounting and
/// allocation scope through a <see cref="RandomAccessSource"/>.
/// </para>
/// <para>
/// <see cref="Read"/> is thread-safe, so the entries of a collection can be decoded concurrently: memory and file inputs
/// read at an offset without any shared position, and a stream input serializes its seek-then-read pairs. Disposing an
/// input waits for the stream read in progress; a later read throws <see cref="ObjectDisposedException"/>.
/// </para>
/// <para>
/// Reads are synchronous on purpose: a random-access decoder issues many small reads whose offsets depend on the bytes
/// just read (an IFD chain, then the strip-offset array, then one strip), so an asynchronous variant would await every
/// tag without any concurrency to gain from. The asynchronous public entry points read the whole input into memory first
///.
/// </para>
/// </remarks>
internal abstract class RandomAccessInput : IDisposable
{
    /// <summary>Gets the number of bytes of the input, from its first byte.</summary>
    public abstract long Length { get; }

    /// <summary>Creates an input over data already in memory. The memory must stay valid until the input is disposed.</summary>
    /// <param name="data">The whole encoded input.</param>
    /// <param name="owner">An owner disposed with the input, or <see langword="null"/> when the caller owns the memory.</param>
    /// <returns>The input.</returns>
    public static RandomAccessInput Create(ReadOnlyMemory<byte> data, IDisposable? owner = null) => new MemoryInput(data, owner);

    /// <summary>Creates an input over a seekable stream.</summary>
    /// <param name="stream">The seekable, readable stream.</param>
    /// <param name="origin">The absolute position of the first byte of the input.</param>
    /// <param name="ownsStream"><see langword="true"/> to dispose the stream with the input.</param>
    /// <returns>The input.</returns>
    /// <exception cref="ArgumentException">The stream is not seekable.</exception>
    public static RandomAccessInput Create(Stream stream, long origin, bool ownsStream) => new StreamInput(stream, origin, ownsStream);

    /// <summary>Creates an input over a file opened for reading, read at an offset so that concurrent reads do not share a position.</summary>
    /// <param name="handle">The file handle; the input takes ownership.</param>
    /// <returns>The input.</returns>
    public static RandomAccessInput Create(SafeFileHandle handle) => new FileInput(handle);

    /// <summary>
    /// Creates an input over bytes the caller keeps pinned until it disposes the input: a synchronous decode reads the
    /// bytes of the input driver in place (<see cref="WholeInputParser{TResult}"/>) instead of copying them.
    /// </summary>
    /// <param name="data">The first byte of the whole encoded input.</param>
    /// <param name="length">The number of bytes.</param>
    /// <returns>The input; reading it after it was disposed throws <see cref="ObjectDisposedException"/>.</returns>
    public static unsafe RandomAccessInput CreatePinned(byte* data, int length) => new PinnedInput(data, length);

    /// <summary>Reads a validated, non-empty range.</summary>
    /// <param name="offset">The offset from the first byte of the input.</param>
    /// <param name="destination">The destination, filled completely.</param>
    public abstract void Read(long offset, Span<byte> destination);

    /// <summary>Gets a value indicating whether the input is held in memory, so that <see cref="GetSpan"/> can serve its bytes without a copy.</summary>
    public virtual bool IsInMemory => false;

    /// <summary>Gets a validated range of an input held in memory (<see cref="IsInMemory"/>) without copying it.</summary>
    /// <param name="offset">The offset from the first byte of the input.</param>
    /// <param name="length">The number of bytes.</param>
    /// <returns>The bytes, valid until the input is disposed.</returns>
    /// <exception cref="NotSupportedException">The input is not held in memory.</exception>
    public virtual ReadOnlySpan<byte> GetSpan(long offset, int length) => throw new NotSupportedException("The input is not held in memory.");

    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
    }

    private sealed class MemoryInput(ReadOnlyMemory<byte> data, IDisposable? owner) : RandomAccessInput
    {
        public override long Length => data.Length;

        public override void Read(long offset, Span<byte> destination) => data.Span.Slice((int)offset, destination.Length).CopyTo(destination);

        public override bool IsInMemory => true;

        public override ReadOnlySpan<byte> GetSpan(long offset, int length) => data.Span.Slice((int)offset, length);

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                owner?.Dispose();
            }

            base.Dispose(disposing);
        }
    }

    private sealed class PinnedInput(byte* data, int length) : RandomAccessInput
    {
        private byte* _data = data;

        public override long Length => length;

        public override void Read(long offset, Span<byte> destination) => GetBytes().Slice((int)offset, destination.Length).CopyTo(destination);

        public override bool IsInMemory => true;

        public override ReadOnlySpan<byte> GetSpan(long offset, int length) => GetBytes().Slice((int)offset, length);

        protected override void Dispose(bool disposing)
        {
            // The bytes are only pinned while the input is alive
            _data = null;
            base.Dispose(disposing);
        }

        private ReadOnlySpan<byte> GetBytes()
        {
            ObjectDisposedException.ThrowIf(_data is null && length != 0, this);
            return unsafe(new ReadOnlySpan<byte>(_data, length));
        }
    }

    private sealed class FileInput(SafeFileHandle handle) : RandomAccessInput
    {
        public override long Length { get; } = RandomAccess.GetLength(handle);

        public override void Read(long offset, Span<byte> destination)
        {
            // A closed handle throws ObjectDisposedException, and a read in progress keeps the handle alive until it returns
            while (!destination.IsEmpty)
            {
                var read = RandomAccess.Read(handle, destination, offset);
                if (read is 0)
                    throw new EndOfStreamException();

                destination = destination[read..];
                offset += read;
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                handle.Dispose();
            }

            base.Dispose(disposing);
        }
    }

    private sealed class StreamInput : RandomAccessInput
    {
        private readonly Lock _lock = new();
        private readonly Stream _stream;
        private readonly long _origin;
        private readonly bool _ownsStream;
        private bool _disposed;

        public StreamInput(Stream stream, long origin, bool ownsStream)
        {
            ArgumentNullException.ThrowIfNull(stream);
            if (!stream.CanSeek)
                throw new ArgumentException("The stream is not seekable.", nameof(stream));

            _stream = stream;
            _origin = origin;
            _ownsStream = ownsStream;
            Length = Math.Max(0, stream.Length - origin);
        }

        public override long Length { get; }

        public override void Read(long offset, Span<byte> destination)
        {
            // The position of the stream is shared: a concurrent read must not seek between this seek and this read
            lock (_lock)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                _stream.Position = _origin + offset;
                _stream.ReadExactly(destination);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                // Waits for the read in progress, so the stream is never disposed or reused by its owner under a read
                lock (_lock)
                {
                    _disposed = true;
                    if (_ownsStream)
                    {
                        _stream.Dispose();
                    }
                }
            }

            base.Dispose(disposing);
        }
    }
}
