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

    public void OnHeaderComplete(BmpStructureParser structure)
    {
        ArgumentNullException.ThrowIfNull(structure);
        _structure = structure;
        _sink = DecodedFrameSink.Create(_context, _request, ImageFormat.Bmp, structure.Size, structure.DefaultPixelFormat, structure.DefaultPixelFormat, iccProfile: null);
    }

    public void WriteRow(int y, ReadOnlySpan<byte> row)
    {
        var sink = _sink ?? throw new InvalidOperationException("The BMP header was not parsed.");
        if (!_frameStarted)
        {
            sink.BeginFrame(FrameDuration.Zero);
            _frameStarted = true;
        }

        sink.WriteRow(y, row);
    }

    public void OnEnd(BmpStructureParser structure)
    {
        ArgumentNullException.ThrowIfNull(structure);
        if (!_frameStarted)
            throw new InvalidOperationException("The BMP image has no row.");

        var sink = _sink!;
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
