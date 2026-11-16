namespace Meziantou.Framework.FileConversionTool;

internal enum ComicImageFormat
{
    /// <summary>The JPEG, PNG, GIF and WebP images are copied as they are, the other pages are written as PNG.</summary>
    Keep,
    Jpeg,
    Png,
    WebP,
}
