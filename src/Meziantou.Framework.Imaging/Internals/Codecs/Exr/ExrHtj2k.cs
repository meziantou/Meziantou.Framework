using System.Buffers.Binary;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// Decodes the chunks of the HTJ2K256 and HTJ2K32 compressions (section 14 of <c>ExrCompression.md</c>): a small header
/// that maps the components of a JPEG 2000 codestream to the channels, then the codestream, which the JPEG 2000 decoder
/// decodes as the compressed data of another format.
/// </summary>
/// <remarks>
/// Only HALF channels are decoded: their samples are 16-bit signed components whose negative values went through the
/// binary complement of the NLT marker segment (type 3, ISO/IEC 15444-2). The 32-bit components of FLOAT and UINT
/// channels need more magnitude bit-planes than the block decoder has, and are rejected with the header
/// (<see cref="ExrHeader"/>).
/// </remarks>
internal sealed class ExrHtj2k : Jpeg2000DecodeObserver
{
    /// <summary>The magic number of the chunks of HTJ2K256 and HTJ2K32 (<c>'H' 'T'</c>).</summary>
    public const ushort Magic = 0x4854;

    private const int HalfPrecision = 16;

    private readonly ImageCodecContext _context;
    private Jpeg2000TileDecoder? _tileDecoder;
    private PooledBuffer? _scratch;
    private int[] _map = [];
    private int[] _lineStarts = [];
    private ExrChunkLayout _layout;

    public ExrHtj2k(ImageCodecContext context)
    {
        _context = context;
    }

    /// <summary>Decompresses a chunk.</summary>
    /// <param name="stored">The stored data of the chunk.</param>
    /// <param name="destination">Receives the uncompressed data.</param>
    /// <param name="layout">The geometry of the chunk.</param>
    /// <exception cref="InvalidImageContentException">The data are malformed.</exception>
    /// <exception cref="UnsupportedImageFeatureException">The codestream uses a feature that is not supported.</exception>
    public void Decompress(ReadOnlySpan<byte> stored, Span<byte> destination, in ExrChunkLayout layout)
    {
        var channels = layout.Channels;
        var codestream = ReadHeader(stored, channels.Count);
        _layout = layout;
        PrepareLines(layout);
        if (_scratch is null || _scratch.Length < destination.Length)
        {
            _scratch?.Dispose();
            _scratch = null;
            _scratch = _context.Scope.Rent(Math.Max(destination.Length, 1), AllocationKind.DecoderState, clear: false);
        }

        // Every sample of the chunk is written by the components, which cover their channels
        try
        {
            using var parser = new Jpeg2000StructureParser(_context, StructureWalk.Decode, this, isEmbedded: true, allowNonLinearity: true);
            if (!parser.Parse(codestream, isEndOfInput: true, out _).IsComplete)
                throw ExrFormat.Invalid("the JPEG 2000 codestream of an HTJ2K chunk is truncated.");
        }
        catch (InvalidImageContentException exception) when (exception.Format == ImageFormat.Jpeg2000)
        {
            throw new InvalidImageContentException("The OpenEXR file is invalid: the JPEG 2000 codestream of an HTJ2K chunk is malformed: " + exception.Message, ImageFormat.OpenExr, exception);
        }
        catch (UnsupportedImageFeatureException exception) when (exception.Format == ImageFormat.Jpeg2000)
        {
            throw ExrFormat.Unsupported("The JPEG 2000 codestream of an HTJ2K chunk is not supported: " + exception.Message, "HTJ2K: " + exception.Feature);
        }

        _scratch.Span[..destination.Length].CopyTo(destination);
    }

