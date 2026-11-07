using System.Runtime.InteropServices;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The canvas an APNG frame is drawn on, as seen by the encoder: a <see cref="PngCanvas"/> (or the rows of the previous
/// frame, for an estimate) with an optional rectangle cleared to transparent black by the disposal of the previous frame,
/// or the transparent black canvas of the first frame (no canvas).
/// </summary>
/// <param name="Canvas">The canvas, or <see langword="null"/> for a transparent black canvas.</param>
/// <param name="Cleared">The rectangle that is transparent black whatever the canvas holds; empty for none.</param>
[StructLayout(LayoutKind.Auto)]
internal readonly record struct PngCanvasReference(PngWideRows? Canvas, Rectangle Cleared)
{
    /// <summary>Gets the row of the canvas, or an empty span when the whole row is transparent black.</summary>
    /// <param name="y">The row.</param>
    /// <param name="clearedStart">The first column of the cleared part of the row.</param>
    /// <param name="clearedEnd">The column after the cleared part of the row (equal to <paramref name="clearedStart"/> when nothing is cleared).</param>
    /// <returns>The row of wide pixels, or an empty span.</returns>
    public ReadOnlySpan<byte> GetRow(int y, out int clearedStart, out int clearedEnd)
    {
        if (Canvas is null)
        {
            (clearedStart, clearedEnd) = (0, int.MaxValue);
            return default;
        }

        (clearedStart, clearedEnd) = y >= Cleared.Top && y < Cleared.Bottom ? (Cleared.Left, Cleared.Right) : (0, 0);
        return Canvas.GetRow(y);
    }
}
