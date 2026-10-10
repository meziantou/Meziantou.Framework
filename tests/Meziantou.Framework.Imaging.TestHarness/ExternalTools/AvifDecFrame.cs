namespace Meziantou.Framework.Imaging.TestHarness.ExternalTools;

/// <summary>One <c>Decoded frame</c> line of <c>avifdec --info</c>.</summary>
/// <param name="Index">The index of the frame, from 0.</param>
/// <param name="PresentationTime">The presentation time, in units of the time scale.</param>
/// <param name="Duration">The duration, in units of the time scale.</param>
/// <param name="Width">The width of the decoded frame.</param>
/// <param name="Height">The height of the decoded frame.</param>
public sealed record AvifDecFrame(int Index, long PresentationTime, long Duration, int Width, int Height);