    public override void OnHeaderComplete(Jpeg2000StructureParser structure)
    {
        ArgumentNullException.ThrowIfNull(structure);
        var siz = structure.Siz;
        var layout = _layout;
        var channels = layout.Channels;

        // The reference writes the chunk as an image at the origin of the reference grid, of the size of the chunk
        if (siz.XOffset != 0 || siz.YOffset != 0 || siz.Width != layout.Width || siz.Height != layout.Lines)
            throw ExrFormat.Invalid(string.Create(CultureInfo.InvariantCulture, $"the JPEG 2000 codestream of an HTJ2K chunk is a {siz.Width}x{siz.Height} image at ({siz.XOffset}, {siz.YOffset}); the chunk is {layout.Width}x{layout.Lines}."));

        if (siz.Components.Length != channels.Count)
            throw ExrFormat.Invalid(string.Create(CultureInfo.InvariantCulture, $"the JPEG 2000 codestream of an HTJ2K chunk has {siz.Components.Length} components; the part has {channels.Count} channels."));

        if (structure.HasTileNonLinearity)
            throw ExrFormat.Unsupported("The JPEG 2000 codestream of an HTJ2K chunk has a non-linearity point transformation in a tile-part header.", "HTJ2K: tile NLT marker segment");

        for (var i = 0; i < siz.Components.Length; i++)
        {
            var component = siz.Components[i];
            var channel = channels[_map[i]];
            if (channel.PixelType != ExrPixelType.Half)
                throw ExrFormat.Unsupported($"The HTJ2K chunk has a channel of {channel.PixelType} samples; only HALF channels are supported.", "HTJ2K: 32-bit channels");

            if (component.Precision != HalfPrecision || !component.IsSigned)
                throw ExrFormat.Invalid(string.Create(CultureInfo.InvariantCulture, $"the component {i} of an HTJ2K chunk has {(component.IsSigned ? "signed" : "unsigned")} samples of {component.Precision} bits; the HALF channel '{channel.Name}' needs signed samples of 16 bits."));

            // The component starts at the first line of the chunk: its lines are those of the channel when the chunk starts on one
            if (component.XSubsampling != channel.XSampling || component.YSubsampling != channel.YSampling || layout.Y0 % channel.YSampling != 0
                || Jpeg2000Siz.CeilingDivide(siz.Width, component.XSubsampling) != layout.GetSampleCount(_map[i])
                || Jpeg2000Siz.CeilingDivide(siz.Height, component.YSubsampling) != layout.GetLineCount(_map[i]))
            {
                throw ExrFormat.Invalid(string.Create(CultureInfo.InvariantCulture, $"the component {i} of an HTJ2K chunk is sub-sampled by {component.XSubsampling}x{component.YSubsampling}; it does not hold the samples of the channel '{channel.Name}' in the chunk."));
            }

            switch (structure.GetNonLinearity(i))
            {
                case null:
                    break;

                case { Type: Jpeg2000NonLinearity.BinaryComplement } nonLinearity:
                    if (nonLinearity.Precision != HalfPrecision || !nonLinearity.IsSigned)
                        throw ExrFormat.Invalid(string.Create(CultureInfo.InvariantCulture, $"the NLT marker segment of the component {i} of an HTJ2K chunk is for samples of {nonLinearity.Precision} bits."));

                    break;

                case { } nonLinearity:
                    throw ExrFormat.Unsupported(string.Create(CultureInfo.InvariantCulture, $"The component {i} of an HTJ2K chunk has a non-linearity point transformation of type {nonLinearity.Type}; only the binary complement (type 3) is supported."), string.Create(CultureInfo.InvariantCulture, $"HTJ2K: NLT type {nonLinearity.Type}"));
            }
        }
    }

    public override void OnTile(Jpeg2000StructureParser structure, Jpeg2000Tile tile)
    {
        ArgumentNullException.ThrowIfNull(structure);
        ArgumentNullException.ThrowIfNull(tile);
        var siz = structure.Siz;
        var coding = Jpeg2000TileCoding.Resolve(siz, structure.MainHeader, tile.Headers, structure.Capabilities);
        using var geometry = Jpeg2000TileGeometry.Create(_context.Scope, siz, coding, tile.Index);

        // One worker: the chunks are decoded by the workers of the OpenEXR decoder
        _tileDecoder ??= new Jpeg2000TileDecoder(_context, allQualityLayers: true);
        structure.AddTools(_tileDecoder.Decode(siz, coding, geometry, tile));

        var layout = _layout;
        var data = _scratch!.Span;
        var components = geometry.Components;
        for (var i = 0; i < components.Length; i++)
        {
            var component = components[i];
            var channelIndex = _map[i];
            var ySampling = layout.Channels[channelIndex].YSampling;
            var complement = structure.GetNonLinearity(i) is { Type: Jpeg2000NonLinearity.BinaryComplement };
            var width = component.Width;
            var samples = component.Samples;
            for (var y = 0; y < component.Height; y++)
            {
                var line = (int)(component.Y0 + y) * ySampling;
                var offset = GetChannelOffset(layout, line, channelIndex) + ((int)component.X0 * sizeof(ushort));
                var row = samples.Slice(y * width, width);
                var target = data.Slice(offset, width * sizeof(ushort));
                for (var x = 0; x < row.Length; x++)
                {
                    // The level shift added half the range of the signed samples
                    var value = row[x] - (1 << (HalfPrecision - 1));
                    if (complement && value < 0)
                    {
                        value = -value - (1 << (HalfPrecision - 1)) - 1;
                    }

                    BinaryPrimitives.WriteUInt16LittleEndian(target[(x * sizeof(ushort))..], (ushort)value);
                }
            }
        }

        _context.CancellationToken.ThrowIfCancellationRequested();
    }

