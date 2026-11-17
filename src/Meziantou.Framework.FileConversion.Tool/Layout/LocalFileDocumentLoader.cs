using Meziantou.Framework.WebPlatform.Dom;

namespace Meziantou.Framework.FileConversionTool.Layout;

/// <summary>
/// Serves a document and the local files it loads (images, style sheets, fonts) to the browsing context that lays it out,
/// from the URLs of <see cref="BaseUrl"/>. Only the files of the root folder and its subfolders are served, after resolving
/// the links of their paths: a link to a file or a folder outside of the root folder is not followed. Every other URL, such
/// as a URL of the network, is a network error.
/// </summary>
/// <param name="root">The folder whose files the document can load.</param>
/// <param name="documentPath">The path of the document, in <paramref name="root"/>.</param>
/// <param name="documentContent">The content served for the document, such as the HTML of a Markdown file.</param>
/// <param name="documentMediaType">The media type of the document: <c>text/html</c> lets the parser detect the encoding of the content.</param>
internal sealed class LocalFileDocumentLoader(FullPath root, FullPath documentPath, byte[] documentContent, string documentMediaType) : DocumentLoader
{
    public const string BaseUrl = "https://local.invalid/";

    // The resources of a document are images, style sheets and fonts: a larger file is not loaded
    private const long MaxFileLength = 256L * 1024 * 1024;

    private readonly FullPath _resolvedRoot = FileLinks.GetComparisonKey(root);

    /// <summary>Gets the URL of a file of the root folder.</summary>
    public string GetUrl(FullPath path) => BaseUrl + string.Join('/', path.MakePathRelativeTo(root).Replace('\\', '/').Split('/').Select(Uri.EscapeDataString));

    public override ValueTask<DocumentResource?> LoadAsync(DocumentRequest request, CancellationToken cancellationToken)
    {
        return ValueTask.FromResult(Load(request.Url));
    }

    /// <summary>Gets the path of the file of a URL, when it is a URL of <see cref="BaseUrl"/> of a file of the root folder.</summary>
    internal static bool TryResolvePath(FullPath root, string url, out FullPath path)
    {
        path = default;
        if (!url.StartsWith(BaseUrl, StringComparison.Ordinal))
            return false;

        var relative = url.AsSpan(BaseUrl.Length);
        var end = relative.IndexOfAny('?', '#');
        if (end >= 0)
        {
            relative = relative[..end];
        }

        var segments = new List<string>();
        foreach (var range in relative.Split('/'))
        {
            string segment;
            try
            {
                segment = Uri.UnescapeDataString(relative[range]);
            }
            catch (UriFormatException)
            {
                return false;
            }

            // A segment is a name: no separator, no drive, no stream, no control character
            if (segment.AsSpan().IndexOfAny("/\\:") >= 0 || segment.Any(char.IsControl))
                return false;

            if (segment is "" or ".")
                continue;

            if (segment is "..")
            {
                if (segments.Count is 0)
                    return false;

                segments.RemoveAt(segments.Count - 1);
                continue;
            }

            segments.Add(segment);
        }

        if (segments.Count is 0)
            return false;

        var result = FullPath.Combine([root.Value, .. segments]);
        if (!result.IsChildOf(root))
            return false;

        path = result;
        return true;
    }

    private DocumentResource? Load(string url)
    {
        if (!TryResolvePath(root, url, out var path))
            return null;

        if (path == documentPath)
            return new DocumentResource(documentContent) { Headers = [new("Content-Type", documentMediaType)] };

        // A link of the path can point outside of the root folder: the file is read from its resolved path
        var resolvedPath = FileLinks.Resolve(path);
        if (!FileLinks.GetComparisonKey(resolvedPath).IsChildOf(_resolvedRoot))
            return null;

        var file = new FileInfo(resolvedPath);
        if (!file.Exists)
            return new DocumentResource("Not found") { StatusCode = 404, Headers = [new("Content-Type", "text/plain")] };

        if (file.Length > MaxFileLength)
            return null;

        byte[] data;
        try
        {
            data = File.ReadAllBytes(resolvedPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }

        return new DocumentResource(data) { Headers = [new("Content-Type", MediaTypes.FromPath(path))] };
    }
}
