namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The pixel-decoding side of WebP loading. <see cref="WebPStructureParser"/> walks the RIFF chunks in
/// <see cref="StructureWalk.Decode"/> mode (container rules, frame control data, metadata, limits) and buffers the payloads of
/// each image; the observer (<see cref="WebPDecoder"/>) decodes them and writes full-canvas frames to a <see cref="DecodedFrameSink"/>.
/// </summary>
/// <remarks>
/// Call order: <see cref="OnHeaderComplete"/> once (at the bitstream chunk of a still image, or the first <c>ANMF</c> chunk of an
/// animation; the canvas, the color profile and the animation parameters are known), then <see cref="OnFrame"/> once per image
/// with its complete payloads, then <see cref="OnEnd"/> at the end of the RIFF data. Returning <see langword="false"/> from
/// <see cref="OnFrame"/> stops the walk (frame-limit prefix selection). Payloads are only valid during the call.
/// </remarks>
internal abstract class WebPDecodeObserver : IImageDecodeObserver
{
    public abstract void OnHeaderComplete(WebPStructureParser structure);

    /// <param name="frame">The frame control data.</param>
    /// <param name="alpha">The <c>ALPH</c> payload of a VP8 frame, or <see langword="null"/>.</param>
    /// <param name="bitstream">The VP8 or VP8L payload.</param>
    /// <returns><see langword="true"/> to continue the walk; <see langword="false"/> to stop (frame limit reached).</returns>
    public abstract bool OnFrame(WebPFrameInfo frame, WebPPayloadBuffer? alpha, WebPPayloadBuffer bitstream);

    public virtual void OnEnd(WebPStructureParser structure)
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
