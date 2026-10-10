using System.Buffers.Binary;
using System.IO.Compression;
using Meziantou.Framework.Imaging.Formats;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// Encodes the image datastream of one PNG image incrementally (the encoder side of <see cref="PngImageDataDecoder"/>):
/// conversion of stored rows to PNG samples (<see cref="PngEncodedLayout"/>), Adam7 pass extraction (empty passes have no
/// scanline), forward filtering (a fixed filter type or the adaptive heuristic), BCL zlib compression, and emission of the
/// compressed bytes as <c>IDAT</c> chunks of at most <see cref="ChunkCapacity"/> bytes, or as APNG <c>fdAT</c> chunks (a
/// 4-byte sequence number followed by at most <see cref="ChunkCapacity"/> compressed bytes) for the frames of an animation.
/// </summary>
/// <remarks>
/// <para>
/// Push-model use: <see cref="Start"/> (or <see cref="StartFrameData"/>) begins a datastream; each <see cref="Encode"/> call processes a bounded band of
/// scanlines (about <see cref="BandBytes"/> unfiltered bytes, at least one scanline, fewer when the output buffer should be
/// flushed) read straight from the frame storage, and returns <see langword="true"/> once the zlib datastream is finished and
/// its last chunk written. Memory is bounded by four scanlines (two for adaptive filtering), one stored row for Adam7, one
/// chunk buffer and the BCL deflater state, whatever the image size; the image is never copied or buffered as a whole, and
/// neither is the compressed datastream. The pooled buffers are charged to the writer scope; the native deflater state is
/// not (as for the decoder's inflater).
/// </para>
/// <para>
/// Adaptive filtering applies the heuristic recommended by the PNG specification (section 12.8): every filter type is tried
/// for each scanline and the one with the smallest sum of absolute signed filtered bytes is kept (ties keep the lowest type).
/// </para>
/// </remarks>
internal sealed class PngImageDataEncoder : IDisposable
{
    /// <summary>The largest data length of the emitted <c>IDAT</c> chunks.</summary>
    public const int ChunkCapacity = 32 * 1024;

    /// <summary>The number of unfiltered scanline bytes processed per <see cref="Encode"/> call (at least one scanline).</summary>
    public const int BandBytes = 64 * 1024;

    private readonly PngEncodedLayout _layout;
    private readonly int _width;
    private readonly int _height;
    private readonly int _passCount;
    private readonly int _fixedFilter;
    private readonly CompressionLevel _compressionLevel;
    private PooledBuffer? _current;
    private PooledBuffer? _previous;
    private PooledBuffer? _filtered;
    private PooledBuffer? _trial;
    private PooledBuffer? _row;
    private PooledBuffer? _chunkBuffer;
    private ChunkStream? _chunks;
    private ZLibStream? _zlib;

    private int _pass;
    private int _passWidth;
    private int _passHeight;
    private int _passRow;
    private bool _started;

    /// <summary>Creates the encoder of images of one size and pixel format, and rents its working buffers.</summary>
    /// <param name="scope">The writer scope, charged for every buffer.</param>
    /// <param name="pixelFormat">The stored pixel format.</param>
    /// <param name="width">The image width.</param>
    /// <param name="height">The image height.</param>
    /// <param name="interlaced">Whether Adam7 interlacing is used.</param>
    /// <param name="filter">The filter strategy.</param>
    /// <param name="compressionLevel">The zlib compression level.</param>
    /// <exception cref="ImageResourceLimitException">The working buffers exceed the allocation limit.</exception>
    public PngImageDataEncoder(AllocationScope scope, PixelFormat pixelFormat, int width, int height, bool interlaced, PngFilter filter, CompressionLevel compressionLevel)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        _layout = PngEncodedLayout.Get(pixelFormat);
        _width = width;
        _height = height;
        _passCount = interlaced ? PngInterlace.PassCount : 1;
        _compressionLevel = compressionLevel;
        _fixedFilter = filter switch
        {
            PngFilter.None => PngFilters.None,
            PngFilter.Sub => PngFilters.Sub,
            PngFilter.Up => PngFilters.Up,
            PngFilter.Average => PngFilters.Average,
            PngFilter.Paeth => PngFilters.Paeth,
            _ => -1, // Adaptive
        };

        // The widest scanline is the full width (non-interlaced, or the last Adam7 passes)
        var rowLength = (long)width * _layout.BytesPerPixel;
        if (rowLength + 1 > CheckedSizes.MaxBufferLength)
            throw CheckedSizes.CreateOverflowException(scope.Limits);

