using System.Buffers.Binary;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// Incremental walker of a JPEG 2000 codestream (T.800, Annex A): the main header, then the tile-parts, then the EOC
/// marker. It is fed the codestream in whatever slices arrive and asks for a whole marker segment at a time; the bytes
/// that follow a SOD marker are streamed.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item><description>
/// Marker segments: SIZ comes first; COD and QCD are required in the main header; COD, COC, QCD, QCC and RGN are only
/// allowed in the first tile-part header of a tile; a header has at most one of each per component and at most one POC.
/// TLM, PLM, PLT, CRG, COM and CPF are skipped, like the marker segments this Recommendation does not define.
/// </description></item>
/// <item><description>
/// Tile-parts: those of a tile come in order, possibly separated by tile-parts of other tiles; every tile has at least one;
/// a tile-part whose length is 0 extends to the EOC marker, which is required.
/// </description></item>
/// <item><description>
/// When it collects (decoding), the walker keeps the bytes of each tile until the tile is complete: its last tile-part
/// when the number of tile-parts is known, the EOC marker otherwise. A complete tile is handed over with
/// <see cref="Jpeg2000CodestreamEvent.Tile"/>.
/// </description></item>
/// </list>
/// </remarks>
internal sealed class Jpeg2000CodestreamWalker : IDisposable
{
    private const int MaxTileParts = 255;

    private readonly AllocationScope _scope;
    private readonly bool _collect;
    private State _state;
    private Jpeg2000HeaderSet? _main;
    private Av1PooledArray<ushort>? _parts;
    private Dictionary<int, Jpeg2000Tile>? _tiles;
    private int _startedTiles;
    private int _lastTileIndex = -1;

    // Packed packet headers of the main header (PPM)
    private JpegXlCodestreamBuffer? _packedHeaders;
    private int _packedHeadersOffset;
    private int _nextPackedHeaderIndex;
    private bool _hasPackedHeaders;

    // Tile-part being walked
    private int _tileIndex;
    private bool _isFirstPart;
    private long _partLength;
    private long _partConsumed;
    private long _bodyRemaining;
    private bool _pendingMarkerPrefix;
    private int _nextTilePackedHeaderIndex;
    private bool _progressionSeen;
    private Jpeg2000Tile? _currentTile;
    private Jpeg2000HeaderSet? _currentHeaders;

    public Jpeg2000CodestreamWalker(AllocationScope scope, bool collect)
    {
        ArgumentNullException.ThrowIfNull(scope);
        _scope = scope;
        _collect = collect;
    }

    private enum State
    {
        Start,
        MainHeader,
        TilePartStart,
        TilePartHeader,
        Body,
        Draining,
        Done,
    }

    /// <summary>Gets the SIZ marker segment, once it is read.</summary>
    public Jpeg2000Siz? Siz { get; private set; }

    /// <summary>Gets the CAP marker segment, or <see langword="null"/>.</summary>
    public Jpeg2000Capabilities? Capabilities { get; private set; }

    /// <summary>Gets the parameters of the main header.</summary>
    public Jpeg2000HeaderSet MainHeader => _main ?? throw new InvalidOperationException("The SIZ marker segment was not read.");

    /// <summary>Gets a value indicating whether the EOC marker was read.</summary>
    public bool IsComplete => _state is State.Draining or State.Done;

    /// <summary>Gets the features the codestream used so far.</summary>
    public Jpeg2000Tools Tools { get; private set; }

    /// <summary>Gets the tile of the last <see cref="Jpeg2000CodestreamEvent.Tile"/> event. The caller disposes it.</summary>
    public Jpeg2000Tile? CompletedTile { get; private set; }

