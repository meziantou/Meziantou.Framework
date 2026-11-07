namespace Meziantou.Framework.Imaging.Internals;

/// <summary>The layout of <see cref="Rgba32"/>.</summary>
internal readonly struct JpegRgbaLayout : IJpegSourceLayout
{
    public static int PixelSize => 4;

    public static bool IsBgr => false;
}
