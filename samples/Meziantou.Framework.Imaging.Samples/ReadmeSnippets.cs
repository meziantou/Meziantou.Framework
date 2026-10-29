using Meziantou.Framework.Imaging.Formats;

namespace Meziantou.Framework.Imaging.Samples;

/// <summary>
/// The second quick start of the package README (src/Meziantou.Framework.Imaging/readme.md), compiled verbatim: every
/// C# block of the README is generated from a region of the samples by Meziantou.FileReferencer
/// (<c>DocumentationSnippetTests</c>).
/// </summary>
internal static class ReadmeSnippets
{
    /// <summary>Package README quick start with explicit encoders.</summary>
    public static void QuickStart()
    {
        #region Imaging_QuickStartWithEncoders
        using var image = Image.Load("animation.gif");          // every displayed frame, exact timing
        image.Resize(new ResizeOptions(320, 240));               // all frames, atomically
        image.Save("thumbnail.png", new PngEncoder());           // APNG, because the image is animated

        image.Frames[0].Save("first.jpg",                         // JPEG stores one frame: export it explicitly
            new JpegEncoder { Quality = 85, BackgroundColor = new Rgba32(255, 255, 255) });
        #endregion
    }
}