    /// <summary>Feeds the next bytes of the codestream.</summary>
    /// <param name="data">The bytes that follow the ones consumed so far.</param>
    /// <param name="consumed">Receives the number of bytes consumed.</param>
    /// <param name="needed">
    /// When the result is <see cref="Jpeg2000CodestreamEvent.None"/>, receives the number of bytes the walker needs after the
    /// consumed ones to continue.
    /// </param>
    /// <returns>The event that stopped the walk.</returns>
    public Jpeg2000CodestreamEvent Feed(ReadOnlySpan<byte> data, out int consumed, out int needed)
    {
        consumed = 0;
        needed = 0;
        while (true)
        {
            var rest = data[consumed..];
            switch (_state)
            {
                case State.Start:
                    if (rest.Length < 2)
                    {
                        needed = 2;
                        return Jpeg2000CodestreamEvent.None;
                    }

                    if (BinaryPrimitives.ReadUInt16BigEndian(rest) != Jpeg2000Format.MarkerSoc)
                        throw Invalid("the codestream does not start with the SOC marker.");

                    consumed += 2;
                    _state = State.MainHeader;
                    break;

                case State.MainHeader:
                {
                    if (rest.Length < 2)
                    {
                        needed = 2;
                        return Jpeg2000CodestreamEvent.None;
                    }

                    if (BinaryPrimitives.ReadUInt16BigEndian(rest) == Jpeg2000Format.MarkerSot)
                    {
                        // The first tile-part ends the main header; its marker segment is read by the next state
                        CompleteMainHeader();
                        _state = State.TilePartStart;
                        return Jpeg2000CodestreamEvent.MainHeader;
                    }

                    if (!TryReadSegment(rest, out var marker, out var length, ref needed))
                        return Jpeg2000CodestreamEvent.None;

                    var isSiz = marker == Jpeg2000Format.MarkerSiz;
                    ProcessMainSegment(marker, rest[Math.Min(4, length)..length]);
                    consumed += length;
                    if (isSiz)
                        return Jpeg2000CodestreamEvent.ImageSize;

                    break;
                }

                case State.TilePartStart:
                    if (!TryBeginTilePart(rest, ref consumed, ref needed))
                        return Jpeg2000CodestreamEvent.None;

                    break;

                case State.TilePartHeader:
                {
                    if (rest.Length < 2)
                    {
                        needed = 2;
                        return Jpeg2000CodestreamEvent.None;
                    }

                    if (BinaryPrimitives.ReadUInt16BigEndian(rest) == Jpeg2000Format.MarkerSod)
                    {
                        consumed += 2;
                        _partConsumed += 2;
                        CompleteTilePartHeader();
                        _state = State.Body;
                        break;
                    }

                    if (!TryReadSegment(rest, out var marker, out var length, ref needed))
                        return Jpeg2000CodestreamEvent.None;

                    ProcessTileSegment(marker, rest[Math.Min(4, length)..length]);
                    consumed += length;
                    _partConsumed += length;
                    break;
                }

                case State.Body:
                {
                    if (_bodyRemaining < 0)
                    {
                        if (!TryReadBodyToEnd(rest, ref consumed))
                        {
                            needed = 1;
                            return Jpeg2000CodestreamEvent.None;
                        }

                        // The EOC marker was consumed
                        _state = State.Draining;
                        if (EndTilePart())
                            return Jpeg2000CodestreamEvent.Tile;

                        break;
                    }

                    if (_bodyRemaining > 0)
                    {
                        if (rest.IsEmpty)
                        {
                            needed = 1;
                            return Jpeg2000CodestreamEvent.None;
                        }

                        var count = (int)Math.Min(rest.Length, _bodyRemaining);
                        _currentTile?.Body.Append(rest[..count]);
                        consumed += count;
                        _bodyRemaining -= count;
                        break;
                    }

                    _state = State.TilePartStart;
                    if (EndTilePart())
                        return Jpeg2000CodestreamEvent.Tile;

                    break;
                }

                case State.Draining:
                    if (TryTakeTile())
                        return Jpeg2000CodestreamEvent.Tile;

                    EnsureEveryTileIsComplete();
                    _state = State.Done;
                    return Jpeg2000CodestreamEvent.End;

                default:
                    return Jpeg2000CodestreamEvent.End;
            }
        }
    }