    public override Image GetResult() => throw new NotSupportedException("An HTJ2K chunk gives samples, not an image.");

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _tileDecoder?.Dispose();
            _tileDecoder = null;
            _scratch?.Dispose();
            _scratch = null;
        }

        base.Dispose(disposing);
    }

    /// <summary>Reads the header of a chunk (section 14.1) into the channel map, and gets the codestream that follows it.</summary>
    private ReadOnlySpan<byte> ReadHeader(ReadOnlySpan<byte> stored, int channelCount)
    {
        if (stored.Length < 8)
            throw ExrFormat.Invalid("an HTJ2K chunk is shorter than its header.");

        if (BinaryPrimitives.ReadUInt16BigEndian(stored) != Magic)
            throw ExrFormat.Invalid("an HTJ2K chunk does not start with its magic number.");

        var payloadLength = BinaryPrimitives.ReadUInt32BigEndian(stored[2..]);
        if (payloadLength > (uint)(stored.Length - 6))
            throw ExrFormat.Invalid("the header of an HTJ2K chunk is longer than the chunk.");

        var payload = stored.Slice(6, (int)payloadLength);
        if (payload.Length < 2)
            throw ExrFormat.Invalid("the header of an HTJ2K chunk has no channel map.");

        var count = BinaryPrimitives.ReadUInt16BigEndian(payload);
        if (count != channelCount)
            throw ExrFormat.Invalid(string.Create(CultureInfo.InvariantCulture, $"the channel map of an HTJ2K chunk has {count} entries; the part has {channelCount} channels."));

        if (payload.Length < 2 + (2 * count))
            throw ExrFormat.Invalid("the channel map of an HTJ2K chunk is longer than its header.");

        if (_map.Length != count)
        {
            _map = new int[count];
        }

        // The map is a permutation of the channels
        Span<bool> seen = count <= 256 ? stackalloc bool[count] : new bool[count];
        seen.Clear();
        for (var i = 0; i < count; i++)
        {
            var channel = BinaryPrimitives.ReadUInt16BigEndian(payload[(2 + (2 * i))..]);
            if (channel >= count || seen[channel])
                throw ExrFormat.Invalid("the channel map of an HTJ2K chunk is not a permutation of the channels.");

            seen[channel] = true;
            _map[i] = channel;
        }

        var codestream = stored[(6 + payload.Length)..];
        if (codestream.IsEmpty)
            throw ExrFormat.Invalid("an HTJ2K chunk has no codestream.");

        return codestream;
    }

    /// <summary>Computes where each line of the uncompressed data of the chunk starts (section 1.2).</summary>
    private void PrepareLines(in ExrChunkLayout layout)
    {
        if (_lineStarts.Length < layout.Lines)
        {
            _lineStarts = new int[layout.Lines];
        }

        var position = 0;
        for (var line = 0; line < layout.Lines; line++)
        {
            _lineStarts[line] = position;
            for (var c = 0; c < layout.Channels.Count; c++)
            {
                if (layout.HasLine(c, line))
                {
                    position += layout.Channels[c].BytesPerSample * layout.GetSampleCount(c);
                }
            }
        }
    }

    /// <summary>Gets the position of the samples of a channel in a line of the uncompressed data.</summary>
    private int GetChannelOffset(in ExrChunkLayout layout, int line, int channel)
    {
        var offset = _lineStarts[line];
        for (var c = 0; c < channel; c++)
        {
            if (layout.HasLine(c, line))
            {
                offset += layout.Channels[c].BytesPerSample * layout.GetSampleCount(c);
            }
        }

        return offset;
    }
}
