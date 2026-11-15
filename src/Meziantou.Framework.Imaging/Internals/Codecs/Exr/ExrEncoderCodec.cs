using System.Buffers.Binary;
using System.Numerics;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Text;
using Meziantou.Framework.Imaging.Formats;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>The OpenEXR encoder registration: one single-part scan-line or tiled image per output.</summary>
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
    /// <see cref="Encode"/> call (1 to 256 scan lines, or one tile), then the offsets of the compressed chunks, patched over
    /// the table.
    /// </summary>
    /// <remarks>
    /// The rows of a chunk are converted to <see cref="PixelFormat.RgbaFloat"/> (in place for <see cref="RgbaFloat"/>
    /// frames), their colors multiplied by the alpha that is stored (the convention of OpenEXR; a pixel whose stored alpha is
    /// 0 keeps its color, as <see cref="ExrDecoder.Unpremultiply"/> reads it), their samples laid out channel by channel in
    /// name order, then compressed; a compressed chunk that is not
    /// smaller than its samples is stored uncompressed (compression notes, section 1.5). The private state is one chunk of
    /// samples, its compressed copy and the offset table: memory does not depend on the height beyond the table.
    /// <para>
    /// The chunks are numbered in the order of the file (<see cref="GetChunk"/>): the levels in the order of the offset
    /// table, and in a level the rows of chunks from the top (from the bottom for <see cref="ExrLineOrder.DecreasingY"/>), each
    /// row from the left; a scan-line image is one level of one column of chunks. A level below the full resolution is
    /// generated before its first chunk (<see cref="PrepareLevel"/>), as samples in name order, from the previous level, into
    /// one of four buffers reserved when the session is created: the level that a later level is generated from stays in
    /// its buffer while that one is written.
    /// </para>
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
        private readonly int _tileWidth;
        private readonly int _tileHeight;
        private readonly bool _tiled;
        private readonly ExrLevelMode _levelMode;
        private readonly bool _roundsLevelsUp;
        private readonly ExrLineOrder _lineOrder;
        private readonly Level[] _levels;
        private readonly int _chunkCount;
        private readonly int _bytesPerPixel;
        private readonly float _dwaLevel;
        private readonly bool _reducesFloats;
        private readonly bool _validatesSamples;
        private readonly bool _hasAlpha;
        private readonly PixelFormat _source;
        private readonly byte _primaries;
        private readonly PixelConversionPlan? _plan;
        private PooledBuffer? _floats;
        private PooledBuffer? _data;
        private PooledBuffer? _stored;
        private PooledBuffer? _offsets;
        private PooledBuffer? _predicted;
        private ExrPiz? _piz;
        private ExrDwaEncoder? _dwa;
        private PooledBuffer?[]? _levelBuffers;
        private PooledBuffer? _levelSource;
        private PooledBuffer? _levelRow;
        private int _preparedLevel;
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
            _source = source;
            var isSingle = PixelFormats.IsFloatingPoint(source) && PixelFormats.GetBitsPerComponent(source) == 32;
            var exact = PixelFormats.IsFloatingPoint(source) ? !isSingle : PixelFormats.GetBitsPerComponent(source) <= 8;
            var pixelType = encoder.PixelType ?? (exact ? OpenExrPixelType.Half : OpenExrPixelType.Float);
            if (pixelType == OpenExrPixelType.Half && !exact && !encoder.AllowBitDepthReduction)
                throw new UnsupportedImageFeatureException($"Half samples cannot store {source} pixels exactly. Set OpenExrEncoder.AllowBitDepthReduction to round them, or write float samples (OpenExrPixelType.Float).", ImageFormat.OpenExr, "Bit depth reduction");

            _pixelType = pixelType == OpenExrPixelType.Half ? ExrPixelType.Half : ExrPixelType.Float;
            _reducesFloats = _pixelType == ExrPixelType.Half && isSingle;

            // Integer samples and their products are within [0, 1], and the product of two halves is within the range of floats
            _validatesSamples = isSingle || (PixelFormats.IsFloatingPoint(source) && _pixelType == ExrPixelType.Half);
            _compression = (ExrCompressionMethod)(int)encoder.Compression;
            _width = options.CanvasSize.Width;
            _height = options.CanvasSize.Height;
            _primaries = options.Metadata.Cicp?.ColorPrimaries ?? 1;
            _lineOrder = (ExrLineOrder)(int)encoder.LineOrder;
            _dwaLevel = encoder.DwaCompressionLevel;
            _bytesPerPixel = PixelFormats.GetBytesPerPixel(source);
            var tiling = encoder.Tiling;
            if (tiling is null && _lineOrder == ExrLineOrder.RandomY)
                throw new ArgumentException("The line order RandomY is only valid for tiled OpenEXR output: set OpenExrEncoder.Tiling, or use IncreasingY or DecreasingY.", nameof(options));

            // The channels in name order, and the component of an RGBA pixel each one holds
            (string Name, int Component)[] channels = PixelFormats.IsGrayscale(source)
                ? PixelFormats.HasAlpha(source) ? [("A", 3), ("Y", 0)] : [("Y", 0)]
                : PixelFormats.HasAlpha(source) ? [("A", 3), ("B", 2), ("G", 1), ("R", 0)] : [("B", 2), ("G", 1), ("R", 0)];
            _hasAlpha = PixelFormats.HasAlpha(source);
            _channels = [.. channels.Select(channel => new ExrChannel(channel.Name, _pixelType, PLinear: false, XSampling: 1, YSampling: 1))];
            _components = [.. channels.Select(channel => channel.Component)];
            _tiled = tiling is not null;
            if (tiling is null)
            {
                _tileWidth = _width;
                _tileHeight = ExrFormat.GetLinesPerChunk(_compression);
                _levelMode = ExrLevelMode.OneLevel;
            }
            else
            {
                _tileWidth = tiling.TileWidth;
                _tileHeight = tiling.TileHeight;
                _levelMode = (ExrLevelMode)(int)tiling.LevelMode;
                _roundsLevelsUp = tiling.LevelRounding == OpenExrLevelRounding.Up;
            }

            _levels = CreateLevels(out var chunkCount);
            var maxWidth = Math.Min(_tileWidth, _width);
            var maxLines = Math.Min(_tileHeight, _height);
            var dataLength = (long)maxWidth * maxLines * _channels.Length * ExrFormat.GetBytesPerSample(_pixelType);
            if (dataLength > CheckedSizes.MaxBufferLength || (long)_width * 16 > CheckedSizes.MaxBufferLength || chunkCount * sizeof(long) > CheckedSizes.MaxBufferLength)
                throw CheckedSizes.CreateOverflowException(options.Configuration.Limits);

            _chunkCount = (int)chunkCount;
            var levelLengths = GetLevelBufferLengths();
            foreach (var length in levelLengths)
            {
                if (length > CheckedSizes.MaxBufferLength)
                    throw CheckedSizes.CreateOverflowException(options.Configuration.Limits);
            }

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
                if (_compression != ExrCompressionMethod.None && workers == 1)
                {
                    _stored = scope.Rent((int)dataLength, AllocationKind.Temporary, clear: false);
                }

                if (_compression != ExrCompressionMethod.None)
                {
                    _offsets = scope.Rent(_chunkCount * sizeof(long), AllocationKind.Temporary, clear: false);
                }
                (_predicted, _piz, _dwa) = RentCompressor(scope, (int)dataLength, maxWidth, maxLines);
                if (levelLengths.Length > 0)
                {
                    _levelBuffers = new PooledBuffer?[levelLengths.Length];
                    for (var i = 0; i < levelLengths.Length; i++)
                    {
                        if (levelLengths[i] > 0)
                        {
                            _levelBuffers[i] = scope.Rent((int)levelLengths[i], AllocationKind.Temporary, clear: false);
                        }
                    }

                    _levelSource = scope.Rent(_width * _channels.Length * sizeof(float), AllocationKind.Temporary, clear: false);
                    _levelRow = scope.Rent(_width * _channels.Length * sizeof(double), AllocationKind.Temporary, clear: false);
                }

                if (workers > 1)
                {
                    RentWorkers(scope, workers, batchChunks, (int)dataLength, maxWidth, maxLines);
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
                if (IsRowWithinRange(storage.GetRowSpanCore(y)))
                    continue;

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

        /// <summary>
        /// Determines whether no sample of a row can fail the checks of <see cref="ValidateRows"/>, without converting it: every
        /// alpha is within [-1, 1] (so a color multiplied by alpha is not larger than the color, and the stored alpha is the
        /// alpha rounded) and every color within the range of the sample type. A row with an infinity or a value that is not a
        /// number takes the checks, which skip some of them.
        /// </summary>
        private bool IsRowWithinRange(ReadOnlySpan<byte> row)
        {
            if (_source is not (PixelFormat.RgbaFloat or PixelFormat.RgbaHalf))
            {
                // The rows of the gray formats are checked once converted
                return false;
            }

            if (_plan is null)
            {
                // RgbaFloat pixels: 65,504 is the largest half, float.MaxValue the largest float
                var colorLimit = BitConverter.SingleToUInt32Bits(_pixelType == ExrPixelType.Half ? 65504f : float.MaxValue);
                var alphaLimit = _hasAlpha ? BitConverter.SingleToUInt32Bits(1f) : colorLimit;
                return ExrLineKernels.AreSingleMagnitudesWithin(unsafe(MemoryMarshal.Cast<byte, uint>(row[..(_width * 16)])), colorLimit, alphaLimit);
            }

            // RgbaHalf pixels written as half samples: a finite color is within the range of halves, and the checks skip the
            // colors that are not finite
            const ushort Unlimited = 0x7FFF;
            return ExrLineKernels.AreHalfMagnitudesWithin(unsafe(MemoryMarshal.Cast<byte, ushort>(row[..(_width * 8)])), Unlimited, _hasAlpha ? BitConverter.HalfToUInt16Bits(Half.One) : Unlimited);
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

                    if (_compression != ExrCompressionMethod.None)
                    {
                        output.AddPatch(_tableOffset, _offsets!.Span[..(_chunkCount * sizeof(long))]);
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
                _dwa?.Dispose();
                _dwa = null;
                _levelSource?.Dispose();
                _levelSource = null;
                _levelRow?.Dispose();
                _levelRow = null;
                if (_levelBuffers is not null)
                {
                    foreach (var buffer in _levelBuffers)
                    {
                        buffer?.Dispose();
                    }

                    _levelBuffers = null;
                }

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

        /// <summary>
        /// Gets the levels in the order of the offset table (the levels of a mipmap, the rows of the levels of a ripmap from
        /// the top, each row from the left), and the number of chunks of the file.
        /// </summary>
        private Level[] CreateLevels(out long chunkCount)
        {
            var (xLevels, yLevels) = _levelMode switch
            {
                ExrLevelMode.MipmapLevels => (GetLevelCount(Math.Max(_width, _height)), 1),
                ExrLevelMode.RipmapLevels => (GetLevelCount(_width), GetLevelCount(_height)),
                _ => (1, 1),
            };

            var levels = new Level[xLevels * yLevels];
            chunkCount = 0;
            for (var ly = 0; ly < yLevels; ly++)
            {
                for (var lx = 0; lx < xLevels; lx++)
                {
                    var (levelX, levelY) = _levelMode == ExrLevelMode.MipmapLevels ? (lx, lx) : (lx, ly);
                    var width = GetLevelSize(_width, levelX);
                    var height = GetLevelSize(_height, levelY);
                    var tilesX = (int)(((long)width + _tileWidth - 1) / _tileWidth);
                    var tilesY = (int)(((long)height + _tileHeight - 1) / _tileHeight);
                    var index = (ly * xLevels) + lx;

                    // A mipmap level comes from the previous one; a ripmap level from the one on its left, or above it
                    var sourceLevel = index == 0 ? -1 : _levelMode == ExrLevelMode.MipmapLevels || lx > 0 ? index - 1 : index - xLevels;
                    var slot = index == 0 ? -1 : _levelMode == ExrLevelMode.MipmapLevels ? (lx - 1) % 2 : lx > 0 ? (lx - 1) % 2 : 2 + ((ly - 1) % 2);
                    levels[index] = new Level(levelX, levelY, width, height, tilesX, tilesY, chunkCount, sourceLevel, slot);
                    chunkCount += (long)tilesX * tilesY;
                    if (chunkCount > int.MaxValue)
                    {
                        // The offset table is too large to be a buffer: the constructor reports it
                        chunkCount = int.MaxValue + 1L;
                        return levels;
                    }
                }
            }

            return levels;
        }

        /// <summary>Gets the size of a level along one axis (OpenEXR File Layout, tiles: halved and rounded, at least 1).</summary>
        private int GetLevelSize(int size, int level)
        {
            var result = _roundsLevelsUp ? (size + (1L << level) - 1) >> level : (long)size >> level;
            return (int)Math.Max(result, 1);
        }

        /// <summary>Gets the number of levels along one axis: floor(log2(size)) + 1, or ceil(log2(size)) + 1 with rounding up.</summary>
        private int GetLevelCount(int size)
        {
            var floor = 31 - BitOperations.LeadingZeroCount((uint)size);
            return (_roundsLevelsUp && (size & (size - 1)) != 0 ? floor + 1 : floor) + 1;
        }

        /// <summary>
        /// Gets the sizes of the buffers of the levels below the full resolution: the largest level each one holds (two
        /// buffers alternate along a row of levels, two along the first column of a ripmap).
        /// </summary>
        private long[] GetLevelBufferLengths()
        {
            var lengths = new long[_levelMode == ExrLevelMode.RipmapLevels ? 4 : 2];
            foreach (var level in _levels)
            {
                if (level.Slot >= 0)
                {
                    lengths[level.Slot] = Math.Max(lengths[level.Slot], (long)level.Width * level.Height * _channels.Length * sizeof(float));
                }
            }

            return _levels.Length > 1 ? lengths : [];
        }

        /// <summary>Gets a chunk from its position in the file.</summary>
        private Chunk GetChunk(int position)
        {
            var low = 0;
            var high = _levels.Length - 1;
            while (low < high)
            {
                var middle = (low + high + 1) / 2;
                if (_levels[middle].FirstChunk <= position)
                {
                    low = middle;
                }
                else
                {
                    high = middle - 1;
                }
            }

            var level = _levels[low];
            var local = (int)(position - level.FirstChunk);
            var row = local / level.TilesX;
            var tx = local % level.TilesX;
            var ty = _lineOrder == ExrLineOrder.DecreasingY ? level.TilesY - 1 - row : row;
            var x0 = tx * _tileWidth;
            var y0 = ty * _tileHeight;
            return new Chunk(
                Index: (int)(level.FirstChunk + ((long)ty * level.TilesX) + tx),
                Level: low,
                X0: x0,
                Y0: y0,
                Width: Math.Min(_tileWidth, level.Width - x0),
                Lines: Math.Min(_tileHeight, level.Height - y0),
                Tx: tx,
                Ty: ty);
        }

        /// <summary>Gets the position in the file after the last chunk of the level of a chunk.</summary>
        private int GetLevelEnd(int level) => level + 1 < _levels.Length ? (int)_levels[level + 1].FirstChunk : _chunkCount;

        private int GetDataLength(in Chunk chunk) => chunk.Width * chunk.Lines * _channels.Length * ExrFormat.GetBytesPerSample(_pixelType);

        private ExrChunkLayout GetLayout(in Chunk chunk) => new(_channels, chunk.Width, _tiled ? 0 : chunk.Y0, chunk.Lines);

        private void WriteHeader(ImageOutputBuffer output)
        {
            _fileStart = output.TotalBytes;
            Span<byte> preamble = stackalloc byte[ExrFormat.PreambleLength];
            ExrFormat.Magic.CopyTo(preamble);
            BinaryPrimitives.WriteInt32LittleEndian(preamble[4..], ExrFormat.Version | (_tiled ? ExrFormat.SingleTileFlag : 0));
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
            WriteAttribute(output, "lineOrder", "lineOrder", [(byte)_lineOrder]);
            Span<byte> number = stackalloc byte[8];
            BinaryPrimitives.WriteSingleLittleEndian(number, 1f);
            WriteAttribute(output, "pixelAspectRatio", "float", number[..4]);
            number.Clear();
            WriteAttribute(output, "screenWindowCenter", "v2f", number);
            BinaryPrimitives.WriteSingleLittleEndian(number, 1f);
            WriteAttribute(output, "screenWindowWidth", "float", number[..4]);
            if (_tiled)
            {
                // The tile description: the tile size, then the level mode and, in the high nibble, the rounding mode
                Span<byte> tiles = stackalloc byte[9];
                BinaryPrimitives.WriteInt32LittleEndian(tiles, _tileWidth);
                BinaryPrimitives.WriteInt32LittleEndian(tiles[4..], _tileHeight);
                tiles[8] = (byte)((int)_levelMode | (_roundsLevelsUp ? 0x10 : 0));
                WriteAttribute(output, "tiles", "tiledesc", tiles);
            }

            if (_compression is ExrCompressionMethod.Dwaa or ExrCompressionMethod.Dwab)
            {
                BinaryPrimitives.WriteSingleLittleEndian(number, _dwaLevel);
                WriteAttribute(output, "dwaCompressionLevel", "float", number[..4]);
            }

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

            // The offset table: patched once the chunks are written, or, without compression, known now and written entry by
            // entry (no buffer of the whole table)
            _tableOffset = output.TotalBytes;
            if (_offsets is not null)
            {
                var table = _offsets.Span[..(_chunkCount * sizeof(long))];
                table.Clear();
                output.Write(table);
                return;
            }

            WriteUncompressedOffsets(output);
        }

        /// <summary>
        /// Writes the offset table of uncompressed chunks, in table order: the chunks of a level follow those of the previous
        /// levels, and in a level the rows of chunks before it in the line order, then the chunks on its left.
        /// </summary>
        private void WriteUncompressedOffsets(ImageOutputBuffer output)
        {
            var headerLength = _tiled ? ExrFormat.TileChunkHeaderLength : ExrFormat.ScanLineChunkHeaderLength;
            long bytesPerPixel = _channels.Length * ExrFormat.GetBytesPerSample(_pixelType);
            var levelStart = _tableOffset - _fileStart + ((long)_chunkCount * sizeof(long));
            Span<byte> entry = stackalloc byte[sizeof(long)];
            foreach (var level in _levels)
            {
                for (var ty = 0; ty < level.TilesY; ty++)
                {
                    // The lines of the rows of chunks written before this row, and their chunk headers
                    var top = (long)ty * _tileHeight;
                    var bottom = Math.Min(top + _tileHeight, level.Height);
                    var (linesBefore, rowsBefore) = _lineOrder == ExrLineOrder.DecreasingY ? (level.Height - bottom, level.TilesY - 1 - ty) : (top, ty);
                    var rowStart = levelStart + (linesBefore * level.Width * bytesPerPixel) + ((long)rowsBefore * level.TilesX * headerLength);
                    var lines = bottom - top;
                    for (var tx = 0; tx < level.TilesX; tx++)
                    {
                        BinaryPrimitives.WriteInt64LittleEndian(entry, rowStart + ((long)tx * ((_tileWidth * lines * bytesPerPixel) + headerLength)));
                        output.Write(entry);
                    }
                }

                levelStart += ((long)level.Width * level.Height * bytesPerPixel) + ((long)level.TilesX * level.TilesY * headerLength);
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

        /// <summary>Rents the state of the compressor of a worker: a buffer of transformed samples, or a PIZ or DWA coder.</summary>
        private (PooledBuffer? Predicted, ExrPiz? Piz, ExrDwaEncoder? Dwa) RentCompressor(AllocationScope scope, int dataLength, int maxWidth, int maxLines)
        {
            switch (_compression)
            {
                case ExrCompressionMethod.None:
                    return default;

                case ExrCompressionMethod.Piz:
                    return (null, new ExrPiz(scope, dataLength, encoder: true), null);

                case ExrCompressionMethod.Dwaa or ExrCompressionMethod.Dwab:
                    return (null, null, new ExrDwaEncoder(scope, _dwaLevel, dataLength, maxWidth, maxLines, _channels.Length, UsesHuffmanAc));

                default:
                    return (scope.Rent(dataLength, AllocationKind.Temporary, clear: false), null, null);
            }
        }

        /// <summary>Gets a value indicating whether the AC symbols of DWA chunks are Huffman coded: always but for DWAA tiles (section 8.14, step 9).</summary>
        private bool UsesHuffmanAc => !(_tiled && _compression == ExrCompressionMethod.Dwaa);

        /// <summary>Rents the state of the workers other than the first one, and the buffers of the coded chunks of a batch.</summary>
        private void RentWorkers(AllocationScope scope, int workers, int batchChunks, int dataLength, int maxWidth, int maxLines)
        {
            // The first worker has the state of one worker, which the session disposes
            _workers = new ChunkState?[workers];
            _workers[0] = new ChunkState { Floats = _floats, Data = _data, Predicted = _predicted, Piz = _piz, Dwa = _dwa };
            for (var worker = 1; worker < workers; worker++)
            {
                var state = new ChunkState();
                _workers[worker] = state;
                if (_plan is not null)
                {
                    state.Floats = scope.Rent(_width * 16, AllocationKind.Temporary, clear: false);
                }

                state.Data = scope.Rent(dataLength, AllocationKind.Temporary, clear: false);
                (state.Predicted, state.Piz, state.Dwa) = RentCompressor(scope, dataLength, maxWidth, maxLines);
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
            var chunk = GetChunk(_chunk);
            if (chunk.Level != _preparedLevel)
            {
                PrepareLevel(chunk.Level);
            }

            ReadOnlySpan<byte> stored;
            if (_batch is null)
            {
                var data = _data!.Span[..GetDataLength(chunk)];
                using (var lease = _frame!.GetStorage().AcquireLease())
                {
                    FillChunk(lease.Storage, _floats, chunk, data);
                }

                var length = Compress(_predicted, _piz, _dwa, data, GetLayout(chunk), _stored);
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
                BinaryPrimitives.WriteInt64LittleEndian(_offsets.Span[(chunk.Index * sizeof(long))..], output.TotalBytes - _fileStart);
            }
            Span<byte> header = stackalloc byte[ExrFormat.TileChunkHeaderLength];
            if (_tiled)
            {
                var level = _levels[chunk.Level];
                BinaryPrimitives.WriteInt32LittleEndian(header, chunk.Tx);
                BinaryPrimitives.WriteInt32LittleEndian(header[4..], chunk.Ty);
                BinaryPrimitives.WriteInt32LittleEndian(header[8..], level.Lx);
                BinaryPrimitives.WriteInt32LittleEndian(header[12..], level.Ly);
                BinaryPrimitives.WriteInt32LittleEndian(header[16..], stored.Length);
            }
            else
            {
                header = header[..ExrFormat.ScanLineChunkHeaderLength];
                BinaryPrimitives.WriteInt32LittleEndian(header, chunk.Y0);
                BinaryPrimitives.WriteInt32LittleEndian(header[4..], stored.Length);
            }

            output.Write(header);
            output.Write(stored);
            _chunk++;
            CancellationToken.ThrowIfCancellationRequested();
        }

        /// <summary>
        /// Codes the next chunks of the current level with the workers, each into its buffer of the batch: the error is the one
        /// of the first chunk that fails, a cancellation first.
        /// </summary>
        private void CodeBatch()
        {
            var workers = _workers!;
            var batch = _batch!;
            var lengths = _batchLengths!;
            var failures = _failures!;
            var first = _chunk;
            var count = Math.Min(batch.Length, GetLevelEnd(_preparedLevel) - first);
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
                            var chunk = GetChunk(first + k);
                            var data = state.Data!.Span[..GetDataLength(chunk)];
                            FillChunk(storage, state.Floats, chunk, data);
                            var slot = batch[k]!;
                            var length = Compress(state.Predicted, state.Piz, state.Dwa, data, GetLayout(chunk), slot);
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

        /// <summary>
        /// Generates a level below the full resolution into its buffer, from the level it comes from: the area average of the
        /// samples the file stores for that level (colors premultiplied by alpha, rounded to the sample type), computed in
        /// double precision and rounded to the sample type, so that each level is the average of the samples of its parent.
        /// </summary>
        private void PrepareLevel(int index)
        {
            _preparedLevel = index;
            if (index == 0)
                return;

            var level = _levels[index];
            var source = _levels[level.SourceLevel];
            var channelCount = _channels.Length;
            var target = unsafe(MemoryMarshal.Cast<byte, float>(_levelBuffers![level.Slot]!.Span));
            var sums = unsafe(MemoryMarshal.Cast<byte, double>(_levelRow!.Span))[..(level.Width * channelCount)];
            using var lease = _frame!.GetStorage().AcquireLease();
            for (var y = 0; y < level.Height; y++)
            {
                CancellationToken.ThrowIfCancellationRequested();
                sums.Clear();

                // The rows of the source that the row covers, in units of 1 / level.Height source rows
                var top = (long)y * source.Height;
                var bottom = top + source.Height;
                for (var sy = (int)(top / level.Height); sy < source.Height && (long)sy * level.Height < bottom; sy++)
                {
                    var rowWeight = (double)(Math.Min(bottom, ((long)sy + 1) * level.Height) - Math.Max(top, (long)sy * level.Height)) / source.Height;
                    var row = ReadLevelRow(lease.Storage, source, level.SourceLevel, sy);
                    for (var x = 0; x < level.Width; x++)
                    {
                        var left = (long)x * source.Width;
                        var right = left + source.Width;
                        for (var sx = (int)(left / level.Width); sx < source.Width && (long)sx * level.Width < right; sx++)
                        {
                            var weight = rowWeight * (Math.Min(right, ((long)sx + 1) * level.Width) - Math.Max(left, (long)sx * level.Width)) / source.Width;
                            for (var c = 0; c < channelCount; c++)
                            {
                                sums[(x * channelCount) + c] += weight * row[(sx * channelCount) + c];
                            }
                        }
                    }
                }

                var line = target.Slice(y * level.Width * channelCount, level.Width * channelCount);
                for (var i = 0; i < line.Length; i++)
                {
                    line[i] = ToSampleType(sums[i]);
                }
            }
        }

        /// <summary>Gets a row of a level as the samples the file stores, in name order: computed from the frame for the full resolution.</summary>
        private ReadOnlySpan<float> ReadLevelRow(PixelStorage storage, in Level level, int index, int y)
        {
            var channelCount = _channels.Length;
            if (index != 0)
                return unsafe(MemoryMarshal.Cast<byte, float>(_levelBuffers![level.Slot]!.Span)).Slice(y * level.Width * channelCount, level.Width * channelCount);

            var row = unsafe(MemoryMarshal.Cast<byte, float>(_levelSource!.Span))[..(_width * channelCount)];
            var pixels = GetRow(storage.GetRowSpanCore(y), _floats, 0, _width);
            for (var x = 0; x < _width; x++)
            {
                for (var c = 0; c < channelCount; c++)
                {
                    var index4 = (4 * x) + _components[c];
                    row[(x * channelCount) + c] = ToSampleType(GetSampleValue(pixels, index4, _hasAlpha && _components[c] != 3 && GetStoredAlpha(pixels[index4 | 3]) != 0));
                }
            }

            return row;
        }

        /// <summary>Lays out the samples of the lines of a chunk: for each line, each channel in name order.</summary>
        private void FillChunk(PixelStorage storage, PooledBuffer? floats, in Chunk chunk, Span<byte> data)
        {
            var sampleSize = ExrFormat.GetBytesPerSample(_pixelType);
            var width = chunk.Width;
            var position = 0;
            if (chunk.Level != 0)
            {
                // A generated level holds the samples the file stores, already rounded to the sample type
                var level = _levels[chunk.Level];
                var channelCount = _channels.Length;
                var samples = unsafe(MemoryMarshal.Cast<byte, float>(_levelBuffers![level.Slot]!.Span));
                for (var line = 0; line < chunk.Lines; line++)
                {
                    var row = samples.Slice((((chunk.Y0 + line) * level.Width) + chunk.X0) * channelCount, width * channelCount);
                    for (var c = 0; c < channelCount; c++)
                    {
                        for (var x = 0; x < width; x++)
                        {
                            var value = row[(x * channelCount) + c];
                            if (_pixelType == ExrPixelType.Half)
                            {
                                BinaryPrimitives.WriteUInt16LittleEndian(data[(position + (2 * x))..], BitConverter.HalfToUInt16Bits((Half)value));
                            }
                            else
                            {
                                BinaryPrimitives.WriteSingleLittleEndian(data[(position + (4 * x))..], value);
                            }
                        }

                        position += width * sampleSize;
                    }
                }

                return;
            }

            for (var line = 0; line < chunk.Lines; line++)
            {
                var pixels = GetRow(storage.GetRowSpanCore(chunk.Y0 + line), floats, chunk.X0, width);
                if (_pixelType == ExrPixelType.Half && BitConverter.IsLittleEndian)
                {
                    var samples = unsafe(MemoryMarshal.Cast<byte, ushort>(data.Slice(position, width * _channels.Length * sampleSize)));
                    ExrLineKernels.WriteHalfSamples(pixels, samples, _components, _hasAlpha);
                    position += samples.Length * sampleSize;
                    continue;
                }

                for (var c = 0; c < _channels.Length; c++)
                {
                    var component = _components[c];
                    var samples = data.Slice(position, width * sampleSize);
                    for (var x = 0; x < width; x++)
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
        private ReadOnlySpan<float> GetRow(Span<byte> source, PooledBuffer? floats) => GetRow(source, floats, 0, _width);

        /// <summary>Gets <paramref name="count"/> pixels of a row from <paramref name="x0"/> as <see cref="PixelFormat.RgbaFloat"/> samples.</summary>
        private ReadOnlySpan<float> GetRow(Span<byte> source, PooledBuffer? floats, int x0, int count)
        {
            var pixels = source.Slice(x0 * _bytesPerPixel, count * _bytesPerPixel);
            if (_plan is null)
                return unsafe(MemoryMarshal.Cast<byte, float>(pixels));

            var row = floats!.Span[..(count * 16)];
            _plan.ConvertRow(pixels, row);
            return unsafe(MemoryMarshal.Cast<byte, float>(row));
        }

        /// <summary>Rounds a value to the sample type of the file (to a half, held in a float, for half samples).</summary>
        private float ToSampleType(double value) => _pixelType == ExrPixelType.Half ? (float)(Half)value : (float)value;

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
        private int Compress(PooledBuffer? predictedBuffer, ExrPiz? piz, ExrDwaEncoder? dwa, ReadOnlySpan<byte> data, in ExrChunkLayout layout, PooledBuffer? stored)
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

                case ExrCompressionMethod.Pxr24:
                {
                    var planes = ExrPxr24.Encode(data, predictedBuffer!.Span, layout);
                    length = ExrZlib.Deflate(predictedBuffer.Span[..planes], stored!);
                    break;
                }

                case ExrCompressionMethod.B44 or ExrCompressionMethod.B44A:
                {
                    var planes = predictedBuffer!.Span[..data.Length];
                    layout.DeinterleavePlanes(data, planes);
                    length = ExrB44.Encode(planes, stored!.Span[..data.Length], layout, flatBlocks: _compression == ExrCompressionMethod.B44A);
                    break;
                }

                case ExrCompressionMethod.Dwaa or ExrCompressionMethod.Dwab:
                    length = dwa!.Compress(data, stored!.Span[..data.Length], layout, UsesHuffmanAc);
                    break;

                default:
                    length = piz!.Compress(data, stored!.Span, layout);
                    break;
            }

            return length < 0 || length >= data.Length ? -1 : length;
        }
    }

    /// <summary>A resolution level, in the order of the offset table.</summary>
    /// <param name="Lx">The level number along x.</param>
    /// <param name="Ly">The level number along y.</param>
    /// <param name="Width">The width of the level.</param>
    /// <param name="Height">The height of the level.</param>
    /// <param name="TilesX">The number of chunks of a row of the level.</param>
    /// <param name="TilesY">The number of rows of chunks of the level.</param>
    /// <param name="FirstChunk">The index of the first chunk of the level, in the table and in the file.</param>
    /// <param name="SourceLevel">The index of the level it is generated from, or -1 for the full resolution.</param>
    /// <param name="Slot">The buffer that holds the level, or -1 for the full resolution (the frame).</param>
    [StructLayout(LayoutKind.Auto)]
    private readonly record struct Level(int Lx, int Ly, int Width, int Height, int TilesX, int TilesY, long FirstChunk, int SourceLevel, int Slot);

    /// <summary>A chunk: its index in the offset table, its level, and its area in the level.</summary>
    [StructLayout(LayoutKind.Auto)]
    private readonly record struct Chunk(int Index, int Level, int X0, int Y0, int Width, int Lines, int Tx, int Ty);

    /// <summary>The working memory of a worker that codes chunks: a row of floats, a chunk of samples, and the compressor's.</summary>
    private sealed class ChunkState : IDisposable
    {
        public PooledBuffer? Floats { get; set; }

        public PooledBuffer? Data { get; set; }

        public PooledBuffer? Predicted { get; set; }

        public ExrPiz? Piz { get; set; }

        public ExrDwaEncoder? Dwa { get; set; }

        public void Dispose()
        {
            Floats?.Dispose();
            Data?.Dispose();
            Predicted?.Dispose();
            Piz?.Dispose();
            Dwa?.Dispose();
        }
    }
}
