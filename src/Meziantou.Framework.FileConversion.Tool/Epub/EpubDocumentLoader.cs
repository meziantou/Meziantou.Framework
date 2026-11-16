using System.IO.Compression;
using Meziantou.Framework.FileConversionTool.Layout;
using Meziantou.Framework.WebPlatform.Dom;

namespace Meziantou.Framework.FileConversionTool.Epub;

/// <summary>
/// Serves the files of a book to the browsing context that renders it, from the URLs of <see cref="BaseUrl"/>. Every other
/// URL is a network error: a book does not load anything from the network.
/// </summary>
internal sealed class EpubDocumentLoader(ZipArchive archive, EpubPackage package) : DocumentLoader
{
    public const string BaseUrl = "https://epub.invalid/";

    public static string GetUrl(string path) => BaseUrl + string.Join('/', path.Split('/').Select(Uri.EscapeDataString));

    public override ValueTask<DocumentResource?> LoadAsync(DocumentRequest request, CancellationToken cancellationToken)
    {
        return ValueTask.FromResult(Load(request.Url));
    }

    private DocumentResource? Load(string url)
    {
        if (!url.StartsWith(BaseUrl, StringComparison.Ordinal))
            return null;

        var path = EpubPackage.ResolvePath(basePath: "", url[BaseUrl.Length..]);
        if (path is null)
            return null;

        var entry = archive.GetEntry(path);
        if (entry is null)
            return new DocumentResource("Not found") { StatusCode = 404, Headers = [new("Content-Type", "text/plain")] };

        var data = ZipFiles.ReadEntry(entry);
        var mediaType = package.GetItemByPath(path)?.MediaType is { Length: > 0 } manifestMediaType ? manifestMediaType : MediaTypes.FromPath(path);
        return new DocumentResource(data) { Headers = [new("Content-Type", mediaType)] };
    }
}
