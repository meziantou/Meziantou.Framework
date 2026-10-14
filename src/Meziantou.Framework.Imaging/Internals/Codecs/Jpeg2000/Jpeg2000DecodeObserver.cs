namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The sample-decoding side of JPEG 2000 loading. <see cref="Jpeg2000StructureParser"/> walks the file and the codestream
/// in <see cref="StructureWalk.Decode"/> mode (boxes, marker segments, metadata, limits) and collects the bytes of each
/// tile; the observer decodes the tiles and writes the image to a <see cref="DecodedFrameSink"/>.
/// </summary>
/// <remarks>
/// Call order: <see cref="OnHeaderComplete"/> once, when the main header of the codestream is read (the channels of the
/// image and the metadata that precedes the codestream are known), then <see cref="OnTile"/> once per tile, in the order
/// the tiles are completed by the codestream, then <see cref="OnEnd"/> at the end of the input. A tile is only valid
/// during the call.
/// </remarks>
internal abstract class Jpeg2000DecodeObserver : IImageDecodeObserver
{
    public abstract void OnHeaderComplete(Jpeg2000StructureParser structure);

    public abstract void OnTile(Jpeg2000StructureParser structure, Jpeg2000Tile tile);

    public virtual void OnEnd(Jpeg2000StructureParser structure)
    {
    }

    public abstract Image GetResult();

    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
    }
}
