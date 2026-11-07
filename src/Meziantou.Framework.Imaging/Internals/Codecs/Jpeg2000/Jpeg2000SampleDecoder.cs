using System.Runtime.InteropServices;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// Decodes a JPEG 2000 codestream that another format embeds, for a <see cref="Jpeg2000SampleReceiver"/>: the tiles are
/// decoded as for an image, and their samples are handed over as they are, component after component for each pixel.
/// </summary>
internal sealed class Jpeg2000SampleDecoder : Jpeg2000DecodeObserver
{
    private readonly ImageCodecContext _context;
    private readonly Jpeg2000SampleReceiver _receiver;
    private Jpeg2000TileDecoder? _tileDecoder;
    private PooledBuffer? _row;

    public Jpeg2000SampleDecoder(ImageCodecContext context, Jpeg2000SampleReceiver receiver)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(receiver);
        _context = context;
        _receiver = receiver;
    }

    public override void OnHeaderComplete(Jpeg2000StructureParser structure)
    {
        ArgumentNullException.ThrowIfNull(structure);
        var siz = structure.Siz;
        foreach (var component in siz.Components)
        {
            if (component.XSubsampling != 1 || component.YSubsampling != 1)
                throw Jpeg2000Format.Unsupported("The JPEG 2000 codestream has a sub-sampled component.", "sub-sampled component");
        }

        _receiver.OnImage((int)siz.Width, (int)siz.Height, siz.Components);
    }

    public override void OnTile(Jpeg2000StructureParser structure, Jpeg2000Tile tile)
    {
        ArgumentNullException.ThrowIfNull(structure);
        ArgumentNullException.ThrowIfNull(tile);
        var siz = structure.Siz;
        var coding = Jpeg2000TileCoding.Resolve(siz, structure.MainHeader, tile.Headers, structure.Capabilities);
        using var geometry = Jpeg2000TileGeometry.Create(_context.Scope, siz, coding, tile.Index);
        // One worker: the format that embeds the codestream decides the parallelism of its blocks
        _tileDecoder ??= new Jpeg2000TileDecoder(_context);
        structure.AddTools(_tileDecoder.Decode(siz, coding, geometry, tile));

        var width = (int)(geometry.X1 - geometry.X0);
        var height = (int)(geometry.Y1 - geometry.Y0);
        var left = (int)(geometry.X0 - siz.XOffset);
        var top = (int)(geometry.Y0 - siz.YOffset);
        var components = geometry.Components;
        var isDeep = false;
        foreach (var component in components)
        {
            isDeep |= component.Precision > 8;
        }

        var length = checked(width * components.Length * (isDeep ? sizeof(ushort) : 1));
        if (_row is null || _row.Length < length)
        {
            _row?.Dispose();
            _row = null;
            _row = _context.Scope.Rent(length, AllocationKind.DecoderState, clear: false);
        }

        var row = _row.RawBuffer.AsSpan(0, length);
        var words = unsafe(MemoryMarshal.Cast<byte, ushort>(row));
        for (var y = 0; y < height; y++)
        {
            if ((y & 63) == 0)
            {
                _context.CancellationToken.ThrowIfCancellationRequested();
            }

            for (var c = 0; c < components.Length; c++)
            {
                var samples = components[c].Samples.Slice(y * width, width);
                if (isDeep)
                {
                    for (var x = 0; x < width; x++)
                    {
                        words[(x * components.Length) + c] = (ushort)samples[x];
                    }
                }
                else
                {
                    for (var x = 0; x < width; x++)
                    {
                        row[(x * components.Length) + c] = (byte)samples[x];
                    }
                }
            }

            _receiver.OnRow(left, top + y, row);
        }
    }

    public override Image GetResult() => throw new NotSupportedException("An embedded JPEG 2000 codestream gives samples, not an image.");

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _tileDecoder?.Dispose();
            _tileDecoder = null;
            _row?.Dispose();
            _row = null;
        }

        base.Dispose(disposing);
    }
}
