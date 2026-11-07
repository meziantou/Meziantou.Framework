namespace Meziantou.Framework.Imaging.TestHarness.Drawing;

/// <summary>
/// A paint color in the layout of a pixel format: the color samples in storage order and the opacity, at the precision
/// of the format. The opacity exists for every format (a format without alpha is painted with translucent colors too).
/// </summary>
/// <param name="Color">The color samples, in storage order.</param>
/// <param name="Alpha">The opacity, from 0 to the largest sample value.</param>
public sealed record ReferencePaint(IReadOnlyList<int> Color, int Alpha)
{
    /// <summary>
    /// Converts a 16-bit straight-alpha color with the documented pixel conversion rules, written here from their
    /// definition: Rec. 709 luma <c>(2126 R + 7152 G + 722 B + 5000) / 10000</c> at 16 bits for grayscale formats, then
    /// <c>(v * 255 + 32767) / 65535</c> for 8-bit formats.
    /// </summary>
    /// <param name="red">The red sample (16 bits).</param>
    /// <param name="green">The green sample (16 bits).</param>
    /// <param name="blue">The blue sample (16 bits).</param>
    /// <param name="alpha">The alpha sample (16 bits).</param>
    /// <param name="layout">The destination layout.</param>
    /// <returns>The paint.</returns>
    public static ReferencePaint Convert(int red, int green, int blue, int alpha, ReferencePixelLayout layout)
    {
        ArgumentNullException.ThrowIfNull(layout);
        int Narrow(long value) => layout.MaxValue == 65535 ? (int)value : (int)(((value * 255) + 32767) / 65535);
        int[] color;
        if (layout.ColorChannels == 1)
        {
            color = [Narrow(((2126L * red) + (7152L * green) + (722L * blue) + 5000) / 10000)];
        }
        else if (layout.BlueFirst)
        {
            color = [Narrow(blue), Narrow(green), Narrow(red)];
        }
        else
        {
            color = [Narrow(red), Narrow(green), Narrow(blue)];
        }

        return new ReferencePaint(color, Narrow(alpha));
    }
}
