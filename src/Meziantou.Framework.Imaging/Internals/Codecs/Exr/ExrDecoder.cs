using System.Buffers.Binary;
using System.Numerics;
using System.Runtime.ExceptionServices;
using Meziantou.Framework.Imaging.Metadata;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The OpenEXR pixel sink, shared by eager loads and sequential readers: <see cref="ExrStructureParser"/> gives it the
/// chunks of the full-resolution image as they arrive (in the order of the file), and it decompresses each one and writes its
/// samples to the rows of the frame in <see cref="PixelFormat.RgbaHalf"/> (only HALF channels) or
/// <see cref="PixelFormat.RgbaFloat"/> (also when there is an alpha channel).
/// </summary>
/// <remarks>
/// <para>
/// The image is the display window: the pixels of the data window are placed at their position in it, the others stay
/// transparent black. A missing red, green or blue channel is 0, a missing alpha channel is 1, and a luminance channel
/// gives the three colors. UINT samples become the floating-point number of the same value, which must be exact. The
/// colors of an image with alpha are divided by it (<see cref="Unpremultiply"/>).
/// </para>
/// <para>
/// A chunk only covers part of the rows (a tile, or the data window of a scan line), so the rows are written in place:
/// directly in the frame when it holds the source layout, otherwise in a staging image of the source layout, converted
/// to the frame once every chunk is decoded (a conversion that tone maps needs the peak of the whole image first).
/// </para>
/// <para>
/// With several workers (<see cref="GetWorkerCount"/>), the chunks are copied to a batch as they arrive, and the workers
/// decode the batch at once. The chunks are independent: each one is compressed on its own, and covers pixels that no other
/// chunk covers (the offset table gives one offset to each chunk, and a chunk must carry the coordinates of its entry), so a
/// worker writes only the pixels of its chunk. The structure parser has the batch decoded before it asks for more input and
/// before it reports an error, and the error of a batch is the one of its first chunk that fails (a cancellation first):
/// the errors are those of one worker.
/// </para>
/// </remarks>
internal sealed class ExrDecoder : IImageDecodeObserver
{
    // A batch holds at least two chunks per worker, so that the workers that finish early take the chunks left (a batch
    // waits for its slowest chunk), and at least 1 MiB of uncompressed data per worker, for chunks of one scan line
    private const int ChunksPerWorker = 2;
    private const int BatchBytesPerWorker = 1 << 20;

    private readonly ImageDecodeRequest _request;
    private readonly ImageCodecContext _context;
    private int _workers = 1;
    private ChunkBatch? _batch;
    private ExrStructureParser? _structure;
    private ExrHeader? _header;
    private DecodedFrameSink? _sink;
    private ExrChunkDecompressor? _decompressor;
    private PooledBuffer? _data;
    private Image? _staging;
    private bool _frameStarted;

