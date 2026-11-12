namespace Meziantou.Framework.WebPlatform.DevTools.Targets;

/// <summary>
/// The media types of the files selected by path (<c>DOM.setFileInputFiles</c>), from their extension, as the browsers do for
/// the common types (an unknown extension is an empty type).
/// </summary>
internal static class FileMediaTypes
{
    private static readonly Dictionary<string, string> MediaTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        [".avif"] = "image/avif",
        [".bmp"] = "image/bmp",
        [".css"] = "text/css",
        [".csv"] = "text/csv",
        [".gif"] = "image/gif",
        [".gz"] = "application/gzip",
        [".htm"] = "text/html",
        [".html"] = "text/html",
        [".ico"] = "image/vnd.microsoft.icon",
        [".jpeg"] = "image/jpeg",
        [".jpg"] = "image/jpeg",
        [".js"] = "text/javascript",
        [".json"] = "application/json",
        [".md"] = "text/markdown",
        [".mjs"] = "text/javascript",
        [".mp3"] = "audio/mpeg",
        [".mp4"] = "video/mp4",
        [".oga"] = "audio/ogg",
        [".ogg"] = "audio/ogg",
        [".ogv"] = "video/ogg",
        [".pdf"] = "application/pdf",
        [".png"] = "image/png",
        [".svg"] = "image/svg+xml",
        [".tif"] = "image/tiff",
        [".tiff"] = "image/tiff",
        [".txt"] = "text/plain",
        [".wasm"] = "application/wasm",
        [".wav"] = "audio/wav",
        [".weba"] = "audio/webm",
        [".webm"] = "video/webm",
        [".webp"] = "image/webp",
        [".woff"] = "font/woff",
        [".woff2"] = "font/woff2",
        [".xml"] = "text/xml",
        [".zip"] = "application/zip",
    };

    public static string GetMediaType(string fileName) => MediaTypes.GetValueOrDefault(Path.GetExtension(fileName), "");
}
