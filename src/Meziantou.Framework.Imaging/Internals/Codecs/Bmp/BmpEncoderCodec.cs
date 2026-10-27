using System.Buffers.Binary;
using System.Runtime.InteropServices;
using Meziantou.Framework.Imaging.Formats;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>The BMP encoder registration: one still image per output, written by one streaming session.</summary>
internal sealed class BmpEncoderCodec : ImageEncoderCodec
{
    public static BmpEncoderCodec Instance { get; } = new();

    public override ImageFormat Format => ImageFormat.Bmp;

    public override ImageEncoderSession CreateSession(ImageEncoderSessionOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return new Session(options);
    }

    /// <summary>
    /// The encoding state of one BMP output: the file header, the DIB header, its masks and its palette, then the rows in
    /// bands of at most <see cref="RowsPerCall"/> rows.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The 16-, 24- and 32-bit layouts convert one row at a time, read from the frame in file order (from the last row up
    /// for bottom-up output), so they need neither seeking nor a copy of the image: private state is one converted row.
    /// </para>
    /// <para>
    /// The indexed layouts need the palette before the first pixel, and a run-length stream needs its length before the
    /// headers (the output is not seekable). They run bounded passes over the borrowed frame before anything is written:
    /// the histogram of its colors, the palette (exact when the colors fit; otherwise the image is rejected unless color
    /// reduction is allowed), the mapping of every pixel into an index plane of one byte per pixel, in display order so
    /// that dithering does not depend on the row order, and for run-length output the measurement of the encoded rows. The
    /// headers and the rows are then written from the plane.
    /// </para>
    /// <para>
    /// Every byte of padding is written as zero and the declared <c>bfSize</c>/<c>biSizeImage</c> match the bytes produced.
    /// </para>
    /// </remarks>
    private sealed class Session : ImageEncoderSession
    {
        /// <summary>The largest number of rows processed by one <see cref="Encode"/> call.</summary>
        internal const int RowsPerCall = 64;

        private readonly BmpEncoder _encoder;
        private readonly PixelFormat _sourceFormat;
        private readonly BmpPixelLayout _layout;
        private readonly Rgba64? _background;
        private readonly bool _isIndexed;
        private readonly bool _runLength;
        private readonly bool _topDown;
        private readonly int _width;
        private readonly int _height;
        private readonly int _bitsPerPixel;
        private readonly int _dibLength;
        private readonly int _maskLength;
        private readonly int _paletteCapacity;
        private readonly int _rowLength;
        private readonly int[]? _fixedPalette;
        private PooledBuffer? _band;
        private PooledBuffer? _keys;
        private PooledBuffer? _plane;
        private PooledBuffer? _encodedRow;
        private PaletteQuantizer? _quantizer;
        private PaletteRowMapper? _mapper;
        private ImageFrame? _frame;
        private Step _step;
        private int _row;
        private long _imageLength;

