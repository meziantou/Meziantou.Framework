namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The TGA pixel sink, shared by eager loads and sequential readers:
/// <see cref="TgaStructureParser"/> expands every packet into one row and this observer writes it to a
/// <see cref="DecodedFrameSink"/> at its displayed position, in the source layout of the header
/// (<see cref="PixelFormat.Gray8"/>, <see cref="PixelFormat.Rgb24"/> or <see cref="PixelFormat.Rgba32"/>).
/// </summary>
/// <remarks>
/// Call order: <see cref="OnHeaderComplete"/> once, <see cref="WriteRow"/> for every row, then <see cref="OnEnd"/> once the
/// trailer is validated, so that readers and eager loads see the same complete, validated image.
/// </remarks>
internal sealed class TgaDecoder : IImageDecodeObserver
{
    private readonly ImageDecodeRequest _request;
    private readonly ImageCodecContext _context;
    private TgaStructureParser? _structure;
    private DecodedFrameSink? _sink;
    private bool _frameStarted;

    public TgaDecoder(ImageDecodeRequest request, ImageCodecContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);
        _request = request;
        _context = context;
    }

    public void OnHeaderComplete(TgaStructureParser structure)
    {
        ArgumentNullException.ThrowIfNull(structure);
        _structure = structure;
        _sink = DecodedFrameSink.Create(_context, _request, ImageFormat.Tga, structure.Size, structure.SourcePixelFormat, structure.SourcePixelFormat, iccProfile: null);
    }

    public void WriteRow(int y, ReadOnlySpan<byte> row)
    {
        var sink = _sink ?? throw new InvalidOperationException("The TGA header was not parsed.");
        if (!_frameStarted)
        {
            sink.BeginFrame(FrameDuration.Zero);
            _frameStarted = true;
        }

        sink.WriteRow(y, row);
    }

    public void OnEnd(TgaStructureParser structure)
    {
        ArgumentNullException.ThrowIfNull(structure);
        if (!_frameStarted)
            throw new InvalidOperationException("The TGA image has no row.");

        var sink = _sink!;
        sink.UpdateMetadata(structure.Metadata);
        sink.EndImage();
    }

    public Image GetResult()
    {
        var structure = _structure ?? throw new InvalidOperationException("The TGA data was not decoded.");
        return _sink!.Build(structure.Metadata, animation: null);
    }

    public void Dispose()
    {
        _sink?.Dispose();
        _sink = null;
    }
}
