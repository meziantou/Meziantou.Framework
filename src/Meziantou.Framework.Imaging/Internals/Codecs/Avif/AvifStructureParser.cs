using System.Buffers.Binary;
using Meziantou.Framework.Imaging.Metadata;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// Walks the top-level boxes of an AVIF file, forward only (ISO/IEC 14496-12 and ISO/IEC 23008-12): validates the file type,
/// resolves the image from the <c>meta</c> box, or the image sequence from the <c>moov</c> box of a file with the brand of
/// the sequences, then captures the data of the items or of the samples as their bytes go by. The same walk identifies a file
/// (header or full scan) and feeds the pixel decoder.
/// </summary>
/// <remarks>
/// <para>
/// The data of the items and of the samples is located by offsets in the file. When the box that locates them comes first,
/// which is the usual layout, only the bytes that are needed are kept: the items of the image, or the samples of the images
/// of a sequence that are not decoded yet. Media data that precede this box are kept until it is parsed, because what they
/// hold is not known yet; a header identification keeps nothing.
/// </para>
/// <para>
/// The walk ends after the last byte of the last needed item or sample: the boxes that follow are not read. An image sequence
/// takes its color description from its track, and its Exif and XMP items from the <c>meta</c> box of the track, or else
/// from the <c>meta</c> box of the file when it precedes the <c>moov</c> box.
/// </para>
/// </remarks>
internal sealed class AvifStructureParser : ImageParser<ImageInfo>
{
    private const int BoxHeaderLength = 8;
    private const int LargeBoxHeaderLength = 16;
    private const int MaxFileTypeLength = 64 * 1024;

    // The number of bytes of the first coded item, or of the first sample, that a header walk reads to find the sequence
    // header of the bitstream: the largest sequence header has less than 300 bytes
    private const int HeaderProbeLength = 1024;

    private readonly ImageCodecContext _context;
    private readonly StructureWalk _walk;
    private readonly AvifDecodeObserver? _observer;
    private readonly DecodedMetadataBuilder _metadata;
    private readonly List<Capture> _captures = [];
    private readonly List<(long Offset, WebPPayloadBuffer Data)> _retained = [];
    private State _state;
    private long _position;
    private long _boxRemaining;
    private bool _seenFileType;
    private WebPPayloadBuffer? _meta;
    private bool _isRetaining;
    private AvifImagePlan? _plan;
    private bool _isSequence;
    private WebPPayloadBuffer? _movieData;
    private AvifMovieBox? _movie;
    private CicpColorDescription? _nclx;
    private int? _bitstreamMatrix;
    private int _nextFrame;

    // A movie fragment of a fragmented image sequence: the payload of its 'moof' box and the offset of the box, the
    // encoded bytes of the samples that were scheduled, and whether the input ended on a box boundary (a fragmented
    // sequence ends with the file, since nothing tells how many fragments it has)
    private WebPPayloadBuffer? _fragmentData;
    private long _fragmentOffset;
    private long _scheduledBytes;
    private bool _isAtEndOfInput;

    // The images that were shown so far, and how the next image of the track is shown (read once: the timeline is sequential)
    private long _shownFrames;
    private bool _hasFrameTiming;
    private bool _isFrameShown;
    private FrameDuration _frameDuration;
    private bool _isProbingHeader;
    private AvifItem? _sequenceExif;
    private AvifItem? _sequenceXmp;
    private bool _hasPendingSequenceMetadata;
    private ImageInfo? _result;
    private bool _returnNow;

    public AvifStructureParser(ImageCodecContext context, StructureWalk walk, AvifDecodeObserver? observer = null)
        : base(ImageFormat.Avif)
    {
        ArgumentNullException.ThrowIfNull(context);
        _context = context;
        _walk = walk;
        _observer = observer;
        _metadata = new DecodedMetadataBuilder(context.Tracker, ImageFormat.Avif);
    }

    private enum State
    {
        BoxHeader,
        Meta,
        Movie,
        Fragment,
        Media,
        Done,
    }

    /// <summary>Gets the image of the file; available once the <c>meta</c> box of a file that is not an image sequence is parsed.</summary>
    public AvifImagePlan Plan => _plan ?? throw new InvalidOperationException("The AVIF 'meta' box was not parsed.");

    /// <summary>Gets the image sequence of the file, or <see langword="null"/> when the file presents its primary item.</summary>
    public AvifMovieBox? Movie => _movie;

    /// <summary>Gets the size of the images that the file presents.</summary>
    public Size CanvasSize => _movie is { } movie ? new Size(movie.Color.Crop.Width, movie.Color.Crop.Height) : Plan.Size;

    /// <summary>Gets the pixel format of an untyped load, which is also the layout of the rows that the decoder produces.</summary>
    public PixelFormat PixelFormat => _movie is { } movie
        ? DefaultPixelFormats.ForAvif(movie.Color.IsMonochrome, movie.Alpha is not null, Math.Max(movie.Color.BitDepth, movie.Alpha?.BitDepth ?? 0))
        : Plan.PixelFormat;

    /// <summary>Gets a value indicating whether the color samples of the images are multiplied by the alpha of their pixel.</summary>
    public bool IsPremultiplied => _movie?.IsPremultiplied ?? Plan.IsPremultiplied;

    /// <summary>Gets the color description that the container gives to the images, or <see langword="null"/>.</summary>
    public CicpColorDescription? ContainerColorDescription => _nclx;

    /// <summary>Gets the animation settings of an image sequence of several images, or <see langword="null"/>.</summary>
    public AnimationMetadata? Animation => _movie is { } movie && (movie.FrameCount > 1 || (movie.IsFragmented && !_isAtEndOfInput)) ? new AnimationMetadata { TotalPlays = movie.TotalPlays } : null;

    /// <summary>Gets the metadata collected so far.</summary>
    public ImageMetadata Metadata => _metadata.Metadata;

    /// <summary>Gets a number that changes when <see cref="Metadata"/> does after the header: the decoder then gives the new metadata to its sink.</summary>
    public int MetadataVersion { get; private set; }

    /// <summary>Gets the operation the walk belongs to.</summary>
    public ImageCodecContext Context => _context;