    public void Dispose()
    {
        _parts?.Dispose();
        _parts = null;
        _packedHeaders?.Dispose();
        _packedHeaders = null;
        CompletedTile?.Dispose();
        CompletedTile = null;
        _currentTile?.Dispose();
        _currentTile = null;
        if (_tiles is not null)
        {
            foreach (var tile in _tiles.Values)
            {
                tile.Dispose();
            }

            _tiles = null;
        }
    }

    private static InvalidImageContentException Invalid(string message) => Jpeg2000Format.Invalid(message);

    /// <summary>Reads the marker and the length of a marker segment, and waits for the whole segment.</summary>
    /// <param name="rest">The bytes at hand.</param>
    /// <param name="marker">Receives the marker.</param>
    /// <param name="length">Receives the length of the marker segment, with its marker.</param>
    /// <param name="needed">Receives the number of bytes to wait for when the segment is not complete.</param>
    private static bool TryReadSegment(ReadOnlySpan<byte> rest, out ushort marker, out int length, ref int needed)
    {
        marker = 0;
        length = 0;
        if (rest.Length < 4)
        {
            needed = 4;
            return false;
        }

        marker = BinaryPrimitives.ReadUInt16BigEndian(rest);
        if (marker < 0xFF01)
            throw Invalid(string.Create(CultureInfo.InvariantCulture, $"a marker is expected where the bytes {rest[0]:X2} {rest[1]:X2} are."));

        if (marker is >= 0xFF30 and <= 0xFF3F)
        {
            // The markers of this range have no parameter (A.1.3)
            length = 2;
            return true;
        }

        var segmentLength = BinaryPrimitives.ReadUInt16BigEndian(rest[2..]);
        if (segmentLength < 2)
            throw Invalid(string.Create(CultureInfo.InvariantCulture, $"the marker segment {marker:X4} has a length that does not hold its length field."));

        length = 2 + segmentLength;
        if (rest.Length < length)
        {
            needed = length;
            return false;
        }

        return true;
    }

    private void ProcessMainSegment(ushort marker, ReadOnlySpan<byte> content)
    {
        if (Siz is null)
        {
            if (marker != Jpeg2000Format.MarkerSiz)
                throw Invalid("the SOC marker is not followed by the SIZ marker segment.");

            Siz = Jpeg2000MarkerReader.ReadSiz(content);
            _main = new Jpeg2000HeaderSet(Siz.Components.Length);
            return;
        }

        var main = _main!;
        var componentCount = Siz.Components.Length;
        switch (marker)
        {
            case Jpeg2000Format.MarkerSiz:
                throw Invalid("the main header has two SIZ marker segments.");

            case Jpeg2000Format.MarkerCap:
                if (Capabilities is not null)
                    throw Invalid("the main header has two CAP marker segments.");

                Capabilities = Jpeg2000Capabilities.Read(content);
                break;

            case Jpeg2000Format.MarkerCod:
                if (main.CodingDefaults is not null)
                    throw Invalid("the main header has two COD marker segments.");

                main.CodingDefaults = Jpeg2000MarkerReader.ReadCod(content);
                break;

            case Jpeg2000Format.MarkerCoc:
            {
                var style = Jpeg2000MarkerReader.ReadCoc(content, componentCount, out var component);
                if (!main.TrySetComponentStyle(component, style))
                    throw Invalid(string.Create(CultureInfo.InvariantCulture, $"the main header has two COC marker segments for the component {component}."));

                break;
            }

            case Jpeg2000Format.MarkerQcd:
                if (main.Quantization is not null)
                    throw Invalid("the main header has two QCD marker segments.");

                main.Quantization = Jpeg2000MarkerReader.ReadQcd(content);
                break;

            case Jpeg2000Format.MarkerQcc:
            {
                var quantization = Jpeg2000MarkerReader.ReadQcc(content, componentCount, out var component);
                if (!main.TrySetComponentQuantization(component, quantization))
                    throw Invalid(string.Create(CultureInfo.InvariantCulture, $"the main header has two QCC marker segments for the component {component}."));

                break;
            }

            case Jpeg2000Format.MarkerRgn:
            {
                var shift = Jpeg2000MarkerReader.ReadRgn(content, componentCount, out var component);
                if (!main.TrySetRegionShift(component, shift))
                    throw Invalid(string.Create(CultureInfo.InvariantCulture, $"the main header has two RGN marker segments for the component {component}."));

                break;
            }

            case Jpeg2000Format.MarkerPoc:
                if (main.Progressions is not null)
                    throw Invalid("the main header has two POC marker segments.");

                Jpeg2000MarkerReader.ReadPoc(content, componentCount, main.GetOrCreateProgressions());
                break;

            case Jpeg2000Format.MarkerPpm:
                if (content.IsEmpty || content[0] != _nextPackedHeaderIndex)
                    throw Invalid("the PPM marker segments of the main header are not in order.");

                _nextPackedHeaderIndex++;
                _hasPackedHeaders = true;
                Tools |= Jpeg2000Tools.PackedHeadersMain;
                if (_collect)
                {
                    _packedHeaders ??= new JpegXlCodestreamBuffer(_scope);
                    _packedHeaders.Append(content[1..]);
                }

                break;

            case Jpeg2000Format.MarkerTlm:
                Tools |= Jpeg2000Tools.TilePartLengths;
                break;

            case Jpeg2000Format.MarkerSod or Jpeg2000Format.MarkerEoc or Jpeg2000Format.MarkerSoc or Jpeg2000Format.MarkerSop or Jpeg2000Format.MarkerEph or Jpeg2000Format.MarkerPlt or Jpeg2000Format.MarkerPpt:
                throw Invalid(string.Create(CultureInfo.InvariantCulture, $"the marker {marker:X4} is not allowed in the main header."));
        }
    }

