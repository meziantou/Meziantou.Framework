namespace Meziantou.Framework.Imaging.TestHarness.Drawing;

/// <summary>The samples of a pixel, in storage order, as the drawing reference needs them.</summary>
/// <param name="ColorChannels">The number of color samples: 1 for grayscale, 0 for an alpha mask, 3 otherwise.</param>
/// <param name="HasAlpha">Whether an alpha sample follows the color samples.</param>
/// <param name="MaxValue">The largest sample value (255 or 65535).</param>
/// <param name="BlueFirst">Whether the color samples are stored blue, green, red.</param>
public sealed record ReferencePixelLayout(int ColorChannels, bool HasAlpha, int MaxValue, bool BlueFirst)
{
    /// <summary>Gets the number of samples per pixel.</summary>
    public int ChannelCount => ColorChannels + (HasAlpha ? 1 : 0);

    /// <summary>Gets the layout of a library pixel format.</summary>
    /// <param name="format">The pixel format.</param>
    /// <returns>The layout.</returns>
    public static ReferencePixelLayout FromPixelFormat(PixelFormat format) => format switch
    {
        PixelFormat.Rgba32 => new ReferencePixelLayout(3, HasAlpha: true, 255, BlueFirst: false),
        PixelFormat.Bgra32 => new ReferencePixelLayout(3, HasAlpha: true, 255, BlueFirst: true),
        PixelFormat.Rgb24 => new ReferencePixelLayout(3, HasAlpha: false, 255, BlueFirst: false),
        PixelFormat.Rgba64 => new ReferencePixelLayout(3, HasAlpha: true, 65535, BlueFirst: false),
        PixelFormat.Gray8 => new ReferencePixelLayout(1, HasAlpha: false, 255, BlueFirst: false),
        PixelFormat.Gray16 => new ReferencePixelLayout(1, HasAlpha: false, 65535, BlueFirst: false),
        PixelFormat.La16 => new ReferencePixelLayout(1, HasAlpha: true, 255, BlueFirst: false),
        PixelFormat.La32 => new ReferencePixelLayout(1, HasAlpha: true, 65535, BlueFirst: false),
        PixelFormat.Rgb48 => new ReferencePixelLayout(3, HasAlpha: false, 65535, BlueFirst: false),
        PixelFormat.A8 => new ReferencePixelLayout(0, HasAlpha: true, 255, BlueFirst: false),
        _ => throw new ArgumentOutOfRangeException(nameof(format), format, "Unsupported pixel format."),
    };
}
