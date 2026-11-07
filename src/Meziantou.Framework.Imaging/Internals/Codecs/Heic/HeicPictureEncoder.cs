namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The HEVC encoder of the library as the encoder of a stream of an HEIC file: the data of an item or of a sample are the
/// NAL units of a picture preceded by their length, as <see cref="HevcEncoder"/> writes them, and the parameter sets of
/// the stream are in its configuration record.
/// </summary>
/// <remarks>
/// A still picture of at least <see cref="RowBands.MinPixelsForParallelism"/> samples, with two rows and two columns of
/// coding tree blocks, is coded with rows of the wavefront (<see cref="HevcEncoderSettings.Wavefront"/>), which decoders
/// decode concurrently: up to 0.6% larger at the coarsest quantizers, and no larger from the default quality up. The tiles of a
/// grid are coded without it, since decoders decode them concurrently, and so are the pictures of a sequence.
/// </remarks>
internal sealed class HeicPictureEncoder : HeifPictureEncoder
{
    private static readonly AsyncLocal<bool> TestWavefrontValue = new();

    private readonly HevcEncoder _encoder;

    /// <summary>
    /// Gets or sets a value indicating whether every picture encoded on the current asynchronous flow is coded with rows of
    /// the wavefront (<see cref="HevcEncoderSettings.Wavefront"/>), whatever its size: the tests of the decoder write grids
    /// whose tiles have them.
    /// </summary>
    internal static bool TestWavefront
    {
        get => TestWavefrontValue.Value;
        set => TestWavefrontValue.Value = value;
    }

    public HeicPictureEncoder(AllocationScope scope, in HeifPictureSettings settings)
    {
        // The chroma samples of a 4:2:0 picture are where HeifRgbConverter computes them for this format: at the left of
        // their luma samples and between two rows, which is chroma_sample_loc_type 0 (ITU-T H.265, figure E.1)
        var hevcSettings = new HevcEncoderSettings(
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
            settings.KeyFrameInterval,
            OptimizeQuantization: settings.OptimizeQuantization);
        _encoder = new HevcEncoder(scope, hevcSettings with { Wavefront = TestWavefront || UsesWavefront(hevcSettings, settings.GridTile) });
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

    private static bool UsesWavefront(in HevcEncoderSettings settings, bool gridTile)
    {
        if (!settings.StillPicture || gridTile || (long)settings.Width * settings.Height < RowBands.MinPixelsForParallelism)
            return false;

        var ctbSize = 1 << HevcEncoderDecisions.Create(settings).CtbLog2Size;
        return settings.Width > ctbSize && settings.Height > ctbSize;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            _encoder.Dispose();
    }
}