    private void CompleteMainHeader()
    {
        var siz = Siz ?? throw Invalid("the main header has no SIZ marker segment.");
        var main = _main!;
        if (main.CodingDefaults is null)
            throw Invalid("the main header has no COD marker segment.");

        if (main.Quantization is null)
            throw Invalid("the main header has no QCD marker segment.");

        // The parameters of the main header apply to every tile that does not override them: check them once
        Tools |= Jpeg2000TileCoding.Resolve(siz, main, tile: null, Capabilities).Tools;
        if (siz.TileCount > 1)
        {
            Tools |= Jpeg2000Tools.Tiles;
        }

        if (siz.XOffset != 0 || siz.YOffset != 0 || siz.TileXOffset != 0 || siz.TileYOffset != 0)
        {
            Tools |= Jpeg2000Tools.Offsets;
        }

        foreach (var component in siz.Components)
        {
            if (component.XSubsampling != 1 || component.YSubsampling != 1)
            {
                Tools |= Jpeg2000Tools.Subsampling;
            }
        }

        // For each tile: the number of tile-parts read (low byte) and the number its tile-parts declare (high byte)
        _parts = Av1PooledArray<ushort>.Rent(_scope, siz.TileCount);
        if (_collect)
        {
            _tiles = [];
        }
    }