        public Session(ImageEncoderSessionOptions options)
            : base(options)
        {
            _sourceFormat = options.PixelFormat;
            _encoder = (BmpEncoder)options.Encoder;
            var hasAlpha = PixelFormats.HasAlpha(_sourceFormat);
            _layout = _encoder.PixelLayout == BmpPixelLayout.Auto ? (hasAlpha ? BmpPixelLayout.Bgra32 : BmpPixelLayout.Bgr24) : _encoder.PixelLayout;
            _isIndexed = _layout is BmpPixelLayout.Indexed1 or BmpPixelLayout.Indexed4 or BmpPixelLayout.Indexed8;
            _runLength = _encoder.Compression == BmpCompression.RunLength;
            _topDown = _encoder.RowOrder == BmpRowOrder.TopDown;

            // Settings that contradict each other are a caller error, whatever the image
            if (_runLength && _layout is not (BmpPixelLayout.Indexed4 or BmpPixelLayout.Indexed8))
                throw new ArgumentException($"BmpCompression.RunLength requires BmpPixelLayout.Indexed4 or BmpPixelLayout.Indexed8 (BI_RLE4 and BI_RLE8 store palette indexes); the layout is {_layout}.", nameof(options));

            if (_runLength && _topDown)
                throw new ArgumentException("BmpCompression.RunLength requires BmpRowOrder.BottomUp: a run-length encoded bitmap cannot store top-down rows.", nameof(options));

            if (PixelFormats.GetBitsPerComponent(_sourceFormat) > 8 && !_encoder.AllowBitDepthReduction)
                throw new UnsupportedImageFeatureException($"BMP output stores 8-bit samples, so encoding {_sourceFormat} pixels would discard precision. Set BmpEncoder.AllowBitDepthReduction to reduce the samples to 8 bits (nearest rounding), or convert the image explicitly with CloneAs.", ImageFormat.Bmp, "Bit depth reduction");

            if (_layout is BmpPixelLayout.Bgr565 or BmpPixelLayout.Bgr555 && !_encoder.AllowBitDepthReduction)
                throw new UnsupportedImageFeatureException($"The BMP layout {_layout} stores 5 or 6 bits per channel, so encoding {_sourceFormat} pixels would discard precision. Set BmpEncoder.AllowBitDepthReduction to reduce the samples (nearest rounding), or select BmpPixelLayout.Bgr24.", ImageFormat.Bmp, "Bit depth reduction");

            _background = _encoder.BackgroundColor;
            PixelConverter.ValidateBackground(_background);
            _bitsPerPixel = _layout switch
            {
                BmpPixelLayout.Indexed1 => 1,
                BmpPixelLayout.Indexed4 => 4,
                BmpPixelLayout.Indexed8 => 8,
                BmpPixelLayout.Bgr565 or BmpPixelLayout.Bgr555 => 16,
                BmpPixelLayout.Bgra32 => 32,
                _ => 24,
            };

            // BITMAPV4HEADER declares the alpha mask of the 32-bit layout; 5-6-5 stores its three masks after a 40-byte header
            _dibLength = _layout == BmpPixelLayout.Bgra32 ? BmpFormat.V4HeaderLength : BmpFormat.InfoHeaderLength;
            _maskLength = _layout == BmpPixelLayout.Bgr565 ? BmpFormat.BitFieldsMaskLength : 0;
            _fixedPalette = _isIndexed && _encoder.Quantization.Palette is { } palette ? GetFixedPalette(palette, 1 << _bitsPerPixel, nameof(options)) : null;
            _paletteCapacity = _isIndexed ? _fixedPalette?.Length ?? Math.Min(_encoder.Quantization.MaxColors, 1 << _bitsPerPixel) : 0;
            _width = options.CanvasSize.Width;
            _height = options.CanvasSize.Height;

            var scope = options.Scope;
            var rowLength = BmpFormat.GetRowLength(_width, _bitsPerPixel);
            if (!_runLength)
            {
                // The length of a run-length stream is only known once its rows are measured
                _imageLength = rowLength * _height;
                EnsureFileLengthFits(GetPixelDataOffset(_paletteCapacity));
            }

            // Every buffer is one array: the converted row, and for indexed layouts the row of color keys, the index plane
            // and the scratch row that measures a run-length stream
            var bandLength = _layout == BmpPixelLayout.Bgra32 ? 0 : (long)_width * 3;
            var keysLength = _isIndexed ? (long)_width * sizeof(int) : 0;
            var planeLength = _isIndexed ? (long)_width * _height : 0;
            var encodedRowLength = _runLength ? BmpRunLengthEncoder.GetMaxEncodedRowLength(_width) : 0;
            if (Math.Max(Math.Max(rowLength, bandLength), Math.Max(Math.Max(keysLength, planeLength), encodedRowLength)) > CheckedSizes.MaxBufferLength)
                throw CheckedSizes.CreateOverflowException(scope.Limits);

            _rowLength = (int)rowLength;
            try
            {
                // One row of RGB pixels (32-bit rows are converted straight into the output buffer)
                if (bandLength > 0)
                {
                    _band = scope.Rent((int)bandLength, AllocationKind.Temporary, clear: false);
                }

                if (_isIndexed)
                {
                    _keys = scope.Rent((int)keysLength, AllocationKind.Temporary, clear: false);
                    _quantizer = new PaletteQuantizer(scope);
                    _mapper = new PaletteRowMapper(scope, _width, _encoder.Quantization.Dithering, _encoder.Quantization.DitherStrength);
                    _plane = scope.Rent((int)planeLength, AllocationKind.Temporary, clear: false);
                }

                if (_runLength)
                {
                    _encodedRow = scope.Rent((int)encodedRowLength, AllocationKind.Temporary, clear: false);
                }
            }
            catch
            {
                ReleaseBuffers();
                throw;
            }
        }

