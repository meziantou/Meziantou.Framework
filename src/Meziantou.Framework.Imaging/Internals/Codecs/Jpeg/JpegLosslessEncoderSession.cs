using System.Buffers.Binary;
using System.Numerics;
using System.Runtime.InteropServices;
using Meziantou.Framework.Imaging.Formats;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The writer of a lossless JPEG (ITU-T T.81 Annex H): one interleaved scan of the samples of every component, each sample
/// predicted from its decoded neighbors, and the difference coded with Huffman tables computed from the image (SOF3) or
/// with arithmetic coding (SOF11).
/// </summary>
/// <remarks>
/// <para>
/// Gray pixel formats are one component; color pixel formats are three RGB components (identifiers <c>R</c>, <c>G</c>,
/// <c>B</c> and an Adobe APP14 segment with the transform 0, without a JFIF segment, which would mean YCbCr). Every
/// component has its own Huffman or conditioning table, and sampling factors of 1, so an MCU is one pixel. The samples
/// are read from the frame as 16-bit samples and reduced to the precision of the frame (nearest rounding).
/// </para>
/// <para>
/// Prediction (T.81 H.1.2.1): the first sample of the scan, and of each restart interval, is predicted by
/// <c>2^(P - 1)</c>; the rest of the first line by <c>Ra</c>; the first sample of the other lines by <c>Rb</c>; the other
/// samples by the selected predictor. The difference is taken modulo 2^16, from -32,768 to 32,767 (Huffman coding codes
/// -32,768 as the category 16).
/// </para>
/// <para>
/// Huffman coding: the samples are coded twice, once to count the categories of each component, from which its table is
/// built (T.81 K.2), then to write them. Arithmetic coding (T.81 H.1.4.3, as the ISO reference software codes it): each
/// difference is coded with the binary decisions of a DC difference, in the statistics of one of 25 contexts given by the
/// classes of the differences on the left (<c>Da</c>, 0 at the start of a line) and above (<c>Db</c>, 0 on the first line
/// of the scan or of a restart interval), and the magnitude bins of the small or large class of <c>Db</c>, with the
/// default conditioning <c>L = 0</c>, <c>U = 1</c>.
/// </para>
/// <para>
/// A restart interval is a whole number of lines (T.81 H.1.1): <see cref="JpegEncoder.RestartInterval"/> must be a
/// multiple of the width.
/// </para>
/// </remarks>
internal sealed class JpegLosslessEncoderSession : ImageEncoderSession
{
    private const byte MarkerSoi = 0xD8;
    private const byte MarkerEoi = 0xD9;
    private const byte MarkerSof3 = 0xC3;
    private const byte MarkerSof11 = 0xCB;
    private const byte MarkerDht = 0xC4;
    private const byte MarkerSos = 0xDA;
    private const byte MarkerDri = 0xDD;
    private const byte MarkerRst0 = 0xD0;
    private const byte MarkerApp14 = 0xEE;

    // The pixels coded per call of the bit writer: each one is at most 16 + 16 bits per component, doubled for byte stuffing
    private const int PixelsPerChunk = 1024;
    private const int MaxPixelBytes = 3 * 2 * 4;

    // The statistics bins of a component (arithmetic coding): S0, SS, SP and SN for each of the 5 x 5 classes of Da and
    // Db, then the 15 category bins and the 15 bit bins of the small, then of the large magnitudes of Db
    private const int SignZeroBins = 25 * 4;
    private const int MagnitudeBins = 15;
    private const int BinCount = SignZeroBins + (4 * MagnitudeBins);

    private readonly JpegEncoder _encoder;
    private readonly PixelFormat _sourceFormat;
    private readonly PixelFormat _workFormat;
    private readonly Rgba64? _background;
    private readonly int _components;
    private readonly int _precision;
    private readonly int _predictor;
    private readonly int _width;
    private readonly int _height;
    private readonly int _restartInterval;
    private readonly JpegMetadataWriter _metadata;
    private readonly uint[][] _histograms;
    private readonly JpegHuffmanEncoder?[] _tables;
    private readonly JpegArithmeticEncoder? _arithmetic;
    private readonly byte[][] _statistics = [];
    private PooledBuffer? _row;
    private PooledBuffer? _lines;
    private ImageFrame? _frame;
    private Step _step;
    private int _metadataIndex;
    private int _y;
    private bool _frameWritten;
    private bool _counting;
    private JpegBitWriter _bits;

