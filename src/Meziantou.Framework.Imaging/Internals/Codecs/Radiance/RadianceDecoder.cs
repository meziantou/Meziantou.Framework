using System.Runtime.InteropServices;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The Radiance HDR pixel sink, shared by eager loads and sequential readers: <see cref="RadianceStructureParser"/> gives
/// it each scanline of RGBE pixels, and it writes their <see cref="PixelFormat.RgbaFloat"/> values (alpha 1) to their
/// place in the image.
/// </summary>
/// <remarks>
/// <para>
/// The image is in the standard orientation of Radiance: its first row is the top of the picture (the largest Y), and X
/// grows to the right. A scanline is a row or, when the resolution string gives the X axis first, a column, and it is
/// stored from either end; its pixels are written where the resolution string puts them, directly in the frame when it
/// holds the source layout, otherwise in a staging image converted to the frame at the end (a conversion that tone maps
/// needs the peak of the whole image first).
/// </para>
/// <para>
/// XYZE pictures are rejected with <see cref="UnsupportedImageFeatureException"/> before anything is allocated: their
/// samples are CIE XYZ (with the luminous efficacy of Radiance in Y), which no pixel format holds, and the library does not
/// convert colors implicitly.
/// </para>
/// </remarks>
internal sealed class RadianceDecoder : IImageDecodeObserver
{
    private readonly ImageDecodeRequest _request;
    private readonly ImageCodecContext _context;
    private RadianceStructureParser? _structure;
    private DecodedFrameSink? _sink;
    private Image? _staging;
    private bool _frameStarted;

    public RadianceDecoder(ImageDecodeRequest request, ImageCodecContext context)
    {
        _request = request;
        _context = context;
    }

    public void OnHeaderComplete(RadianceStructureParser structure)
    {
        if (structure.IsXyz)
            throw RadianceFormat.Unsupported("The Radiance HDR file stores CIE XYZ samples (32-bit_rle_xyze), which no pixel format holds: converting them to RGB is a color conversion the library does not make implicitly. Convert the file to RGBE first, for example with 'ra_xyze -r'.", "XYZE samples");

        _structure = structure;
        _sink = DecodedFrameSink.Create(_context, _request, ImageFormat.Radiance, structure.Size, DefaultPixelFormats.Radiance, PixelFormat.RgbaFloat, structure.Metadata.IccProfile);
    }

    /// <summary>Writes the pixels of a scanline to their place in the image.</summary>
    /// <param name="structure">The parser.</param>
    /// <param name="index">The index of the scanline in the file.</param>
    /// <param name="rgbe">The RGBE pixels of the scanline, four bytes each.</param>
    public void OnScanline(RadianceStructureParser structure, int index, ReadOnlySpan<byte> rgbe)
    {
        using var lease = AcquireTarget();
        var count = structure.ScanlineLength;
        if (!structure.IsColumnMajor)
        {
            // A row: the top row is the largest Y, so rows stored from the top ("-Y") come in order
            var y = structure.IsMajorDecreasing ? index : structure.Height - 1 - index;
            var row = lease.GetRowBytes(y);
            if (!structure.IsMinorDecreasing)
            {
                RadianceFormat.ToFloats(rgbe, unsafe(MemoryMarshal.Cast<byte, float>(row[..(count * 16)])));
                return;
            }

            var pixels = unsafe(MemoryMarshal.Cast<byte, float>(row[..(count * 16)]));
            for (var i = 0; i < count; i++)
            {
                RadianceFormat.ToFloats(rgbe, i * 4, pixels.Slice((count - 1 - i) * 4, 4));
            }

            return;
        }

        // A column: the pixels go up the picture ("+Y") or down it ("-Y")
        var x = structure.IsMajorDecreasing ? structure.Width - 1 - index : index;
        for (var i = 0; i < count; i++)
        {
            var y = structure.IsMinorDecreasing ? i : structure.Height - 1 - i;
            RadianceFormat.ToFloats(rgbe, i * 4, unsafe(MemoryMarshal.Cast<byte, float>(lease.GetRowBytes(y).Slice(x * 16, 16))));
        }
    }

    public void OnEnd(RadianceStructureParser structure)
    {
        var sink = _sink ?? throw new InvalidOperationException("The Radiance HDR header was not parsed.");
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
            throw new InvalidOperationException("The Radiance HDR image has no scanline.");
        }

        sink.UpdateMetadata(structure.Metadata);
        sink.EndImage();
    }

    public Image GetResult()
    {
        var structure = _structure ?? throw new InvalidOperationException("The Radiance HDR data was not decoded.");
        return _sink!.Build(structure.Metadata, animation: null);
    }

    public void Dispose()
    {
        _sink?.Dispose();
        _sink = null;
        _staging?.Dispose();
        _staging = null;
    }

    private PixelLease AcquireTarget()
    {
        var sink = _sink ?? throw new InvalidOperationException("The Radiance HDR header was not parsed.");
        if (!sink.Plan.IsIdentity)
        {
            _staging ??= new Image<RgbaFloat>(_context.Configuration, _structure!.Size, _context.Scope, layoutOptions: null);
            return _staging.FrameCore(0).GetStorage().AcquireLease();
        }

        if (!_frameStarted)
        {
            sink.BeginFrame(FrameDuration.Zero);
            _frameStarted = true;
        }

        return sink.LeaseCurrentFrame();
    }
}
