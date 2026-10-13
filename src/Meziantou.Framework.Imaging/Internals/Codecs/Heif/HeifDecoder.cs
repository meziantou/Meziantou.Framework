namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The decode observer of a file of the HEIF family whose format is only known once its file type box is read: creates the
/// pixel decoder of the format that the walker resolved when the image is described, then hands it everything.
/// </summary>
internal sealed class HeifDecoder : HeifDecodeObserver
{
    private readonly ImageDecodeRequest _request;
    private readonly ImageCodecContext _context;
    private HeifDecodeObserver? _decoder;

    public HeifDecoder(ImageDecodeRequest request, ImageCodecContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);
        _request = request;
        _context = context;
    }

    public override void OnHeaderComplete(HeifStructureParser structure)
    {
        ArgumentNullException.ThrowIfNull(structure);
        _decoder = structure.Binding.CreateDecodeObserver(_request, _context);
        _decoder.OnHeaderComplete(structure);
    }

    public override void OnImageData(HeifStructureParser structure) => Decoder.OnImageData(structure);

    public override void OnSequenceSample(HeifStructureParser structure, WebPPayloadBuffer color, WebPPayloadBuffer? alpha) => Decoder.OnSequenceSample(structure, color, alpha);

    public override void OnSequenceEnd(HeifStructureParser structure) => Decoder.OnSequenceEnd(structure);

    public override bool TryWriteSequenceFrame(HeifStructureParser structure, FrameDuration duration, out bool proceed) => Decoder.TryWriteSequenceFrame(structure, duration, out proceed);

    public override Image GetResult() => Decoder.GetResult();

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _decoder?.Dispose();
            _decoder = null;
        }

        base.Dispose(disposing);
    }

    private HeifDecodeObserver Decoder => _decoder ?? throw new InvalidOperationException("The file was not decoded.");
}
