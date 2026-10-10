namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// A bounded, read-only, random-access input: the bytes of one encoded file, addressable by offset. It is the I/O half of
/// the container formats whose structure is a graph of file offsets instead of a byte stream: TIFF/BigTIFF image file
/// directories and ICO/CUR directories. The forward-only <see cref="ImageParser{TResult}"/> contract
/// cannot express them, because an offset may point anywhere in the file, including before the current position.
/// </summary>
/// <remarks>
/// <para>
/// An input never grows: <see cref="Length"/> is known when it is created. It holds no per-operation state, so an
/// <see cref="ImageCollection"/> can keep one input open and give every operation its own limits, accounting and
/// allocation scope through a <see cref="RandomAccessSource"/>.
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

    /// <summary>Reads a validated, non-empty range.</summary>
    /// <param name="offset">The offset from the first byte of the input.</param>
    /// <param name="destination">The destination, filled completely.</param>
    public abstract void Read(long offset, Span<byte> destination);

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

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                owner?.Dispose();
            }

            base.Dispose(disposing);
        }
    }

    private sealed class StreamInput : RandomAccessInput
    {
        private readonly Stream _stream;
        private readonly long _origin;
        private readonly bool _ownsStream;

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
            _stream.Position = _origin + offset;
            _stream.ReadExactly(destination);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && _ownsStream)
            {
                _stream.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}
