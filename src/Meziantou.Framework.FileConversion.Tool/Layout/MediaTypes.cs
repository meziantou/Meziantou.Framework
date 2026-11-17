namespace Meziantou.Framework.FileConversionTool.Layout;

/// <summary>The media types of the files a laid-out document loads, from their extension.</summary>
internal static class MediaTypes
{
    public static string FromPath(string path)
    {
        var extension = Path.GetExtension(path);
        if (FileFormat.FromExtension(extension) is { Kind: FileFormatKind.Image } format)
            return Imaging.ImageFormatInfo.Get(format.ImageFormat).MimeType;

        return extension.ToUpperInvariant() switch
        {
            ".XHTML" or ".XHT" or ".XML" => "application/xhtml+xml",
            ".HTML" or ".HTM" => "text/html",
            ".CSS" => "text/css",
            ".SVG" => "image/svg+xml",
            ".TTF" => "font/ttf",
            ".OTF" => "font/otf",
            ".WOFF" => "font/woff",
            ".WOFF2" => "font/woff2",
            _ => "application/octet-stream",
        };
    }
}
