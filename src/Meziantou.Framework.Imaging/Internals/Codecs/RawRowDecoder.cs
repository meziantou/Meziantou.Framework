using Meziantou.Framework.Imaging.Metadata;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The pixel sink of the formats whose structure parser expands each stored row itself (DPX, Cineon), shared by eager loads
/// and sequential readers: the parser gives it rows in the source layout of the image, and it writes them to a
/// <see cref="DecodedFrameSink"/>.
/// </summary>
/// <remarks>
/// Floating-point rows that the frame does not hold as they are go to a staging image, converted to the frame at the end:
/// a conversion rejects the samples it cannot store, and a tone mapping needs the peak of the whole image first, so a
/// truncated file is reported as truncated before any sample is converted.
/// </remarks>
internal sealed class RawRowDecoder : IImageDecodeObserver
{
    private readonly ImageDecodeRequest _request;
    private readonly ImageCodecContext _context;
    private readonly ImageFormat _format;
    private DecodedFrameSink? _sink;
    private Image? _staging;
    private Size _size;
    private ImageMetadata? _metadata;
    private bool _frameStarted;

    public RawRowDecoder(ImageDecodeRequest request, ImageCodecContext context, ImageFormat format)
    {
        _request = request;
        _context = context;
        _format = format;
    }

    /// <summary>Creates the frame sink once the header is parsed.</summary>
    public void OnHeaderComplete(Size size, PixelFormat defaultPixelFormat, PixelFormat sourcePixelFormat)
    {
        _size = size;
        _sink = DecodedFrameSink.Create(_context, _request, _format, size, defaultPixelFormat, sourcePixelFormat, iccProfile: null);
    }

    /// <summary>Starts the frame on its first row and leases it (or the staging image); dispose the lease before returning from the parser call.</summary>
    public PixelLease LeaseRows()
    {
        var sink = _sink ?? throw new InvalidOperationException("The header was not parsed.");
        if (UsesStaging)
        {
            _staging ??= new Image<RgbaFloat>(_context.Configuration, _size, _context.Scope, layoutOptions: null);
            return _staging.FrameCore(0).GetStorage().AcquireLease();
        }

        if (!_frameStarted)
        {
            sink.BeginFrame(FrameDuration.Zero);
            _frameStarted = true;
        }

        return sink.LeaseCurrentFrame();
    }

    /// <summary>Writes one row in the source layout through a lease from <see cref="LeaseRows"/>.</summary>
    public void WriteRow(scoped in PixelLease lease, int y, ReadOnlySpan<byte> row)
    {
        if (UsesStaging)
        {
            row.CopyTo(lease.GetRowBytes(y));
            return;
        }

        _sink!.WriteRow(lease, y, row);
    }

    /// <summary>Gets the frame row to expand a row into through a lease from <see cref="LeaseRows"/>, or an empty span when rows are converted (<see cref="DecodedFrameSink.GetDirectRow"/>).</summary>
    public Span<byte> GetDirectRow(scoped in PixelLease lease, int y) => UsesStaging ? lease.GetRowBytes(y) : _sink!.GetDirectRow(lease, y);

    public void OnEnd(ImageMetadata metadata)
    {
        var sink = _sink ?? throw new InvalidOperationException("The header was not parsed.");
        if (_staging is { } staging)
        {
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
            throw new InvalidOperationException("The image has no row.");
        }

        _metadata = metadata;
        sink.UpdateMetadata(metadata);
        sink.EndImage();
    }

    public Image GetResult()
    {
        var metadata = _metadata ?? throw new InvalidOperationException("The image data was not decoded.");
        return _sink!.Build(metadata, animation: null);
    }

    public void Dispose()
    {
        _sink?.Dispose();
        _sink = null;
        _staging?.Dispose();
        _staging = null;
    }

    private bool UsesStaging => _sink!.SourcePixelFormat == PixelFormat.RgbaFloat && !_sink.Plan.IsIdentity;
}
