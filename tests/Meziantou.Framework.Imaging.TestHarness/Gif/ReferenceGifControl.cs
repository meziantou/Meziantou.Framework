namespace Meziantou.Framework.Imaging.TestHarness.Gif;

/// <summary>The fields of a Graphic Control Extension read by <see cref="ReferenceGif"/>.</summary>
/// <param name="DisposalMethod">The raw disposal method (0-7).</param>
/// <param name="HasTransparency">The transparent color flag.</param>
/// <param name="DelayHundredths">The delay, in hundredths of a second.</param>
/// <param name="TransparentIndex">The transparent color index.</param>
public sealed record ReferenceGifControl(int DisposalMethod, bool HasTransparency, ushort DelayHundredths, byte TransparentIndex);
