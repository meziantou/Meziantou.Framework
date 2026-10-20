namespace Meziantou.Framework.Imaging.Samples;

/// <summary>The first quick start of the package README (src/Meziantou.Framework.Imaging/readme.md), compiled verbatim (see <see cref="ReadmeSnippets"/>).</summary>
internal static class PackageReadmeSnippets
{
    /// <summary>Package README quick start.</summary>
    public static void QuickStart()
    {
        #region Imaging_QuickStart
        using var image = Image.Load("animation.gif");
        image.Resize(new ResizeOptions(320, 240));
        image.Save("thumbnail.gif");
        #endregion
    }
}
