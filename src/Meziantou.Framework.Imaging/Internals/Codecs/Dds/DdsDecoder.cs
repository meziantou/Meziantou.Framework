namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The DDS pixel sink, shared by eager loads, sequential readers and collection entries: <see cref="DdsStructureParser"/>
/// gives it the rows of a surface in <see cref="DdsPixelLayout.SourcePixelFormat"/>, and it writes them to the frame.
/// </summary>
/// <remarks>
/// Rows of floating-point pixels that are converted go through a staging image converted at the end, since a conversion
/// that tone maps needs the peak of the whole image first; the other rows are written to the frame directly.
/// </remarks>
internal sealed class DdsDecoder : IImageDecodeObserver
{
    private readonly ImageDecodeRequest _request;
    private readonly ImageCodecContext _context;
    private DdsStructureParser? _structure;
    private DecodedFrameSink? _sink;
    private Image? _staging;
    private bool _frameStarted;

    public DdsDecoder(ImageDecodeRequest request, ImageCodecContext context)
    {
        _request = request;
        _context = context;
    }

    /// <summary>Gets or sets the size of the surface to decode, when it is not the top surface (a collection entry).</summary>
    public Size? SurfaceSize { get; set; }

    public void OnHeaderComplete(DdsStructureParser structure)
    {
        _structure = structure;
        var layout = structure.Header.Layout;
        _sink = DecodedFrameSink.Create(_context, _request, ImageFormat.Dds, SurfaceSize ?? structure.Header.Size, layout.DefaultPixelFormat, layout.SourcePixelFormat, iccProfile: null);
    }

    /// <summary>Writes rows of the source layout.</summary>
    /// <param name="y">The first row.</param>
    /// <param name="count">The number of rows.</param>
    /// <param name="rows">The rows, one after the other.</param>
    /// <param name="rowLength">The number of bytes of a row.</param>
    public void WriteRows(int y, int count, ReadOnlySpan<byte> rows, int rowLength)
    {
        var sink = _sink ?? throw new InvalidOperationException("The DDS header was not parsed.");
        if (!sink.Plan.IsIdentity && PixelFormats.IsFloatingPoint(sink.SourcePixelFormat))
        {
            _staging ??= sink.SourcePixelFormat == PixelFormat.RgbaHalf
                ? new Image<RgbaHalf>(_context.Configuration, sink.Canvas, _context.Scope, layoutOptions: null)
                : new Image<RgbaFloat>(_context.Configuration, sink.Canvas, _context.Scope, layoutOptions: null);
            using var staging = _staging.FrameCore(0).GetStorage().AcquireLease();
            for (var i = 0; i < count; i++)
            {
                rows.Slice(i * rowLength, rowLength).CopyTo(staging.GetRowBytes(y + i));
            }

            return;
        }

        if (!_frameStarted)
        {
            sink.BeginFrame(FrameDuration.Zero);
            _frameStarted = true;
        }

        using var lease = sink.LeaseCurrentFrame();
        for (var i = 0; i < count; i++)
        {
            sink.WriteRow(lease, y + i, rows.Slice(i * rowLength, rowLength));
        }
    }

    public void OnEnd(DdsStructureParser structure)
    {
        var sink = _sink ?? throw new InvalidOperationException("The DDS header was not parsed.");
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
            throw new InvalidOperationException("The DDS surface has no row.");
        }

        sink.UpdateMetadata(structure.Metadata);
        sink.EndImage();
    }

    public Image GetResult()
    {
        var structure = _structure ?? throw new InvalidOperationException("The DDS data was not decoded.");
        return _sink!.Build(structure.Metadata, animation: null);
    }

    public void Dispose()
    {
        _sink?.Dispose();
        _sink = null;
        _staging?.Dispose();
        _staging = null;
    }
}
