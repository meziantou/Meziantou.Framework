using System.Buffers.Binary;
using Meziantou.Framework.Imaging.Metadata;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// Incremental GIF87a/GIF89a block walker (GIF89a specification): header, logical screen descriptor, color tables, image
/// descriptors, extensions and bounded data sub-blocks (at most 255 bytes each are buffered). It never decodes LZW data.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item><description>
/// Header walk: stops before the first image descriptor. The frame count is never guessed (<see langword="null"/>); the
/// file is known to be animated only when a NETSCAPE2.0/ANIMEXTS1.0 loop extension precedes the first image.
/// </description></item>
/// <item><description>Full scan: walks to the trailer, charges every image to <see cref="ImageResourceLimits.MaxFrames"/>, and counts the frames.</description></item>
/// <item><description>Decode: like a full scan, but image data goes to a <see cref="GifDecodeObserver"/>, which charges frames itself and may stop early.</description></item>
/// </list>
/// A GIF is animated when it has several images or a loop extension. Plain-text extensions (which require text rendering)
/// are <see cref="UnsupportedImageFeatureException"/>; other unknown extensions and application extensions are skipped.
/// An empty logical screen, an unknown block, an invalid Graphic Control Extension or LZW code size, a missing image, and
/// truncation (including a missing trailer) are <see cref="InvalidImageContentException"/>. Comment extensions become
/// <c>Comment</c> text entries (Latin-1).
/// </remarks>
internal sealed class GifStructureParser : ImageParser<ImageInfo>
{
    private const int HeaderLength = 13;

    private readonly ImageCodecContext _context;
    private readonly StructureWalk _walk;
    private readonly GifDecodeObserver? _observer;
    private readonly DecodedMetadataBuilder _metadata;

    private State _state;
    private BlockKind _block;
    private int _subBlockIndex;
    private List<byte[]>? _comment;
    private bool _loopExtensionPending;
    private GifGraphicControl? _pendingControl;
    private bool _headerReported;
    private int _imageCount;
    private int _lastDisposal;
    private bool _transparencyPossible;
    private bool _anyTransparentIndex;
    private ImageInfo? _result;

    public GifStructureParser(ImageCodecContext context, StructureWalk walk, GifDecodeObserver? observer = null)
        : base(ImageFormat.Gif)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (walk == StructureWalk.Decode)
        {
            ArgumentNullException.ThrowIfNull(observer);
        }