    public override ParseStatus Parse(ReadOnlySpan<byte> buffer, bool isEndOfInput, out int consumed)
    {
        consumed = 0;
        while (true)
        {
            var remaining = buffer[consumed..];
            switch (_state)
            {
                case State.BoxHeader:
                {
                    if (_returnNow)
                    {
                        _returnNow = false;
                        return ParseStatus.Complete;
                    }

                    _isAtEndOfInput = isEndOfInput && remaining.IsEmpty;
                    if (IsResolved && TryFinish())
                        return ParseStatus.Complete;

                    // A header walk that looks for the bitstream ends with the input: what it found is all there is
                    if (_isProbingHeader && isEndOfInput && remaining.Length < BoxHeaderLength)
                    {
                        FinishHeaderWalk();
                        return ParseStatus.Complete;
                    }

                    var status = ReadBoxHeader(remaining, ref consumed);
                    if (!status.IsComplete)
                    {
                        // A fragmented sequence ends with the file, after any of its fragments: the end of the input is
                        // an answer to the request for the next box
                        return _movie is { IsFragmented: true } && !isEndOfInput && remaining.IsEmpty ? ParseStatus.NeedMoreDataOrEnd(status.RequiredBytes) : status;
                    }

                    break;
                }

                case State.Meta:
                {
                    if (_boxRemaining is 0)
                    {
                        // The 'meta' box of an image sequence is kept for the 'moov' box, which may refer to its metadata items
                        if (!_isSequence)
                            ProcessMeta();

                        if (_state is State.Done)
                            return ParseStatus.Complete;

                        _state = State.BoxHeader;
                        break;
                    }

                    if (remaining.IsEmpty)
                        return ParseStatus.NeedMoreData(1);

                    var piece = remaining[..(int)Math.Min(remaining.Length, _boxRemaining)];
                    _meta!.Append(piece);
                    Advance(ref consumed, piece.Length);
                    break;
                }

                case State.Movie:
                {
                    if (_boxRemaining is 0)
                    {
                        ProcessMovie();
                        if (_state is State.Done)
                            return ParseStatus.Complete;

                        _state = State.BoxHeader;
                        break;
                    }

                    if (remaining.IsEmpty)
                        return ParseStatus.NeedMoreData(1);

                    var piece = remaining[..(int)Math.Min(remaining.Length, _boxRemaining)];
                    _movieData!.Append(piece);
                    Advance(ref consumed, piece.Length);
                    break;
                }

                case State.Fragment:
                {
                    if (_boxRemaining is 0)
                    {
                        ProcessFragment();
                        _state = State.BoxHeader;
                        break;
                    }

                    if (remaining.IsEmpty)
                        return ParseStatus.NeedMoreData(1);

                    var piece = remaining[..(int)Math.Min(remaining.Length, _boxRemaining)];
                    _fragmentData!.Append(piece);
                    Advance(ref consumed, piece.Length);
                    break;
                }

                case State.Media:
                {
                    if (_boxRemaining is 0)
                    {
                        _isRetaining = false;
                        _state = State.BoxHeader;
                        break;
                    }

                    if (IsResolved && TryFinish())
                        return ParseStatus.Complete;

                    if (remaining.IsEmpty)
                    {
                        if (_isProbingHeader && isEndOfInput)
                        {
                            FinishHeaderWalk();
                            return ParseStatus.Complete;
                        }

                        return ParseStatus.NeedMoreData(1);
                    }

                    var piece = remaining[..(int)Math.Min(remaining.Length, _boxRemaining)];
                    if (_isRetaining)
                    {
                        _retained[^1].Data.Append(piece);
                    }
                    else if (_isProbingHeader)
                    {
                        // A header walk stops after the sequence header of the bitstream: the bytes that follow are not consumed
                        var probe = _captures[0];
                        Feed(_position, piece);
                        if (TryLocateSequenceHeader(probe.Buffer.Span, out var end) is true && probe.Offset + end - _position < piece.Length)
                            piece = piece[..(int)Math.Max(probe.Offset + end - _position, 0)];
                    }
                    else if (IsResolved)
                    {
                        Feed(_position, piece);
                    }

                    Advance(ref consumed, piece.Length);
                    break;
                }

                default:
                    return ParseStatus.Complete;
            }
        }
    }

    public override ImageInfo GetResult() => _result ?? throw new InvalidOperationException("The AVIF structure was not parsed.");

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _meta?.Dispose();
            _meta = null;
            _movieData?.Dispose();
            _movieData = null;
            _movie?.Dispose();
            _movie = null;
            _fragmentData?.Dispose();
            _fragmentData = null;
            ReleaseRetained();
            foreach (var capture in _captures)
            {
                capture.Dispose();
            }

