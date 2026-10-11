// The namespace of this file already imports Meziantou.Framework.Imaging; the README shows the directive a consumer needs
#pragma warning disable IDE0005
// begin-snippet: readme-quickstart
using Meziantou.Framework.Imaging;
using Meziantou.Framework.Imaging.Formats;
// end-snippet
#pragma warning restore IDE0005

namespace Meziantou.Framework.Imaging.Samples;

/// <summary>
/// The second quick start of the package README (src/Meziantou.Framework.Imaging/readme.md), compiled verbatim: every
/// C# block of a published Markdown file must be a snippet of the samples (<c>DocumentationSnippetTests</c>). A snippet
/// made of several regions is their concatenation, separated by a blank line.
/// </summary>
internal static class ReadmeSnippets
{
    /// <summary>Package README quick start with explicit encoders.</summary>
    public static void QuickStart()
    {
        // begin-snippet: readme-quickstart
        using var image = Image.Load("animation.gif");          // every displayed frame, exact timing
        image.Resize(new ResizeOptions(320, 240));               // all frames, atomically
        image.Save("thumbnail.png", new PngEncoder());           // APNG, because the image is animated

        using var frame = image.CloneFrame(0);                    // JPEG stores one frame: export it explicitly
        frame.Save("first.jpg", new JpegEncoder { Quality = 85, BackgroundColor = new Rgba32(255, 255, 255) });
        // end-snippet
    }
}
