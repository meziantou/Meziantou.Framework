namespace Meziantou.Framework.Imaging.TestHarness.WebP;

/// <summary>The <c>ANIM</c> chunk of an animated WebP file read by <see cref="ReferenceWebP"/>.</summary>
/// <param name="BackgroundColor">The background color as stored: blue, green, red, alpha bytes in a little-endian 32-bit value.</param>
/// <param name="LoopCount">The loop count (the number of plays; 0 is infinite).</param>
public sealed record ReferenceWebPAnimation(uint BackgroundColor, int LoopCount);
