namespace Meziantou.Framework.Imaging.Internals;

/// <summary>The layout of <see cref="Bgra32"/>.</summary>
internal readonly struct JpegBgraLayout : IJpegSourceLayout
{
    public static int PixelSize => 4;

    public static bool IsBgr => true;
}
