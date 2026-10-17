using System.Runtime.InteropServices;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>The control data and bitstream description of one WebP image: the still image, or one <c>ANMF</c> frame.</summary>
/// <param name="X">The left edge on the canvas (twice the encoded <c>Frame X</c>; 0 for a still image).</param>
/// <param name="Y">The top edge on the canvas.</param>
/// <param name="Width">The frame width (equal to the bitstream width).</param>
/// <param name="Height">The frame height.</param>
/// <param name="DurationMilliseconds">The encoded 24-bit duration (0 for a still image).</param>
/// <param name="AlphaBlend">The blending method: <see langword="true"/> for alpha blending (encoded 0), <see langword="false"/> for "do not blend".</param>
/// <param name="DisposeToBackground">The disposal method: <see langword="true"/> for "dispose to the background color" (encoded 1).</param>
/// <param name="IsLossless">Whether the bitstream is VP8L (otherwise VP8).</param>
/// <param name="HasAlpha">Whether the frame carries alpha: an <c>ALPH</c> chunk with a VP8 bitstream, or the VP8L <c>alpha_is_used</c> hint.</param>
[StructLayout(LayoutKind.Auto)]
internal readonly record struct WebPFrameInfo(int X, int Y, int Width, int Height, int DurationMilliseconds, bool AlphaBlend, bool DisposeToBackground, bool IsLossless, bool HasAlpha)
{
    /// <summary>Gets the frame rectangle on the canvas.</summary>
    public Rectangle Bounds => new(X, Y, Width, Height);
}