        _context = context;
        _walk = walk;
        _observer = walk == StructureWalk.Decode ? observer : null;
        _metadata = new DecodedMetadataBuilder(context.Tracker, ImageFormat.Gif);
    }

    private enum State
    {
        Header,
        GlobalColorTable,
        Block,
        SubBlock,
        Done,
    }

    private enum BlockKind
    {
        GraphicControl,
        Comment,
        Application,
        LoopApplication,
        Unknown,
        ImageData,
    }

    /// <summary>Gets the version: <c>"87a"</c> or <c>"89a"</c>.</summary>
    public string Version { get; private set; } = "";

    public int Width { get; private set; }

    public int Height { get; private set; }

    public Size Size => new(Width, Height);

    /// <summary>Gets the global color table (RGB triplets), or empty.</summary>
    public ReadOnlyMemory<byte> GlobalColorTable { get; private set; }

    /// <summary>Gets the background color index of the logical screen descriptor.</summary>
    public byte BackgroundColorIndex { get; private set; }

    /// <summary>
    /// Gets the raw NETSCAPE2.0/ANIMEXTS1.0 loop count of the last loop extension traversed so far, or <see langword="null"/>
    /// when the file has no loop extension (so far).
    /// </summary>
    public ushort? LoopCount { get; private set; }

    /// <summary>Gets the number of images traversed so far.</summary>
    public int ImageCount => _imageCount;

    /// <summary>Gets the metadata collected so far.</summary>
    public ImageMetadata Metadata => _metadata.Metadata;

    /// <summary>Gets the animation settings for the images traversed so far, or <see langword="null"/> for a still image.</summary>
    public AnimationMetadata? Animation => _imageCount > 1 || LoopCount is not null ? new AnimationMetadata { TotalPlays = AnimationTiming.FromGifLoopCount(LoopCount) } : null;

    public override ParseStatus Parse(ReadOnlySpan<byte> buffer, bool isEndOfInput, out int consumed)
    {
        consumed = 0;
        while (true)
        {
            var remaining = buffer[consumed..];
            switch (_state)
            {
                case State.Header:
                    if (remaining.Length < HeaderLength)
                        return ParseStatus.NeedMoreData(HeaderLength);

                    ProcessHeader(remaining[..HeaderLength]);
                    consumed += HeaderLength;
                    break;

                case State.GlobalColorTable:
                {
                    var length = GlobalColorTable.Length;
                    if (remaining.Length < length)
                        return ParseStatus.NeedMoreData(length);

                    GlobalColorTable = remaining[..length].ToArray();
                    consumed += length;
                    _state = State.Block;
                    break;
                }

                case State.Block:
                {
                    if (remaining.IsEmpty)
                        return ParseStatus.NeedMoreData(1);

                    switch (remaining[0])
                    {
                        case 0x21:
                            if (remaining.Length < 2)
                                return ParseStatus.NeedMoreData(2);

                            StartExtension(remaining[1]);
                            consumed += 2;
                            break;

                        case 0x2C:
                        {
                            if (!_headerReported)
                            {
                                _headerReported = true;
                                if (_walk == StructureWalk.Header)
                                {
                                    _result = CreateInfo(ImageIdentifyMode.Header);
                                    _state = State.Done;
                                    return ParseStatus.Complete;
                                }

                                // Sequential readers: the header snapshot is their Info; yield before the first image
                                // descriptor, which the next call processes (the header is not reported twice)
                                _context.Sequential?.OnHeader(CreateInfo(ImageIdentifyMode.Header));
                                _observer?.OnHeaderComplete(this);
                                if (TryYield())
                                    return ParseStatus.Complete;
                            }

                            if (remaining.Length < 10)
                                return ParseStatus.NeedMoreData(10);

                            var packed = remaining[9];
                            var localEntries = (packed & 0x80) != 0 ? 2 << (packed & 0x07) : 0;
                            var total = 10 + (localEntries * 3) + 1;
                            if (remaining.Length < total)
                                return ParseStatus.NeedMoreData(total);

                            ProcessImageDescriptor(remaining[..total], localEntries);
                            consumed += total;
                            break;
                        }

                        case 0x3B:
                            consumed++;
                            OnTrailer();
                            return ParseStatus.Complete;

                        default:
                            throw Invalid(string.Create(CultureInfo.InvariantCulture, $"The GIF block introducer 0x{remaining[0]:X2} is not valid."));
                    }

                    break;
                }

                case State.SubBlock:
                {
                    if (remaining.IsEmpty)
                        return ParseStatus.NeedMoreData(1);

                    var size = remaining[0];
                    if (size == 0)
                    {
                        consumed++;
                        _state = State.Block;
                        if (!EndBlock())
                        {
                            _state = State.Done;
                            return ParseStatus.Complete;
                        }

                        if (TryYield())
                            return ParseStatus.Complete;

                        break;
                    }

                    if (remaining.Length < size + 1)
                        return ParseStatus.NeedMoreData(size + 1);

                    ProcessSubBlock(remaining.Slice(1, size));
                    consumed += size + 1;
                    _subBlockIndex++;
                    break;
                }

                default:
                    return ParseStatus.Complete;
            }
        }
    }

    public override ImageInfo GetResult() => _result ?? throw new InvalidOperationException("The GIF structure was not parsed.");

    private bool TryYield() => _walk == StructureWalk.Decode && _context.TryConsumeYield();

    private void ProcessHeader(ReadOnlySpan<byte> header)
    {
        if (!GifCodec.MatchesSignature(header))
            throw Invalid("The GIF signature is invalid.");

        Version = header[3..6].SequenceEqual("87a"u8) ? "87a" : "89a";
        var width = BinaryPrimitives.ReadUInt16LittleEndian(header[6..]);
        var height = BinaryPrimitives.ReadUInt16LittleEndian(header[8..]);
        if (width == 0 || height == 0)
            throw Invalid(string.Create(CultureInfo.InvariantCulture, $"The GIF logical screen {width}x{height} is empty."));

        Width = width;
        Height = height;
        BackgroundColorIndex = header[11];
        _context.Limits.EnsureCanvasWithinLimits(Width, Height);
        var packed = header[10];
        if ((packed & 0x80) != 0)
        {
            // The table length is recorded now; its bytes are read by the next state
            GlobalColorTable = new byte[(2 << (packed & 0x07)) * 3];
            _state = State.GlobalColorTable;
        }
        else
        {
            _state = State.Block;
        }
    }

    private void StartExtension(byte label)
    {
        _subBlockIndex = 0;
        _block = label switch
        {
            0xF9 => BlockKind.GraphicControl,
            0xFE => BlockKind.Comment,
            0xFF => BlockKind.Application,
            0x01 => throw new UnsupportedImageFeatureException("GIF plain text extensions require text rendering and are not supported.", ImageFormat.Gif, "GIF plain text extension"),
            _ => BlockKind.Unknown,
        };

        _state = State.SubBlock;
    }

    private void ProcessImageDescriptor(ReadOnlySpan<byte> data, int localEntries)
    {
        var descriptor = new GifImageDescriptor(
            BinaryPrimitives.ReadUInt16LittleEndian(data[1..]),
            BinaryPrimitives.ReadUInt16LittleEndian(data[3..]),
            BinaryPrimitives.ReadUInt16LittleEndian(data[5..]),
            BinaryPrimitives.ReadUInt16LittleEndian(data[7..]),
            (data[9] & 0x40) != 0,
            localEntries);
        var codeSize = data[^1];
        if (codeSize is < 1 or > 11)
            throw Invalid(string.Create(CultureInfo.InvariantCulture, $"The GIF LZW minimum code size {codeSize} is invalid."));

        if (_walk == StructureWalk.FullScan)
        {
            _context.Tracker.ChargeScannedFrame();
        }

        // Transparency capability (not a pixel scan): a transparent index, a region leaving the initial (transparent)
        // canvas visible, or a disposal of a non-final image that restores transparency
        var control = _pendingControl;
        if (_imageCount > 0 && (_lastDisposal == GifGraphicControl.DisposeRestoreBackground || (_imageCount == 1 && _lastDisposal == GifGraphicControl.DisposeRestorePrevious)))
        {
            _transparencyPossible = true;
        }

        if (!descriptor.CoversCanvas(Width, Height))
        {
            _transparencyPossible = true;
        }

        if (control?.HasTransparency == true)
        {
            _anyTransparentIndex = true;
        }

        _lastDisposal = control?.EffectiveDisposalMethod ?? GifGraphicControl.DisposeNotSpecified;
        _imageCount++;
        _pendingControl = null;
        _observer?.OnImageStart(descriptor, data.Slice(10, localEntries * 3), control, codeSize);
        _block = BlockKind.ImageData;
        _subBlockIndex = 0;
        _state = State.SubBlock;
    }

    private void ProcessSubBlock(ReadOnlySpan<byte> data)
    {
        switch (_block)
        {
            case BlockKind.ImageData:
                _observer?.OnImageData(data);
                break;

            case BlockKind.GraphicControl when _subBlockIndex == 0:
                if (data.Length != 4)
                    throw Invalid("The GIF Graphic Control Extension must have a 4-byte block.");

                _pendingControl = new GifGraphicControl((data[0] >> 2) & 0x07, (data[0] & 0x01) != 0, BinaryPrimitives.ReadUInt16LittleEndian(data[1..]), data[3]);
                break;

            case BlockKind.Comment:
                _metadata.Charge(data.Length);
                (_comment ??= []).Add(data.ToArray());
                break;

            case BlockKind.Application when _subBlockIndex == 0:
                _loopExtensionPending = false;
                if (data.Length == 11 && (data.SequenceEqual("NETSCAPE2.0"u8) || data.SequenceEqual("ANIMEXTS1.0"u8)))
                {
                    _block = BlockKind.LoopApplication;
                    _loopExtensionPending = true;
                }

                break;

            case BlockKind.LoopApplication when _loopExtensionPending && data.Length >= 3 && data[0] == 1:
                _loopExtensionPending = false;

                // The last loop extension wins, as in FFmpeg and Apple ImageIO
                LoopCount = BinaryPrimitives.ReadUInt16LittleEndian(data[1..]);
                break;
        }
    }

    private bool EndBlock()
    {
        switch (_block)
        {
            case BlockKind.ImageData:
                return _observer?.OnImageEnd() ?? true;

            case BlockKind.GraphicControl when _subBlockIndex == 0:
                throw Invalid("The GIF Graphic Control Extension has no data block.");

            case BlockKind.Comment:
                if (_comment is not null)
                {
                    var length = _comment.Sum(part => part.Length);
                    var bytes = new byte[length];
                    var offset = 0;
                    foreach (var part in _comment)
                    {
                        part.CopyTo(bytes, offset);
                        offset += part.Length;
                    }

                    _comment = null;
                    _metadata.AddText(ImageTextEntry.CommentKeyword, Encoding.Latin1.GetString(bytes));
                }
                else
                {
                    _metadata.AddText(ImageTextEntry.CommentKeyword, "");
                }

                return true;

            default:
                return true;
        }
    }

    private void OnTrailer()
    {
        if (_imageCount == 0)
            throw Invalid("The GIF file contains no image.");

        _state = State.Done;
        _result = CreateInfo(ImageIdentifyMode.FullScan);
        _observer?.OnEnd(this);
    }

    private ImageInfo CreateInfo(ImageIdentifyMode mode)
    {
        int? frameCount = null;
        bool? isAnimated = null;
        bool? mayHaveTransparency = null;
        var animation = LoopCount is null ? null : new AnimationMetadata { TotalPlays = AnimationTiming.FromGifLoopCount(LoopCount) };
        if (mode == ImageIdentifyMode.Header)
        {
            // Only what precedes the first image is known: never guess the frame count
            if (LoopCount is not null)
            {
                isAnimated = true;
            }

            if (_pendingControl?.HasTransparency == true)
            {
                mayHaveTransparency = true;
            }
        }
        else
        {
            frameCount = _imageCount;
            isAnimated = _imageCount > 1 || LoopCount is not null;
            animation = Animation;
            mayHaveTransparency = _anyTransparentIndex || _transparencyPossible;
        }

        return new ImageInfo(
            ImageFormat.Gif,
            Size,
            DefaultPixelFormats.Gif,
            ImageColorModel.Indexed,
            bitsPerComponent: 8,
            frameCount,
            isAnimated,
            hasPosterFrame: false,
            mayHaveTransparency,
            animation,
            _metadata.Metadata,
            mode);
    }

    private static InvalidImageContentException Invalid(string message) => new(message, ImageFormat.Gif);
}
