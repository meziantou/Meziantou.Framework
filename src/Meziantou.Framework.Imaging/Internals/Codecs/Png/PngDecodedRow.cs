using System.Runtime.InteropServices;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>One reconstructed row of a PNG image: <see cref="Width"/> source pixels placed at <c>x = X + i * Step</c> of row <see cref="Y"/>.</summary>
[StructLayout(LayoutKind.Auto)]
internal readonly ref struct PngDecodedRow
{
    public PngDecodedRow(int y, int x, int step, int width, ReadOnlySpan<byte> pixels)
    {
        Y = y;
        X = x;
        Step = step;
        Width = width;
        Pixels = pixels;
    }

    /// <summary>Gets the row index in the image.</summary>
    public int Y { get; }

    /// <summary>Gets the column of the first pixel.</summary>
    public int X { get; }

    /// <summary>Gets the distance between two pixels (1 when not interlaced).</summary>
    public int Step { get; }

    /// <summary>Gets the number of pixels.</summary>
    public int Width { get; }

    /// <summary>Gets the pixels in the source layout of the <see cref="PngSampleFormat"/>.</summary>
    public ReadOnlySpan<byte> Pixels { get; }
}
