namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The pixel-decoding side of JPEG XL loading. <see cref="JpegXlStructureParser"/> walks the container and the codestream
/// in <see cref="StructureWalk.Decode"/> mode (boxes, headers, metadata, limits) and buffers the sections of each frame;
/// the observer decodes them and writes the displayed frames to a <see cref="DecodedFrameSink"/>.
/// </summary>
/// <remarks>
/// Call order: <see cref="OnHeaderComplete"/> once, before the first frame (the image header, the color profile and the
/// metadata boxes that precede the frames are known), then <see cref="OnFrame"/> once per coded frame with its complete
/// sections, then <see cref="OnEnd"/> at the end of the input. Returning <see langword="false"/> from <see cref="OnFrame"/>
/// stops the walk (frame-limit prefix selection). The sections are only valid during the call.
/// </remarks>
internal abstract class JpegXlDecodeObserver : IImageDecodeObserver
{
    public abstract void OnHeaderComplete(JpegXlStructureParser structure);

    /// <param name="structure">The structure parser.</param>
    /// <param name="frame">The header of the frame.</param>
    /// <param name="toc">The table of contents of the frame.</param>
    /// <param name="sections">The sections of the frame, one after the other.</param>
    /// <returns><see langword="true"/> to continue the walk; <see langword="false"/> to stop (frame limit reached).</returns>
    public abstract bool OnFrame(JpegXlStructureParser structure, JpegXlFrameHeader frame, JpegXlToc toc, WebPPayloadBuffer sections);

    public virtual void OnEnd(JpegXlStructureParser structure)
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
