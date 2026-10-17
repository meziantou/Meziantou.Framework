namespace Meziantou.Framework.Imaging.TestHarness.Jpeg2000;

/// <summary>An entry of the channel definition box of a JP2 file (<c>cdef</c>).</summary>
/// <param name="Channel">The index of the channel.</param>
/// <param name="Type">The type: 0 color, 1 opacity, 2 premultiplied opacity, 65535 unspecified.</param>
/// <param name="Association">The color the channel is associated with: 0 the whole image, 1 the first color, and so on; 65535 none.</param>
public sealed record ReferenceJp2Channel(int Channel, int Type, int Association);
