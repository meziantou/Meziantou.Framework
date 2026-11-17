using Meziantou.Framework.Imaging.Metadata;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// Incremental DDS reader: the headers, then the surfaces, in the three walks of the codec infrastructure.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item><description>Header walk: reads the headers only (128 bytes, or 148 with a DX10 header).</description></item>
/// <item><description>
/// Full scan: reads the headers and every byte of the top surface, the image the identification describes; the surfaces
/// after it are not read (<see cref="DdsCollectionReader"/> checks that the file holds them all).
/// </description></item>
/// <item><description>
/// Decode: the top surface of the first face or element (the first depth slice of a volume texture), which is the first one
/// of the file, stored row by row (rows of 4×4 blocks for the block-compressed formats) and given to a
/// <see cref="DdsDecoder"/> as rows of pixels. The other surfaces are not read: <see cref="ImageCollection"/> reaches them.
/// </description></item>
/// </list>
/// </remarks>
internal sealed class DdsStructureParser : ImageParser<ImageInfo>
{
    private readonly ImageCodecContext _context;
    private readonly StructureWalk _walk;
    private readonly DdsDecoder? _decoder;
    private readonly int _mip;

    private State _state;
    private long _remaining;
    private int _storedRow;
    private DdsRowDecoder? _rows;
    private PooledBuffer? _band;
    private ImageInfo? _result;

    /// <summary>Creates a parser.</summary>
    /// <param name="context">The per-input context.</param>
    /// <param name="walk">The walk.</param>
    /// <param name="decoder">The decoder of a decode walk.</param>
    /// <param name="mip">
    /// The mipmap level of the surface a decode walk reads right after the headers: 0 for a file, another level for the
    /// data of a collection entry, which <see cref="DdsCollectionReader"/> puts after the headers.
    /// </param>
    public DdsStructureParser(ImageCodecContext context, StructureWalk walk, DdsDecoder? decoder = null, int mip = 0)
        : base(ImageFormat.Dds)
    {
        if (walk == StructureWalk.Decode)
        {
            ArgumentNullException.ThrowIfNull(decoder);
        }

        _context = context;
        _walk = walk;
        _decoder = walk == StructureWalk.Decode ? decoder : null;
        _mip = mip;
    }

    private enum State
    {
        Header,
        Scan,
        Rows,
        Done,
    }

    /// <summary>Gets the header, once it is read.</summary>
    public DdsHeader Header { get => field ?? throw new InvalidOperationException("The DDS header was not read."); private set; }

    /// <summary>Gets the metadata of the file: the labels its pixel format gives.</summary>
    public ImageMetadata Metadata { get => field ?? throw new InvalidOperationException("The DDS header was not read."); private set; }

    public override ParseStatus Parse(ReadOnlySpan<byte> buffer, bool isEndOfInput, out int consumed)
    {
        consumed = 0;
        while (true)
        {
            switch (_state)
            {
                case State.Header:
                {
                    if (buffer.Length < DdsFormat.HeaderLength)
                        return ParseStatus.NeedMoreData(DdsFormat.HeaderLength);

                    if (!DdsFormat.MatchesSignature(buffer))
                        throw DdsFormat.Invalid("The DDS magic number 'DDS ' is missing.");

                    var length = DdsHeader.GetLength(buffer);
                    if (buffer.Length < length)
                        return ParseStatus.NeedMoreData(length);

                    var header = DdsHeader.Parse(buffer[..length], _context);
                    Header = header;
                    Metadata = header.CreateMetadata();
                    consumed += length;
                    if (_walk == StructureWalk.Header)
                    {
                        _result = CreateInfo(ImageIdentifyMode.Header);
                        _state = State.Done;
                        return ParseStatus.Complete;
                    }

                    if (_walk == StructureWalk.FullScan)
                    {
                        _remaining = header.GetSurfaceLength(0);
                        _context.Tracker.ChargeScannedFrame();
                        _state = State.Scan;
                        break;
                    }

                    if (_mip >= header.MipCount)
                        throw new InvalidOperationException("The mipmap level is outside the texture.");

                    _context.Sequential?.OnHeader(CreateInfo(ImageIdentifyMode.Header));
                    _decoder!.SurfaceSize = new Size(header.GetWidth(_mip), header.GetHeight(_mip));
                    _decoder.OnHeaderComplete(this);
                    var rowLength = header.GetRowLength(_mip);
                    _rows = new DdsRowDecoder(header.Layout, header.GetWidth(_mip));
                    var bandLength = (long)_rows.SourceRowLength * _rows.RowsPerStoredRow;
                    if (rowLength > CheckedSizes.MaxBufferLength || bandLength > CheckedSizes.MaxBufferLength)
                        throw CheckedSizes.CreateOverflowException(_context.Limits);

                    _band = _context.Scope.Rent((int)bandLength, AllocationKind.DecoderState, clear: false);
                    _state = State.Rows;
                    if (_context.TryConsumeYield())
                        return ParseStatus.Complete;

                    break;
                }

                case State.Scan:
                {
                    var skip = (int)Math.Min(_remaining, buffer.Length - consumed);
                    consumed += skip;
                    _remaining -= skip;
                    if (_remaining > 0)
                        return ParseStatus.NeedMoreData(1);

                    _state = State.Done;
                    _result = CreateInfo(ImageIdentifyMode.FullScan);
                    return ParseStatus.Complete;
                }

                case State.Rows:
                {
                    var header = Header;
                    var rowLength = (int)header.GetRowLength(_mip);
                    var rowCount = header.GetRowCount(_mip);
                    var height = header.GetHeight(_mip);
                    var rows = _rows!;
                    while (_storedRow < rowCount)
                    {
                        if (buffer.Length - consumed < rowLength)
                            return ParseStatus.NeedMoreData(rowLength);

                        var band = _band!.RawBuffer.AsSpan(0, rows.SourceRowLength * rows.RowsPerStoredRow);
                        rows.Decode(buffer.Slice(consumed, rowLength), band);
                        consumed += rowLength;
                        var y = _storedRow * rows.RowsPerStoredRow;
                        var count = Math.Min(rows.RowsPerStoredRow, height - y);
                        _decoder!.WriteRows(y, count, band, rows.SourceRowLength);
                        _storedRow++;
                        _context.CancellationToken.ThrowIfCancellationRequested();
                    }

                    _state = State.Done;
                    _result = CreateInfo(ImageIdentifyMode.FullScan);
                    _decoder!.OnEnd(this);
                    return ParseStatus.Complete;
                }

                default:
                    return ParseStatus.Complete;
            }
        }
    }

    public override ImageInfo GetResult() => _result ?? throw new InvalidOperationException("The DDS data was not parsed.");

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _band?.Dispose();
            _band = null;
        }

        base.Dispose(disposing);
    }

    /// <summary>Describes the image the headers give: the top surface of the first face or element.</summary>
    public ImageInfo CreateInfo(ImageIdentifyMode mode)
    {
        var header = Header;
        var layout = header.Layout;
        var surfaces = header.SurfaceCount;
        return new ImageInfo(
            ImageFormat.Dds,
            header.Size,
            layout.DefaultPixelFormat,
            layout.ColorModel,
            layout.BitsPerComponent,
            frameCount: 1,
            isAnimated: false,
            hasPosterFrame: false,
            mayHaveTransparency: layout.HasAlpha,
            animation: null,
            Metadata,
            mode,
            collectionEntryCount: surfaces <= int.MaxValue ? (int)surfaces : null);
    }
}