    private bool TryBeginTilePart(ReadOnlySpan<byte> rest, ref int consumed, ref int needed)
    {
        if (rest.Length < 2)
        {
            needed = 2;
            return false;
        }

        var marker = BinaryPrimitives.ReadUInt16BigEndian(rest);
        if (marker == Jpeg2000Format.MarkerEoc)
        {
            consumed += 2;
            _state = State.Draining;
            return true;
        }

        if (marker != Jpeg2000Format.MarkerSot)
            throw Invalid(string.Create(CultureInfo.InvariantCulture, $"a tile-part or the end of the codestream is expected where the bytes {rest[0]:X2} {rest[1]:X2} are."));

        if (rest.Length < Jpeg2000Format.SotSegmentLength)
        {
            needed = Jpeg2000Format.SotSegmentLength;
            return false;
        }

        if (BinaryPrimitives.ReadUInt16BigEndian(rest[2..]) != Jpeg2000Format.SotSegmentLength - 2)
            throw Invalid("the length of a SOT marker segment is not the one of its fields.");

        var siz = Siz!;
        int tileIndex = BinaryPrimitives.ReadUInt16BigEndian(rest[4..]);
        long partLength = BinaryPrimitives.ReadUInt32BigEndian(rest[6..]);
        int partIndex = rest[10];
        int partCount = rest[11];
        if (tileIndex >= siz.TileCount)
            throw Invalid(string.Create(CultureInfo.InvariantCulture, $"a tile-part belongs to the tile {tileIndex}; the image has {siz.TileCount} tiles."));

        var parts = _parts!.Span;
        var seen = parts[tileIndex] & 0xFF;
        var declared = parts[tileIndex] >> 8;
        if (partIndex != seen)
            throw Invalid(string.Create(CultureInfo.InvariantCulture, $"the tile-part {partIndex} of the tile {tileIndex} is not in order: the tile-part {seen} is expected."));

        if (declared != 0 && partCount != 0 && partCount != declared)
            throw Invalid(string.Create(CultureInfo.InvariantCulture, $"the tile-parts of the tile {tileIndex} do not agree on their number."));

        if (declared == 0)
        {
            declared = partCount;
        }

        if (partIndex >= MaxTileParts || (declared != 0 && partIndex >= declared))
            throw Invalid(string.Create(CultureInfo.InvariantCulture, $"the tile {tileIndex} has more tile-parts than it declares."));

        // A tile-part holds at least its SOT marker segment and its SOD marker
        if (partLength != 0 && partLength < Jpeg2000Format.SotSegmentLength + 2)
            throw Invalid("the length of a tile-part does not hold its header.");

        parts[tileIndex] = (ushort)(seen | (declared << 8));
        if (partIndex == 0)
        {
            _startedTiles++;
        }
        else
        {
            Tools |= Jpeg2000Tools.TileParts;
            if (_lastTileIndex != tileIndex)
            {
                Tools |= Jpeg2000Tools.InterleavedTileParts;
            }
        }

        _lastTileIndex = tileIndex;
        _tileIndex = tileIndex;
        _isFirstPart = partIndex == 0;
        _partLength = partLength;
        _partConsumed = Jpeg2000Format.SotSegmentLength;
        _nextTilePackedHeaderIndex = 0;
        _progressionSeen = false;
        _currentTile = null;
        _currentHeaders = null;
        if (_collect)
        {
            var tiles = _tiles!;
            if (_isFirstPart)
            {
                _currentTile = new Jpeg2000Tile(tileIndex, _scope, siz.Components.Length);
                tiles.Add(tileIndex, _currentTile);
            }
            else
            {
                _currentTile = tiles[tileIndex];
            }

            _currentHeaders = _currentTile.Headers;
        }
        else
        {
            _currentHeaders = new Jpeg2000HeaderSet(siz.Components.Length);
        }

        consumed += Jpeg2000Format.SotSegmentLength;
        _state = State.TilePartHeader;
        return true;
    }

