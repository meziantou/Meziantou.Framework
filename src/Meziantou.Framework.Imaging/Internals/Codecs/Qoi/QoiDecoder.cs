namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The QOI pixel sink, shared by eager loads and sequential readers:
/// <see cref="QoiStructureParser"/> reconstructs each row and this observer writes it to a <see cref="DecodedFrameSink"/> in
/// the source layout of the header (<see cref="PixelFormat.Rgb24"/> for 3 channels, <see cref="PixelFormat.Rgba32"/> for 4,
/// or for 3 channels requested as <see cref="PixelFormat.Rgba32"/>: opaque rows that need no conversion), so 8-bit samples,
/// including the colors of fully transparent pixels, are preserved exactly.
/// </summary>
/// <remarks>
/// Call order: <see cref="OnHeaderComplete"/> once, <see cref="WriteRow"/> for every row (the frame is begun at the first row,
/// after the sequential reader's header yield), then <see cref="OnEnd"/> once the end marker is validated: the only frame
/// ends there, so readers and eager loads see the same complete, validated image.
/// </remarks>
internal sealed class QoiDecoder : IImageDecodeObserver
{
    private readonly ImageDecodeRequest _request;
    private readonly ImageCodecContext _context;
    private QoiStructureParser? _structure;
    private DecodedFrameSink? _sink;
    private bool _frameStarted;

    public QoiDecoder(ImageDecodeRequest request, ImageCodecContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);
        _request = request;
        _context = context;
    }

    /// <summary>Gets the number of channels of the rows given to <see cref="WriteRow"/>: 4 for an RGB image requested as <see cref="PixelFormat.Rgba32"/> (opaque rows, no conversion), otherwise the channels of the image.</summary>
    public int RowChannels { get; private set; }

    public void OnHeaderComplete(QoiStructureParser structure)
    {
        ArgumentNullException.ThrowIfNull(structure);
        _structure = structure;
        var rgba = structure.Channels == 4 || _request.PixelFormat == PixelFormat.Rgba32;
        RowChannels = rgba ? 4 : 3;
        _sink = DecodedFrameSink.Create(_context, _request, ImageFormat.Qoi, structure.Size, structure.DefaultPixelFormat, rgba ? PixelFormat.Rgba32 : PixelFormat.Rgb24, iccProfile: null);
    }

    /// <summary>Starts the frame on its first row and leases it; dispose the lease before returning from the parser call.</summary>
    public PixelLease LeaseRows()
    {
        var sink = _sink ?? throw new InvalidOperationException("The QOI header was not parsed.");
        if (!_frameStarted)
        {
            sink.BeginFrame(FrameDuration.Zero);
            _frameStarted = true;
        }

        return sink.LeaseCurrentFrame();
    }

    /// <summary>Writes one row through a lease from <see cref="LeaseRows"/>.</summary>
    public void WriteRow(scoped in PixelLease lease, int y, ReadOnlySpan<byte> row) => _sink!.WriteRow(lease, y, row);

    public void OnEnd(QoiStructureParser structure)
    {
        ArgumentNullException.ThrowIfNull(structure);
        if (!_frameStarted)
            throw new InvalidOperationException("The QOI image has no row.");

        var sink = _sink!;
        sink.UpdateMetadata(structure.Metadata);
        sink.EndImage();
    }

    public Image GetResult()
    {
        var structure = _structure ?? throw new InvalidOperationException("The QOI data was not decoded.");
        return _sink!.Build(structure.Metadata, animation: null);
    }

    public void Dispose()
    {
        _sink?.Dispose();
        _sink = null;
    }
}
