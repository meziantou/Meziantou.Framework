// The namespace of this file already imports Meziantou.Framework.Imaging; the README shows the directive a consumer needs
#pragma warning disable IDE0005
// begin-snippet: package-readme-quickstart
using Meziantou.Framework.Imaging;
// end-snippet
#pragma warning restore IDE0005

namespace Meziantou.Framework.Imaging.Samples;

/// <summary>The first quick start of the package README (src/Meziantou.Framework.Imaging/readme.md), compiled verbatim (see <see cref="ReadmeSnippets"/>).</summary>
internal static class PackageReadmeSnippets
{
    /// <summary>Package README quick start.</summary>
    public static void QuickStart()
    {
        // begin-snippet: package-readme-quickstart
        using var image = Image.Load("animation.gif");
        image.Resize(new ResizeOptions(320, 240));
        image.Save("thumbnail.gif");
        // end-snippet
    }
}