    private void ProcessTileSegment(ushort marker, ReadOnlySpan<byte> content)
    {
        var headers = _currentHeaders!;
        var componentCount = Siz!.Components.Length;
        switch (marker)
        {
            case Jpeg2000Format.MarkerCod:
                EnsureFirstTilePart("COD");
                if (headers.CodingDefaults is not null)
                    throw Invalid("a tile-part header has two COD marker segments.");

                headers.CodingDefaults = Jpeg2000MarkerReader.ReadCod(content);
                break;

            case Jpeg2000Format.MarkerCoc:
            {
                EnsureFirstTilePart("COC");
                var style = Jpeg2000MarkerReader.ReadCoc(content, componentCount, out var component);
                if (!headers.TrySetComponentStyle(component, style))
                    throw Invalid(string.Create(CultureInfo.InvariantCulture, $"a tile-part header has two COC marker segments for the component {component}."));

                break;
            }

            case Jpeg2000Format.MarkerQcd:
                EnsureFirstTilePart("QCD");
                if (headers.Quantization is not null)
                    throw Invalid("a tile-part header has two QCD marker segments.");

                headers.Quantization = Jpeg2000MarkerReader.ReadQcd(content);
                break;

            case Jpeg2000Format.MarkerQcc:
            {
                EnsureFirstTilePart("QCC");
                var quantization = Jpeg2000MarkerReader.ReadQcc(content, componentCount, out var component);
                if (!headers.TrySetComponentQuantization(component, quantization))
                    throw Invalid(string.Create(CultureInfo.InvariantCulture, $"a tile-part header has two QCC marker segments for the component {component}."));

                break;
            }

            case Jpeg2000Format.MarkerRgn:
            {
                EnsureFirstTilePart("RGN");
                var shift = Jpeg2000MarkerReader.ReadRgn(content, componentCount, out var component);
                if (!headers.TrySetRegionShift(component, shift))
                    throw Invalid(string.Create(CultureInfo.InvariantCulture, $"a tile-part header has two RGN marker segments for the component {component}."));

                break;
            }

            case Jpeg2000Format.MarkerPoc:
                if (_progressionSeen)
                    throw Invalid("a tile-part header has two POC marker segments.");

                // The progressions of the tile-parts of a tile follow each other
                _progressionSeen = true;
                Jpeg2000MarkerReader.ReadPoc(content, componentCount, headers.GetOrCreateProgressions());
                break;

            case Jpeg2000Format.MarkerPpt:
                if (_hasPackedHeaders)
                    throw Invalid("the packet headers are both in the main header and in a tile-part header.");

                if (content.IsEmpty || content[0] != _nextTilePackedHeaderIndex)
                    throw Invalid("the PPT marker segments of a tile-part header are not in order.");

                _nextTilePackedHeaderIndex++;
                Tools |= Jpeg2000Tools.PackedHeadersTile;
                if (_currentTile is { } tile)
                {
                    tile.HasTilePackedHeaders = true;
                    tile.AppendPackedHeaders(content[1..]);
                }

                break;

            case Jpeg2000Format.MarkerPlt:
                Tools |= Jpeg2000Tools.PacketLengths;
                break;

            case Jpeg2000Format.MarkerSiz or Jpeg2000Format.MarkerCap or Jpeg2000Format.MarkerCpf or Jpeg2000Format.MarkerTlm or Jpeg2000Format.MarkerPlm or Jpeg2000Format.MarkerPpm or Jpeg2000Format.MarkerCrg
                or Jpeg2000Format.MarkerSot or Jpeg2000Format.MarkerEoc or Jpeg2000Format.MarkerSoc or Jpeg2000Format.MarkerSop or Jpeg2000Format.MarkerEph:
                throw Invalid(string.Create(CultureInfo.InvariantCulture, $"the marker {marker:X4} is not allowed in a tile-part header."));
        }
    }

    private void EnsureFirstTilePart(string marker)
    {
        if (!_isFirstPart)
            throw Invalid($"a {marker} marker segment is in a tile-part header that is not the first one of its tile.");
    }

    private void CompleteTilePartHeader()
    {
        if (_partLength == 0)
        {
            _bodyRemaining = -1;
            _pendingMarkerPrefix = false;
            Tools |= Jpeg2000Tools.TilePartToEnd;
        }
        else
        {
            if (_partConsumed > _partLength)
                throw Invalid("the header of a tile-part is longer than the tile-part.");

            _bodyRemaining = _partLength - _partConsumed;
        }

        if (_isFirstPart)
        {
            // A tile whose parameters are wrong is rejected before its packets are read
            Tools |= Jpeg2000TileCoding.Resolve(Siz!, _main!, _currentHeaders, Capabilities).Tools;
        }

        if (_hasPackedHeaders && _currentTile is { } tile)
        {
            // The headers of the packets of each tile-part follow each other in the PPM marker segments, each run
            // preceded by its length (A.7.4)
            var packed = _packedHeaders!.Span[_packedHeadersOffset..];
            if (packed.Length < 4)
                throw Invalid("the PPM marker segments hold the packet headers of fewer tile-parts than the codestream has.");

            var length = BinaryPrimitives.ReadUInt32BigEndian(packed);
            if (length > packed.Length - 4)
                throw Invalid("the PPM marker segments end before the packet headers of a tile-part.");

            tile.AppendPackedHeaders(packed.Slice(4, (int)length));
            _packedHeadersOffset += 4 + (int)length;
        }
    }