        private enum Step
        {
            None,
            Histogram,
            Map,
            Measure,
            Header,
            Pixels,
            Complete,
        }

        public override void ValidateFrame(ImageFrame frame, bool isPoster)
        {
            ArgumentNullException.ThrowIfNull(frame);

            // Alpha is never discarded silently: without a background every pixel must be opaque
            if (_layout == BmpPixelLayout.Bgra32 || !PixelFormats.HasAlpha(_sourceFormat) || _background is not null)
                return;

            using var lease = frame.GetStorage().AcquireLease();
            for (var y = 0; y < lease.Height; y++)
            {
                var x = PixelConverter.IndexOfNonOpaque(_sourceFormat, lease.GetRowBytes(y));
                if (x >= 0)
                    throw new UnsupportedImageFeatureException(string.Create(CultureInfo.InvariantCulture, $"The selected BMP layout {_layout} has no alpha channel and the pixel ({x}, {y}) is not fully opaque. Set BmpEncoder.BackgroundColor to composite the pixels over an opaque color, select BmpPixelLayout.Bgra32, or flatten the image explicitly."), ImageFormat.Bmp, "Alpha removal");
            }
        }

        public override void BeginPosterFrame(ImageFrame frame) => throw new InvalidOperationException("BMP output has no poster frame.");

        public override void BeginFrame(ImageFrame frame, int index)
        {
            ArgumentNullException.ThrowIfNull(frame);
            if (index != 0)
                throw new InvalidOperationException("A BMP image stores a single frame.");

            _frame = frame;
            _row = 0;
            _step = _isIndexed ? Step.Histogram : Step.Header;
        }

        public override void BeginComplete(int frameCount) => _step = Step.Complete;

        public override bool Encode(ImageOutputBuffer output)
        {
            ArgumentNullException.ThrowIfNull(output);
            switch (_step)
            {
                case Step.Histogram:
                    if (!AnalyzeRows())
                        return false;

                    BuildPalette();
                    _row = 0;
                    _mapper!.Reset();
                    _step = Step.Map;
                    return false;

                case Step.Map:
                    if (!MapRows())
                        return false;

                    _row = 0;
                    if (_runLength)
                    {
                        _imageLength = BmpRunLengthEncoder.EndOfBitmapLength;
                        _step = Step.Measure;
                        return false;
                    }

                    _step = Step.Header;
                    return false;

                case Step.Measure:
                    if (!MeasureRows())
                        return false;

                    EnsureFileLengthFits(GetPixelDataOffset(_quantizer!.PaletteCount));
                    _row = 0;
                    _step = Step.Header;
                    return false;

                case Step.Header:
                    WriteHeaders(output);
                    _row = 0;
                    _step = Step.Pixels;
                    return false;

                case Step.Pixels:
                    if (_isIndexed)
                    {
                        EncodeIndexedBand(output);
                    }
                    else
                    {
                        EncodeSampleBand(output);
                    }

                    if (_row < _height)
                        return false;

                    _frame = null;
                    _step = Step.None;
                    return true;

                case Step.Complete:
                    _step = Step.None;
                    return true;

                default:
                    throw new InvalidOperationException("No BMP operation was begun.");
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                ReleaseBuffers();
                _frame = null;
            }

            base.Dispose(disposing);
        }

