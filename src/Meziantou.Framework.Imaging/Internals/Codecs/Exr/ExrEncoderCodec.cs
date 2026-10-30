using System.Buffers.Binary;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Text;
using Meziantou.Framework.Imaging.Formats;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>The OpenEXR encoder registration: one single-part scan-line image per output.</summary>
internal sealed class ExrEncoderCodec : ImageEncoderCodec
{
    public static ExrEncoderCodec Instance { get; } = new();

    public override ImageFormat Format => ImageFormat.OpenExr;

    public override ImageEncoderSession CreateSession(ImageEncoderSessionOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return new Session(options);
    }

    /// <summary>
    /// The encoding state of one OpenEXR output: the header and the offset table, then one chunk per
    /// <see cref="Encode"/> call (1, 16 or 32 scan lines), then the offsets of the compressed chunks, patched over the table.
    /// </summary>
    /// <remarks>
    /// The rows of a chunk are converted to <see cref="PixelFormat.RgbaFloat"/> (in place for <see cref="RgbaFloat"/>
    /// frames), their colors multiplied by the alpha that is stored (the convention of OpenEXR; a pixel whose stored alpha is
    /// 0 keeps its color, as <see cref="ExrDecoder.Unpremultiply"/> reads it), their samples laid out channel by channel in
    /// name order, then compressed; a compressed chunk that is not
    /// smaller than its samples is stored uncompressed (compression notes, section 1.5). The private state is one chunk of
    /// samples, its compressed copy and the offset table: memory does not depend on the height beyond the table.
    /// <para>
    /// With several workers (<see cref="ImageConfiguration.MaxDegreeOfParallelism"/>, at least two chunks and
    /// <see cref="RowBands.MinPixelsForParallelism"/> pixels), the chunks are coded by batches: the workers convert and
    /// compress the chunks of a batch at once, each into a buffer of its own, and the chunks are written one per
    /// <see cref="Encode"/> call, in order. A chunk reads only its own rows of the frame and its compression starts afresh, so
    /// the bytes and the offsets are those of one worker. Each worker has its own chunk of samples, and the batch the coded
    /// chunks. The samples of the frame are checked by bands of rows (<see cref="RowBands.RunParallelInBandOrder"/>), so the
    /// error is the one of its first sample out of range.
    /// </para>
    /// </remarks>
    private sealed class Session : ImageEncoderSession
    {
        // A batch holds at least two chunks per worker, and at least 1 MiB of samples per worker (chunks of one scan line)
        private const int ChunksPerWorker = 2;
        private const int BatchBytesPerWorker = 1 << 20;

        private readonly int _width;
        private readonly int _height;
        private readonly ExrCompressionMethod _compression;
        private readonly ExrPixelType _pixelType;
        private readonly ExrChannel[] _channels;
        private readonly int[] _components;
        private readonly int _linesPerChunk;
        private readonly int _chunkCount;
        private readonly bool _reducesFloats;
        private readonly bool _validatesSamples;
        private readonly bool _hasAlpha;
        private readonly byte _primaries;
        private readonly PixelConversionPlan? _plan;
        private PooledBuffer? _floats;
        private PooledBuffer? _data;
        private PooledBuffer? _stored;
        private PooledBuffer? _offsets;
        private PooledBuffer? _predicted;
        private ExrPiz? _piz;
        private ChunkState?[]? _workers;
        private PooledBuffer?[]? _batch;
        private int[]? _batchLengths;
        private Exception?[]? _failures;
        private int _batchStart;
        private int _batchCount;
        private ImageFrame? _frame;
        private Step _step;
        private int _chunk;
        private long _fileStart;
        private long _tableOffset;

