namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// A decoder that can show the frame it is decoding when the input ends before the frame does: what it decoded, as an
/// intermediate rendering (<see cref="DecodedFrameSink.EndRefinement"/>), before the reader reports the truncation.
/// </summary>
internal interface IPartialFrameRenderer
{
    /// <summary>Writes what was decoded of the frame being decoded to the sink, and ends it as an intermediate rendering.</summary>
    /// <returns><see langword="false"/> when no frame is being decoded, or nothing of it can be shown.</returns>
    bool TryRenderPartialFrame();
}