        private void ReleaseBuffers()
        {
            _band?.Dispose();
            _band = null;
            _keys?.Dispose();
            _keys = null;
            _plane?.Dispose();
            _plane = null;
            _encodedRow?.Dispose();
            _encodedRow = null;
            _quantizer?.Dispose();
            _quantizer = null;
            _mapper?.Dispose();
            _mapper = null;
        }

        /// <summary>Gets the offset of the pixel data: the two headers, the masks of the 5-6-5 layout and the palette.</summary>
        private int GetPixelDataOffset(int paletteEntries)
            => BmpFormat.FileHeaderLength + _dibLength + _maskLength + (paletteEntries * BmpFormat.PaletteEntryLength);

        private void EnsureFileLengthFits(int pixelDataOffset)
        {
            var totalLength = pixelDataOffset + _imageLength;
            if (totalLength > uint.MaxValue)
                throw new UnsupportedImageFeatureException(string.Create(CultureInfo.InvariantCulture, $"The BMP file would be {totalLength} bytes, more than the 4,294,967,295 bytes the bfSize field can declare; the canvas is {_width}x{_height}."), ImageFormat.Bmp, "Canvas size");
        }

        /// <summary>Gets the row of the frame stored at a position of the file.</summary>
        private int GetDisplayRow(int storedRow) => _topDown ? storedRow : _height - 1 - storedRow;

        /// <summary>Reads source rows into the histogram, a band per call.</summary>
        /// <returns><see langword="true"/> when every row was added.</returns>
        private bool AnalyzeRows()
        {
            var quantizer = _quantizer!;
            if (_row == 0)
            {
                quantizer.Begin(_paletteCapacity);
            }

            using var lease = _frame!.GetStorage().AcquireLease();
            var keys = unsafe(MemoryMarshal.Cast<byte, int>(_keys!.Span))[.._width];
            var end = Math.Min(_height, _row + RowsPerCall);
            for (; _row < end; _row++)
            {
                LoadKeys(in lease, _row, keys);
                quantizer.Add(keys);
            }

            CancellationToken.ThrowIfCancellationRequested();
            return _row == _height;
        }

        /// <summary>Builds the palette; an image whose colors do not fit is only accepted when color reduction is allowed.</summary>
        private void BuildPalette()
        {
            var quantizer = _quantizer!;
            if (_fixedPalette is not null)
            {
                quantizer.SetPalette(_fixedPalette);
                if (!quantizer.IsExact && !_encoder.AllowColorReduction)
                    throw new UnsupportedImageFeatureException("The image has colors that are not in BmpEncoder.Quantization.Palette. Set BmpEncoder.AllowColorReduction to map them to the palette, or select a layout without a palette.", ImageFormat.Bmp, "Color reduction");

                return;
            }

            quantizer.BuildPalette(_paletteCapacity);
            if (!quantizer.IsExact && !_encoder.AllowColorReduction)
                throw new UnsupportedImageFeatureException(string.Create(CultureInfo.InvariantCulture, $"The image has more than {_paletteCapacity} distinct colors, the most the palette of the BMP layout {_layout} can hold with BmpEncoder.Quantization.MaxColors = {_encoder.Quantization.MaxColors}. Set BmpEncoder.AllowColorReduction to reduce the colors to a palette, or select a layout without a palette."), ImageFormat.Bmp, "Color reduction");
        }

        /// <summary>Maps the rows to palette indexes in display order (dithering diffuses its error downward), a band per call.</summary>
        /// <returns><see langword="true"/> when every row was mapped.</returns>
        private bool MapRows()
        {
            using var lease = _frame!.GetStorage().AcquireLease();
            var keys = unsafe(MemoryMarshal.Cast<byte, int>(_keys!.Span))[.._width];
            var plane = _plane!.Span;
            var end = Math.Min(_height, _row + RowsPerCall);
            for (; _row < end; _row++)
            {
                LoadKeys(in lease, _row, keys);
                _mapper!.MapRow(_quantizer!, keys, plane.Slice(_row * _width, _width), transparentIndex: 0, _row);
            }

            CancellationToken.ThrowIfCancellationRequested();
            return _row == _height;
        }

