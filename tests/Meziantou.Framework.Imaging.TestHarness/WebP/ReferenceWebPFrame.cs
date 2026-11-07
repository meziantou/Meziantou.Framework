namespace Meziantou.Framework.Imaging.TestHarness.WebP;

/// <summary>
/// An image of a WebP file read by <see cref="ReferenceWebP"/>: the still image (covering the canvas, no duration) or one
/// <c>ANMF</c> frame with its rectangle, duration and flags.
/// </summary>
/// <param name="X">The left offset on the canvas.</param>
/// <param name="Y">The top offset on the canvas.</param>
/// <param name="Width">The frame width.</param>
/// <param name="Height">The frame height.</param>
/// <param name="DurationMilliseconds">The ANMF duration in milliseconds (0 for a still image).</param>
/// <param name="AlphaBlend"><see langword="true"/> when the ANMF blending method is alpha blending (bit clear).</param>
/// <param name="DisposeToBackground"><see langword="true"/> when the ANMF disposal method is "dispose to background".</param>
/// <param name="IsLossless"><see langword="true"/> for a <c>VP8L</c> bitstream, <see langword="false"/> for <c>VP8</c>.</param>
/// <param name="Alpha">The <c>ALPH</c> chunk payload of a lossy image, or <see langword="null"/>.</param>
/// <param name="Bitstream">The <c>VP8</c> or <c>VP8L</c> chunk payload.</param>
public sealed record ReferenceWebPFrame(int X, int Y, int Width, int Height, int DurationMilliseconds, bool AlphaBlend, bool DisposeToBackground, bool IsLossless, ReadOnlyMemory<byte>? Alpha, ReadOnlyMemory<byte> Bitstream);