        public Session(ImageEncoderSessionOptions options)
            : base(options)
        {
            var encoder = (OpenExrEncoder)options.Encoder;
            var source = options.PixelFormat;
            var exact = source == PixelFormat.RgbaHalf || (!PixelFormats.IsFloatingPoint(source) && PixelFormats.GetBitsPerComponent(source) <= 8);
            var pixelType = encoder.PixelType ?? (exact ? OpenExrPixelType.Half : OpenExrPixelType.Float);
            if (pixelType == OpenExrPixelType.Half && !exact && !encoder.AllowBitDepthReduction)
                throw new UnsupportedImageFeatureException($"Half samples cannot store {source} pixels exactly. Set OpenExrEncoder.AllowBitDepthReduction to round them, or write float samples (OpenExrPixelType.Float).", ImageFormat.OpenExr, "Bit depth reduction");

            _pixelType = pixelType == OpenExrPixelType.Half ? ExrPixelType.Half : ExrPixelType.Float;
            _reducesFloats = _pixelType == ExrPixelType.Half && source == PixelFormat.RgbaFloat;

            // Integer samples and their products are within [0, 1], and the product of two halves is within the range of floats
            _validatesSamples = source == PixelFormat.RgbaFloat || (source == PixelFormat.RgbaHalf && _pixelType == ExrPixelType.Half);
            _compression = (ExrCompressionMethod)(int)encoder.Compression;
            _width = options.CanvasSize.Width;
            _height = options.CanvasSize.Height;
            _primaries = options.Metadata.Cicp?.ColorPrimaries ?? 1;

            // The channels in name order, and the component of an RGBA pixel each one holds
            (string Name, int Component)[] channels = PixelFormats.IsGrayscale(source)
                ? [("Y", 0)]
                : PixelFormats.HasAlpha(source) ? [("A", 3), ("B", 2), ("G", 1), ("R", 0)] : [("B", 2), ("G", 1), ("R", 0)];
            _hasAlpha = PixelFormats.HasAlpha(source);
            _channels = [.. channels.Select(channel => new ExrChannel(channel.Name, _pixelType, PLinear: false, XSampling: 1, YSampling: 1))];
            _components = [.. channels.Select(channel => channel.Component)];
            _linesPerChunk = ExrFormat.GetLinesPerChunk(_compression);
            _chunkCount = (int)(((long)_height + _linesPerChunk - 1) / _linesPerChunk);

            var dataLength = (long)_width * Math.Min(_linesPerChunk, _height) * _channels.Length * ExrFormat.GetBytesPerSample(_pixelType);
            if (dataLength > CheckedSizes.MaxBufferLength || (long)_width * 16 > CheckedSizes.MaxBufferLength || (long)_chunkCount * sizeof(long) > CheckedSizes.MaxBufferLength)
                throw CheckedSizes.CreateOverflowException(options.Configuration.Limits);

            if (source != PixelFormat.RgbaFloat)
            {
                _plan = PixelConversionPlan.Create(source, PixelFormat.RgbaFloat, format: ImageFormat.OpenExr);
            }

            var workers = GetWorkerCount(options.Configuration.MaxDegreeOfParallelism, _chunkCount, (long)_width * _height);
            var batchChunks = 0;
            if (workers > 1)
            {
                var bytes = Math.Max((long)ChunksPerWorker * workers * dataLength, (long)BatchBytesPerWorker * workers);
                batchChunks = (int)Math.Min(bytes / dataLength, _chunkCount);
            }

            // Every buffer is rented before any output: those of the first worker, then those of the others and the batch
            var scope = options.Scope;
            try
            {
                if (_plan is not null)
                {
                    _floats = scope.Rent(_width * 16, AllocationKind.Temporary, clear: false);
                }

                _data = scope.Rent((int)dataLength, AllocationKind.Temporary, clear: false);
                if (_compression != ExrCompressionMethod.None)
                {
                    if (workers == 1)
                    {
                        _stored = scope.Rent((int)dataLength, AllocationKind.Temporary, clear: false);
                    }

                    _offsets = scope.Rent(_chunkCount * sizeof(long), AllocationKind.Temporary, clear: false);
                }

                if (_compression == ExrCompressionMethod.Piz)
                {
                    _piz = new ExrPiz(scope, (int)dataLength, encoder: true);
                }
                else if (_compression != ExrCompressionMethod.None)
                {
                    _predicted = scope.Rent((int)dataLength, AllocationKind.Temporary, clear: false);
                }

                if (workers > 1)
                {
                    RentWorkers(scope, workers, batchChunks, (int)dataLength);
                }
            }
            catch
            {
                Dispose(disposing: true);
                throw;
            }
        }

        private enum Step
        {
            None,
            Header,
            Chunks,
        }

        public override void ValidateFrame(ImageFrame frame, bool isPoster)
        {
            ArgumentNullException.ThrowIfNull(frame);
            if (!_validatesSamples)
                return;

            using var lease = frame.GetStorage().AcquireLease();
            var storage = lease.Storage;
            var height = lease.Height;
            if (_workers is not { } workers)
            {
                ValidateRows(storage, _floats, 0, height);
                return;
            }

            // The rows are split in bands, one per worker with its own row of floats: the error is the one of the first band
            // that fails, whose first failing sample is the first one of the frame
            var bands = Math.Min(workers.Length, height);
            RowBands.RunParallelInBandOrder(bands, band => ValidateRows(storage, workers[band]!.Floats, RowBands.GetBandStart(height, band, bands), RowBands.GetBandStart(height, band + 1, bands)));
        }