        /// <summary>Adds the encoded length of the rows of a run-length stream, a band per call.</summary>
        /// <returns><see langword="true"/> when every row was measured.</returns>
        private bool MeasureRows()
        {
            var plane = _plane!.Span;
            var scratch = _encodedRow!.Span;
            var end = Math.Min(_height, _row + RowsPerCall);
            for (; _row < end; _row++)
            {
                _imageLength += BmpRunLengthEncoder.EncodeRow(plane.Slice(GetDisplayRow(_row) * _width, _width), _bitsPerPixel == 4, scratch);
            }

            CancellationToken.ThrowIfCancellationRequested();
            return _row == _height;
        }

        /// <summary>
        /// Reduces the entries of a fixed palette as the pixels are reduced: an entry that is not opaque is composited over the
        /// background.
        /// </summary>
        /// <exception cref="ArgumentException">An entry is not opaque without background, or the palette does not fit the layout.</exception>
        private int[] GetFixedPalette(ImagePalette palette, int capacity, string paramName)
        {
            var keys = new List<int>(palette.Count);
            Span<byte> color = stackalloc byte[4];
            Span<byte> opaque = stackalloc byte[3];
            foreach (var entry in palette.Colors)
            {
                if (entry.A != byte.MaxValue && _background is null)
                    throw new ArgumentException($"BmpEncoder.Quantization.Palette has a color that is not opaque, and the BMP layout {_layout} has no alpha channel. Set BmpEncoder.BackgroundColor to composite it over an opaque color, or use an opaque palette.", paramName);

                color[0] = entry.R;
                color[1] = entry.G;
                color[2] = entry.B;
                color[3] = entry.A;
                PixelConverter.ConvertRow(PixelFormat.Rgba32, color, PixelFormat.Rgb24, opaque, _background, ImageFormat.Bmp);
                var key = (opaque[0] << 16) | (opaque[1] << 8) | opaque[2];
                if (!keys.Contains(key))
                {
                    keys.Add(key);
                }
            }

            if (keys.Count > capacity)
                throw new ArgumentException(string.Create(CultureInfo.InvariantCulture, $"BmpEncoder.Quantization.Palette has {keys.Count} colors, and the palette of the BMP layout {_layout} holds at most {capacity}."), paramName);

            return [.. keys];
        }

        /// <summary>
        /// Converts a source row to color keys: alpha flattened onto the background by the shared converter, 16-bit samples
        /// reduced to 8 bits (nearest), gray replicated.
        /// </summary>
        private void LoadKeys(scoped in PixelLease lease, int y, Span<int> keys)
        {
            var band = _band!.Span[..(_width * 3)];
            PixelConverter.ConvertRow(_sourceFormat, lease.GetRowBytes(y)[..(_width * PixelFormats.GetBytesPerPixel(_sourceFormat))], PixelFormat.Rgb24, band, _background, ImageFormat.Bmp);
            for (var x = 0; x < keys.Length; x++)
            {
                var pixel = band.Slice(3 * x, 3);
                keys[x] = (pixel[0] << 16) | (pixel[1] << 8) | pixel[2];
            }
        }

