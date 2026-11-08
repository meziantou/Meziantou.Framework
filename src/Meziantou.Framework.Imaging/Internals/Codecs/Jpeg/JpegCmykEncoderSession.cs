using System.Runtime.InteropServices;
using Meziantou.Framework.Imaging.Formats;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The encoding of CMYK pixels (<see cref="PixelFormat.Cmyk32"/>, <see cref="PixelFormat.Cmyk64"/>) as a four-component
/// baseline JPEG file, the layout of Adobe Photoshop: an Adobe APP14 segment with the transform 0 (no color conversion),
/// and the complement of each ink amount stored (255 is no ink).
/// </summary>
/// <remarks>
/// <para>
/// The four components are not subsampled (<see cref="JpegEncoder.ChromaSubsampling"/> plays no role) and share the
/// luminance quantization table scaled by <see cref="JpegEncoder.Quality"/>, and one pair of Huffman tables: those of
/// ITU-T T.81 annex K, or tables built from the symbols of the image with <see cref="JpegEncoder.OptimizeHuffmanTables"/>.
/// The blocks are transformed like the ones of the other encoder (<see cref="JpegForwardDct"/>), with the edge samples
/// replicated into the partial blocks.
/// </para>
/// <para>
/// The quantized coefficients of the whole image are kept (8 bytes per pixel), then the file is written from them by
/// <see cref="JpegBitstreamWriter"/>, in memory charged to the writer: the size of the output is known before any byte of
/// the scan is given to the destination.
/// </para>
/// </remarks>
internal sealed class JpegCmykEncoderSession : ImageEncoderSession
{
    private const int ComponentCount = 4;
    private const byte MarkerSoi = 0xD8;
    private const byte MarkerEoi = 0xD9;
    private const byte MarkerApp14 = 0xEE;

    private readonly JpegEncoder _encoder;
    private readonly PixelFormat _sourceFormat;
    private readonly int _width;
    private readonly int _height;
    private readonly int _blocksWide;
    private readonly int _blocksHigh;
    private readonly ushort[] _quantization = new ushort[64];
    private readonly double[] _divisors = new double[64];
    private readonly JpegMetadataWriter _metadata;
    private JpegCoefficientStore? _coefficients;
    private PooledBuffer? _samples;
    private ImageFrame? _frame;
    private Step _step;
    private int _metadataIndex;
    private int _blockRow;
    private bool _frameWritten;

    // "Adobe", the version 100, no flags, and the transform 0: the four components are stored as they are (Adobe
    // Technical Note #5116, section 18)
    private static ReadOnlySpan<byte> AdobeSegment => [(byte)'A', (byte)'d', (byte)'o', (byte)'b', (byte)'e', 0, 100, 0, 0, 0, 0, 0];