        /// <summary>
        /// Checks the samples of rows of a frame: a finite sample that the reduction to half, or the multiplication by alpha,
        /// takes beyond the range of the sample type would become an infinity (or not a number, for an alpha that is not finite).
        /// </summary>
        private void ValidateRows(PixelStorage storage, PooledBuffer? floats, int start, int end)
        {
            for (var y = start; y < end; y++)
            {
                var pixels = GetRow(storage.GetRowSpanCore(y), floats);
                for (var i = 0; i < pixels.Length; i++)
                {
                    var sample = pixels[i];
                    if (!float.IsFinite(sample))
                        continue;

                    var premultiplied = _hasAlpha && i % 4 != 3 && GetStoredAlpha(pixels[i | 3]) != 0;
                    var value = GetSampleValue(pixels, i, premultiplied);
                    if (_pixelType == ExrPixelType.Half ? Half.IsFinite((Half)value) : float.IsFinite((float)value))
                        continue;

                    var range = _pixelType == ExrPixelType.Half ? "half samples (±65,504)" : "float samples (±3.4028235E+38)";
                    var message = premultiplied
                        ? string.Create(CultureInfo.InvariantCulture, $"The color {sample} of the row {y} multiplied by its alpha {pixels[i | 3]} is beyond the range of {range}: OpenEXR stores colors premultiplied by alpha. Clip the colors or the alpha of the image first.")
                        : string.Create(CultureInfo.InvariantCulture, $"The sample {sample} of the row {y} is beyond the range of {range}. Write float samples (OpenExrPixelType.Float), or clip the image first.");
                    throw new UnsupportedImageFeatureException(message, ImageFormat.OpenExr, premultiplied ? "Premultiplied samples beyond the range of the sample type" : "Samples beyond the range of half");
                }
            }
        }

        public override void BeginPosterFrame(ImageFrame frame) => throw new InvalidOperationException("OpenEXR output has no poster frame.");

        public override void BeginFrame(ImageFrame frame, int index)
        {
            ArgumentNullException.ThrowIfNull(frame);
            if (index != 0)
                throw new InvalidOperationException("An OpenEXR image stores a single frame.");

            _frame = frame;
            _step = Step.Header;
        }

        public override void BeginComplete(int frameCount) => _step = Step.None;

        public override bool Encode(ImageOutputBuffer output)
        {
            ArgumentNullException.ThrowIfNull(output);
            switch (_step)
            {
                case Step.None:
                    return true;

                case Step.Header:
                    WriteHeader(output);
                    _step = Step.Chunks;
                    return false;

                case Step.Chunks:
                    WriteChunk(output);
                    if (_chunk < _chunkCount)
                        return false;

                    if (_offsets is not null)
                    {
                        output.AddPatch(_tableOffset, _offsets.Span);
                    }

                    _frame = null;
                    _step = Step.None;
                    return true;

                default:
                    throw new InvalidOperationException("No OpenEXR operation was begun.");
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _floats?.Dispose();
                _floats = null;
                _data?.Dispose();
                _data = null;
                _stored?.Dispose();
                _stored = null;
                _offsets?.Dispose();
                _offsets = null;
                _predicted?.Dispose();
                _predicted = null;
                _piz?.Dispose();
                _piz = null;
                if (_workers is not null)
                {
                    for (var worker = 1; worker < _workers.Length; worker++)
                    {
                        _workers[worker]?.Dispose();
                    }

                    _workers = null;
                }

                if (_batch is not null)
                {
                    foreach (var buffer in _batch)
                    {
                        buffer?.Dispose();
                    }

                    _batch = null;
                }

                _frame = null;
            }

            base.Dispose(disposing);
        }