    public ExrDecoder(ImageDecodeRequest request, ImageCodecContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);
        _request = request;
        _context = context;
    }

    /// <summary>Gets the largest number of chunks of a batch, or 0 when the chunks are decoded one by one as they arrive.</summary>
    public int BatchChunkCount { get; private set; }

    /// <summary>Gets the largest number of bytes of stored data of a batch.</summary>
    public int BatchByteCount { get; private set; }

    /// <summary>Gets a value indicating whether the batch holds chunks that are not decoded yet (<see cref="DecodeBatch"/>).</summary>
    public bool HasPendingChunks => _batch is { Count: > 0 };

    public void OnHeaderComplete(ExrStructureParser structure)
    {
        ArgumentNullException.ThrowIfNull(structure);
        _structure = structure;
        _header = structure.Header;
        var format = structure.DefaultPixelFormat;
        _sink = DecodedFrameSink.Create(_context, _request, ImageFormat.OpenExr, structure.Size, format, format, structure.Metadata.IccProfile);
        _workers = GetWorkerCount(_context.Configuration.MaxDegreeOfParallelism, _header);
        if (_workers > 1)
        {
            var maxChunk = Math.Max(_header.MaxChunkDataLength, 1);
            var bytes = Math.Min(Math.Max((long)ChunksPerWorker * _workers * maxChunk, (long)BatchBytesPerWorker * _workers), CheckedSizes.MaxBufferLength);
            var count = Math.Min(bytes / maxChunk, _header.FirstLevelChunkCount);
            if (count < 2)
            {
                // A batch cannot hold two of the largest chunks
                _workers = 1;
                return;
            }

            BatchChunkCount = (int)count;
            BatchByteCount = (int)bytes;
        }
    }

    /// <summary>
    /// Gets the number of workers that decode the chunks of an image: several for at least two chunks of the full-resolution
    /// image and <see cref="RowBands.MinPixelsForParallelism"/> pixels in its data window, at most one per chunk.
    /// </summary>
    internal static int GetWorkerCount(int maxDegreeOfParallelism, ExrHeader header)
    {
        var data = header.DataWindow;
        if (maxDegreeOfParallelism <= 1 || header.FirstLevelChunkCount < 2 || data.Width * data.Height < RowBands.MinPixelsForParallelism)
            return 1;

        return (int)Math.Min(maxDegreeOfParallelism, header.FirstLevelChunkCount);
    }

    /// <summary>Decodes a chunk of the full-resolution image and writes its samples, or adds it to the batch of the workers.</summary>
    /// <param name="index">The index of the chunk in the offset table.</param>
    /// <param name="stored">The stored data of the chunk.</param>
    public void OnChunk(long index, ReadOnlySpan<byte> stored)
    {
        var header = _header ?? throw new InvalidOperationException("The OpenEXR header was not parsed.");
        if (_workers > 1)
        {
            AddToBatch(header, index, stored);
            return;
        }

        DecodeAlone(header, index, stored);
    }

    /// <summary>
    /// Decodes the chunks of the batch with the workers. The error is the one of the first chunk of the batch that fails, a
    /// cancellation first; the chunks after it are not decoded.
    /// </summary>
    public void DecodeBatch()
    {
        if (_batch is not { Count: > 0 } batch)
            return;

        var count = batch.Count;
        batch.Count = 0;
        batch.Bytes = 0;
        var failures = batch.Failures;
        Array.Clear(failures, 0, count);
        var cancellationToken = _context.CancellationToken;
        var next = -1;
        var firstFailure = int.MaxValue;
        using (var lease = AcquireTarget())
        {
            var storage = lease.Storage;
            RowBands.RunParallel(Math.Min(_workers, count), worker =>
            {
                var decompressor = worker == 0 ? _decompressor! : batch.Decompressors[worker]!;
                var buffer = worker == 0 ? _data! : batch.Buffers[worker]!;
                while (true)
                {
                    var k = Interlocked.Increment(ref next);
                    if (k >= count || k > Volatile.Read(ref firstFailure))
                        return;

                    try
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        DecodeChunk(decompressor, buffer, storage, batch.Indexes[k], new ArraySegment<byte>(batch.Stored.RawBuffer, batch.Starts[k], batch.Lengths[k]));
                    }
                    catch (Exception exception)
                    {
                        failures[k] = exception;
                        var current = Volatile.Read(ref firstFailure);
                        while (k < current)
                        {
                            var previous = Interlocked.CompareExchange(ref firstFailure, k, current);
                            if (previous == current)
                                break;

                            current = previous;
                        }
                    }
                }
            });
        }

        for (var k = 0; k < count; k++)
        {
            if (failures[k] is OperationCanceledException canceled)
            {
                ExceptionDispatchInfo.Throw(canceled);
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        if (firstFailure != int.MaxValue)
        {
            ExceptionDispatchInfo.Throw(failures[firstFailure]!);
        }
    }

    public void OnEnd(ExrStructureParser structure)
    {
        ArgumentNullException.ThrowIfNull(structure);
        var sink = _sink ?? throw new InvalidOperationException("The OpenEXR header was not parsed.");
        DecodeBatch();

        // The state of the workers is released before the conversion allocates the frame
        _batch?.Dispose();
        _batch = null;
        if (_staging is { } staging)
        {
            // The peak of a tone mapping, and the samples a conversion rejects, are those of the whole image
            var frame = staging.FrameCore(0);
            Image.PrepareConversion(sink.Plan, [frame]);
            sink.BeginFrame(FrameDuration.Zero);
            _frameStarted = true;
            using var source = frame.GetStorage().AcquireLease();
            using var destination = sink.LeaseCurrentFrame();
            for (var y = 0; y < source.Height; y++)
            {
                sink.WriteRow(destination, y, source.GetRowBytes(y));
            }
        }
        else if (!_frameStarted)
        {
            throw new InvalidOperationException("The OpenEXR image has no chunk.");
        }

        sink.UpdateMetadata(structure.Metadata);
        sink.EndImage();
    }

    public Image GetResult()
    {
        var structure = _structure ?? throw new InvalidOperationException("The OpenEXR data was not decoded.");
        return _sink!.Build(structure.Metadata, animation: null);
    }

    public void Dispose()
    {
        _sink?.Dispose();
        _sink = null;
        _decompressor?.Dispose();
        _decompressor = null;
        _data?.Dispose();
        _data = null;
        _staging?.Dispose();
        _staging = null;
        _batch?.Dispose();
        _batch = null;
    }

    /// <summary>Gets the position of a chunk of the full-resolution image in the data window, and its geometry.</summary>
    private static ExrChunkLayout GetChunk(ExrHeader header, long index, out int x0, out int y0)
    {
        var data = header.DataWindow;
        if (header.IsTiled)
        {
            var tilesX = header.GetTileCount(0, horizontal: true);
            var tx = (int)(index % tilesX);
            var ty = (int)(index / tilesX);
            x0 = data.MinX + (tx * header.TileWidth);
            y0 = data.MinY + (ty * header.TileHeight);
            var width = (int)Math.Min(header.TileWidth, data.MaxX - (long)x0 + 1);
            var height = (int)Math.Min(header.TileHeight, data.MaxY - (long)y0 + 1);
            return new ExrChunkLayout(header.Channels, width, 0, height);
        }

        x0 = data.MinX;
        y0 = (int)(data.MinY + (index * header.LinesPerChunk));
        var lines = (int)Math.Min(header.LinesPerChunk, data.MaxY - (long)y0 + 1);
        return new ExrChunkLayout(header.Channels, (int)data.Width, y0, lines);
    }

    /// <summary>Writes the uncompressed data of a chunk to the pixels it covers.</summary>
    private static void WriteChunk(ExrHeader header, PixelStorage target, in ExrChunkLayout layout, int x0, int y0, ReadOnlySpan<byte> uncompressed)
    {
        var display = header.DisplayWindow;
        var bytesPerPixel = header.UsesHalfPixels ? 8 : 16;
        var column = (x0 - display.MinX) * bytesPerPixel;
        var lineLength = uncompressed.Length / layout.Lines;
        for (var line = 0; line < layout.Lines; line++)
        {
            var row = target.GetRowSpanCore(y0 + line - display.MinY);
            WriteLine(header, uncompressed.Slice(line * lineLength, lineLength), row.Slice(column, layout.Width * bytesPerPixel), layout.Width);
        }
    }

    /// <summary>Writes the samples of one line of a chunk (every channel, in name order) to pixels of the source layout.</summary>
    private static void WriteLine(ExrHeader header, ReadOnlySpan<byte> line, Span<byte> pixels, int width)
    {
        var half = header.UsesHalfPixels;
        var componentSize = half ? 2 : 4;

        // Colors 0, alpha 1, before the channels the image has are written
        if (!header.HasAlpha)
        {
            for (var x = 0; x < width; x++)
            {
                var alpha = pixels[(((x * 4) + 3) * componentSize)..];
                if (half)
                {
                    BinaryPrimitives.WriteUInt16LittleEndian(alpha, BitConverter.HalfToUInt16Bits(Half.One));
                }
                else
                {
                    BinaryPrimitives.WriteSingleLittleEndian(alpha, 1f);
                }
            }
        }

        var offset = 0;
        var channels = header.Channels;
        for (var c = 0; c < channels.Count; c++)
        {
            var channel = channels[c];
            var role = header.Roles[c];
            var first = role == ExrChannelRole.Luminance ? 0 : (int)role;
            var last = role == ExrChannelRole.Luminance ? 2 : first;
            var samples = line.Slice(offset, width * channel.BytesPerSample);
            offset += samples.Length;
            for (var x = 0; x < width; x++)
            {
                for (var component = first; component <= last; component++)
                {
                    var target = pixels[(((x * 4) + component) * componentSize)..];
                    if (half)
                    {
                        // Only HALF channels: the bits are copied
                        target[0] = samples[2 * x];
                        target[1] = samples[(2 * x) + 1];
                        continue;
                    }

                    switch (channel.PixelType)
                    {
                        case ExrPixelType.Half:
                            BinaryPrimitives.WriteSingleLittleEndian(target, (float)BitConverter.UInt16BitsToHalf(BinaryPrimitives.ReadUInt16LittleEndian(samples[(2 * x)..])));
                            break;

                        case ExrPixelType.Float:
                            samples.Slice(4 * x, 4).CopyTo(target);
                            break;

                        default:
                            var value = BinaryPrimitives.ReadUInt32LittleEndian(samples[(4 * x)..]);
                            if (value > (1 << 24) && (value >> BitOperations.TrailingZeroCount(value)) >= (1 << 24))
                                throw ExrFormat.Unsupported(string.Create(CultureInfo.InvariantCulture, $"The OpenEXR channel '{channel.Name}' has the UINT sample {value}, which a 32-bit floating-point number cannot hold exactly."), "UINT samples above 2^24");

                            BinaryPrimitives.WriteSingleLittleEndian(target, value);
                            break;
                    }
                }
            }
        }

        if (header.HasAlpha)
        {
            Unpremultiply(pixels, width);
        }
    }

    /// <summary>
    /// Divides the colors by the alpha of their pixel: OpenEXR colors are premultiplied by alpha (Technical Introduction,
    /// "Premultiplied vs. Un-Premultiplied Color Channels"), the pixels of the library are not. A pixel whose alpha is 0
    /// keeps its color, which is the light it emits (<see cref="ExrEncoderCodec"/> writes it back unchanged).
    /// </summary>
    internal static void Unpremultiply(Span<byte> pixels, int width)
    {
        for (var x = 0; x < width; x++)
        {
            var pixel = pixels.Slice(x * 16, 16);
            var alpha = BinaryPrimitives.ReadSingleLittleEndian(pixel[12..]);
            if (alpha == 0)
                continue;

            for (var c = 0; c < 12; c += 4)
            {
                BinaryPrimitives.WriteSingleLittleEndian(pixel[c..], (float)(BinaryPrimitives.ReadSingleLittleEndian(pixel[c..]) / (double)alpha));
            }
        }
    }

    /// <summary>Decodes a chunk on the calling thread, with the state of the first worker, and writes its samples.</summary>
    private void DecodeAlone(ExrHeader header, long index, ReadOnlySpan<byte> stored)
    {
        var layout = GetChunk(header, index, out var x0, out var y0);
        RentFirstWorker(header);
        var uncompressed = _data!.Span[..layout.GetDataLength()];
        _decompressor!.Decompress(stored, uncompressed, layout);
        using (var lease = AcquireTarget())
        {
            WriteChunk(header, lease.Storage, layout, x0, y0, uncompressed);
        }

        _context.CancellationToken.ThrowIfCancellationRequested();
    }

    /// <summary>Decodes a chunk of the batch on a worker, and writes its samples.</summary>
    private void DecodeChunk(ExrChunkDecompressor decompressor, PooledBuffer buffer, PixelStorage target, long index, ArraySegment<byte> stored)
    {
        var header = _header!;
        var layout = GetChunk(header, index, out var x0, out var y0);
        var uncompressed = buffer.Span[..layout.GetDataLength()];
        decompressor.Decompress(stored, uncompressed, layout);
        WriteChunk(header, target, layout, x0, y0, uncompressed);
    }

    private void RentFirstWorker(ExrHeader header)
    {
        _decompressor ??= new ExrChunkDecompressor(_context.Scope, header.Compression, (int)header.MaxChunkDataLength);
        _data ??= _context.Scope.Rent((int)Math.Max(header.MaxChunkDataLength, 1), AllocationKind.DecoderState, clear: false);
    }

    /// <summary>Creates the image the chunks are written to (the staging image, or the frame of the sink), and leases it.</summary>
    private PixelLease AcquireTarget()
    {
        var sink = _sink!;
        if (!sink.Plan.IsIdentity)
        {
            _staging ??= CreateStagingImage();
            return _staging.FrameCore(0).GetStorage().AcquireLease();
        }

        if (!_frameStarted)
        {
            sink.BeginFrame(FrameDuration.Zero);
            _frameStarted = true;
        }

        return sink.LeaseCurrentFrame();
    }

    /// <summary>Copies a chunk to the batch, which is decoded once it is full; a chunk larger than a batch is decoded alone.</summary>
    private void AddToBatch(ExrHeader header, long index, ReadOnlySpan<byte> stored)
    {
        var batch = _batch ??= CreateBatch(header);
        if (stored.Length > batch.Stored.Length - batch.Bytes)
        {
            DecodeBatch();
            if (stored.Length > batch.Stored.Length)
            {
                DecodeAlone(header, index, stored);
                return;
            }
        }

        stored.CopyTo(batch.Stored.Span[batch.Bytes..]);
        batch.Indexes[batch.Count] = index;
        batch.Starts[batch.Count] = batch.Bytes;
        batch.Lengths[batch.Count] = stored.Length;
        batch.Count++;
        batch.Bytes += stored.Length;
        if (batch.Count == batch.Indexes.Length)
        {
            DecodeBatch();
        }
    }

    /// <summary>
    /// Rents the state of every worker on the calling thread, before any chunk is decoded: the state of the first worker and
    /// the frame, in the order of one worker, then the state of the other workers and the batch.
    /// </summary>
    private ChunkBatch CreateBatch(ExrHeader header)
    {
        RentFirstWorker(header);
        using (AcquireTarget())
        {
        }

        if (header.Compression is ExrCompressionMethod.B44 or ExrCompressionMethod.B44A && header.Channels.Any(channel => channel.PLinear))
        {
            // The table is built before the workers read it
            _ = ExrB44.GetFromPerceptualTable();
        }

        var batch = new ChunkBatch(_workers, BatchChunkCount);
        try
        {
            for (var worker = 1; worker < _workers; worker++)
            {
                batch.Decompressors[worker] = new ExrChunkDecompressor(_context.Scope, header.Compression, (int)header.MaxChunkDataLength);
                batch.Buffers[worker] = _context.Scope.Rent((int)Math.Max(header.MaxChunkDataLength, 1), AllocationKind.DecoderState, clear: false);
            }

            batch.Stored = _context.Scope.Rent(BatchByteCount, AllocationKind.DecoderState, clear: false);
        }
        catch
        {
            batch.Dispose();
            throw;
        }

        return batch;
    }

    private Image CreateStagingImage()
    {
        var size = _structure!.Size;
        return _header!.UsesHalfPixels
            ? new Image<RgbaHalf>(_context.Configuration, size, _context.Scope, layoutOptions: null)
            : new Image<RgbaFloat>(_context.Configuration, size, _context.Scope, layoutOptions: null);
    }

    /// <summary>The chunks the workers decode at once, and the state of the workers other than the first one.</summary>
    private sealed class ChunkBatch : IDisposable
    {
        public ChunkBatch(int workers, int capacity)
        {
            Decompressors = new ExrChunkDecompressor?[workers];
            Buffers = new PooledBuffer?[workers];
            Indexes = new long[capacity];
            Starts = new int[capacity];
            Lengths = new int[capacity];
            Failures = new Exception?[capacity];
        }

        /// <summary>Gets the decompressor of each worker (none for the first one, which has the decoder's).</summary>
        public ExrChunkDecompressor?[] Decompressors { get; }

        /// <summary>Gets the buffer of the uncompressed data of each worker (none for the first one).</summary>
        public PooledBuffer?[] Buffers { get; }

        /// <summary>Gets or sets the stored data of the chunks, one after the other.</summary>
        public PooledBuffer Stored { get; set; } = null!;

        /// <summary>Gets the indexes of the chunks in the offset table.</summary>
        public long[] Indexes { get; }

        /// <summary>Gets the position of the stored data of each chunk in <see cref="Stored"/>.</summary>
        public int[] Starts { get; }

        /// <summary>Gets the length of the stored data of each chunk.</summary>
        public int[] Lengths { get; }

        /// <summary>Gets the error of each chunk of the batch being decoded.</summary>
        public Exception?[] Failures { get; }

        /// <summary>Gets or sets the number of chunks in the batch.</summary>
        public int Count { get; set; }

        /// <summary>Gets or sets the number of bytes of <see cref="Stored"/> the chunks hold.</summary>
        public int Bytes { get; set; }

        public void Dispose()
        {
            foreach (var decompressor in Decompressors)
            {
                decompressor?.Dispose();
            }

            foreach (var buffer in Buffers)
            {
                buffer?.Dispose();
            }

            Stored?.Dispose();
            Count = 0;
        }
    }
}
