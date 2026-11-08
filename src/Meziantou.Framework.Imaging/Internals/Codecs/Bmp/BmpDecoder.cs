namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The BMP pixel sink, shared by eager loads and sequential readers:
/// <see cref="BmpStructureParser"/> expands each stored row and this observer writes it to a <see cref="DecodedFrameSink"/>
/// at its displayed position, in the source layout of the DIB (<see cref="PixelFormat.Rgb24"/>, or
/// <see cref="PixelFormat.Rgba32"/> when the layout defines a real alpha channel).
/// </summary>
/// <remarks>
/// Call order: <see cref="OnHeaderComplete"/> once, <see cref="WriteRow"/> for every row (bottom-up files write the last
/// displayed row first), then <see cref="OnEnd"/> once every row has been read.
/// </remarks>
internal sealed class BmpDecoder : IImageDecodeObserver
{
    private readonly ImageDecodeRequest _request;
    private readonly ImageCodecContext _context;
    private BmpStructureParser? _structure;
    private DecodedFrameSink? _sink;
    private bool _frameStarted;

    public BmpDecoder(ImageDecodeRequest request, ImageCodecContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);
        _request = request;
        _context = context;
    }

    /// <summary>
    /// Gets a value indicating whether the 24-bit pixels are expanded to opaque <see cref="PixelFormat.Rgba32"/> rows (requested
    /// so: no conversion) rather than to <see cref="BmpDibLayout.SourcePixelFormat"/>.
    /// </summary>
    public bool OpaqueRgbaRows { get; private set; }

    public void OnHeaderComplete(BmpStructureParser structure)
    {
        ArgumentNullException.ThrowIfNull(structure);
        _structure = structure;
        OpaqueRgbaRows = structure.Layout.Header.BitsPerPixel == 24 && _request.PixelFormat == PixelFormat.Rgba32;
        _sink = DecodedFrameSink.Create(_context, _request, ImageFormat.Bmp, structure.Size, structure.DefaultPixelFormat, OpaqueRgbaRows ? PixelFormat.Rgba32 : structure.DefaultPixelFormat, iccProfile: null);
    }

    /// <summary>Starts the frame on its first row and leases it; dispose the lease before returning from the parser call.</summary>
    public PixelLease LeaseRows()
    {
        var sink = _sink ?? throw new InvalidOperationException("The BMP header was not parsed.");
        if (!_frameStarted)
        {
            sink.BeginFrame(FrameDuration.Zero);
            _frameStarted = true;
        }

        return sink.LeaseCurrentFrame();
    }

    /// <summary>Writes one row through a lease from <see cref="LeaseRows"/>.</summary>
    public void WriteRow(scoped in PixelLease lease, int y, ReadOnlySpan<byte> row) => _sink!.WriteRow(lease, y, row);

    /// <summary>Gets the frame row to expand a row into through a lease from <see cref="LeaseRows"/>, or an empty span when rows are converted (<see cref="DecodedFrameSink.GetDirectRow"/>).</summary>
    public Span<byte> GetDirectRow(scoped in PixelLease lease, int y) => _sink!.GetDirectRow(lease, y);

    public void OnEnd(BmpStructureParser structure)
    {
        ArgumentNullException.ThrowIfNull(structure);
        if (!_frameStarted)
            throw new InvalidOperationException("The BMP image has no row.");

        var sink = _sink!;

        // The profile is read after the sink was created: it may follow the pixel data
        if (structure.Metadata.IccProfile is { } profile)
        {
            sink.AdoptColorProfile(profile);
        }

        sink.UpdateMetadata(structure.Metadata);
        sink.EndImage();
    }

    public Image GetResult()
    {
        var structure = _structure ?? throw new InvalidOperationException("The BMP data was not decoded.");
        return _sink!.Build(structure.Metadata, animation: null);
    }

    public void Dispose()
    {
        _sink?.Dispose();
        _sink = null;
    }
}