        private void WriteHeader(ImageOutputBuffer output)
        {
            _fileStart = output.TotalBytes;
            Span<byte> preamble = stackalloc byte[ExrFormat.PreambleLength];
            ExrFormat.Magic.CopyTo(preamble);
            BinaryPrimitives.WriteInt32LittleEndian(preamble[4..], ExrFormat.Version);
            output.Write(preamble);

            // The channel list: the names, the pixel type, pLinear, three reserved bytes and the sampling
            var channels = new List<byte>();
            Span<byte> fields = stackalloc byte[16];
            foreach (var channel in _channels)
            {
                channels.AddRange(Encoding.ASCII.GetBytes(channel.Name));
                channels.Add(0);
                fields.Clear();
                BinaryPrimitives.WriteInt32LittleEndian(fields, (int)channel.PixelType);
                BinaryPrimitives.WriteInt32LittleEndian(fields[8..], 1);
                BinaryPrimitives.WriteInt32LittleEndian(fields[12..], 1);
                channels.AddRange(fields.ToArray());
            }

            channels.Add(0);
            WriteAttribute(output, "channels", "chlist", CollectionsMarshal.AsSpan(channels));
            WriteAttribute(output, "compression", "compression", [(byte)_compression]);
            Span<byte> window = stackalloc byte[16];
            BinaryPrimitives.WriteInt32LittleEndian(window[8..], _width - 1);
            BinaryPrimitives.WriteInt32LittleEndian(window[12..], _height - 1);
            WriteAttribute(output, "dataWindow", "box2i", window);
            WriteAttribute(output, "displayWindow", "box2i", window);
            WriteAttribute(output, "lineOrder", "lineOrder", [(byte)ExrLineOrder.IncreasingY]);
            Span<byte> number = stackalloc byte[8];
            BinaryPrimitives.WriteSingleLittleEndian(number, 1f);
            WriteAttribute(output, "pixelAspectRatio", "float", number[..4]);
            number.Clear();
            WriteAttribute(output, "screenWindowCenter", "v2f", number);
            BinaryPrimitives.WriteSingleLittleEndian(number, 1f);
            WriteAttribute(output, "screenWindowWidth", "float", number[..4]);
            if (_primaries != 1)
            {
                Span<byte> chromaticities = stackalloc byte[32];
                var values = ExrColorDescription.GetChromaticities(_primaries);
                for (var i = 0; i < values.Length; i++)
                {
                    BinaryPrimitives.WriteSingleLittleEndian(chromaticities[(4 * i)..], values[i]);
                }

                WriteAttribute(output, "chromaticities", "chromaticities", chromaticities);
            }

            output.Write([0]);

            // The offset table: known now without compression, patched once the chunks are written otherwise
            _tableOffset = output.TotalBytes;
            var offset = _tableOffset - _fileStart + ((long)_chunkCount * sizeof(long));
            Span<byte> entry = stackalloc byte[8];
            for (var chunk = 0; chunk < _chunkCount; chunk++)
            {
                BinaryPrimitives.WriteInt64LittleEndian(entry, _offsets is null ? offset : 0);
                output.Write(entry);
                offset += ExrFormat.ScanLineChunkHeaderLength + GetDataLength(chunk);
            }
        }

        private static void WriteAttribute(ImageOutputBuffer output, string name, string type, ReadOnlySpan<byte> value)
        {
            output.Write(Encoding.ASCII.GetBytes(name));
            output.Write([0]);
            output.Write(Encoding.ASCII.GetBytes(type));
            output.Write([0]);
            Span<byte> size = stackalloc byte[4];
            BinaryPrimitives.WriteInt32LittleEndian(size, value.Length);
            output.Write(size);
            output.Write(value);
        }

        private int GetLineCount(int chunk) => Math.Min(_linesPerChunk, _height - (chunk * _linesPerChunk));

        private int GetDataLength(int chunk) => _width * GetLineCount(chunk) * _channels.Length * ExrFormat.GetBytesPerSample(_pixelType);

        /// <summary>
        /// Gets the number of workers that code the chunks of an image: several for at least two chunks and
        /// <see cref="RowBands.MinPixelsForParallelism"/> pixels, at most one per chunk.
        /// </summary>
        private static int GetWorkerCount(int maxDegreeOfParallelism, int chunkCount, long pixelCount)
        {
            if (maxDegreeOfParallelism <= 1 || chunkCount < 2 || pixelCount < RowBands.MinPixelsForParallelism)
                return 1;

            return Math.Min(maxDegreeOfParallelism, chunkCount);
        }

