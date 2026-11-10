using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.Arm;
using System.Runtime.Intrinsics.X86;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// Turns decoded component planes of 8-bit samples into output rows: chroma upsampling to the frame size,
/// color conversion to the source layout of the sink (<see cref="PixelFormat.Gray8"/>, <see cref="PixelFormat.Rgb24"/>, or
/// <see cref="PixelFormat.Rgba32"/> with opaque alpha for three components when the image is requested so), and the row
/// writes. When the sink's plan is an identity, rows are produced directly in the frame storage.
/// </summary>
/// <remarks>
/// <para>Upsampling (centered chroma siting, edges replicated at the component's real width and height):</para>
/// <list type="bullet">
/// <item><description>factor 1: the samples are used as is;</description></item>
/// <item><description>
/// horizontal factor 2 (triangle filter): <c>out[2j] = (3 s[j] + s[j - 1] + 1) &gt;&gt; 2</c>, <c>out[2j + 1] = (3 s[j] + s[j + 1] + 2) &gt;&gt; 2</c>;
/// </description></item>
/// <item><description>
/// vertical factor 2 (triangle filter): <c>out = (3 near + far + 1) &gt;&gt; 2</c> for the upper output row of each sample row
/// (far = the row above) and <c>(3 near + far + 2) &gt;&gt; 2</c> for the lower one (far = the row below);
/// </description></item>
/// <item><description>
/// both factors 2: column sums <c>c[j] = 3 near[j] + far[j]</c>, then <c>out[2j] = (3 c[j] + c[j - 1] + 8) &gt;&gt; 4</c> and
/// <c>out[2j + 1] = (3 c[j] + c[j + 1] + 7) &gt;&gt; 4</c>;
/// </description></item>
/// <item><description>any other integral factor (3 or 4, or 2 combined with 3 or 4): sample replication;</description></item>
/// <item><description>
/// a horizontal factor of 2 on a plane of at most 2 samples (frames of at most 4 pixels wide): sample replication in both
/// directions, as libjpeg-turbo does (its triangle filters require wider planes), so that tiny frames decode like the
/// reference decoder and the browsers built on it.
/// </description></item>
/// </list>
/// <para>
/// Color conversion (JFIF 1.02, full range): with <c>cb = Cb - 128</c> and <c>cr = Cr - 128</c>,
/// <c>R = Y + ((91881 cr + 32768) &gt;&gt; 16)</c>, <c>G = Y + ((-22554 cb - 46802 cr + 32768) &gt;&gt; 16)</c>,
/// <c>B = Y + ((116130 cb + 32768) &gt;&gt; 16)</c> (the coefficients 1.402, 0.34414, 0.71414 and 1.772 in 16-bit fixed point,
/// arithmetic shifts, i.e. rounding to nearest with ties upward), each clamped to 0..255. RGB samples (Adobe transform 0,
/// or component identifiers R, G, B) are copied; grayscale samples are the luma samples.
/// </para>
/// <para>
/// Four components are inks, converted to RGB by an ICC conversion that expects ink amounts (0 is no ink). JPEG files
/// store the complement (255 is no ink): Adobe Photoshop writes them so, and decoders read every four-component file
/// that way, with or without the Adobe APP14 segment. CMYK samples are therefore complemented. For YCCK (Adobe Technical
/// Note #5116, section 18: the first three stored samples are complemented, then converted like RGB to YCbCr; the fourth
/// is kept) the two complements cancel out: the color conversion above gives the cyan, magenta and yellow amounts
/// directly, and only the black samples are complemented.
/// </para>
/// <para>
/// With 128-bit hardware vector support (and little-endian byte order), the triangle filters and the color conversion process
/// 16 samples at a time in 16-bit lanes (the color conversion 32 with 256-bit vectors; the filter of both factors computes
/// the column sums of a row once, then filters 8 or 16 of them at a time); the edges and the remainders use the scalar code. The output is identical to the scalar reference: the filters use the same integer arithmetic, and the
/// conversion an exact rewriting of it whose intermediate values fit in 16 bits (see <see cref="ConvertYCbCrVector"/>).
/// </para>
/// </remarks>
internal sealed class JpegRowWriter : JpegBandWriter
{
    // Per-sample color terms: complete for R and B, unshifted (with the rounding half added once) for G
    private static readonly int[] CrToR = CreateTable(static v => ((91881 * v) + 32768) >> 16);
    private static readonly int[] CbToB = CreateTable(static v => ((116130 * v) + 32768) >> 16);
    private static readonly int[] CbToG = CreateTable(static v => -22554 * v);
    private static readonly int[] CrToG = CreateTable(static v => (-46802 * v) + 32768);

    private readonly JpegFrameComponent[] _components;
    private readonly DecodedFrameSink? _sink;

    // The container of an embedded datastream, which receives the samples instead of an image; null for an image
    private readonly JpegSampleReceiver? _receiver;
    private readonly ImageColorModel _colorModel;

    // The conversion of the inks of a four-component frame to RGB; null for the other frames
    private readonly IccPipeline? _inkConversion;
    private readonly int _width;

    // Whether the rows of a three-component frame are written as opaque RGBA (the source layout of the sink) instead of RGB
    private readonly bool _opaqueRgba;

    // Whether the ink amounts of a four-component frame are written as they are (Cmyk32, the source layout of the sink)
    private readonly bool _nativeInks;
    private readonly PooledBuffer?[] _upsampled;

    // The column sums of the components upsampled by 2 in both directions (UpsampleBoth)
    private readonly PooledBuffer?[] _columnSums;
    private PooledBuffer? _sourceRow;
    private PooledBuffer? _inkRow;

    public JpegRowWriter(JpegFrameComponent[] components, DecodedFrameSink sink, ImageColorModel colorModel, IccPipeline? inkConversion, int width, int height, int bandRows, Rectangle window, AllocationScope scope)
        : this(components, sink, receiver: null, colorModel, inkConversion, width, height, bandRows, window, scope)
    {
        if (components.Length == 4 ? inkConversion is null && sink.SourcePixelFormat != PixelFormat.Cmyk32 : inkConversion is not null)
            throw new ArgumentException("A four-component frame, and only it, needs the conversion of its inks, unless its rows are CMYK.", nameof(inkConversion));
    }

    /// <summary>Creates a writer that hands the rows to the container of an embedded datastream instead of an image.</summary>
    public JpegRowWriter(JpegFrameComponent[] components, JpegSampleReceiver receiver, ImageColorModel colorModel, int width, int height, int bandRows, AllocationScope scope)
        : this(components, sink: null, receiver, colorModel, inkConversion: null, width, height, bandRows, new Rectangle(0, 0, width, height), scope)
    {
    }

