namespace Meziantou.Framework.Imaging.Internals;

/// <summary>The layout of <see cref="Rgb24"/>.</summary>
internal readonly struct JpegRgbLayout : IJpegSourceLayout
{
    public static int PixelSize => 3;

    public static bool IsBgr => false;
}