        /// <summary>Rents the state of the workers other than the first one, and the buffers of the coded chunks of a batch.</summary>
        private void RentWorkers(AllocationScope scope, int workers, int batchChunks, int dataLength)
        {
            // The first worker has the state of one worker, which the session disposes
            _workers = new ChunkState?[workers];
            _workers[0] = new ChunkState { Floats = _floats, Data = _data, Predicted = _predicted, Piz = _piz };
            for (var worker = 1; worker < workers; worker++)
            {
                var state = new ChunkState();
                _workers[worker] = state;
                if (_plan is not null)
                {
                    state.Floats = scope.Rent(_width * 16, AllocationKind.Temporary, clear: false);
                }

                state.Data = scope.Rent(dataLength, AllocationKind.Temporary, clear: false);
                if (_compression == ExrCompressionMethod.Piz)
                {
                    state.Piz = new ExrPiz(scope, dataLength, encoder: true);
                }
                else if (_compression != ExrCompressionMethod.None)
                {
                    state.Predicted = scope.Rent(dataLength, AllocationKind.Temporary, clear: false);
                }
            }

            _batch = new PooledBuffer?[batchChunks];
            _batchLengths = new int[batchChunks];
            _failures = new Exception?[batchChunks];
            for (var i = 0; i < batchChunks; i++)
            {
                _batch[i] = scope.Rent(dataLength, AllocationKind.Temporary, clear: false);
            }
        }

        private void WriteChunk(ImageOutputBuffer output)
        {
            var y0 = _chunk * _linesPerChunk;
            ReadOnlySpan<byte> stored;
            if (_batch is null)
            {
                var lines = GetLineCount(_chunk);
                var data = _data!.Span[..GetDataLength(_chunk)];
                using (var lease = _frame!.GetStorage().AcquireLease())
                {
                    FillChunk(lease.Storage, _floats, y0, lines, data);
                }

                var length = Compress(_predicted, _piz, data, new ExrChunkLayout(_channels, _width, y0, lines), _stored);
                stored = length < 0 ? data : _stored!.Span[..length];
            }
            else
            {
                if (_chunk == _batchStart + _batchCount)
                {
                    CodeBatch();
                }

                var slot = _chunk - _batchStart;
                stored = _batch[slot]!.Span[.._batchLengths![slot]];
            }

            if (_offsets is not null)
            {
                BinaryPrimitives.WriteInt64LittleEndian(_offsets.Span[(_chunk * sizeof(long))..], output.TotalBytes - _fileStart);
            }

            Span<byte> header = stackalloc byte[ExrFormat.ScanLineChunkHeaderLength];
            BinaryPrimitives.WriteInt32LittleEndian(header, y0);
            BinaryPrimitives.WriteInt32LittleEndian(header[4..], stored.Length);
            output.Write(header);
            output.Write(stored);
            _chunk++;
            CancellationToken.ThrowIfCancellationRequested();
        }

