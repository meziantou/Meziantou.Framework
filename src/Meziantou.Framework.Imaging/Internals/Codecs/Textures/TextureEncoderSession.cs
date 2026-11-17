namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The encoding state of one texture written by <c>Image.Save</c> or a writer (DDS, KTX 2.0): a 2D texture of one surface
/// and its generated mipmaps.
/// </summary>
/// <remarks>
/// <see cref="ValidateFrame"/> converts the frame to working samples and checks every texel, so that nothing is written
/// when a texel cannot be stored; the samples are kept for <see cref="BeginFrame"/>. Each <see cref="Encode"/> call then
/// encodes one row of blocks (or generates a level), without output, and once every level is encoded, writes the file
/// a part of at most <see cref="ImageOutputBuffer.FlushThreshold"/> bytes per call. The encoded levels stay in memory
/// until the file is written, since the containers give the size of every level before the first one.
/// </remarks>
internal abstract class TextureEncoderSession : ImageEncoderSession
{
    private readonly TextureTarget _target;
    private readonly bool _thorough;
    private readonly bool _generateMipmaps;
    private readonly bool _srgbFilter;
    private readonly bool _allowChannelReduction;
    private TexturePixels? _validated;
    private ImageFrame? _validatedFrame;
    private TextureSurfaceEncoder? _encoder;
    private EncodedTexture? _texture;
    private IDisposable? _partsOwner;
    private IReadOnlyList<ReadOnlyMemory<byte>>? _parts;
    private int _part;
    private int _partOffset;
    private Step _step;

    protected TextureEncoderSession(ImageEncoderSessionOptions options, TextureTarget target, bool thorough, bool generateMipmaps, bool srgbFilter, bool allowBitDepthReduction, bool allowChannelReduction, string formatName, string encoderName)
        : base(options)
    {
        FormatName = formatName;
        _target = target;
        _thorough = thorough;
        _generateMipmaps = generateMipmaps;
        _srgbFilter = srgbFilter;
        _allowChannelReduction = allowChannelReduction;
        EncoderName = encoderName;
        TextureValidation.EnsurePrecision(options.PixelFormat, target, allowBitDepthReduction, options.Encoder.Format, FormatName, encoderName);
    }

    private enum Step
    {
        None,
        Encoding,
        Writing,
    }

    /// <summary>Gets the name of the format of the texels, for messages.</summary>
    protected string FormatName { get; }

    protected string EncoderName { get; }

    public override void ValidateFrame(ImageFrame frame, bool isPoster)
    {
        _validated?.Dispose();
        _validated = null;
        _validatedFrame = null;
        var pixels = TexturePixels.Load(frame, Options.PixelFormat, _target.Precision, Options.Scope, CancellationToken);
        try
        {
            TextureValidation.Validate(pixels, _target, _allowChannelReduction, Options.Encoder.Format, FormatName, EncoderName, CancellationToken);
        }
        catch
        {
            pixels.Dispose();
            throw;
        }

        _validated = pixels;
        _validatedFrame = frame;
    }

    public override void BeginPosterFrame(ImageFrame frame) => throw new InvalidOperationException("A texture has no poster frame.");

    public override void BeginFrame(ImageFrame frame, int index)
    {
        if (index != 0)
            throw new InvalidOperationException("A texture written from an image stores a single surface.");

        TexturePixels pixels;
        if (_validated is not null && ReferenceEquals(_validatedFrame, frame))
        {
            pixels = _validated;
            _validated = null;
        }
        else
        {
            ValidateFrame(frame, isPoster: false);
            pixels = _validated!;
            _validated = null;
        }

        var levels = _generateMipmaps ? TextureMipmaps.GetLevelCount(pixels.Width, pixels.Height) : 1;
        _encoder = new TextureSurfaceEncoder(pixels, _target, levels, _thorough, _srgbFilter, Options.Scope);
        _texture = new EncodedTexture(_target, pixels.Width, pixels.Height, levels, layerCount: 1, faceCount: 1, isArray: false);
        _step = Step.Encoding;
    }

    public override void BeginComplete(int frameCount) => _step = Step.None;

    public override bool Encode(ImageOutputBuffer output)
    {
        switch (_step)
        {
            case Step.Encoding:
                if (!_encoder!.Step(CancellationToken))
                    return false;

                var levels = _encoder.TakeLevels();
                for (var level = 0; level < levels.Length; level++)
                {
                    _texture!.Set(0, 0, level, levels[level]);
                }

                _encoder.Dispose();
                _encoder = null;
                _parts = CreateParts(_texture!, out _partsOwner);
                _part = 0;
                _partOffset = 0;
                _step = Step.Writing;
                return false;

            case Step.Writing:
            {
                var written = 0;
                while (_part < _parts!.Count && written < output.FlushThreshold)
                {
                    var part = _parts[_part].Span[_partOffset..];
                    var count = Math.Min(part.Length, output.FlushThreshold - written);
                    output.Write(part[..count]);
                    written += count;
                    _partOffset += count;
                    if (_partOffset == _parts[_part].Length)
                    {
                        _part++;
                        _partOffset = 0;
                    }
                }

                if (_part < _parts.Count)
                    return false;

                _step = Step.None;
                return true;
            }

            case Step.None:
                return true;

            default:
                throw new InvalidOperationException("No texture operation was begun.");
        }
    }

    /// <summary>Creates the parts of the file, in order: the headers, then the texels of the surfaces.</summary>
    /// <param name="texture">The encoded surfaces.</param>
    /// <param name="owner">What the parts need alive until they are written, disposed with the session, or <see langword="null"/>.</param>
    protected abstract IReadOnlyList<ReadOnlyMemory<byte>> CreateParts(EncodedTexture texture, out IDisposable? owner);

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _validated?.Dispose();
            _validated = null;
            _validatedFrame = null;
            _encoder?.Dispose();
            _encoder = null;
            _partsOwner?.Dispose();
            _partsOwner = null;
            _texture?.Dispose();
            _texture = null;
            _parts = null;
        }

        base.Dispose(disposing);
    }
}
