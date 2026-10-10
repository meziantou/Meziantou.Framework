namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The Netpbm pixel sink, shared by eager loads and sequential readers:
/// <see cref="PnmStructureParser"/> expands each row and this observer writes it to a <see cref="DecodedFrameSink"/> in the
/// decoded representation of the header.
/// </summary>
internal sealed class PnmDecoder : IImageDecodeObserver
{
    private readonly ImageDecodeRequest _request;
    private readonly ImageCodecContext _context;
    private PnmStructureParser? _structure;
    private DecodedFrameSink? _sink;
    private bool _frameStarted;

    public PnmDecoder(ImageDecodeRequest request, ImageCodecContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);
        _request = request;
        _context = context;
    }

    public void OnHeaderComplete(PnmStructureParser structure)
    {
        ArgumentNullException.ThrowIfNull(structure);
        _structure = structure;
        _sink = DecodedFrameSink.Create(_context, _request, ImageFormat.Pnm, structure.Size, structure.SourcePixelFormat, structure.SourcePixelFormat, iccProfile: null);
    }

    public void WriteRow(int y, ReadOnlySpan<byte> row)
    {
        var sink = _sink ?? throw new InvalidOperationException("The PNM header was not parsed.");
        if (!_frameStarted)
        {
            sink.BeginFrame(FrameDuration.Zero);
            _frameStarted = true;
        }

        sink.WriteRow(y, row);
    }

    public void OnEnd(PnmStructureParser structure)
    {
        ArgumentNullException.ThrowIfNull(structure);
        if (!_frameStarted)
            throw new InvalidOperationException("The PNM image has no row.");

        var sink = _sink!;
        sink.UpdateMetadata(structure.Metadata);
        sink.EndImage();
    }

    public Image GetResult()
    {
        var structure = _structure ?? throw new InvalidOperationException("The PNM data was not decoded.");
        return _sink!.Build(structure.Metadata, animation: null);
    }

    public void Dispose()
    {
        _sink?.Dispose();
        _sink = null;
    }
}
