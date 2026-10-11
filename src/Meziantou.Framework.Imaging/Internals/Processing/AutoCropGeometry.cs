using System.Runtime.InteropServices;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The rectangle an auto-crop keeps, in the coordinates of the source canvas: the content box, padded, shifted by the
/// weights and, for <see cref="AutoCropPaddingMode.Contain"/>, clamped to the canvas. It may reach outside the canvas for
/// <see cref="AutoCropPaddingMode.Expand"/>, hence the 64-bit coordinates: nothing overflows whatever the padding.
/// </summary>
/// <param name="X">The left coordinate.</param>
/// <param name="Y">The top coordinate.</param>
/// <param name="Width">The width; always positive.</param>
/// <param name="Height">The height; always positive.</param>
[StructLayout(LayoutKind.Auto)]
internal readonly record struct AutoCropGeometry(long X, long Y, long Width, long Height)
{
    /// <summary>Computes the rectangle kept when applying <paramref name="analysis"/> to a <paramref name="canvas"/> of the same size.</summary>
    /// <remarks>
    /// The shift is <c>padding * weight</c> truncated toward zero. As a weight is between -1 and 1, the rectangle always
    /// contains the content box.
    /// </remarks>
    public static AutoCropGeometry Compute(Size canvas, AutoCropAnalysis analysis, AutoCropOptions options)
    {
        var bounds = analysis.Bounds;
        var x = bounds.X - (long)options.PaddingX + (long)(options.PaddingX * analysis.WeightX);
        var y = bounds.Y - (long)options.PaddingY + (long)(options.PaddingY * analysis.WeightY);
        var right = x + bounds.Width + (2L * options.PaddingX);
        var bottom = y + bounds.Height + (2L * options.PaddingY);
        if (options.PaddingMode == AutoCropPaddingMode.Contain)
        {
            x = Math.Max(x, 0);
            y = Math.Max(y, 0);
            right = Math.Min(right, canvas.Width);
            bottom = Math.Min(bottom, canvas.Height);
        }

        return new(x, y, right - x, bottom - y);
    }

    /// <summary>Gets a value indicating whether the rectangle is exactly the canvas.</summary>
    public bool IsIdentity(Size canvas) => X == 0 && Y == 0 && Width == canvas.Width && Height == canvas.Height;

    /// <summary>Gets a value indicating whether the rectangle is entirely inside the canvas: a plain crop.</summary>
    public bool IsInside(Size canvas) => X >= 0 && Y >= 0 && X + Width <= canvas.Width && Y + Height <= canvas.Height;
}