    public JpegLosslessEncoderSession(ImageEncoderSessionOptions options)
        : base(options)
    {
        _encoder = (JpegEncoder)options.Encoder;
        _sourceFormat = options.PixelFormat;
        _precision = JpegEncoderCodec.GetPrecision(_encoder, _sourceFormat, nameof(options));
        _predictor = _encoder.LosslessPredictor;
        _components = PixelFormats.IsGrayscale(_sourceFormat) ? 1 : 3;
        _background = PixelFormats.HasAlpha(_sourceFormat) ? _encoder.BackgroundColor : null;
        _workFormat = _components == 1 ? PixelFormat.Gray16 : PixelFormat.Rgb48;
        _width = options.CanvasSize.Width;
        _height = options.CanvasSize.Height;
        _restartInterval = _encoder.RestartInterval;
        if (_restartInterval % _width != 0)
            throw new ArgumentException(string.Create(CultureInfo.InvariantCulture, $"A restart interval of a lossless JPEG is a whole number of lines: set JpegEncoder.RestartInterval to a multiple of the width ({_width} pixels), not {_restartInterval}."), nameof(options));

        _metadata = new JpegMetadataWriter(options.Metadata);
        _histograms = new uint[_components][];
        _tables = new JpegHuffmanEncoder?[_components];
        if (_encoder.EntropyCoding == JpegEntropyCoding.Arithmetic)
        {
            _arithmetic = new JpegArithmeticEncoder();
            _statistics = new byte[_components][];
            for (var c = 0; c < _components; c++)
            {
                _statistics[c] = new byte[BinCount];
            }
        }
        else
        {
            for (var c = 0; c < _components; c++)
            {
                _histograms[c] = new uint[256];
            }
        }

        // The source row as 16-bit samples, then per component the previous and current lines of samples and the
        // differences of the line above (Db)
        var rowBytes = (long)_width * PixelFormats.GetBytesPerPixel(_workFormat);
        var lineBytes = (long)_width * _components * ((2 * sizeof(ushort)) + sizeof(int));
        if (rowBytes > CheckedSizes.MaxBufferLength || lineBytes > CheckedSizes.MaxBufferLength)
            throw CheckedSizes.CreateOverflowException(options.Scope.Limits);

        try
        {
            _row = options.Scope.Rent((int)rowBytes, AllocationKind.Temporary, clear: false);
            _lines = options.Scope.Rent((int)lineBytes, AllocationKind.Temporary, clear: true);
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
        Header,
        Metadata,
        Tables,
        Count,
        ScanHeader,
        Scan,
        Trailer,
    }

    public override void ValidateFrame(ImageFrame frame, bool isPoster)
    {
        ArgumentNullException.ThrowIfNull(frame);
        if (!PixelFormats.HasAlpha(_sourceFormat) || _background is not null)
            return;

        using var lease = frame.GetStorage().AcquireLease();
        for (var y = 0; y < lease.Height; y++)
        {
            var x = PixelConverter.IndexOfNonOpaque(_sourceFormat, lease.GetRowBytes(y));
            if (x >= 0)
                throw new UnsupportedImageFeatureException(string.Create(CultureInfo.InvariantCulture, $"JPEG output has no alpha channel and the pixel ({x}, {y}) is not fully opaque. Set JpegEncoder.BackgroundColor to composite the pixels over an opaque color, or flatten the image explicitly."), ImageFormat.Jpeg, "Alpha removal");
        }
    }

    public override void BeginPosterFrame(ImageFrame frame) => throw new InvalidOperationException("JPEG output has no poster frame.");

    public override void BeginFrame(ImageFrame frame, int index)
    {
        ArgumentNullException.ThrowIfNull(frame);
        if (index != 0 || _frameWritten)
            throw new InvalidOperationException("JPEG output stores a single frame.");

        _frame = frame;
        _frameWritten = true;
        _step = Step.Header;
    }

    public override void BeginComplete(int frameCount)
    {
        if (frameCount != 1 || _step != Step.None)
            throw new InvalidOperationException("JPEG output stores exactly one frame.");

        _step = Step.Trailer;
    }

    public override bool Encode(ImageOutputBuffer output)
    {
        ArgumentNullException.ThrowIfNull(output);
        switch (_step)
        {
            case Step.Header:
                WriteMarker(output, MarkerSoi);
                if (_components == 1)
                {
                    JpegMetadataWriter.WriteJfif(output, Options.Metadata);
                }
                else
                {
                    WriteAdobe(output);
                }

                _step = Step.Metadata;
                return false;

            case Step.Metadata:
                if (_metadataIndex < _metadata.Count)
                {
                    _metadata.Write(_metadataIndex++, output);
                    return false;
                }

                _step = Step.Tables;
                goto case Step.Tables;

            case Step.Tables:
                WriteFrameHeader(output);
                WriteRestartInterval(output);
                _y = 0;
                _counting = _arithmetic is null;
                _step = _counting ? Step.Count : Step.ScanHeader;
                return false;

            case Step.Count:
                CodeRow(output);
                if (++_y < _height)
                    return false;

                for (var c = 0; c < _components; c++)
                {
                    _tables[c] = JpegHuffmanCodeBuilder.BuildAnnexK(_histograms[c]);
                }

                _counting = false;
                _step = Step.ScanHeader;
                return false;

            case Step.ScanHeader:
                if (_arithmetic is null)
                {
                    WriteHuffmanTables(output);
                }

                WriteScanHeader(output);
                _y = 0;
                _bits.Reset();
                _step = Step.Scan;
                return false;

            case Step.Scan:
                CodeRow(output);
                if (++_y < _height)
                    return false;

                if (_arithmetic is { } arithmetic)
                {
                    arithmetic.Flush();
                    output.Write(arithmetic.Output);
                    arithmetic.ClearOutput();
                }
                else
                {
                    var end = output.GetSpan(2);
                    var position = 0;
                    _bits.Pad(end, ref position);
                    output.Advance(position);
                }

                _frame = null;
                _step = Step.None;
                ReleaseBuffers();
                return true;

            case Step.Trailer:
                WriteMarker(output, MarkerEoi);
                _step = Step.None;
                return true;

            default:
                throw new InvalidOperationException("No JPEG encoding operation is in progress.");
        }
    }

    protected override void Dispose(bool disposing)
    {
        _frame = null;
        ReleaseBuffers();
        base.Dispose(disposing);
    }

    private static void WriteMarker(ImageOutputBuffer output, byte marker) => output.Write([0xFF, marker]);

    private void ReleaseBuffers()
    {
        _row?.Dispose();
        _row = null;
        _lines?.Dispose();
        _lines = null;
    }

    /// <summary>Writes the Adobe APP14 segment that says that three components are RGB (transform 0, Adobe Technical Note #5116 section 18).</summary>
    private static void WriteAdobe(ImageOutputBuffer output)
    {
        Span<byte> payload = stackalloc byte[12];
        "Adobe"u8.CopyTo(payload);
        BinaryPrimitives.WriteUInt16BigEndian(payload[5..], 100);
        BinaryPrimitives.WriteUInt16BigEndian(payload[7..], 0);
        BinaryPrimitives.WriteUInt16BigEndian(payload[9..], 0);
        payload[11] = 0;
        JpegMetadataWriter.WriteSegment(output, MarkerApp14, payload);
    }

    private void WriteFrameHeader(ImageOutputBuffer output)
    {
        // Precision, height, width, components (identifiers 'R', 'G', 'B' for color, sampling 1x1, no quantization table)
        Span<byte> payload = stackalloc byte[6 + (3 * 3)];
        payload[0] = (byte)_precision;
        BinaryPrimitives.WriteUInt16BigEndian(payload[1..], (ushort)_height);
        BinaryPrimitives.WriteUInt16BigEndian(payload[3..], (ushort)_width);
        payload[5] = (byte)_components;
        for (var c = 0; c < _components; c++)
        {
            payload[6 + (3 * c)] = _components == 1 ? (byte)1 : (byte)"RGB"[c];
            payload[7 + (3 * c)] = 0x11;
            payload[8 + (3 * c)] = 0;
        }

        JpegMetadataWriter.WriteSegment(output, _arithmetic is null ? MarkerSof3 : MarkerSof11, payload[..(6 + (3 * _components))]);
    }

    private void WriteRestartInterval(ImageOutputBuffer output)
    {
        if (_restartInterval == 0)
            return;

        Span<byte> payload = stackalloc byte[2];
        BinaryPrimitives.WriteUInt16BigEndian(payload, (ushort)_restartInterval);
        JpegMetadataWriter.WriteSegment(output, MarkerDri, payload);
    }

    private void WriteHuffmanTables(ImageOutputBuffer output)
    {
        Span<byte> payload = stackalloc byte[3 * (17 + 256)];
        var offset = 0;
        for (var c = 0; c < _components; c++)
        {
            var table = _tables[c]!;
            payload[offset++] = (byte)c;
            table.Bits.Span.CopyTo(payload[offset..]);
            offset += 16;
            table.Values.Span.CopyTo(payload[offset..]);
            offset += table.Values.Length;
        }

        JpegMetadataWriter.WriteSegment(output, MarkerDht, payload[..offset]);
    }

    /// <summary>Writes the header of the scan: every component with its table, the predictor (Ss), Se = 0 and the point transform 0 (T.81 B.2.3).</summary>
    private void WriteScanHeader(ImageOutputBuffer output)
    {
        Span<byte> payload = stackalloc byte[1 + (2 * 3) + 3];
        payload[0] = (byte)_components;
        for (var c = 0; c < _components; c++)
        {
            payload[1 + (2 * c)] = _components == 1 ? (byte)1 : (byte)"RGB"[c];
            payload[2 + (2 * c)] = (byte)(c << 4);
        }

        var tail = 1 + (2 * _components);
        payload[tail] = (byte)_predictor;
        payload[tail + 1] = 0;
        payload[tail + 2] = 0;
        JpegMetadataWriter.WriteSegment(output, MarkerSos, payload[..(tail + 3)]);
    }

    /// <summary>Gets the previous or current line of samples of a component.</summary>
    private Span<ushort> GetLine(int component, int y) => unsafe(MemoryMarshal.Cast<byte, ushort>(_lines!.Span)).Slice(((2 * component) + (y & 1)) * _width, _width);

    /// <summary>Gets the differences of the line above of a component (arithmetic coding).</summary>
    private Span<int> GetAboveDifferences(int component) => unsafe(MemoryMarshal.Cast<byte, int>(_lines!.Span[(_width * _components * 2 * sizeof(ushort))..])).Slice(component * _width, _width);

    /// <summary>
    /// Converts the line <see cref="_y"/> to the samples of the components, then counts or codes their differences; a
    /// restart interval starts before the line when it is due.
    /// </summary>
    private void CodeRow(ImageOutputBuffer output)
    {
        var frame = _frame ?? throw new InvalidOperationException("No JPEG frame is being written.");
        using (var lease = frame.GetStorage().AcquireLease())
        {
            ReadOnlySpan<byte> source = lease.Storage.GetRowSpanCore(_y);
            var converted = _row!.Span;
            PixelConverter.ConvertRow(_sourceFormat, source, _workFormat, converted, _background, ImageFormat.Jpeg);
            var samples = unsafe(MemoryMarshal.Cast<byte, ushort>(converted));
            var maximum = (1u << _precision) - 1;
            for (var c = 0; c < _components; c++)
            {
                var line = GetLine(c, _y);
                for (var x = 0; x < _width; x++)
                {
                    line[x] = (ushort)(((samples[(x * _components) + c] * maximum) + 32767) / 65535);
                }
            }
        }

        // The first line of the scan and of each restart interval is predicted from 2^(P - 1) and from the left
        var restart = _restartInterval > 0 && _y > 0 && (long)_y * _width % _restartInterval == 0;
        var first = _y == 0 || restart;
        if (_arithmetic is { } arithmetic)
        {
            if (restart)
            {
                arithmetic.Flush();
                arithmetic.WriteMarker((byte)(MarkerRst0 + RestartNumber()));
                foreach (var statistics in _statistics)
                {
                    statistics.AsSpan().Clear();
                }
            }

            if (first)
            {
                for (var c = 0; c < _components; c++)
                {
                    GetAboveDifferences(c).Clear();
                }
            }

            var left = (Span<int>)stackalloc int[3];
            left.Clear();
            for (var x = 0; x < _width; x++)
            {
                for (var c = 0; c < _components; c++)
                {
                    var difference = GetDifference(c, x, first);
                    var above = GetAboveDifferences(c);
                    EncodeArithmetic(arithmetic, _statistics[c], difference, left[c], above[x]);
                    left[c] = difference;
                    above[x] = difference;
                }
            }

            output.Write(arithmetic.Output);
            arithmetic.ClearOutput();
            CancellationToken.ThrowIfCancellationRequested();
            return;
        }

        if (_counting)
        {
            for (var x = 0; x < _width; x++)
            {
                for (var c = 0; c < _components; c++)
                {
                    _histograms[c][GetCategory(GetDifference(c, x, first))]++;
                }
            }

            CancellationToken.ThrowIfCancellationRequested();
            return;
        }

        var start = 0;
        if (restart)
        {
            var span = output.GetSpan(4);
            var position = 0;
            _bits.Pad(span, ref position);
            span[position++] = 0xFF;
            span[position++] = (byte)(MarkerRst0 + RestartNumber());
            output.Advance(position);
        }

        while (start < _width)
        {
            var count = Math.Min(PixelsPerChunk, _width - start);
            var span = output.GetSpan(count * MaxPixelBytes);
            var position = 0;
            for (var x = start; x < start + count; x++)
            {
                for (var c = 0; c < _components; c++)
                {
                    var difference = GetDifference(c, x, first);
                    var category = GetCategory(difference);
                    var table = _tables[c]!;
                    if (category is 0 or 16)
                    {
                        _bits.WriteSymbol(span, ref position, table, category);
                    }
                    else
                    {
                        var bits = (uint)(difference < 0 ? difference - 1 : difference) & ((1u << category) - 1);
                        _bits.WriteSymbolAndBits(span, ref position, table, category, bits, category);
                    }
                }
            }

            output.Advance(position);
            start += count;
        }

        CancellationToken.ThrowIfCancellationRequested();
    }

    /// <summary>Gets the number of the RSTn marker before the line <see cref="_y"/>: 0 to 7, cyclically.</summary>
    private int RestartNumber() => (int)((((long)_y * _width / _restartInterval) - 1) & 7);

    /// <summary>Gets the difference of a sample of the current line with its prediction, modulo 2^16, from -32,768 to 32,767.</summary>
    private int GetDifference(int component, int x, bool firstLine)
    {
        var line = GetLine(component, _y);
        int prediction;
        if (firstLine)
        {
            prediction = x == 0 ? 1 << (_precision - 1) : line[x - 1];
        }
        else
        {
            var above = GetLine(component, _y - 1);
            if (x == 0)
            {
                prediction = above[0];
            }
            else
            {
                int ra = line[x - 1];
                int rb = above[x];
                int rc = above[x - 1];
                prediction = _predictor switch
                {
                    1 => ra,
                    2 => rb,
                    3 => rc,
                    4 => ra + rb - rc,
                    5 => ra + ((rb - rc) >> 1),
                    6 => rb + ((ra - rc) >> 1),
                    _ => (ra + rb) >> 1,
                };
            }
        }

        return (short)(line[x] - prediction);
    }

    /// <summary>Gets the category of a difference (T.81 table H.2): the number of bits of its magnitude, 16 for -32,768.</summary>
    private static int GetCategory(int difference) => 32 - BitOperations.LeadingZeroCount((uint)Math.Abs(difference));

    /// <summary>Codes a difference with the decisions of a DC difference, in the context of the classes of <c>Da</c> and <c>Db</c>.</summary>
    private static void EncodeArithmetic(JpegArithmeticEncoder encoder, byte[] statistics, int difference, int left, int above)
    {
        var context = ((Classify(left) * 5) + Classify(above)) * 4;
        if (difference == 0)
        {
            encoder.Encode(ref statistics[context], 0);
            return;
        }

        encoder.Encode(ref statistics[context], 1);
        var negative = difference < 0;
        encoder.Encode(ref statistics[context + 1], negative ? 1 : 0);
        var sz = negative ? -(difference + 1) : difference - 1;
        ref var signBin = ref statistics[context + (negative ? 3 : 2)];
        if (sz == 0)
        {
            encoder.Encode(ref signBin, 0);
            return;
        }

        encoder.Encode(ref signBin, 1);

        // The magnitude bins of the small or large differences above (U = 1)
        var categoryBin = SignZeroBins + (Math.Abs(above) > 2 ? 2 * MagnitudeBins : 0);
        var category = 0;
        var bound = 2;
        while (sz >= bound)
        {
            encoder.Encode(ref statistics[categoryBin + category], 1);
            bound <<= 1;
            category++;
        }

        encoder.Encode(ref statistics[categoryBin + category], 0);
        for (var bit = bound >> 2; bit != 0; bit >>= 1)
        {
            encoder.Encode(ref statistics[categoryBin + MagnitudeBins + category], (sz & bit) != 0 ? 1 : 0);
        }
    }

    /// <summary>Gets the class of a difference with the default bounds <c>L = 0</c> and <c>U = 1</c>: 0 for large negative, 1 small negative, 2 zero, 3 small positive, 4 large positive.</summary>
    private static int Classify(int difference) => difference switch
    {
        0 => 2,
        < -2 => 0,
        < 0 => 1,
        <= 2 => 3,
        _ => 4,
    };
}
