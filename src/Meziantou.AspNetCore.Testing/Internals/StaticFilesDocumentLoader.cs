using Meziantou.Framework.WebPlatform.Dom;

namespace Meziantou.AspNetCore.Testing.Internals;

/// <summary>Serves the files of folders for the URLs of an origin. The other URLs are network errors.</summary>
internal sealed class StaticFilesDocumentLoader(Uri baseUri, IReadOnlyList<KeyValuePair<string, string>> folders) : DocumentLoader
{
    public override async ValueTask<DocumentResource?> LoadAsync(DocumentRequest request, CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(request.Url, UriKind.Absolute, out var url) || !string.Equals(url.GetLeftPart(UriPartial.Authority), baseUri.GetLeftPart(UriPartial.Authority), StringComparison.OrdinalIgnoreCase))
            return null;

        // A NUL character cannot be in a file name
        var path = Uri.UnescapeDataString(url.AbsolutePath);
        if (path.Contains('\0', StringComparison.Ordinal))
            return NotFound(request);

        foreach (var (prefix, folder) in folders.OrderByDescending(entry => entry.Key.Length))
        {
            var normalizedPrefix = prefix.EndsWith('/', StringComparison.Ordinal) ? prefix : prefix + "/";
            if (!path.StartsWith(normalizedPrefix, StringComparison.Ordinal))
                continue;

            // The file must be in the folder: the root ends with a separator, so that a sibling folder whose name starts
            // with the name of the folder (an encoded "../static-secret/" in the URL) does not match
            var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder)) + Path.DirectorySeparatorChar;
            var filePath = Path.GetFullPath(Path.Combine(root, path[normalizedPrefix.Length..]));
            if (!filePath.StartsWith(root, StringComparison.Ordinal) || !File.Exists(filePath))
                continue;

            var content = await File.ReadAllBytesAsync(filePath, cancellationToken).ConfigureAwait(false);
            return new DocumentResource(content)
            {
                Url = request.Url,
                Headers = [new("Content-Type", GetContentType(filePath))],
            };
        }

        return NotFound(request);
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
