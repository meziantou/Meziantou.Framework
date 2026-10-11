namespace Meziantou.Framework.Imaging.TestHarness.ExternalTools;

/// <summary>An extra channel of a JPEG XL image, as <c>jxlinfo -v</c> describes it.</summary>
/// <param name="Type">The type as printed (<c>Alpha</c>, <c>Depth</c>, <c>Spotcolor</c>, <c>Black</c>, <c>Optional</c>, ...).</param>
/// <param name="BitsPerSample">The bit depth of the channel.</param>
/// <param name="IsAlphaPremultiplied">For an alpha channel, whether the color channels are premultiplied; otherwise <see langword="null"/>.</param>
public sealed record JxlInfoExtraChannel(string Type, int BitsPerSample, bool? IsAlphaPremultiplied);
