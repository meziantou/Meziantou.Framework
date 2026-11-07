namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The pixel-decoding side of PNG/APNG loading (static decoding and APNG compositing). <see cref="PngStructureParser"/>
/// walks the chunks in <see cref="StructureWalk.Decode"/> mode (order, lengths, CRCs, sequence numbers, frame bounds,
/// metadata, limits) and calls these hooks; the observer inflates the delivered payloads (<see cref="PngImageDataDecoder"/>)
/// and builds the image with a <see cref="DecodedFrameSink"/>. <see cref="PngDecoder"/> is the static decoder.
/// </summary>
/// <remarks>
/// Call order: <see cref="OnHeaderComplete"/> once (the first <c>IDAT</c> is reached; every preceding chunk was parsed),
/// then for each image (static image, separate poster, or APNG frame): <see cref="OnImageStart"/>, any number of
/// <see cref="OnImageData"/> calls (raw zlib bytes of <c>IDAT</c>/<c>fdAT</c> chunks, sequence numbers removed, CRCs
/// already verified), and <see cref="OnImageEnd"/>; finally <see cref="OnEnd"/> at <c>IEND</c>. Returning
/// <see langword="false"/> from <see cref="OnImageEnd"/> stops the walk immediately (frame-limit prefix selection: the
/// rest of the data is not examined). Payload spans are only valid during the call.
/// </remarks>
internal abstract class PngDecodeObserver : IImageDecodeObserver
{
    public abstract void OnHeaderComplete(PngStructureParser structure);

    /// <param name="control">The APNG frame control, or <see langword="null"/> for a static image or a separate poster (<see cref="PngStructureParser.HasSeparatePoster"/>).</param>
    public virtual void OnImageStart(PngFrameControl? control)
    {
    }

    public virtual void OnImageData(ReadOnlySpan<byte> compressed)
    {
    }

    /// <returns><see langword="true"/> to continue the walk; <see langword="false"/> to stop (frame limit reached).</returns>
    public virtual bool OnImageEnd() => true;

    public virtual void OnEnd(PngStructureParser structure)
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