    /// <summary>Streams the bytes of a tile-part that extends to the EOC marker.</summary>
    /// <returns><see langword="true"/> once the EOC marker is consumed.</returns>
    private bool TryReadBodyToEnd(ReadOnlySpan<byte> rest, ref int consumed)
    {
        // No two bytes of the packets form a marker above FF8F: the first FF D9 is the EOC marker. An FF byte that ends a
        // slice is held back until the byte that follows it is known.
        if (rest.IsEmpty)
            return false;

        if (_pendingMarkerPrefix)
        {
            _pendingMarkerPrefix = false;
            if (rest[0] == (Jpeg2000Format.MarkerEoc & 0xFF))
            {
                consumed++;
                return true;
            }

            _currentTile?.Body.Append([0xFF]);
        }

        var end = rest.IndexOf([(byte)0xFF, (byte)(Jpeg2000Format.MarkerEoc & 0xFF)]);
        if (end >= 0)
        {
            _currentTile?.Body.Append(rest[..end]);
            consumed += end + 2;
            return true;
        }

        if (rest[^1] == 0xFF)
        {
            _pendingMarkerPrefix = true;
            _currentTile?.Body.Append(rest[..^1]);
        }
        else
        {
            _currentTile?.Body.Append(rest);
        }

        consumed += rest.Length;
        return false;
    }

    /// <summary>Records the end of the current tile-part.</summary>
    /// <returns><see langword="true"/> if the tile is complete and handed over as <see cref="CompletedTile"/>.</returns>
    private bool EndTilePart()
    {
        var parts = _parts!.Span;
        var seen = (parts[_tileIndex] & 0xFF) + 1;
        var declared = parts[_tileIndex] >> 8;
        parts[_tileIndex] = (ushort)(seen | (declared << 8));
        _currentTile = null;
        _currentHeaders = null;
        if (declared == 0)
        {
            Tools |= Jpeg2000Tools.UnknownTilePartCount;
            return false;
        }

        if (seen != declared)
            return false;

        if (_tiles is null || !_tiles.Remove(_tileIndex, out var tile))
            return false;

        CompletedTile?.Dispose();
        CompletedTile = tile;
        return true;
    }

    /// <summary>Takes one of the tiles that only the EOC marker completes.</summary>
    private bool TryTakeTile()
    {
        if (_tiles is null || _tiles.Count == 0)
            return false;

        var parts = _parts!.Span;
        var index = -1;
        foreach (var key in _tiles.Keys)
        {
            if (index < 0 || key < index)
            {
                index = key;
            }
        }

        // A tile that declares its number of tile-parts and is still here misses some of them
        if ((parts[index] >> 8) != 0)
            throw Invalid(string.Create(CultureInfo.InvariantCulture, $"the tile {index} has fewer tile-parts than it declares."));

        _tiles.Remove(index, out var tile);
        CompletedTile?.Dispose();
        CompletedTile = tile;
        return true;
    }

    private void EnsureEveryTileIsComplete()
    {
        var siz = Siz!;
        if (_startedTiles != siz.TileCount)
            throw Invalid(string.Create(CultureInfo.InvariantCulture, $"the codestream has the tile-parts of {_startedTiles} tiles; the image has {siz.TileCount} tiles."));

        var parts = _parts!.Span;
        for (var i = 0; i < parts.Length; i++)
        {
            var declared = parts[i] >> 8;
            if (declared != 0 && (parts[i] & 0xFF) != declared)
                throw Invalid(string.Create(CultureInfo.InvariantCulture, $"the tile {i} has fewer tile-parts than it declares."));
        }
    }
}