        private void WriteHeaders(ImageOutputBuffer output)
        {
            var palette = _quantizer is null ? default : _quantizer.Palette;
            var pixelDataOffset = GetPixelDataOffset(palette.Length);
            var span = output.GetSpan(pixelDataOffset);
            span[..pixelDataOffset].Clear();
            BmpFormat.Magic.CopyTo(span);
            BinaryPrimitives.WriteUInt32LittleEndian(span[2..], (uint)(pixelDataOffset + _imageLength));
            BinaryPrimitives.WriteUInt32LittleEndian(span[10..], (uint)pixelDataOffset);

            var compression = _layout switch
            {
                BmpPixelLayout.Bgra32 or BmpPixelLayout.Bgr565 => BmpFormat.CompressionBitFields,
                BmpPixelLayout.Indexed8 when _runLength => BmpFormat.CompressionRle8,
                BmpPixelLayout.Indexed4 when _runLength => BmpFormat.CompressionRle4,
                _ => BmpFormat.CompressionRgb,
            };

            var dib = span[BmpFormat.FileHeaderLength..];
            BinaryPrimitives.WriteUInt32LittleEndian(dib, (uint)_dibLength);
            BinaryPrimitives.WriteInt32LittleEndian(dib[4..], _width);
            BinaryPrimitives.WriteInt32LittleEndian(dib[8..], _topDown ? -_height : _height);
            BinaryPrimitives.WriteUInt16LittleEndian(dib[12..], 1);
            BinaryPrimitives.WriteUInt16LittleEndian(dib[14..], (ushort)_bitsPerPixel);
            BinaryPrimitives.WriteUInt32LittleEndian(dib[16..], compression);
            BinaryPrimitives.WriteUInt32LittleEndian(dib[20..], (uint)_imageLength);
            if (Options.Metadata.Resolution is { } resolution)
            {
                var (x, y) = ResolutionConversion.ToBmpPixelsPerMeter(resolution);
                BinaryPrimitives.WriteInt32LittleEndian(dib[24..], x);
                BinaryPrimitives.WriteInt32LittleEndian(dib[28..], y);
            }

            // biClrUsed: the palette only stores the entries the image uses
            BinaryPrimitives.WriteUInt32LittleEndian(dib[32..], (uint)palette.Length);
            if (_layout == BmpPixelLayout.Bgra32)
            {
                // BITMAPV4HEADER: explicit BGRA masks and the sRGB color space; the endpoints and gamma fields stay zero
                BinaryPrimitives.WriteUInt32LittleEndian(dib[40..], 0x00FF_0000);
                BinaryPrimitives.WriteUInt32LittleEndian(dib[44..], 0x0000_FF00);
                BinaryPrimitives.WriteUInt32LittleEndian(dib[48..], 0x0000_00FF);
                BinaryPrimitives.WriteUInt32LittleEndian(dib[52..], 0xFF00_0000);
                BinaryPrimitives.WriteUInt32LittleEndian(dib[56..], BmpFormat.ColorSpaceSrgb);
            }
            else if (_layout == BmpPixelLayout.Bgr565)
            {
                // BI_BITFIELDS after a BITMAPINFOHEADER: the red, green and blue masks follow the header
                BinaryPrimitives.WriteUInt32LittleEndian(dib[_dibLength..], 0xF800);
                BinaryPrimitives.WriteUInt32LittleEndian(dib[(_dibLength + 4)..], 0x07E0);
                BinaryPrimitives.WriteUInt32LittleEndian(dib[(_dibLength + 8)..], 0x001F);
            }

            // RGBQUAD entries: blue, green, red and a reserved zero
            var entries = dib[(_dibLength + _maskLength)..];
            for (var i = 0; i < palette.Length; i++)
            {
                var entry = entries.Slice(i * BmpFormat.PaletteEntryLength, BmpFormat.PaletteEntryLength);
                entry[0] = (byte)palette[i];
                entry[1] = (byte)(palette[i] >> 8);
                entry[2] = (byte)(palette[i] >> 16);
            }

            output.Advance(pixelDataOffset);
        }

