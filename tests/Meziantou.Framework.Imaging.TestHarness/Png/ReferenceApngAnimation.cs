namespace Meziantou.Framework.Imaging.TestHarness.Png;

/// <summary>The validated APNG control data read by <see cref="ReferencePng"/>.</summary>
/// <param name="NumFrames">The <c>acTL num_frames</c> field (displayed frames; a separate poster is not counted).</param>
/// <param name="NumPlays">The <c>acTL num_plays</c> field (0 = infinite).</param>
/// <param name="HasSeparatePoster">Whether the <c>IDAT</c> image has no preceding <c>fcTL</c> (a poster outside the animation).</param>
/// <param name="Frames">The frames in playback order.</param>
public sealed record ReferenceApngAnimation(int NumFrames, int NumPlays, bool HasSeparatePoster, IReadOnlyList<ReferenceApngFrame> Frames);
