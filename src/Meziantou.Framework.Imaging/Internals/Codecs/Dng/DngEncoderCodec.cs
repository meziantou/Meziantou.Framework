using System.Buffers.Binary;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text;
using Meziantou.Framework.Imaging.Formats;
using Meziantou.Framework.Imaging.Metadata;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The DNG encoder registration (DNG specification 1.7.1): a little-endian TIFF file whose first image file directory is a
/// linear raw image, with the camera profile of linear BT.709 RGB.
/// </summary>
internal sealed class DngEncoderCodec : ImageEncoderCodec
{
    /// <summary>The unique camera model (<c>UniqueCameraModel</c>) and the software the encoder writes.</summary>
    public const string CameraModel = "Meziantou.Framework.Imaging";

    /// <summary>The light source of the calibration of the camera profile: D65 (EXIF LightSource 21).</summary>
    public const int IlluminantD65 = 21;

    // The denominator of the signed rational numbers of the matrices: six decimal places
    private const int MatrixDenominator = 1_000_000;

    // The number of bytes of a strip, when the raw image is not tiled
    private const int StripBytes = 64 * 1024;

    // The tile size of lossless JPEG data, when it is not set
    private const int DefaultJpegTileSize = 256;

    public static DngEncoderCodec Instance { get; } = new();

    public override ImageFormat Format => ImageFormat.Dng;

    public override ImageEncoderSession CreateSession(ImageEncoderSessionOptions options) => new Session(options);

    /// <summary>
    /// Gets the forward matrix of the camera profile: linear BT.709 RGB (D65) to CIE XYZ (D50), the inverse of the
    /// transform with which the decoder renders XYZ (D50), so that the white of the camera, (1, 1, 1), is the white of D50.
    /// </summary>
    internal static DngMatrix GetForwardMatrix() => DngColorProfile.GetXyzD50ToOutput().TryInvert()!;

    /// <summary>Gets the color matrix of the camera profile: CIE XYZ (D65, Y = 1 for the white) to linear BT.709 RGB.</summary>
    internal static DngMatrix GetColorMatrix()
    {
        var d65 = DngColorProfile.ToXyz(0.3127, 0.3290);
        var rgbToXyz = DngColorProfile.GetRgbToXyz(0.64, 0.33, 0.30, 0.60, 0.15, 0.06, d65);
        return rgbToXyz.TryInvert()!;
    }

    /// <summary>Gets the linearization table of sRGB samples: the sRGB transfer function of IEC 61966-2-1, to 16-bit linear values.</summary>
    internal static ushort[] GetSrgbLinearizationTable(int bits)
    {
        var count = 1 << bits;
        var table = new ushort[count];
        for (var i = 0; i < count; i++)
        {
            var v = (double)i / (count - 1);
            var linear = v <= 0.04045 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4);
            table[i] = (ushort)Math.Round(linear * ushort.MaxValue, MidpointRounding.AwayFromZero);
        }

