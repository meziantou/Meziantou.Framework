namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The AV1 encoder of the library as the encoder of a stream of an AVIF file: the data of an item or of a sample are the
/// units (OBUs) of a frame, as <see cref="Av1Encoder"/> writes them.
/// </summary>
internal sealed class AvifPictureEncoder : HeifPictureEncoder
{
    private readonly Av1Encoder _encoder;

    public AvifPictureEncoder(AllocationScope scope, in HeifPictureSettings settings)
    {
        _encoder = new Av1Encoder(scope, new Av1EncoderSettings(
            settings.Width,
            settings.Height,
            settings.BitDepth,
            settings.Monochrome,
            settings.SubsamplingX,
            settings.SubsamplingY,
            settings.ColorPrimaries,
            settings.TransferCharacteristics,
            settings.MatrixCoefficients,
            settings.FullRange,
            ChromaSamplePosition: 0,
            settings.StillPicture,
            settings.Lossless,
            settings.Quality,
            settings.Effort,
            settings.KeyFrameInterval));
        Description = new AvifStreamDescription(_encoder.Sequence);
    }

    public override HeifStreamDescription Description { get; }

    public override ReadOnlyMemory<byte> Payload => _encoder.Payload;

    public override bool IsKeyFrame => _encoder.IsKeyFrame;

    public override Av1Plane GetSourcePlane(int plane) => _encoder.GetSourcePlane(plane);

    public override void BeginFrame() => _encoder.BeginFrame();

    public override void Reset() => _encoder.Reset();

    public override bool Encode(CancellationToken cancellationToken) => _encoder.Encode(cancellationToken);

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            _encoder.Dispose();
    }
}