            _captures.Clear();
        }

        base.Dispose(disposing);
    }

    private void Advance(ref int consumed, int count)
    {
        consumed += count;
        _position += count;
        if (_boxRemaining != long.MaxValue)
            _boxRemaining -= count;
    }

    private ParseStatus ReadBoxHeader(ReadOnlySpan<byte> remaining, ref int consumed)
    {
        if (remaining.Length < BoxHeaderLength)
            return ParseStatus.NeedMoreData(BoxHeaderLength);

        ulong size = BinaryPrimitives.ReadUInt32BigEndian(remaining);
        var type = BinaryPrimitives.ReadUInt32BigEndian(remaining[4..]);
        var headerLength = BoxHeaderLength;
        if (size is 1)
        {
            if (remaining.Length < LargeBoxHeaderLength)
                return ParseStatus.NeedMoreData(LargeBoxHeaderLength);

            size = BinaryPrimitives.ReadUInt64BigEndian(remaining[8..]);
            headerLength = LargeBoxHeaderLength;
        }

        // A size of 0 is a box that extends to the end of the file
        long payloadLength;
        if (size is 0)
        {
            payloadLength = long.MaxValue;
        }
        else if (size < (ulong)headerLength || size > long.MaxValue)
        {
            throw AvifFormat.Invalid($"the box '{AvifFormat.ToText(type)}' has a size that is not valid.");
        }
        else
        {
            payloadLength = (long)size - headerLength;
        }

        if (!_seenFileType)
        {
            if (type is not AvifFormat.Ftyp)
                throw AvifFormat.Invalid("the file does not start with a file type box.");

            if (payloadLength > MaxFileTypeLength)
                throw AvifFormat.Invalid("the file type box is too large.");

            var total = headerLength + (int)payloadLength;
            if (remaining.Length < total)
                return ParseStatus.NeedMoreData(total);

            ProcessFileType(remaining.Slice(headerLength, (int)payloadLength));
            _seenFileType = true;
            _boxRemaining = total;
            Advance(ref consumed, total);
            return ParseStatus.Complete;
        }

        if (type is AvifFormat.Ftyp)
            throw AvifFormat.Invalid("the file has two file type boxes.");

        var boxOffset = _position;
        _boxRemaining = headerLength;
        Advance(ref consumed, headerLength);
        _boxRemaining = payloadLength;

        // A movie fragment describes more images of an image sequence whose movie announces fragments. A file that
        // presents its primary item has no use for it
        if (type is AvifFormat.Moof && _isSequence)
        {
            if (_movie is null)
                throw AvifFormat.Unsupported("The AVIF image sequence has a movie fragment before its 'moov' box.", "fragmented image sequence without movie");

            if (!_movie.IsFragmented)
                throw AvifFormat.Invalid("the file has a movie fragment that its 'moov' box does not announce.");

            if (payloadLength is long.MaxValue)
                throw AvifFormat.Unsupported("The AVIF 'moof' box has no size.", "moof box without size");

            _fragmentData?.Dispose();
            _fragmentData = new WebPPayloadBuffer(_context.Scope, payloadLength);
            _fragmentOffset = boxOffset;
            _state = State.Fragment;
            return ParseStatus.Complete;
        }

        if (type is AvifFormat.Moov && _isSequence)
        {
            if (_movieData is not null || _movie is not null)
                throw AvifFormat.Invalid("the file has two 'moov' boxes.");

            if (payloadLength is long.MaxValue)
                throw AvifFormat.Unsupported("The AVIF 'moov' box has no size.", "moov box without size");

            _movieData = new WebPPayloadBuffer(_context.Scope, payloadLength);
            _state = State.Movie;
            return ParseStatus.Complete;
        }

        // The 'meta' box of an image sequence only matters before the 'moov' box, and for a walk that reads metadata items
        if (type is AvifFormat.Meta && (!_isSequence || (_movie is null && _walk is not StructureWalk.Header)))
        {
            if (_meta is not null)
                throw AvifFormat.Invalid("the file has two 'meta' boxes.");

            if (payloadLength is long.MaxValue)
                throw AvifFormat.Unsupported("The AVIF 'meta' box has no size.", "meta box without size");

            _meta = new WebPPayloadBuffer(_context.Scope, payloadLength);
            _state = State.Meta;
            return ParseStatus.Complete;
        }

        // Media data that precede the 'meta' box, or the 'moov' box of an image sequence, are kept until the items or the
        // samples they hold are known
        if (type is AvifFormat.Mdat && !IsResolved && _walk is not StructureWalk.Header)
        {
            _retained.Add((_position, new WebPPayloadBuffer(_context.Scope, Math.Min(payloadLength, CheckedSizes.MaxBufferLength))));
            _isRetaining = true;
        }

        _state = State.Media;
        return ParseStatus.Complete;
    }

    // ISO/IEC 14496-12, section 4.3 "File Type Box"
    private void ProcessFileType(ReadOnlySpan<byte> payload)
    {
        if (payload.Length < 8 || payload.Length % 4 is not 0)
            throw AvifFormat.Invalid("the file type box is not valid.");

        var isImage = false;
        var isSequence = false;
        for (var i = 0; i < payload.Length; i += 4)
        {
            // The second field is the minor version of the major brand
            if (i is 4)
                continue;

            var brand = BinaryPrimitives.ReadUInt32BigEndian(payload[i..]);
            isImage |= brand is AvifFormat.Avif;
            isSequence |= brand is AvifFormat.Avis;
        }

        if (!isImage && !isSequence)
            throw new UnknownImageFormatException("The file is an ISO base media file that is not an AVIF image.");

        // A file with the brand of the image sequences presents its sequence, not its primary item
        _isSequence = isSequence;
    }

    private bool IsResolved => _plan is not null || _movie is not null;

    private void ProcessMeta()
    {
        var payload = _meta!.Span;
        var meta = AvifMetaBox.Parse(payload);
        var plan = AvifImagePlan.Create(meta, payload, _context.Limits);
        _plan = plan;

        var metadata = _metadata.Metadata;
        _nclx = plan.Nclx;
        // (the coded images of an image that is built were checked with their own color description)
        if (plan.Nclx is { } nclx && plan.Root is null)
            AvifYuvConverter.EnsureSupported(nclx.MatrixCoefficients, plan.Color.IsMonochrome, plan.Color.SubsamplingX, plan.Color.SubsamplingY, nclx.ColorPrimaries, nclx.TransferCharacteristics);

        metadata.Orientation = plan.Orientation;
        metadata.Cicp = plan.Nclx;
        metadata.PixelAspectRatio = plan.PixelAspectRatio;
        metadata.ContentLightLevel = plan.ContentLightLevel;
        metadata.MasteringDisplay = plan.MasteringDisplay;
        if (plan.IccProfile.Length > 0)
        {
            _metadata.Charge(plan.IccProfile.Length);
            _metadata.TryAdoptIccProfile(payload.Slice(plan.IccProfile.Offset, plan.IccProfile.Length).ToArray(), plan.Root is null && plan.Color.IsMonochrome);
        }

        if (_walk is StructureWalk.Header)
        {
            // Without 'colr' box, the color description is the one of the bitstream: the walk goes on to its first bytes
            // (those of the first coded image of an image that is built from other images)
            var first = plan.Color.Tiles[0];
            if (plan.Nclx is null && (plan.Root is not null || !plan.Color.IsMonochrome) && first.HasLocation && first.Extents.Count > 0 && first.ConstructionMethod <= 1)
            {
                var (offset, length) = first.Extents[0];
                if (first.ConstructionMethod is 1)
                {
                    var idat = payload.Slice(meta.ItemData.Offset, meta.ItemData.Length);
                    if (offset <= idat.Length)
                        ProbeBitstream(idat[(int)offset..]);
                }
                else if (offset >= _position)
                {
                    _captures.Add(new Capture(_context.Scope, first, offset, length is 0 ? HeaderProbeLength : Math.Min(length, HeaderProbeLength), isItemData: false));
                    _isProbingHeader = true;
                    return;
                }
            }

            FinishHeaderWalk();
            return;
        }

        Schedule(meta, plan, payload);
        if (_walk is StructureWalk.Decode)
        {
            _context.Sequential?.OnHeader(CreateInfo(ImageIdentifyMode.Header));
            _observer!.OnHeaderComplete(this);
            _returnNow = _context.TryConsumeYield();
        }
    }

    // The items whose data the walk captures: the coded items and the grid descriptors of the color image and of the alpha
    // plane, and the Exif and XMP items
    private void Schedule(AvifMetaBox meta, AvifImagePlan plan, ReadOnlySpan<byte> metaPayload)
    {
        var items = new List<AvifItem>();
        if (plan.Root is { } root)
        {
            // An image that is built: every coded image with its alpha plane, and the descriptors of the grids and of the
            // overlays
            root.ForEachCoded(node =>
            {
                AddImage(node.Image);
                if (node.Alpha is not null)
                    AddImage(node.Alpha);
            });
            root.ForEachDescriptor(node => AddItem(node.Image.Item, isMetadata: false));
        }
        else
        {
            AddImage(plan.Color);
            if (plan.Alpha is not null)
                AddImage(plan.Alpha);
        }

        if (plan.Exif is not null)
            AddItem(plan.Exif, isMetadata: true);

        if (plan.Xmp is not null)
            AddItem(plan.Xmp, isMetadata: true);

        long total = 0;
        foreach (var capture in _captures)
        {
            total = checked(total + capture.Length);
        }

        if (total > _context.Limits.MaxEncodedBytes)
            throw new ImageResourceLimitException(ImageResourceLimitKind.EncodedBytes, _context.Limits.MaxEncodedBytes, total);

        FillItemData(meta, metaPayload);

        // The media data that came before the 'meta' box
        foreach (var (offset, data) in _retained)
        {
            Feed(offset, data.Span);
        }

        ReleaseRetained();

        void AddImage(AvifCodedImage image)
        {
            if (image.IsGrid)
                AddItem(image.Item, isMetadata: false);

            foreach (var tile in image.Tiles)
            {
                AddItem(tile, isMetadata: false);
            }
        }

        void AddItem(AvifItem item, bool isMetadata)
        {
            // Identical tiles may share an item
            if (items.Contains(item))
                return;

            items.Add(item);
            AddItemCaptures(meta, item, isMetadata);
        }
    }

    // Schedules the capture of the extents of an item
    private void AddItemCaptures(AvifMetaBox meta, AvifItem item, bool isMetadata)
    {
        if (!item.HasLocation || item.Extents.Count is 0)
            throw AvifFormat.Invalid("an item has no data.");

        if (item.ConstructionMethod > 1)
            throw AvifFormat.Unsupported("The data of an AVIF item is built from other items.", "item construction method");

        if (isMetadata)
            _context.Tracker.ChargeMetadataBytes(item.DataLength);

        foreach (var (offset, length) in item.Extents)
        {
            var extentLength = length;
            if (extentLength is 0)
            {
                // The whole 'idat' box; a file extent without length has no end
                if (item.ConstructionMethod is not 1 || item.Extents.Count is not 1)
                    throw AvifFormat.Invalid("an item has no data.");

                extentLength = meta.ItemData.Length - offset;
                if (extentLength <= 0)
                    throw AvifFormat.Invalid("an item has no data.");
            }

            _captures.Add(new Capture(_context.Scope, item, offset, extentLength, item.ConstructionMethod is 1));
        }
    }

    // Copies the data of the items that are stored in the 'idat' box of their 'meta' box
    private void FillItemData(AvifMetaBox meta, ReadOnlySpan<byte> metaPayload)
    {
        var idat = metaPayload.Slice(meta.ItemData.Offset, meta.ItemData.Length);
        foreach (var capture in _captures)
        {
            if (capture.IsItemData && !capture.IsComplete)
            {
                if (capture.Offset > idat.Length || capture.Length > idat.Length - capture.Offset)
                    throw AvifFormat.Invalid("the data of an item is outside the 'idat' box.");

                capture.Buffer.Append(idat.Slice((int)capture.Offset, (int)capture.Length));
            }
        }
    }

    // Adopts the Exif and XMP items whose data are captured
    private void AdoptItemMetadata(AvifItem? exif, AvifItem? xmp)
    {
        if (exif is not null)
        {
            // ISO/IEC 23008-12, annex A.2.1: the offset of the TIFF header, then the Exif data
            var data = GetItemData(exif);
            if (data.Length >= 4)
            {
                var headerOffset = BinaryPrimitives.ReadUInt32BigEndian(data);
                if (headerOffset <= (uint)(data.Length - 4))
                    _metadata.TryAdoptExif(data[(4 + (int)headerOffset)..], adoptOrientation: false, alreadyCharged: true);
            }
        }

        if (xmp is not null)
            _metadata.TryAdoptXmp(GetItemData(xmp), alreadyCharged: true);
    }

    // Copies the bytes of the file at offset to the extents that include them
    private void Feed(long offset, ReadOnlySpan<byte> data)
    {
        foreach (var capture in _captures)
        {
            if (capture.IsItemData || capture.IsComplete)
                continue;

            // An extent is captured in order: its next byte follows the ones that were received
            var next = capture.Offset + capture.ReceivedLength;
            if (next < offset || next >= offset + data.Length)
                continue;

            var available = data[(int)(next - offset)..];
            var count = (int)Math.Min(available.Length, capture.Length - capture.ReceivedLength);
            capture.Append(available[..count]);
        }
    }

    private bool TryFinish()
    {
        if (_walk is StructureWalk.Header)
        {
            // The walk goes on until the sequence header is whole, cannot be found, or the probe is full
            if (!_isProbingHeader || (TryLocateSequenceHeader(_captures[0].Buffer.Span, out _) is null && !_captures[0].IsComplete))
                return false;

            FinishHeaderWalk();
            return true;
        }

        if (_movie is not null)
            return TryAdvanceSequence();

        foreach (var capture in _captures)
        {
            if (!capture.IsComplete)
                return false;
        }

        var plan = _plan!;
        AdoptItemMetadata(plan.Exif, plan.Xmp);
        if (plan.Root is { } root)
        {
            root.ForEachCoded(node =>
            {
                if (node.Image.IsGrid)
                    node.Image.ReadGridDescriptor(GetItemData(node.Image.Item));

                if (node.Alpha is { IsGrid: true } nodeAlpha)
                    nodeAlpha.ReadGridDescriptor(GetItemData(nodeAlpha.Item));
            });

            // A coded image without color description in the boxes has the one of its bitstream, which converts its
            // samples and which must be the one of the image, like a description of the boxes. When no box describes
            // the image, it is described by the bitstream of its first coded image, which a header walk reads
            var declared = plan.Nclx;
            var isFirst = true;
            root.ForEachCoded(node =>
            {
                var describesImage = isFirst && declared is null;
                isFirst = false;
                if (node.Nclx is not null)
                    return;

                var sequence = ReadSequenceHeader(GetItemData(node.Image.Tiles[0]), node.Image.OperatingPoints[0]);
                AvifYuvConverter.EnsureSupported(sequence.MatrixCoefficients, sequence.Monochrome, sequence.SubsamplingX, sequence.SubsamplingY, sequence.ColorPrimaries, sequence.TransferCharacteristics);
                if (!sequence.ColorDescriptionPresent)
                    return;

                if (describesImage)
                    declared = new CicpColorDescription((byte)sequence.ColorPrimaries, (byte)sequence.TransferCharacteristics, 0, fullRange: true);

                if (declared is null || sequence.ColorPrimaries != declared.ColorPrimaries || sequence.TransferCharacteristics != declared.TransferCharacteristics)
                    throw AvifFormat.Unsupported("The images that the AVIF image is built from do not have the same color primaries and transfer characteristics.", "derived image of several color spaces");
            });
            _metadata.Metadata.Cicp ??= declared;

            // The descriptors of the grids and of the overlays. The fill color of an overlay is an sRGB color: where it
            // shows, the image must be an sRGB one, since nothing converts colors. Black is the same color in every
            // color space, and a transparent canvas has no color
            var isSrgb = _metadata.Metadata.IccProfile is { } profile ? SrgbProfileRecognition.IsSrgb(profile, PixelFormat.Rgba32) : declared is null || MetadataWritePlan.IsSrgbEquivalent(declared);
            root.ForEachDescriptor(node =>
            {
                node.ReadDescriptor(GetItemData(node.Image.Item));
                if (node.Kind is AvifImageNodeKind.Overlay && node.Fill[3] is not 0 && (node.Fill[0] | node.Fill[1] | node.Fill[2]) is not 0 && !isSrgb)
                    throw AvifFormat.Unsupported("The canvas of the AVIF overlay is filled with an sRGB color and the image is not an sRGB one.", "overlay fill color in another color space");
            });
        }
        else
        {
            if (plan.Color.IsGrid)
                plan.Color.ReadGridDescriptor(GetItemData(plan.Color.Item));

            if (plan.Alpha is { IsGrid: true } alpha)
                alpha.ReadGridDescriptor(GetItemData(alpha.Item));
        }

        if (_walk is StructureWalk.Decode)
        {
            _observer!.OnImageData(this);
        }
        else
        {
            _context.Tracker.ChargeScannedFrame();
            if (plan.Root is { } tree)
            {
                tree.ForEachCoded(node =>
                {
                    InspectCodedItems(node.Image);
                    if (node.Alpha is not null)
                        InspectCodedItems(node.Alpha);
                });
            }
            else
            {
                InspectCodedItems(plan.Color);
                if (plan.Alpha is not null)
                    InspectCodedItems(plan.Alpha);
            }
        }

        _result = CreateInfo(ImageIdentifyMode.FullScan);
        _state = State.Done;
        return true;
    }

    // Ends a header walk with what it read of the bitstream, if anything
    private void FinishHeaderWalk()
    {
        // The only capture of a header walk holds the first bytes of the bitstream
        if (_isProbingHeader)
        {
            ProbeBitstream(_captures[0].Buffer.Span);
            _isProbingHeader = false;
        }

        _result = CreateInfo(ImageIdentifyMode.Header);
        _state = State.Done;
    }

    // Reads the color description of the bitstream from the first bytes of a coded item or of a sample. These bytes are a
    // prefix: when they do not hold a whole sequence header, the color description stays unknown, and the bitstream is
    // validated by a full scan or by a load
    private void ProbeBitstream(ReadOnlySpan<byte> prefix)
    {
        if (TryLocateSequenceHeader(prefix, out var end) is not true)
            return;

        Av1SequenceHeader sequence;
        try
        {
            sequence = ReadSequenceHeader(prefix[..end]);
        }
        catch (ImageException)
        {
            return;
        }

        SetBitstreamColorDescription(sequence);
    }

    // Looks for the first sequence header in the first bytes of a bitstream: true when the data hold it whole, up to the
    // returned offset; false when they cannot lead to one; null when more bytes are needed to tell
    private static bool? TryLocateSequenceHeader(ReadOnlySpan<byte> data, out int end)
    {
        end = 0;
        var offset = 0;
        while (offset < data.Length)
        {
            // AV1 specification, section 5.3.2 "OBU header syntax": only the last unit of the data may have no size field
            var header = data[offset];
            if ((header & 0x82) is not 0x02)
                return false;

            var position = offset + 1 + ((header >> 2) & 1);
            long size = 0;
            var shift = 0;
            while (true)
            {
                if (position >= data.Length)
                    return null;

                var value = data[position++];
                size |= (long)(value & 0x7F) << shift;
                if ((value & 0x80) is 0)
                    break;

                shift += 7;
                if (shift > 28)
                    return false;
            }

            var next = position + size;
            if (((header >> 3) & 15) is Av1Constants.ObuSequenceHeader)
            {
                if (next > data.Length)
                    return null;

                end = (int)next;
                return true;
            }

            if (next > int.MaxValue)
                return false;

            offset = (int)next;
        }

        return null;
    }

    // A full scan verifies that the bitstream of each coded item has the sample format of its configuration record
    private void InspectCodedItems(AvifCodedImage image)
    {
        for (var i = 0; i < image.Tiles.Length; i++)
        {
            // The operating point that the item selects must exist, as a load requires
            var sequence = ReadSequenceHeader(GetItemData(image.Tiles[i]), image.OperatingPoints[i]);
            EnsureMatchesConfiguration(image, sequence);
            if (ReferenceEquals(image, _plan!.Color) && _plan.Root is null)
                SetBitstreamColorDescription(sequence);
        }
    }

    /// <summary>Reads the sequence header of the data of a coded item or of a sample, for an operating point.</summary>
    /// <exception cref="InvalidImageContentException">The data have no sequence header, or it does not have the operating point.</exception>
    public static Av1SequenceHeader ReadSequenceHeader(ReadOnlySpan<byte> data, int operatingPoint = 0)
    {
        while (!data.IsEmpty)
        {
            var obu = Av1ObuHeader.Read(data);
            if (obu.Type is Av1Constants.ObuSequenceHeader)
            {
                return Av1SequenceHeader.Parse(data.Slice(obu.HeaderLength, obu.PayloadLength), operatingPoint);
            }

            data = data[obu.TotalLength..];
        }

        throw AvifFormat.Invalid("an AV1 image has no sequence header.");
    }

    /// <summary>Verifies that the bitstream of a coded item has the sample format that its configuration record declares.</summary>
    public static void EnsureMatchesConfiguration(AvifCodedImage image, Av1SequenceHeader sequence)
    {
        if (sequence.BitDepth != image.BitDepth || sequence.Monochrome != image.IsMonochrome || sequence.SubsamplingX != image.SubsamplingX || sequence.SubsamplingY != image.SubsamplingY)
            throw AvifFormat.Invalid("the bitstream of an AV1 image does not match its configuration record.");
    }

    /// <summary>Verifies that the bitstream of a track has the sample format that its configuration record declares.</summary>
    public static void EnsureMatchesConfiguration(AvifTrack track, Av1SequenceHeader sequence)
    {
        if (sequence.BitDepth != track.BitDepth || sequence.Monochrome != track.IsMonochrome || sequence.SubsamplingX != track.SubsamplingX || sequence.SubsamplingY != track.SubsamplingY)
            throw AvifFormat.Invalid("the bitstream of an AV1 track does not match its configuration record.");
    }

    /// <summary>
    /// Sets the color description of the image from the sequence header of its bitstream, unless a <c>colr</c> property
    /// describes it.
    /// </summary>
    public void SetBitstreamColorDescription(Av1SequenceHeader sequence)
    {
        if (_nclx is not null)
            return;

        // Without 'colr' box, the matrix coefficients of the bitstream convert the samples
        AvifYuvConverter.EnsureSupported(sequence.MatrixCoefficients, sequence.Monochrome, sequence.SubsamplingX, sequence.SubsamplingY, sequence.ColorPrimaries, sequence.TransferCharacteristics);
        _bitstreamMatrix = sequence.MatrixCoefficients;
        if (!sequence.ColorDescriptionPresent)
            return;

        // The pixels of an image that is built from other images are red, green and blue samples in full range
        _metadata.Metadata.Cicp ??= _plan is { Root: not null }
            ? new CicpColorDescription((byte)sequence.ColorPrimaries, (byte)sequence.TransferCharacteristics, 0, fullRange: true)
            : new CicpColorDescription((byte)sequence.ColorPrimaries, (byte)sequence.TransferCharacteristics, (byte)sequence.MatrixCoefficients, sequence.FullRange);
    }

    /// <summary>Gets the data of a captured item.</summary>
    public ReadOnlySpan<byte> GetItemData(AvifItem item)
    {
        GetItemBuffer(item, out var data, out var length);
        return data.AsSpan(0, length);
    }

    /// <summary>Gets the buffer that holds the data of a captured item, which starts at index 0.</summary>
    public void GetItemBuffer(AvifItem item, out byte[] data, out int length)
    {
        Capture? first = null;
        var count = 0;
        foreach (var capture in _captures)
        {
            if (ReferenceEquals(capture.Item, item))
            {
                first ??= capture;
                count++;
            }
        }

        if (first is null)
            throw new InvalidOperationException("The item was not captured.");

        if (count > 1)
            first = Merge(item, first);

        data = first.Buffer.Data;
        length = first.Buffer.Length;
    }

    // Concatenates the extents of an item into its first capture
    private Capture Merge(AvifItem item, Capture first)
    {
        long total = 0;
        foreach (var capture in _captures)
        {
            if (ReferenceEquals(capture.Item, item))
                total += capture.Buffer.Length;
        }

        var merged = new Capture(_context.Scope, item, first.Offset, total, first.IsItemData);
        var index = -1;
        for (var i = 0; i < _captures.Count; i++)
        {
            var capture = _captures[i];
            if (!ReferenceEquals(capture.Item, item))
                continue;

            merged.Buffer.Append(capture.Buffer.Span);
            capture.Dispose();
            if (index < 0)
            {
                index = i;
            }
            else
            {
                _captures.RemoveAt(i);
                i--;
            }
        }

        _captures[index] = merged;
        return merged;
    }

    private void ReleaseRetained()
    {
        foreach (var (_, data) in _retained)
        {
            data.Dispose();
        }

        _retained.Clear();
        _isRetaining = false;
    }

    private ImageInfo CreateInfo(ImageIdentifyMode mode)
    {
        bool isMonochrome;
        bool hasAlpha;
        int bitDepth;
        int? frameCount = 1;
        if (_movie is { } movie)
        {
            isMonochrome = movie.Color.IsMonochrome;
            hasAlpha = movie.Alpha is not null;
            bitDepth = movie.Color.BitDepth;

            // The images of a fragmented sequence are counted when the file was read to its end
            frameCount = movie.IsFragmented && (mode is ImageIdentifyMode.Header || !_isAtEndOfInput) ? null : (int)Math.Min(movie.FrameCount, int.MaxValue);
        }
        else
        {
            var plan = _plan!;
            isMonochrome = plan.Root is null && plan.Color.IsMonochrome;
            hasAlpha = plan.HasAlpha;
            bitDepth = plan.Root is null ? plan.Color.BitDepth : plan.RootBitDepth;
        }

        // The matrix coefficients come from the 'colr' box, or else from the bitstream, which a header walk does not read
        var matrix = _nclx?.MatrixCoefficients ?? _bitstreamMatrix;
        ImageColorModel colorModel;
        if (_plan is { Root: not null })
        {
            // An image that is built from other images is made of red, green and blue pixels with alpha
            colorModel = ImageColorModel.Rgba;
        }
        else if (isMonochrome)
        {
            colorModel = hasAlpha ? ImageColorModel.GrayscaleAlpha : ImageColorModel.Grayscale;
        }
        else if (matrix is null)
        {
            colorModel = ImageColorModel.Unknown;
        }
        else if (matrix is Av1Constants.McIdentity)
        {
            colorModel = hasAlpha ? ImageColorModel.Rgba : ImageColorModel.Rgb;
        }
        else
        {
            colorModel = ImageColorModel.YCbCr;
        }

        return new ImageInfo(
            ImageFormat.Avif,
            CanvasSize,
            PixelFormat,
            colorModel,
            bitDepth,
            frameCount,
            isAnimated: frameCount is { } count ? count > 1 : Animation is not null,
            hasPosterFrame: false,
            mayHaveTransparency: hasAlpha,
            Animation,
            _metadata.Metadata,
            mode);
    }

    // ISO/IEC 14496-12, section 8.2.1 "Movie Box": the image sequence of the file
    private void ProcessMovie()
    {
        var payload = _movieData!.Span;
        // A header walk locates the first sample only, where the sequence header of the bitstream is
        var maxSamples = _walk is StructureWalk.Header ? 1 : (int)Math.Min(_context.Limits.MaxFrames + 1L, int.MaxValue);

        var movie = AvifMovieBox.Parse(payload, _context.Scope, maxSamples);
        if (movie.Timeline is not null && maxSamples > 1 && movie.Color.LocatedSampleCount < movie.SampleCount)
        {
            // An edit list that selects parts of the media may show images that are not the first ones of the track,
            // after images that it leaves out: every sample is located (their tables are charged to the scope), and the
            // frame limit applies to the images that are shown
            movie.Dispose();
            movie = AvifMovieBox.Parse(payload, _context.Scope, int.MaxValue);
        }

        _movie = movie;
        var color = movie.Color;
        _context.Limits.EnsureCanvasWithinLimits(color.Size.Width, color.Size.Height);
        _nclx = color.Nclx;
        if (color.Nclx is { } nclx)
            AvifYuvConverter.EnsureSupported(nclx.MatrixCoefficients, color.IsMonochrome, color.SubsamplingX, color.SubsamplingY, nclx.ColorPrimaries, nclx.TransferCharacteristics);

        _metadata.Metadata.Cicp = color.Nclx;
        _metadata.Metadata.Orientation = color.Orientation;
        _metadata.Metadata.PixelAspectRatio = color.PixelAspectRatio;
        _metadata.Metadata.ContentLightLevel = color.ContentLightLevel;
        _metadata.Metadata.MasteringDisplay = color.MasteringDisplay;
        if (color.IccProfile.Length > 0)
        {
            _metadata.Charge(color.IccProfile.Length);
            _metadata.TryAdoptIccProfile(payload.Slice(color.IccProfile.Offset, color.IccProfile.Length).ToArray(), color.IsMonochrome);
        }

        if (_walk is StructureWalk.Header)
        {
            _movieData.Dispose();
            _movieData = null;

            // Without 'colr' box, the color description is the one of the bitstream: the walk goes on to its first bytes
            // (those of the first coded image of an image that is built from other images)
            if (color.Nclx is null && !color.IsMonochrome && color.LocatedSampleCount > 0 && color.GetSampleOffset(0) >= _position)
            {
                _captures.Add(new Capture(_context.Scope, item: null, color.GetSampleOffset(0), Math.Min(color.GetSampleSize(0), HeaderProbeLength), isItemData: false));
                _isProbingHeader = true;
                return;
            }

            FinishHeaderWalk();
            return;
        }

        ScheduleSequenceMetadata(color, payload);

        // A full scan counts the images, and walks their samples without keeping more than the first one of each track
        if (_walk is StructureWalk.FullScan)
        {
            for (long i = 0; i < movie.FrameCount; i++)
            {
                _context.Tracker.ChargeScannedFrame();
            }
        }

        // The samples whose data the walk captures
        for (var i = 0; i < color.LocatedSampleCount; i++)
        {
            ScheduleSample(color, i);
            if (movie.Alpha is { } alpha && i < alpha.LocatedSampleCount)
                ScheduleSample(alpha, i);
        }

        foreach (var (offset, data) in _retained)
        {
            Feed(offset, data.Span);
        }

        ReleaseRetained();
        _movieData.Dispose();
        _movieData = null;
        _meta?.Dispose();
        _meta = null;
        if (_walk is StructureWalk.Decode)
        {
            _context.Sequential?.OnHeader(CreateInfo(ImageIdentifyMode.Header));
            _observer!.OnHeaderComplete(this);
            _returnNow = _context.TryConsumeYield();
        }

    }

    // Schedules the capture of a sample of an image sequence
    private void ScheduleSample(AvifTrack track, int index)
    {
        var size = track.GetSampleSize(index);
        _scheduledBytes += size;
        if (_scheduledBytes > _context.Limits.MaxEncodedBytes)
            throw new ImageResourceLimitException(ImageResourceLimitKind.EncodedBytes, _context.Limits.MaxEncodedBytes, _scheduledBytes);

        var storesData = _walk is StructureWalk.Decode || index is 0;
        _captures.Add(new Capture(_context.Scope, item: null, track.GetSampleOffset(index), size, isItemData: false, storesData) { Track = track, SampleIndex = index });
    }

    // ISO/IEC 14496-12, section 8.8 "Movie Fragments": the samples of the fragment follow the samples that are known. Their
    // data follow the 'moof' box, where the walk captures them as they go by
    private void ProcessFragment()
    {
        var movie = _movie!;
        var color = movie.Color;
        var alpha = movie.Alpha;
        var (colorCount, alphaCount) = (color.LocatedSampleCount, alpha?.LocatedSampleCount ?? 0);
        var samplesBefore = color.SampleCount;

        // A full scan keeps the first sample of each track, which it verifies at the end, and forgets the other ones
        // once they went by; a decoding walk released the samples of the images it produced
        var firstNeeded = _walk is StructureWalk.Decode ? _nextFrame : color.LocatedSampleCount;
        if (_walk is not StructureWalk.Decode)
        {
            for (var i = _captures.Count - 1; i >= 0; i--)
            {
                if (_captures[i] is { Track: not null, SampleIndex: > 0, IsComplete: true } capture)
                {
                    _captures.RemoveAt(i);
                    capture.Dispose();
                }
            }
        }

        var maxSamples = (int)Math.Min(_context.Limits.MaxFrames + 1L, int.MaxValue);
        AvifMovieFragment.Parse(_fragmentData!.Span, _fragmentOffset, movie, _context.Scope, firstNeeded, maxSamples);
        _fragmentData.Dispose();
        _fragmentData = null;
        if (_walk is StructureWalk.FullScan)
        {
            for (var i = samplesBefore; i < color.SampleCount; i++)
            {
                _context.Tracker.ChargeScannedFrame();
            }
        }

        // The data of a fragment follow its description: what is behind the position of the walk is gone
        for (var i = colorCount; i < color.LocatedSampleCount; i++)
        {
            EnsureAhead(color, i);
            ScheduleSample(color, i);
        }

        if (alpha is not null)
        {
            for (var i = alphaCount; i < alpha.LocatedSampleCount; i++)
            {
                EnsureAhead(alpha, i);
                ScheduleSample(alpha, i);
            }
        }

        void EnsureAhead(AvifTrack track, int index)
        {
            if (track.GetSampleOffset(index) < _position)
                throw AvifFormat.Unsupported("The samples of a fragment of the AVIF image sequence precede the fragment.", "fragmented image sequence whose data precede their fragment");
        }
    }

    // The Exif and XMP items of an image sequence are the ones of the 'meta' box of its track, or else of the 'meta' box
    // of the file when it precedes the 'moov' box. They are data of the file, located as the items of a still image are
    private void ScheduleSequenceMetadata(AvifTrack color, ReadOnlySpan<byte> moviePayload)
    {
        ReadOnlySpan<byte> metaPayload;
        if (color.Meta.Length > 0)
        {
            metaPayload = moviePayload.Slice(color.Meta.Offset, color.Meta.Length);
        }
        else if (_meta is not null)
        {
            metaPayload = _meta.Span;
        }
        else
        {
            return;
        }

        var meta = AvifMetaBox.Parse(metaPayload);
        foreach (var item in meta.Items.Values.OrderBy(item => item.Id))
        {
            if (item.Type is AvifFormat.Exif)
            {
                _sequenceExif ??= item;
            }
            else if (item.IsXmp)
            {
                _sequenceXmp ??= item;
            }
        }

        if (_sequenceExif is not null)
            AddItemCaptures(meta, _sequenceExif, isMetadata: true);

        if (_sequenceXmp is not null)
            AddItemCaptures(meta, _sequenceXmp, isMetadata: true);

        FillItemData(meta, metaPayload);
        _hasPendingSequenceMetadata = _sequenceExif is not null || _sequenceXmp is not null;
    }

    // Adopts the metadata items of the sequence once their data went by
    private bool TryAdoptSequenceMetadata()
    {
        if (!_hasPendingSequenceMetadata)
            return true;

        foreach (var capture in _captures)
        {
            if (capture.Item is not null && !capture.IsComplete)
                return false;
        }

        AdoptItemMetadata(_sequenceExif, _sequenceXmp);
        _hasPendingSequenceMetadata = false;
        MetadataVersion++;
        return true;
    }

    // Produces the images of the sequence whose samples are captured, in order
    private bool TryAdvanceSequence()
    {
        var movie = _movie!;
        if (_walk is not StructureWalk.Decode)
        {
            // A full scan ends when every sample went by; it verifies the first sample of each track. The samples of a
            // fragmented sequence go by until the end of the file
            foreach (var capture in _captures)
            {
                if (!capture.IsComplete)
                    return false;
            }

            if (movie.IsFragmented)
            {
                if (!_isAtEndOfInput)
                    return false;

                EnsureFragmentsAreWhole(movie);
            }

            TryAdoptSequenceMetadata();
            var first = FindSample(movie.Color, 0);
            var sequence = ReadSequenceHeader(first.Buffer.Span);
            EnsureMatchesConfiguration(movie.Color, sequence);
            SetBitstreamColorDescription(sequence);
            if (movie.Alpha is { } alphaTrack)
                EnsureMatchesConfiguration(alphaTrack, ReadSequenceHeader(FindSample(alphaTrack, 0).Buffer.Span));

            _result = CreateInfo(ImageIdentifyMode.FullScan);
            _state = State.Done;
            return true;
        }

        while (true)
        {
            // The images of the track are read in their order. The ones that the edit list leaves out are decoded too:
            // the images that follow may refer to them, and a file is read to the last of its samples, so that one that is
            // cut short never reads as a whole one
            if (_nextFrame >= movie.SampleCount)
            {
                // The images of a fragmented sequence come with its fragments, until the end of the file
                if (movie.IsFragmented)
                {
                    if (!_isAtEndOfInput)
                        return false;

                    EnsureFragmentsAreWhole(movie);
                }

                _result = CreateInfo(ImageIdentifyMode.FullScan);
                _state = State.Done;
                return true;
            }

            // The alpha plane of an image of a fragmented sequence may be described by a later fragment
            if (movie.Alpha is { } pendingAlpha && _nextFrame >= pendingAlpha.SampleCount)
            {
                if (_isAtEndOfInput)
                    throw AvifFormat.Invalid("the alpha track does not have the number of images of the color track.");

                return false;
            }

            if (!_hasFrameTiming)
            {
                _isFrameShown = movie.TryGetNextDuration(_nextFrame, out _frameDuration);
                _hasFrameTiming = true;
            }

            // The metadata items are adopted as soon as their data went by, and the last image waits for them: the images
            // that follow their data have them, at the latest the last one
            if (!TryAdoptSequenceMetadata() && _isFrameShown && _shownFrames == movie.FrameCount - 1)
                return false;

            // The samples beyond the frame limit of the operation are not located: the limit was reported before
            if (_nextFrame >= movie.Color.LocatedSampleCount)
                throw new ImageResourceLimitException(ImageResourceLimitKind.Frames, _context.Limits.MaxFrames, _nextFrame + 1L);

            var color = FindSample(movie.Color, _nextFrame);
            var alpha = movie.Alpha is null ? null : FindSample(movie.Alpha, _nextFrame);
            if (!color.IsComplete || alpha is { IsComplete: false })
                return false;

            var proceed = _observer!.OnSequenceFrame(this, color.Buffer, alpha?.Buffer, _frameDuration, _isFrameShown);
            Release(color);
            if (alpha is not null)
                Release(alpha);

            _nextFrame++;
            _hasFrameTiming = false;
            if (_isFrameShown)
                _shownFrames++;

            if (!proceed)
            {
                _result = CreateInfo(ImageIdentifyMode.FullScan);
                _state = State.Done;
                return true;
            }

            if (_context.TryConsumeYield())
                return true;
        }

        static void EnsureFragmentsAreWhole(AvifMovieBox movie)
        {
            if (movie.SampleCount is 0)
                throw AvifFormat.Invalid("the image sequence has no image.");

            if (movie.Alpha is { } alpha && alpha.SampleCount != movie.SampleCount)
                throw AvifFormat.Invalid("the alpha track does not have the number of images of the color track.");
        }

        Capture FindSample(AvifTrack track, int index)
        {
            foreach (var capture in _captures)
            {
                if (ReferenceEquals(capture.Track, track) && capture.SampleIndex == index)
                    return capture;
            }

            throw new InvalidOperationException("The sample was not scheduled.");
        }

        void Release(Capture capture)
        {
            _captures.Remove(capture);
            capture.Dispose();
        }
    }

    private sealed class Capture(AllocationScope scope, AvifItem? item, long offset, long length, bool isItemData, bool storesData = true) : IDisposable
    {
        private long _skippedLength;

        /// <summary>Gets the item that the extent belongs to, or <see langword="null"/> for a sample of an image sequence.</summary>
        public AvifItem? Item { get; } = item;

        /// <summary>Gets the track of a sample of an image sequence.</summary>
        public AvifTrack? Track { get; init; }

        /// <summary>Gets the index of a sample in its track.</summary>
        public int SampleIndex { get; init; }

        /// <summary>Gets the offset of the extent: in the file, or in the <c>idat</c> box.</summary>
        public long Offset { get; } = offset;

        public long Length { get; } = length;

        public bool IsItemData { get; } = isItemData;

        /// <summary>Gets the bytes of the extent; empty when the walk only verifies that they are present.</summary>
        public WebPPayloadBuffer Buffer { get; } = new(scope, storesData ? length : 0);

        /// <summary>Gets the number of bytes of the extent that went by.</summary>
        public long ReceivedLength => storesData ? Buffer.Length : _skippedLength;

        public bool IsComplete => ReceivedLength == Length;

        public void Append(ReadOnlySpan<byte> data)
        {
            if (storesData)
            {
                Buffer.Append(data);
            }
            else
            {
                _skippedLength += data.Length;
            }
        }

        public void Dispose() => Buffer.Dispose();
    }
}