    private JpegRowWriter(JpegFrameComponent[] components, DecodedFrameSink? sink, JpegSampleReceiver? receiver, ImageColorModel colorModel, IccPipeline? inkConversion, int width, int height, int bandRows, Rectangle window, AllocationScope scope)
        : base(height, bandRows, window)
    {
        _components = components;
        _sink = sink;
        _receiver = receiver;
        _colorModel = colorModel;
        _inkConversion = inkConversion;
        _width = width;
        _opaqueRgba = sink?.SourcePixelFormat == PixelFormat.Rgba32;
        if (_opaqueRgba && (components.Length != 3 || inkConversion is not null))
            throw new ArgumentException("Only the rows of a three-component frame are written as RGBA.", nameof(sink));

        _nativeInks = sink?.SourcePixelFormat == PixelFormat.Cmyk32;
        if (_nativeInks && (components.Length != 4 || inkConversion is not null))
            throw new ArgumentException("Only the rows of a four-component frame are written as CMYK.", nameof(sink));

        _upsampled = new PooledBuffer?[components.Length];
        _columnSums = new PooledBuffer?[components.Length];
        try
        {
            foreach (var component in components)
            {
                if (component.HorizontalRatio != 1 || component.VerticalRatio != 1)
                {
                    _upsampled[component.Index] = scope.Rent(width, AllocationKind.DecoderState, clear: false);
                    if (component.HorizontalRatio == 2 && component.VerticalRatio == 2)
                    {
                        _columnSums[component.Index] = scope.Rent(component.Width * sizeof(ushort), AllocationKind.DecoderState, clear: false);
                    }
                }
            }

            // A sink that stores the rows as they are is written in place; a receiver is given a row of its own
            if (sink is null ? components.Length == 3 : !sink.Plan.IsIdentity && components.Length > 1)
            {
                _sourceRow = scope.Rent(width * (_opaqueRgba || _nativeInks ? 4 : 3), AllocationKind.DecoderState, clear: false);
            }

            if (components.Length == 4)
            {
                _inkRow = scope.Rent(width * 4, AllocationKind.DecoderState, clear: false);
            }
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    protected override void WriteRows(int first, int last)
    {
        if (_receiver is not null)
        {
            for (var y = first; y < last; y++)
            {
                WriteSampleRow(_receiver, y);
            }
        }
        else
        {
            using var lease = _sink!.LeaseCurrentFrame();
            WriteRows(lease.Storage, first, last);
        }
    }

    protected override void WriteRows(PixelStorage storage, int first, int last)
    {
        for (var y = first; y < last; y++)
        {
            WriteRow(storage, y);
        }
    }

    protected override PixelLease LeaseImage() => (_sink ?? throw new InvalidOperationException("The rows of an embedded JPEG datastream are handed to its container.")).LeaseCurrentFrame();

    protected override void Dispose(bool disposing)
    {
        for (var i = 0; i < _upsampled.Length; i++)
        {
            _upsampled[i]?.Dispose();
            _upsampled[i] = null;
            _columnSums[i]?.Dispose();
            _columnSums[i] = null;
        }

        _sourceRow?.Dispose();
        _sourceRow = null;
        _inkRow?.Dispose();
        _inkRow = null;
        base.Dispose(disposing);
    }

    private static int[] CreateTable(Func<int, int> term)
    {
        var table = new int[256];
        for (var i = 0; i < 256; i++)
        {
            table[i] = term(i - 128);
        }

        return table;
    }

    /// <summary>Hands one row of an embedded datastream to its container: the samples of the components, interleaved.</summary>
    private void WriteSampleRow(JpegSampleReceiver receiver, int y)
    {
        if (_components.Length == 1)
        {
            receiver.OnRow(y, _components[0].GetRow(y)[.._width]);
            return;
        }

        var c0 = GetSamples(_components[0], y);
        var c1 = GetSamples(_components[1], y);
        var c2 = GetSamples(_components[2], y);
        if (_components.Length == 4)
        {
            // The container says what the four samples are: they are handed over as they are stored
            var samples = _inkRow!.RawBuffer.AsSpan(0, _width * 4);
            var c3 = GetSamples(_components[3], y);
            for (var x = 0; x < c3.Length; x++)
            {
                samples[4 * x] = c0[x];
                samples[(4 * x) + 1] = c1[x];
                samples[(4 * x) + 2] = c2[x];
                samples[(4 * x) + 3] = c3[x];
            }

            receiver.OnRow(y, samples);
            return;
        }

        var row = _sourceRow!.RawBuffer.AsSpan(0, _width * 3);
        if (_colorModel == ImageColorModel.YCbCr)
        {
            ConvertYCbCr(c0, c1, c2, row);
        }
        else
        {
            Interleave(c0, c1, c2, row);
        }

        receiver.OnRow(y, row);
    }

    private void WriteRow(PixelStorage storage, int y)
    {
        var sink = _sink!;
        if (_components.Length == 1)
        {
            // Grayscale: the luma samples are the output (a single-component frame is never subsampled)
            var samples = _components[0].GetRow(y).Slice(Left, Columns);
            if (sink.Plan.IsIdentity)
            {
                samples.CopyTo(storage.GetRowSpanCore(y - Top));
            }
            else
            {
                sink.Plan.ConvertRow(samples, storage.GetRowSpanCore(y - Top));
            }

            return;
        }

        // The color conversions are per pixel: the upsampled rows are cut to the columns of the window
        var c0 = GetSamples(_components[0], y).Slice(Left, Columns);
        var c1 = GetSamples(_components[1], y).Slice(Left, Columns);
        var c2 = GetSamples(_components[2], y).Slice(Left, Columns);
        var identity = sink.Plan.IsIdentity;
        var rowLength = Columns * (_opaqueRgba || _nativeInks ? 4 : 3);
        var row = identity ? storage.GetRowSpanCore(y - Top)[..rowLength] : _sourceRow!.RawBuffer.AsSpan(0, rowLength);
        if (_opaqueRgba)
        {
            if (_colorModel == ImageColorModel.Rgb)
            {
                InterleaveOpaque(c0, c1, c2, row);
            }
            else
            {
                ConvertYCbCrToRgba(c0, c1, c2, row);
            }
        }
        else if (_nativeInks)
        {
            var black = GetSamples(_components[3], y).Slice(Left, Columns);
            if (_colorModel == ImageColorModel.Ycck)
            {
                // The ink row holds the cyan, magenta and yellow amounts given by the color conversion
                var amounts = _inkRow!.RawBuffer.AsSpan(0, Columns * 3);
                ConvertYCbCr(c0, c1, c2, amounts);
                InterleaveInkAmounts(amounts, black, row);
            }
            else
            {
                InterleaveStoredInks(c0, c1, c2, black, row);
            }
        }
        else if (_inkConversion is not null)
        {
            var black = GetSamples(_components[3], y).Slice(Left, Columns);
            var inks = _inkRow!.RawBuffer.AsSpan(0, Columns * 4);
            if (_colorModel == ImageColorModel.Ycck)
            {
                // The output row is free until the inks are converted: it holds the cyan, magenta and yellow amounts first
                ConvertYCbCr(c0, c1, c2, row);
                InterleaveInkAmounts(row, black, inks);
            }
            else
            {
                InterleaveStoredInks(c0, c1, c2, black, inks);
            }

            ColorConversionKernels.Convert<byte, IccByteSample>(_inkConversion, inks, row, 4, 3);
        }
        else if (_colorModel == ImageColorModel.Rgb)
        {
            Interleave(c0, c1, c2, row);
        }
        else
        {
            ConvertYCbCr(c0, c1, c2, row);
        }

        if (!identity)
        {
            sink.Plan.ConvertRow(row, storage.GetRowSpanCore(y - Top));
        }
    }

    /// <summary>Interleaves three planes whose samples are stored as they are displayed.</summary>
    internal static void Interleave(ReadOnlySpan<byte> first, ReadOnlySpan<byte> second, ReadOnlySpan<byte> third, Span<byte> destination)
    {
        var done = UseVectors ? InterleaveVector(first, second, third, destination) : 0;
        InterleaveScalar(first[done..], second[done..], third[done..], destination[(3 * done)..]);
    }

    /// <summary>Interleaves the samples of whole 16-sample groups with byte shuffles.</summary>
    /// <returns>The number of samples of each plane interleaved.</returns>
    private static int InterleaveVector(ReadOnlySpan<byte> first, ReadOnlySpan<byte> second, ReadOnlySpan<byte> third, Span<byte> destination)
    {
        ref var firstStart = ref unsafe(MemoryMarshal.GetReference(first));
        ref var secondStart = ref unsafe(MemoryMarshal.GetReference(second));
        ref var thirdStart = ref unsafe(MemoryMarshal.GetReference(third));
        ref var destinationStart = ref unsafe(MemoryMarshal.GetReference(destination));
        var length = Math.Min(Math.Min(first.Length, Math.Min(second.Length, third.Length)), destination.Length / 3);
        var x = 0;
        for (; x + 16 <= length; x += 16)
        {
            Interleave16(
                unsafe(Vector128.LoadUnsafe(ref firstStart, (nuint)x)),
                unsafe(Vector128.LoadUnsafe(ref secondStart, (nuint)x)),
                unsafe(Vector128.LoadUnsafe(ref thirdStart, (nuint)x)),
                ref unsafe(Unsafe.Add(ref destinationStart, 3 * x)));
        }

        return x;
    }

    /// <summary>Writes 16 interleaved triples (48 bytes): each output byte comes from one of the three vectors (an index of 0xFF selects zero).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void Interleave16(Vector128<byte> r, Vector128<byte> g, Vector128<byte> b, ref byte output)
    {
        var out0 = Vector128.Shuffle(r, Vector128.Create((byte)0, 0xFF, 0xFF, 1, 0xFF, 0xFF, 2, 0xFF, 0xFF, 3, 0xFF, 0xFF, 4, 0xFF, 0xFF, 5))
            | Vector128.Shuffle(g, Vector128.Create((byte)0xFF, 0, 0xFF, 0xFF, 1, 0xFF, 0xFF, 2, 0xFF, 0xFF, 3, 0xFF, 0xFF, 4, 0xFF, 0xFF))
            | Vector128.Shuffle(b, Vector128.Create((byte)0xFF, 0xFF, 0, 0xFF, 0xFF, 1, 0xFF, 0xFF, 2, 0xFF, 0xFF, 3, 0xFF, 0xFF, 4, 0xFF));
        var out1 = Vector128.Shuffle(r, Vector128.Create((byte)0xFF, 0xFF, 6, 0xFF, 0xFF, 7, 0xFF, 0xFF, 8, 0xFF, 0xFF, 9, 0xFF, 0xFF, 10, 0xFF))
            | Vector128.Shuffle(g, Vector128.Create((byte)5, 0xFF, 0xFF, 6, 0xFF, 0xFF, 7, 0xFF, 0xFF, 8, 0xFF, 0xFF, 9, 0xFF, 0xFF, 10))
            | Vector128.Shuffle(b, Vector128.Create((byte)0xFF, 5, 0xFF, 0xFF, 6, 0xFF, 0xFF, 7, 0xFF, 0xFF, 8, 0xFF, 0xFF, 9, 0xFF, 0xFF));
        var out2 = Vector128.Shuffle(r, Vector128.Create((byte)0xFF, 11, 0xFF, 0xFF, 12, 0xFF, 0xFF, 13, 0xFF, 0xFF, 14, 0xFF, 0xFF, 15, 0xFF, 0xFF))
            | Vector128.Shuffle(g, Vector128.Create((byte)0xFF, 0xFF, 11, 0xFF, 0xFF, 12, 0xFF, 0xFF, 13, 0xFF, 0xFF, 14, 0xFF, 0xFF, 15, 0xFF))
            | Vector128.Shuffle(b, Vector128.Create((byte)10, 0xFF, 0xFF, 11, 0xFF, 0xFF, 12, 0xFF, 0xFF, 13, 0xFF, 0xFF, 14, 0xFF, 0xFF, 15));
        unsafe { out0.StoreUnsafe(ref output); }
        unsafe { out1.StoreUnsafe(ref output, 16); }
        unsafe { out2.StoreUnsafe(ref output, 32); }
    }

    /// <summary>The scalar reference of <see cref="Interleave"/>.</summary>
    internal static void InterleaveScalar(ReadOnlySpan<byte> first, ReadOnlySpan<byte> second, ReadOnlySpan<byte> third, Span<byte> destination)
    {
        for (var x = 0; x < first.Length; x++)
        {
            destination[3 * x] = first[x];
            destination[(3 * x) + 1] = second[x];
            destination[(3 * x) + 2] = third[x];
        }
    }

    /// <summary>Interleaves three planes whose samples are stored as they are displayed into opaque RGBA pixels.</summary>
    internal static void InterleaveOpaque(ReadOnlySpan<byte> first, ReadOnlySpan<byte> second, ReadOnlySpan<byte> third, Span<byte> destination)
    {
        var done = 0;
        if (UseVectors)
        {
            ref var firstStart = ref unsafe(MemoryMarshal.GetReference(first));
            ref var secondStart = ref unsafe(MemoryMarshal.GetReference(second));
            ref var thirdStart = ref unsafe(MemoryMarshal.GetReference(third));
            ref var destinationStart = ref unsafe(MemoryMarshal.GetReference(destination));
            var length = Math.Min(Math.Min(first.Length, Math.Min(second.Length, third.Length)), destination.Length / 4);
            for (; done + 16 <= length; done += 16)
            {
                StoreOpaqueRgba16(
                    unsafe(Vector128.LoadUnsafe(ref firstStart, (nuint)done)),
                    unsafe(Vector128.LoadUnsafe(ref secondStart, (nuint)done)),
                    unsafe(Vector128.LoadUnsafe(ref thirdStart, (nuint)done)),
                    ref unsafe(Unsafe.Add(ref destinationStart, 4 * done)));
            }
        }

        InterleaveOpaqueScalar(first[done..], second[done..], third[done..], destination[(4 * done)..]);
    }

    /// <summary>The scalar reference of <see cref="InterleaveOpaque"/>.</summary>
    internal static void InterleaveOpaqueScalar(ReadOnlySpan<byte> first, ReadOnlySpan<byte> second, ReadOnlySpan<byte> third, Span<byte> destination)
    {
        for (var x = 0; x < first.Length; x++)
        {
            destination[4 * x] = first[x];
            destination[(4 * x) + 1] = second[x];
            destination[(4 * x) + 2] = third[x];
            destination[(4 * x) + 3] = 255;
        }
    }

    /// <summary>Writes 16 opaque RGBA pixels (64 bytes) from 16 red, green and blue values.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void StoreOpaqueRgba16(Vector128<byte> r, Vector128<byte> g, Vector128<byte> b, ref byte output)
    {
        var a = Vector128<byte>.AllBitsSet;
        Vector128<byte> first, second, third, fourth;
        if (AdvSimd.Arm64.IsSupported)
        {
            var redGreenLow = AdvSimd.Arm64.ZipLow(r, g).AsUInt16();
            var redGreenHigh = AdvSimd.Arm64.ZipHigh(r, g).AsUInt16();
            var blueAlphaLow = AdvSimd.Arm64.ZipLow(b, a).AsUInt16();
            var blueAlphaHigh = AdvSimd.Arm64.ZipHigh(b, a).AsUInt16();
            first = AdvSimd.Arm64.ZipLow(redGreenLow, blueAlphaLow).AsByte();
            second = AdvSimd.Arm64.ZipHigh(redGreenLow, blueAlphaLow).AsByte();
            third = AdvSimd.Arm64.ZipLow(redGreenHigh, blueAlphaHigh).AsByte();
            fourth = AdvSimd.Arm64.ZipHigh(redGreenHigh, blueAlphaHigh).AsByte();
        }
        else if (Sse2.IsSupported)
        {
            var redGreenLow = Sse2.UnpackLow(r, g).AsUInt16();
            var redGreenHigh = Sse2.UnpackHigh(r, g).AsUInt16();
            var blueAlphaLow = Sse2.UnpackLow(b, a).AsUInt16();
            var blueAlphaHigh = Sse2.UnpackHigh(b, a).AsUInt16();
            first = Sse2.UnpackLow(redGreenLow, blueAlphaLow).AsByte();
            second = Sse2.UnpackHigh(redGreenLow, blueAlphaLow).AsByte();
            third = Sse2.UnpackLow(redGreenHigh, blueAlphaHigh).AsByte();
            fourth = Sse2.UnpackHigh(redGreenHigh, blueAlphaHigh).AsByte();
        }
        else
        {
            // Byte 4 i + c is the sample c of the pixel i: each byte comes from one of the three vectors, or is the alpha
            var opaque = Vector128.Create(0, 0, 0, 255, 0, 0, 0, 255, 0, 0, 0, 255, 0, 0, 0, (byte)255);
            first = Vector128.Shuffle(r, Vector128.Create((byte)0, 0xFF, 0xFF, 0xFF, 1, 0xFF, 0xFF, 0xFF, 2, 0xFF, 0xFF, 0xFF, 3, 0xFF, 0xFF, 0xFF))
                | Vector128.Shuffle(g, Vector128.Create((byte)0xFF, 0, 0xFF, 0xFF, 0xFF, 1, 0xFF, 0xFF, 0xFF, 2, 0xFF, 0xFF, 0xFF, 3, 0xFF, 0xFF))
                | Vector128.Shuffle(b, Vector128.Create((byte)0xFF, 0xFF, 0, 0xFF, 0xFF, 0xFF, 1, 0xFF, 0xFF, 0xFF, 2, 0xFF, 0xFF, 0xFF, 3, 0xFF)) | opaque;
            second = Vector128.Shuffle(r, Vector128.Create((byte)4, 0xFF, 0xFF, 0xFF, 5, 0xFF, 0xFF, 0xFF, 6, 0xFF, 0xFF, 0xFF, 7, 0xFF, 0xFF, 0xFF))
                | Vector128.Shuffle(g, Vector128.Create((byte)0xFF, 4, 0xFF, 0xFF, 0xFF, 5, 0xFF, 0xFF, 0xFF, 6, 0xFF, 0xFF, 0xFF, 7, 0xFF, 0xFF))
                | Vector128.Shuffle(b, Vector128.Create((byte)0xFF, 0xFF, 4, 0xFF, 0xFF, 0xFF, 5, 0xFF, 0xFF, 0xFF, 6, 0xFF, 0xFF, 0xFF, 7, 0xFF)) | opaque;
            third = Vector128.Shuffle(r, Vector128.Create((byte)8, 0xFF, 0xFF, 0xFF, 9, 0xFF, 0xFF, 0xFF, 10, 0xFF, 0xFF, 0xFF, 11, 0xFF, 0xFF, 0xFF))
                | Vector128.Shuffle(g, Vector128.Create((byte)0xFF, 8, 0xFF, 0xFF, 0xFF, 9, 0xFF, 0xFF, 0xFF, 10, 0xFF, 0xFF, 0xFF, 11, 0xFF, 0xFF))
                | Vector128.Shuffle(b, Vector128.Create((byte)0xFF, 0xFF, 8, 0xFF, 0xFF, 0xFF, 9, 0xFF, 0xFF, 0xFF, 10, 0xFF, 0xFF, 0xFF, 11, 0xFF)) | opaque;
            fourth = Vector128.Shuffle(r, Vector128.Create((byte)12, 0xFF, 0xFF, 0xFF, 13, 0xFF, 0xFF, 0xFF, 14, 0xFF, 0xFF, 0xFF, 15, 0xFF, 0xFF, 0xFF))
                | Vector128.Shuffle(g, Vector128.Create((byte)0xFF, 12, 0xFF, 0xFF, 0xFF, 13, 0xFF, 0xFF, 0xFF, 14, 0xFF, 0xFF, 0xFF, 15, 0xFF, 0xFF))
                | Vector128.Shuffle(b, Vector128.Create((byte)0xFF, 0xFF, 12, 0xFF, 0xFF, 0xFF, 13, 0xFF, 0xFF, 0xFF, 14, 0xFF, 0xFF, 0xFF, 15, 0xFF)) | opaque;
        }

        unsafe { first.StoreUnsafe(ref output); }
        unsafe { second.StoreUnsafe(ref output, 16); }
        unsafe { third.StoreUnsafe(ref output, 32); }
        unsafe { fourth.StoreUnsafe(ref output, 48); }
    }

    /// <summary>Interleaves the stored samples of four ink planes as ink amounts: the complement of each sample.</summary>
    internal static void InterleaveStoredInks(ReadOnlySpan<byte> cyan, ReadOnlySpan<byte> magenta, ReadOnlySpan<byte> yellow, ReadOnlySpan<byte> black, Span<byte> destination)
    {
        for (var x = 0; x < black.Length; x++)
        {
            destination[4 * x] = (byte)~cyan[x];
            destination[(4 * x) + 1] = (byte)~magenta[x];
            destination[(4 * x) + 2] = (byte)~yellow[x];
            destination[(4 * x) + 3] = (byte)~black[x];
        }
    }

    /// <summary>Interleaves interleaved cyan, magenta and yellow amounts with the complement of the stored black samples.</summary>
    internal static void InterleaveInkAmounts(ReadOnlySpan<byte> amounts, ReadOnlySpan<byte> black, Span<byte> destination)
    {
        for (var x = 0; x < black.Length; x++)
        {
            destination[4 * x] = amounts[3 * x];
            destination[(4 * x) + 1] = amounts[(3 * x) + 1];
            destination[(4 * x) + 2] = amounts[(3 * x) + 2];
            destination[(4 * x) + 3] = (byte)~black[x];
        }
    }

    /// <summary>Converts full-range YCbCr samples to interleaved RGB samples.</summary>
    internal static void ConvertYCbCr(ReadOnlySpan<byte> luma, ReadOnlySpan<byte> blue, ReadOnlySpan<byte> red, Span<byte> destination)
    {
        var done = UseVectors256 ? ConvertYCbCrVector256(luma, blue, red, destination) : 0;
        if (UseVectors)
        {
            done += ConvertYCbCrVector(luma[done..], blue[done..], red[done..], destination[(3 * done)..]);
        }

        ConvertYCbCrScalar(luma[done..], blue[done..], red[done..], destination[(3 * done)..]);
    }

    /// <summary>The scalar reference of <see cref="ConvertYCbCr"/>.</summary>
    internal static void ConvertYCbCrScalar(ReadOnlySpan<byte> luma, ReadOnlySpan<byte> blue, ReadOnlySpan<byte> red, Span<byte> destination)
    {
        var crToR = CrToR;
        var cbToB = CbToB;
        var cbToG = CbToG;
        var crToG = CrToG;
        for (var x = 0; x < luma.Length; x++)
        {
            int yValue = luma[x];
            int cb = blue[x];
            int cr = red[x];
            destination[3 * x] = Clamp(yValue + crToR[cr]);
            destination[(3 * x) + 1] = Clamp(yValue + ((cbToG[cb] + crToG[cr]) >> 16));
            destination[(3 * x) + 2] = Clamp(yValue + cbToB[cb]);
        }
    }

    /// <summary>Gets a value indicating whether the vector paths are used (128-bit hardware vectors, little-endian lanes).</summary>
    private static bool UseVectors => Vector128.IsHardwareAccelerated && BitConverter.IsLittleEndian;

    /// <summary>Gets a value indicating whether the color conversion processes 32 pixels at a time (256-bit hardware vectors, x64 with AVX2).</summary>
    private static bool UseVectors256 => Vector256.IsHardwareAccelerated && BitConverter.IsLittleEndian;

    /// <summary>Gets a value indicating whether the RGBA conversion and the upsampling process 64 samples at a time (x64 with AVX-512).</summary>
    private static bool UseVectors512 => Vector512.IsHardwareAccelerated && Avx512BW.IsSupported;

    /// <summary>Converts the pixels of whole 16-pixel groups.</summary>
    /// <returns>The number of pixels converted.</returns>
    /// <remarks>
    /// <para>
    /// The terms of the scalar reference are rewritten exactly so that every intermediate value fits in a 16-bit lane. A
    /// multiple of 65536 leaves the shift: <c>91881 = 65536 + 26345</c>, <c>-46802 = -65536 + 18734</c> and
    /// <c>116130 = 2 * 65536 - 14942</c>, hence <c>R = Y + cr + ((26345 cr + 32768) &gt;&gt; 16)</c>,
    /// <c>G = Y - cr + ((-22554 cb + 18734 cr + 32768) &gt;&gt; 16)</c> and <c>B = Y + 2 cb + ((-14942 cb + 32768) &gt;&gt; 16)</c>.
    /// Each remaining term <c>(x + 32768) &gt;&gt; 16</c> is <c>((x &gt;&gt; 8) + 128) &gt;&gt; 8</c> (nested floor divisions),
    /// and with each constant written <c>256 h + l</c> (|l| below 128), <c>x &gt;&gt; 8</c> is <c>h c + ((l c) &gt;&gt; 8)</c>:
    /// <c>26345 = 256 * 103 - 23</c>, <c>-14942 = 256 * -58 - 94</c>, <c>-22554 = 256 * -88 - 26</c>, <c>18734 = 256 * 73 + 46</c>.
    /// With <c>cb</c> and <c>cr</c> in -128..127, no product or sum exceeds 20,535 in magnitude.
    /// </para>
    /// <para>The results are clamped, narrowed to bytes and interleaved by byte shuffles.</para>
    /// </remarks>
    private static int ConvertYCbCrVector(ReadOnlySpan<byte> luma, ReadOnlySpan<byte> blue, ReadOnlySpan<byte> red, Span<byte> destination)
    {
        ref var lumaStart = ref unsafe(MemoryMarshal.GetReference(luma));
        ref var blueStart = ref unsafe(MemoryMarshal.GetReference(blue));
        ref var redStart = ref unsafe(MemoryMarshal.GetReference(red));
        ref var destinationStart = ref unsafe(MemoryMarshal.GetReference(destination));
        var length = Math.Min(Math.Min(luma.Length, Math.Min(blue.Length, red.Length)), destination.Length / 3);
        var x = 0;
        for (; x + 16 <= length; x += 16)
        {
            ConvertYCbCr16(
                unsafe(Vector128.LoadUnsafe(ref lumaStart, (nuint)x)),
                unsafe(Vector128.LoadUnsafe(ref blueStart, (nuint)x)),
                unsafe(Vector128.LoadUnsafe(ref redStart, (nuint)x)),
                out var r, out var g, out var b);
            Interleave16(r, g, b, ref unsafe(Unsafe.Add(ref destinationStart, 3 * x)));
        }

        return x;
    }

    /// <summary>Converts 16 pixels with the arithmetic of <see cref="ConvertYCbCrVector"/>: the R, G and B samples.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void ConvertYCbCr16(Vector128<byte> luma, Vector128<byte> blue, Vector128<byte> red, out Vector128<byte> r, out Vector128<byte> g, out Vector128<byte> b)
    {
        var (y0, y1) = Vector128.Widen(luma);
        var (b0, b1) = Vector128.Widen(blue);
        var (r0, r1) = Vector128.Widen(red);
        Convert8(y0.AsInt16(), b0.AsInt16() - Vector128.Create((short)128), r0.AsInt16() - Vector128.Create((short)128), out var red0, out var green0, out var blue0);
        Convert8(y1.AsInt16(), b1.AsInt16() - Vector128.Create((short)128), r1.AsInt16() - Vector128.Create((short)128), out var red1, out var green1, out var blue1);
        r = Vector128.Narrow(red0, red1);
        g = Vector128.Narrow(green0, green1);
        b = Vector128.Narrow(blue0, blue1);
    }

    /// <summary>Eight pixels of <see cref="ConvertYCbCrVector"/>: R, G and B clamped to 0..255, in 16-bit lanes.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void Convert8(Vector128<short> luma, Vector128<short> cb, Vector128<short> cr, out Vector128<ushort> red, out Vector128<ushort> green, out Vector128<ushort> blue)
    {
        var rounding = Vector128.Create((short)128);
        var redTerm = Vector128.ShiftRightArithmetic((Vector128.Create((short)103) * cr) + Vector128.ShiftRightArithmetic(Vector128.Create((short)-23) * cr, 8) + rounding, 8);
        var blueTerm = Vector128.ShiftRightArithmetic((Vector128.Create((short)-58) * cb) + Vector128.ShiftRightArithmetic(Vector128.Create((short)-94) * cb, 8) + rounding, 8);
        var greenHigh = (Vector128.Create((short)-88) * cb) + (Vector128.Create((short)73) * cr);
        var greenLow = (Vector128.Create((short)-26) * cb) + (Vector128.Create((short)46) * cr);
        var greenTerm = Vector128.ShiftRightArithmetic(greenHigh + Vector128.ShiftRightArithmetic(greenLow, 8) + rounding, 8);
        red = Clamp(luma + cr + redTerm);
        green = Clamp(luma - cr + greenTerm);
        blue = Clamp(luma + cb + cb + blueTerm);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static Vector128<ushort> Clamp(Vector128<short> value) => Vector128.Min(Vector128.Max(value, Vector128<short>.Zero), Vector128.Create((short)255)).AsUInt16();
    }

    /// <summary>
    /// Converts the pixels of whole 32-pixel groups with the arithmetic of <see cref="ConvertYCbCrVector"/> in 256-bit
    /// vectors; the interleaving is the one of 16 pixels, on each half.
    /// </summary>
    /// <returns>The number of pixels converted.</returns>
    private static int ConvertYCbCrVector256(ReadOnlySpan<byte> luma, ReadOnlySpan<byte> blue, ReadOnlySpan<byte> red, Span<byte> destination)
    {
        ref var lumaStart = ref unsafe(MemoryMarshal.GetReference(luma));
        ref var blueStart = ref unsafe(MemoryMarshal.GetReference(blue));
        ref var redStart = ref unsafe(MemoryMarshal.GetReference(red));
        ref var destinationStart = ref unsafe(MemoryMarshal.GetReference(destination));
        var length = Math.Min(Math.Min(luma.Length, Math.Min(blue.Length, red.Length)), destination.Length / 3);
        var x = 0;
        for (; x + 32 <= length; x += 32)
        {
            ConvertYCbCr32(
                unsafe(Vector256.LoadUnsafe(ref lumaStart, (nuint)x)),
                unsafe(Vector256.LoadUnsafe(ref blueStart, (nuint)x)),
                unsafe(Vector256.LoadUnsafe(ref redStart, (nuint)x)),
                out var r, out var g, out var b);
            ref var output = ref unsafe(Unsafe.Add(ref destinationStart, 3 * x));
            Interleave16(r.GetLower(), g.GetLower(), b.GetLower(), ref output);
            Interleave16(r.GetUpper(), g.GetUpper(), b.GetUpper(), ref unsafe(Unsafe.Add(ref output, 48)));
        }

        return x;
    }

    /// <summary>Converts 32 pixels with the arithmetic of <see cref="ConvertYCbCrVector"/> in 256-bit vectors: the R, G and B samples.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void ConvertYCbCr32(Vector256<byte> luma, Vector256<byte> blue, Vector256<byte> red, out Vector256<byte> r, out Vector256<byte> g, out Vector256<byte> b)
    {
        var (y0, y1) = Vector256.Widen(luma);
        var (b0, b1) = Vector256.Widen(blue);
        var (r0, r1) = Vector256.Widen(red);
        Convert16(y0.AsInt16(), b0.AsInt16() - Vector256.Create((short)128), r0.AsInt16() - Vector256.Create((short)128), out var red0, out var green0, out var blue0);
        Convert16(y1.AsInt16(), b1.AsInt16() - Vector256.Create((short)128), r1.AsInt16() - Vector256.Create((short)128), out var red1, out var green1, out var blue1);
        r = Vector256.Narrow(red0, red1);
        g = Vector256.Narrow(green0, green1);
        b = Vector256.Narrow(blue0, blue1);
    }

    /// <summary>Sixteen pixels of <see cref="ConvertYCbCrVector256"/>: R, G and B clamped to 0..255, in 16-bit lanes.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void Convert16(Vector256<short> luma, Vector256<short> cb, Vector256<short> cr, out Vector256<ushort> red, out Vector256<ushort> green, out Vector256<ushort> blue)
    {
        var rounding = Vector256.Create((short)128);
        var redTerm = Vector256.ShiftRightArithmetic((Vector256.Create((short)103) * cr) + Vector256.ShiftRightArithmetic(Vector256.Create((short)-23) * cr, 8) + rounding, 8);
        var blueTerm = Vector256.ShiftRightArithmetic((Vector256.Create((short)-58) * cb) + Vector256.ShiftRightArithmetic(Vector256.Create((short)-94) * cb, 8) + rounding, 8);
        var greenHigh = (Vector256.Create((short)-88) * cb) + (Vector256.Create((short)73) * cr);
        var greenLow = (Vector256.Create((short)-26) * cb) + (Vector256.Create((short)46) * cr);
        var greenTerm = Vector256.ShiftRightArithmetic(greenHigh + Vector256.ShiftRightArithmetic(greenLow, 8) + rounding, 8);
        red = Clamp(luma + cr + redTerm);
        green = Clamp(luma - cr + greenTerm);
        blue = Clamp(luma + cb + cb + blueTerm);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static Vector256<ushort> Clamp(Vector256<short> value) => Vector256.Min(Vector256.Max(value, Vector256<short>.Zero), Vector256.Create((short)255)).AsUInt16();
    }

    /// <summary>Converts 64 pixels with the arithmetic of <see cref="ConvertYCbCrVector"/> in 512-bit vectors: the R, G and B samples.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void ConvertYCbCr64(Vector512<byte> luma, Vector512<byte> blue, Vector512<byte> red, out Vector512<byte> r, out Vector512<byte> g, out Vector512<byte> b)
    {
        var (y0, y1) = Vector512.Widen(luma);
        var (b0, b1) = Vector512.Widen(blue);
        var (r0, r1) = Vector512.Widen(red);
        Convert32(y0.AsInt16(), b0.AsInt16() - Vector512.Create((short)128), r0.AsInt16() - Vector512.Create((short)128), out var red0, out var green0, out var blue0);
        Convert32(y1.AsInt16(), b1.AsInt16() - Vector512.Create((short)128), r1.AsInt16() - Vector512.Create((short)128), out var red1, out var green1, out var blue1);
        r = Vector512.Narrow(red0, red1);
        g = Vector512.Narrow(green0, green1);
        b = Vector512.Narrow(blue0, blue1);
    }

    /// <summary>Thirty-two pixels of <see cref="ConvertYCbCr64"/>: R, G and B clamped to 0..255, in 16-bit lanes.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void Convert32(Vector512<short> luma, Vector512<short> cb, Vector512<short> cr, out Vector512<ushort> red, out Vector512<ushort> green, out Vector512<ushort> blue)
    {
        var rounding = Vector512.Create((short)128);
        var redTerm = Vector512.ShiftRightArithmetic((Vector512.Create((short)103) * cr) + Vector512.ShiftRightArithmetic(Vector512.Create((short)-23) * cr, 8) + rounding, 8);
        var blueTerm = Vector512.ShiftRightArithmetic((Vector512.Create((short)-58) * cb) + Vector512.ShiftRightArithmetic(Vector512.Create((short)-94) * cb, 8) + rounding, 8);
        var greenHigh = (Vector512.Create((short)-88) * cb) + (Vector512.Create((short)73) * cr);
        var greenLow = (Vector512.Create((short)-26) * cb) + (Vector512.Create((short)46) * cr);
        var greenTerm = Vector512.ShiftRightArithmetic(greenHigh + Vector512.ShiftRightArithmetic(greenLow, 8) + rounding, 8);
        red = Clamp(luma + cr + redTerm);
        green = Clamp(luma - cr + greenTerm);
        blue = Clamp(luma + cb + cb + blueTerm);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static Vector512<ushort> Clamp(Vector512<short> value) => Vector512.Min(Vector512.Max(value, Vector512<short>.Zero), Vector512.Create((short)255)).AsUInt16();
    }

    /// <summary>
    /// Writes 64 opaque RGBA pixels (256 bytes) from 64 red, green and blue values: byte interleaving within each 128-bit
    /// block (the pixels 16 i + 4 j to 16 i + 4 j + 3 in the block i of the vector j), then a transposition of the blocks.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void StoreOpaqueRgba64(Vector512<byte> r, Vector512<byte> g, Vector512<byte> b, ref byte output)
    {
        var a = Vector512<byte>.AllBitsSet;
        var redGreenLow = Avx512BW.UnpackLow(r, g).AsUInt16();
        var redGreenHigh = Avx512BW.UnpackHigh(r, g).AsUInt16();
        var blueAlphaLow = Avx512BW.UnpackLow(b, a).AsUInt16();
        var blueAlphaHigh = Avx512BW.UnpackHigh(b, a).AsUInt16();
        var pixels0 = Avx512BW.UnpackLow(redGreenLow, blueAlphaLow).AsDouble();
        var pixels1 = Avx512BW.UnpackHigh(redGreenLow, blueAlphaLow).AsDouble();
        var pixels2 = Avx512BW.UnpackLow(redGreenHigh, blueAlphaHigh).AsDouble();
        var pixels3 = Avx512BW.UnpackHigh(redGreenHigh, blueAlphaHigh).AsDouble();

        // Output vector i is the block i of pixels0 to pixels3
        var low01 = Avx512F.Shuffle4x128(pixels0, pixels1, 0b01_00_01_00);
        var high01 = Avx512F.Shuffle4x128(pixels0, pixels1, 0b11_10_11_10);
        var low23 = Avx512F.Shuffle4x128(pixels2, pixels3, 0b01_00_01_00);
        var high23 = Avx512F.Shuffle4x128(pixels2, pixels3, 0b11_10_11_10);
        unsafe { Avx512F.Shuffle4x128(low01, low23, 0b10_00_10_00).AsByte().StoreUnsafe(ref output); }
        unsafe { Avx512F.Shuffle4x128(low01, low23, 0b11_01_11_01).AsByte().StoreUnsafe(ref output, 64); }
        unsafe { Avx512F.Shuffle4x128(high01, high23, 0b10_00_10_00).AsByte().StoreUnsafe(ref output, 128); }
        unsafe { Avx512F.Shuffle4x128(high01, high23, 0b11_01_11_01).AsByte().StoreUnsafe(ref output, 192); }
    }

    /// <summary>Converts full-range YCbCr samples to opaque RGBA pixels, with the arithmetic of <see cref="ConvertYCbCr"/>.</summary>
    internal static void ConvertYCbCrToRgba(ReadOnlySpan<byte> luma, ReadOnlySpan<byte> blue, ReadOnlySpan<byte> red, Span<byte> destination)
    {
        var done = 0;
        if (UseVectors)
        {
            ref var lumaStart = ref unsafe(MemoryMarshal.GetReference(luma));
            ref var blueStart = ref unsafe(MemoryMarshal.GetReference(blue));
            ref var redStart = ref unsafe(MemoryMarshal.GetReference(red));
            ref var destinationStart = ref unsafe(MemoryMarshal.GetReference(destination));
            var length = Math.Min(Math.Min(luma.Length, Math.Min(blue.Length, red.Length)), destination.Length / 4);
            if (UseVectors512)
            {
                for (; done + 64 <= length; done += 64)
                {
                    ConvertYCbCr64(
                        unsafe(Vector512.LoadUnsafe(ref lumaStart, (nuint)done)),
                        unsafe(Vector512.LoadUnsafe(ref blueStart, (nuint)done)),
                        unsafe(Vector512.LoadUnsafe(ref redStart, (nuint)done)),
                        out var r, out var g, out var b);
                    StoreOpaqueRgba64(r, g, b, ref unsafe(Unsafe.Add(ref destinationStart, 4 * done)));
                }
            }

            if (UseVectors256)
            {
                for (; done + 32 <= length; done += 32)
                {
                    ConvertYCbCr32(
                        unsafe(Vector256.LoadUnsafe(ref lumaStart, (nuint)done)),
                        unsafe(Vector256.LoadUnsafe(ref blueStart, (nuint)done)),
                        unsafe(Vector256.LoadUnsafe(ref redStart, (nuint)done)),
                        out var r, out var g, out var b);
                    ref var output = ref unsafe(Unsafe.Add(ref destinationStart, 4 * done));
                    StoreOpaqueRgba16(r.GetLower(), g.GetLower(), b.GetLower(), ref output);
                    StoreOpaqueRgba16(r.GetUpper(), g.GetUpper(), b.GetUpper(), ref unsafe(Unsafe.Add(ref output, 64)));
                }
            }

            for (; done + 16 <= length; done += 16)
            {
                ConvertYCbCr16(
                    unsafe(Vector128.LoadUnsafe(ref lumaStart, (nuint)done)),
                    unsafe(Vector128.LoadUnsafe(ref blueStart, (nuint)done)),
                    unsafe(Vector128.LoadUnsafe(ref redStart, (nuint)done)),
                    out var r, out var g, out var b);
                StoreOpaqueRgba16(r, g, b, ref unsafe(Unsafe.Add(ref destinationStart, 4 * done)));
            }
        }

        ConvertYCbCrToRgbaScalar(luma[done..], blue[done..], red[done..], destination[(4 * done)..]);
    }

    /// <summary>The scalar reference of <see cref="ConvertYCbCrToRgba"/>.</summary>
    internal static void ConvertYCbCrToRgbaScalar(ReadOnlySpan<byte> luma, ReadOnlySpan<byte> blue, ReadOnlySpan<byte> red, Span<byte> destination)
    {
        var crToR = CrToR;
        var cbToB = CbToB;
        var cbToG = CbToG;
        var crToG = CrToG;
        for (var x = 0; x < luma.Length; x++)
        {
            int yValue = luma[x];
            int cb = blue[x];
            int cr = red[x];
            destination[4 * x] = Clamp(yValue + crToR[cr]);
            destination[(4 * x) + 1] = Clamp(yValue + ((cbToG[cb] + crToG[cr]) >> 16));
            destination[(4 * x) + 2] = Clamp(yValue + cbToB[cb]);
            destination[(4 * x) + 3] = 255;
        }
    }

    private static byte Clamp(int value) => (uint)value <= 255 ? (byte)value : value < 0 ? (byte)0 : (byte)255;

    /// <summary>Gets the samples of a component for an output row, upsampled to the frame width.</summary>
    private ReadOnlySpan<byte> GetSamples(JpegFrameComponent component, int y)
    {
        var horizontal = component.HorizontalRatio;
        var vertical = component.VerticalRatio;
        if (horizontal == 1 && vertical == 1)
            return component.GetRow(y)[.._width];

        var output = _upsampled[component.Index]!.RawBuffer.AsSpan(0, _width);
        if (horizontal == 2 && component.Width <= 2)
        {
            // Planes of at most two samples with a horizontal factor of 2 (frames of at most 4 pixels) are replicated in both
            // directions, like libjpeg-turbo
            Replicate(component.GetRow(y / vertical), horizontal, output);
        }
        else if (vertical == 2 && horizontal <= 2)
        {
            // Triangle filter: the nearer sample row weighs 3/4, the other neighbor row (replicated at the edges) 1/4
            var row = y >> 1;
            var upper = (y & 1) == 0;
            var other = Math.Clamp(upper ? row - 1 : row + 1, 0, component.Height - 1);
            var near = component.GetRow(row);
            var far = component.GetRow(other);
            if (horizontal == 2)
            {
                UpsampleBoth(near, far, output, unsafe(MemoryMarshal.Cast<byte, ushort>(_columnSums[component.Index]!.RawBuffer.AsSpan())));
            }
            else
            {
                UpsampleVertical(near, far, upper ? 1 : 2, output);
            }
        }
        else if (vertical == 1 && horizontal == 2)
        {
            UpsampleHorizontal(component.GetRow(y), output);
        }
        else
        {
            // Replication for the other integral factors
            Replicate(component.GetRow(y / vertical), horizontal, output);
        }

        return output;
    }

    private static void Replicate(ReadOnlySpan<byte> samples, int factor, Span<byte> output)
    {
        for (var x = 0; x < output.Length; x++)
        {
            output[x] = samples[x / factor];
        }
    }

    /// <summary>Horizontal triangle filter (factor 2).</summary>
    internal static void UpsampleHorizontal(ReadOnlySpan<byte> samples, Span<byte> output)
    {
        if (!UseVectors)
        {
            UpsampleHorizontalScalar(samples, output);
            return;
        }

        // Inner samples by groups of 16 (their neighbors exist); the edges and the remainder use the scalar formula
        var end = UpsampleHorizontalInner(samples, output);
        UpsampleHorizontalRange(samples, output, 0, 1);
        UpsampleHorizontalRange(samples, output, end, samples.Length);
    }

    /// <summary>The scalar reference of <see cref="UpsampleHorizontal"/>.</summary>
    internal static void UpsampleHorizontalScalar(ReadOnlySpan<byte> samples, Span<byte> output) => UpsampleHorizontalRange(samples, output, 0, samples.Length);

    private static void UpsampleHorizontalRange(ReadOnlySpan<byte> samples, Span<byte> output, int start, int end)
    {
        var last = samples.Length - 1;
        for (var j = start; j < end; j++)
        {
            var center = 3 * samples[j];
            var x = 2 * j;
            output[x] = (byte)((center + samples[Math.Max(j - 1, 0)] + 1) >> 2);
            if (x + 1 < output.Length)
            {
                output[x + 1] = (byte)((center + samples[Math.Min(j + 1, last)] + 2) >> 2);
            }
        }
    }

    /// <summary>Vertical triangle filter (factor 2): <c>(3 near + far + bias) &gt;&gt; 2</c>.</summary>
    internal static void UpsampleVertical(ReadOnlySpan<byte> near, ReadOnlySpan<byte> far, int bias, Span<byte> output)
    {
        var x = 0;
        if (UseVectors)
        {
            ref var nearStart = ref unsafe(MemoryMarshal.GetReference(near));
            ref var farStart = ref unsafe(MemoryMarshal.GetReference(far));
            ref var outputStart = ref unsafe(MemoryMarshal.GetReference(output));
            var biasVector = Vector128.Create((ushort)bias);
            var three = Vector128.Create((ushort)3);
            var length = Math.Min(output.Length, Math.Min(near.Length, far.Length));
            for (; x + 16 <= length; x += 16)
            {
                var (n0, n1) = Vector128.Widen(unsafe(Vector128.LoadUnsafe(ref nearStart, (nuint)x)));
                var (f0, f1) = Vector128.Widen(unsafe(Vector128.LoadUnsafe(ref farStart, (nuint)x)));
                var low = Vector128.ShiftRightLogical((three * n0) + f0 + biasVector, 2);
                var high = Vector128.ShiftRightLogical((three * n1) + f1 + biasVector, 2);
                unsafe { Vector128.Narrow(low, high).StoreUnsafe(ref outputStart, (nuint)x); }
            }
        }

        UpsampleVerticalScalar(near[x..], far[x..], bias, output[x..]);
    }

    /// <summary>The scalar reference of <see cref="UpsampleVertical"/>.</summary>
    internal static void UpsampleVerticalScalar(ReadOnlySpan<byte> near, ReadOnlySpan<byte> far, int bias, Span<byte> output)
    {
        for (var x = 0; x < output.Length; x++)
        {
            output[x] = (byte)(((3 * near[x]) + far[x] + bias) >> 2);
        }
    }

    /// <summary>Both factors 2: column sums <c>c = 3 near + far</c>, then the horizontal triangle filter of the sums.</summary>
    /// <param name="near">The nearer sample row.</param>
    /// <param name="far">The other neighbor row, as long as <paramref name="near"/>.</param>
    /// <param name="output">The upsampled row: <c>2 near.Length</c> or <c>2 near.Length - 1</c> samples.</param>
    /// <param name="sums">A buffer of at least <c>near.Length</c> values, which receives the column sums.</param>
    internal static void UpsampleBoth(ReadOnlySpan<byte> near, ReadOnlySpan<byte> far, Span<byte> output, Span<ushort> sums)
    {
        if (!UseVectors)
        {
            UpsampleBothScalar(near, far, output);
            return;
        }

        // The sums are computed once, then the filter reads each of them three times (as the current, previous and next sum)
        sums = sums[..near.Length];
        ColumnSums(near, far[..near.Length], sums);
        var end = UpsampleSums(sums, output);
        UpsampleSumsRange(sums, output, 0, 1);
        UpsampleSumsRange(sums, output, end, sums.Length);
    }

    /// <summary>Computes <c>sums[j] = 3 near[j] + far[j]</c>.</summary>
    private static void ColumnSums(ReadOnlySpan<byte> near, ReadOnlySpan<byte> far, Span<ushort> sums)
    {
        ref var nearStart = ref unsafe(MemoryMarshal.GetReference(near));
        ref var farStart = ref unsafe(MemoryMarshal.GetReference(far));
        ref var sumsStart = ref unsafe(MemoryMarshal.GetReference(sums));
        var length = Math.Min(sums.Length, Math.Min(near.Length, far.Length));
        var j = 0;
        if (UseVectors512)
        {
            for (; j + 64 <= length; j += 64)
            {
                var (n0, n1) = Vector512.Widen(unsafe(Vector512.LoadUnsafe(ref nearStart, (nuint)j)));
                var (f0, f1) = Vector512.Widen(unsafe(Vector512.LoadUnsafe(ref farStart, (nuint)j)));
                unsafe { ((n0 * 3) + f0).StoreUnsafe(ref sumsStart, (nuint)j); }
                unsafe { ((n1 * 3) + f1).StoreUnsafe(ref sumsStart, (nuint)(j + 32)); }
            }
        }

        if (UseVectors256)
        {
            for (; j + 32 <= length; j += 32)
            {
                var (n0, n1) = Vector256.Widen(unsafe(Vector256.LoadUnsafe(ref nearStart, (nuint)j)));
                var (f0, f1) = Vector256.Widen(unsafe(Vector256.LoadUnsafe(ref farStart, (nuint)j)));
                unsafe { ((n0 * 3) + f0).StoreUnsafe(ref sumsStart, (nuint)j); }
                unsafe { ((n1 * 3) + f1).StoreUnsafe(ref sumsStart, (nuint)(j + 16)); }
            }
        }

        for (; j + 16 <= length; j += 16)
        {
            var (n0, n1) = Vector128.Widen(unsafe(Vector128.LoadUnsafe(ref nearStart, (nuint)j)));
            var (f0, f1) = Vector128.Widen(unsafe(Vector128.LoadUnsafe(ref farStart, (nuint)j)));
            unsafe { ((n0 * 3) + f0).StoreUnsafe(ref sumsStart, (nuint)j); }
            unsafe { ((n1 * 3) + f1).StoreUnsafe(ref sumsStart, (nuint)(j + 8)); }
        }

        for (; j < length; j++)
        {
            sums[j] = (ushort)((3 * near[j]) + far[j]);
        }
    }

    /// <summary>
    /// The horizontal triangle filter of the column sums for the inner samples <c>j</c> (from 1, by groups whose right
    /// neighbors exist and whose outputs fit): <c>(3 c[j] + c[j - 1] + 8) &gt;&gt; 4</c> and <c>(3 c[j] + c[j + 1] + 7) &gt;&gt; 4</c>.
    /// Each output pair is formed as <c>even | odd &lt;&lt; 8</c> in a 16-bit lane, which is the interleaved byte order in
    /// little-endian memory.
    /// </summary>
    /// <returns>The first sample not processed (at least 1).</returns>
    private static int UpsampleSums(ReadOnlySpan<ushort> sums, Span<byte> output)
    {
        ref var sumsStart = ref unsafe(MemoryMarshal.GetReference(sums));
        ref var outputStart = ref unsafe(MemoryMarshal.GetReference(output));
        var length = sums.Length;
        var j = 1;
        if (UseVectors512)
        {
            for (; j + 33 <= length && (2 * j) + 64 <= output.Length; j += 32)
            {
                var current = unsafe(Vector512.LoadUnsafe(ref sumsStart, (nuint)j)) * 3;
                var even = Vector512.ShiftRightLogical(current + unsafe(Vector512.LoadUnsafe(ref sumsStart, (nuint)(j - 1))) + Vector512.Create((ushort)8), 4);
                var odd = Vector512.ShiftRightLogical(current + unsafe(Vector512.LoadUnsafe(ref sumsStart, (nuint)(j + 1))) + Vector512.Create((ushort)7), 4);
                unsafe { (even | Vector512.ShiftLeft(odd, 8)).AsByte().StoreUnsafe(ref outputStart, (nuint)(2 * j)); }
            }
        }

        if (UseVectors256)
        {
            for (; j + 17 <= length && (2 * j) + 32 <= output.Length; j += 16)
            {
                var current = unsafe(Vector256.LoadUnsafe(ref sumsStart, (nuint)j)) * 3;
                var even = Vector256.ShiftRightLogical(current + unsafe(Vector256.LoadUnsafe(ref sumsStart, (nuint)(j - 1))) + Vector256.Create((ushort)8), 4);
                var odd = Vector256.ShiftRightLogical(current + unsafe(Vector256.LoadUnsafe(ref sumsStart, (nuint)(j + 1))) + Vector256.Create((ushort)7), 4);
                unsafe { (even | Vector256.ShiftLeft(odd, 8)).AsByte().StoreUnsafe(ref outputStart, (nuint)(2 * j)); }
            }
        }

        for (; j + 9 <= length && (2 * j) + 16 <= output.Length; j += 8)
        {
            var current = unsafe(Vector128.LoadUnsafe(ref sumsStart, (nuint)j)) * 3;
            var even = Vector128.ShiftRightLogical(current + unsafe(Vector128.LoadUnsafe(ref sumsStart, (nuint)(j - 1))) + Vector128.Create((ushort)8), 4);
            var odd = Vector128.ShiftRightLogical(current + unsafe(Vector128.LoadUnsafe(ref sumsStart, (nuint)(j + 1))) + Vector128.Create((ushort)7), 4);
            unsafe { (even | Vector128.ShiftLeft(odd, 8)).AsByte().StoreUnsafe(ref outputStart, (nuint)(2 * j)); }
        }

        return j;
    }

    /// <summary>The scalar reference of <see cref="UpsampleBoth"/>.</summary>
    internal static void UpsampleBothScalar(ReadOnlySpan<byte> near, ReadOnlySpan<byte> far, Span<byte> output)
    {
        var last = near.Length - 1;
        var previous = (3 * near[0]) + far[0];
        var current = previous;
        for (var j = 0; j <= last; j++)
        {
            var next = j < last ? (3 * near[j + 1]) + far[j + 1] : current;
            var x = 2 * j;
            output[x] = (byte)(((3 * current) + previous + 8) >> 4);
            if (x + 1 < output.Length)
            {
                output[x + 1] = (byte)(((3 * current) + next + 7) >> 4);
            }

            previous = current;
            current = next;
        }
    }

    private static void UpsampleSumsRange(ReadOnlySpan<ushort> sums, Span<byte> output, int start, int end)
    {
        var last = sums.Length - 1;
        for (var j = start; j < end; j++)
        {
            int current = sums[j];
            int previous = sums[Math.Max(j - 1, 0)];
            int next = sums[Math.Min(j + 1, last)];
            var x = 2 * j;
            output[x] = (byte)(((3 * current) + previous + 8) >> 4);
            if (x + 1 < output.Length)
            {
                output[x + 1] = (byte)(((3 * current) + next + 7) >> 4);
            }
        }
    }

    /// <summary>
    /// The horizontal triangle filter of the inner samples <c>j</c> (from 1, by groups of 16 whose right neighbors exist and
    /// whose 32 outputs fit) in 16-bit lanes: <c>(3 s + left + 1) &gt;&gt; 2</c> and <c>(3 s + right + 2) &gt;&gt; 2</c>. Each output
    /// pair is formed as <c>even | odd &lt;&lt; 8</c> in a 16-bit lane, which is the interleaved byte order in little-endian memory.
    /// </summary>
    /// <returns>The first sample not processed (at least 1).</returns>
    private static int UpsampleHorizontalInner(ReadOnlySpan<byte> samples, Span<byte> output)
    {
        ref var samplesStart = ref unsafe(MemoryMarshal.GetReference(samples));
        ref var outputStart = ref unsafe(MemoryMarshal.GetReference(output));
        var three = Vector128.Create((ushort)3);

        // Every load is in bounds: 16 samples from j - 1 to j + 1
        var j = 1;
        for (; j + 17 <= samples.Length && (2 * j) + 32 <= output.Length; j += 16)
        {
            var (current0, current1) = Vector128.Widen(unsafe(Vector128.LoadUnsafe(ref samplesStart, (nuint)j)));
            var (previous0, previous1) = Vector128.Widen(unsafe(Vector128.LoadUnsafe(ref samplesStart, (nuint)(j - 1))));
            var (next0, next1) = Vector128.Widen(unsafe(Vector128.LoadUnsafe(ref samplesStart, (nuint)(j + 1))));
            var even0 = Vector128.ShiftRightLogical((three * current0) + previous0 + Vector128.Create((ushort)1), 2);
            var odd0 = Vector128.ShiftRightLogical((three * current0) + next0 + Vector128.Create((ushort)2), 2);
            var even1 = Vector128.ShiftRightLogical((three * current1) + previous1 + Vector128.Create((ushort)1), 2);
            var odd1 = Vector128.ShiftRightLogical((three * current1) + next1 + Vector128.Create((ushort)2), 2);
            ref var destination = ref unsafe(Unsafe.Add(ref outputStart, 2 * j));
            unsafe { (even0 | Vector128.ShiftLeft(odd0, 8)).AsByte().StoreUnsafe(ref destination); }
            unsafe { (even1 | Vector128.ShiftLeft(odd1, 8)).AsByte().StoreUnsafe(ref Unsafe.Add(ref destination, 16)); }
        }

        return j;
    }
}