    public JpegCmykEncoderSession(ImageEncoderSessionOptions options)
        : base(options)
    {
        _encoder = (JpegEncoder)options.Encoder;
        _sourceFormat = options.PixelFormat;
        if (!PixelFormats.IsCmyk(_sourceFormat))
            throw new ArgumentException("The CMYK JPEG session encodes CMYK pixels.", nameof(options));

        if (_sourceFormat == PixelFormat.Cmyk64 && !_encoder.AllowBitDepthReduction)
            throw new UnsupportedImageFeatureException($"JPEG output stores 8-bit samples, so encoding {_sourceFormat} pixels would discard precision. Set JpegEncoder.AllowBitDepthReduction to reduce the samples to 8 bits (nearest rounding), or convert the image explicitly with CloneAs.", ImageFormat.Jpeg, "Bit depth reduction");

        if (_encoder.Process == JpegEncodingProcess.Progressive)
            throw new UnsupportedImageFeatureException("CMYK JPEG output is baseline sequential: set JpegEncoder.Process to Baseline.", ImageFormat.Jpeg, "Progressive CMYK JPEG");

        // A gain map maps an RGB rendition to another one: inks have no such rendition
        if (_encoder.GainMap is not null)
            throw new UnsupportedImageFeatureException("A gain map cannot be written next to CMYK pixels: convert the image to RGB first, or remove JpegEncoder.GainMap.", ImageFormat.Jpeg, "Gain map with CMYK pixels");

        _width = options.CanvasSize.Width;
        _height = options.CanvasSize.Height;
        _blocksWide = (_width + 7) / 8;
        _blocksHigh = (_height + 7) / 8;
        JpegEncodingTables.ScaleQuantizationTable(JpegEncodingTables.LuminanceQuantization, _encoder.Quality, _quantization);
        JpegForwardDct.CreateDivisors(_quantization, _divisors);
        _metadata = new JpegMetadataWriter(options.Metadata);

        // One row of blocks of samples per component
        var stride = (long)_blocksWide * 8;
        var sampleBytes = stride * 8 * ComponentCount * sizeof(float);
        if (sampleBytes > CheckedSizes.MaxBufferLength)
            throw CheckedSizes.CreateOverflowException(options.Scope.Limits);

        try
        {
            Span<int> blocksWide = [_blocksWide, _blocksWide, _blocksWide, _blocksWide];
            Span<int> blocksHigh = [_blocksHigh, _blocksHigh, _blocksHigh, _blocksHigh];
            _coefficients = new JpegCoefficientStore(blocksWide, blocksHigh, options.Scope);
            _samples = options.Scope.Rent((int)sampleBytes, AllocationKind.Temporary, clear: false);
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
        Transform,
        Write,
        Trailer,
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
                output.Write([0xFF, MarkerSoi]);

                // JFIF carries the density; the Adobe segment says that the four components are CMYK samples
                JpegMetadataWriter.WriteJfif(output, Options.Metadata);
                JpegMetadataWriter.WriteSegment(output, MarkerApp14, AdobeSegment);
                _step = Step.Metadata;
                return false;

            case Step.Metadata:
                if (_metadataIndex < _metadata.Count)
                {
                    _metadata.Write(_metadataIndex++, output);
                    return false;
                }

                _step = Step.Transform;
                return false;

            case Step.Transform:
                TransformBlockRow();
                CancellationToken.ThrowIfCancellationRequested();
                if (++_blockRow < _blocksHigh)
                    return false;

                _frame = null;
                _samples?.Dispose();
                _samples = null;
                _step = Step.Write;
                return false;

            case Step.Write:
                WriteFrame(output);
                _step = Step.None;
                ReleaseBuffers();
                return true;

            case Step.Trailer:
                output.Write([0xFF, MarkerEoi]);
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

    /// <summary>Counts the symbols of the DC and AC tables of a sequential scan of every component, like <see cref="JpegBitstreamWriter"/> codes them.</summary>
    private static void CountSymbols(JpegCoefficientStore coefficients, int blocksWide, int blocksHigh, int restartInterval, Span<uint> dc, Span<uint> ac)
    {
        var zigZag = JpegIdct.ZigZag;
        Span<int> predictions = stackalloc int[ComponentCount];
        var unit = 0;
        for (var y = 0; y < blocksHigh; y++)
        {
            for (var x = 0; x < blocksWide; x++)
            {
                if (restartInterval > 0 && unit > 0 && unit % restartInterval == 0)
                {
                    predictions.Clear();
                }

                unit++;
                for (var component = 0; component < ComponentCount; component++)
                {
                    var block = coefficients.GetBlock(component, x, y);
                    dc[GetSizeCategory(block[0] - predictions[component])]++;
                    predictions[component] = block[0];
                    var run = 0;
                    for (var k = 1; k < 64; k++)
                    {
                        var value = block[zigZag[k]];
                        if (value == 0)
                        {
                            run++;
                            continue;
                        }

                        while (run >= 16)
                        {
                            ac[0xF0]++;
                            run -= 16;
                        }

                        ac[(run << 4) | GetSizeCategory(value)]++;
                        run = 0;
                    }

                    if (run > 0)
                    {
                        ac[0x00]++;
                    }
                }
            }
        }
    }

    private static int GetSizeCategory(int value) => value == 0 ? 0 : 32 - System.Numerics.BitOperations.LeadingZeroCount((uint)Math.Abs(value));

    private void ReleaseBuffers()
    {
        _samples?.Dispose();
        _samples = null;
        _coefficients?.Dispose();
        _coefficients = null;
    }

    /// <summary>Transforms and quantizes the blocks of one row of blocks of the four components.</summary>
    private void TransformBlockRow()
    {
        var frame = _frame ?? throw new InvalidOperationException("No frame is being encoded.");
        var coefficients = _coefficients!;
        var stride = _blocksWide * 8;
        var plane = stride * 8;
        var samples = unsafe(MemoryMarshal.Cast<byte, float>(_samples!.RawBuffer.AsSpan(0, plane * ComponentCount * sizeof(float))));
        var deep = _sourceFormat == PixelFormat.Cmyk64;
        using (var lease = frame.GetStorage().AcquireLease())
        {
            for (var row = 0; row < 8; row++)
            {
                // The last row and column of the image fill the partial blocks
                var y = Math.Min((_blockRow * 8) + row, _height - 1);
                var bytes = lease.GetRowBytes(y);
                for (var x = 0; x < stride; x++)
                {
                    var column = Math.Min(x, _width - 1);
                    for (var component = 0; component < ComponentCount; component++)
                    {
                        var index = (column * ComponentCount) + component;
                        var ink = deep ? PixelConverter.To8Bit(unsafe(MemoryMarshal.Cast<byte, ushort>(bytes))[index]) : bytes[index];

                        // The complement of the ink amount, level-shifted
                        samples[(component * plane) + (row * stride) + x] = 255 - ink - 128;
                    }
                }
            }
        }

        Span<double> scratch = stackalloc double[64];
        Span<int> zigZagCoefficients = stackalloc int[64];
        var zigZag = JpegIdct.ZigZag;
        for (var component = 0; component < ComponentCount; component++)
        {
            for (var bx = 0; bx < _blocksWide; bx++)
            {
                JpegForwardDct.TransformAndQuantize(samples[((component * plane) + (bx * 8))..], stride, _divisors, scratch, zigZagCoefficients);
                var block = coefficients.GetBlock(component, bx, _blockRow);
                for (var k = 0; k < 64; k++)
                {
                    block[zigZag[k]] = (short)zigZagCoefficients[k];
                }
            }
        }
    }

    /// <summary>Writes the tables, the frame header and the scan, without the start and end of image markers that the session writes.</summary>
    private void WriteFrame(ImageOutputBuffer output)
    {
        var coefficients = _coefficients!;
        var model = new JpegBitstreamModel { Width = _width, Height = _height, RestartInterval = _encoder.RestartInterval };
        model.MarkerOrder.AddRange([0xDB, 0xC0]);
        if (model.RestartInterval > 0)
        {
            model.MarkerOrder.Add(0xDD);
        }

        model.MarkerOrder.AddRange([0xC4, 0xDA, MarkerEoi]);
        var quantization = new JpegBitstreamQuantizationTable(Precision: 0, Slot: 0, IsLast: true) { HasValues = true };
        _quantization.CopyTo(quantization.Values, 0);
        model.QuantizationTables.Add(quantization);
        var scan = new JpegBitstreamScan(SpectralStart: 0, SpectralEnd: 63, SuccessiveHigh: 0, SuccessiveLow: 0);
        for (var component = 0; component < ComponentCount; component++)
        {
            model.Components.Add(new JpegBitstreamComponent(component + 1, QuantizationTable: 0));
            scan.Components.Add(new JpegBitstreamScanComponent(component, DcTable: 0, AcTable: 0));
        }

        model.Scans.Add(scan);
        if (_encoder.OptimizeHuffmanTables)
        {
            Span<uint> dc = stackalloc uint[256];
            Span<uint> ac = stackalloc uint[256];
            CountSymbols(coefficients, _blocksWide, _blocksHigh, model.RestartInterval, dc, ac);
            var dcTable = JpegHuffmanCodeBuilder.Build(dc);
            var acTable = JpegHuffmanCodeBuilder.Build(ac);
            model.HuffmanTables.Add(new JpegBitstreamHuffmanTable(IsAc: false, Slot: 0, IsLast: false, dcTable.Bits.ToArray(), dcTable.Values.ToArray()));
            model.HuffmanTables.Add(new JpegBitstreamHuffmanTable(IsAc: true, Slot: 0, IsLast: true, acTable.Bits.ToArray(), acTable.Values.ToArray()));
        }
        else
        {
            model.HuffmanTables.Add(new JpegBitstreamHuffmanTable(IsAc: false, Slot: 0, IsLast: false, JpegEncodingTables.LuminanceDcBits.ToArray(), JpegEncodingTables.LuminanceDcValues.ToArray()));
            model.HuffmanTables.Add(new JpegBitstreamHuffmanTable(IsAc: true, Slot: 0, IsLast: true, JpegEncodingTables.LuminanceAcBits.ToArray(), JpegEncodingTables.LuminanceAcValues.ToArray()));
        }

        Span<int> channels = [0, 1, 2, 3];
        using var writer = new JpegBitstreamWriter(model, coefficients, channels, Options.Scope, CancellationToken);
        writer.Write();

        // The writer starts the file and ends it: the session writes these markers itself
        var written = writer.WrittenMemory.Span;
        output.Write(written[2..^2]);
    }
}
