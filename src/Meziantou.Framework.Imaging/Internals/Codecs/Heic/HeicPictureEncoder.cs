namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The HEVC encoder of the library as the encoder of a stream of an HEIC file: the data of an item or of a sample are the
/// NAL units of a picture preceded by their length, as <see cref="HevcEncoder"/> writes them, and the parameter sets of
/// the stream are in its configuration record.
/// </summary>
internal sealed class HeicPictureEncoder : HeifPictureEncoder
{
    private readonly HevcEncoder _encoder;

    public HeicPictureEncoder(AllocationScope scope, in HeifPictureSettings settings)
    {
        // The chroma samples of a 4:2:0 picture are where HeifRgbConverter computes them for this format: at the left of
        // their luma samples and between two rows, which is chroma_sample_loc_type 0 (ITU-T H.265, figure E.1)
        _encoder = new HevcEncoder(scope, new HevcEncoderSettings(
            settings.Width,
            settings.Height,
            settings.BitDepth,
            ChromaFormatIdc: settings.Monochrome ? 0 : settings.SubsamplingY is 1 ? 1 : settings.SubsamplingX is 1 ? 2 : 3,
            settings.ColorPrimaries,
            settings.TransferCharacteristics,
            settings.MatrixCoefficients,
            settings.FullRange,
            ChromaSampleLocType: 0,
            settings.StillPicture,
            settings.Lossless,
            settings.Quality,
            settings.Effort,
            settings.KeyFrameInterval));
        try
        {
            Description = new HeicStreamDescription(scope, _encoder.Parameters, _encoder.VideoParameterSet.Span, _encoder.SequenceParameterSet.Span, _encoder.PictureParameterSet.Span);
        }
        catch
        {
            _encoder.Dispose();
            throw;
        }
    }

    public override HeifStreamDescription Description { get; }

    public override ReadOnlyMemory<byte> Payload => _encoder.Payload;

    public override bool IsKeyFrame => _encoder.IsKeyFrame;

    public override void Reset() => _encoder.Reset();

    public override Av1Plane GetSourcePlane(int plane) => _encoder.GetSourcePlane(plane);

    public override void BeginFrame() => _encoder.BeginFrame();

    public override bool Encode(CancellationToken cancellationToken) => _encoder.Encode(cancellationToken);

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            _encoder.Dispose();
    }
}
