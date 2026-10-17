namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The pixel-decoding side of GIF loading. <see cref="GifStructureParser"/> walks the blocks in
/// <see cref="StructureWalk.Decode"/> mode (header, color tables, extensions, bounded sub-blocks, limits) and calls these
/// hooks; the observer (<see cref="GifDecoder"/>) decodes LZW data, composites full-canvas frames and writes them to a <see cref="DecodedFrameSink"/>.
/// </summary>
/// <remarks>
/// Call order: <see cref="OnHeaderComplete"/> once (the first image descriptor is reached; the logical screen, global color
/// table and preceding extensions were parsed), then per image: <see cref="OnImageStart"/>, <see cref="OnImageDataBlocks"/>
/// for each run of data sub-blocks available together (by default <see cref="OnImageData"/> for each sub-block, without its
/// length byte), <see cref="OnImageEnd"/>; finally <see cref="OnEnd"/> at the trailer.
/// Returning <see langword="false"/> from <see cref="OnImageEnd"/> stops the walk (frame-limit prefix selection). Spans are
/// only valid during the call.
/// </remarks>
internal abstract class GifDecodeObserver : IImageDecodeObserver
{
    public abstract void OnHeaderComplete(GifStructureParser structure);

    /// <param name="descriptor">The image descriptor.</param>
    /// <param name="localColorTable">The local color table (RGB triplets), or empty.</param>
    /// <param name="control">The Graphic Control Extension preceding the image, if any.</param>
    /// <param name="lzwMinimumCodeSize">The LZW minimum code size (1 to 11).</param>
    public virtual void OnImageStart(GifImageDescriptor descriptor, ReadOnlySpan<byte> localColorTable, GifGraphicControl? control, int lzwMinimumCodeSize)
    {
    }

    public virtual void OnImageData(ReadOnlySpan<byte> subBlock)
    {
    }

    /// <summary>
    /// Receives consecutive complete data sub-blocks of the current image, each with its length byte (none of them is the
    /// terminator). By default, <see cref="OnImageData"/> is called for each of them.
    /// </summary>
    public virtual void OnImageDataBlocks(ReadOnlySpan<byte> subBlocks)
    {
        while (!subBlocks.IsEmpty)
        {
            var size = subBlocks[0];
            OnImageData(subBlocks.Slice(1, size));
            subBlocks = subBlocks[(1 + size)..];
        }
    }

    /// <returns><see langword="true"/> to continue the walk; <see langword="false"/> to stop (frame limit reached).</returns>
    public virtual bool OnImageEnd() => true;

    public virtual void OnEnd(GifStructureParser structure)
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