        try
        {
            _current = scope.Rent((int)rowLength, AllocationKind.Temporary, clear: false);
            _previous = scope.Rent((int)rowLength, AllocationKind.Temporary);
            _filtered = scope.Rent((int)rowLength + 1, AllocationKind.Temporary, clear: false);
            if (_fixedFilter < 0)
            {
                _trial = scope.Rent((int)rowLength + 1, AllocationKind.Temporary, clear: false);
            }

            if (interlaced)
            {
                _row = scope.Rent((int)rowLength, AllocationKind.Temporary, clear: false);
            }

            _chunkBuffer = scope.Rent(ChunkCapacity, AllocationKind.Temporary, clear: false);
            _chunks = new ChunkStream(_chunkBuffer.RawBuffer.AsMemory(0, ChunkCapacity));
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    /// <summary>
    /// Gets the sequence number of the next <c>fdAT</c> chunk: the value given to <see cref="StartFrameData"/>, incremented
    /// for every emitted chunk.
    /// </summary>
    public uint NextSequenceNumber => _chunks?.SequenceNumber ?? 0;

    /// <summary>Starts a new datastream emitted as <c>IDAT</c> chunks (the previous one, if any, must be complete).</summary>
    public void Start() => StartCore(frameData: false, sequenceNumber: 0);

    /// <summary>Starts a new datastream emitted as APNG <c>fdAT</c> chunks (the previous one, if any, must be complete).</summary>
    /// <param name="sequenceNumber">The sequence number of the first <c>fdAT</c> chunk; read <see cref="NextSequenceNumber"/> once the datastream is finished.</param>
    public void StartFrameData(uint sequenceNumber) => StartCore(frameData: true, sequenceNumber);

    private void StartCore(bool frameData, uint sequenceNumber)
    {
        ObjectDisposedException.ThrowIf(_chunks is null, this);
        if (_started)
            throw new InvalidOperationException("The previous PNG datastream is not complete.");

        _chunks.Reset(frameData, sequenceNumber);
        _zlib = new ZLibStream(_chunks, _compressionLevel, leaveOpen: true);
        _pass = -1;
        _started = true;
        StartNextPass();
    }

    /// <summary>Encodes the next band of scanlines of <paramref name="storage"/>, appending complete chunks to <paramref name="output"/>.</summary>
    /// <param name="storage">The pixels of the image (leased for the duration of the call).</param>
    /// <param name="output">The output buffer.</param>
    /// <returns><see langword="true"/> when the datastream is finished (its last chunk written).</returns>
    public bool Encode(PixelStorage storage, ImageOutputBuffer output)
    {
        ArgumentNullException.ThrowIfNull(storage);
        ArgumentNullException.ThrowIfNull(output);
        var zlib = _zlib ?? throw new InvalidOperationException("No PNG datastream is in progress.");
        var chunks = _chunks!;
        chunks.Output = output;
        try
        {
            using (var lease = storage.AcquireLease())
            {
                long processed = 0;
                while (_pass < _passCount)
                {
                    if (processed >= BandBytes || (processed > 0 && output.ShouldFlush))
                        return false;

                    processed += EncodeScanline(in lease, zlib);
                }
            }

            // Finishing the zlib stream writes the last deflate block and the Adler-32 trailer, then the last chunk
            _zlib = null;
            zlib.Dispose();
            chunks.FlushChunk();
            _started = false;
            return true;
        }
        finally
        {
            chunks.Output = null;
        }
    }

    public void Dispose()
    {
        if (_chunks is not null)
        {
            // An aborted datastream is discarded: nothing reaches the output any more
            _chunks.Output = null;
            _chunks.Discard = true;
        }

        _zlib?.Dispose();
        _zlib = null;
        _chunks?.Dispose();
        _chunks = null;
        _chunkBuffer?.Dispose();
        _chunkBuffer = null;
        _row?.Dispose();
        _row = null;
        _trial?.Dispose();
        _trial = null;
        _filtered?.Dispose();
        _filtered = null;
        _previous?.Dispose();
        _previous = null;
        _current?.Dispose();
        _current = null;
    }

    private int EncodeScanline(scoped in PixelLease lease, ZLibStream zlib)
    {
        var bytesPerPixel = _layout.BytesPerPixel;
        var length = _passWidth * bytesPerPixel;
        var current = _current!.RawBuffer.AsSpan(0, length);
        var previous = _previous!.RawBuffer.AsSpan(0, length);
        if (_passCount == 1)
        {
            _layout.ToSamples(lease.GetRowBytes(_passRow), current);
        }
        else
        {
            var (x0, y0, dx, dy) = PngInterlace.GetPass(_pass);
            var row = _row!.RawBuffer.AsSpan(0, _width * bytesPerPixel);
            _layout.ToSamples(lease.GetRowBytes(y0 + (_passRow * dy)), row);
            for (var i = 0; i < _passWidth; i++)
            {
                row.Slice((x0 + (i * dx)) * bytesPerPixel, bytesPerPixel).CopyTo(current[(i * bytesPerPixel)..]);
            }
        }

        var filtered = _filtered!.RawBuffer.AsSpan(0, length + 1);
        if (_fixedFilter >= 0)
        {
            filtered[0] = (byte)_fixedFilter;
            PngFilters.Filter((byte)_fixedFilter, current, previous, bytesPerPixel, filtered[1..]);
        }
        else
        {
            filtered = FilterAdaptive(current, previous, bytesPerPixel, length);
        }

        zlib.Write(filtered);

        // The unfiltered scanline is the "previous" one of the next scanline of the pass
        (_current, _previous) = (_previous, _current);
        _passRow++;
        if (_passRow == _passHeight)
        {
            StartNextPass();
        }

        return length + 1;
    }

    private Span<byte> FilterAdaptive(ReadOnlySpan<byte> current, ReadOnlySpan<byte> previous, int bytesPerPixel, int length)
    {
        var best = _filtered!;
        var trial = _trial!;
        var bestCost = long.MaxValue;
        for (byte type = PngFilters.None; type <= PngFilters.Paeth; type++)
        {
            var candidate = trial.RawBuffer.AsSpan(0, length + 1);
            candidate[0] = type;
            PngFilters.Filter(type, current, previous, bytesPerPixel, candidate[1..]);
            var cost = PngFilters.GetAdaptiveCost(candidate[1..]);
            if (cost < bestCost)
            {
                bestCost = cost;
                (best, trial) = (trial, best);
            }
        }

        // Keep the buffers attached to their fields whatever the winner
        _filtered = best;
        _trial = trial;
        return best.RawBuffer.AsSpan(0, length + 1);
    }

    private void StartNextPass()
    {
        while (++_pass < _passCount)
        {
            (_passWidth, _passHeight) = _passCount == 1 ? (_width, _height) : PngInterlace.GetPassSize(_width, _height, _pass);
            if (_passWidth == 0)
                continue; // empty pass: no scanline at all, not even filter bytes

            _passRow = 0;
            _previous!.RawBuffer.AsSpan(0, _passWidth * _layout.BytesPerPixel).Clear();
            return;
        }
    }

    /// <summary>The sink of the deflater: buffers compressed bytes and writes them to the current output as chunks.</summary>
    private sealed class ChunkStream(Memory<byte> buffer) : Stream
    {
        private int _length;

        /// <summary>Gets or sets the output of the current <see cref="Encode"/> call.</summary>
        public ImageOutputBuffer? Output { get; set; }

        /// <summary>Gets or sets a value indicating whether the datastream is aborted (bytes are dropped).</summary>
        public bool Discard { get; set; }

        public override bool CanRead => false;

        public override bool CanSeek => false;

        public override bool CanWrite => true;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        /// <summary>Gets the sequence number of the next <c>fdAT</c> chunk.</summary>
        public uint SequenceNumber { get; private set; }

        private bool FrameData { get; set; }

        public void Reset(bool frameData, uint sequenceNumber)
        {
            _length = 0;
            FrameData = frameData;
            SequenceNumber = sequenceNumber;
        }

        public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            if (Discard)
                return;

            while (!buffer.IsEmpty)
            {
                var count = Math.Min(buffer.Length, ChunkCapacity - _length);
                buffer[..count].CopyTo(Chunk[_length..]);
                _length += count;
                buffer = buffer[count..];
                if (_length == ChunkCapacity)
                {
                    FlushChunk();
                }
            }
        }

        public override void WriteByte(byte value) => Write([value]);

        /// <summary>Writes the buffered bytes as one chunk (nothing when empty).</summary>
        public void FlushChunk()
        {
            if (Discard || _length == 0)
                return;

            var output = Output ?? throw new InvalidOperationException("Compressed PNG data was produced outside an encoding step.");
            if (FrameData)
            {
                // fdAT: the sequence number, then the same bytes an IDAT chunk would hold (APNG specification)
                PngEncoderCodec.EnsureSequenceNumber(SequenceNumber);
                var crc = PngChunkWriter.Begin(output, PngChunkWriter.Fdat, 4 + _length);
                Span<byte> sequence = stackalloc byte[4];
                BinaryPrimitives.WriteUInt32BigEndian(sequence, SequenceNumber);
                PngChunkWriter.Append(output, ref crc, sequence);
                PngChunkWriter.Append(output, ref crc, Chunk[.._length]);
                PngChunkWriter.End(output, crc);
                SequenceNumber++;
            }
            else
            {
                PngChunkWriter.Write(output, PngChunkWriter.Idat, Chunk[.._length]);
            }

            _length = 0;
        }

        public override void Flush()
        {
            // Chunks are emitted when full and at the end of the datastream; a deflater flush never forces a short chunk
        }

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        private Span<byte> Chunk => buffer.Span;
    }
}