        /// <summary>Converts and writes a band of 16-, 24- or 32-bit rows, in file order.</summary>
        private void EncodeSampleBand(ImageOutputBuffer output)
        {
            var frame = _frame ?? throw new InvalidOperationException("No frame was begun.");
            var bytesPerPixel = PixelFormats.GetBytesPerPixel(_sourceFormat);
            var samples = _width * (_bitsPerPixel / 8);
            using var lease = frame.GetStorage().AcquireLease();
            var end = Math.Min(_height, _row + RowsPerCall);
            for (; _row < end; _row++)
            {
                CancellationToken.ThrowIfCancellationRequested();
                var source = lease.GetRowBytes(GetDisplayRow(_row))[..(_width * bytesPerPixel)];
                var destination = output.GetSpan(_rowLength);
                if (_layout == BmpPixelLayout.Bgra32)
                {
                    PixelConverter.ConvertRow(_sourceFormat, source, PixelFormat.Bgra32, destination[..samples], background: null, ImageFormat.Bmp);
                }
                else
                {
                    var band = _band!.Span[..(_width * 3)];
                    PixelConverter.ConvertRow(_sourceFormat, source, PixelFormat.Rgb24, band, _background, ImageFormat.Bmp);
                    switch (_layout)
                    {
                        case BmpPixelLayout.Bgr565:
                            for (var x = 0; x < _width; x++)
                            {
                                var pixel = band.Slice(x * 3, 3);
                                BinaryPrimitives.WriteUInt16LittleEndian(destination[(x * 2)..], (ushort)((Reduce(pixel[0], 31) << 11) | (Reduce(pixel[1], 63) << 5) | Reduce(pixel[2], 31)));
                            }

                            break;

                        case BmpPixelLayout.Bgr555:
                            for (var x = 0; x < _width; x++)
                            {
                                var pixel = band.Slice(x * 3, 3);
                                BinaryPrimitives.WriteUInt16LittleEndian(destination[(x * 2)..], (ushort)((Reduce(pixel[0], 31) << 10) | (Reduce(pixel[1], 31) << 5) | Reduce(pixel[2], 31)));
                            }

                            break;

                        default:
                            PixelConverter.SwapRedAndBlue24(band, destination);
                            break;
                    }
                }

                destination[samples.._rowLength].Clear();
                output.Advance(_rowLength);
            }

            // The nearest of the 2^n levels: round(value * maximum / 255), the inverse of the exact expansion decoders apply
            static int Reduce(int value, int maximum) => ((value * maximum) + 127) / 255;
        }

        /// <summary>Writes a band of rows of palette indexes from the index plane, in file order.</summary>
        private void EncodeIndexedBand(ImageOutputBuffer output)
        {
            var plane = _plane!.Span;
            var end = Math.Min(_height, _row + RowsPerCall);
            for (; _row < end; _row++)
            {
                CancellationToken.ThrowIfCancellationRequested();
                var indexes = plane.Slice(GetDisplayRow(_row) * _width, _width);
                if (_runLength)
                {
                    var encoded = output.GetSpan(_encodedRow!.Length);
                    output.Advance(BmpRunLengthEncoder.EncodeRow(indexes, _bitsPerPixel == 4, encoded));
                    continue;
                }

                var destination = output.GetSpan(_rowLength)[.._rowLength];
                switch (_bitsPerPixel)
                {
                    case 8:
                        indexes.CopyTo(destination);
                        destination[_width..].Clear();
                        break;

                    case 4:
                        // The high-order half of a byte is the left pixel
                        destination.Clear();
                        for (var x = 0; x < _width; x++)
                        {
                            destination[x >> 1] |= (byte)((x & 1) == 0 ? indexes[x] << 4 : indexes[x]);
                        }

                        break;

                    default:
                        // The most significant bit of a byte is the left pixel
                        destination.Clear();
                        for (var x = 0; x < _width; x++)
                        {
                            if (indexes[x] != 0)
                            {
                                destination[x >> 3] |= (byte)(0x80 >> (x & 7));
                            }
                        }

                        break;
                }

                output.Advance(_rowLength);
            }

            if (_runLength && _row == _height)
            {
                BmpRunLengthEncoder.WriteEndOfBitmap(output.GetSpan(BmpRunLengthEncoder.EndOfBitmapLength));
                output.Advance(BmpRunLengthEncoder.EndOfBitmapLength);
            }
        }
    }
}