        return table;
    }

    /// <summary>
    /// The encoding state of one DNG output: the TIFF header, the blocks (strips or tiles) of the raw image, one per
    /// <see cref="Encode"/> call, then the image file directory, whose offset the header gets by a patch.
    /// </summary>
    private sealed class Session : ImageEncoderSession
    {
        private readonly ComponentRowReader _reader;
        private readonly int _width;
        private readonly int _height;
        private readonly int _samplesPerPixel;
        private readonly int _bitsPerSample;
        private readonly DngCompression _compression;
        private readonly bool _isTiled;
        private readonly int _blockWidth;
        private readonly int _blockHeight;
        private readonly int _blocksAcross;
        private readonly int _blocksDown;
        private readonly bool _hasLinearizationTable;
        private readonly uint[] _offsets = [];
        private readonly uint[] _byteCounts = [];
        private AllocationCharge? _tablesCharge;
        private PooledBuffer? _block;
        private PooledBuffer? _components;
        private Image? _jpegTile;
        private ImageFrame? _frame;
        private Step _step;
        private int _blockIndex;
        private uint _floatingPointWhiteLevel = 1;

        public Session(ImageEncoderSessionOptions options)
            : base(options)
        {
            var encoder = (DngEncoder)options.Encoder;
            _reader = new ComponentRowReader(options.PixelFormat);
            if (_reader.ColorComponents == 4)
                throw DngFormat.Unsupported("DNG stores no CMYK samples.", "CMYK samples");

            _width = options.CanvasSize.Width;
            _height = options.CanvasSize.Height;
            _samplesPerPixel = _reader.ColorComponents;
            _bitsPerSample = _reader.Depth;
            _compression = encoder.Compression ?? (_reader.IsFloatingPoint ? DngCompression.Deflate : DngCompression.LosslessJpeg);
            if (_compression == DngCompression.LosslessJpeg && _reader.IsFloatingPoint)
                throw DngFormat.Unsupported("Lossless JPEG stores integer samples: write the floating-point samples with DngCompression.Deflate or DngCompression.None.", "Lossless JPEG of floating-point samples");

            if (_compression == DngCompression.Deflate && !_reader.IsFloatingPoint)
                throw DngFormat.Unsupported("DNG compresses floating-point samples with Deflate: write the integer samples with DngCompression.LosslessJpeg or DngCompression.None.", "Deflate of integer samples");

            _hasLinearizationTable = !_reader.IsFloatingPoint && options.Metadata.TransferFunction != ColorTransferFunction.Linear;
            var bytesPerPixel = _samplesPerPixel * _bitsPerSample / 8;
            var tileSize = encoder.TileSize ?? (_compression == DngCompression.LosslessJpeg ? DefaultJpegTileSize : (int?)null);
            if (tileSize is { } size)
            {
                // A tile is never larger than the image, as the DNG SDK writes them: LibRaw misreads the lossless JPEG tiles
                // that are wider than the image
                _isTiled = true;
                _blockWidth = Math.Min(size, _width);
                _blockHeight = Math.Min(size, _height);
            }
            else
            {
                _blockWidth = _width;
                _blockHeight = (int)Math.Clamp(StripBytes / Math.Max(1L, (long)_width * bytesPerPixel), 1, _height);
            }

            _blocksAcross = (int)(((long)_width + _blockWidth - 1) / _blockWidth);
            _blocksDown = (int)(((long)_height + _blockHeight - 1) / _blockHeight);
            var blockLength = (long)_blockWidth * _blockHeight * bytesPerPixel;
            if (blockLength > CheckedSizes.MaxBufferLength || (long)_width * 4 > CheckedSizes.MaxBufferLength)
                throw CheckedSizes.CreateOverflowException(options.Configuration.Limits);

            if ((long)_blocksAcross * _blocksDown > int.MaxValue / 8)
                throw CheckedSizes.CreateOverflowException(options.Configuration.Limits);

            var scope = options.Scope;
            try
            {
                // The offsets and byte counts of the blocks, and their copies in the values of the directory
                var blocks = _blocksAcross * _blocksDown;
                _tablesCharge = scope.Charge((long)blocks * 4 * sizeof(uint), AllocationKind.Temporary);
                _offsets = new uint[blocks];
                _byteCounts = new uint[blocks];
                _block = scope.Rent((int)blockLength, AllocationKind.Temporary, clear: true);
                _components = scope.Rent(_width * 4, AllocationKind.Temporary, clear: false);
                if (_compression == DngCompression.LosslessJpeg)
                {
                    var tileFormat = (_samplesPerPixel, _bitsPerSample) switch
                    {
                        (1, 8) => PixelFormat.Gray8,
                        (1, _) => PixelFormat.Gray16,
                        (_, 8) => PixelFormat.Rgb24,
                        _ => PixelFormat.Rgb48,
                    };

                    _jpegTile = Image.CreateForDecoder(tileFormat, options.Configuration, new Size(_blockWidth, _blockHeight), scope);
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
            Blocks,
            Directory,
        }

        public override void ValidateFrame(ImageFrame frame, bool isPoster)
        {
            ArgumentNullException.ThrowIfNull(frame);
            using var lease = frame.GetStorage().AcquireLease();
            var max = 0f;
            for (var y = 0; y < _height; y++)
            {
                var row = lease.GetRowBytes(y);
                if (!_reader.IsOpaque(row, _width))
                    throw DngFormat.Unsupported(string.Create(CultureInfo.InvariantCulture, $"The pixels of the row {y} are not all fully opaque, and a DNG raw image has no transparency. Flatten the image first."), "Transparency");

                if (_reader.IsFloatingPoint)
                {
                    var values = unsafe(MemoryMarshal.Cast<byte, float>(_components!.RawBuffer.AsSpan(0, _width * 4)));
                    for (var c = 0; c < _samplesPerPixel; c++)
                    {
                        _reader.ReadComponent(row, _width, c, values);
                        foreach (var value in values)
                        {
                            // Linear values are light: not negative, finite
                            if (!(value >= 0 && float.IsFinite(value)))
                                throw DngFormat.Unsupported(string.Create(CultureInfo.InvariantCulture, $"A sample of the row {y} is {value}: the linear values of a DNG raw image are finite and not negative. Clamp the colors of the image first."), "Negative or non-finite samples");

                            max = Math.Max(max, value);
                        }
                    }
                }
            }

            // The white level of floating-point samples is an integer: the highlights above 1 keep their value through the
            // baseline exposure, which brings the white level back to 1
            if (max > uint.MaxValue)
                throw DngFormat.Unsupported(string.Create(CultureInfo.InvariantCulture, $"The largest sample of the image is {max}, above the largest white level of a DNG file."), "Samples above the white level");

            _floatingPointWhiteLevel = Math.Max(1u, (uint)Math.Ceiling(max));
        }

        public override void BeginPosterFrame(ImageFrame frame) => throw new InvalidOperationException("DNG output has no poster frame.");

        public override void BeginFrame(ImageFrame frame, int index)
        {
            ArgumentNullException.ThrowIfNull(frame);
            if (index != 0)
                throw new InvalidOperationException("A DNG file stores a single image.");

            _frame = frame;
            _step = Step.Header;
        }

        public override void BeginComplete(int frameCount) => _step = Step.None;

        public override bool Encode(ImageOutputBuffer output)
        {
            ArgumentNullException.ThrowIfNull(output);
            switch (_step)
            {
                case Step.Header:
                {
                    // Little-endian TIFF; the offset of the directory is patched once it is written
                    var header = output.GetSpan(8);
                    "II"u8.CopyTo(header);
                    BinaryPrimitives.WriteUInt16LittleEndian(header[2..], 42);
                    BinaryPrimitives.WriteUInt32LittleEndian(header[4..], 0);
                    output.Advance(8);
                    _step = Step.Blocks;
                    return false;
                }

                case Step.Blocks:
                    WriteBlock(output);
                    _blockIndex++;
                    if (_blockIndex == _blocksAcross * _blocksDown)
                    {
                        _step = Step.Directory;
                    }

                    return false;

                case Step.Directory:
                    WriteDirectory(output);
                    _frame = null;
                    _step = Step.None;
                    return true;

                case Step.None:
                    return true;

                default:
                    throw new InvalidOperationException("No DNG operation was begun.");
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _block?.Dispose();
                _block = null;
                _components?.Dispose();
                _components = null;
                _jpegTile?.Dispose();
                _jpegTile = null;
                _tablesCharge?.Dispose();
                _tablesCharge = null;
                _frame = null;
            }

            base.Dispose(disposing);
        }

        /// <summary>Writes one strip or tile: its samples, compressed, at an even offset.</summary>
        private void WriteBlock(ImageOutputBuffer output)
        {
            if (output.TotalBytes % 2 != 0)
            {
                output.Write((ReadOnlySpan<byte>)[0]);
            }

            var start = output.TotalBytes;
            var blockX = _blockIndex % _blocksAcross * _blockWidth;
            var blockY = _blockIndex / _blocksAcross * _blockHeight;

            // A strip ends with the image; a tile is padded with the last column and row of the image
            var rows = _isTiled ? _blockHeight : Math.Min(_blockHeight, _height - blockY);
            var bytesPerPixel = _samplesPerPixel * _bitsPerSample / 8;
            var rowLength = _blockWidth * bytesPerPixel;
            var block = _block!.RawBuffer.AsSpan(0, rows * rowLength);
            using (var lease = _frame!.GetStorage().AcquireLease())
            {
                for (var row = 0; row < rows; row++)
                {
                    var y = Math.Min(blockY + row, _height - 1);
                    StoreRow(lease.GetRowBytes(y), blockX, block.Slice(row * rowLength, rowLength));
                }
            }

            switch (_compression)
            {
                case DngCompression.None:
                    output.Write(block);
                    break;

                case DngCompression.Deflate:
                {
                    using var destination = new BufferWriterStream(output);
                    using (var zlib = new ZLibStream(destination, CompressionLevel.Optimal, leaveOpen: true))
                    {
                        zlib.Write(block);
                    }

                    break;
                }

                case DngCompression.LosslessJpeg:
                    WriteJpegTile(output, block, rowLength);
                    break;

                default:
                    throw new InvalidOperationException("The DNG compression is not valid.");
            }

            var length = output.TotalBytes - start;
            if (output.TotalBytes > uint.MaxValue)
                throw DngFormat.Unsupported("The DNG file would be larger than the 4 GiB its 32-bit offsets can address.", "File size");

            _offsets[_blockIndex] = (uint)start;
            _byteCounts[_blockIndex] = (uint)length;
        }

        /// <summary>
        /// Writes the samples of a row of a block in the byte order of the file: the components of the pixels from
        /// <paramref name="blockX"/>, the last pixel of the image repeated past its right edge.
        /// </summary>
        private void StoreRow(ReadOnlySpan<byte> pixels, int blockX, Span<byte> destination)
        {
            var count = Math.Min(_blockWidth, _width - blockX);
            pixels = pixels[(blockX * PixelFormats.GetBytesPerPixel(_reader.Format))..];
            for (var c = 0; c < _samplesPerPixel; c++)
            {
                if (_reader.IsFloatingPoint)
                {
                    var values = unsafe(MemoryMarshal.Cast<byte, float>(_components!.RawBuffer.AsSpan(0, count * 4)));
                    _reader.ReadComponent(pixels, count, c, values);
                    for (var x = 0; x < _blockWidth; x++)
                    {
                        var value = values[Math.Min(x, count - 1)];
                        BinaryPrimitives.WriteSingleLittleEndian(destination[(((x * _samplesPerPixel) + c) * 4)..], value);
                    }
                }
                else
                {
                    var values = unsafe(MemoryMarshal.Cast<byte, ushort>(_components!.RawBuffer.AsSpan(0, count * 2)));
                    _reader.ReadComponent(pixels, count, c, values);
                    for (var x = 0; x < _blockWidth; x++)
                    {
                        var value = values[Math.Min(x, count - 1)];
                        if (_bitsPerSample == 8)
                        {
                            destination[(x * _samplesPerPixel) + c] = (byte)value;
                        }
                        else
                        {
                            BinaryPrimitives.WriteUInt16LittleEndian(destination[(((x * _samplesPerPixel) + c) * 2)..], value);
                        }
                    }
                }
            }
        }

        /// <summary>Writes a tile as a lossless JPEG datastream of one component per sample (the JPEG encoder of the library).</summary>
        private void WriteJpegTile(ImageOutputBuffer output, ReadOnlySpan<byte> block, int rowLength)
        {
            var tile = Image.GetFrameForDecoder(_jpegTile!, 0);
            using (var lease = tile.GetStorage().AcquireLease())
            {
                for (var y = 0; y < _blockHeight; y++)
                {
                    var row = lease.GetRowBytes(y);
                    var source = block.Slice(y * rowLength, rowLength);
                    if (_bitsPerSample == 8 || BitConverter.IsLittleEndian)
                    {
                        source.CopyTo(row);
                    }
                    else
                    {
                        for (var i = 0; i < rowLength; i += 2)
                        {
                            BinaryPrimitives.WriteUInt16BigEndian(row[i..], BinaryPrimitives.ReadUInt16LittleEndian(source[i..]));
                        }
                    }
                }
            }

            var size = new Size(_blockWidth, _blockHeight);
            var encoder = new JpegEncoder { Process = JpegEncodingProcess.Lossless, MetadataHandling = MetadataHandling.Strip };
            var capabilities = ImageOutputCapabilities.ForWriter(new ImageWriterOptions(size) { Encoder = encoder, ExpectedFrameCount = 1, Configuration = Options.Configuration });
            var metadata = MetadataWritePlan.Create(metadata: null, ImageFormat.Jpeg, MetadataHandling.Strip, size, tile.PixelFormat);
            var options = new ImageEncoderSessionOptions(encoder, capabilities, size, tile.PixelFormat, ExpectedFrameCount: 1, metadata, Animation: null, Options.Configuration, Options.Scope);
            using var session = JpegEncoderCodec.Instance.CreateSession(options);
            session.CancellationToken = CancellationToken;
            session.BeginFrame(tile, 0);
            while (!session.Encode(output))
            {
            }

            session.BeginComplete(frameCount: 1);
            while (!session.Encode(output))
            {
            }
        }

        private void WriteDirectory(ImageOutputBuffer output)
        {
            if (output.TotalBytes % 2 != 0)
            {
                output.Write((ReadOnlySpan<byte>)[0]);
            }

            var metadata = Options.Metadata;
            var isColor = _samplesPerPixel == 3;
            var fields = new List<TiffWriteField>
            {
                Longs(TiffFormat.NewSubfileTypeTag, [DngFormat.SubfileMain]),
                Longs(TiffFormat.ImageWidthTag, [(uint)_width]),
                Longs(TiffFormat.ImageLengthTag, [(uint)_height]),
                Shorts(TiffFormat.BitsPerSampleTag, [.. Enumerable.Repeat((ushort)_bitsPerSample, _samplesPerPixel)]),
                Shorts(TiffFormat.CompressionTag, [(ushort)(_compression switch
                {
                    DngCompression.LosslessJpeg => TiffFormat.CompressionJpeg,
                    DngCompression.Deflate => TiffFormat.CompressionAdobeDeflate,
                    _ => TiffFormat.CompressionNone,
                })]),
                Shorts(TiffFormat.PhotometricInterpretationTag, [TiffFormat.PhotometricLinearRaw]),
                Ascii(DngFormat.MakeTag, CameraModel),
                Ascii(DngFormat.ModelTag, CameraModel),
                Shorts(TiffFormat.OrientationTag, [(ushort)metadata.Orientation]),
                Shorts(TiffFormat.SamplesPerPixelTag, [(ushort)_samplesPerPixel]),
                Shorts(TiffFormat.PlanarConfigurationTag, [1]),
                Ascii(TiffFormat.SoftwareTag, CameraModel),
                Bytes(DngFormat.DngVersionTag, TiffFieldType.Byte, [1, 4, 0, 0]),
                Bytes(DngFormat.DngBackwardVersionTag, TiffFieldType.Byte, _reader.IsFloatingPoint ? [1, 4, 0, 0] : [1, 1, 0, 0]),
                Ascii(DngFormat.UniqueCameraModelTag, CameraModel),
            };

            if (_isTiled)
            {
                fields.Add(Longs(TiffFormat.TileWidthTag, [(uint)_blockWidth]));
                fields.Add(Longs(TiffFormat.TileLengthTag, [(uint)_blockHeight]));
                fields.Add(Longs(TiffFormat.TileOffsetsTag, _offsets));
                fields.Add(Longs(TiffFormat.TileByteCountsTag, _byteCounts));
            }
            else
            {
                fields.Add(Longs(TiffFormat.StripOffsetsTag, _offsets));
                fields.Add(Longs(TiffFormat.RowsPerStripTag, [(uint)_blockHeight]));
                fields.Add(Longs(TiffFormat.StripByteCountsTag, _byteCounts));
            }

            if (_reader.IsFloatingPoint)
            {
                fields.Add(Shorts(TiffFormat.SampleFormatTag, [.. Enumerable.Repeat((ushort)TiffFormat.SampleFormatFloat, _samplesPerPixel)]));
                if (_floatingPointWhiteLevel > 1)
                {
                    fields.Add(Longs(DngFormat.WhiteLevelTag, [.. Enumerable.Repeat(_floatingPointWhiteLevel, _samplesPerPixel)]));
                    fields.Add(SignedRational(DngFormat.BaselineExposureTag, Math.Log2(_floatingPointWhiteLevel)));
                }
            }
            else
            {
                // The white level is the largest linear value: of the table, or of the samples
                var white = _hasLinearizationTable ? ushort.MaxValue : (uint)_reader.MaxValue;
                fields.Add(Longs(DngFormat.WhiteLevelTag, [.. Enumerable.Repeat(white, _samplesPerPixel)]));
            }

            if (_hasLinearizationTable)
            {
                fields.Add(Shorts(DngFormat.LinearizationTableTag, GetSrgbLinearizationTable(_bitsPerSample)));
            }

            if (isColor)
            {
                fields.Add(Matrix(DngFormat.ColorMatrix1Tag, GetColorMatrix()));
                fields.Add(Rationals(DngFormat.AsShotNeutralTag, [1, 1, 1]));
                fields.Add(Shorts(DngFormat.CalibrationIlluminant1Tag, [IlluminantD65]));
                fields.Add(Matrix(DngFormat.ForwardMatrix1Tag, GetForwardMatrix()));
            }

            // The image is already rendered: the profile has no tone curve (an identity curve rather than the default one of
            // the readers), and the readers do not subtract a black level of their own
            fields.Add(Ascii(DngFormat.ProfileNameTag, CameraModel));
            fields.Add(Floats(DngFormat.ProfileToneCurveTag, [0, 0, 1, 1]));
            fields.Add(Longs(DngFormat.DefaultBlackRenderTag, [1]));

            // The copy of the XMP packet in the values of the directory
            var xmp = metadata.XmpProfile;
            using var xmpCharge = xmp is null ? null : Options.Scope.Charge(xmp.Data.Length, AllocationKind.Temporary);
            if (xmp is not null)
            {
                fields.Add(Bytes(TiffFormat.XmpTag, TiffFieldType.Byte, xmp.Data.Span.ToArray()));
            }

            fields.Sort(static (left, right) => left.Tag.CompareTo(right.Tag));

            // The table, then the values of more than four bytes, each at an even offset
            var directoryOffset = output.TotalBytes;
            var tableLength = 2 + (fields.Count * 12) + 4;
            var valueOffsets = new long[fields.Count];
            var valuesLength = 0L;
            for (var i = 0; i < fields.Count; i++)
            {
                if (fields[i].Data.Length <= 4)
                    continue;

                valuesLength += valuesLength % 2;
                valueOffsets[i] = directoryOffset + tableLength + valuesLength;
                valuesLength += fields[i].Data.Length;
            }

            var total = tableLength + valuesLength + (valuesLength % 2);
            if (directoryOffset + total > uint.MaxValue)
                throw DngFormat.Unsupported("The DNG file would be larger than the 4 GiB its 32-bit offsets can address.", "File size");

            var span = output.GetSpan((int)total)[..(int)total];
            span.Clear();
            BinaryPrimitives.WriteUInt16LittleEndian(span, (ushort)fields.Count);
            for (var i = 0; i < fields.Count; i++)
            {
                var field = fields[i];
                var entry = span.Slice(2 + (i * 12), 12);
                BinaryPrimitives.WriteUInt16LittleEndian(entry, field.Tag);
                BinaryPrimitives.WriteUInt16LittleEndian(entry[2..], (ushort)field.Type);
                BinaryPrimitives.WriteUInt32LittleEndian(entry[4..], (uint)field.Count);
                if (field.Data.Length <= 4)
                {
                    field.Data.Span.CopyTo(entry[8..]);
                }
                else
                {
                    BinaryPrimitives.WriteUInt32LittleEndian(entry[8..], (uint)valueOffsets[i]);
                    field.Data.Span.CopyTo(span[(int)(valueOffsets[i] - directoryOffset)..]);
                }
            }

            output.Advance((int)total);
            Span<byte> pointer = stackalloc byte[4];
            BinaryPrimitives.WriteUInt32LittleEndian(pointer, (uint)directoryOffset);
            output.AddPatch(4, pointer);
        }

        private static TiffWriteField Bytes(ushort tag, TiffFieldType type, byte[] data) => new(tag, type, (ulong)data.Length, data);

        private static TiffWriteField Ascii(ushort tag, string value)
        {
            var data = new byte[value.Length + 1];
            Encoding.ASCII.GetBytes(value, data);
            return new(tag, TiffFieldType.Ascii, (ulong)data.Length, data);
        }

        private static TiffWriteField Shorts(ushort tag, ReadOnlySpan<ushort> values)
        {
            var data = new byte[values.Length * 2];
            for (var i = 0; i < values.Length; i++)
            {
                BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(i * 2), values[i]);
            }

            return new(tag, TiffFieldType.Short, (ulong)values.Length, data);
        }

        private static TiffWriteField Shorts(ushort tag, ReadOnlySpan<int> values)
        {
            var shorts = new ushort[values.Length];
            for (var i = 0; i < values.Length; i++)
            {
                shorts[i] = (ushort)values[i];
            }

            return Shorts(tag, shorts);
        }

        private static TiffWriteField Longs(ushort tag, ReadOnlySpan<uint> values)
        {
            var data = new byte[values.Length * 4];
            for (var i = 0; i < values.Length; i++)
            {
                BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(i * 4), values[i]);
            }

            return new(tag, TiffFieldType.Long, (ulong)values.Length, data);
        }

        private static TiffWriteField Floats(ushort tag, ReadOnlySpan<float> values)
        {
            var data = new byte[values.Length * 4];
            for (var i = 0; i < values.Length; i++)
            {
                BinaryPrimitives.WriteSingleLittleEndian(data.AsSpan(i * 4), values[i]);
            }

            return new(tag, TiffFieldType.Float, (ulong)values.Length, data);
        }

        private static TiffWriteField Rationals(ushort tag, ReadOnlySpan<uint> numerators)
        {
            var data = new byte[numerators.Length * 8];
            for (var i = 0; i < numerators.Length; i++)
            {
                BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(i * 8), numerators[i]);
                BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan((i * 8) + 4), 1);
            }

            return new(tag, TiffFieldType.Rational, (ulong)numerators.Length, data);
        }

        private static TiffWriteField SignedRational(ushort tag, double value)
        {
            var data = new byte[8];
            BinaryPrimitives.WriteInt32LittleEndian(data, (int)Math.Round(value * MatrixDenominator, MidpointRounding.AwayFromZero));
            BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(4), MatrixDenominator);
            return new(tag, TiffFieldType.SRational, 1, data);
        }

        /// <summary>A 3 x 3 matrix of signed rational numbers, row by row, with six decimal places.</summary>
        private static TiffWriteField Matrix(ushort tag, DngMatrix matrix)
        {
            var data = new byte[9 * 8];
            for (var i = 0; i < 9; i++)
            {
                var value = Math.Round(matrix[i / 3, i % 3] * MatrixDenominator, MidpointRounding.AwayFromZero);
                BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(i * 8), (int)value);
                BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan((i * 8) + 4), MatrixDenominator);
            }

            return new(tag, TiffFieldType.SRational, 9, data);
        }
    }
}
