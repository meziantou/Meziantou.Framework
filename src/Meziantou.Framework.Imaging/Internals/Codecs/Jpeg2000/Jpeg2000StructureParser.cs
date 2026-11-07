using System.Buffers.Binary;
using Meziantou.Framework.Imaging.Metadata;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// Incremental JPEG 2000 walker: a codestream (T.800, Annex A), or the boxes of a JP2 file (T.800, Annex I) whose
/// contiguous codestream box is given to a <see cref="Jpeg2000CodestreamWalker"/> in whatever slices arrive. It never
/// decodes samples: it validates the structure and the headers, collects the metadata, and in
/// <see cref="StructureWalk.Decode"/> mode hands each complete tile to a <see cref="Jpeg2000DecodeObserver"/>.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item><description>
/// Header walk: stops once the main header of the codestream is read; in a file, the boxes that precede the codestream
/// are read first.
/// </description></item>
/// <item><description>
/// Full scan: also reads the header of every tile-part, skips their packets, requires the EOC marker and reads every box.
/// </description></item>
/// <item><description>Decode: like a full scan, but the bytes of each tile are collected and given to the observer.</description></item>
/// </list>
/// <para>
/// Box rules: the signature box then the file type box come first; the file type names the brand <c>jp2 </c>,
/// <c>jpx </c> or <c>jph </c>, as its brand or as a compatible one; the JP2 header box precedes the codestream box; a
/// codestream header box or a compositing layer header box of a JPX file replaces the boxes of the JP2 header it
/// holds; the first <c>uuid</c> box of an XMP packet and the first one of EXIF data are metadata, adopted when valid;
/// every other box is skipped. A box whose size is 0 extends to the end of the input.
/// </para>
/// </remarks>
internal sealed class Jpeg2000StructureParser : ImageParser<ImageInfo>
{
    private const int MaxFileTypeLength = 64 * 1024;
    private const int UuidLength = 16;

    private readonly ImageCodecContext _context;
    private readonly StructureWalk _walk;
    private readonly Jpeg2000DecodeObserver? _observer;
    private readonly DecodedMetadataBuilder _metadata;
    private readonly Jpeg2000CodestreamWalker _walker;
    private readonly bool _isEmbedded;

    private State _state;
    private bool _isFile;
    private bool _fileTypeSeen;
    private bool _codestreamSeen;
    private bool _codestreamHeaderSeen;
    private bool _layerHeaderSeen;
    private bool _exifSeen;
    private bool _xmpSeen;
    private Jpeg2000HeaderBoxes? _boxes;
    private Jpeg2000ChannelLayout? _layout;
    private Jpeg2000Tools _tools;
    private ImageInfo? _result;

    // Box being streamed
    private BoxKind _boxKind;
    private long _boxRemaining;
    private bool _boxExtendsToEnd;
    private JpegXlCodestreamBuffer? _boxBuffer;

    /// <param name="context">The state of the operation.</param>
    /// <param name="walk">How far the input is walked.</param>
    /// <param name="observer">The decoder of the tiles, for <see cref="StructureWalk.Decode"/>.</param>
    /// <param name="isEmbedded">
    /// <see langword="true"/> when the codestream is the compressed data of another format (a strip or a tile of a TIFF
    /// page), which says what its components are: the boxes of a file are then not interpreted, no channel layout is
    /// resolved and the observer receives the components as they are.
    /// </param>
    public Jpeg2000StructureParser(ImageCodecContext context, StructureWalk walk, Jpeg2000DecodeObserver? observer = null, bool isEmbedded = false)
        : base(ImageFormat.Jpeg2000)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (walk == StructureWalk.Decode)
        {
            ArgumentNullException.ThrowIfNull(observer);
        }

