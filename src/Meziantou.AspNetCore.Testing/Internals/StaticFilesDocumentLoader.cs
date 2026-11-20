using Meziantou.Framework.WebPlatform.Dom;

namespace Meziantou.AspNetCore.Testing.Internals;

/// <summary>
/// Serves the files of folders for the URLs of an origin. A URL of a folder serves its <c>index.html</c> file. The URLs
/// without a file and without an extension serve the fallback file, when there is one. The other URLs are network errors.
/// </summary>
internal sealed class StaticFilesDocumentLoader(Uri baseUri, IReadOnlyList<KeyValuePair<string, string>> folders, string? fallbackPath = null) : DocumentLoader
{
    public override async ValueTask<DocumentResource?> LoadAsync(DocumentRequest request, CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(request.Url, UriKind.Absolute, out var url) || !string.Equals(url.GetLeftPart(UriPartial.Authority), baseUri.GetLeftPart(UriPartial.Authority), StringComparison.OrdinalIgnoreCase))
            return null;

        // A NUL character cannot be in a file name
        var path = Uri.UnescapeDataString(url.AbsolutePath);
        if (path.Contains('\0', StringComparison.Ordinal))
            return NotFound(request);

        var filePath = FindFile(path);
        if (filePath is null && fallbackPath is not null && !Path.HasExtension(path))
        {
            // The routes of a single-page application (/accounts/42) serve its page, as MapFallbackToFile does
            filePath = FindFile(fallbackPath);
        }

        if (filePath is null)
            return NotFound(request);

        var content = await File.ReadAllBytesAsync(filePath, cancellationToken).ConfigureAwait(false);
        return new DocumentResource(content)
        {
            Url = request.Url,
            Headers = [new("Content-Type", GetContentType(filePath))],
        };
    }

    private string? FindFile(string path)
    {
        foreach (var (prefix, folder) in folders.OrderByDescending(entry => entry.Key.Length))
        {
            var normalizedPrefix = prefix.EndsWith('/', StringComparison.Ordinal) ? prefix : prefix + "/";
            if (!path.StartsWith(normalizedPrefix, StringComparison.Ordinal) && !string.Equals(path + "/", normalizedPrefix, StringComparison.Ordinal))
                continue;

            var relativePath = path.Length > normalizedPrefix.Length ? path[normalizedPrefix.Length..] : "";
            if (relativePath.Length is 0 || relativePath.EndsWith('/', StringComparison.Ordinal))
            {
                relativePath += "index.html";
            }

            // The file must be in the folder: the root ends with a separator, so that a sibling folder whose name starts
            // with the name of the folder (an encoded "../static-secret/" in the URL) does not match
            var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder)) + Path.DirectorySeparatorChar;
            var filePath = Path.GetFullPath(Path.Combine(root, relativePath));
            if (filePath.StartsWith(root, StringComparison.Ordinal) && File.Exists(filePath))
                return filePath;
        }

        return null;
    }

    private static DocumentResource NotFound(DocumentRequest request) => new("") { Url = request.Url, StatusCode = 404, StatusText = "Not Found" };

    private static string GetContentType(string path) => Path.GetExtension(path).ToUpperInvariant() switch
    {
        ".CSS" => "text/css",
        ".JS" or ".MJS" => "text/javascript",
        ".JSON" => "application/json",
        ".HTML" or ".HTM" => "text/html",
        ".SVG" => "image/svg+xml",
        ".PNG" => "image/png",
        ".JPG" or ".JPEG" => "image/jpeg",
        ".GIF" => "image/gif",
        ".WEBP" => "image/webp",
        ".AVIF" => "image/avif",
        ".ICO" => "image/x-icon",
        ".WOFF" => "font/woff",
        ".WOFF2" => "font/woff2",
        ".TTF" => "font/ttf",
        ".OTF" => "font/otf",
        ".WASM" => "application/wasm",
        _ => "application/octet-stream",
    };
}
