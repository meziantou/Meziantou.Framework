using Meziantou.Framework.Imaging.Formats;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The session of an encoder with a gain map (<see cref="JpegEncoder.GainMap"/>, <see cref="AvifEncoder.GainMap"/>): the
/// session of the format writes the primary image into a private buffer, the gain map image is written with the same
/// settings into another one, and the assembler of the format writes the file from both once the primary image is complete.
/// </summary>
/// <remarks>
/// Nothing reaches the output before the file is complete, so a failure leaves nothing written. The gain map image is
/// encoded first, when the session is created: its errors come before any work on the primary image.
/// </remarks>
internal sealed class GainMapEncoderSession : ImageEncoderSession
{
    private readonly ImageEncoderSession _primary;
    private readonly ImageOutputBuffer _scratch;
    private readonly PooledPayloadWriter _encoded;
    private readonly byte[] _gainMap;
    private readonly Action<ImageOutputBuffer, ReadOnlyMemory<byte>, byte[]> _assemble;
    private bool _completing;

    private GainMapEncoderSession(ImageEncoderSessionOptions options, ImageEncoderSession primary, byte[] gainMap, Action<ImageOutputBuffer, ReadOnlyMemory<byte>, byte[]> assemble)
        : base(options)
    {
        _primary = primary;
        _gainMap = gainMap;
        _assemble = assemble;
        _scratch = new ImageOutputBuffer(options.Scope);
        _encoded = new PooledPayloadWriter(options.Scope);
    }

    /// <summary>Creates the session of an encoder with a gain map.</summary>
    /// <param name="options">The options of the session.</param>
    /// <param name="gainMap">The gain map.</param>
    /// <param name="gainMapMetadataHandling">The metadata policy of the gain map image.</param>
    /// <param name="createPrimarySession">Creates the session of the format.</param>
    /// <param name="assemble">Writes the file from the primary image and the gain map image.</param>
    /// <returns>The session.</returns>
    /// <exception cref="UnsupportedImageFeatureException">The image or the gain map image is not a still image.</exception>
    /// <exception cref="ObjectDisposedException">The gain map image is disposed.</exception>
    public static GainMapEncoderSession Create(ImageEncoderSessionOptions options, GainMap gainMap, MetadataHandling gainMapMetadataHandling, Func<ImageEncoderSessionOptions, ImageEncoderSession> createPrimarySession, Action<ImageOutputBuffer, ReadOnlyMemory<byte>, byte[]> assemble)
    {
        var format = options.Encoder.Format;
        if (options.ExpectedFrameCount is not (null or 1) || options.Animation is not null)
            throw new UnsupportedImageFeatureException($"A gain map is written next to a still image; this {format.ToString().ToUpperInvariant()} output has several images.", format, "Gain map with an animation");

        var image = gainMap.Image;
        if (image.Frames.Count is not 1 || image.PosterFrame is not null)
            throw new UnsupportedImageFeatureException("The gain map image must be a still image.", format, "Animated gain map");

        using var stream = new MemoryStream();
        image.Save(stream, options.Encoder.CloneForGainMapImage(gainMapMetadataHandling));
        var session = createPrimarySession(options);
        try
        {
            return new GainMapEncoderSession(options, session, stream.ToArray(), assemble);
        }
        catch
        {
            session.Dispose();
            throw;
        }
    }

    public override void ValidateFrame(ImageFrame frame, bool isPoster) => _primary.ValidateFrame(frame, isPoster);

    public override void PlanFrames(ImageFrame? poster, IReadOnlyList<ImageFrame> frames) => _primary.PlanFrames(poster, frames);

    public override void BeginPosterFrame(ImageFrame frame) => _primary.BeginPosterFrame(frame);

    public override void BeginFrame(ImageFrame frame, int index) => _primary.BeginFrame(frame, index);

    public override void BeginComplete(int frameCount)
    {
        _primary.BeginComplete(frameCount);
        _completing = true;
    }

    public override bool Encode(ImageOutputBuffer output)
    {
        ArgumentNullException.ThrowIfNull(output);
        _primary.CancellationToken = CancellationToken;
        var done = _primary.Encode(_scratch);
        if (_scratch.Patches is not null)
            throw new InvalidOperationException("A session that patches its output cannot write a gain map.");

        _encoded.Write(_scratch.WrittenMemory.Span);
        _scratch.Clear();
        if (done && _completing)
        {
            _assemble(output, _encoded.WrittenMemory, _gainMap);
        }

        return done;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _primary.Dispose();
            _scratch.Dispose();
            _encoded.Dispose();
        }

        base.Dispose(disposing);
    }
}