        _context = context;
        _walk = walk;
        _observer = walk == StructureWalk.Decode ? observer : null;
        _metadata = new DecodedMetadataBuilder(context.Tracker, ImageFormat.Jpeg2000);
        _walker = new Jpeg2000CodestreamWalker(context.Scope, collect: walk == StructureWalk.Decode);
        _isEmbedded = isEmbedded;
    }

    private enum State
    {
        Start,
        BoxHeader,
        Payload,
        Done,
    }

    private enum BoxKind
    {
        Skip,
        Codestream,
        Header,
        Exif,
        Xmp,
    }

    /// <summary>Gets the SIZ marker segment of the codestream.</summary>
    public Jpeg2000Siz Siz => _walker.Siz ?? throw new InvalidOperationException("The JPEG 2000 main header was not parsed.");

    /// <summary>Gets the parameters of the main header of the codestream.</summary>
    public Jpeg2000HeaderSet MainHeader => _walker.MainHeader;

    /// <summary>Gets the CAP marker segment of the codestream, or <see langword="null"/>.</summary>
    public Jpeg2000Capabilities? Capabilities => _walker.Capabilities;

    /// <summary>Gets the channels of the image, once the main header is read.</summary>
    public Jpeg2000ChannelLayout Layout => _layout ?? throw new InvalidOperationException("The JPEG 2000 main header was not parsed.");

    /// <summary>Gets the size of the image: the image area of the reference grid.</summary>
    public Size Size => new((int)Siz.Width, (int)Siz.Height);

    /// <summary>Gets the metadata collected so far.</summary>
    public ImageMetadata Metadata => _metadata.Metadata;

    /// <summary>Gets the features the file used so far.</summary>
    public Jpeg2000Tools Tools => _tools | _walker.Tools;

    /// <summary>Records features that the decoding of a tile used.</summary>
    public void AddTools(Jpeg2000Tools tools) => _tools |= tools;

    public override ParseStatus Parse(ReadOnlySpan<byte> buffer, bool isEndOfInput, out int consumed)
    {
        consumed = 0;
        while (true)
        {
            var remaining = buffer[consumed..];
            ParseStatus? status;
            switch (_state)
            {
                case State.Start:
                    status = ProcessStart(remaining, ref consumed);
                    break;

                case State.BoxHeader:
                    status = ProcessBoxHeader(remaining, isEndOfInput, ref consumed);
                    break;

                case State.Payload:
                    status = _boxKind switch
                    {
                        BoxKind.Codestream => ProcessCodestream(remaining, ref consumed),
                        BoxKind.Skip => ProcessSkippedBox(remaining, ref consumed),
                        _ => ProcessBufferedBox(remaining, isEndOfInput, ref consumed),
                    };
                    break;

                default:
                    return ParseStatus.Complete;
            }

            if (status is { } result)
                return result;
        }
    }

    public override ImageInfo GetResult() => _result ?? throw new InvalidOperationException("The JPEG 2000 structure was not parsed.");

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _walker.Dispose();
            _boxBuffer?.Dispose();
            _boxBuffer = null;
        }

        base.Dispose(disposing);
    }

    private static InvalidImageContentException Invalid(string message) => Jpeg2000Format.Invalid(message);

    private bool TryYield() => _walk == StructureWalk.Decode && _context.TryConsumeYield();

    private ParseStatus? ProcessStart(ReadOnlySpan<byte> remaining, ref int consumed)
    {
        var codestream = Jpeg2000Format.CodestreamSignature;
        if (remaining.Length < codestream.Length)
            return ParseStatus.NeedMoreData(codestream.Length);

        if (remaining.StartsWith(codestream))
        {
            // A codestream that is not in a file: it ends with its EOC marker
            _isFile = false;
            _boxKind = BoxKind.Codestream;
            _boxRemaining = long.MaxValue;
            _boxExtendsToEnd = true;
            _codestreamSeen = true;
            _state = State.Payload;
            return null;
        }

        var signature = Jpeg2000Format.FileSignature;
        if (remaining.Length < signature.Length)
            return ParseStatus.NeedMoreData(signature.Length);

        if (!remaining.StartsWith(signature))
            throw Invalid("the data starts with neither a codestream nor the signature box.");

        consumed += signature.Length;
        _isFile = true;
        _state = State.BoxHeader;
        return null;
    }

    private ParseStatus? ProcessBoxHeader(ReadOnlySpan<byte> remaining, bool isEndOfInput, ref int consumed)
    {
        if (remaining.Length < Jpeg2000Format.BoxHeaderLength)
        {
            if (!_walker.IsComplete)
                return ParseStatus.NeedMoreData(Jpeg2000Format.BoxHeaderLength);

            // The codestream is complete: the boxes end with the input
            if (remaining.IsEmpty && isEndOfInput)
                return Finish();

            return isEndOfInput ? ParseStatus.NeedMoreData(Jpeg2000Format.BoxHeaderLength) : ParseStatus.NeedMoreDataOrEnd(Jpeg2000Format.BoxHeaderLength);
        }

        var size = BinaryPrimitives.ReadUInt32BigEndian(remaining);
        var type = BinaryPrimitives.ReadUInt32BigEndian(remaining[4..]);
        var headerLength = Jpeg2000Format.BoxHeaderLength;
        long payloadLength;
        var extendsToEnd = false;
        if (size == 1)
        {
            headerLength = Jpeg2000Format.LargeBoxHeaderLength;
            if (remaining.Length < headerLength)
                return ParseStatus.NeedMoreData(headerLength);

            var largeSize = BinaryPrimitives.ReadUInt64BigEndian(remaining[8..]);
            if (largeSize < (ulong)headerLength || largeSize > long.MaxValue)
                throw Invalid("a box has a size that does not hold its header.");

            payloadLength = (long)largeSize - headerLength;
        }
        else if (size == 0)
        {
            extendsToEnd = true;
            payloadLength = long.MaxValue;
        }
        else if (size < headerLength)
        {
            throw Invalid("a box has a size that does not hold its header.");
        }
        else
        {
            payloadLength = size - headerLength;
        }

        if (!_fileTypeSeen)
            return ProcessFileType(remaining, type, payloadLength, extendsToEnd, headerLength, ref consumed);

        switch (type)
        {
            case Jpeg2000Format.CodestreamBox:
                if (_codestreamSeen)
                    throw Jpeg2000Format.Unsupported("The JPEG 2000 file has several codestreams.", "several codestreams");

                if (_boxes is null && !_isEmbedded)
                    throw Invalid("the codestream box is not preceded by a JP2 header box.");

                _codestreamSeen = true;
                return BeginBox(BoxKind.Codestream, payloadLength, extendsToEnd, headerLength, ref consumed);

            case Jpeg2000Format.HeaderBox:
                if (_boxes is not null)
                    throw Invalid("the file has two JP2 header boxes.");

                if (_codestreamSeen)
                    throw Invalid("the JP2 header box follows the codestream box.");

                _boxes = new Jpeg2000HeaderBoxes();
                return BeginBufferedBox(BoxKind.Header, payloadLength, extendsToEnd, headerLength, ref consumed);

            case Jpeg2000Format.CodestreamHeaderBox or Jpeg2000Format.CompositingLayerHeaderBox when !_codestreamSeen:
                if (_boxes is null)
                    throw Invalid("a header box of the extended file format is not preceded by a JP2 header box.");

                if (type == Jpeg2000Format.CodestreamHeaderBox ? _codestreamHeaderSeen : _layerHeaderSeen)
                    throw Jpeg2000Format.Unsupported("The JPEG 2000 file has several codestreams or compositing layers.", "several codestreams");

                if (type == Jpeg2000Format.CodestreamHeaderBox)
                {
                    _codestreamHeaderSeen = true;
                }
                else
                {
                    _layerHeaderSeen = true;
                }

                return BeginBufferedBox(BoxKind.Header, payloadLength, extendsToEnd, headerLength, ref consumed);

            case Jpeg2000Format.FragmentTableBox when !_codestreamSeen:
                throw Jpeg2000Format.Unsupported("The codestream of the JPEG 2000 file is stored in fragments.", "fragmented codestream");

            case Jpeg2000Format.UuidBox when payloadLength >= UuidLength:
            {
                if (remaining.Length < headerLength + UuidLength)
                    return ParseStatus.NeedMoreData(headerLength + UuidLength);

                var identifier = remaining.Slice(headerLength, UuidLength);
                var kind = BoxKind.Skip;
                if (!_xmpSeen && identifier.SequenceEqual(Jpeg2000Format.XmpUuid))
                {
                    _xmpSeen = true;
                    kind = BoxKind.Xmp;
                }
                else if (!_exifSeen && identifier.SequenceEqual(Jpeg2000Format.ExifUuid))
                {
                    _exifSeen = true;
                    kind = BoxKind.Exif;
                }

                if (kind == BoxKind.Skip)
                    return BeginBox(BoxKind.Skip, payloadLength, extendsToEnd, headerLength, ref consumed);

                return BeginBufferedBox(kind, extendsToEnd ? payloadLength : payloadLength - UuidLength, extendsToEnd, headerLength + UuidLength, ref consumed);
            }

            default:
                return BeginBox(BoxKind.Skip, payloadLength, extendsToEnd, headerLength, ref consumed);
        }
    }

    private ParseStatus? ProcessFileType(ReadOnlySpan<byte> remaining, uint type, long payloadLength, bool extendsToEnd, int headerLength, ref int consumed)
    {
        // The file type box follows the signature box: the brand, the minor version, then the compatible brands
        if (type != Jpeg2000Format.FileTypeBox || extendsToEnd || payloadLength < 8 || payloadLength % 4 != 0)
            throw Invalid("the signature box is not followed by a file type box.");

        if (payloadLength > MaxFileTypeLength)
            throw Invalid("the file type box is too large.");

        var length = headerLength + (int)payloadLength;
        if (remaining.Length < length)
            return ParseStatus.NeedMoreData(length);

        var isSupported = false;
        var other = 0u;
        var brands = remaining[headerLength..length];
        for (var offset = 0; offset < brands.Length; offset += 4)
        {
            // The second field is the minor version, not a brand
            if (offset == 4)
                continue;

            var brand = BinaryPrimitives.ReadUInt32BigEndian(brands[offset..]);
            if (brand is Jpeg2000Format.BrandJp2 or Jpeg2000Format.BrandJpx or Jpeg2000Format.BrandJph)
            {
                isSupported = true;
            }
            else if (other == 0 && brand is Jpeg2000Format.BrandJpm or Jpeg2000Format.BrandMj2 or Jpeg2000Format.BrandMj2Simple)
            {
                other = brand;
            }
        }

        if (!isSupported)
        {
            var brand = other != 0 ? other : BinaryPrimitives.ReadUInt32BigEndian(brands);
            throw Jpeg2000Format.Unsupported($"The file is of the JPEG 2000 family with the brand '{Jpeg2000Format.ToText(brand)}'; still images with the brands 'jp2 ', 'jpx ' and 'jph ' are supported.", $"brand '{Jpeg2000Format.ToText(brand)}'");
        }

        _fileTypeSeen = true;
        consumed += length;
        _state = State.BoxHeader;
        return null;
    }

    private ParseStatus? BeginBufferedBox(BoxKind kind, long payloadLength, bool extendsToEnd, int headerLength, ref int consumed)
    {
        if (!extendsToEnd)
        {
            if (payloadLength > CheckedSizes.MaxBufferLength)
                throw CheckedSizes.CreateOverflowException(_context.Limits);

            // Metadata is retained as it is: charge it before buffering it
            if (kind != BoxKind.Header)
            {
                _metadata.Charge(payloadLength);
            }
        }

        _boxBuffer?.Dispose();
        _boxBuffer = new JpegXlCodestreamBuffer(_context.Scope);
        return BeginBox(kind, payloadLength, extendsToEnd, headerLength, ref consumed);
    }

    private ParseStatus? BeginBox(BoxKind kind, long payloadLength, bool extendsToEnd, int headerLength, ref int consumed)
    {
        consumed += headerLength;
        _boxKind = kind;
        _boxRemaining = payloadLength;
        _boxExtendsToEnd = extendsToEnd;
        _state = State.Payload;
        return null;
    }

    private ParseStatus? ProcessSkippedBox(ReadOnlySpan<byte> remaining, ref int consumed)
    {
        if (_boxExtendsToEnd)
        {
            // The last box of the file: nothing of it is read
            if (!_walker.IsComplete)
                throw Invalid("the file ends before its codestream.");

            return Finish();
        }

        if (_boxRemaining == 0)
        {
            _state = State.BoxHeader;
            return null;
        }

        if (remaining.IsEmpty)
            return ParseStatus.NeedMoreData(1);

        var count = (int)Math.Min(remaining.Length, _boxRemaining);
        consumed += count;
        _boxRemaining -= count;
        return null;
    }

    private ParseStatus? ProcessBufferedBox(ReadOnlySpan<byte> remaining, bool isEndOfInput, ref int consumed)
    {
        var buffer = _boxBuffer!;
        var isMetadata = _boxKind != BoxKind.Header;
        var count = (int)Math.Min(remaining.Length, _boxRemaining);
        if (count > 0)
        {
            if (_boxExtendsToEnd && isMetadata)
            {
                _metadata.Charge(count);
            }

            buffer.Append(remaining[..count]);
            consumed += count;
            if (!_boxExtendsToEnd)
            {
                _boxRemaining -= count;
            }
        }

        if (_boxExtendsToEnd)
        {
            if (!isEndOfInput)
                return ParseStatus.NeedMoreDataOrEnd(1);
        }
        else if (_boxRemaining > 0)
        {
            return ParseStatus.NeedMoreData(1);
        }

        switch (_boxKind)
        {
            case BoxKind.Header:
                if (!_isEmbedded)
                {
                    _boxes!.Read(buffer.Span, _metadata);
                }

                break;

            case BoxKind.Exif:
                _metadata.TryAdoptExif(buffer.Span, adoptOrientation: true, alreadyCharged: true);
                break;

            case BoxKind.Xmp:
                _metadata.TryAdoptXmp(buffer.Span, alreadyCharged: true);
                break;
        }

        buffer.Dispose();
        _boxBuffer = null;
        if (_boxExtendsToEnd)
        {
            if (!_walker.IsComplete)
                throw Invalid("the file ends before its codestream.");

            return Finish();
        }

        _state = State.BoxHeader;
        return null;
    }

    private ParseStatus? ProcessCodestream(ReadOnlySpan<byte> remaining, ref int consumed)
    {
        if (_walker.IsComplete)
        {
            // A codestream ends with its EOC marker; in a file, the rest of the box is ignored and the other boxes are read
            if (!_isFile)
                return Finish();

            _boxKind = BoxKind.Skip;
            return null;
        }

        var piece = remaining[..(int)Math.Min(remaining.Length, _boxRemaining)];
        var streamEvent = _walker.Feed(piece, out var taken, out var needed);
        consumed += taken;
        if (!_boxExtendsToEnd)
        {
            _boxRemaining -= taken;
        }

        switch (streamEvent)
        {
            case Jpeg2000CodestreamEvent.ImageSize:
                OnImageSize();
                return null;

            case Jpeg2000CodestreamEvent.MainHeader:
                return OnMainHeader();

            case Jpeg2000CodestreamEvent.Tile:
                OnTile();
                return null;

            case Jpeg2000CodestreamEvent.End:
                if (_walk != StructureWalk.Decode)
                {
                    _context.Tracker.ChargeScannedFrame();
                }

                return null;
        }

        // The walker needs more bytes than it was given: they must be in the box
        if (needed > _boxRemaining)
            throw Invalid("the codestream box ends before its codestream.");

        return ParseStatus.NeedMoreData(needed);
    }

    private void OnImageSize()
    {
        var siz = Siz;
        _context.Limits.EnsureCanvasWithinLimits(siz.Width, siz.Height);
        Jpeg2000FeaturePolicy.EnsureSupported(siz);
    }

    private ParseStatus? OnMainHeader()
    {
        var siz = Siz;
        Jpeg2000FeaturePolicy.EnsureSupportedCapabilities(siz, Capabilities);
        if (_isEmbedded)
        {
            // The format that embeds the codestream says what the components are
            _observer?.OnHeaderComplete(this);
            return null;
        }

        if (_boxes is { } boxes)
        {
            _layout = Jpeg2000FeaturePolicy.CreateLayout(siz, boxes);
            _metadata.TrySetResolution(boxes.Resolution);
            if (boxes.Palette is not null)
            {
                _tools |= Jpeg2000Tools.Palette;
            }

            if (boxes.Definitions is not null)
            {
                _tools |= Jpeg2000Tools.ChannelDefinition;
            }
        }
        else
        {
            _layout = Jpeg2000FeaturePolicy.CreateLayout(siz);
        }

        // The bytes of the profile were charged when the color specification box was read
        if (_layout.Profile is { } profile)
        {
            _metadata.TryAdoptIccProfile(profile, _layout.ProfileColorSpace);
        }

        if (_walk == StructureWalk.Header)
        {
            _result = CreateInfo(ImageIdentifyMode.Header);
            _state = State.Done;
            return ParseStatus.Complete;
        }

        if (_walk == StructureWalk.Decode)
        {
            _context.Sequential?.OnHeader(CreateInfo(ImageIdentifyMode.Header));
            _observer!.OnHeaderComplete(this);
            if (TryYield())
                return ParseStatus.Complete;
        }

        return null;
    }

    private void OnTile()
    {
        using var tile = _walker.CompletedTile ?? throw new InvalidOperationException("The JPEG 2000 codestream walker completed no tile.");
        _observer!.OnTile(this, tile);
    }

    private ParseStatus Finish()
    {
        _state = State.Done;
        if (!_isEmbedded)
        {
            _result = CreateInfo(ImageIdentifyMode.FullScan);
        }

        _observer?.OnEnd(this);
        return ParseStatus.Complete;
    }

    private ImageInfo CreateInfo(ImageIdentifyMode mode)
    {
        var layout = Layout;
        return new ImageInfo(
            ImageFormat.Jpeg2000,
            Size,
            layout.DefaultPixelFormat,
            layout.ColorModel,
            layout.BitsPerComponent,
            frameCount: 1,
            isAnimated: false,
            hasPosterFrame: false,
            mayHaveTransparency: layout.Alpha is not null,
            animation: null,
            _metadata.Metadata,
            mode);
    }
}