        /// <summary>
        /// Codes the next chunks with the workers, each into its buffer of the batch: the error is the one of the first chunk
        /// that fails, a cancellation first.
        /// </summary>
        private void CodeBatch()
        {
            var workers = _workers!;
            var batch = _batch!;
            var lengths = _batchLengths!;
            var failures = _failures!;
            var first = _chunk;
            var count = Math.Min(batch.Length, _chunkCount - first);
            _batchStart = first;
            _batchCount = 0;
            Array.Clear(failures, 0, count);
            var cancellationToken = CancellationToken;
            var next = -1;
            var firstFailure = int.MaxValue;
            using (var lease = _frame!.GetStorage().AcquireLease())
            {
                var storage = lease.Storage;
                RowBands.RunParallel(Math.Min(workers.Length, count), worker =>
                {
                    var state = workers[worker]!;
                    while (true)
                    {
                        var k = Interlocked.Increment(ref next);
                        if (k >= count || k > Volatile.Read(ref firstFailure))
                            return;

                        try
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            var chunk = first + k;
                            var y0 = chunk * _linesPerChunk;
                            var lines = GetLineCount(chunk);
                            var data = state.Data!.Span[..GetDataLength(chunk)];
                            FillChunk(storage, state.Floats, y0, lines, data);
                            var slot = batch[k]!;
                            var length = Compress(state.Predicted, state.Piz, data, new ExrChunkLayout(_channels, _width, y0, lines), slot);
                            if (length < 0)
                            {
                                data.CopyTo(slot.Span);
                                length = data.Length;
                            }

                            lengths[k] = length;
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

            _batchCount = count;
        }

        /// <summary>Lays out the samples of the lines of a chunk: for each line, each channel in name order.</summary>
        private void FillChunk(PixelStorage storage, PooledBuffer? floats, int y0, int lines, Span<byte> data)
        {
            var sampleSize = ExrFormat.GetBytesPerSample(_pixelType);
            var position = 0;
            for (var line = 0; line < lines; line++)
            {
                var pixels = GetRow(storage.GetRowSpanCore(y0 + line), floats);
                for (var c = 0; c < _channels.Length; c++)
                {
                    var component = _components[c];
                    var samples = data.Slice(position, _width * sampleSize);
                    for (var x = 0; x < _width; x++)
                    {
                        var index = (4 * x) + component;
                        var value = GetSampleValue(pixels, index, _hasAlpha && component != 3 && GetStoredAlpha(pixels[index | 3]) != 0);
                        if (_pixelType == ExrPixelType.Half)
                        {
                            BinaryPrimitives.WriteUInt16LittleEndian(samples[(2 * x)..], BitConverter.HalfToUInt16Bits((Half)value));
                        }
                        else
                        {
                            BinaryPrimitives.WriteSingleLittleEndian(samples[(4 * x)..], (float)value);
                        }
                    }

                    position += samples.Length;
                }
            }
        }

        /// <summary>Gets the pixels of a row as <see cref="PixelFormat.RgbaFloat"/> samples, converted in <paramref name="floats"/> when the frame has another format.</summary>
        private ReadOnlySpan<float> GetRow(Span<byte> source, PooledBuffer? floats)
        {
            if (_plan is null)
                return unsafe(MemoryMarshal.Cast<byte, float>(source));

            var row = floats!.Span[..(_width * 16)];
            _plan.ConvertRow(source, row);
            return unsafe(MemoryMarshal.Cast<byte, float>(row));
        }

        /// <summary>Gets the alpha the file stores: the alpha rounded to the sample type, which the decoder divides the colors by.</summary>
        private double GetStoredAlpha(float alpha) => _pixelType == ExrPixelType.Half ? (double)(Half)alpha : alpha;

        /// <summary>
        /// Gets the value of the sample a component is written as, before its rounding to the sample type: a color is
        /// multiplied by the stored alpha of its pixel when <paramref name="premultiplied"/> (the product of a float and a
        /// half or a float is exact in double precision, so the sample is rounded once).
        /// </summary>
        private double GetSampleValue(ReadOnlySpan<float> pixels, int index, bool premultiplied)
        {
            double value = pixels[index];
            if (premultiplied)
            {
                value *= GetStoredAlpha(pixels[index | 3]);
            }

            return value;
        }

        /// <summary>
        /// Compresses a chunk into <paramref name="stored"/>, whose length is the largest chunk of samples: the length of the
        /// compressed data, or -1 when the data do not shrink and are stored as they are (section 1.5).
        /// </summary>
        private int Compress(PooledBuffer? predictedBuffer, ExrPiz? piz, ReadOnlySpan<byte> data, in ExrChunkLayout layout, PooledBuffer? stored)
        {
            if (_compression == ExrCompressionMethod.None)
                return -1;

            int length;
            switch (_compression)
            {
                case ExrCompressionMethod.Rle:
                {
                    var predicted = predictedBuffer!.Span[..data.Length];
                    ExrPredictor.Encode(data, predicted);
                    length = ExrRle.Encode(predicted, stored!.Span[..data.Length]);
                    break;
                }

                case ExrCompressionMethod.Zips or ExrCompressionMethod.Zip:
                {
                    var predicted = predictedBuffer!.Span[..data.Length];
                    ExrPredictor.Encode(data, predicted);
                    length = ExrZlib.Deflate(predicted, stored!);
                    break;
                }

                default:
                    length = piz!.Compress(data, stored!.Span, layout);
                    break;
            }

            return length < 0 || length >= data.Length ? -1 : length;
        }
    }

    /// <summary>The working memory of a worker that codes chunks: a row of floats, a chunk of samples, and the compressor's.</summary>
    private sealed class ChunkState : IDisposable
    {
        public PooledBuffer? Floats { get; set; }

        public PooledBuffer? Data { get; set; }

        public PooledBuffer? Predicted { get; set; }

        public ExrPiz? Piz { get; set; }

        public void Dispose()
        {
            Floats?.Dispose();
            Data?.Dispose();
            Predicted?.Dispose();
            Piz?.Dispose();
        }
    }
}
